---
title: Migrating to Mibo v2
category: Migrating
categoryindex: 2
index: 1
---

# Migrating to Mibo v2

This document collects **every breaking change** you will face when moving from
the `1.3.0` (raylib-only) release to Mibo v2 — a backend-agnostic core with
pluggable raylib and MonoGame backends. Work through the sections that match
your code; each entry lists what changed, why, and exactly how to update your
code.

## What v2 is

Mibo is being split into a backend-agnostic core and pluggable backends:

```
Mibo.Core          ← the shared core (Cmd, Sub, GameTime, DispatchMode, FixedStep,
                     System, RenderBuffer, IRenderer, GameContext, Program,
                     HeadlessProgram, IInput/IInputMapper contracts, IAssetCache,
                     the shared ElmishLoop, Layout, Layout3D)
Mibo.Raylib        ← the raylib backend (Runtime host, Input/Assets impls, Graphics2D/3D)
Mibo.MonoGame      ← a fresh MonoGame backend (Runtime host + Input/Assets impls, full
                     Graphics2D + Graphics3D renderers with a Forward PBR pipeline, shadow
                     atlas, post-processing, and built-in HLSL shaders)
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
| 4  | Shared types extracted to Core: `Mibo.Color`, `Light3D` definitions, `Animation3DState` state machine, `MouseCapture` | Yes |

## Breaking changes

Each entry below lists what changed, why, and exactly how to update your code.

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

// After (v2, raylib backend)
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

// After (v2)
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

### Phase 4 — Shared types to Core (`Mibo.Color`, `Light3D`, `Animation3DState`, `MouseCapture`)

**Breaking.** The last major batch of backend-duplicated types has been promoted
into `Mibo.Core` so there is a single implementation. The guiding rule: **if the
type is identical across both backends (or can be made backend-neutral with a
conversion at the boundary), it belongs in Core.**

The changes fall into four groups.

#### 4a — `Mibo.Color` (backend-neutral color)

A new `[<Struct>]` byte RGBA color lives in `Mibo.Core`. It replaces the
backend-specific `Color` (`Raylib_cs.Color` / `Microsoft.Xna.Framework.Color`)
wherever it is used as a *contract* — currently in the shared light definitions
(Phase 4b). Rendering code still uses native colors for anything that touches a
backend draw call directly.

```fsharp
// Mibo.Core (namespace Mibo)
[<Struct>]
type Color =
  val R: byte; val G: byte; val B: byte; val A: byte
  static member White  = Color(255uy, 255uy, 255uy, 255uy)
  static member Black  = Color(0uy,   0uy,   0uy,   255uy)
  // Red, Green, Blue, SkyBlue, Orange, …
  member toVector3: unit -> Vector3  // [r;g;b] / 255.0f
  member toVector4: unit -> Vector4  // [r;g;b;a] / 255.0f
```

Each backend ships a conversion module and an implicit conversion:

- raylib: `Mibo.RaylibColor.toRaylibColor` / `fromRaylibColor` (+ `op_Implicit`)
- MonoGame: `Mibo.MonoGameColor.toMonoGameColor` / `fromMonoGameColor` (+ `op_Implicit`)

> _**F# note**_: `op_Implicit` works for passing values that take an implicit
> conversion, but **cannot be called as `.op_Implicit(x)`** in F# syntax. Use the
> named module function (`Mibo.MonoGameColor.toMonoGameColor c`) when you need an
> explicit conversion.

#### 4b — Light definitions move to Core

`AmbientLight3D`, `DirectionalLight3D`, `PointLight3D`, `SpotLight3D`, and their
builder modules previously existed as **byte-for-byte identical copies** in both
backends. They now live once in `Mibo.Core/Graphics3D/Light3D.fs` and use
`Mibo.Color` + `System.Numerics.Vector3`.

**Migration:**

| Field | Before | After |
|-------|--------|-------|
| `*.Color` | native `Color` | `Mibo.Color` |
| `*.Direction` (MonoGame backend) | `Microsoft.Xna.Framework.Vector3` | `System.Numerics.Vector3` |
| `*.Position` (MonoGame backend) | `Microsoft.Xna.Framework.Vector3` | `System.Numerics.Vector3` |

```fsharp
// Before (raylib-only or duplicated)
open Raylib_cs
let light = DirectionalLight3D.create (Vector3(0.3f, -0.7f, -0.5f))
            |> DirectionalLight3D.withColor Color.White

// After (any backend — the light definition is in Core)
open Mibo   // Mibo.Color lives here
let light = DirectionalLight3D.create (Vector3(0.3f, -0.7f, -0.5f))
            |> DirectionalLight3D.withColor Mibo.Color.White
```

> _**MonoGame note**_: MonoGame types interop freely with `System.Numerics`
> via `op_Implicit` / `ToNumerics()`, so passing an `XNA.Vector3` where a
> `System.Numerics.Vector3` is expected just works at the call site.

#### 4c — `Animation3DState` playback clock in Core

The pure state machine that drives 3D skeletal animation playback
(`create`, `play`, `blendTo`, `update`, etc.) was line-for-line identical across
both backends — only the underlying clip data type differed. It now lives in
`Mibo.Core/Animation3D.fs`.

- **New:** `Animation3DClipsInfo` (clip names + keyframe counts) — built at load
  time from each backend's native clip data. Each backend's `Animation3DClips`
  gains a `ClipsInfo: Animation3DClipsInfo` field.
- **Delegation:** both backends' `Animation3DState` delegate playback to the Core
  state machine via inlineable struct-mapping helpers — **zero hot-path cost**
  (all inline struct copies).
- **Bug fix:** `update` blend target wrapping now respects `Loop = false`
  consistently on both backends (previously raylib always wrapped the blend
  target regardless of the loop flag).

**Migration:** the public API (`Animation3DState.create`/`play`/`blendTo`/`update`,
etc.) is unchanged at call sites. Construct `Animation3DClips` via the backend
module functions — do not construct the struct literal directly (the
`ClipsInfo` field is internal and populated by the loader).

#### 4d — `MouseCapture` + `IInput.SetMouseCapture`

A new `MouseCapture` DU (`Free` / `Captured`) and `IInput.SetMouseCapture` method
let games request pointer-locked, unlimited-rotation mouse input via a
backend-neutral contract. Previously this required backend-specific hacks
(raylib: call `DisableCursor` yourself; MonoGame: a user-authored
`GameComponent` to re-center the mouse).

```fsharp
// Before (raylib-only)
open Raylib_cs
Raylib.DisableCursor()

// After (any backend)
let input = Input.getService ctx
input.SetMouseCapture(MouseCapture.Captured)
```

- raylib: native `DisableCursor` / `EnableCursor`.
- MonoGame: re-centers the mouse inside its own `Poll()` (edge-based, to avoid
  WinForms message-pump jitter). The external `GameComponent` hacks
  (`CursorClampComponent` / `MouseCenterComponent`) are no longer needed.

#### 4e — raylib camera changes

**Breaking (raylib only).** The raylib camera surface has two breaking changes
in v2.

**1. Dead `Camera`/`Ray` struct types removed; helpers return native types.**
The raylib backend used to carry a `Mibo.Camera` struct (view + projection
`Matrix4x4`) and a `Mibo.Ray` struct, with `Camera3D.lookAt` / `orbit` /
`screenPointToRay` / `fromRaylib` built on top of them. Raylib never used these
for rendering — it renders through the native `Raylib_cs.Camera3D` — so the
structs and the `fromRaylib` converter are removed. The constructor helpers
stay, but now return **native raylib types** so they compose directly with
raylib's own APIs:

- `Camera3D.lookAt` / `orthographic` / `orbit` → `Raylib_cs.Camera3D`
  (FOV in **degrees**, raylib's convention).
- `Camera3D.screenPointToRay` → `Raylib_cs.Ray` (wraps
  `Raylib.GetScreenToWorldRay`).
- Removed: `Mibo.Camera`, `Mibo.Ray`, `Camera3D.fromRaylib`.

Both backends now expose the same `Camera3D` constructor surface. On MonoGame
these return the `Camera` view/projection struct (FOV in radians, with explicit
near/far/aspect); on raylib they return the native structs above.

**2. 2D camera readers take the camera by reference.** `Camera2D.viewportBounds`,
`screenToWorld`, and `worldToScreen` now take the camera by read-only reference
(`inref`) to avoid copying the native struct on every call. Pass it with `&`:

```fsharp
// Before
let world = Camera2D.screenToWorld camera mousePos
let bounds = Camera2D.viewportBounds camera w h

// After
let world = Camera2D.screenToWorld &camera mousePos
let bounds = Camera2D.viewportBounds &camera w h
```

`Camera3D.screenPointToRay` takes the camera the same way
(`Camera3D.screenPointToRay &camera mousePos`). The movement helpers
`Camera2D.smoothFollow` / `clampTarget` already took the camera by `byref`
(they mutate it), so they're unchanged.

> _**MonoGame note**_: MonoGame's camera fields are immutable, so its
> `smoothFollow` / `clampTarget` return a new camera rather than mutating in
> place, and none of its helpers use `&`. The two backends otherwise expose the
> same camera operations.

**Migration (raylib):** if you held a `Mibo.Camera` or `Mibo.Ray` value, switch
to the native `Raylib_cs.Camera3D` / `Raylib_cs.Ray` (produced by
`Camera3D.lookAt` / `orbit` / `screenPointToRay`). Add `&` at your
`viewportBounds` / `screenToWorld` / `worldToScreen` / `screenPointToRay` call
sites. Code that already used `Raylib_cs.Camera3D` directly is otherwise
unaffected.

---

## Post-processing and shadow refinements

### Post-processing is now command-driven (breaking)

**Breaking (2D and 3D).** In 1.x, post-processing was configured once at
renderer/pipeline construction and ran **every frame for the life of the
renderer** — whether or not the effect was needed that frame:

- **2D:** `Renderer2DConfig.PostProcess: PostProcessPass[] voption`, where
  `PostProcessPass = { Shader; OnSetup }`.
- **3D:** `PostProcessConfig3D` / `PostProcessPass3D` (`{ Shader; OnSetup }`),
  passed to the pipeline constructor.

These config types have been removed. Post-processing is now **on-demand and
model-aware**: the view emits a post-process command into the render buffer, and
the action runs only when (and only on the frames) it is emitted — so a
hit-flash, a pause vignette, or a cutscene grade costs nothing on the frames it
isn't drawn.

```fsharp
// 2D — v1, declared once, ran every frame whether needed or not
let renderer =
  Renderer2D.createWithConfig
    { PostProcess =
        ValueSome
          [|
            {
              Shader = vignetteShader
              OnSetup = ValueSome(fun shader ctx -> ...)
            }
          |]
      ClearColor = ValueNone }
    view

// 2D — v2, emitted from the view, only on frames that need it
let view ctx model (buffer: RenderBuffer2D) =
  buffer
  |> Draw.postProcess (fun ppCtx ->
      // ppCtx.Source — the scene texture (or previous pass's output)
      // ppCtx.Width, ppCtx.Height, ppCtx.Time — dimensions + frame time
      drawFullscreenQuad ppCtx.Source vignetteShader)
```

```fsharp
// 3D — v1, declared once, ran every frame whether needed or not
let pipeline =
  ClusteredForwardPipeline(
    postProcess = {
      Passes =
        ValueSome
          [|
            {
              Shader = vignetteShader
              OnSetup = ValueSome(fun shader ctx -> ...)
            }
          |]
    }
  )

// 3D — v2, emitted from the view, only on frames that need it
let view ctx model (buffer: RenderBuffer3D) =
  buffer
  |> Draw3D.postProcess (fun ppCtx ->
      // ppCtx.Source — the scene texture (or previous pass's output)
      // ppCtx.Width, ppCtx.Height, ppCtx.Time — dimensions + frame time
      // ppCtx.Context — GameContext (resolve a shader via IAssets)
      drawFullscreenQuad ppCtx.Source vignetteShader)
  |> Draw3D.drop
```

Multiple passes chain in buffer order, ping-ponging through pooled render
targets with the last pass drawing to the back-buffer. Resolve the shader inside
the action (e.g. via `IAssets`) rather than capturing a renderer/pipeline-wide
one — the action owns the draw.

### Depth-aware post-processing (3D)

A second command, `Draw3D.postProcessWithDepth`, gives the action a
`PostProcessContext3D` whose `Depth` field is a camera-POV depth texture (NDC z
in `[0,1]`) for distance effects like fog, depth-of-field, and SSAO. Use plain
`Draw3D.postProcess` when you don't sample depth — the pipeline skips the
depth-production cost entirely:

```fsharp
buffer
|> Draw3D.postProcessWithDepth (fun ppCtx ->
    match ppCtx.Depth with
    | ValueSome depthTex -> // bind depthTex, apply the distance effect
    | ValueNone ->          // no depth produced this frame — pass through
    ())
|> Draw3D.drop
```

The depth texture is produced differently per backend (raylib samples the
forward pass's depth attachment directly; MonoGame re-renders opaque geometry
into a dedicated R32F target), but the contract — single-channel NDC z, `0` =
near, `1` = far, skybox/uncovered = `1.0` — is identical on both. See the
[3D Rendering Overview](graphics3d/overview.html) for the linearization formula.

### 2D post-process context enrichment

The 2D `PostProcessContext2D` now exposes two fields a post-process shader can
read: the active `LightContext2D` (point lights, directional lights, ambient,
occluders) and the last active `Camera2D`. No new command variants — the
existing `Draw.postProcess` action closure just receives a richer context:

```fsharp
Draw.postProcess (fun ctx ->
  // ctx.Lights — the active LightContext2D (ValueNone when no lit sprites were drawn).
  //   Read ctx.Lights.Value.PointLights, .DirLights, .Ambient, .Occluders
  //   to drive bloom thresholds, light-tinted grading, etc.

  // ctx.Camera — the last active Camera2D (ValueNone when no BeginCamera block was used).
  //   Use it to convert between screen UVs and world coordinates for world-anchored effects.

  // ctx.Source, ctx.Width, ctx.Height, ctx.Time — unchanged
  ()) buffer
```

**Multi-camera caveat:** `Camera` is the **last** `Camera2D` active during the
scene render. When multiple camera blocks exist in the same frame (e.g. main
view + minimap), the scene render target contains all of them composited — a
single camera reference can't reconstruct per-camera regions. Use
`DrawImmediate` for per-camera post-processing.

**No depth in 2D:** 2D rendering does not write a depth buffer on either
backend, so there is no `PostProcessWithDepth` for 2D. If you need depth-like
data for a 2D effect (e.g. fake DOF from layer ordering), render a custom R32F
target via `DrawImmediate` and pass it through your action's closure.

### Point-light shadow direction

Point-light shadows previously rendered a single face looking straight down
(−Y), no matter where the light was or what geometry surrounded it.
`PointLight3D` now has a `ShadowDirection: Vector3 voption` field so you can aim
the shadow map toward your geometry:

```fsharp
// Ceiling light — default, looks down (no change needed)
let ceiling = PointLight3D.create(pos, 10.0f) |> PointLight3D.withCastsShadows true

// Wall sconce — aim the shadow map along +X
let sconce =
  PointLight3D.create(pos, 8.0f)
  |> PointLight3D.withCastsShadows true
  |> PointLight3D.withShadowDirection Vector3.UnitX
```

**Breaking (minor):** if you construct `PointLight3D` via a struct literal
(`{ Position = ...; ... }`), add `ShadowDirection = ValueNone`. If you use
`PointLight3D.create` + builder functions, no change is needed.

### Single directional-light shadow

The forward PBR pipeline lights and shadows **only the first directional
light**. The shader uses scalar (non-array) directional-light uniforms, so a
second `AddDirectionalLight` with `CastsShadows = true` is silently ignored by
the shader. This is now documented on `DirectionalLight3D.CastsShadows`.





