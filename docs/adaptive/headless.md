---
title: Headless Mode
category: Adaptive
categoryindex: 3
index: 8
---

# Headless Mode

An adaptive program doesn't need a window. `AdaptiveHeadless` runs the exact same program — same update, same frame function, same loop — with no backend attached. You use it for two things: testing your game, and running it as a server.

## Stepping a game by hand

Create the runner from your program, then step it. Each `Step` runs one full frame: queued work, update, frame pack.

```fsharp
open Mibo.Adaptive

let runner = AdaptiveHeadless(program)

// one frame of 1/60s
runner.Step(TimeSpan.FromSeconds(1.0 / 60.0)) |> ignore

// the packed frame — assert on it like the renderer would read it
let frame = runner.Frame
```

`runner.Post(work)` queues work from the test thread — the same thing `ctx.Intents.post` does from inside update. That is how a test plays the game: post the inputs, step, look at the frame.

```fsharp
runner.Post(fun () -> world.Paused.Set false)

let cleared =
    runner.StepUntil(
        (fun frame -> frame.Score >= 10),
        TimeSpan.FromSeconds(1.0 / 60.0),
        maxFrames = 600)
```

`StepUntil` steps until a condition on the frame holds, and tells you whether it ever did — the standard shape for "play until something happened" assertions. `StepN(count, dt)` runs a fixed number of frames when you want to test timing itself.

A full test looks like:

```fsharp
[<Test>]
let ``clicking a gem scores it``() =
    let runner = AdaptiveHeadless(program)
    runner.Frame.Gems.Count |> isGreaterThan 0

    runner.Post(fun () -> clickAt(Vector2(5f, 5f)))

    let scored =
        runner.StepUntil((fun f -> f.Score = 1), oneFrame, 60)

    scored |> isTrue
```

## Long-running servers

A headless server steps a simulation and broadcasts frames. Two forms pace the loop for you, and they differ in whose thread does the stepping:

* `Run` returns a lazy sequence — the loop advances only as you enumerate it, on your thread.
* `RunAsync` returns an `IAsyncEnumerable` stepped by a dedicated game thread; you consume the outcomes.

```fsharp
let server = AdaptiveHeadless(serverProgram)

// Synchronous: steps as you enumerate — the loop owns the calling thread
for struct (gameTime, frame) in server.Run(TimeSpan.FromMilliseconds(16)) do
    broadcast frame

// Asynchronous: a dedicated game thread steps; you consume the frames
async {
    for outcome in server.RunAsync(TimeSpan.FromMilliseconds(16)) do
        broadcast outcome.Frame
}
```

Nothing runs until you enumerate — a bare `server.Run(...)` statement builds the sequence and discards it.

Pick one way to advance a runner and stay with it: `Step`/`StepN`/`StepUntil` for tests driven by hand, `Run`/`RunAsync` for servers that pace the loop automatically. Mixing them on the same runner double-advances the simulation.

Because there is no window, there are no renderers — the frame your function packs is just data. A server serializes it and sends it; a test asserts on it.

## Fixed step

Physics and networked simulations want a fixed timestep: update in fixed slices, not once per drawn frame. `AdaptiveProgram.withFixedStep` runs update as many fixed sub-steps as the frame's elapsed time covers, then packs the frame once at the end:

```fsharp
AdaptiveProgram.mkProgram init update
|> AdaptiveProgram.withFixedStep {
    StepSeconds = 1.0f / 60.0f
    MaxStepsPerFrame = 5
    MaxFrameSeconds = ValueNone
  }
```

Variable `GameTime` arrives once per frame; your simulation runs in fixed steps (e.g. 1/60 s) potentially multiple times, and the frame packs once. On the headless runner, `Step(interval)` with an interval larger than `StepSeconds` runs the extra sub-steps automatically.

## Observers

Tests and diagnostics sometimes want to look at each packed frame without being a renderer — an FPS counter, a recorder, a per-frame assertion. `withObserver` adds a callback that receives every forced frame:

```fsharp
let program =
    myProgram
    |> AdaptiveProgram.withObserver(fun () ->
        AdaptiveProgram.observe(fun struct (_ctx, frame, gameTime) ->
            frameRate.See(gameTime)))
```

Observers run after the frame is forced, in registration order. They read the frame; they don't change the simulation. A headless runner keeps the same frame in `runner.Frame`, so in tests you can just read that instead of wiring an observer — observers are for code that runs alongside the game, not for the game's own assertions.
