---
title: System Pipeline
category: Architecture
index: 3
---

# System Pipeline (phases + snapshot boundary)

When `update` grows, the hardest part is maintaining a clear mental model of:

- which subsystems are allowed to **mutate** the world
- which subsystems are **readonly/query**
- and where you want explicit “barriers” between them

`Mibo.Elmish.System` is a small pipeline helper that gives you:

- a natural _phase_ style
- a **type-enforced snapshot boundary**
- a single accumulated `Cmd<'Msg>` (no lists, no reversing)

## The idea

You run mutation-heavy phases first, then take a snapshot (often a smaller readonly view), then run readonly phases.

```fsharp
| Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    System.start model
    |> System.pipeMutable (Physics.update dt)
    |> System.pipeMutable (Particles.update dt)
    |> System.snapshot Model.toSnapshot
    |> System.pipe (Ai.decide dt)
    |> System.finish Model.fromSnapshot
```

### What a “system” looks like

A system is just a function that returns an updated state and a `Cmd`:

```fsharp
let physics (m: Model) : struct (Model * Cmd<Msg>) =
  // mutate-ish logic (still functional at the boundary)
  struct ({ m with ... }, Cmd.none)
```

## Why the snapshot boundary matters

The key is the type change:

- before snapshot: `'Model`
- after snapshot: `'Snapshot`

That means you can’t accidentally call a “mutable phase” after you’ve committed to readonly.

## When to use this (and when not)

Use it when:

- you have many continuous subsystems (physics, movement, particles, animation)
- you want predictable per-tick ordering
- you’re heading toward ARPG/RTS complexity

Skip it when:

- your game is small and `update` is still easy to read
- you’re mostly event-driven (menus, turn-based)

See also: [Scaling Mibo (Simple → Complex)](scaling.html) (how this fits into the ladder).
