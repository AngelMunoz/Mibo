# Changelog

## [Unreleased]

### Added

- **MonoGame: host & program** — `MiboGame(program)` is the MonoGame game host (subclasses `Microsoft.Xna.Framework.Game`, drives the shared `ElmishLoop`). `MonoGameProgram.withInputMapper` registers the MonoGame-backed input mapper (and calls `withInput`). `MonoGameGameContext` accessors (`getGraphicsDevice`/`getContentManager`/`getGame`) retrieve MonoGame handles from the Core `GameContext` service registry. MonoGame `IAssets` exposes the typed loaders (`Texture`/`Font`/`Sound`/`Model`/`Effect`/`ModelAnimations`/`AnimatedMesh`) and extends the portable `IAssetCache`.
- **Core: `IAssetCache`** — backend-neutral asset cache interface (`Get`/`Create`/`GetOrCreate`/`Clear`/`Dispose`) that portable code depends on; the backend `IAssets` extends it.
- **Docs: migration guide** — `docs/migration-from-monogame.md`, a before/after guide for moving from the original monolithic `Mibo` package to `Mibo.Core` + `Mibo.MonoGame` (program setup, GameContext, input, assets, the renamed 2D/3D rendering stacks, animation, cameras, the content pipeline, and a Raylib-backend appendix).
- **MonoGame 3D: per-group custom shading** — `Draw3D.beginEffect`/`endEffect` shade the draws between them with a user-supplied `Effect` instead of PBR. The effect inherits the scene's camera, lights, shadows, material, bones, and a `time` clock **by declaring the matching uniform names**; uniforms it doesn't declare are skipped. Scopes don't persist across cameras. Lets you render toon/water/vignette alongside the default PBR scene.
- **MonoGame 3D: extensible pipeline** — `ForwardPipelineBase` (abstract; owns the gather + frame orchestration + a virtual `Shade`) with `ForwardPipeline` as the thin PBR subclass. Override `Shade` to plug a different shading strategy; it receives the per-frame scene (lights, bones, shadow output, `time`). Register the same way: `Renderer3D.create (ForwardPipeline()) view`.
- **MonoGame 3D: `drawImmediate` receives a `SceneContext`** — the raw `GraphicsDevice` plus the gathered scene (camera, lights, shadows, `time`). For fully-custom draws (water/refraction, screen-space, multi-pass) that want device control without re-gathering the scene.
- **MonoGame 3D: `time` uniform** in the scene-data contract. Shaders opt into animation (ripples, flowing textures) by declaring `time`. `IRenderPipeline3D.Execute` gains a `GameTime` argument (MonoGame backend only).
- **MonoGame 3D: PBR shading** — models, animated models, primitives, and instanced geometry route through a Cook-Torrance PBR effect (ambient + 1 directional + up to 8 point + up to 4 spot lights, emission, opacity, tiling, optional normal maps). Imported models keep their authored look; a `MaterialKey` short-circuit skips re-uploading unchanged materials. Per-draw `normalMatrix`; instanced normals transform by the per-instance world matrix; the instanced shader negates the directional light direction.
- **MonoGame 3D: shadows** — directional, point, and spot lights that set `CastsShadows` render depth into an `R32F` atlas (sampled with 3×3 PCF; OpenGL uses `RasterizerState` polygon-offset + a `shadowTexelSize` uniform since SM3.0 has no `dFdx`/`textureSize`). Per-light frustum culling skips casters outside each light's view (accounting for transform scale). Animated models cast correct shadows (skinned casters render depth-only with matching bone semantics; not frustum-culled — a bare mesh part has no reachable bounds). A per-light shadow index replaces the per-fragment caster scan. Configure via `ShadowAtlasConfig`/`ShadowBiasConfig`; `EnableShadows`/`DisableShadows`/`SetShadowOrigin` are honored. Only the first shadow-casting directional light is registered (the shader samples slot 0).
- **MonoGame 3D: skeletal animation** — `AnimatedModel` plays/blends animation clips loaded at runtime from raw model files (`.glb`/`.gltf`/`.fbx`/…) via AssimpNetter (the content pipeline discards animation data; loading both `ModelAnimations` + `AnimatedMesh` for the same path parses once). `Draw3D.drawAnimatedModel` computes the bone palette and routes through GPU skinning; the caller never handles a `Matrix[]`. Cross-fade blend targets respect `Loop = false`. Load via `IAssets.ModelAnimations`/`AnimatedMesh` (filesystem paths — copy the raw model to your output directory). Adds the `AssimpNetter` dependency.
- **MonoGame 3D: instancing** — `Draw3D.drawInstanced` renders bulk geometry via hardware instancing (dual vertex stream) through the PBR `Instanced` technique.
- **MonoGame 3D: billboards + lines** — `Draw3D.drawBillboard`/`drawBillboardBatch`/`drawLine3D` (billboard UVs normalized `[0,1]`; line staging pooled).
- **MonoGame 3D core** — `Camera3D` (perspective/orthographic, orbit, screen-point-to-ray), `Culling`, `Primitive3D` (unit cube/sphere/cylinder/plane/torus/cone meshes), `Material3D`, the `Draw3D` DSL, and a pluggable `IRenderPipeline3D`. `EndCamera` resets camera state so draws after it don't use stale matrices.
- **MonoGame: 2D rendering stack** — sprites, text, shapes, cameras, custom shaders, render targets, 2D lighting (point/directional/occluders), particles, post-processing, and sprite-sheet animation. Parity with the Raylib `Graphics2D` surface. Includes: mouse back/forward buttons on the `MouseDelta` stream; `Renderer2D.Draw` always closes batches and releases RTs even when a frame throws; per-instance lit-sprite quad buffer; centroid-radiating rounded-rect fill; `AddTriangleFan` `closeLoop` for open fans; `LightContext2D.Dispose` respects caller effect ownership; float-space particle removal; allocation-free occluder upload; multi-pass post-process; `RenderTargetPool` idle-target cap (window-resize leak).
- **Core: backend-neutral input** — `KeyCode`/`MouseButtonCode`/`GamepadButtonCode`/`GestureKind` + `IInput`/`IInputMapper<'Action>` live in `Mibo.Core`, so input bindings are portable across backends.
- **Core: `Cmd.Msg`** — a zero-allocation `Cmd` case for `Cmd.ofMsg` (no delegate wrap).
- **Core: `Program` builder** gains `withServiceRegistration` for backend-specific service registration.
- **Core: `Mibo.Core` project** — backend-agnostic home for `Cmd`/`Sub`/`GameTime`/`Program`/`GameContext`/layout/`HeadlessProgram`/`ElmishLoop`. The Raylib backend now references it; namespaces are unchanged. Includes `Mibo.Core.Tests`.
- **Raylib: `RaylibProgram.withInputMapper`** — the raylib-specific input-mapper builder (decoupled from the shared Core `Program`).
- **Docs:** multi-backend documentation. The site now covers the Core, Raylib, and MonoGame packages: the rendering, shaders, assets, input, and lighting pages show both backends side-by-side where their APIs diverge (the pipeline types, GLSL vs HLSL effects, loose-file vs content-pipeline assets, and viewport coordinate conventions). The original raylib-only docs are preserved as a frozen archive for the prior release.

### Changed

- **Raylib — Breaking:** `Mibo.Elmish.Camera` is now a `[<Struct>]` (was a reference record). It flows through the view function every frame, so stack-allocating it removes per-frame Gen0 pressure. Code that held it by reference or relied on reference-identity semantics needs review.
- **Raylib:** 3D point/spot shadow lookup is now an O(N) indexed read instead of an O(N·M) per-fragment caster scan (no visual change; faster with many shadow-casting lights). Only the first shadow-casting directional light is registered.
- **Core — Breaking:** `Cmd<'Msg>` has a new `Msg of 'Msg` case. Exhaustive pattern matches must handle it (or use a wildcard). `Cmd.ofMsg` returns `Msg` instead of wrapping in an `Effect`.
- **Core — Breaking:** input uses backend-neutral codes instead of raylib enums — `InputMap.key` takes `KeyCode` (not `Raylib_cs.KeyboardKey`), `InputMap.mouse` takes `MouseButtonCode` (not `int`), and `Trigger.MouseBut`/`GamepadBut` became `MouseButton`/`GamepadButton`. Bindings are now portable.
- **Raylib — Breaking:** `Program.withInputMapper` moved to `RaylibProgram.withInputMapper` (raylib backend only). Call sites change `Program.withInputMapper map` → `RaylibProgram.withInputMapper map`.
- **Core — Breaking (behavioral):** multiple renderers now draw in the order you add them (previously the last-added drew first). Review your setup if you stack renderers.

### Fixed

- **Core:** `Cmd.batch` no longer silently drops a lone `NowAndDeferNextFrame` effect.
- **Core:** `HeadlessRunner.StepUntil` off-by-one fixed (the predicate is now tested after each step; the loop exits immediately when met).
- **Raylib:** `pollMouse` filters `Unknown` button codes; `InputMapper` binds the raylib key once per trigger (was three times).
- **Docs:** the MonoGame migration guide no longer claims the backend ships without renderers — it now documents the full default pipeline and 2D/3D stacks that are available.

### Removed

- **Raylib:** 11 stale duplicate test files from `Mibo.Raylib.Tests` (leftovers from the `Mibo.Core.Tests` extraction; never compiled).

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
