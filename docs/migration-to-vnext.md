# Migrating to Mibo vNext

> **Status: In progress.** This document tracks the work happening on the `vnext`
> branch to make Mibo backend-agnostic, and collects **every breaking change** a
> user will face when moving from the current `1.3.0` (raylib-only) release to the
> upcoming major version. It is updated as each phase lands — if a phase is not
> listed here, it has not shipped yet.
>
> If you are tracking `vnext`, read this document first whenever you pull.

## What vNext is

Mibo is being split into a backend-agnostic core and pluggable backends:

```
Mibo.Core          ← the shared core (Cmd, Sub, GameTime, DispatchMode, FixedStep,
                     System, RenderBuffer, IRenderer, GameContext, Program,
                     HeadlessProgram, IInput/IInputMapper contracts, IAssetCache,
                     the shared ElmishLoop, Layout, Layout3D)
Mibo.Raylib        ← the raylib backend (Runtime host, Input/Assets impls, Graphics2D/3D)
Mibo.MonoGame      ← a fresh MonoGame backend (Runtime host + Input/Assets impls; no
                     default renderers yet — you implement IRenderer<'Model>)
```

Mibo.Raylib is the authoritative source: the Core types are the raylib types
generalized, and the MonoGame backend is written from scratch against Core.

The guiding rule for what lives where: **if it is an interface or contract that
the Program builder, a Runtime host, the Headless runner, or portable user code
needs, its contract goes in Core. Backend-specific implementations and any type
that leaks a backend enum/handle stay in the backend.**

## Phased rollout

| Phase | Scope | Breaking? |
|-------|-------|-----------|
| 1a | Move framework-free files (Time, Commands, System, Subscriptions, Rendering, ProgramTypes) into `Mibo.Core` | No |
| 1b | Input abstraction: Core key/mouse/gamepad/gesture codes, `IInput`/`IInputMapper` contracts + delta types in Core | Yes |
| 1c | `IAssetCache` split: generic asset cache in Core; typed loaders stay backend | Yes |
| 1d | `Program` builder moves to Core; `withInputMapper` stores a factory instead of calling the backend directly | Yes |
| 2  | Shared `ElmishLoop` extracted; `HeadlessRunner`/`HeadlessProgram` move to Core | No |
| 3  | `Layout` and `Layout3D` move to Core | No |
| 4  | Fresh `Mibo.MonoGame` backend | n/a (new project) |

## Breaking changes

> Sections are added as the phase that introduces them lands. Each entry lists
> what changed, why, and exactly how to update your code.

### Phase 1a — `Mibo.Core` extraction

**No breaking changes.** Six files moved verbatim from `Mibo.Raylib` into a new
`Mibo.Core` project:

- `Elmish.Time.fs` (`DispatchMode`, `GameTime`, `FixedStepConfig`, `FixedStep`)
- `Elmish.Commands.fs` (`Effect<'Msg>`, `Cmd<'Msg>`, `Cmd` module)
- `Elmish.System.fs` (`System` module)
- `Elmish.Subscriptions.fs` (`SubId`, `Sub<'Msg>`, `Sub` module, etc.)
- `Elmish.Rendering.fs` (`GameContext`, `IRenderer<'Model>`, `RenderBuffer<_,_>`)
- `Elmish.ProgramTypes.fs` (`GameConfig`, `Program<_,_>`, `GameConfig` module)

All of these stay in the `Mibo.Elmish` namespace, so `open Mibo.Elmish` continues
to resolve them exactly as before. No code changes are required for existing games.

**Project-level note:** if you reference `Mibo.Raylib` source directly via
`<Compile Include>` (atypical — you normally take the NuGet package), be aware
that these six files no longer live in the `Mibo.Raylib` project. Consumers of
the package are unaffected.

### Phase 1b — Backend-neutral input types

**Breaking.** The input surface has been generalized so the contracts work on
any backend. raylib's native types no longer leak into the public input API.

#### What moved to `Mibo.Core` (still namespace `Mibo.Input`)

The `IInput` contract, the delta types, the subscription modules
(`Keyboard`/`Mouse`/`Touch`/`Gamepad`/`Gesture`), `Input.getService`/
`tryGetService`, the `Trigger` DU, `InputMap<'Action>`, `ActionState<'Action>`,
`IInputMapper<'Action>`, and `InputMapper.getService`/`tryGetService` all live
in `Mibo.Core` now. `open Mibo.Input` keeps working.

The raylib backend retains only: the `IInput` *implementation* (`Input.create`)
and the `IInputMapper` *implementation* (`InputMapper.createService`), plus the
native↔neutral translation functions documented below.

#### New backend-neutral code DUs (struct DUs, `RequireQualifiedAccess`)

Four new struct DUs replace direct use of `Raylib_cs.KeyboardKey`,
`MouseButton`, `GamepadButton`, and `Gesture`:

- `Mibo.Input.KeyCode` — keyboard keys. Includes an `Unknown` case.
- `Mibo.Input.MouseButtonCode` — `Left`, `Right`, `Middle`, `Extra1`–`Extra4`, `Unknown`.
- `Mibo.Input.GamepadButtonCode` — face buttons, shoulders, triggers, sticks, D-pad, `Unknown`.
- `Mibo.Input.GestureKind` — `Tap`, `DoubleTap`, `Hold`, `Drag`, `Swipe*`, `Pinch`, `Unknown`.
  (Note: there is no `None` case — "no gesture" is expressed with `voption`.)

These are `[<RequireQualifiedAccess>]`. Always write `KeyCode.W`, not bare `W`.

#### Migration: code that bound keys via `InputMap.key`

Before (raylib-only):

```fsharp
open Raylib_cs

let map =
  InputMap.empty
  |> InputMap.key MoveLeft KeyboardKey.A
  |> InputMap.key MoveLeft KeyboardKey.Left
  |> InputMap.key Jump KeyboardKey.Space
```

After (backend-neutral):

```fsharp
// open Raylib_cs is no longer required for key bindings
let map =
  InputMap.empty
  |> InputMap.key MoveLeft KeyCode.A
  |> InputMap.key MoveLeft KeyCode.Left
  |> InputMap.key Jump KeyCode.Space
```

Same shape, just `KeyboardKey.X` → `KeyCode.X`. A quick way to migrate is a
search-and-replace of `KeyboardKey.` → `KeyCode.` across your codebase, then
fix the few cases where the name changed (notably `KeyboardKey.Zero`/`One`/…
→ `KeyCode.D0`/`D1`/…).

#### Migration: the `Trigger` DU

The `Trigger` DU changed cases (it now uses Core codes instead of native types):

| Before (raylib)                                | After (Core)                                       |
|------------------------------------------------|----------------------------------------------------|
| `Key of KeyboardKey`                           | `Key of KeyCode`                                   |
| `KeyCombo of Set<KeyboardKey>`                 | `KeyCombo of Set<KeyCode>`                         |
| `MouseBut of int`                              | `MouseButton of MouseButtonCode`                   |
| `GamepadBut of player: int * button: GamepadButton` | `GamepadButton of player: int * button: GamepadButtonCode` |

`InputMap.mouse` now takes a `MouseButtonCode` instead of an `int`:

```fsharp
// Before
|> InputMap.mouse Jump 0

// After
|> InputMap.mouse Jump MouseButtonCode.Left
```

#### Migration: `MouseDelta` / handler signatures

`MouseDelta.Buttons` now holds `MouseButtonCode[]` (not `MouseButton[]`), and
`Mouse.onButton`/`onLeftClick`/`onRightClick`/etc. expose `MouseButtonCode`.
If you pattern-matched on `MouseButton.Left` etc. in a handler, switch to
`MouseButtonCode.Left`.

#### Native↔neutral translation (raylib backend only)

The raylib backend exposes translation modules in `Mibo.Input`:

- `KeyCode.ofRaylibKey` / `KeyCode.toRaylibKey`
- `MouseButtonCode.ofRaylibButton` / `MouseButtonCode.toRaylibButton`
- `GamepadButtonCode.ofRaylibButton` / `GamepadButtonCode.toRaylibButton`
- `GestureKind.ofRaylibGesture` / `GestureKind.toRaylibGesture`

Use these when you need to call a raylib function that takes a native enum from
Mibo-side code that works in Core codes (e.g. `Raylib.IsKeyDown(KeyCode.toRaylibKey k)`).

#### Notes on round-tripping

- raylib's `GamepadButton` enum names the D-pad cluster "left face"
  (`LeftFaceUp/Down/Left/Right`) and the action-button cluster "right face"
  (`RightFaceUp/Down/Left/Right`, i.e. Y/B/A/X on Xbox). The raylib backend
  maps `LeftFace*` to `GamepadButtonCode.DPad*` and `RightFace*` to `Face*`.
- Any native input with no logical Core case maps to `Unknown`. Do not assume
  `Unknown` round-trips to the same native value.

