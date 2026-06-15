# Mibo vNext — Backend Abstraction Plan

> This is the **execution plan** for making Mibo backend-agnostic. It tracks
> what is done, what remains, and the decisions behind each phase. The
> user-facing breaking-changes live in [`docs/migration-to-vnext.md`](docs/migration-to-vnext.md);
> this document is the engineering record.
>
> **Branch:** `vnext`. **Status:** Phases 1a–1d and 2 are merged.
>
> **Workflow:** `vnext` is the integration branch. Each phase is developed on a
> feature branch cut from `vnext` and merged back via a PR to `vnext`
> (e.g. `feature/core-and-input-extraction` → PR → `vnext`). Never push without
> permission; never force push.

## Goal

Split Mibo into a backend-agnostic core and pluggable backends:

```
Mibo.Core          ← shared core (Cmd/Sub/System/GameTime/RenderBuffer/Program/
                     IRenderer/GameContext/ElmishLoop/HeadlessRunner/IInput/
                     IInputMapper/IAssetCache/Layout/Layout3D)
Mibo.Raylib        ← the raylib backend (Runtime host, Input/Assets impls,
                     Graphics2D/3D, Command2D/3D, Pipelines)
Mibo.MonoGame      ← a fresh MonoGame backend
```

**Guiding rule:** if it is an interface or contract that the Program builder, a
Runtime host, the Headless runner, or portable user code needs, its contract
goes in Core. Backend-specific implementations and any type that leaks a
backend enum/handle stays in the backend.

## Phase status

| Phase | Scope | Breaking? | Status |
|-------|-------|-----------|--------|
| 1a | Move framework-free files (Time, Commands, System, Subscriptions, Rendering, ProgramTypes) into `Mibo.Core` | No | ✅ Done (`#20`) |
| 1b | Input abstraction: Core key/mouse/gamepad/gesture codes, `IInput`/`IInputMapper` contracts + delta types in Core | Yes | ✅ Done (`#20`) |
| 1c | `IAssetCache` split: generic asset cache in Core; typed loaders stay backend | No | ✅ Done (`#21`) |
| 1d | `Program` builder moves to Core; `withInputMapper` decoupled from the backend | Yes | ✅ Done (`#21`) |
| 2  | Shared `ElmishLoop` extracted; `HeadlessRunner`/`HeadlessProgram` move to Core | No | ✅ Done (`#22`) |
| 3  | `Layout` and `Layout3D` move to Core | No | ⬜ Pending |
| 4  | Fresh `Mibo.MonoGame` backend | n/a (new project) | ⬜ Pending |
| 5  | Parity verification (optional) | n/a | ⬜ Pending |

---

## Phase 3 — Move `Layout` and `Layout3D` to Core

**Breaking:** No. Both namespaces (`Mibo.Layout`, `Mibo.Layout3D`) are preserved.

### Verification (done during planning)

Every layout file's `open` list was audited against `src/Mibo.Raylib`. Confirmed:
- **All 9 `Layout/` files** depend only on `System.Numerics`, `System.Collections.Generic`,
  `System.Buffers`, and each other (`CellGrid2D`, `HexGrid`).
- **8 of 9 `Layout3D/` files** are equally clean (`System.Numerics`, plus internal
  `CellGrid3D`/`HexGrid3D`/`Mibo.Layout`).
- **Nothing in the raylib backend outside the Layout folders references layout types**
  — so removing the 17 files from `Mibo.Raylib.fsproj` breaks no compile order.
- **Consumers are namespace-only**: `PlatformerSample` (`open Mibo.Layout`),
  `ThreeDSample` (`open Mibo.Layout3D`, `Mibo.Layout3D.CellGrid3D<BlockType>`,
  `Mibo.Layout3D.BoundingBox`), and the test suite (`open Mibo.Layout`/`Mibo.Layout3D`).
  Namespace preservation means all compile unchanged.

### What moves (17 files, clean)

**`Mibo.Layout` (9 files)** — the entire folder:
- `Grid2D.fs`, `HexGrid.fs`, `Spatial2D.fs`, `HexLayout.fs`, `LayeredHex.fs`,
  `Layout.fs`, `Platformer.fs`, `TopDown.fs`, `Layered.fs`

**`Mibo.Layout3D` (8 of 9 files)** — everything except `Renderer3D.fs`:
- `Grid3D.fs`, `HexGrid3D.fs`, `Spatial3D.fs`, `Layout3D.fs`, `HexLayout3D.fs`,
  `LayeredHex3D.fs`, `Interior.fs`, `Terrain.fs`

### What stays in `Mibo.Raylib` (1 file)

- **`Layout3D/Renderer3D.fs`** — it `open Mibo.Elmish.Graphics3D` and its
  `InstancedRenderContext` takes `Raylib_cs.Mesh * Material3D[]` and emits
  into a `RenderBuffer3D`. `Command3D` itself holds native raylib types
  (`Mesh`, `Model`, `Texture2D`, `Camera3D`, `Color`), and `RenderBuffer3D`
  is an `ArrayPool<Command3D>` buffer. This file is a **renderer**, and
  renderers are backend-specific by the guiding rule above. It stays in the
  raylib backend (in `namespace Mibo.Layout3D`, same as today).

  > Rationale: moving `Renderer3D.fs` would require first abstracting
  > `Command3D`/`RenderBuffer3D`/`Material3D`/native `Mesh` into Core contracts.
  > That is a much larger, separately-scoped effort and out of scope for Phase 3.
  > The layout *geometry* (grids, hexes, spatial queries, terrain) is what's
  > portable; the instanced draw bridge stays backend-side.

### Internal dependency note

`HexGrid3D.fs`, `LayeredHex3D.fs`, and `Spatial3D.fs` `open Mibo.Layout` (the 2D
module) and `CellGrid3D`. Once both `Layout` and the clean `Layout3D` files live
in Core, these dependencies are satisfied within Core. Compile order in
`Mibo.Core.fsproj` must list `Mibo.Layout` files before the `Mibo.Layout3D`
files (mirroring the current order in `Mibo.Raylib.fsproj`).

### Target framework decision (OPEN)

`Mibo.Core` targets **`net8.0`** only; `Mibo.Raylib` targets **`net8.0;net10.0`**.
Moving layout files into Core does not change this — a `net8.0` library is
consumable by `net10.0` apps, and the samples/tests already build this way.
**No multi-targeting change is required for Phase 3.** (The MonoGame backend in
Phase 4 also targets `net8.0`, matching the upstream Mibo repo.)

### Steps

1. Create `src/Mibo.Core/Layout/` and `src/Mibo.Core/Layout3D/` folders.
2. Move the 9 `Layout/` files into `Mibo.Core/Layout/`.
3. Move the 8 clean `Layout3D/` files into `Mibo.Core/Layout3D/` (NOT `Renderer3D.fs`).
4. Keep `Layout3D/Renderer3D.fs` in `src/Mibo.Raylib/Layout3D/`.
5. Update `Mibo.Core.fsproj` `<Compile Include>` list — append the 17 files after
   the existing Elmish entries, Layout before Layout3D (preserve relative order).
6. Update `Mibo.Raylib.fsproj` `<Compile Include>` list: remove the 17 moved
   entries, keep `Layout3D/Renderer3D.fs`. `Renderer3D.fs` depends on
   `Mibo.Layout3D.CellGrid3D`/`HexGrid3D` (now in Core) and
   `Mibo.Elmish.Graphics3D` (raylib backend) — ensure it stays ordered after
   the Graphics3D block.
7. Run `dotnet fantomas .`, build the whole solution, run tests.
8. Update `docs/migration-to-vnext.md` with a Phase 3 section.
9. Update `CHANGELOG.md` (Added: layout types in Core).

---

## Phase 4 — Fresh `Mibo.MonoGame` backend

**Breaking:** n/a — new project.

### Upstream reference: `E:\Mibo` (the original MonoGame Mibo)

The original MonoGame Mibo lives at `E:\Mibo` (`net8.0`, `MonoGame.Framework.Native`
`3.8.*`). It is the **algorithmic reference**, not portable code — its public
types leak MonoGame everywhere (see "Why not port" below). The MonoGame backend
is written from scratch against Core contracts, using `E:\Mibo` as a guide for
behavior and API shape.

**What `E:\Mibo` contains** (`src/Mibo`, `net8.0`, 49 .fs files; tests/samples
target `net10.0`):
- Elmish core: `Elmish.{Time,Commands,Subscriptions,Rendering,ProgramTypes,Runtime}.fs`
  + `Program.fs` (builder) + `System.fs` — **superseded by `Mibo.Core`**; do not port.
  The `Elmish.Runtime.fs` message-pump *body* (deferred-effect drain, fixed-step,
  tick, batched drain in `StartBatch`/`EndBatch`) is backend-agnostic and is the
  spec for how the new host should order work — but Core's shared `ElmishLoop`
  (Phase 2) already encapsulates this, so the new host delegates rather than
  reimplements.
- Input: `Input.fs` (`IInput` + `InputPolling` + `Input.create` returning a
  `GameComponent`) + `InputMapper.fs` (`Trigger`/`InputMap`/`ActionState`/
  `InputMapperService`) — **superseded by Core contracts**; rewrite the impl
  against Core `KeyCode`/`MouseButtonCode`/`GamepadButtonCode`. The pure
  `InputMap`/`ActionState.update`/`nextFrame` logic is the spec.
- Assets: `Assets.fs` (`IAssets` over `ContentManager`, with `Get`/`Create`/
  `GetOrCreate` generic cache + typed loaders + `Assets.fromJson` via JDeck) —
  rewrite against Core `IAssetCache` + a MonoGame `IAssets` (typed loaders stay
  backend). Note: Core's `IAssetCache` already absorbed the generic-cache subset.
- Layout: `Layout/` (9 files) + `Layout3D/` (9 files) — **superseded by Core**
  after Phase 3. The Mibo versions use `Microsoft.Xna.Framework` vectors, not
  `System.Numerics`, so they are not reusable; they only cross-check algorithms.
- Renderers (NOT porting — user decision, "in bad shape"): `Graphics2D.fs`
  (~2200 lines, the flagship `Batch2DRenderer` + 2D lighting/shadows),
  `Render.Shared.fs` (RT pooling), `DefaultShaders.fs` (loads `.fxb`),
  `BillboardBatch.fs`, `LineBatch.fs`, `ShapeBatch.fs`, `Sprite3DTypes.fs`,
  `SpriteQuadBatch.fs`, `QuadBatch.fs`, `Animation.fs`, `Camera.fs`, `Culling.fs`,
  and the entire `Rendering3D/` folder (`Lighting`/`Types`/`Config`/
  `SpriteQuadBatch`/`LineBatch`/`Pipeline`/`View`/`Program`).
- Shaders: `Shaders/{DirectX_11,OpenGL}/*.fxb` — MG-compiled HLSL/GLSL effects;
  no raylib/Core equivalent. Not ported.
- Templates: `Templates/` (2D/3D + multi-platform Android/iOS/WindowsDX/Desktop).
  The multi-platform templates assume MonoGame platform hosts (Android
  `MyGameActivity`, iOS/WindowsDX entry points); since the raylib backend is
  desktop-only, the multi-platform template story collapses for now. Out of scope
  for the backend library; revisit as separate packaging.

### Why not port the Mibo code verbatim

The original Mibo was the **pre-abstraction** shape. Its core types embed
MonoGame handles directly:
- `GameContext` holds `GraphicsDevice`, `ContentManager`, `Game` (MonoGame types).
- `KeyboardDelta.Pressed: Keys[]` uses `Microsoft.Xna.Framework.Input.Keys`.
- `MouseDelta.Position: Point` uses MonoGame's `Point`.
- `IAssets` exposes `GraphicsDevice`/`Content` as abstract members.
- `Program.withRenderer` takes `Game -> IRenderer<'Model>` (factory receives the
  MonoGame `Game`); `withComponent`/`withComponentRef` are MonoGame-specific.
- Service location goes through `ctx.Game.Services` (MonoGame's
  `GameServiceContainer`); every `getXxxService ctx` call site depends on it.

Core has already generalized all of this: `GameContext` carries width/height +
a service registry (no backend handle), input uses backend-neutral code DUs,
and the `Program` builder is backend-neutral with `ServiceRegistrations`
callbacks. Porting the Mibo types would reintroduce the coupling Phases 1–3 removed.

**Dependencies note:** Mibo uses `FSharp.UMX` (`SubId = string<subId>`,
`int<RenderLayer>`) and `JDeck` (JSON asset decoding). Core already keeps
`FSharp.UMX` for `SubId`. `JDeck`'s `Assets.fromJson` is an asset-layer feature;
if the MonoGame backend wants JSON asset decoding, add `JDeck` to that backend
only — not to Core.

### What the new backend must provide

| Core contract | MonoGame implementation |
|---------------|-------------------------|
| Runtime host | `MiboGame : Microsoft.Xna.Framework.Game`, overriding `Update`/`Draw` to drive the shared `ElmishLoop` (Phase 2). Owns the architecture — no `GameComponent` hosting (see Decisions §2). Use `E:\Mibo\src\Mibo\Elmish.Runtime.fs` as the spec for work ordering (deferred-effect drain → fixed-step → tick → batched message drain → renderers), but delegate the mechanics to `ElmishLoop` rather than reimplementing the queue. |
| `IInput` (`Mibo.Input`) | Translate MonoGame `KeyboardState`/`MouseState`/`GamePadState`/`TouchPanel` → Core `KeyCode`/`MouseButtonCode`/`GamepadButtonCode` + Core delta structs |
| `IInputMapper<'Action>` | `InputMapper.createService` over the MonoGame `IInput` |
| `IAssetCache` + `IAssets` | Cache over `ContentManager`; typed loaders (`Texture2D`/`SpriteFont`/`SoundEffect`/`Model`/`Effect`) stay in the MonoGame backend |
| `MonoGameProgram.withInputMapper` | Per-backend `withInputMapper` mirroring `RaylibProgram.withInputMapper`, registering via `ServiceRegistrations` |

### Out of scope for Phase 4

- **Renderers.** No `IRenderer<'Model>` implementations ship. Users write their
  own against MonoGame's `SpriteBatch`/draw calls (per the user's explicit
  decision — the Mibo renderers are not worth porting).
- **Templates** (2D/3D/multi-platform). Separate packaging effort.
- **Content pipeline shaders** (the `.fxb` DirectX/OpenGL compiled shaders in
  `E:\Mibo\src\Mibo\Shaders/`). No default shaders ship.

### Decisions (resolved)

1. **`GameContext` exposes backend-specific, long-lived data/services.**
   `GameContext` is **not** purely backend-neutral — it is each backend's seam
   for the stateful handles user code needs in `init`/`subscribe`. The asymmetry
   is intentional:
   - **raylib:** most access is via static `Raylib.*` functions, so the raylib
     `GameContext` stays light (window dims + a service registry for `IInput`/
     `IAssets`).
   - **MonoGame:** far more stateful surface (`GraphicsDevice`, `ContentManager`,
     the `Game` itself, `Components`), so the MonoGame `GameContext` exposes those
     directly — mirroring the original `E:\Mibo` `GameContext` shape.
   - **The constraint: the two `GameContext` types must not diverge too far.**
     Keep the *portable* fields identical (window width/height, the service
     registry, `IServiceProvider`-style `getService`/`tryGetService`), and let
     each backend add its own strongly-typed handle properties. Portable code
     written against the shared fields works on both; backend-specific code takes
     the backend's `GameContext`. This may mean two concrete `GameContext` types
     (one per backend) — the exact sharing mechanism is settled in decision 5
     below (Core contract + backend inheritance, same as `IAssets`/`IAssetCache`).
     The *intent* is: same portable surface, backend adds handles on top.
2. **Own the architecture: `MiboGame : Game`.** The MonoGame backend hosts the
   loop by inheriting `Microsoft.Xna.Framework.Game` and overriding `Update`/
   `Draw` — not by running on a `GameComponent`. The `ElmishLoop` (Phase 2) is
   driven from those overrides. Whether a loop can *also* run hosted in a
   `GameComponent` is a separate, unrelated feature and is **not** part of this
   split-and-port effort.
3. **Input translation: match the raylib surface (80/20).** The MonoGame backend
   covers at least the same key/mouse/gamepad/gesture codes the raylib backend
   already maps — the 80/20 that the raylib `KeyCode`/`MouseButtonCode`/
   `GamepadButtonCode`/`GestureKind` work already targets. (Both raylib and
   MonoGame sit on SDL under the hood, but that's irrelevant — our contract is
   the Core code DUs, not SDL.) Implement
   `KeyCode.ofMonoGameKey`/`toMonoGameKey`, `MouseButtonCode.ofMonoGameButton`,
   `GamepadButtonCode.ofMonoGameButton`, `GestureKind.ofMonoGameGesture`/
   `toMonoGameGesture`, mirroring the raylib translation modules. Use the `Unknown`
   fallback for anything outside the covered surface, same as raylib.
4. **Package: `MonoGame.Framework.Native`, platform-agnostic.** `Mibo.MonoGame`
   is a core library, so it references `MonoGame.Framework.Native` (`3.8.*`, as
   upstream) and must **not** prescribe a graphics backend — no
   `MonoGame.Framework.WindowsDX` / `.DesktopGL` / `.OpenGL` / `.DirectX` reference.
   The choice of platform package is the app author's, not the library's. Targets
   `net8.0`.
5. **`GameContext`/`IAssets` sharing: Core contract + backend inheritance.** Same
   pattern as the raylib `IAssets`/`IAssetCache` split (Phase 1c): the
   backend-neutral contract lives in Core; each backend's concrete type extends
   it with backend-specific members. Apply this to both `GameContext` and
   `IAssets`:
   - Core defines the portable `GameContext` surface (window dims, service
     registry) and `IAssetCache`/`IAssets` contracts.
   - The MonoGame backend adds a `MonoGameGameContext` (or extends Core's) that
     also exposes `GraphicsDevice`/`ContentManager`/`Game`, and a MonoGame
     `IAssets` that adds `Texture2D`/`SpriteFont`/`SoundEffect`/`Model`/`Effect`
     typed loaders — exactly as the raylib backend's `IAssets` adds
     `Texture2D`/`Font`/`Sound`/`Model`/`ModelAnimations`.

   This resolves the "sharing mechanism" question: it's the established
   inheritance pattern, not a new interface/base-record debate.

### Steps (high level — flesh out when Phase 4 starts)

1. Create `src/Mibo.MonoGame/Mibo.MonoGame.fsproj` (`net8.0`, references
   `Mibo.Core`, `MonoGame.Framework.Native`).
2. Implement the Runtime host (`MonoGameGame` wrapping `ElmishLoop`).
3. Implement `IInput` + the `KeyCode`/`MouseButtonCode`/`GamepadButtonCode`
   translation modules.
4. Implement `IInputMapper` via `InputMapper.createService`.
5. Implement `IAssetCache` + MonoGame `IAssets` over `ContentManager`.
6. Add `MonoGameProgram.withInputMapper`.
7. Add a smoke test; add `docs/` page; update `CHANGELOG.md`.

---

## Phase 5 — Parity verification (optional)

**Breaking:** n/a — verification only.

### Goal

Confirm the abstraction is real: that a non-trivial program written against
`Mibo.Core` contracts behaves identically whether backed by raylib or MonoGame,
and that no backend-specific type has leaked into Core.

### Checks

1. **Core cleanliness.** `Mibo.Core` must have **zero** backend dependencies.
   Grep `src/Mibo.Core` for `Raylib_cs`, `Microsoft.Xna`, `MonoGame`, and any
   native handle types (`Mesh`, `Texture2D`, `Camera3D`, `Color` from a backend).
   Any hit is a leak that must be fixed.
2. **Backend parity.** Port one sample (e.g. `PlatformerSample`) to the
   MonoGame backend and verify the update loop, input mapping, fixed-step,
   subscriptions, and asset cache produce equivalent behavior. Differences in
   rendering are expected and acceptable; differences in model/timing logic
   are not.
3. **Contract completeness.** Every `withX` the raylib backend offers that is
   backend-neutral should be available on MonoGame too. Audit
   `RaylibProgram.withInputMapper` vs `MonoGameProgram.withInputMapper` and
   confirm the `Program` builder surface is identical across backends.
4. **Test suite portability.** The Headless runner is already backend-neutral
   (Phase 2). Confirm the input/asset/layout tests can run unchanged against
   either backend's implementations.

### Definition of done

Core has no backend references; at least one sample runs on both backends with
matching simulation behavior; the migration doc is complete through Phase 5.
