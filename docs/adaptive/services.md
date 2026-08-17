---
title: Services
category: Adaptive
categoryindex: 3
index: 7
---

# Services in Adaptive Programs

As your game grows, you will likely need services that are shared across your update and frame functions: things like Audio, Networking, or Save Data.

Instead of passing these individually or relying on global state, create a strongly typed environment record. The [Elmish version of this guide](../mvu/services.html) covers the same pattern for that runtime; this page is the adaptive one, end to end.

## The environment record

Initialize your services **before** you build the program, so they are ready before anything needs them and nothing has to be built twice.

```fsharp
// The "Env" pattern
type Env = {
    Audio: IAudioService
    Save: ISaveService
}

// 1. Create the environment independent of the program
let createEnv () = {
    Audio = AudioService.create()
    Save = SaveService.create()
}
```

## Full Program Example

Here is the whole picture: an environment with two services, a small world, and the program from [Adaptive Programs](program.html). The environment is created first, at the top; `init`, `update` and the program are named and applied to it:

```fsharp
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish

type Gem = { Pos: Vector2 }

type World = {
    Gems: cmap<int, Gem>
    Score: cval<int>
}

/// Everything the renderer needs, packed once per step.
type Frame = {
    Gems: System.Collections.Generic.IReadOnlyDictionary<int, Gem>
    Score: int
}

// 1. The environment, built before the program
let env = createEnv()

let world = { Gems = CMap.empty; Score = CVal.create 0 }

// Scratch buffer, created once and reused every step; the posted
// intent below clears it after draining, never during update
let taken = ResizeArray<int>()

let update (world: World) (ctx: AdaptiveContext) (gameTime: GameTime) =
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    for KeyValue(id, gem) in world.Gems |> AMap.getValue do
        let moved = { gem with Pos = gem.Pos + Vector2(dt, 0f) }
        world.Gems |> CMap.addOrUpdate id moved
        if moved.Pos.X > 10f then taken.Add id

    // Removals during the loop would invalidate the enumeration:
    // post them; the queue runs the work after update, before the frame is forced
    let takeGems () =
        world.Score.UpdateTo((world.Score |> AVal.getValue) + taken.Count) |> ignore

        for id in taken do
            world.Gems |> CMap.remove id
            // Synchronous service call: one-shot sounds are cheap
            env.Audio.PlayPickup()

        taken.Clear()

    if taken.Count > 0 then ctx.Intents.post takeGems

    // Autosave every ten pickups, off the game thread
    let score = world.Score |> AVal.getValue

    if score > 0 && score % 10 = 0 then
        let snapshot = score

        let saveScore () = env.Save.SaveScoreAsync(snapshot)
        let saved () = ()
        let saveFailed (ex: exn) = eprintfn $"autosave failed: {ex.Message}"

        ctx.Intents.postTask(saveScore, ofSuccess = saved, ofError = saveFailed)

/// The frame builder: `frame world` is the `unit -> Frame`
/// the runner forces once per step.
let frame (world: World) () : Frame =
    { Gems = world.Gems |> AMap.getValue
      Score = world.Score |> AVal.getValue }

let init (world: World) (ctx: AdaptiveFrameContext) : AdaptiveInit<Frame> =
    // Services that need the game context (asset caches, audio devices)
    // initialize here: once, before the first frame
    env.Audio.Init(ctx.Context)
    AdaptiveInit.ofFrameBuilder (frame world)

// your drawing code: turns a Frame into drawing commands
// (see rendering.html)
let draw frameBuffer buffer = ...

let createRenderer () = Renderer2D.create draw

let program =
    AdaptiveProgram.mkProgram (init world) (update world)
    |> AdaptiveProgram.withConfig (GameConfig.withTitle "Gems")
    |> AdaptiveProgram.withRenderer createRenderer

[<EntryPoint>]
let main _ =
    AdaptiveRaylibGame<Frame>(program).Run()
    0
```

## Avoiding Circular References

A common pitfall is a service that needs the `GameContext` (to load sounds, say), which only exists once the host is running, so it feels like the service can't be built first.

On the adaptive side this has a clean answer: build the service in `createEnv` without the context, and give it an `Init(ctx)` method you call where the example does, inside the program's setup, which runs before the first frame and already receives the context. No mutable "initialized later" fields, no `ref` cells.

Framework services are already registered for you; pull them from the context instead of building your own:

```fsharp
let assets = ctx.Context |> GameContext.getService<IAssets>
```

## A Note on Async & The Game Loop

Background work follows the same rules as everywhere in an adaptive game: never touch state containers from another thread. Queue the work with `ctx.Intents.postTask` or `postAsync`; when it completes, your `ofSuccess` callback applies the result on the game thread:

1. Your `update` returns immediately; the frame is not delayed.
2. The work runs in the background.
3. The game loop keeps running (updating, drawing).
4. When it completes, the callback runs on the game thread and writes the result.

This means you can safely perform heavy I/O (network requests, file saving) without frame stutters.

One last habit worth keeping: frame counters, cost timers, and debug stats are plain mutable fields on your world, written from `update`. Don't increment a global from inside a derived value's computation; derived values are supposed to be pure (no side effects), and a hidden write in one is invisible to everything else.
