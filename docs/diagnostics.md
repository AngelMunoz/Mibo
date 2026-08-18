---
title: Diagnostics
category: Amenities
categoryindex: 12
index: 5
---

# Diagnostics (frame rates + screenshots)

Mibo can measure your game while it runs: frame rates, update and draw cost, allocation, and graphics counters. It can also save a screenshot on request. All of it is opt-in. You build one `FrameProfiler`, hand it to the program with `withProfiler`, and the host does the measuring. When you do not hand one over, nothing runs and nothing is registered.

`FrameProfiler.Snapshot` holds the numbers of the last completed window. The window is half a second by default. The values stay zeroed until the first window closes.

## MVU wiring

Create the profiler before the program, the same way you create any service. Then wire it in and read it where you already have the context.

```fsharp
module MyGame.Program

open System.Numerics
open Mibo.Diagnostics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

let update ctx msg model =
    match msg with
    | ScreenshotRequested ->
      struct (model, Cmd.ofEffect(
        Effect<Msg>(fun() ->
          // get the screenshot as a side effect
          // the profiler was registered in the context
          // when the program was initialized.
          let profiler = Diagnostics.getProfiler ctx
          profiler.RequestScreenshot "screenshot.png"
        )
      ))
    // ... your other messages

let createRenderer () =
    let mutable display = ""
    let mutable lastWindow = 0L

    let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
        match Diagnostics.tryGetProfiler ctx with
        | ValueSome p ->
            let stats = p.Snapshot

            if stats.TotalFrames <> lastWindow then
                lastWindow <- stats.TotalFrames
                display <- Diagnostics.format stats

            buffer
                .text(model.Font, display, Vector2(8f, 8f), 20f, layer = 1001<RenderLayer>)
                .drop()
        | ValueNone -> buffer.drop()

    Renderer2D.create view

let program =
    Program.mkProgramCtx init update
    |> Program.withProfiler (FrameProfiler(FrameProfiler.DefaultWindow, canScreenshot = true))
    |> Program.withRenderer createRenderer
```

The update needs the context to build the screenshot command, so the example uses `Program.mkProgramCtx`. If your update does not need the context, `Program.mkProgram` works the same for the overlay view.

## Adaptive wiring

The adaptive program takes the profiler the same way. The `Update` phase already receives the context, so reads and requests go inline there. The overlay view shape is identical to the MVU one above: read the profiler from the context in your view.

```fsharp
open Mibo.Adaptive
open Mibo.Diagnostics
open Mibo.Elmish


// The imperative phase: reads projections, writes roots, asks for captures.
let update ctx gameTime =

    ctx.Intents.post(fun () ->
      let profiler = Diagnostics.getProfiler ctx
      profiler.RequestScreenshot "screenshot.png"
      // Turn measurement On|Off at runtime
      // profiler.Enabled <- false
    )

    ()

let program =
    AdaptiveProgram.mkProgram init update
    |> AdaptiveProgram.withProfiler (FrameProfiler(FrameProfiler.DefaultWindow, canScreenshot = true))
    |> AdaptiveProgram.withRenderer createRenderer
```

## Headless runners

`HeadlessProgram.withProfiler` wires the same profiler into a headless runner. There is no screen, so `CanScreenshot` is only true if you built the profiler with `canScreenshot = true` yourself; a capture request still goes nowhere because no host drains it. Build headless profilers with the one argument form, `FrameProfiler(window)`.

With `Step` you drive the clock yourself, so read the cost fields (`UpdateMs`, `AllocatedBytes`) rather than the rate fields. With `Run` and `RunAsync` the pacing is wall clock time and the rate fields are meaningful. A fixed step still feeds `SimStepsPerSecond` and `SlowFrames`.

## FrameStats

| Field                                                   | Meaning                                                                                                           |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `FramesPerSecond`                                       | Host frames per second. A host frame is one pass of the host loop.                                                |
| `DrawsPerSecond`                                        | Draws per second. This is the rate the player sees. `ValueNone` on headless runners.                              |
| `SimStepsPerSecond`                                     | Simulation steps per second. With a fixed step this counts every step, not every frame.                           |
| `FrameMs`                                               | Mean host frame interval.                                                                                         |
| `WorstFrameMs`                                          | Worst host frame interval in the window. A spike here with a low mean means stutter, not a constant cost problem. |
| `UpdateMs`                                              | Mean cost of the update phase.                                                                                    |
| `DrawMs`                                                | Mean cost of the draw phase. `ValueNone` on headless runners.                                                     |
| `AllocatedBytes`                                        | Bytes allocated on the frame thread during the window.                                                            |
| `Gen0Collections`, `Gen1Collections`, `Gen2Collections` | Collection counts during the window.                                                                              |
| `SlowFrames`                                            | Frames that ran behind. Fixed step drops and the MonoGame catch up flag count here.                               |
| `TotalFrames`                                           | Frames since the profiler was created.                                                                            |
| `GpuDrawCalls`, `GpuPrimitives`, `GpuTextureBinds`      | The last frame's graphics counters. MonoGame only. `ValueNone` elsewhere.                                         |

## Screenshots

`RequestScreenshot(path)` queues a capture. The host writes a PNG at the given path when the frame finishes drawing, so the file holds the complete frame. The path is used as given, and a missing directory is created. The request does nothing while measurement is off. The capture reads the whole frame and encodes it on the same thread, so expect one slow frame per request.

In an MVU game the request belongs to a command, as the wiring example shows: the message asks, the effect queues. In an adaptive game the `Update` phase can call it directly, because that phase is imperative by design.

## Turning measurement on and off

`profiler.Enabled` turns measurement on and off at any time, from any code that holds the profiler. While it is off, every stamp and every request does nothing. Turning it back on starts a fresh window, so the time spent off never shows up as a frame spike. A typical use is an F key style toggle behind an input action, or flipping it off once a tuning session ends.

## Cost

A game that supplies no profiler runs no diagnostics code at all. A game that supplies one pays a handful of stopwatch reads per frame, about one hundred nanoseconds with zero allocation. The window math and the collection counters run once per window. `Diagnostics.format` allocates, which is why the overlay views format once per window.
