---
title: The Elmish Architecture
category: Architecture
index: 1
---

# The Elmish Architecture in Games

Mibo uses the Elmish (MVU) pattern to provide a clean, predictable way to manage game state and side effects.

## The Model

The **Model** represents the entire state of your game at a single point in time. It's usually a record containing everything from player positions to scores and active effects.

```fsharp
type Model = {
    PlayerPos: Vector3
    Score: int
}
```

## The Message

A **Message** is a simple type (usually a discriminated union) that describes something that happened in your game.

```fsharp
type Msg =
    | MoveRequested of direction: Vector3
    | CoinCollected of value: int
    | Tick of gt: GameTime
```

## The Update

The **Update** function is the heart of your game. It takes a message and the current model, and returns a new model and a **Command** (for side effects).

```fsharp
let update msg model =
    match msg with
    | MoveRequested dir ->
        { model with PlayerPos = model.PlayerPos + dir }, Cmd.none
    | Tick gt ->
        // handle time-based logic
        let dt = float32 gt.ElapsedGameTime.TotalSeconds
        model, Cmd.none
```

## The Subscription

Your `update` function is pure and passive—it only runs when it receives a message. But games need to be proactive; they need to react to time, raw input, network packets, and async results that happen *outside* that pure loop.

**Subscriptions** bridge this gap. They are active listeners that sit alongside your model, waiting for external events and converting them into messages that your `update` function can handle.

### Defining a subscription

Instead of manually polling hardware or managing event listeners, you simply define a `subscribe` function. This function looks at your current `Model` and declares *what* you want to listen to right now.

```fsharp
let subscribe (ctx: GameContext) (model: Model) =
    Sub.batch [
        // Always listen for keyboard input
        Keyboard.onPressed (fun key -> KeyPressed key) ctx

        // Only listen for mouse clicks if the game is not paused
        if not model.IsPaused then
            Mouse.onLeftClick (fun point -> ClickedAt point) ctx
    ]
```

### How it works

Mibo re-evaluates this function **every time your model changes**. It compares the new list of subscriptions to the previous one:

- **New** subscriptions are started immediately.
- **Removed** subscriptions are stopped (and resources disposed).
- **Unchanged** subscriptions are kept alive.

This declarative approach makes managing complex event logic trivial. You don't need to manually register/unregister handlers when switching states (like from "Menu" to "Gameplay")—you just stop returning the subscription in your list, and Mibo handles the cleanup.

## The View

In Mibo, the **View** doesn't return a visual tree like in web apps. Instead, it receives a `RenderBuffer` and submits drawing commands to it.

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
    Draw2D.sprite texture model.PlayerPos
    |> Draw2D.submit buffer
```

## Why MVU for Games?

1. **Time Travel Debugging**: Since state is centralized, you can record and replay sessions perfectly.
2. **Easy Testing**: Logic is isolated in the pure `update` function, which is trivial to unit test.
3. **Stability**: No more "spooky action at a distance" caused by unexpected mutations.

## `Tick` as a simulation boundary

In Mibo, `Tick` is typically represented as a normal Elmish message (e.g. `Tick of GameTime`).
That means time is _data_ flowing through the same `update` pipeline as input, networking, UI events, etc.

A very scalable convention is:

- non-`Tick` messages update _buffers_ (input snapshots, event queues, pending requests)
- `Tick` is the only place where you mutate the “world” state (movement, physics, spawning, combat)

This makes your simulation feel like a transaction: gather → simulate → commit.

## Framework-managed fixed timestep

If you want a stable simulation step (physics, deterministic-ish gameplay, networking-friendly structure), Mibo can manage a fixed timestep for you.

When enabled, the runtime converts MonoGame's variable `ElapsedGameTime` into **zero or more fixed-size steps per frame** and dispatches a step message for each one.

```fsharp
type Msg =
    | FixedStep of dt: float32
    | Tick of gt: GameTime

let fixedCfg : FixedStepConfig<Msg> = {
    StepSeconds = 1.0f / 60.0f
    MaxStepsPerFrame = 5
    MaxFrameSeconds = ValueSome 0.25f
    Map = FixedStep
}

Program.mkProgram init update
|> Program.withFixedStep fixedCfg
|> Program.withTick Tick // optional: keep for per-frame work
```

Notes:

- Step messages are enqueued **before** `Tick`, so fixed-step simulation runs first.
- `MaxStepsPerFrame` prevents a "spiral of death" after stalls; if the cap is hit, remaining accumulated time is dropped.
- If you keep `Tick`, reserve it for per-frame tasks (UI, camera smoothing, interpolation), and keep simulation in `FixedStep`.

## Frame boundaries and dispatch modes

By default, Mibo uses immediate dispatch: messages dispatched while the runtime is draining the queue can be processed in the same MonoGame `Update` call.

For advanced use cases, you can opt into frame-bounded processing:

```fsharp
Program.mkProgram init update
|> Program.withDispatchMode DispatchMode.FrameBounded
```

In `DispatchMode.FrameBounded`, messages dispatched while the runtime is draining are deferred until the next MonoGame `Update`. This provides a stronger “frame boundary” guarantee at the cost of (at most) one frame of latency for cascades.

### Interaction with `Cmd.deferNextFrame`

`Cmd.deferNextFrame` defers _effects_ (commands) until the next MonoGame `Update`. If the effect dispatches synchronously when it runs, it will usually still be processed next frame.

If the effect starts async work and dispatches later, and that dispatch occurs while the runtime is draining messages, then `DispatchMode.FrameBounded` will push it to the following frame.

For a deeper “upgrade path” overview, see [Scaling Mibo (Simple → Complex)](scaling.html).

Related:

- [Programs & composition](program.html)
- [System pipeline (phases + snapshot)](system.html)
