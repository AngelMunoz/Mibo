---
title: Headless Mode
category: Architecture
categoryindex: 1
index: 5
---

# Headless Mode

The headless runtime runs your Elmish update loop without graphics, input polling, or Raylib initialization. Use it for unit testing, server-side simulation, and CLI debugging.

## Core Definition

Start with `HeadlessProgram.mkHeadless init update` — same `Init` and `Update` signatures as `Program`, but without renderers or window configuration.

```fsharp
let program =
  HeadlessProgram.mkHeadless init update
  |> HeadlessProgram.withSubscribe subscribe
  |> HeadlessProgram.withTick Tick

use runner = new HeadlessRunner(program)
```

## Building a Program

`HeadlessProgram` supports the same builder DSL as `Program`:

| Function | Description |
|----------|-------------|
| `mkHeadless init update` | Create a program with init and update functions |
| `withSubscribe` | Add a subscription function |
| `withTick` | Add a per-frame tick message |
| `withFixedStep` | Enable framework-managed fixed timestep |
| `withDispatchMode` | Set `Immediate` or `FrameBounded` dispatch |

## Running the Simulation

`HeadlessRunner` provides explicit frame control with virtual time:

```fsharp
use runner = new HeadlessRunner(program)

// Advance one frame
runner.Step(TimeSpan.FromMilliseconds(16))

// Advance multiple frames
runner.StepN(10, TimeSpan.FromMilliseconds(16))

// Run until a condition is met
let met = runner.StepUntil(
    (fun model -> model.Count >= 100),
    TimeSpan.FromMilliseconds(16))
```

## Dispatching Messages

Send messages into the runner from outside the update loop:

```fsharp
runner.Dispatch(Increment)
runner.DispatchMany [ Increment; Increment; Increment ]
```

This is useful for:
- Simulating input in tests
- Feeding network messages in server scenarios
- Driving the simulation from external sources

## Accessing State

```fsharp
let model = runner.Model        // Current model state
let time = runner.TotalTime     // Elapsed virtual time
let quit = runner.ShouldQuit    // Whether Quit was signaled
```

## Time Control

Unlike `RaylibGame` which uses real wall-clock time, `HeadlessRunner` uses virtual time controlled by the caller:

```fsharp
// Each Step advances virtual time by the given elapsed
runner.Step(TimeSpan.FromMilliseconds(16))  // ~60fps
runner.Step(TimeSpan.FromSeconds(1))        // 1 second

// TotalTime accumulates across steps
runner.Step(TimeSpan.FromMilliseconds(100))
runner.Step(TimeSpan.FromMilliseconds(200))
// runner.TotalTime.TotalSeconds = 0.3
```

## Fixed Step with Headless

`withFixedStep` works identically to the graphical runtime. The runner accumulates time and dispatches fixed-step messages at the configured rate:

```fsharp
let program =
  HeadlessProgram.mkHeadless init update
  |> HeadlessProgram.withFixedStep {
    StepSeconds = 1f / 60f
    MaxStepsPerFrame = 5
    MaxFrameSeconds = ValueSome 0.25f
    Map = PhysicsTick
  }

use runner = new HeadlessRunner(program)
runner.Step(TimeSpan.FromMilliseconds(16))
```

## DispatchMode

Control when dispatched messages are processed:

- **`Immediate`** (default): Messages dispatched during `Update` are processed in the same frame. Good for responsive updates.
- **`FrameBounded`**: Messages dispatched during `Update` are deferred to the next `Step` call. Prevents re-entrant updates.

```fsharp
let program =
  HeadlessProgram.mkHeadless init update
  |> HeadlessProgram.withDispatchMode FrameBounded
```

## Subscriptions

Subscriptions work the same as in the graphical runtime — they start, stop, and restart based on model changes:

```fsharp
let subscribe (ctx: GameContext) (model: Model) =
    if model.IsActive then
        Sub.batch [
            // Subscriptions work without graphics
        ]
    else
        Sub.none

let program =
  HeadlessProgram.mkHeadless init update
  |> HeadlessProgram.withSubscribe subscribe
```

## Cleanup

`HeadlessRunner` implements `IDisposable`. Disposing it cleans up active subscriptions:

```fsharp
use runner = new HeadlessRunner(program)
// ... run simulation ...
// Subscriptions are disposed when runner goes out of scope
```

## Example: Unit Testing

```fsharp
open Mibo.Elmish

type Msg = Increment | Decrement
type Model = { Count: int }

let init _ctx = struct ({ Count = 0 }, Cmd.none)

let update msg model =
  match msg with
  | Increment -> struct ({ model with Count = model.Count + 1 }, Cmd.none)
  | Decrement -> struct ({ model with Count = model.Count - 1 }, Cmd.none)

[<Test>]
let ``increment increases count`` () =
  use runner =
    HeadlessProgram.mkHeadless init update
    |> HeadlessRunner

  runner.Dispatch(Increment)
  runner.Step(TimeSpan.FromMilliseconds(16))

  Assert.Equal(1, runner.Model.Count)
```

## Example: Server Simulation

```fsharp
let program =
  HeadlessProgram.mkHeadless init update
  |> HeadlessProgram.withSubscribe subscribe
  |> HeadlessProgram.withFixedStep {
    StepSeconds = 1f / 20f  // 20 ticks/sec
    MaxStepsPerFrame = 4
    MaxFrameSeconds = ValueSome 0.5f
    Map = GameTick
  }

use runner = new HeadlessRunner(program)

// Simulate 10 seconds of game time
while not runner.ShouldQuit do
  runner.Step(TimeSpan.FromMilliseconds(50))

  // In a real server, you'd receive client inputs here
  // runner.Dispatch(ClientInput(clientId, input))
```
