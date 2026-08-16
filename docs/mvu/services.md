---
title: Service Composition
category: MVU
categoryindex: 2
index: 7
---

# Service Composition

As your game grows, you will likely need services that are shared across your `init`, `update`, and `view` functions—things like Networking, Leaderboards, or Save Data.

> **NOTE:** the Env pattern here is runtime-agnostic — it applies verbatim to adaptive programs, with one improvement: `boot ctx` receives the `GameContext` before init runs, so context-dependent services initialize without escape hatches. See [Adaptive Services](../adaptive/services.html).

Instead of passing these individually or relying on global state, we recommend creating a strongly typed "Composition Root" or "Environment" record.

## The Environment Context

You should initialize your services **before** you construct the program. This ensures they are ready immediately and avoids the "circular dependency" trap.

This pattern is often referred to as the **Env** (Environment) pattern in F# application architecture.

```fsharp
// The "Env" pattern
type Env = {
    Network: INetworkService
    Leaderboard: ILeaderboardService
}

// 1. Create environment independent of the program
let env = {
    Network = Network.create "https://api.example.com"
    Leaderboard = Leaderboard.create ()
}

// 2. Pass to program functions
//    (e.g., capture in a closure or pass as an argument)
let init = State.init env
let update = State.update env
```

## Further Reading

For a deeper dive into this pattern, we recommend Bartosz Sypytkowski's article:
[Dealing with complex dependency injection in F#](https://www.bartoszsypytkowski.com/dealing-with-complex-dependency-injection-in-f/)

## Avoiding Circular References

A common pitfall is trying to initialize a service inside your `init` function because it requires access to the `GameContext` or `IAssets`. This creates a circular dependency.

### Guidance

1.  **Prefer Independence**: Design services to be independent of the concrete window or renderer if possible (they should live against `Mibo.Core` contracts like `GameContext`/`IAssetCache`, not a backend host).
2.  **Escape Hatches**: If you absolutely *must* have a circular reference, handle this carefully using:
    - F# `ref` cells or mutable fields initialized later.
    - **Note**: These approaches are "know what you are doing" scenarios. Use them only when necessary.

> _**NOTE**_: If you prefer to use a DI container, you can create it at the same time as you would with the environment and pass it to the program.

## Full Program Example

Here is how the whole picture fits together in `Program.fs`.

```fsharp
module MyGame.Program

open Mibo.Elmish
open Mibo.Elmish.Graphics2D

// 1. Define the Environment Type
type Env = {
    Network: INetworkService
    Leaderboard: ILeaderboardService
}

// 2. Define the Composition Root (Factory)
let createEnv () =
    {
        Network = Network.create "https://api.example.com"
        Leaderboard = Leaderboard.create ()
    }

// 3. Define Game Logic (Dependencies Injected)
let init (env: Env) (ctx: GameContext) =
    // Synchronous call (e.g. settings listeners)
    env.Network.Connect()

    // Async call (e.g. fetching data)
    let cmd = Cmd.ofAsync (async { return env.Leaderboard.Load() }) LeaderboardLoaded LeaderboardError

    struct ({ Score = 0; HighScores = [] }, cmd)

let update (env: Env) (msg: Msg) (model: Model) =
    match msg with
    | ScoreChanged newScore ->
        // Trigger async operation
        let cmd = Cmd.ofAsync (async { return env.Leaderboard.SubmitScore(newScore) }) (fun _ -> ScoreSubmitted) ScoreError
        struct ({ model with Score = newScore }, cmd)

    | LeaderboardLoaded scores ->
        struct ({ model with HighScores = scores }, Cmd.none)

let view (env: Env) (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
    // Draw score, etc.
    ()

// 4. Assemble & Run (Entry Point)
[<EntryPoint>]
let main _args =
    // Create the environment FIRST
    let env = createEnv ()

    // partial application/inject the environment
    let init = init env
    let update = update env
    let view = view env

    // Compose the program with the Env captured
    let program =
        Program.mkProgram init update
        |> Program.withConfig (fun cfg ->
            { cfg with Title = "My Game"; Width = 1280; Height = 720 })
        |> Program.withAssets
        |> Program.withRenderer (fun () -> Renderer2D.create view)

    // Run the game — use your backend's host:
    //   raylib:   new RaylibGame<Model, Msg>(program)
    //   MonoGame: new MiboGame<Model, Msg>(program)
    let game = new RaylibGame<Model, Msg>(program)
    game.Run()
    0
```

## Note on Async & The Game Loop

Common `Cmd.ofAsync` usage in Mibo follows standard Elmish rules: **it does not block the game loop**.

When you dispatch an async command:
1.  The `update` function returns immediately with the new model.
2.  The async work starts on a background thread (or the thread pool).
3.  The game loop continues running (rendering frames, processing inputs).
4.  When the async work completes, a new message is dispatched back into the loop.

This means you can safely perform heavy I/O (network requests, file saving) without causing frame stutters.
