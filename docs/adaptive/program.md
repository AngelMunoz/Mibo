---
title: Programs
category: Adaptive
categoryindex: 3
index: 2
---

# Adaptive Programs & Hosts

An adaptive game is three things you write: **State, Projection, Update** (SPU), plus one loop the framework runs. This page shows all three in one complete game, then how to run it on each host. Everything else (intents, subscriptions, services) has its own page, linked at the end.

## The three things you write

**The state.** A record holding the changeable containers (`cval`, `cmap`) plus whatever plain data your game wants. This is where the facts live: you write them, the framework tracks when they changed.

**The projection.** Everything the game derives from the state instead of maintaining it by hand: the scoreboard string that follows the honey count, the "bees near this flower" view, and the one every game has, the frame projection that packs what the renderer needs into a single value (the `Frame`). Projections are built once and recompute when their inputs change; the runner forces the frame projection after update, once per step, via `AdaptiveInit.ofFrameBuilder`. The `Frame` itself is an implementation detail of your renderer; the projections are what make the architecture tick.

**The update.** A function that runs once per step and advances the game by writing to the state's containers.

Here is a complete game: bees fly around, and each one that leaves the meadow scores a honey:

```fsharp
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish

type Bee = { Pos: Vector2; Dir: Vector2 }

// S: the state
type World = {
    Bees: cmap<int, Bee>
    Honey: cval<int>
}

let world = { Bees = CMap.ofSeq [ 0, { Pos = Vector2.Zero; Dir = Vector2.One } ]
              Honey = CVal.create 0 }

// the projection's output: everything the renderer needs, once per step
type Frame = {
    Bees: System.Collections.Generic.IReadOnlyDictionary<int, Bee>
    Honey: int
    Status: string
}

// P: the projection. First the derived values, built once from the
// state: statusText is a plain function over one fact, and status
// recomputes only when the honey fact moves.
let statusText h = $"Honey: {h}"
let status = world.Honey |> AVal.map statusText

// Then the frame projection: the `unit -> Frame` the runner forces
// once per step, after update. It packs state reads and derived
// values alike.
let frame () : Frame =
    { Bees = world.Bees |> AMap.getValue
      Honey = world.Honey |> AVal.getValue
      Status = status |> AVal.getValue }

let meadowWidth = 40f

// Scratch buffer, created once and reused every step; the posted
// intent below clears it after draining, never during update
let leavers = ResizeArray<int>()

// U: the update
let update (world: World) (ctx: AdaptiveContext) (gameTime: GameTime) =
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    // Move every bee; remember who flew past the edge
    for KeyValue(id, bee) in world.Bees |> AMap.getValue do
        let moved = { bee with Pos = bee.Pos + bee.Dir * dt }
        world.Bees |> CMap.addOrUpdate id moved
        if moved.Pos.X > meadowWidth then leavers.Add id

    // Removing mid-loop would invalidate the enumeration, so the
    // despawn (and the score that reacts to it) is posted work:
    // the queue runs it after update, in order, before the frame is forced
    let despawnLeavers () =
        world.Honey.UpdateTo((world.Honey |> AVal.getValue) + leavers.Count) |> ignore

        for id in leavers do
            world.Bees |> CMap.remove id

        leavers.Clear()

    if leavers.Count > 0 then ctx.Intents.post despawnLeavers

/// init: called once at startup with the frame context;
/// registers the projection with the program.
let init (world: World) (ctx: AdaptiveFrameContext) : AdaptiveInit<Frame> =
    AdaptiveInit.ofFrameBuilder frame

// your drawing code: turns a Frame into drawing commands
// (see rendering.html)
let draw frameBuffer buffer = ...

let createRenderer () = Renderer2D.create draw

let program =
    AdaptiveProgram.mkProgram (init world) (update world)
    |> AdaptiveProgram.withConfig (GameConfig.withTitle "Bees")
    |> AdaptiveProgram.withRenderer createRenderer

AdaptiveRaylibGame<Frame>(program).Run()
```

`update world` and `init world` are partially applied: the `world` argument is already fixed, and what is left is exactly the shape `mkProgram` asks for (`init world : AdaptiveFrameContext -> AdaptiveInit<Frame>` and `update world : AdaptiveContext -> GameTime -> unit`). The renderer's `draw` is your drawing function, reading the `Frame`; [rendering](../rendering.html) covers how to write one.

Notice what the game does *not* have: no code that recomputes the status string when honey changes. `status` is built once, when the program starts; from then on it recomputes only when the honey fact moves, and the frame just reads it. That is the P doing its job. [Derived State](derived-state.html) covers where projections live as the game grows, and the full combinator catalog is in [Mibo.Adaptive](../mibo-adaptive/overview.html).

> **NOTE:** when the game grows, the world record is still the thing you apply; `init world` and `update world` keep working as features get added to `World`. See [Systems](systems.html).

## The loop the framework runs

Every step, in order:

1. Poll input, apply values posted from other threads, and refresh subscriptions.
2. Write the current game time into the time root (`ctx.Time`).
3. Run your `update`.
4. Run the intents you queued during update.
5. Force the projection and hand the frame it produces to the renderers.
6. Draw.

You don't write this loop. The part that matters for your code: update always runs before the projection is forced, so the renderer never sees a half-updated world.

Before the first step, the runner drains the intent queue once at startup: work `init` posted through its context (`ctx.Intents.post`, `postNextFrame`, `postTask`, `postAsync`) runs right after `init` returns and before the first frame is forced, so the first frame includes its effects. That is the adaptive counterpart of the `Cmd` the MVU `init` returns — startup setup can react like any other phase.

The runner writes the game time into `ctx.Time` every step, so you can read `dt` from it. If you want animations to pause when the game does, keep a clock of your own on the world instead (write it in update unless paused, read it in the projection); the projection then stays a plain state-to-frame mapping.

## Reading state in the projection

The projection reads your containers with `getValue` (`AMap.getValue`, `AVal.getValue`), and for the normal game that is the whole story: the frame is consumed by the renderer on the same thread before the next step, which is exactly the lifetime a `getValue` result promises.

The one exception: data that has to *outlive* the frame or *leave* the game thread (a server frame sent over the network, a save written to disk, a render thread of your own). For that, `force` builds an immutable copy that is yours to keep, at the cost of an allocation. The rule of thumb: `force` at the boundary where data leaves the frame's lifetime, and only there. The full comparison, per situation, is in [Mibo.Adaptive: which read, when](../mibo-adaptive/collections.html#Which-one-when).

## Running it: hosts

The program is the same on every backend; each one hands it to its own host. MonoGame wraps it first (the wrapper is where device-level setup hooks go):

```fsharp
// MonoGame:
let mgProgram = AdaptiveMonoGameProgram.ofProgram program
AdaptiveMonoGameGame<Frame>(mgProgram).Run()

// No window at all: tests and servers
let runner = AdaptiveHeadless(program)
```

| Host | Backend |
|---|---|
| `AdaptiveRaylibGame<'Frame>` | raylib |
| `AdaptiveMonoGameGame<'Frame>` | MonoGame |
| `AdaptiveHeadless<'Frame>` | none ([Headless Mode](headless.html)) |

## Where to go next

- Work queued during update (the `ctx.Intents.post` in the game above), including next-frame and background variants: [Intents](intents.html).
- Input, timers, and network events, registered in `init`: [Subscriptions](subscriptions.html).
- Splitting the game into features once `update` grows: [Systems](systems.html).
- Sharing audio, save data, and other services: [Services](services.html).
- Setup that must run before the first frame (connect a socket, warm a cache) goes in `init`: it receives the context, so framework services like the asset cache are already available, and work it defers through `ctx.Intents` runs at the startup drain, before the first frame is forced (see [Intents](intents.html)).
