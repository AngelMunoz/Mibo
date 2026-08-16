---
title: Scaling Mibo
category: Architecture
categoryindex: 1
index: 1
---

# Scaling Mibo (Simple → Complex)

Mibo is designed to stay fun for small games while still giving you an upgrade path for "serious" games.
This document is a practical ladder you can climb as complexity increases—without rewriting your engine.

> **NOTE:** this ladder climbs within the **MVU** runtime. Past Level 3 — when derived state recomputed by hand and the model re-read per frame stop being free — the next rung is a change of runtime, not a level: the [Adaptive architecture](adaptive/overview.html), where a derived state graph replaces the recomputation. The [Defli](https://github.com/AngelMunoz/Mibo.Samples) samples show the full shape.

The recurring theme is:

- keep **state changes** serialized (Elmish)
- keep expensive logic **data-oriented** (snapshots + mutable hot paths when needed)
- introduce **explicit boundaries** (per-tick phases, and optionally frame-bounded dispatch)

## Level 0 — Pure MVU

**Best for:** card games, menus, puzzle games.

**Goal:** maximum simplicity.

**Model**: mostly immutable records.

**Update discipline:** handle one message at a time, return `Cmd.none` most of the time.

**Mibo helpers you'll use:**

- `Program.mkProgram`, `Program.withRenderer`, `Program.withSubscription`
- `Cmd.ofMsg`, `Cmd.batch`

**What you gain:**

- trivially testable logic
- deterministic replay (record the message stream)

```fsharp
type Model = { Position: System.Numerics.Vector2 }
type Msg = Teleport of System.Numerics.Vector2

let update msg model =
    match msg with
    | Teleport pos -> { model with Position = pos }, Cmd.none

let view ctx model (buffer: RenderBuffer2D) =
    // Deferred sprite draw via the fluent DSL (same shape on every backend)
    buffer
      .sprite(SpriteState.create(texture, Rectangle(model.Position.X, model.Position.Y, 32f, 32f), Rectangle(0f, 0f, 32f, 32f)))
      .drop()
```

## Level 1 — Add semantic input

**Best for:** Action-heavy games (platformers, arcade) where rebindable keys and state queries (is "Jump" held?) are essential.

**Goal:** stop sprinkling device-specific checks across gameplay.

**Pattern:** map hardware input → semantic actions → update your model.

**Mibo helpers you'll use:**

- `InputMap` + `InputMapper.subscribe`
- model field like `Actions: ActionState<_>` updated by an `InputMapped` message

**Recommendation:** treat input as _data for the next simulation step_.

That usually looks like:

- `InputMapped actions` updates a field (`model.Actions <- actions`)
- `Tick gt` consumes `model.Actions` to advance simulation

```fsharp
type Action = MoveLeft | MoveRight | Jump

let inputMap =
    InputMap.empty
    |> InputMap.key MoveLeft KeyCode.Left
    |> InputMap.key MoveRight KeyCode.Right
    |> InputMap.key Jump KeyCode.Space

type Model = {
    Position: System.Numerics.Vector2
    Actions: ActionState<Action>
}

let update msg model =
    match msg with
    | InputMapped actions ->
        { model with Actions = actions }, Cmd.none

    | Tick gt ->
        let dt = float32 gt.ElapsedGameTime.TotalSeconds
        let dx =
            if model.Actions.Held.Contains MoveRight then 100.0f * dt
            elif model.Actions.Held.Contains MoveLeft then -100.0f * dt
            else 0.0f

        { model with Position = model.Position + System.Numerics.Vector2(dx, 0.0f) }, Cmd.none
```

## Level 2 — Establish a simulation "transaction"

**Best for:** Growing projects where you need to prevent "spaghetti logic." By forcing all gameplay changes into `Tick`, you avoid race conditions caused by random events mutating state unpredictably.

**Goal:** keep your mental model simple when the game grows.

**Rule of thumb:**

> Non-`Tick` messages update _buffers_ (input snapshots, event queues, pending requests). Only `Tick` mutates the "world".

This gives you an explicit boundary:

- gather external events during the frame
- run simulation once on `Tick`
- commit results

**Why it helps:**

- fewer ordering surprises
- easier to reason about "what changed this frame"
- makes later deterministic/multiplayer work much easier

```fsharp
type Msg =
    | InputMapped of ActionState<Action> // Just updates the input buffer
    | NetworkPacket of byte[]            // Just updates the network buffer
    | Tick of Mibo.Elmish.GameTime                  // The ONLY place physics/gameplay runs

let update msg model =
    match msg with
    | InputMapped actions ->
        { model with Actions = actions }, Cmd.none

    | NetworkPacket data ->
        // Buffering network data, not processing it yet
        model.NetworkBuffer.Enqueue(data)
        model, Cmd.none

    | Tick gt ->
        // 1. Read buffers (Input, Network)
        // 2. Run simulation (Physics, AI)
        // 3. Update world
        let newPos = Physics.integrate model.Position model.Actions gt
        { model with Position = newPos }, Cmd.none
```

## Level 2.5 — Performance optimization

**Best for:** Games with frequent message dispatch or large Models where measurable GC pressure appears.

**Goal:** Reduce GC allocations while keeping the Elmish architecture.

### Struct messages

Messages are dispatched frequently throughout your game. Each message allocation adds pressure to the GC. For small messages, marking them as `[<Struct>]` eliminates heap allocation entirely.

**Simple guideline:**

- **Small messages** (1-2 simple fields like `int`, `float32`, `Vector2`): Use `[<Struct>]`
- **Large messages** (arrays, large structs, many fields): Keep as reference types

**Profile-driven:** Use a profiler to identify what's "small" vs "large" in your context. What works for one game may differ for another.

```fsharp
[<Struct>]
type Message =
    | Tick of Mibo.Elmish.GameTime              // Small - struct candidate
    | Damage of int                  // Small - struct candidate
    | ChildMsg of Child.Msg          // Works fine with struct
```

Struct messages work seamlessly with `Cmd.map` and `Sub.map`—they just wrap the dispatch function with the mapping function.

### Mutable Model for large state

When your Model grows large (10+ properties or contains substantial nested state), immutable updates allocate new objects each update cycle. While you might be tempted to make the Model a struct to avoid GC, this doesn't help: the runtime passes the Model by value, so a large struct would be copied every update.

Instead, use a reference type (class) with mutable members. This pattern avoids GC pressure while maintaining the Elmish contract—you still return `Model * Cmd<'Msg>` from your update function. The runtime simply re-assigns the state variable.

```fsharp
type Model() =
    // Top-level state fields (10+ properties in production games)
    member val Time = Mibo.Elmish.GameTime.Zero with get, set
    member val PlayerId = 0 with get, set
    member val PlayerPosition = System.Numerics.Vector2.Zero with get, set
    member val PlayerVelocity = System.Numerics.Vector2.Zero with get, set
    member val Actions = ActionState.empty with get, set
    // ... more large fields

    // Child subsystem state (initialized in init)
    member val ChildState = Unchecked.defaultof<_> with get, set

[<Struct>]
type Message =
    | Tick of Mibo.Elmish.GameTime
    | ChildMsg of Child.Msg

let update msg model =
    match msg with
    | Tick gt ->
        model.Time <- gt
        model, Cmd.none
    | ChildMsg msg ->
        let childState, childCmd = Child.update msg model.ChildState
        model.ChildState <- childState
        model, Cmd.map ChildMsg childCmd
```

**Tradeoffs:**

- Pros: Zero GC pressure from Model updates
- Cons: Less pure than immutable records, more potential for bugs if you mutate unexpectedly

### Hybrid approach: gradual mutability

You don't need to go all-in on mutability. Many games work well with a hybrid approach:

- **Small models** (child subsystems, simple components): Keep as immutable structs
- **Large models** (root state, complex subsystems): Use mutable reference types

This lets you apply the right tool at the right level. A `Player` component with 3-4 fields works great as an immutable struct, while the root `Model` with 10+ fields benefits from mutability.

The key is that mutability stays **encapsulated** and **predictable**:

```fsharp
// Small, immutable child model (struct)
[<Struct>]
type Player = {
    Position: System.Numerics.Vector2
    Velocity: System.Numerics.Vector2
    Health: int
}

// Large, mutable parent model (class)
type GameModel() =
    member val Time = Mibo.Elmish.GameTime.Zero with get, set
    member val Player: Player = { Position = System.Numerics.Vector2.Zero; Velocity = System.Numerics.Vector2.Zero; Health = 100 } with get, set
    // ... 10+ more large fields

// Update still returns the same Model instance
let update msg model =
    match msg with
    | Tick gt ->
        model.Time <- gt
        // Player can be updated immutably when needed
        model.Player <- { model.Player with Position = model.Player.Position + System.Numerics.Vector2(1f, 0f) }
        model, Cmd.none
```

The Elmish contract is preserved: your `update` function still returns `Model * Cmd<'Msg>`. The mutability is an internal implementation detail that doesn't leak into your architecture. You get zero GC pressure where it matters most, without sacrificing the benefits of the functional model elsewhere.

### When to apply these patterns

Profile first, optimize second. Start with idiomatic code and apply these patterns only when measurements show a need. Struct messages and mutable Models complement each other—both reduce allocation but at different points in the update cycle.

## Level 3 — Phase pipelines + snapshot barriers

**Best for:** Complex simulations (ARPG, RTS) where update order matters. E.g., Physics must run before Collision, which must run before AI.

**Goal:** support many subsystems without turning update into spaghetti.

Mibo provides a type-guided pipeline in `Mibo.Elmish.System`:

- `System.pipeMutable` for mutation-heavy phases
- `System.snapshot` to freeze a readonly view
- `System.pipe` for readonly/query/decision phases

The pipeline accumulates a single `Cmd<'Msg>` (not a list), so it stays allocation-friendly even as you add phases.

See: [System pipeline (phases + snapshot)](mvu/system.html)

**Typical layout:**

1. Integrate physics / movement (mutable)
2. Update particles / animation state (mutable)
3. Snapshot
4. AI decisions, queries, overlap detection (readonly)
5. Emit commands/messages

This is an "ECS-ish" approach that works well even if your storage is still dictionaries/arrays.

```fsharp
// Example: splitting mutable physics from readonly logic

match msg with
| Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    System.start model
    // Phase 1: Mutable systems (can mutate positions, particles)
    |> System.pipeMutable (Physics.update dt)
    |> System.pipeMutable (Particles.update dt)
    // SNAPSHOT: transition to readonly
    |> System.snapshot Model.toSnapshot
    // Phase 2: Readonly systems (work with immutable snapshot)
    |> System.pipe (HueColor.update dt 5.0f)
    |> System.pipe (Player.processActions (fun id pos -> PlayerFired(id, pos)))
    // Finish: convert back to Model
    |> System.finish Model.fromSnapshot
```

## Level 4 — Fixed timestep and determinism

**Best for:** Networked games or physics-heavy simulations that require deterministic behavior independent of the user's framerate.

**Goal:** stable simulation independent of framerate.

**Pattern:** run your simulation in fixed slices.

You can do this manually (accumulator in the model), or use Mibo's framework-managed fixed timestep:

```fsharp
Program.mkProgram init update
|> Program.withFixedStep {
    StepSeconds = 1.0f / 60.0f
    MaxStepsPerFrame = 5
    MaxFrameSeconds = ValueSome 0.25f
    Map = FixedStep
}
```

- variable `GameTime` arrives once per frame
- your simulation runs in fixed steps (e.g. 1/60s) potentially multiple times

See: [The Elmish Architecture](mvu/elmish.html) (fixed timestep + dispatch modes)

**Guidelines for determinism:**

- put RNG state (seed) in the model (don't call ambient `System.Random()` from update)
- avoid reading mutable global state from `update`
- represent time as data (the `Tick` message already does this)

```fsharp
// Using framework-managed fixed step
type Msg =
    | FixedStep of dt: float32
    | Tick of Mibo.Elmish.GameTime // Still used for interpolation/rendering

let update msg model =
    match msg with
    | FixedStep dt ->
        // Run deterministic physics
        let newPos = model.Pos + model.Vel * dt
        { model with Pos = newPos }, Cmd.none

    | Tick gt ->
        // Only update visual/interpolation state
        let alpha = float32 gt.ElapsedGameTime.TotalSeconds / 0.01666667f
        let visualPos = System.Numerics.Vector2.Lerp(model.PrevPos, model.Pos, alpha)
        { model with VisualPos = visualPos }, Cmd.none
```

## Level 5 — Frame-stable message processing

**Best for:** Strict lockstep architectures or rollback networking where you need a guarantee that no "stray" messages can slip into the current frame after processing starts.

By default, Mibo processes messages **immediately**: a message dispatched while the runtime is draining the queue can be processed in the same update call.

For some advanced architectures (strict frame boundaries, rollback/lockstep friendliness, avoiding re-entrant cascades), you may want:

> messages dispatched while processing frame N are not eligible until frame N+1.

Mibo supports this via `DispatchMode`:

- `DispatchMode.Immediate` (default): maximum responsiveness
- `DispatchMode.FrameBounded`: stronger frame boundary, up to 1-frame extra latency for cascades

Enable it like this:

```fsharp
Program.mkProgram init update
|> Program.withDispatchMode DispatchMode.FrameBounded
```

### Interaction with `Cmd.deferNextFrame`

`Cmd.deferNextFrame` delays an _effect_ until the next frame.
In `FrameBounded` mode:

- if the deferred effect dispatches immediately when it runs (synchronous dispatch), it will typically be processed **next frame** as expected
- if it dispatches later (async completion), and that completion happens while the runtime is draining messages, it may be deferred **one more frame**

This is not a bug; it's the natural result of combining "defer effect execution" with "frame-bounded message eligibility".

```fsharp
// Example: Spawning entities safely at the start of the next frame
// to avoid mutating the list while iterating it in the current frame.
let update msg model =
    match msg with
    | EnemyDied id ->
        let cleanup = Cmd.ofMsg (RemoveEntity id)
        // Ensure spawn happens cleanly next frame
        let spawnLoot = Cmd.ofMsg (SpawnLoot id) |> Cmd.deferNextFrame
         model, Cmd.batch [ cleanup; spawnLoot ]
```

## Choosing the right rung

You can ship a lot of games at Level 2–3.

- **Card/turn-based:** Level 0–1
- **Platformer/shooter:** Level 1–2.5
- **ARPG:** Level 2.5–3 (+ maybe Level 4)
- **RTS:** Level 2.5–4 (+ Level 5 if you want strict boundaries)

Pick the simplest level that fits your game today, and add the next pieces only when you feel the need.
