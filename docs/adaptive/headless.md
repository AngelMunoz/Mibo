---
title: Headless Mode
category: Adaptive
categoryindex: 3
index: 8
---

# Headless Mode

An adaptive program doesn't need a window. `AdaptiveHeadless` runs the exact same program (same update, same frame builder, same loop) with no backend attached. You use it for two things: testing your game, and running it as a server.

## Stepping a game by hand

Create the runner from your program, then step it. Each `Step` runs one full pass of the loop: posted work drains, update runs, queued intents drain, the frame is forced.

```fsharp
open Mibo.Adaptive

let oneFrame = TimeSpan.FromSeconds(1.0 / 60.0)

let runner = AdaptiveHeadless(program)

// one frame of 1/60s
runner.Step(oneFrame) |> ignore

// the packed frame: assert on it like the renderer would read it
let frame = runner.Frame
```

`runner.Post(work)` queues work from the test thread, the same thing `ctx.Intents.post` does from inside update. That is how a test plays the game: post the inputs, step, look at the frame.

```fsharp
let unpause () = world.Paused.Set false
let scoreAtLeastTen (frame: Frame) = frame.Score >= 10

runner.Post unpause

let cleared =
    runner.StepUntil(
        scoreAtLeastTen,
        oneFrame,
        maxFrames = 600)
```

`StepUntil` steps until a condition on the frame holds, and tells you whether it ever did, the standard shape for "play until something happened" assertions. `StepN(count, dt)` runs a fixed number of frames when you want to test timing itself.

A full test looks like (`isGreaterThan`/`isTrue` are Expecto assertion helpers, and the backtick-quoted name is F# allowing spaces in a test's name):

```fsharp
[<Test>]
let ``clicking a gem scores it``() =
    let runner = AdaptiveHeadless(program)
    runner.Frame.Gems.Count |> isGreaterThan 0

    let clickCenter () = clickAt (Vector2(5f, 5f))
    runner.Post clickCenter

    let scoredExactlyOne (f: Frame) = f.Score = 1
    let scored = runner.StepUntil(scoredExactlyOne, oneFrame, 60)

    scored |> isTrue
```

## Long-running servers

A headless server steps a simulation and broadcasts frames. Two forms pace the loop for you, and they differ in whose thread does the stepping:

* `Run` returns a lazy sequence: the loop advances only as you enumerate it, on your thread.
* `RunAsync` returns an `IAsyncEnumerable` stepped by a dedicated game thread; you consume the outcomes.

```fsharp
let server = AdaptiveHeadless(serverProgram)

// Synchronous: steps as you enumerate; the loop owns the calling thread
for struct (gameTime, frame) in server.Run(TimeSpan.FromMilliseconds(16)) do
    broadcast frame

// Asynchronous: a dedicated game thread steps; you consume the frames
open IcedTasks

asyncEx {
    for outcome in server.RunAsync(TimeSpan.FromMilliseconds(16)) do
        broadcast outcome.Frame
}
```

Nothing runs until you enumerate: a bare `server.Run(...)` statement builds the sequence and discards it.

> **Note:** `for .. in` over `IAsyncEnumerable` comes from the [IcedTasks](https://www.nuget.org/packages/IcedTasks) package; the built-in `async`/`task` builders don't accept it. `open IcedTasks` gives you `asyncEx`; alternatively, `open IcedTasks.Polyfill.Async.PolyfillBuilders` upgrades the plain `async` builder, and `open IcedTasks.Polyfill.Task.Tasks` does the same for `task`.

Pick one way to advance a runner and stay with it: `Step`/`StepN`/`StepUntil` for tests driven by hand, `Run`/`RunAsync` for servers that pace the loop automatically. Mixing them on the same runner double-advances the simulation.

Because there is no window, there are no renderers; the frame your builder packs is data. A server serializes it and sends it; a test asserts on it.

## Fixed step

Physics and networked simulations want a fixed timestep: update in fixed slices, not once per drawn frame. `AdaptiveProgram.withFixedStep` runs update as many fixed sub-steps as the frame's elapsed time covers, then forces the frame once at the end:

```fsharp
AdaptiveProgram.mkProgram init update
|> AdaptiveProgram.withFixedStep {
    StepSeconds = 1.0f / 60.0f
    MaxStepsPerFrame = 5
    MaxFrameSeconds = ValueNone
  }
```

Variable `GameTime` arrives once per frame; your simulation runs in fixed steps (e.g. 1/60 s) potentially multiple times, and the frame is forced once. On the headless runner, `Step(interval)` with an interval larger than `StepSeconds` runs the extra sub-steps automatically.

## Observers

Tests and diagnostics sometimes want to look at each packed frame without being a renderer: an FPS counter, a recorder, a per-frame assertion. `withObserver` adds a callback that receives every forced frame (the context, the frame, and the time):

```fsharp
let observeFrameRate struct (_ctx: GameContext, _frame: Frame, gameTime: GameTime) =
    frameRate.See gameTime

let createObserver () = AdaptiveProgram.observe observeFrameRate

let program =
    myProgram
    |> AdaptiveProgram.withObserver createObserver
```

Observers run after the frame is forced, in registration order. They read the frame; they don't change the simulation. A headless runner keeps the same frame in `runner.Frame`, so in tests you can read that instead of wiring an observer; observers are for code that runs alongside the game, not for the game's own assertions.
