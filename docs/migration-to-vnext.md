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

> **Coming from the original Mibo (MonoGame)?** See
> [migration-from-monogame.md](migration-from-monogame.md) for a comprehensive
> guide covering every breaking change, API mapping, and a full before/after
> example.

The guiding rule for what lives where: **if it is an interface or contract that
the Program builder, a Runtime host, the Headless runner, or portable user code
needs, its contract goes in Core. Backend-specific implementations and any type
that leaks a backend enum/handle stay in the backend.**

## Phased rollout

| Phase | Scope | Breaking? |
|-------|-------|-----------|
| 1a | Move framework-free files (Time, Commands, System, Subscriptions, Rendering, ProgramTypes) into `Mibo.Core` | No |
| 1b | Input abstraction: Core key/mouse/gamepad/gesture codes, `IInput`/`IInputMapper` contracts + delta types in Core | Yes |
| 1c | `IAssetCache` split: generic asset cache in Core; typed loaders stay backend | No |
| 1d | `Program` builder moves to Core; `withInputMapper` stores a factory instead of calling the backend directly | Yes |
| 2  | Shared `ElmishLoop` extracted; `HeadlessRunner`/`HeadlessProgram` move to Core | No |
| 3  | `Layout` and `Layout3D` move to Core | No |
| 3b | `Cmd<'Msg>` gains `Msg` case; `Cmd.ofMsg` is zero-alloc | Yes |
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

### Phase 1c - `IAssetCache` split

**No breaking changes.** The generic subset of asset caching - the methods that
store arbitrary user-created assets by string key - is now a backend-neutral
contract in `Mibo.Core`:

```fsharp
// Mibo.Elmish (in Mibo.Core)
type IAssetCache =
  abstract Get<'T> : key: string -> 'T voption
  abstract Create<'T> : key: string * factory: (unit -> 'T) -> 'T
  abstract GetOrCreate<'T> : key: string * factory: (unit -> 'T) -> 'T
  abstract Clear: unit -> unit
  abstract Dispose: unit -> unit
```

The raylib backend's `IAssets` now extends `IAssetCache`:

```fsharp
type IAssets =
  inherit IAssetCache
  abstract Texture: path: string -> Texture2D
  abstract Font:     path: string -> Font
  abstract Sound:    path: string -> Sound
  abstract Model:    path: string -> Model
  abstract ModelAnimations: path: string -> ModelAnimation[]
```

All existing code keeps working unchanged: `assets.Get<'T>(...)`,
`assets.GetOrCreate(...)`, etc. resolve to the inherited members. The benefit
is that portable code (and the Headless runner, once it lands in Core) can
retrieve an `IAssetCache` from a `GameContext` and cache custom assets
without referencing a backend:

```fsharp
let cache = GameContext.getService<IAssetCache> ctx
let config = cache.GetOrCreate("gameConfig", fun () -> loadConfig())
```

### Phase 1d - Program builder in Core; `withInputMapper` decoupled

**Breaking** (minor: only affects `withInputMapper` call sites, of which there
are none in the samples). Two changes:

**1. The framework-free `Program` builder moved to `Mibo.Core`.** `mkProgram`,
`withConfig`, `withRenderer`, `withTick`, `withFixedStep`, `withDispatchMode`,
`withSubscription`, `withAssets`, `withAssetsBasePath`, `withInput` now live in
`Mibo.Core` (still `Mibo.Elmish` namespace, still `Program.withX` at call sites).
A new `Program.withServiceRegistration` lets any builder register a callback the
host runs before `Init`.

**2. `withInputMapper` moved to a per-backend module.** Because the mapper factory
(`InputMapper.createService`) is backend-specific, `withInputMapper` can no longer
live in the shared Core `Program` builder. On the raylib backend it is now
`RaylibProgram.withInputMapper` (in `Mibo.Elmish`):

```fsharp
// Before (1.3.0)
program |> Program.withInputMapper inputMap

// After (vNext, raylib backend)
program |> RaylibProgram.withInputMapper inputMap
```

It still registers `IInput` automatically and now registers `IInputMapper` via a
`ServiceRegistrations` callback (rather than wrapping `Init`), so the Core
`Program` type never references the raylib factory. Each backend will expose its
own `withInputMapper` (MonoGame: `MonoGameProgram.withInputMapper`, etc.).

If you used the subscription path (`InputMapper.subscribe` / `subscribeStatic`),
nothing changes — that API is backend-neutral and already lives in Core.

#### Behavioral fix: renderer draw order

**This is a behavioral breaking change that will not produce compiler errors.
Review your renderer setup if you use multiple renderers.**

The `Program.Renderers` list is built by prepending each new renderer.
Previously, the runtime iterated the list without reversing it,
which meant the last renderer added was the first to draw. If you added a 3D
renderer first and a 2D UI renderer second, the 2D UI drew first and the 3D
scene drew on top — the opposite of the expected layering.

This is now fixed: the runtime reverses `program.Renderers` before iterating,
matching the existing pattern for `Config` and `ServiceRegistrations`.
Renderers now draw in the order you add them.

```fsharp
// This now draws the 3D scene first, then the 2D UI on top (correct)
program
|> Program.withRenderer (fun () -> Renderer3D.create view3D)
|> Program.withRenderer (fun () -> Renderer2D.create view2D)
```

### Phase 2 - Shared ElmishLoop; HeadlessRunner/HeadlessProgram in Core

**No breaking changes.** The message-processing core that was duplicated between
`RaylibGame` and `HeadlessRunner` (the dispatch queue, `execCmd`, `updateSubs`,
deferred-effect draining, `FixedStep` accumulation, tick dispatch, and the
message pump) is now a shared `ElmishLoop<'Model,'Msg>` type in `Mibo.Core`.

A `LoopCore<'Model,'Msg>` struct record captures the six fields that define
message-processing behavior (Init/Update/Subscribe/Tick/FixedStep/DispatchMode).
`Program` and `HeadlessProgram` each project to `LoopCore` without changing shape.

`HeadlessProgram`, `HeadlessRunner`, and the `HeadlessProgram` builder module
(`mkHeadless`, `withSubscribe`, `withTick`, `withFixedStep`, `withDispatchMode`,
`withObserver`, `observe`) have moved from the raylib backend to `Mibo.Core`.
They are pure F# with zero backend dependencies, so this is a pure relocation.

All existing user code (`HeadlessProgram.mkHeadless`, `HeadlessRunner`, etc.)
keeps working unchanged — the types stay in the `Mibo.Elmish` namespace.
The `PingPong` server sample, which relies on `HeadlessRunner`, continues to work.

### Phase 3 - `Layout` and `Layout3D` move to Core

**No breaking changes.** The layout geometry modules have moved from the raylib
backend into `Mibo.Core`. Both namespaces (`Mibo.Layout`, `Mibo.Layout3D`) are
preserved, so `open Mibo.Layout` / `open Mibo.Layout3D` continue to resolve
exactly as before — all existing game code compiles unchanged.

What moved (17 files, all pure F# over `System.Numerics`):
- `Mibo.Layout` (9 files): `Grid2D`, `HexGrid`, `Spatial2D`, `HexLayout`,
  `LayeredHex`, `Layout`, `Platformer`, `TopDown`, `Layered`.
- `Mibo.Layout3D` (8 files): `Grid3D`, `HexGrid3D`, `Spatial3D`, `Layout3D`,
  `HexLayout3D`, `LayeredHex3D`, `Interior`, `Terrain`.

What stays in the raylib backend:
- `Layout3D/Renderer3D.fs` — the instanced-draw bridge (`InstancedRenderContext`,
  `CellGridRenderer3D`, `HexGrid3DRenderer`). It depends on
  `Mibo.Elmish.Graphics3D` (`Command3D`/`RenderBuffer3D`/`Material3D`) and the
  native `Raylib_cs.Mesh`, so it is a renderer and stays backend-side (in
  `namespace Mibo.Layout3D`, same as today). Moving it would require first
  abstracting the 3D command buffer into Core, which is out of scope.

The benefit: a fresh backend (e.g. MonoGame) now gets the full layout/hex/spatial
geometry surface from `Mibo.Core` without re-implementing it, and only has to
provide its own renderer bridge if it wants instanced grid drawing.

### Phase 3b — Zero-alloc `Cmd.ofMsg` (`Msg` case)

**Breaking.** The `Cmd<'Msg>` discriminated union has a new `Msg of 'Msg` case
between `Empty` and `Single`. This eliminates the delegate allocation that
`Cmd.ofMsg` previously incurred.

#### What changed

`Cmd.ofMsg` now returns `Msg msg` directly instead of wrapping the message in
an `Effect` delegate:

```fsharp
// Before (1.3.0)
let inline ofMsg(msg: 'Msg) : Cmd<'Msg> =
  Single(Effect<'Msg>(fun dispatch -> dispatch msg))  // allocates

// After (vNext)
let inline ofMsg(msg: 'Msg) : Cmd<'Msg> = Msg msg  // zero-alloc
```

The runtime dispatches `Msg` directly without invoking a delegate:

```fsharp
// In execCmd (ElmishLoop)
| Msg msg -> dispatch msg  // direct call, no delegate overhead
```

`Cmd.map` on a `Msg` stays allocation-free:

```fsharp
// Before: map on Single(Effect(...)) allocates a new Effect
// After: map on Msg msg returns Msg(f msg) — no allocation
| Msg msg -> Msg(f msg)
```

`batch` and `batch2` preserve the `Msg` case in their fast paths (when the
batch contains a single immediate command, the result is `Msg` rather than
`Single`).

#### Migration: exhaustive pattern matches on `Cmd<'Msg>`

If you pattern-match on `Cmd<'Msg>`, add the new case:

```fsharp
// Before
match cmd with
| Empty -> ...
| Single eff -> ...
| Batch effs -> ...
| DeferNextFrame effs -> ...
| NowAndDeferNextFrame(now, next) -> ...
| Quit -> ...

// After
match cmd with
| Empty -> ...
| Msg msg -> ...          // NEW: direct message dispatch
| Single eff -> ...
| Batch effs -> ...
| DeferNextFrame effs -> ...
| NowAndDeferNextFrame(now, next) -> ...
| Quit -> ...
```

If you have a wildcard match (`| _ ->`), no change is needed.

#### Migration: tests that assert on `Cmd.ofMsg` results

Tests that match on `Single` from `Cmd.ofMsg` will now see `Msg`:

```fsharp
// Before
let cmd = Cmd.ofMsg 42
match cmd with
| Single eff -> eff.Invoke(fun x -> result <- x)  // worked
| _ -> failwith "expected Single"

// After
let cmd = Cmd.ofMsg 42
match cmd with
| Msg msg -> result <- msg  // direct, no invoke needed
| _ -> failwith "expected Msg"
```

#### Why this matters

`Cmd.ofMsg` is one of the most frequently called functions in Mibo games —
every `update` branch that returns a follow-up message uses it. The previous
implementation allocated an `Effect<'Msg>` delegate and a closure on every call.
The new `Msg` case eliminates both allocations, reducing GC pressure in the
hot path.





