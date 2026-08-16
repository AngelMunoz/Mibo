---
title: Programs
category: Adaptive
categoryindex: 3
index: 2
---

# Adaptive Programs & Hosts

An adaptive game is three things you write, plus one loop the framework runs.

## The three things you write

**The state.** A record holding the changeable containers (`cval`, `cmap`) plus whatever plain data your game wants.

**The update.** A function that runs once per frame and advances the game by writing to those containers.

**The frame.** A function that packs what the renderer needs into a single value. It runs after update, once per frame.

Here is a complete game — bees fly around, and each one that leaves the meadow scores a honey:

```fsharp
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish

type Bee = { Pos: Vector2; Dir: Vector2 }

type World = {
    Bees: cmap<int, Bee>
    Honey: cval<int>
}

/// Everything the renderer needs, packed once per frame.
type Frame = {
    Bees: System.Collections.Generic.IReadOnlyDictionary<int, Bee>
    Honey: int
}

let meadowWidth = 40f

let update (world: World) (ctx: AdaptiveContext) (gameTime: GameTime) =
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    // Move every bee; remember who flew past the edge
    let leavers =
        [ for KeyValue(id, bee) in world.Bees |> AMap.getValue ->
            let moved = { bee with Pos = bee.Pos + bee.Dir * dt }
            world.Bees |> CMap.addOrUpdate id moved
            if moved.Pos.X > meadowWidth then Some id else None ]
        |> List.choose id

    // Score and despawn after the loop, one honey per leaver
    for id in leavers do
        world.Bees |> CMap.remove id
        world.Honey.UpdateTo((world.Honey |> AVal.getValue) + 1) |> ignore

/// The frame builder: `frame world` is the `unit -> Frame`
/// the program calls once per frame.
let frame (world: World) () : Frame =
    { Bees = world.Bees |> AMap.getValue
      Honey = world.Honey |> AVal.getValue }

/// init: called once at startup with the frame context.
let init (world: World) (ctx: AdaptiveFrameContext) : AdaptiveInit<Frame> =
    AdaptiveInit.ofFrameBuilder(frame world)

let world = { Bees = CMap.ofList [ 0, { Pos = Vector2.Zero; Dir = Vector2.One } ]
              Honey = CVal.create 0 }

let program =
    AdaptiveProgram.mkProgram (init world) (update world)
    |> AdaptiveProgram.withConfig (GameConfig.withTitle "Bees")
    |> AdaptiveProgram.withRenderer (fun () -> Renderer2D.create draw)

AdaptiveRaylibGame<Frame>(program).Run()
```

`update world` and `init world` are your named functions, partially applied — `mkProgram` receives `init world : AdaptiveFrameContext -> AdaptiveInit<Frame>` and `update world : AdaptiveContext -> GameTime -> unit`, exactly the shapes it asks for. The renderer's `draw` is your drawing function, reading the `Frame` — [rendering](../rendering.html) covers how to write one.

> **NOTE:** when the game grows, the world record is still the thing you apply — `init world` and `update world` keep working as features get added to `World`. See [Systems](systems.html).

## The loop the framework runs

Every frame, in order:

1. Poll input (keyboard, mouse).
2. Run your `update`.
3. Run any work you queued during update ([Intents](intents.html)).
4. Call your `frame` function and hand the result to the renderers.
5. Draw.

You don't write this loop. The part that matters for your code: `update` always runs before `frame`, so the renderer never sees a half-updated world.

## Building the frame: `getValue` or `force`?

The frame builder reads your state with one of two calls, and picking between them is an architecture decision, not a detail:

* `getValue` (`AMap.getValue`, `AVal.getValue`) returns the current state directly. Free, zero allocation — but it is a *borrowed snapshot*: valid until the next write, and only safe on the game thread.
* `force` (`AMap.force`, `ASet.force`, `AList.force`) builds an immutable copy. It allocates, and in exchange the result is *yours*: it never expires, and any thread can hold it.

For a normal game the choice is already made, and the loop above is the reason. The frame is packed after update, nothing writes during packing or drawing, and the renderer consumes it on the same thread before the next step — exactly the lifetime a borrowed snapshot promises. So the default frame is all `getValue`:

```fsharp
let frame (world: World) () : Frame =
    { Bees = world.Bees |> AMap.getValue
      Honey = world.Honey |> AVal.getValue }
```

Switch a value to `force` when it has to leave that read window:

| The packed data... | Read it with | Why |
|---|---|---|
| Goes straight to the renderer this frame | `getValue` | The default — free, consumed inside the window |
| Is broadcast over the network (a headless server packing frames to send) | `force` | You serialize after packing, off the game thread is fine — an immutable copy can't change under the sender |
| Is written to disk (saves, replays) | `force` | Same — the copy is stable while the file IO runs |
| Feeds a render thread of your own | `force` | Borrowed snapshots belong to the game thread; copies don't |
| Is kept for later frames (interpolation history, a pipelined renderer that lags one frame) | `force` | `getValue` results are invalid after the next write |

The rule of thumb: **`force` at the boundary where data leaves the frame's lifetime — and only there.** Calling `force` on everything "to be safe" allocates on every packed value, every frame, and buys nothing: inside the read window the borrowed snapshot is already exactly as correct and faster to produce.

A server packing a frame to broadcast mixes both, forcing only what travels:

```fsharp
let frame (world: World) () : ServerFrame =
    { // the local HUD renderer reads this now — borrow it
      Honey = world.Honey |> AVal.getValue
      // the network thread serializes this later — own it
      Entities = world.Bees |> AMap.force }
```

The collection-by-collection mechanics (including `toSet`/`toMap` for F# interop) are in [Mibo.Adaptive — which read, when](../mibo-adaptive/collections.html#which-one-when).

## Other backends and headless runs

Only the last line changes between backends — the program is the same:

```fsharp
// MonoGame:
AdaptiveMonoGameGame<Frame>(program).Run()

// No window at all — tests and servers:
let runner = AdaptiveHeadless(program)
```

| Host | Backend |
|---|---|
| `AdaptiveRaylibGame<'Frame>` | raylib |
| `AdaptiveMonoGameGame<'Frame>` | MonoGame |
| `AdaptiveHeadless<'Frame>` | none — [Headless Mode](headless.html) |

## Doing work after update

Sometimes update wants to say "do this next, not now" — usually to avoid changing a collection while looping over it, or to react to one feature's events with another feature's logic:

```fsharp
ctx.Intents.post(fun () ->
    world.Honey.UpdateTo((world.Honey |> AVal.getValue) + 1) |> ignore)
```

Queued work runs right after `update` finishes, before the frame is packed. [Intents](intents.html) covers the variants — next frame, background tasks — and when to use which.

## Time and one-time setup

The runner updates `ctx.Time`, a `cval<GameTime>`, every frame — read it for `dt`, or from your frame function so animations run on the game's clock instead of wall-clock.

For setup that must happen before the first frame (connect a socket, warm a cache), do it in `init` — it receives the context, so framework services like the asset cache are already available:

```fsharp
let init (world: World) (ctx: AdaptiveFrameContext) : AdaptiveInit<Frame> =
    connectToServer()
    AdaptiveInit.ofFrameBuilder (frame world)
```

Input, timers and network events arrive through subscriptions registered in `init` — see [Subscriptions](subscriptions.html).
