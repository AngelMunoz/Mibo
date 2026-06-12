# Headless Runtime

## Summary

- Added `HeadlessProgram` and `HeadlessRunner` for running the Elmish update loop without graphics, input, or Raylib initialization
- Enables unit testing, server-side simulation, and CLI debugging of game logic
- API mirrors `Program` builder DSL for familiarity

## What's New

### `HeadlessProgram`

A configuration type for running Elmish without graphics:

```fsharp
let program =
  HeadlessProgram.mkHeadless init update
  |> HeadlessProgram.withSubscribe subscribe
  |> HeadlessProgram.withTick Tick
  |> HeadlessProgram.withFixedStep { ... }
```

### `HeadlessRunner`

Provides explicit frame control with virtual time:

```fsharp
use runner = new HeadlessRunner(program)

runner.Dispatch(Increment)
runner.Step(TimeSpan.FromMilliseconds(16))
runner.StepN(10, TimeSpan.FromMilliseconds(16))
runner.StepUntil((fun m -> m.Count >= 100), TimeSpan.FromMilliseconds(16))
```

### Key Differences from RaylibGame

| Aspect | RaylibGame | HeadlessRunner |
|--------|-----------|----------------|
| Window/Graphics | Required | None |
| Time source | Real clock | Virtual (caller-controlled) |
| Input service | Created if `HasInput` | Dispatch-only |
| Assets | Created if configured | Skipped |
| Renderers | Called each frame | None |

## Files Changed

- `src/Mibo.Raylib/Elmish.Headless.fs` — New headless runtime
- `src/Mibo.Raylib.Tests/HeadlessTests.fs` — 20 tests (8 basic + 12 adversarial)
- `src/Mibo.Raylib/Mibo.Raylib.fsproj` — Added file reference
- `src/Mibo.Raylib.Tests/Mibo.Raylib.Tests.fsproj` — Added test file
- `docs/headless.md` — Documentation
- `CHANGELOG.md` — Updated

## Test Coverage

- Basic: Step, StepN, StepUntil, dispatch, tick, fixed step, quit signal
- Adversarial: FrameBounded/Immediate dispatch timing, quit mid-batch, zero/negative delta, maxFrames limit, deferred commands, subscription disposal

## Breaking Changes

None — this is a purely additive feature.
