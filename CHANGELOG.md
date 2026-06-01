# Changelog

## [Unreleased]

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

## [1.0.0] - 2026.05.30

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
