# Changelog

## [Unreleased]

### Added

- `Mibo.MonoGame.Graphics3D.ForwardPipeline`: billboard + line dispatch for the three previously-stubbed `Command3D` cases. `DrawBillboard`/`DrawBillboardBatch` use `Matrix.CreateBillboard` (native camera-facing math) + `DrawUserIndexedPrimitives` with a lazily-created unlit textured `BasicEffect` (alpha blend, depth read) over `VertexPositionColorTexture` quads; the batch packs all quads into one CPU staging array + index buffer for a single draw call. `DrawLine3D` uses `DrawUserPrimitives(LineList)` with a lazily-created unlit vertex-color `BasicEffect`. Staging arrays grow on demand; effects are disposed in `Shutdown`.

### Fixed

- `Mibo.MonoGame.Graphics3D.ForwardPipeline`: billboard quad UVs were emitted in pixel space instead of normalized `[0,1]` — with `SamplerState.LinearWrap`, a W×H texture tiled W×H times across the quad. `EmitQuad` now normalizes the source rect against texture dimensions (same convention as the `Renderer2D` lit-quad path).
- `Mibo.MonoGame.Graphics3D.ForwardPipeline`: `DrawLine3D` allocated a 2-element `VertexPositionColorTexture[]` per call. Replaced with a pooled instance-level `lineStaging` field, matching the `billboardStaging`/`billboardIndices` pattern.
- `Mibo.MonoGame.Graphics3D.ForwardPipeline`: native hardware instancing via `DrawInstancedPrimitives` + a dual vertex stream (mesh per-vertex + `VertexInstanceWorld` per-instance world matrices). The `DrawMeshInstanced` case (previously a no-op stub from B6) now dispatches through a new minimal `Shaders/Instanced.fx` (compiled to both OGL/DX per §6.3; ambient + 1 directional light over flat albedo — the instanced lighting floor; B9 replaces it with the full PBR instanced variant). Instance vertex buffer and CPU staging array grow on demand; the `Instanced` effect + instance buffer are lazily created on first use and disposed in `Shutdown`.
- `Mibo.MonoGame.Graphics3D.Primitive3D`: `VertexInstanceWorld` — per-instance vertex type packing a 4×4 `Matrix` as four `Vector4` rows (`TEXCOORD1..4`, usage indices 1–4 to avoid colliding with the mesh's `TEXCOORD0` on stream 0), with `IVertexType` + lazily-initialized static `VertexDeclaration`.
- `Mibo.MonoGame.Shaders.Instanced` (`.fx` + `.dx.mgfx`/`.ogl.mgfx`): minimal instanced vertex shader reading per-instance world rows from stream 1 and composing with the shared view-projection (plain `float4x4`, vector-left `mul` per §6.1, `#if OPENGL vs_3_0` split per §6.3). Compiled via `Shaders/script.fsx`.
- `Mibo.Layout3D` (MonoGame backend): `InstancedRenderContext<'T,'K>` + `CellGridRenderer3D` + `HexGrid3DRenderer` — ported from the raylib canonical at the renderer-glue layer (`Raylib_cs.Mesh`→`PrimitiveMesh`, `System.Numerics.Matrix4x4`→XNA `Matrix` via `Conversions` per §5/§6.2). Reuses `Mibo.Core.Layout3D` (`CellGrid3D`/`HexGrid3D`) unchanged. `renderInstanced`/`renderVolumeInstanced` group cells by key, pool per-instance transform snapshots via `ArrayPool<Matrix>`, and emit `DrawMeshInstanced` commands per sub-mesh.
- `Mibo.MonoGame.Graphics3D.Primitive3D`: `PrimitiveMesh` type (effectless geometry — the MonoGame analog of raylib's universal `Mesh`, per §4.1 the only unit `Material3D` may pair with) wrapping a `VertexBuffer` + `IndexBuffer` + primitive count, with `Draw(gd, effect)`/`Dispose()` members and `IDisposable`. `Primitive3D.create(gd)` builds a `PrimitiveSet` of six unit primitives (cube/sphere/cylinder/plane/torus/cone) from `VertexPositionNormalTexture` arrays, uploaded once at startup. No native `GenMesh*` exists in MonoGame, so the vertex builders are hand-written.
- `Mibo.MonoGame.Graphics3D.Command3D`: two new cases per §4.1/§B6 — `DrawMeshPBR(PrimitiveMesh, Matrix, Material3D)` (the PBR path; `ForwardPipeline` dispatches it today via a lazily-created `BasicEffect` fallback that maps the albedo color, ignoring PBR maps until B9) and `DrawMeshInstanced(PrimitiveMesh, Matrix[], Material3D, instanceCount)` (the case is present so B7 can wire native instancing without a breaking signature change; pipeline dispatch is a no-op until B7). `Draw3D` DSL helpers `drawMeshPBR`/`drawMeshInstanced` added.
- `Mibo.MonoGame.Graphics3D.ForwardPipeline`: native-first forward 3D pipeline implementing `IRenderPipeline3D`. Dispatches all `Command3D` cases and binds each `ModelMeshPart`'s own native effect (`BasicEffect`/`SkinnedEffect`/custom `Effect`) with the active camera matrices. Lighting floor: 1 ambient + up to 3 directional lights via `BasicEffect`'s `DirectionalLight0..2` (excess clamped; point/spot accumulated but unbound natively — B9). `DrawMeshEffect` overrides the part's effect; `DrawModel` replicates `Model.Draw`'s bone-composition loop with injected lighting; `DrawSkinnedMesh` binds native `SkinnedEffect` bones. Billboards/lines (B8), shadows (B10), and post-process passes (B9) are accepted no-ops. `PostProcess3D` scaffold ships with `PostProcessPass3D.Effect` (not `Shader`, per §4.1) and `PostProcessConfig3D.none`.
- `Mibo.MonoGame.Camera3D`: full 3D camera module (`lookAt`, `orthographic`, `orbit`, `screenPointToRay`) with the `Camera` struct (View + Projection; a struct, not a reference record, because it flows through the view function every frame — same change applied to the raylib canonical `Camera` in lockstep) and the `Ray` struct. Builders: `render`, `withViewport`, `withClear`, `withPostProcess`, `withoutPostProcess`, `splitScreenLeft`/`Right`/`Top`/`Bottom` (pixel-bounds param), `overlay`. Uses XNA `Matrix.CreateLookAt`/`CreatePerspectiveFieldOfView`/`CreateOrthographic` in right-handed convention.
- `Mibo.MonoGame.Culling`: thin facade over native MonoGame `BoundingFrustum`/`BoundingSphere`/`BoundingBox` for visibility culling — `isVisible` (sphere vs frustum), `isGenericVisible` (box vs frustum), `isVisible2D` (rectangle intersection). No custom `Frustum` class.

- `Mibo.MonoGame.Graphics3D`: scaffolding + core 3D types (`Conversions.fs`, `Light3D.fs`, `Material3D.fs`) and command-buffer infrastructure (`Command3D.fs`, `RenderBuffer3D.fs`, `RenderPipeline3D.fs`, `RenderTargetPool3D.fs`, `Renderer3D.fs`, `Draw3D.fs`). Internal `Conversions` module provides `System.Numerics`↔XNA bridge at the Core/backend boundary. `Material3D` is a PBR-only param struct for the custom-PBR pipeline path (pairs with `PrimitiveMesh` in a later phase, never with a `ModelMeshPart`); native rendering binds each part's own `Effect`. `Command3D` draw cases are native-first: `DrawMesh`/`DrawSkinnedMesh` bind `part.Effect` (no `Material3D` field), and `DrawMeshEffect` lets the caller supply a native `Effect`. `DrawMeshInstanced` is deferred until `PrimitiveMesh` exists (B6). `Renderer3D<'Model>` owns a `RenderBuffer3D` + `RenderTargetPool3D` and delegates to a pluggable `IRenderPipeline3D`. Ships `NoopPipeline` as a placeholder. `Camera3D` + `Camera3DConfig` types added for MonoGame backend (pixel viewport, post-process pass selection).

- `Cmd.Msg of 'Msg` case in the `Cmd<'Msg>` struct DU: a zero-allocation alternative to `Single(Effect(...))` for `Cmd.ofMsg`. `Cmd.ofMsg` now returns `Msg msg` directly instead of wrapping the message in an `Effect` delegate. `Cmd.map` on a `Msg` stays allocation-free (`Msg(f msg)`). The runtime dispatches `Msg` directly without invoking a delegate. `batch` and `batch2` preserve the `Msg` case in their fast paths.
- `Mibo.Core.Tests` project: 11 backend-agnostic test files extracted from `Mibo.Raylib.Tests` into a standalone project that references only `Mibo.Core` (no Raylib dependency). Covers Elmish Cmd/Sub, HeadlessLoop/GameTime, Layout, Layout3D, HexGrid, HexLayout, LayeredHex, HexGrid3D, HexLayout3D, LayeredHex3D, Spatial2D, and Spatial3D (503 tests).
- `Mibo.Core` project: backend-agnostic home for `Cmd`/`Sub`/`GameTime`/`DispatchMode`/`FixedStep`/`System`/`RenderBuffer`/`IRenderer`/`GameContext`/`Program`/`GameConfig`. The `Mibo.Raylib` project now references `Mibo.Core`. No API changes; all types remain in the `Mibo.Elmish` namespace. See `docs/migration-to-vnext.md` for the vNext roadmap.
- Backend-neutral input contracts in `Mibo.Core` (namespace `Mibo.Input`): `KeyCode`, `MouseButtonCode`, `GamepadButtonCode`, `GestureKind` (struct DUs, `RequireQualifiedAccess`), the delta types, the `IInput`/`IInputMapper<'Action>` contracts, `Trigger`/`InputMap<'Action>`/`ActionState<'Action>`, and the `Keyboard`/`Mouse`/`Touch`/`Gamepad`/`Gesture` subscription modules. Backends supply concrete `IInput`/`IInputMapper` implementations.
- Raylib↔Core input translation modules in the raylib backend: `KeyCode.ofRaylibKey`/`toRaylibKey`, `MouseButtonCode.ofRaylibButton`/`toRaylibButton`, `GamepadButtonCode.ofRaylibButton`/`toRaylibButton`, `GestureKind.ofRaylibGesture`/`toRaylibGesture`.
- `IAssetCache` interface in `Mibo.Core` (`Mibo.Elmish` namespace): the backend-neutral generic asset-cache contract (`Get<'T>`/`Create<'T>`/`GetOrCreate<'T>`/`Clear`/`Dispose`). The raylib backend's `IAssets` now extends `IAssetCache`; all existing calls compile unchanged. Portable code can retrieve an `IAssetCache` from `GameContext` to cache custom assets without referencing a backend.
- `Program` builder functions in `Mibo.Core` (`Mibo.Elmish` namespace): `mkProgram`, `withConfig`, `withRenderer`, `withTick`, `withFixedStep`, `withDispatchMode`, `withSubscription`, `withAssets`, `withAssetsBasePath`, `withInput`, plus a new `withServiceRegistration` hook for backend-specific service registration. The `Program` record gained a `ServiceRegistrations: (GameContext -> unit) list` field that hosts invoke before `Init`.
- `RaylibProgram.withInputMapper` in the raylib backend (`Mibo.Elmish` namespace): the raylib-specific `withInputMapper`, now decoupled from the Core `Program` builder. It registers the raylib-backed `IInputMapper` via a `ServiceRegistrations` callback so Core never references the raylib factory.
- `ElmishLoop<'Model,'Msg>` and `LoopCore<'Model,'Msg>` in `Mibo.Core` (`Mibo.Elmish` namespace): the shared message-processing loop extracted from the duplicated code in `RaylibGame` and `HeadlessRunner`. Both hosts now delegate to `ElmishLoop`; `Program` and `HeadlessProgram` project to `LoopCore` via `ElmishLoop.coreOfProgram` / `HeadlessProgram.toLoopCore`.
- `HeadlessProgram`, `HeadlessRunner`, and the `HeadlessProgram` builder module moved from the raylib backend to `Mibo.Core` (pure F#, no backend dependencies). All existing user code keeps working unchanged — types stay in the `Mibo.Elmish` namespace.
- `Mibo.Layout` and `Mibo.Layout3D` modules moved from the raylib backend to `Mibo.Core`. 17 files of pure layout geometry (2D grids/hex/spatial/platformer/top-down/layered + 3D grids/hex/spatial/interior/terrain) over `System.Numerics`. Namespaces preserved; all existing code compiles unchanged. `Layout3D/Renderer3D.fs` (the raylib instanced-draw bridge) stays in the raylib backend.
- Migration guide for Mibo (MonoGame) users: `docs/migration-from-monogame.md` — comprehensive guide covering program setup, GameContext, input types, assets, rendering, animation, camera, and a full before/after example.
- `Mibo.MonoGame`: 2D rendering stack (`Command2D`, `RenderBuffer2D`, `Draw` DSL, `Renderer2D`, `Camera2D`) — MonoGame port of the Raylib `Graphics2D` surface.
  - **Phase 1 (MVP):** `Sprite`, `Text`, `FillRect`, `FillCircle`, `BeginCamera`/`EndCamera`, `DrawImmediate`, and `Clear`.
  - **Phase 2 (Shapes):** Full primitive suite via `PrimitiveBatch` (`BasicEffect` + `DrawUserPrimitives`): `RectOutline`, `FillRectRounded`, `RectRoundedOutline`, `RectGradientV`, `RectGradientH`, `RectGradient`, `FillCircle` (now native tessellation, no texture), `CircleOutline`, `CircleSector`, `CircleSectorOutline`, `CircleGradient`, `FillRing`, `RingOutline`, `FillEllipse`, `EllipseOutline`, `Line`, `LineThick`, `LineStrip`, `Bezier`, `Triangle`, `TriangleFan` (decomposed to `TriangleList`), `TriangleStrip`, `FillPoly`, `PolyOutline`. Camera changes and `DrawImmediate` flush both `SpriteBatch` and `PrimitiveBatch` simultaneously.
  - **Phase 3 (Render State + Shader/Target):** Render-state commands (`SetBlend`, `SetScissor`, `ClearScissor`, `SetLineWidth`, `SetViewport`), camera-config (`BeginCameraConfig` with `Camera2DConfig` pixel-viewport + clear-color), custom shader (`BeginShader`/`EndShader` wrapping `Effect`), render-target (`BeginTarget`/`EndTarget` wrapping `RenderTarget2D`), and `BlendMode` DU (`AlphaBlend`/`NonPremultiplied`/`Additive`/`Opaque`). `PrimitiveBatch` now supports `SetEffect`, `SetBlendState`, `SetRasterizerState` for mid-batch state changes. `Renderer2D` maintains a camera/viewport/blend/shader/target stack with dual-batch flush on every state transition.
  - **Shaders:** Embedded `LitSprite.dx.mgfx` / `LitSprite.ogl.mgfx` and `LitSpriteNormalMap.dx.mgfx` / `LitSpriteNormalMap.ogl.mgfx` as `EmbeddedResource` in the fsproj. `ShaderLoader.loadEffect` resolves the correct platform variant via `PlatformInfo.MonoGamePlatform` + `GraphicsBackend`. Not yet consumed by the renderer (lighting phase deferred).
  - **Phase 4 (Lighting):** Full 2D lighting system in `Graphics2D/Lighting/`: `LightContext2D` (owns two embedded `Effect`s, caches `EffectParameter` locations, uploads uniforms to both), `AmbientLight2D`/`PointLight2D`/`DirectionalLight2D`/`Occluder2D` types + builders, `LitSprite`/`EndLighting`/`NoopLight`/`EnableShadows`/`DisableShadows` commands, `SpriteState.NormalMap` field, `LightCommands`/`LightDraw` DSL. `Renderer2D` draws lit sprites via a custom `DrawUserPrimitives` path (`VertexPositionColorTexture` + lit `Effect`, bypasses `SpriteBatch`) with `MatrixTransform = projection * view`, texture + normal-map binding, and lazy uniform upload on first lit sprite. `ShaderLoader.loadEffect` now consumed by `LightContext2D`.
  - **Phase 5 (Particles):** `Particle2D` render snapshot struct + builders (`create`/`withRotation`/`withSourceRect`/`withColor`) in `Graphics2D/Lighting/ParticleTypes.fs`. `ParticleCommands`/`ParticleDraw` DSL (`particles`) and `ParticleSimulation.fadeAndCompact` helper in `Graphics2D/Lighting/Particle.fs`. New `Command2D.Particle` case dispatched in `Renderer2D` via per-particle `SpriteBatch.Draw` textured quads. `Draw.particles` pipe wrapper.
  - **Phase 6 (Post-process):** `PostProcessPass` struct (`Effect` + optional `OnSetup` callback), `PostProcess2D.apply` ping-pong chain, and `IRenderTargetPool`/`RenderTargetPool` (pooled `RenderTarget2D` by dimensions) in `Graphics2D/RenderTargetPool.fs`. `Renderer2DConfig.PostProcess` field; `Renderer2D.Draw` renders the scene to a pooled `RenderTarget2D` when configured, then applies the pass chain (last pass to backbuffer via `SpriteEffects.FlipVertically`). `Renderer2D` owns and disposes the pool.
  - **Phase 7 (Animation):** `Animation.fs` (namespace `Mibo.Animation`): `Point`, `Animation`, `GridAnimationDef`, `SpriteSheet` (with `Texture2D`/`NormalMap`/`Origin`/`FrameSize`), `AnimatedSprite` struct + `SpriteSheet` factory module (`fromFrames`/`fromGrid`/`single`/`static'`/`withNormalMap`/`tryGetAnimationIndex`/`animationNames`) + `AnimatedSprite` module (`create`/`createWith`/`play`/`playByIndex`/`playIfNot`/`restart`/`update`/`currentSource`/`isFinished`/`isPlaying`/`duration`/`withColor`/`withScale`/`withRotation`/`flipX`/`flipY`/`facingLeft`/`facingRight`). `LightCommands.litAnimatedSprite` / `LightDraw.litAnimatedSprite` extract the current source rect, apply FlipX/FlipY via source-rect negation, and emit `Command2D.LitSprite` with the sheet's `NormalMap`.
  - **Phase 8 (GridOccluders):** `GridOccluders.fromCellGrid` + `[<Flags>] Edge` (`None`/`Top`/`Bottom`/`Left`/`Right`/`All`) in `Graphics2D/Lighting/GridOccluders.fs`. Generates `Occluder2D` line segments for exposed edges of solid cells in a `CellGrid2D<'T>`, converting `System.Numerics.Vector2` (Core) to `Microsoft.Xna.Framework.Vector2` (MonoGame) at the construction boundary.

### Changed

- **Breaking:** `Cmd<'Msg>` discriminated union has new `Msg of 'Msg` case between `Empty` and `Single`. Users with exhaustive pattern matches on `Cmd<'Msg>` must handle the new case (or add a wildcard match). `Cmd.ofMsg` now returns `Msg` instead of `Single(Effect(...))`. See `docs/migration-to-vnext.md` (Phase 3b) for the full migration guide.
- **Breaking:** the input surface now uses backend-neutral codes instead of raylib's native enums. See `docs/migration-to-vnext.md` (Phase 1b) for the full migration guide. Highlights:
  - `InputMap.key` takes `KeyCode` instead of `Raylib_cs.KeyboardKey`. Bindings become portable across backends.
  - `Trigger` cases renamed: `MouseBut of int` → `MouseButton of MouseButtonCode`; `GamepadBut` → `GamepadButton of int * GamepadButtonCode`.
  - `InputMap.mouse` takes `MouseButtonCode` instead of `int`.
  - `MouseDelta.Buttons` holds `MouseButtonCode[]`.
- **Breaking:** `Program.withInputMapper` moved to `RaylibProgram.withInputMapper` (raylib backend only). The factory is backend-specific, so the function can no longer live in the shared Core `Program` builder. Call sites change `Program.withInputMapper map` → `RaylibProgram.withInputMapper map`. No samples used this path (they use the subscription-based `InputMapper.subscribeStatic`), so no sample changes were required. See `docs/migration-to-vnext.md` (Phase 1d).
- **Breaking (behavioral):** renderer draw order is now correct. Previously, `withRenderer` prepended to the list but the runtime iterated without reversing, so the last renderer added drew first. Now the runtime reverses `program.Renderers` before iterating, matching the existing `Config`/`ServiceRegistrations` pattern. Renderers draw in the order you add them. This is a behavioral change that will not produce compiler errors — review your renderer setup if you use multiple renderers.

### Fixed

- `Mibo.MonoGame.Shaders.Instanced`: directional light direction was not negated in the pixel shader, lighting surfaces facing away from the source. MonoGame light directions point in the direction of travel; diffuse needs the surface→light vector, so negate (`normalize(-DirLightDir)`). `BasicEffect` does this internally; the custom instanced shader did not.
- `Cmd.batch` silently dropped a single `NowAndDeferNextFrame([|eff|], [||])` passed as the only command. The single-effect fast path only matched `Msg`/`Single`/`Batch of 1`, so `NowAndDeferNextFrame` fell through to the wildcard and the effect was lost. Added the missing case and initialize the accumulator to `Empty` explicitly.
- `HeadlessRunner.StepUntil` had an off-by-one: the predicate was tested *before* each `Step`, so a predicate satisfied by the final permitted step returned `false`. Also, once `met`/`ShouldQuit` became true the loop kept spinning up to `maxFrames`. Rewritten as a `while` loop that steps first and exits immediately when the predicate (or `ShouldQuit`) becomes true. The documented "quit counts as met" behaviour is preserved.
- MonoGame `InputPolling.pollMouse` now diffs `XButton1`/`XButton2` and emits `MouseButtonCode.Extra1`/`Extra2` on the `MouseDelta` stream, matching the raylib backend. Previously, the event-driven mouse subscription path never surfaced back/forward button presses (the poll-driven `InputMapper` path handled them via `isMouseButtonDownFor`), so the two input paths within MonoGame diverged.
- `ElmishLoop.TickFrame` XML doc corrected: the return value is `true` when any messages were processed (not when the model structurally changed), and subscriptions are re-evaluated on the same condition. The implementation is unchanged — it must support in-place mutable models (e.g. `ThreeDSample`'s `GameModel`) where `Update` returns the same reference every frame, so reference or structural equality cannot detect changes.
- raylib `InputPolling.pollMouse` now filters `MouseButtonCode.Unknown` before adding to the pressed/released buffers, matching the existing `pollGamepad` pattern. Previously the mouse path would have leaked `Unknown` codes into the `MouseDelta` stream if raylib-cs ever added a new `MouseButton` enum value not covered by `ofRaylibButton`.
- raylib `InputMapper.createService.Update` now binds `let rk = KeyCode.toRaylibKey k` once per key trigger instead of calling the mapper three times (pressed/released/down), matching the existing `MouseButton` branch's pattern.
- `RenderBuffer<'Key,'Cmd>` now implements `IDisposable`. The backing array rented from `ArrayPool.Shared` is returned to the pool on `Dispose`. Non-breaking: callers that don't dispose keep the current behavior; callers that use `use` (and renderers whose `Dispose` chains to the buffer) now release the terminal array on shutdown.
- MonoGame `PrimitiveBatch.AddTriangleFan` no longer draws a stray closing triangle for open fans. Added a `closeLoop` argument (default `true`); `circleSector` now passes `closeLoop=false` so partial arcs don't render a chord across the mouth.
- MonoGame `Renderer2D.fillRectRounded` now fills rounded rectangles correctly. `roundedRectPath` returns only the perimeter, but `AddTriangleFan` treats `points[0]` as the center — so the fan radiated from a corner instead of the centroid. The rect centroid is now prepended to the path before the fan call.
- MonoGame `LightContext2D.Dispose` no longer disposes caller-supplied `Effect` instances. An ownership flag tracks whether the context created the effect from embedded resources (owned) or received it from the caller (not owned); only owned effects are disposed, preventing double-dispose errors when the caller shares an effect across contexts.
- MonoGame `Renderer2D.Draw` now always closes its `SpriteBatch`/`PrimitiveBatch` and releases pooled render targets, even when `execute` or a post-processing pass throws. Previously a single throwing frame left the batches open, breaking every subsequent frame with "Begin called while already in a batch", and leaked pooled `RenderTarget2D`s growing GPU memory each frame.
- MonoGame `Renderer2D` lit-sprite quad vertex buffer moved from a module-level mutable to a per-instance field (`RenderResources.QuadVerts`). Stacked/layered `Renderer2D` instances no longer share the scratch buffer, avoiding clobbering an in-progress lit-sprite draw.
- MonoGame `ParticleSimulation.fadeAndCompact` now removes particles only when their faded alpha reaches `0.0f` (in float space), matching the documented "alpha <= 0 are removed" contract. Previously the `>= 1.0f` byte-rounding guard dropped faint-but-alive particles early.
- MonoGame `LightContext2D.uploadOccluderArray` no longer allocates a `Vector4[]` every dirty frame; occluders are uploaded element-by-element via the `EffectParameter.Elements` indexer, matching the dir/point light upload style (AGENTS.md: avoid heap allocations in hot paths).
- MonoGame `PostProcess2D.apply` now iterates every pass in the effect's current technique (instead of hardcoding `Passes.[0]`, which crashed on empty techniques) and passes the effect to `SpriteBatch.Begin` in `Immediate` mode so the batch applies the effect's multi-pass logic correctly. The `SpriteEffects.FlipVertically` copy is retained and documented as backend-consistent (MonoGame stores render targets upside-down relative to the back buffer on both DirectX and OpenGL).
- MonoGame `RenderTargetPool` now caps idle targets retained per dimension (`maxIdlePerDimension`, default 2). Excess targets are disposed at `ReleaseAll` rather than retained, so repeated window resizes (which produce many distinct dimensions) no longer leak GPU memory for sizes that may never be requested again.

### Removed

- 11 stale duplicate test files from `Mibo.Raylib.Tests` (`ElmishTests.fs`, `HeadlessTests.fs`, `HexGridTests.fs`, `HexLayoutTests.fs`, `LayeredHexTests.fs`, `HexGrid3DTests.fs`, `HexLayout3DTests.fs`, `LayeredHex3DTests.fs`, `LayoutTests.fs`, `Spatial2DTests.fs`, `Spatial3DTests.fs`). These were leftovers from the `Mibo.Core.Tests` extraction that were never referenced by `Mibo.Raylib.Tests.fsproj` and therefore never compiled — the canonical copies live in `Mibo.Core.Tests`.

## [1.3.0] - 2026-06-13

### Added

- `HeadlessProgram` and `HeadlessRunner` for running the Elmish update loop without graphics, input polling, or Raylib initialization. Use for unit testing, server-side simulation, and CLI debugging.
- `HeadlessProgram.mkHeadless init update` — creates a headless program with the same `Init`/`Update` signatures as `Program`.
- `HeadlessProgram` builder DSL: `withSubscribe`, `withTick`, `withFixedStep`, `withDispatchMode`, `withObserver`.
- `HeadlessProgram.observe` — helper that creates a `System.IObserver<'T>` from an `onNext` callback, hiding the `OnError`/`OnCompleted` boilerplate.
- `HeadlessRunner` with explicit frame control: `Step(TimeSpan)`, `StepN(count, TimeSpan)`, `StepUntil(predicate, TimeSpan, ?maxFrames)`.
- `HeadlessRunner.Dispatch(msg)` and `DispatchMany(msgs)` for sending messages from outside the update loop.
- `HeadlessRunner.Model`, `GameTime`, `ShouldQuit` for accessing simulation state.
- `HeadlessRunner.Run(interval, ?ct)` — returns `seq<struct(GameTime * 'Model)>`, a paced synchronous sequence of simulation frames. Uses spin-wait with `Thread.Sleep(1)` for timing.
- `HeadlessRunner.RunAsync(interval, ct)` — returns `IAsyncEnumerable<struct(GameTime * 'Model)>`, a paced async sequence of simulation frames. Uses `PeriodicTimer` for efficient timing.
- Observer support: `HeadlessProgram.Observers` field and `withObserver` DSL for registering `System.IObserver<struct(GameContext * 'Model * GameTime)>` factories. Observers fire every frame after the update loop, receiving the current model and game time. Observers implementing `IDisposable` are disposed when the runner is disposed.
- 27 unit tests for new features: step return values, observer lifecycle, observer correctness (post-update model, GameTime accumulation, multiple observers, window dimensions, subscription interaction), Run/RunAsync enumeration, cancellation, and ShouldQuit behavior. 47 total headless tests.
- XML documentation for `HeadlessProgram.withTick`, `withFixedStep`, and `withDispatchMode`.
- Headless mode documentation: Observers section (`withObserver`/`observe`), `Run`/`RunAsync` section with pacing and cancellation examples, server simulation example using observer-based broadcast.

## [1.2.0] - 2026-06-07

### Added

- `Grid2DSpatial` — Spatial helpers for `CellGrid2D`: `neighbors4`, `neighbors8`, `distanceManhattan`, `distanceChebyshev`, `distanceEuclidean`, `worldToCell`, `inRange`, `lineOfSight`, `lineOfSightCells`, `floodFill`, `findPath` (A\* pathfinding with min-heap).
- `Hex2DSpatial` — Spatial helpers for `HexGrid`: `offsetToCube`, `cubeToOffset`, `cubeRound`, `neighbors`, `distance`, `worldToCell`, `inRange`, `ring`, `spiral`, `lineOfSight`, `lineOfSightCells`, `floodFill`, `findPath`. Supports both PointyTop and FlatTop orientations.
- `Grid3DSpatial` — Spatial helpers for `CellGrid3D`: `neighbors6`, `neighbors26`, `distanceManhattan`, `distanceChebyshev`, `distanceEuclidean`, `worldToCell`, `inRange`, `lineOfSight`, `lineOfSightCells`, `floodFill`, `findPath` (A\* pathfinding).
- `Hex3DSpatial` — Spatial helpers for `HexGrid3D`: `neighbors`, `neighborsHex`, `distance`, `worldToCell`, `inRange`, `lineOfSight`, `floodFill`, `findPath`. Supports both PointyTop and FlatTop orientations.
- 275 unit tests for spatial helpers covering both PointyTop and FlatTop hex orientations, property-based correctness tests (triangle inequality, offset-cube roundtrip, A\* optimality vs BFS, flood fill completeness), adversarial/edge cases (1x1 grids, OOB inputs, boundary worldToCell, goal-blocked LOS), and non-square grid validation.
- `HexGrid<'T>` — 2D hex grid with flat-array storage. Supports both PointyTop and FlatTop orientations via `HexOrientation` DU. Module functions: `create`, `set`, `get`, `clear`, `getWorldPos`, `iter`, `iterVisible`.
- `HexLayout` — Full layout DSL for `HexGrid` matching `Layout` module API surface: `run`, `section`, `padding`, `paddingEx`, `center`, `flowX`, `flowY`, `set`, `setIfEmpty`, `repeatX`, `repeatY`, `fill`, `border`, `rect`, `corners`, `clear`, `generate`, `iter`, `map`, `replace`, `replaceScatter`, `line`, `circle`, `polygon`, `checker`, `checkerBorder`, `scatter`, `scatterBorder`, `scatterLine`, `scatterStamp`.
- `LayeredHexGrid<'T>` — Layered variant with `Dictionary<int, HexGrid<'T>>` layers and `LayeredHexLayout.layer` for composable per-layer DSL.
- `HexGrid3D<'T>` — 3D hex grid with hexagonal positioning in the XZ plane and linear layer height on the Y axis. Supports both PointyTop and FlatTop orientations.
- `HexLayout3D` — Full layout DSL for `HexGrid3D` matching `Layout3D` API surface: `run`, `section`, `padding`, `paddingEx`, `center`, `flowX`, `flowY`, `flowZ`, `set`, `setIfEmpty`, `repeatX`, `repeatY`, `repeatZ`, `column`, `fill`, `clear`, `floorHex`, `wallXY`, `wallYZ`, `shell`, `edges`, `line`, `sphere`, `cylinder`, `generate`, `generateHexLayer`, `generateXY`, `generateYZ`, `iter`, `map`, `replace`, `replaceScatter`, `scatter3D`, `scatterHexLayer`, `scatterXY`, `scatterYZ`, `scatterShell`, `scatterEdges`, `scatterStamp`, `checker3D`, `checkerHexLayer`, `checkerXY`, `checkerYZ`, `checkerShell`.
- `LayeredHexGrid3D<'T>` — Layered variant with `Dictionary<int, HexGrid3D<'T>>` layers and `LayeredHexLayout3D.layer` for composable per-layer DSL.
- `HexGrid3DRenderer` — Rendering functions for hex grids: `render`, `renderVolume`, `renderWithIndices`, `renderInstanced`, `renderVolumeInstanced`.
- Non-uniform dimension tests for 2D, Hex2D, and 3D grids validating correct face/edge positions for shell, border, corners, scatterShell, and scatterBorder functions.
- Hex grid documentation: comprehensive guides for 2D and 3D hex grids covering orientation, coordinates, adjacency, pathfinding, elevation patterns, instanced rendering, and complete game examples (strategy maps, Civilization-style maps).
- `KeyCombo of Set<KeyboardKey>` trigger type for simultaneous key combinations in the input mapper.
- `InputMap.keyCombo` helper for binding actions to key combos (e.g., `|> InputMap.keyCombo Save (Set [KeyboardKey.LeftControl; KeyboardKey.S])`).
- `GameConfig` DSL functions: `withWidth`, `withHeight`, `withMinWidth`, `withMinHeight`, `withTitle`, `withTargetFPS`.
- Resizable window support via `GameConfig.MinWidth` and `GameConfig.MinHeight` — when set, enables `ConfigFlags.ResizableWindow` and calls `Raylib.SetWindowMinSize`.
- 4 unit tests for key combo functionality (combo starts, releases, partial hold, multiple combos per action).
- `Cmd.signalExit` for programmatic window exit from `update` functions. Signals the runtime to exit after the current frame completes. Window close via X button or Alt+F4 continues to work independently.

### Changed

- **Breaking:** Default exit key disabled (`SetExitKey(KeyboardKey.Null)`). The ESC key no longer closes the window. Games must handle window close via the OS close button (X) or Alt+F4. To re-enable a custom exit key, call `Raylib.SetExitKey(key)` in your init or use a subscription to dispatch a quit message.
- **Breaking:** `Cmd<'Msg>` discriminated union has new `Quit` case. Users with exhaustive pattern matches on `Cmd<'Msg>` must handle the new case (or add a wildcard match).
- **Breaking:** `GameConfig` struct has new fields (`MinWidth: int voption`, `MinHeight: int voption`). Users constructing `GameConfig` records directly must add these fields. Users using `GameConfig.defaultConfig` or the DSL functions are unaffected.
- **Breaking:** `Trigger` discriminated union has new `KeyCombo of Set<KeyboardKey>` case. Users with exhaustive pattern matches on `Trigger` must handle the new case (or add a wildcard match).
- `GameContext.WindowWidth` and `GameContext.WindowHeight` now update automatically when the window is resized (e.g., via OS resize or fullscreen toggle). Previously these were set once at creation and never changed.

## [1.1.0] - 2026-06-01

### Added

- `ShadowDepthResources` struct bundling shadow shader + material + uniform locations.
- `ShadowPassHelpers` module with `collectShadowCasters`, `createDirectionalShadowCamera`, `renderShadowRegion`, `collectMeshDraws` helpers.
- `PipelineFunctions` module with `preScan`, `clearLights`, `warmMaterial`, `handleDrawMesh`, `handleDrawModel`, `handleDrawSkinnedMesh`, `handleDrawMeshInstanced`, `handleDrawBillboard`, `handleDrawBillboardBatch`, `handleLightCommand`, `applyCameraConfig` helpers.
- 2D normal map support: `SpriteState.NormalMap` field for per-pixel lighting on lit sprites. `LightContext2D` manages two shader variants (standard and normal-mapped) and switches between them via `BeginShaderMode`. The normal-map shader uses a 2D-compatible Half-Lambert lighting model (`NdotL = max(1.0 + dot(normal.xy, L), 0)`) for correct visual results with 2D light directions.
- `LightDraw.litAnimatedSprite` helper for animated sprites with automatic flip handling.
- `SpriteState` promoted to top-level type with builder DSL (`create`, `withNormalMap`, `withLayer`, etc.).
- `Animation3DClips` type for loading and querying 3D skeletal animation clips from `ModelAnimation[]`. Supports name-based and index-based lookup.
- `Animation3DState` struct for per-entity 3D animation playback with `play`, `playByIndex`, `playIfNot`, `blendTo`, `blendToByIndex`, `update`, and `applyToModel`. Uses `UpdateModelAnimation` for single-clip playback and `UpdateModelAnimationEx` for crossfade blending.
- `AnimatedMesh` type for shared GPU skinning data — extracts mesh and inverse bind pose from a `Model`. `computeBoneMatrices` performs pure keyframe interpolation (lerp/slerp) and inverse-bind-pose multiplication without mutating the model.
- GPU skinning vertex shaders (`forwardVertexSkinned`, `depthShadowVertexSkinned`) using raylib's `vertexBoneIndices`/`vertexBoneWeights` attributes and `boneMatrices[128]` uniform.
- `ForwardPbrPipeline.DrawSkinnedMesh` now uploads bone matrices and uses the GPU skinning shader (was a CPU skinning placeholder).
- `IAssets.ModelAnimations: path: string -> ModelAnimation[]` for loading skeletal animations from glb/gltf/iqm files.
- 42 unit tests for `Animation3DClips` and `Animation3DState` covering creation, playback, update, blending, and edge cases.
- ThreeDSample: Player character (`character-oobi.glb`) now animates with idle/walk/jump animations and 0.15s crossfade transitions.

### Changed

- **Breaking:** `ForwardPbrPipeline` refactored — original monolithic class (2167 LOC, 3× duplicated shader variants) replaced with parameterized implementation using `ShaderVariant` structs, self-contained command handlers, and decomposed helpers. Internal `PipelineContext` class eliminated. `MaterialKey.fromMaterial3D` now computed once per draw instead of 3×. Public API (`ForwardPbrPipeline` constructor and `IRenderPipeline3D` interface) is unchanged; consumers using the pipeline via `Renderer3D.create (ForwardPbrPipeline()) view` should see no behavioral difference. Consumers referencing internal types from the old implementation (e.g., `PipelineContext`) will need to update.
- **Breaking:** `LitSprite` command signature changed — now carries `LightContext2D * SpriteState` instead of 8 individual fields. Consumers must update pattern matches and `LightDraw.litSprite` call sites to use the new `SpriteState` type.
- **Breaking:** `IRenderPipeline3D.Execute` signature changed from curried (`gameCtx -> buffer -> rtPool -> unit`) to tupled (`gameCtx * buffer * rtPool -> unit`). All implementations and call sites must update.
- `SpriteState` moved from `Command2D` module to top-level `Mibo.Elmish.Graphics2D` namespace.
- `Renderer2D` refactored: extracted command dispatch into `module private CommandHandlers` with `RendererState` struct threaded `byref`. Post-processing extracted into `PostProcess2D` module. Class reduced from ~530 LOC to ~60 LOC of orchestration.
- `RenderBuffer2D.Sort` optimized: layer keys are now precomputed during `Add` (O(n) pattern matches) and sort uses `Array.Sort(keys, items, ...)` with primitive int comparisons, eliminating O(n log n) repeated pattern matching over the 37-case `Command2D` union. Sort is now stable — same-layer commands preserve insertion order via packed `int64` keys (layer in high 32 bits, insertion index in low 32 bits).
- Shadow rendering: `collectMeshDraws` now partitions draws (non-skinned first, skinned second) to minimize shader switches in the shadow pass.
- Shadow rendering: `renderShadowRegion` skips `computeNormalMatrix` and `SetShaderValueMatrix` when consecutive meshes share the same transform.
- Removed `lightsDirty` class field from `ForwardPbrPipeline`; handlers now check only `ShaderVariant.LightsDirty`. `handleLightCommand` sets all three variants' dirty flags directly.

### Fixed

- Shadow depth shader uniform locations were sourced from the forward skinned shader instead of the actual shadow depth shaders, causing incorrect shadow transforms.
- `BeginShaderMode` was missing for non-skinned meshes in the shadow depth pass — normal matrix was uploaded to whatever shader happened to be active.
- `lightsDirty` was never cleared after the first light upload, causing redundant light uniform uploads every draw call.
- Shadow caster loop bound used `shadowLocs.CasterCount` (a uniform location ID) instead of `atlasCfg.MaxCasters`.
- `uploadShadowUniforms` used a fragile `cameraPos <> Unchecked.defaultof<Vector3>` guard that failed when camera was at world origin.
- Material uniforms were always uploaded even when the same material was used consecutively; re-introduced material cache check via `LastMaterialKey`/`HasLastMaterial` on `ShaderVariant`.
- Duplicate `<summary>` XML doc block on `ForwardPbrPipeline` type.
- `preScan` test cases used `let` instead of `use` for `RenderBuffer3D`, leaking rented arrays from `ArrayPool`.

## [1.0.0] - 2026-05-30

### Added

- `Mibo.Raylib.Templates` NuGet package with `mibo-2d` and `mibo-3d` dotnet templates for scaffolding new Mibo Raylib game projects.
- PlatformerSample: 2D minimap with MVU pattern (`MinimapModel`, `Minimap.system`, `Minimap.view`). Bakes tiles into CPU image, uploads to GPU texture, draws as single sprite. Background matches sky color gradient.
- PlatformerSample: Variable jump height — releasing jump early cuts upward velocity for short hops.
- PlatformerSample: New tile types — `Spikes` (hazard), `Coin` (collectible, increments score), `Flag` (goal marker).
- PlatformerSample: World generation overhaul — 5 ground archetypes (pits, stairs, dense platforms, spikes, treasures), 3 air archetypes (empty, floating clusters, pillar chains), 2 underground archetypes (caves, dense). Biome-consistent tile grouping. XOR seeding.
- PlatformerSample: Spike collision → respawn, coin collection → score increment with grid removal.
- 2D multi-camera support: `Camera2DConfig` type with viewport (normalized coords) and clear color. Builders: `Camera2D.render`, `withViewport`, `withClear`, `splitScreenLeft`/`Right`/`Top`/`Bottom`, `overlay`. Command: `BeginCameraConfig`. Pipe wrapper: `Draw.beginCameraWith`.
- 2D shadow toggle: `LightContext2D.ShadowsEnabled` (default true). Commands: `EnableShadows`/`DisableShadows` per light context. When disabled, occluder segments are not uploaded to the shader, skipping shadow raymarching. Pipe wrappers in `Draw` and `LightDraw`.
- Builder DSL for all render struct types: `create` + `withX` pipeline for `SpriteState`, `TextState`, `Particle2D`, `AmbientLight2D`, `DirectionalLight2D`, `PointLight2D`, `Occluder2D`, `AmbientLight3D`, `DirectionalLight3D`, `PointLight3D`, `SpotLight3D`. Follows `Material3D` / `Camera3D` pattern.
- 3D rendering pipeline with CSM shadow maps (4-layer architecture: Renderer3D → Pipeline → Context → Commands).
- `ClusteredForwardPipeline` with Cook-Torrance PBR shading, CSM shadow mapping, and material caching.
- `Material3D` struct with PBR fields (albedo, roughness, metallic, normal, emission, opacity, tiling) and `fromRaylibMaterial` conversion.
- `DrawMeshInstanced` for GPU instanced rendering of many copies of the same mesh.
- `DrawBillboardBatch` for batched billboard rendering (particle systems).
- Debug drawing commands: `DrawGrid`, `DrawBoundingBox`, `DrawPoint3D`, `DrawRay` via `DrawImmediate`.
- `DrawModel` command that decomposes raylib `Model` into per-sub-mesh `DrawMesh` calls.
- `DrawImmediate` escape hatch for custom rlgl rendering.
- Render context uses camera state (BeginCamera/EndCamera) instead of hardcoding.
- Configurable `maxPointLights` and `ShadowConfig` for CSM cascades.
- `RenderBuffer3D` with `IDisposable` for `ArrayPool` return.
- Initial port of Mibo from MonoGame to raylib-cs.
- Core: `RaylibGame` runtime loop integrating Elmish architecture with raylib lifecycle.
- Core: `Program` module for configuring init, update, renderers, and services.
- Core: `GameConfig` for window and framerate configuration.
- Rendering: `RenderBuffer` for allocation-friendly command sorting and batching.
- Rendering: `Batch2DRenderer` for layer-sorted 2D rendering via raylib `DrawTexturePro`.
- Rendering: `Batch3DRenderer` for 3D rendering with custom Phong shader and lighting.
- Rendering: 2D lighting system (ambient, point, directional lights with CPU accumulation).
- Rendering: 3D lighting system (ambient, directional, point lights with GPU Phong shader).
- Rendering: Post-processing pipeline with multi-pass `PostProcessPass` and embedded GLSL shaders.
- Rendering: Default shader library (`DefaultShaders.fs`) with Phong and tint shaders.
- Rendering: `ModelHelper.setMaterialShader` for patching model material shaders (required by raylib).
- Input: `InputMap` and `ActionState` types for semantic input mapping.
- Input: `Keyboard.poll` for polling keyboard state against a map.
- Assets: `IAssets` service for loading and caching Textures, Fonts, Sounds, and Models.
- Time: `FixedStep` configuration for deterministic physics/simulation steps.
- Animation: `Mibo.Animation` module for 2D sprite animation with `SpriteSheet.fromFrames`, `SpriteSheet.fromGrid`, `AnimatedSprite.update`, and layer-sorted rendering via `RenderCmd2D.DrawSprite`.
- Code-first level design: `Mibo.Layout` and `Mibo.Layout3D` modules for 2D and 3D grid-based levels (planned).
- Documentation: Official documentation site with guides for all modules.
- Sample: 2D Platformer with procedural terrain, sprite animation, day/night cycle, and dynamic lighting.
- Sample: 3D Platformer with procedural levels, custom Phong shader, camera-relative controls, and day/night GPU lighting.
- `PointLight3D` gains `Intensity` and `Falloff` fields (parity with `PointLight2D`). Forward and instanced shaders upload per-light intensity and falloff uniforms; attenuation uses `pow(clamp(1 - dist/radius), falloff)`.
- ThreeDSample: 3D particle system with confetti burst on jump (`ParticleModel`, `spawnConfetti`, `particleSystem`). Uses `Raylib.DrawBillboardRec` for billboard rendering via the default rlgl shader.
- ThreeDSample: Particle count added to diagnostics display.

### Changed

- `DrawBillboard` and `DrawBillboardBatch` now use `Raylib.DrawBillboardRec` instead of custom mesh + matrix approach. Billboards render correctly using raylib's native billboard API with the default rlgl shader.
- ThreeDSample: Minimap rendering now bakes blocks into a CPU-side `Image` + GPU `Texture2D` instead of emitting ~1600 individual `FillRect` commands per frame. The texture is rebuilt every N frames and drawn as a single `Sprite`, reducing per-frame draw calls from ~1600 to 5 (1 sprite + player marker + direction line + border).
- ThreeDSample: Refactored `MinimapView` into proper MVU module (`Minimap`) with `MinimapModel`, `system`, and `view`. Block collection and texture baking moved from the view function into the update pipeline.
- ThreeDSample: Moved text overlay from `View.fs` `DrawImmediate` escape hatch to a proper `Diagnostics` 2D module with `Command2D.text`. Both minimap and diagnostics share a single 2D renderer.
- ThreeDSample: Sun/moon cycle now uses model time instead of hardcoded noon. Arc distance scales with loaded world size via `arcRadius`.
- ThreeDSample: Mushroom light collection moved from `View.fs` to `mushroomLightSystem`. Lights stored as `PointLight3D` on the model, `CastsShadows = false` for performance.
- ThreeDSample: Pre-computed lighting state (`LightingModel`) stored on `GameModel`, populated by `lightingSystem`. View reads from model instead of computing DayNight values.

### Removed

- Dead code cleanup: removed unused `PostProcessConfig` type, `Renderer2D.createWithConfig`, `Renderer3D.createWithConfig`, and empty `RenderCommand.fs`/`RenderContext.fs` stub files.
- ThreeDSample: Removed dead `DayNight.State`, `DayNight.initial`, `DayNight.update` (never used).
