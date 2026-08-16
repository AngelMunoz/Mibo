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

Here is a complete, runnable game — bees fly around, and each one that leaves the meadow scores a honey:

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

let update (world: World) (gameTime: GameTime) =
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    // Move every bee; remember who flew past the edge
    let leavers =
        [ for KeyValue(id, bee) in world.Bees |> AMap.getValue ->
            let moved = { bee with Pos = bee.Pos + bee.Dir * dt }
            world.Bees |> CMap.addOrUpdate id moved
            if moved.Pos.X > meadowWidth then Some id else None ]

    // Score and despawn after the loop, one honey per leaver
    for id in List.choose id leavers do
        world.Bees |> CMap.remove id
        world.Honey.UpdateTo((world.Honey |> AVal.getValue) + 1) |> ignore

let frame (world: World) : Frame =
    { Bees = world.Bees |> AMap.getValue
      Honey = world.Honey |> AVal.getValue }
```

## Wiring it together

`AdaptiveProgram.mkProgram` takes two functions: one that builds the initial state and returns the frame builder, one that runs each frame. Write a single `start` function that creates the world and closes over it — both halves see the same state, and nothing else has to know how to reach it:

```fsharp
let start() =
    let world = { Bees = CMap.ofList [ 0, { Pos = Vector2.Zero; Dir = Vector2.One } ]
                  Honey = CVal.create 0 }

    AdaptiveProgram.mkProgram
        (fun _ -> AdaptiveInit.ofFrameBuilder(fun () -> frame world))
        (fun _ gameTime -> update world gameTime)
    |> AdaptiveProgram.withConfig (fun _ ->
        { GameConfig.defaultConfig with
            Title = "Bees"
            Width = 1280
            Height = 720 })
    |> AdaptiveProgram.withRenderer (fun () -> Renderer2D.create draw)

[<EntryPoint>]
let main _ =
    AdaptiveRaylibGame<Frame>(start()).Run()
    0
```

> **NOTE:** if update grows to the point where you want each feature's logic in its own module, the same closure pattern still works — the world record is the thing you close over. See [Adaptive Systems](systems.html).

## The loop the framework runs

Every frame, in order:

1. Poll input (keyboard, mouse).
2. Run your `update`.
3. Run any work you queued during update (more on `post` below).
4. Call your `frame` function and hand the result to the renderers.
5. Draw.

You don't write this loop. The part that matters for your code: `update` always runs before `frame`, so the renderer never sees a half-updated world.

## Other backends and headless runs

Only the host line changes:

```fsharp
// MonoGame:
let game = AdaptiveMonoGameGame<Frame>(start())
game.Run()
```

| Host | Backend |
|---|---|
| `AdaptiveRaylibGame<'Frame>` | raylib |
| `AdaptiveMonoGameGame<'Frame>` | MonoGame |
| `AdaptiveHeadless<'Frame>` | none — for tests and servers |

For tests, `AdaptiveHeadless` steps the same loop without a window — that's [Headless Mode](headless.html).

## Doing work after update

Sometimes update wants to say "do this next, not now" — usually to avoid changing a collection while looping over it, or to react to one feature's events with another feature's logic:

```fsharp
let update (world: World) (ctx: AdaptiveContext) (gameTime: GameTime) =
    // ...move the bees...
    ctx.Intents.post(fun () ->
        world.Honey.UpdateTo((world.Honey |> AVal.getValue) + 1) |> ignore)
```

Queued work runs right after `update` finishes, before the frame is packed. There are variants for "next frame" and for background work — [Intents](intents.html) covers when to use which.

## Time and one-time setup

The runner updates `ctx.Time`, a `cval<GameTime>`, every frame — read it for `dt`, or from your frame function so animations run on the game's clock instead of wall-clock.

For setup that must happen before the first frame (connect a socket, warm a cache), take a `boot` function and call it first inside `start`'s init — it receives the context, so framework services like the asset cache are already available:

```fsharp
let start() =
    let world = { Bees = CMap.empty; Honey = CVal.create 0 }

    AdaptiveProgram.mkProgram
        (fun ctx ->
            boot ctx
            AdaptiveInit.ofFrameBuilder(fun () -> frame world))
        (fun _ gameTime -> update world gameTime)
```
