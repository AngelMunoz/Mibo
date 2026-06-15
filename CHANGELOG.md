# Changelog

## [Unreleased]

### Added

- `Mibo.Core` project: backend-agnostic home for `Cmd`/`Sub`/`GameTime`/`DispatchMode`/`FixedStep`/`System`/`RenderBuffer`/`IRenderer`/`GameContext`/`Program`/`GameConfig`. The `Mibo.Raylib` project now references `Mibo.Core`. No API changes; all types remain in the `Mibo.Elmish` namespace. See `docs/migration-to-vnext.md` for the vNext roadmap.
- Backend-neutral input contracts in `Mibo.Core` (namespace `Mibo.Input`): `KeyCode`, `MouseButtonCode`, `GamepadButtonCode`, `GestureKind` (struct DUs, `RequireQualifiedAccess`), the delta types, the `IInput`/`IInputMapper<'Action>` contracts, `Trigger`/`InputMap<'Action>`/`ActionState<'Action>`, and the `Keyboard`/`Mouse`/`Touch`/`Gamepad`/`Gesture` subscription modules. Backends supply concrete `IInput`/`IInputMapper` implementations.
- Raylib↔Core input translation modules in the raylib backend: `KeyCode.ofRaylibKey`/`toRaylibKey`, `MouseButtonCode.ofRaylibButton`/`toRaylibButton`, `GamepadButtonCode.ofRaylibButton`/`toRaylibButton`, `GestureKind.ofRaylibGesture`/`toRaylibGesture`.
- `IAssetCache` interface in `Mibo.Core` (`Mibo.Elmish` namespace): the backend-neutral generic asset-cache contract (`Get<'T>`/`Create<'T>`/`GetOrCreate<'T>`/`Clear`/`Dispose`). The raylib backend's `IAssets` now extends `IAssetCache`; all existing calls compile unchanged. Portable code can retrieve an `IAssetCache` from `GameContext` to cache custom assets without referencing a backend.
- `Program` builder functions in `Mibo.Core` (`Mibo.Elmish` namespace): `mkProgram`, `withConfig`, `withRenderer`, `withTick`, `withFixedStep`, `withDispatchMode`, `withSubscription`, `withAssets`, `withAssetsBasePath`, `withInput`, plus a new `withServiceRegistration` hook for backend-specific service registration. The `Program` record gained a `ServiceRegistrations: (GameContext -> unit) list` field that hosts invoke before `Init`.
- `RaylibProgram.withInputMapper` in the raylib backend (`Mibo.Elmish` namespace): the raylib-specific `withInputMapper`, now decoupled from the Core `Program` builder. It registers the raylib-backed `IInputMapper` via a `ServiceRegistrations` callback so Core never references the raylib factory.
- `ElmishLoop<'Model,'Msg>` and `LoopCore<'Model,'Msg>` in `Mibo.Core` (`Mibo.Elmish` namespace): the shared message-processing loop extracted from the duplicated code in `RaylibGame` and `HeadlessRunner`. Both hosts now delegate to `ElmishLoop`; `Program` and `HeadlessProgram` project to `LoopCore` via `ElmishLoop.coreOfProgram` / `HeadlessProgram.toLoopCore`.
- `HeadlessProgram`, `HeadlessRunner`, and the `HeadlessProgram` builder module moved from the raylib backend to `Mibo.Core` (pure F#, no backend dependencies). All existing user code keeps working unchanged — types stay in the `Mibo.Elmish` namespace.
- `Mibo.Layout` and `Mibo.Layout3D` modules moved from the raylib backend to `Mibo.Core`. 17 files of pure layout geometry (2D grids/hex/spatial/platformer/top-down/layered + 3D grids/hex/spatial/interior/terrain) over `System.Numerics`. Namespaces preserved; all existing code compiles unchanged. `Layout3D/Renderer3D.fs` (the raylib instanced-draw bridge) stays in the raylib backend.

### Changed

- **Breaking:** the input surface now uses backend-neutral codes instead of raylib's native enums. See `docs/migration-to-vnext.md` (Phase 1b) for the full migration guide. Highlights:
  - `InputMap.key` takes `KeyCode` instead of `Raylib_cs.KeyboardKey`. Bindings become portable across backends.
  - `Trigger` cases renamed: `MouseBut of int` → `MouseButton of MouseButtonCode`; `GamepadBut` → `GamepadButton of int * GamepadButtonCode`.
  - `InputMap.mouse` takes `MouseButtonCode` instead of `int`.
  - `MouseDelta.Buttons` holds `MouseButtonCode[]`.
- **Breaking:** `Program.withInputMapper` moved to `RaylibProgram.withInputMapper` (raylib backend only). The factory is backend-specific, so the function can no longer live in the shared Core `Program` builder. Call sites change `Program.withInputMapper map` → `RaylibProgram.withInputMapper map`. No samples used this path (they use the subscription-based `InputMapper.subscribeStatic`), so no sample changes were required. See `docs/migration-to-vnext.md` (Phase 1d).
- **Breaking (behavioral):** renderer draw order is now correct. Previously, `withRenderer` prepended to the list but the runtime iterated without reversing, so the last renderer added drew first. Now the runtime reverses `program.Renderers` before iterating, matching the existing `Config`/`ServiceRegistrations` pattern. Renderers draw in the order you add them. This is a behavioral change that will not produce compiler errors — review your renderer setup if you use multiple renderers.

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
