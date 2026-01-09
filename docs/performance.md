---
title: F# For Perf
category: Architecture
index: 5
---

# F# For Perf (Patterns for Games)

F# is a high-level functional language, but games operate under strict latency constraints. The Garbage Collector (GC) is your main adversary here: creating too much short-lived "trash" per frame forces the GC to pause your game to clean it up, causing stutter.

This guide outlines an incremental path to optimization. It serves as the performance implementation guide for the [Scaling Mibo](scaling.html) architectural levels. While the scaling guide helps you manage **complexity**, this guide helps you manage **throughput and CPU/GC pressure**.

**Don't premature optimize.** Write idiomatic code first, then apply these patterns to your "hot paths" (code that runs thousands of times per frame).

## Level 0 — Default to Idiomatic F#

For your game state, high-level logic, UI, and configuration, you should just write normal F#.

Immutable records and lists are excellent for correctness. They prevent bugs, make state management trivial, and are easy to refactor. If you have 50 enemies and you allocate 50 new record objects per frame, the .NET GC won't even blink. It is extremely optimized for "gen 0" collections.

**When to stay here:**
Almost always. Until your profiler says otherwise, this is the most productive place to be.

```fsharp
type Enemy = { Pos: Vector2; Health: int }
type Model = { Enemies: Enemy list }

// This allocates a new list node for every enemy, every frame.
// For small N, this is perfectly fine.
let updateEnemies dt enemies =
    enemies |> List.map (fun e -> { e with Pos = e.Pos + Vector2(1f, 0f) * dt })
```

## Level 1 — Structs for Small Data

Classes (normal F# types) live on the heap. Every time you create one, it adds pressure to the GC. Structs, however, are value types—they live on the stack or are embedded directly inside arrays.

If you have a small type that is created frequently (like a custom 2D vector, a grid coordinate, or a game message), marking it as `[<Struct>]` makes it free to allocate.

**Guideline:**
Use `[<Struct>]` for immutable types smaller than 16-24 bytes (e.g., 2-4 fields like `int` or `float32`).

```fsharp
[<Struct>]
type GridPos = { X: int; Y: int }

[<Struct>]
type Msg =
    | Damage of amount: int
    | Heal of amount: int
```

## Level 2 — Value Tuples and Returns

Standard F# tuples `(a, b)` are actually generic objects allocated on the heap. In a tight loop (like iterating over 10,000 particles), returning a standard tuple from a function will allocate 10,000 objects every single frame.

F# supports **struct tuples** `struct (a, b)` which are value types and incur zero allocation.

**Guideline:**
If a function is called inside a "hot loop" (e.g., physics integration for every entity), prefer returning struct tuples.

```fsharp
// BAD for hot paths: Allocates a Tuple object every call
let calculateVelocity pos target =
    let dir = Vector2.Normalize(target - pos)
    (dir, dir.Length())

// GOOD: Zero allocation
let calculateVelocityStruct pos target =
    let dir = Vector2.Normalize(target - pos)
    struct (dir, dir.Length())
```

## Level 3 — Mutable Collections

F# `List` is a linked list. It is great for pattern matching, but terrible for CPU cache locality (pointer chasing). Transforming it (`List.map`) allocates a fresh list every time.

For subsystems that process thousands of items (particles, projectiles, debris), you should switch to contiguous memory. `ResizeArray` (the F# alias for `System.Collections.Generic.List<T>`) or standard arrays `[]` are cache-friendly and support in-place mutation.

**Guideline:**
Hide the mutation inside the subsystem. Your main game update can still look pure, even if it internally calls a function that mutates a pre-allocated array.

```fsharp
type Model = {
    // Mutable container, treated as read-only by most of the game
    Particles: ResizeArray<Particle>
}

let updateParticles dt (particles: ResizeArray<Particle>) =
    // In-place mutation avoids allocating 10,000 new objects
    let count = particles.Count
    let mutable i = 0
    while i < count do
        let mutable p = particles.[i]
        p.Life <- p.Life - dt
        // Update the struct in the array
        particles.[i] <- p
        i <- i + 1
    particles
```

## Level 4 — Buffer Pooling

Sometimes you need a temporary array for a single frame—for example, to gather potential collision pairs or process a batch of AI requests. Allocating `Array.zeroCreate` every frame creates a massive amount of garbage.

Instead, use `System.Buffers.ArrayPool`. This lets you "rent" an array and return it when you are done.

**Guideline:**
Only use this for large, frequent temporary buffers. Always use a `try...finally` block to ensure you return the array, or you will leak memory.

```fsharp
open System.Buffers

let findCollisions (entities: ResizeArray<Entity>) =
    // Rent a buffer to store potential collision pairs
    // We assume max possible pairs is count * 2 for this broadphase
    let buffer = ArrayPool<int>.Shared.Rent(entities.Count * 2)
    let results = ResizeArray<int * int>()

    try
        let mutable pairCount = 0
        // ... fill buffer with indices of colliding entities ...

        // Process the results using the buffer (no new allocations for the buffer itself)
        for i = 0 to pairCount - 1 do
            let idx = buffer.[i]
            results.Add((idx, idx + 1))

        results
    finally
        // Important: Return the rented buffer to the pool!
        ArrayPool<int>.Shared.Return(buffer)
```

## Level 5 — ByRef, InRef, Span, and Memory

For physics engines, collisions, and matrix math, copying large structs (like a 64-byte `Matrix` or a 24-byte `BoundingBox`) can become a bottleneck. F# provides low-level tools to avoid these copies.

### The Low-Level Pointers

- `inref<'T>`: A read-only pointer (conceptually like C++ `const T&`).
- `byref<'T>`: A mutable pointer.

### The Views

- `Span<'T>` / `ReadOnlySpan<'T>`: A view into contiguous memory (array, stack, or native heap). **Stack-only:** cannot be stored in fields or used in async methods.

**Guideline:**
Use `Span` for synchronous processing (update loops). Use `inref`/`byref` for passing large structs to functions without copying.

```fsharp
// 1. INREF: Read huge structs without copying them
// Essential for collision detection between complex meshes
let inline intersects (boxA: inref<BoundingBox>) (boxB: inref<BoundingBox>) =
    // Access fields directly via the pointer.
    // 'inref' prevents accidental modification of boxA/boxB.
    if boxA.Max.X < boxB.Min.X || boxA.Min.X > boxB.Max.X then false
    else true

// 2. BYREF: Modifying a struct in-place (Physics Step)
// We pass the position by reference so we can modify the original value, not a copy.
let inline integrate (pos: byref<Vector2>) (vel: Vector2) (dt: float32) =
    pos.X <- pos.X + vel.X * dt
    pos.Y <- pos.Y + vel.Y * dt

// 3. SPAN: Processing a slice without allocation
// Sum health of only the first 10 entities
let sumHealth (entities: ReadOnlySpan<Entity>) =
    let mutable total = 0
    for i = 0 to entities.Length - 1 do
        total <- total + entities.[i].Health
    total
```
