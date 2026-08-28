# Changelog

## [Unreleased]

## [4.5.2] - 2026-08-28

### Fixed

- **Raylib 3D, MonoGame 3D:** instanced draws now respect the material's `Opacity` like regular draws. Previously a transparent instanced batch rendered as a solid object on MonoGame (the frame's opaque blend state was never switched) and blended immediately with depth writes on raylib, so it occluded geometry drawn behind it; now `0 < Opacity < 1` defers to the sorted transparent pass (blended, no depth writes) and `Opacity <= 0` draws nothing. Transparent units also stop casting shadows and stop appearing in the depth texture that depth-based post-process effects sample. Classification is per instance and per part, not per batch: on MonoGame the instances whose per-instance tint alpha is below 255 defer and blend while the opaque instances stay inline with their shadows and depth writes, and skinned + instanced models with mixed part opacities keep their opaque parts inline. Each deferred unit sorts by the distance to the average position of the instances it carries, so ordering between those instances stays submission order. The material transparency docs state these rules and the custom-effect exception (`beginEffect`/`endEffect` scopes draw as-is; `drawImmediate` gives full blend/depth control).

## [4.5.1] - 2026-08-21

### Changed

- **Mibo.Adaptive:** bump `Mibo.Adaptive` to 1.0.1.

## [4.5.0] - 2026-08-18

### Added

- **Core:** the `Mibo.Diagnostics` namespace — `FrameStats`, `FrameProfiler`, and the `Diagnostics` module. Opt in by building a `FrameProfiler` and passing it with `Program.withProfiler` / `HeadlessProgram.withProfiler` / `AdaptiveProgram.withProfiler`; without one, nothing is registered and nothing runs. The host registers the supplied profiler in the `GameContext`, so `Diagnostics.getProfiler` / `tryGetProfiler` works from any code that holds the context, on every runtime including headless runners. `FrameProfiler.Snapshot` holds windowed frame rates, simulation step rate, update and draw cost, worst frame time, thread allocation, generation 0/1/2 collection counts, slow frame count, and, where the backend reports them, per frame GPU counters. `Diagnostics.format` turns a snapshot into two overlay lines. `FrameProfiler.Enabled` turns measurement on and off at runtime; a fresh window starts on re-enable. While off, every stamp and request does nothing. The per frame cost when on is a handful of stopwatch stamps with no allocation. Fixed step drops and the MonoGame fixed step catch up flag count as slow frames.
- **Raylib:** screenshots. `FrameProfiler.RequestScreenshot(path)` queues a capture that the host writes as a PNG at the given path when the frame finishes drawing. Requires a profiler built with `canScreenshot = true`. The path is used as given (no working directory join).
- **MonoGame:** screenshots on all four backends (DirectX 11, DirectX 12, OpenGL, Vulkan), queued the same way, saved as a PNG of the back buffer. Requires a profiler built with `canScreenshot = true`. `FrameStats` also carries the frame's draw call, primitive, and texture bind counts from `GraphicsDevice.Metrics`. On the OpenGL backend the capture is flipped to match the other backends.
- **Adaptive:** `AdaptiveHeadless` accepts an optional `profiler` constructor argument that wins over the program's; when neither is set, no profiler is created.
- **Adaptive:** `Init` can defer work — `AdaptiveFrameContext` now exposes the intent queue (`ctx.Intents`) alongside `Update`'s `AdaptiveContext`. Work `Init` posts with `post` runs at the startup drain, right after `Init` returns and before the first frame is forced, so the first frame includes its effects; `postNextFrame` lands at the first step's boundary, before the first update; `postTask`/`postAsync` start at the startup drain and complete at a later post drain. This is the adaptive counterpart of the `Cmd` the MVU `init` returns. The subscription projection receives the same context and must not post: it runs once per step, so its work would land a step late.

## [4.4.0] - 2026-08-18

### Added

- **Window management:** `GameConfig.Resizable` and `GameConfig.WindowMode` (`Windowed` / `BorderlessFullscreen` / `Fullscreen`) set the startup window, via the `GameConfig.withResizable` / `GameConfig.withWindowMode` builders. Setting `MinWidth`/`MinHeight` still enables resizing as before. Every runtime host applies the config: the raylib hosts set the window flags before `InitWindow`; the MonoGame hosts set `AllowUserResizing` / `IsFullScreen` / `HardwareModeSwitch` before the `DeviceConfig` callbacks, which can still override.
- **Window management:** the `IWindow` service (`Mibo.Windowing`), registered by every host unconditionally (like `IAssets`) and retrieved with `Window.getService` / `Window.tryGetService`. It offers `Mode` / `IsFullscreen` queries, `SetMode`, `ToggleFullscreen` (a windowed startup toggles to borderless), and `SetSize` (windowed mode only). The context dimensions track the new size from the next frame.
- **MVU:** `Program.mkProgramCtx`, `Program.withUpdateCtx`, and `HeadlessProgram.mkHeadlessCtx` build a program whose `Update` receives the `GameContext` — the same context `Init`, `Subscribe`, and the renderer callbacks already get. Programs built with `mkProgram` run unchanged.

### Changed

- **Potentially breaking:** `LoopCore.Update` now takes the `GameContext`. Only code that constructs `LoopCore` directly is affected; `Program` and `HeadlessProgram` users are not.

### Fixed

- **MonoGame:** the backbuffer now follows window resizes. Previously a resized window kept the startup backbuffer and showed it stretched, and `gd.Viewport` (which the 3D pipelines read for the camera aspect) disagreed with the context dimensions (which picking and HUD code read). The hosts sync the backbuffer to the client area once per frame in windowed mode; the context dimensions always track the backbuffer.
- **MonoGame:** `MinWidth`/`MinHeight` are now enforced — MonoGame has no minimum-window-size API, so the hosts clamp the backbuffer to the minimum and the window sizes itself to match. Previously the minimum size was applied on raylib only.
- **MonoGame:** borderless fullscreen no longer stretches the old backbuffer. Entering it pre-sizes the backbuffer to the desktop mode (the GDM does not adjust it for borderless), the per-frame sync keeps it matched to the client area, and leaving fullscreen restores the last windowed size.

## [4.3.0] - 2026-08-16

### Added

- **MonoGame 3D:** `ModelParts.ofModel(model)` turns a content-pipeline model into per-part records ready for `meshSlice`/`instancedSlice` — each part draws straight from the model's shared buffers (no per-part buffer copies) with its slice offsets, its absolute bone transform, and its material; results are cached per model and must be treated as read-only. Static models only — skinned parts render in their bind pose (use `animatedModelInstanced` for those). `InstancedRenderContext` accepts a parts resolver, so grid renderers can instance content models directly with correct bones and offsets.
- **MonoGame 3D:** `Draw.meshSlice`/`Draw.instancedSlice` draw a slice of a mesh within shared content-pipeline vertex/index buffers — pass the part's `vertexOffset`/`startIndex` (`0, 0` for self-contained buffers) and the mesh draws that part's own geometry instead of the first part's triangles. The offsets default to 0, so existing self-contained meshes call them unchanged. The mesh record must describe the part: `PrimitiveCount` is the part's triangle count and `Bounds` its local-space bounding sphere. Shared buffers with more than 65,536 vertices need a 32-bit index buffer (the merged-parts pipeline widens automatically).
- **Adaptive:** deferred work — the adaptive counterpart of `Cmd`. `AdaptiveContext.Intents` takes `unit -> unit` work from `Update` (or any thread) and runs it at the moment the name says: `post` (after this step's `Update`, drained until empty so reaction chains settle before the frame is forced), `postNextFrame` (top of the next step), `postTask`/`postAsync` (background work; the completion returns on the owner thread where root writes are legal). `AdaptiveHeadless.Post` injects work without holding the `Update` context.
- **Adaptive:** subscriptions — the adaptive counterpart of `Sub`. `AdaptiveInit.withSubscriptions` takes a keyed `amap` of `AdaptiveSub` specs (dynamic, state-driven subscription sets can use `AMap.custom`); the runner diffs it per step — gated on the map's version, so clean steps do no diff work — to attach, keep, or detach, and detaches everything on `Dispose`. Attach callbacks receive a posting surface: `Post` queues work for the frame boundary before `Update`, so input published right before a `Step` reaches the sim in that same step. `AdaptiveSub.ofObservable` and `AdaptiveSub.ofTimer` build subscriptions from the common sources, so most code never writes `Attach` by hand.
- **Adaptive:** semantic input mapping — `InputMapper.subscribeAdaptive`/`subscribeStaticAdaptive` (both backends) build an `ActionState` from the `IInput` delta observables and an `InputMap`, writing it into a `cval<ActionState<_>>` root through the pre-step lane; no message, no dispatch. The write settles before `Update`, so the update phase and the frame force read the mapped actions like any other root. Edge events (`Started`/`Released`) accumulate across the deltas within a step via the new `ActionState.mergeEdges` (pure, unit-tested in Core); `Held`/`Values` stay last-wins. The subscription clears the consumed edges after `Update` (after each fixed sub-step's `Update`; a fixed-step frame with no sub-step defers the clear to the next sub-step, so the edges are never lost) and before the frame is forced, so `Update` reads each edge exactly once; no manual clearing is needed, and a manual `ActionState.nextFrame` write stays legal and free. Consume `Held`-style state as projections. The `IInputMapper` service registered by `withInputMapper` is registered only — nothing polls it; the docs now say so.
- **Templates:** `Mibo.Templates` (the `mibo-2d`/`mibo-3d`/`mibo-mg-2d`/`mibo-mg-3d` starters) is now packed and published with each release, versioned from the repo changelog like the libraries.
- **Templates:** adaptive starters — `mibo-2d-adaptive`/`mibo-3d-adaptive` (raylib) and `mibo-mg-2d-adaptive`/`mibo-mg-3d-adaptive` (MonoGame) scaffold the adaptive runtime: roots plus a derived projection built once, an update that writes roots, a pure frame pack, input through the adaptive input-mapper subscription, and `AdaptiveRaylibGame`/`AdaptiveMonoGameGame` host wiring. The existing starters keep their names and stay MVU.

### Changed

- **Docs:** reorganized around the two program runtimes — the MVU guides (Elmish, programs, commands, subscriptions, system pipeline, headless, MonoGame migration) live under their own section; a new Adaptive section mirrors the MVU coverage (overview, programs, intents, subscriptions, systems, scaling, services, headless, background work, derived state); and a new Mibo.Adaptive section documents the incremental-computation library as its own package. Runtime-specific patterns (composable systems, background work, pre-computed state) move under MVU; Patterns keeps the runtime-agnostic pages (pooled particles, layered rendering). The scaling guide splits per runtime — the MVU complexity ladder lives under MVU, a new adaptive scaling ladder under Adaptive. The v1 (raylib-only) archive leaves the sidebar and is linked from the front page instead. Docs now build on fsdocs 22.1.0.
- **Breaking (experimental): Adaptive:** `AdaptiveProgram.Init` and the subscription projection now receive the new `AdaptiveFrameContext` (framework roots + `GameContext`, no work queue); only `Update` receives `AdaptiveContext` with `Intents` — the frame builder cannot defer work by construction.
- **Breaking (experimental): Adaptive:** `AdaptiveHeadless.RunAsync` yields `StepOutcome<'Frame>` (`GameTime` + `Frame`) instead of `struct (GameTime * 'Frame)`.

### Deprecated

- **MonoGame 3D:** `Draw.mesh`/`Draw.instanced` are deprecated on MonoGame — they draw from buffer offset 0, which renders the first part's triangles for a mesh wrapping a part of a shared content-pipeline buffer. Use `meshSlice`/`instancedSlice` (see Added); raylib's `mesh`/`instanced` are unchanged.

### Removed

- **Breaking (experimental): Adaptive:** the restart machinery (`AdaptiveContext.RestartRequested`, `AdaptiveHeadless.Restart()`) — dispose of the runner and create a new one.

### Fixed

- **Input mapping (MonoGame):** the subscription mapper snapshots only the input devices the `InputMap`'s triggers reference — previously every input event (including the per-frame mouse-move event) polled the keyboard, the mouse, and four gamepads, even when the map binds keys only. Behavior is unchanged; the unmapped devices are simply not fetched.
- **Input mapping:** `Started` in the subscription mapper's `buildActions` (both backends) is now a per-ACTION transition, matching core `ActionState.update`: it fires only when the action was NOT already held. Previously each binding press fired `Started` — pressing Left while A already holds the same action re-fired it — while `Released` only fired at full release, so an add-on-Started/subtract-on-Released consumer went +N/−1 and stuck (Defli3D's keyboard pan locked when mixing WASD with arrow synonyms). The `IInputMapper` service's poll loop applies the same transition rule now.
- **Mibo.Adaptive:** the NuGet package now ships its own readme — including the credit note for its AdaptiveSlop origin — instead of the repo root readme.
- **Raylib 3D:** alpha-blended billboards — particles and other transparent quads — no longer write depth, so a transparent billboard no longer hides the geometry or particles behind it.
- **Raylib 2D:** full-circle ring outlines no longer show a radial line where the ring closes. Partial arcs keep their end caps.
- **Raylib 2D:** filled triangles, triangle fans and triangle strips now render in any winding order. Clockwise point lists drew nothing on raylib while MonoGame rendered them; both backends now agree.
- **MonoGame 2D:** ring outlines draw as two clean rings — the outline no longer fills in as a band, and full circles no longer show a radial seam where the ring closes. Partial arcs keep their end caps.
- **MonoGame 2D:** sprites and shapes now keep their draw order when sampler-state commands are interleaved — text no longer renders behind shapes on top of it.
- **MonoGame 2D:** flushing the shape batch no longer copies its vertices into a new array — scenes that flush often (HUDs that interleave text and shapes) stop producing garbage on every flush.
- **MonoGame 3D:** cube, plane and cone primitives now face outward under the default cull mode — the cube no longer shows its interior and the plane no longer vanishes from one side.
- **2D:** triangle fans now auto-close the rim on every backend — the last rim vertex connects back to the first, so a full convex rim fills its polygon. raylib previously left the wedge between the last and first rim vertex unfilled while MonoGame closed it.
- **MonoGame 3D:** translucent materials, alpha-blended billboards and alpha-blended line draws now fade their tint with alpha — a material at `Opacity 0.3` shows 30% of its color over the background. They previously added the tint at full strength while only fading the background, so translucent tints now match the raylib backend.
- **MonoGame (DirectX 12):** lines now render on the DX12 backend. Thin lines, line strips and 3D lines drew as filled shapes or vanished there — the DX12 runtime interprets line topologies as triangles. On that backend 2D lines now draw as thin quads and 3D lines as camera-facing quads, close to the native lines the other backends draw.
- **Templates:** the starter agent files link to the MVU guides at their new locations. The docs reorganization moved Elmish, programs, commands, subscriptions, system, scaling, headless, services, composable systems and background work under the MVU section, and the old flat links in the shipped starters were dead.

## [4.2.0] - 2026-08-11

### Added

- **Mibo.Adaptive (experimental)** — the adaptive-data library, adopted from AdaptiveSlop in its entirety and renamed (`AdaptiveSlop.Core` → `Mibo.Adaptive`). Dependency-free: the core never references Mibo; the Mibo integration (`AdaptiveProgram`/`AdaptiveHeadless`) lives in Mibo.Core, and `AdaptiveRaylibGame`/`AdaptiveMonoGameGame` hosts ship in the raylib/MonoGame backends. Ships with its own changelog and versioning, an xunit + FsCheck test suite, and BenchmarkDotNet benchmarks. The AdaptiveSlop submodule is gone.
- **Adaptive (experimental):** `AdaptiveHeadless` runner — a pull-based architecture as an alternative to MVU. State lives in changeable roots, derived state in adaptive projections, and the runner forces the frame's projections at the end of each `Step`, so reads are O(1) and unchanged state recomputes nothing — an idle frame costs no recomputation. `Step`/`StepN`/`StepUntil`/`Run`/`RunAsync` and observers mirror `HeadlessRunner`; there is no `'Msg`, no `Cmd` and no `Sub` — handlers write roots and run effects directly.
- **Adaptive (experimental):** windowed hosts — `AdaptiveRaylibGame` (raylib) and `AdaptiveMonoGameGame` (MonoGame) run an adaptive world in a window with the MVU ceremony removed: no `Program` builder, no Cmd/Sub machinery, input registered and polled when opted in via `withInput`, services read straight from the context. A world can rebuild itself — the `RestartRequested` root and the runner's `Restart()` re-run `Init` for a fresh graph, fresh subscriptions and a reset clock.
- The `Mibo.Adaptive` integration is marked experimental: using `AdaptiveProgram` emits a compiler warning, and the API carries no stability guarantees while it is under active development.

### Fixed

- **MonoGame 2D:** rotated sprites now face the requested direction. `SpriteState.Rotation` is documented in degrees, but the backend passed it straight to MonoGame's radians-based sprite rotation, so any rotated sprite — projectiles, enemy bodies, turrets — spun in place or pointed diagonally instead of along its heading. Lit sprites rotate correctly as well.

## [4.1.0] - 2026-08-10

### Added

- **MonoGame 3D, Raylib 3D:** per-material transparency. Materials with `0 < Opacity < 1` render alpha-blended after all opaque geometry, sorted far-to-near by camera distance, with depth writes off for the sorted pass (depth test stays on) on both backends; `Opacity <= 0` renders nothing. Transparent geometry does not cast shadows and is excluded from the scene-depth pre-pass — the depth pass is binary — so `PostProcessWithDepth` effects (fog, depth-of-field) sample opaque-only depth on both backends. Instanced and `beginEffect`/`endEffect`-scoped transparent draws are not deferred: they render immediately and unsorted, so they may blend incorrectly against sorted transparents — prefer opaque materials for them.

### Changed

- **Breaking (behavioral): MonoGame 3D, Raylib 3D:** models or effects whose albedo/effect alpha is below 1 (e.g. a raylib albedo `Color.A` below 255, or a MonoGame `BasicEffect.Alpha`/`SkinnedEffect.Alpha` below 1) previously rendered fully opaque and cast shadows. They now render alpha-blended and no longer cast shadows or write scene depth. Games that relied on partially-transparent materials rendering as opaque casters must set `Opacity = 1` (raylib: `Color.A = 255`) to restore the previous behavior.

### Fixed

- **Core:** `worldToCell` on square (`Grid2DSpatial`), voxel (`Grid3DSpatial`) and the layer axis of 3D hex (`Hex3DSpatial`) grids now returns the cell that _contains_ the world position. They previously snapped to the nearest cell corner, so any point in the second half of a cell (and, because of banker's rounding, the exact center of an odd-indexed cell) reported the next cell over. Hex 2D and the hex (XZ) plane of 3D hex are unchanged — those resolve to the nearest hex center as before.

## [4.0.0] - 2026-08-07

### Changed

- **Core:** spatial grid queries are now allocation-free apart from the single result array they return. `neighbors*`, `inRange`, `ring`, `spiral`, `lineOfSightCells`, `floodFill` and `findPath` on `Grid2DSpatial`, `Hex2DSpatial`, `Grid3DSpatial` and `Hex3DSpatial` no longer allocate intermediate collections, queues, heaps or throwaway grids per call — 3D hex distance and neighbor queries previously allocated a full grid-sized array per call. Hex A\* now uses the exact hex-distance heuristic (was an underestimate that expanded extra nodes). Public signatures are unchanged.
- **Core:** command/subscription batching (`Cmd.batch2`, `Sub.batch2`, the `System` pipeline), layered-grid edits and 3D layout line drawing no longer allocate tuples per call. **Breaking:** `LayeredGrid2D`/`LayeredHexGrid`/`LayeredGrid3D`/`LayeredHexGrid3D` `getOrAddLayer` now returns a struct tuple — callers must destructure with `let struct (a, b) = getOrAddLayer ...`; the previous `let a, b = ...` form no longer compiles.
- **Core:** dictionary lookups and dictionary enumeration no longer allocate — the Elmish loop's subscription bookkeeping and per-frame `bool * 'T`/`KeyValue` tuple allocations from `TryGetValue` and `for KeyValue` loops are gone (internal zero-allocation helpers in `Mibo.Elmish`).
- **Raylib 3D, MonoGame 3D:** fewer per-frame/per-draw allocations — render-target pools, material/part/transform caches and palette-texture bookkeeping no longer allocate tuples per lookup; instanced renderer grouping is allocation-free per cell; per-block light merging is a single exact array copy.
- **MonoGame 2D:** fewer per-frame allocations — batch-state checks and mouse input handling no longer allocate tuples per draw or per frame.
- **MonoGame 3D:** fewer per-frame allocations — per-draw effect validation and per-frame post-process checks no longer allocate tuples.

## [4.0.0-rc-003] - 2026-08-02

### Fixed

- **MonoGame 3D (DX12):** skinned + instanced draws now animate every instance with its own pose on the DX12 backend. The `groupBoneCount` uniform did not survive the DX12 mgfx reflection parser even in the isolated grouped effects, so the shaders read a zero bone stride and every instance rendered with the first instance's bone palette — all instances shared one pose, and shadows could play a different clip than the body. The bone stride is now pre-multiplied into the per-instance palette offset at staging time and the grouped shaders index the palette directly, with no uniform involved.

## [4.0.0-rc-002] - 2026-08-02

### Fixed

- **MonoGame 3D, Raylib 3D:** instanced draws (`instanced`, `animatedModelInstanced`) now copy the caller's `transforms` array when the draw is recorded. A frame's draws execute only after the whole view is recorded, so reusing one array across camera blocks previously rendered the earlier block with the later block's transforms. Keeping one persistent array per group and refilling it between blocks or frames is now safe, with zero steady-state allocation beyond one pooled copy per instanced command.

## [4.0.0-rc-001] - 2026-08-02

### Added

- **Core, MonoGame 3D, Raylib 3D:** bone pose queries and attachment draws for animated models. The new `BoneRef` type (`ByName` / `ByIndex`) addresses a bone; `Animation3DState.computePose` and `AnimatedModel.computePose` evaluate the pose once per frame into a `BonePose` (per-bone world transforms plus the skinning palette) that bone queries (`AnimatedModel.tryGetBoneWorld`, `BonePose.worldAt`/`tryGetWorld`) and the new `buffer.attachedMesh(animModel, bone, localTransform, mesh, material, transform, ?pose)` member share with the skinned draw. Evaluation is allocation-free after warmup. Attachments compose as `localTransform * boneWorld * transform` and inherit the instance's full world transform including scale; missing bones are never an error — queries return `ValueNone` and attachment draws emit nothing. `AnimatedMesh` now retains a bone name→index lookup (plus bone names/parents on raylib) for `ByName` resolution. See `docs/animation3d.md`.
- **Core, MonoGame 3D, Raylib 3D:** `buffer.animatedModel`/`animatedModelWith`/`animatedModelWithPerMesh` take an optional `pose` parameter so one pose evaluation per frame serves the draw plus any bone queries and attachment draws; omitting it keeps the previous behavior. **Breaking (binary):** adding an optional parameter changes the compiled (IL) signature of these members — existing source compiles unchanged, but assemblies compiled against a previous Mibo version must be recompiled.
- **Raylib 3D:** the new `AnimatedModel` record (shared mesh + per-entity state) is an opt-in GPU skinning path for `animatedModel`: it draws through the GPU-skinned path with a per-instance palette instead of mutating the model via `UpdateModelAnimation`, so the same model can be drawn with several different poses in one frame, and the `pose` parameter is honored. Passing a bare `Animation3DState` keeps the legacy mutating path unchanged (and ignores `pose`).
- **Raylib 3D:** `Animation3DClips.merge` combines animation clips loaded from several files into one clip set, remapping each clip's keyframe poses by bone name when a source file's skeleton orders bones differently than the model being animated (e.g. KayKit's MovementBasic vs General rigs, where left/right sides are swapped — without the remap those clips play mirrored). Companion helpers `Animation3DClips.boneNamesOf` and `Animation3DClips.buildBoneRemap` expose the pieces. MonoGame needs no equivalent — its clip channels are keyed by bone name. See `docs/animation3d.md` → "Loading clips from multiple files".
- **Raylib 3D:** the GPU skinning path (`AnimatedModel`) now supports `animatedModelWithPerMesh` (per-mesh material resolver), matching the legacy mutating path and the MonoGame backend.
- **MonoGame 3D, Raylib 3D:** skinned + instanced draws. `buffer.animatedModelInstanced(animModel, transforms, poses, ?material, ?colors)` draws N instances of the same animated model in one draw call (per sub-mesh), each instance with its own world transform and bone pose. `poses` carries one caller-evaluated `BonePose` per instance — compute poses once per frame and share them with bone queries and attachment draws. A pose palette must cover the model's bones: a shorter one raises an `ArgumentException` instead of silently corrupting the following instances' poses, and extra entries beyond the bone count are ignored. `material` is an optional `MaterialOverride` (`All` / `PerMesh`); `colors` (MonoGame only) tints each instance. Per-instance bone palettes are uploaded once per frame as a texture the vertex shader samples (raylib indexes it by `gl_InstanceID`), shared between the shadow and forward passes. The per-instance world rows are likewise staged once per frame and shared between the passes (DX11/Vulkan); on DX12 the forward/depth group budgets differ, so world-row staging stays per pass there. On DX12 — where the native runtime has no vertex texture fetch — palettes ride a per-group constant array instead, and draws are chunked into groups of `448 / boneCount` instances in the forward pass and `500 / boneCount` in the shadow pass (elsewhere, chunks of 2048); models with more than 448 bones fall back to per-instance skinned draws there (correct, but no batching win). The group budgets ride the effect's `$Globals` Int16 size cap (32767 bytes): 448×64 + 3156 bytes of lights/shadows/material uniforms in the forward effect, 500×64 + 132 bytes in the depth effect — the depth effect affords larger groups because it carries no lighting uniforms. **OpenGL note:** the MonoGame OpenGL backend has no vertex texture fetch either, so there the draw falls back to per-instance skinned draws (correct, but no batching win — this includes Android). Custom effects in a `beginEffect` scope can opt in via a `SkinnedInstanced` technique (MonoGame) or the `bonePalette` sampler plus bone attributes (raylib) — see `docs/shader-uniforms.md`. See `docs/animation3d.md` → "Skinned + instanced draws".
- **MonoGame 3D:** skinned + instanced draws automatically merge mesh parts that share a parent bone, vertex layout, and material into one draw per chunk — the merged geometry is built lazily on a model's first instanced draw and cached per model. A command whose materials split a group (e.g. a `PerMesh` override) falls back to per-part draws for that command, so output never changes. The static per-part data (technique references, skinned/grouped flags, the merged-group maps) is likewise resolved once per model per pipeline and cached — only the game-mutable data (bone worlds, materials, normal matrices) resolves per command, and swapping a part's `Effect` invalidates the cache so the draw follows the new effect.

### Changed

- **MonoGame 3D, Raylib 3D — Breaking (binary):** `Animation3DChannel`, `Animation3DClip`, `Animation3DClips`, and `AnimatedMesh` (MonoGame), and `AnimatedMesh` (raylib) are now `[<Struct>]` records — they previously were reference records. Besides the binary break, equality and copy semantics change to value semantics; code relying on reference identity needs review. **Breaking (source):** raylib's `AnimatedMesh` gains a `BindPose: Transform[]` field (the model-space rest pose, used as the fallback for bones a clip doesn't animate); code constructing the record literally must add the field — `AnimatedMesh.fromModel` populates it for you.
- **Raylib 3D — Breaking (behavioral):** `AnimatedMesh.computeBoneMatrices` now returns the bone palette in plain System.Numerics row-major layout (`result[i] = InverseBindPose[i] * pose[i]`) instead of pre-transposed into raylib's native layout. The framework transposes at upload time where the shader contract needs it, so palettes produced by `computeBoneMatrices` or a computed `BonePose` render unchanged. Code that feeds its own palettes to `skinnedMesh`/`DrawSkinnedMesh` must pass them in the same row-major layout — drop any manual pre-transpose, otherwise skinned meshes render distorted.
- **MonoGame 3D, Raylib 3D:** animation name lookups and pose sampling are faster. `Channels` and `ClipNames` are now frozen dictionaries, and on MonoGame a pose evaluation resolves each bone's clip channel once per (clip, mesh) pair instead of a string dictionary lookup per bone — the resolution cache reads lock-free, so parallel pose evaluation across many instances does not contend on a shared monitor. The string-keyed API is unchanged.
- **MonoGame 3D:** fewer per-frame allocations on the 3D hot paths, no API change. Instanced draws (`instanced`/`animatedModelInstanced`) no longer allocate a vertex-binding array per draw or reference tuples for effect/params selection (struct tuples now), the shadow pass builds its per-span render predicates once per pass instead of per caster region, and the bone-palette texture pool keeps up with per-frame chunk demand instead of disposing and recreating textures whenever a frame needs more than two chunks of a size.
- **MonoGame 3D:** single-camera frames walk the render buffer twice per frame instead of three times: shadow/scene-depth geometry is now collected inline in the pipeline's pre-scan, gated on the frame possibly needing it — a depth-needing post-process, or at least one shadow-casting light in the buffer (tracked by the new `RenderBuffer3D.ShadowCasterLightCount` counter, so frames with provably no caster skip collection entirely). Multi-camera-block frames are unchanged (per-block collection).
- **Raylib 3D:** the shadow-uniform upload now covers only the shader variants the frame actually draws through (usage tracked during the pre-scan) instead of all four unconditionally, and chunked transform slices for skinned-instanced draws are cut once per frame into pooled arrays (shared by the shadow and forward passes) instead of re-copied per chunk per pass. The same inline-collection walk and `ShadowCasterLightCount` gate as the MonoGame backend apply.
- **MonoGame 3D, Raylib 3D:** the forward PBR shader now samples the emission map only when the material binds one (new `useEmissionMap` uniform, mirroring `useNormalMap`) — materials without an emission map, the common case, skip a per-fragment texture read. Custom effects that declare `useEmissionMap` receive the flag from the pipeline as well.
- **MonoGame 3D, Raylib 3D:** fragments facing away from the directional light no longer sample its shadow map — their directional contribution is zero regardless, so roughly half the lit surface in a sun-lit scene skips the shadow texture reads.

### Fixed

- **Raylib 3D:** bones a clip doesn't animate (a merged clip animating only part of the skeleton, or a clip with fewer bones than the mesh) now hold their bind pose instead of collapsing to the skeleton origin with a zeroed transform — and blends no longer slerp against a zero quaternion — matching the MonoGame backend's long-standing behavior. `AnimatedMesh.computeBoneMatrices` also no longer reads past the end of the clip's native keyframe pose array when the clip has fewer bones than the mesh.
- **Raylib 3D:** GPU skinning now works with the stock raylib native library. raylib uploads the bone index/weight vertex buffers only when natively compiled with `SUPPORT_GPU_SKINNING` (off by default, including the raylib-cs NuGet builds), so skinned meshes previously rendered frozen in bind pose; `Animation3DState.create`/`AnimatedMesh.fromModel` now detect the missing buffers and upload them from managed code.
- **MonoGame 3D:** pooled 3D render targets (the post-process scene RT and the ping-pong intermediates) are now created with `RenderTargetUsage.PreserveContents`. MonoGame clears a `DiscardContents` target on every bind, so when a mid-frame pass (the scene-depth pre-pass, or a per-camera-block shadow pass) restored the caller's bindings and re-bound the scene RT, the rendered scene was wiped before post-process actions sampled it — `postProcessWithDepth` effects (fog, DOF, SSAO) received a black scene.

### Deprecated

- **Raylib, MonoGame:** the piped draw modules (`Draw`, `Draw3D`, `LightDraw`, `ParticleDraw`) now carry an `[<Obsolete>]` attribute, so code that still uses them builds with a deprecation warning (FS0044) pointing at the fluent draw DSL. The modules were announced as deprecated in 3.1.0 and remain functional; they will be removed in a future release. See `docs/draw-dsl.md` → "Migrating from the piped DSL".

## [4.0.0-beta-001] - 2026-07-28

### Added

- **3D:** `RenderBuffer3D.CameraBlockCount` counts `BeginCamera`/`BeginCameraConfig` commands added since the last `Clear`, on both backends. The pipelines use it to skip the per-camera-block plan walk (and its per-frame allocations) for single-camera frames.

### Changed

- **MonoGame 3D:** **Breaking (behavioral):** in frames with more than one camera block, lights (ambient, directional, point, spot), the shadow origin, and shadow casting are now scoped per camera block. A block that sets its own lights starts from the frame defaults (lights emitted before the first camera block or between blocks) and applies them in order; a block that sets none inherits the running set. Each camera block renders its own shadow map, so multi-block frames cost one shadow pass per block. Single-camera frames are unchanged.
- **Raylib 3D:** **Breaking (behavioral):** the same per-camera-block scoping: lights no longer accumulate across blocks that set their own lights, and the shadow pass runs per camera block instead of once per frame. Single-camera frames are unchanged.
- **3D:** **Breaking (behavioral):** only the first directional light is shaded, and only it can cast shadows, on both backends. Previously a frame whose first directional light didn't cast could still be shadowed by a later casting light's shadow map, and a casting directional light could render a shadow map nothing sampled.
- **Raylib 3D:** the per-shadow-pass point/spot shadow-slot arrays are now grow-only pipeline scratch (matching the MonoGame backend) instead of fresh arrays per pass, and the pre-scan no longer gathers lights frame-globally in multi-camera-block frames (the per-block forward pass builds them).
- **MonoGame 3D:** the shadow/depth passes no longer allocate a `RenderTargetBinding[]` per pass to save the caller's render-target bindings; the bindings are saved into pooled scratch resized only when the bound count changes.

### Deprecated

- **3D:** `LightBuffers.defaults` is obsolete on both backends: it is a single shared mutable accumulator, so every consumer aliases the same light buffers. Use `LightBuffers.create` for per-instance state instead.

### Fixed

- **3D:** in multi-camera-block frames, live shading no longer diverges from the block plan: between-block lights were applied twice to blocks that set their own lights, blocks that set no lights were shaded by between-block and after-last-block lights the plan (and their shadow pass) didn't include, and after-last-block lights leaked into blocks that reset. The live light sets and frame defaults are now built in-order during the forward pass instead of seeded from the plan's frame defaults.
- **MonoGame 3D:** a camera block's `ClearColor` combined with a custom `Viewport` no longer clears the whole frame. The block clear is drawn as a viewport-covering triangle instead of `gd.Clear`, which ignores the viewport (D3D `ClearRenderTargetView` semantics) and wiped every previously rendered camera block (split-screen).
- **MonoGame:** the backbuffer is now created with `RenderTargetUsage.PreserveContents`. On the DX12-native backend, rebinding the backbuffer after a mid-frame render-target switch (the shadow atlas, the post-process scene RT) discarded everything drawn before the switch — in multi-camera-block frames, earlier camera blocks and the frame clear were wiped. Games can still override via a device-config callback.
- **MonoGame 3D:** two live 3D pipelines no longer bleed lights into each other; each pipeline now accumulates lights in its own buffers.
- **Raylib 3D:** a throw during a per-camera-block shadow pass no longer leaves the pipeline outside the scene render target's texture mode; the caller's texture mode is re-wrapped in a `finally`.

## [3.3.0] - 2026-07-25

### Added

- **MonoGame 3D, Raylib 3D:** billboards gain rotation, atlas sub-rects, and blend control. `buffer.billboard(...)` takes optional `rotation` (degrees around the view axis), `sourceRect` (pixel-space atlas/flipbook sub-rect; an all-zero rect means the full texture), and `blend` (MonoGame: `BlendMode.AlphaBlend | NonPremultiplied | Additive | Opaque`, default `AlphaBlend`; raylib: `Raylib_cs.BlendMode`, default `Alpha`). `buffer.billboardBatch(...)` takes matching optional `rotations`/`sourceRects`/`blend` — a null or too-short array falls back to defaults for the remaining items. Blended billboards draw in buffer order with no depth sorting; non-opaque modes test depth but don't write it, `Opaque` writes depth. Note that a MonoGame batch draws every item with the first texture (use an atlas plus `sourceRects`); raylib honors per-item textures.
- **MonoGame 3D:** `buffer.instanced(...)` gains an optional `colors` array for per-instance tinting — albedo is multiplied by `color.rgb` and final alpha by `color.a`; instances beyond `colors.Length` render white. Custom effects that opt into instancing may declare `float4 InstanceColor : TEXCOORD5` to receive the per-instance color; effects that don't declare it still work. MonoGame only — passing `colors` on raylib raises `NotSupportedException`.

### Fixed

- **MonoGame 3D:** custom effects in a `beginEffect` scope that declare fewer light, shadow-caster, or bone array slots than the pipeline maximums (8 point / 4 spot lights, 16 shadow casters, 128 bones) no longer crash with an `IndexOutOfRangeException` during scene upload — array uniforms are clamped to the effect's declared element count, and the `pointLightCount`/`spotLightCount` uniforms are clamped to the declared slots too, so a shader's light loop never indexes past its own declaration. Note that on the OpenGL backend, uniform arrays indexed only with compile-time constants can still crash inside MonoGame's GL constant-buffer upload; index them dynamically (see `docs/shader-uniforms.md` → "Convention notes").

## [3.2.0] - 2026-07-25

### Added

- **3D:** **Breaking:** `ShadowAtlasConfig` gains a `DirectionalAtlasRatio` field (`ShadowAtlasConfig.defaults` sets it to `0.5`; code constructing the record literally must add the field). It gives the single directional shadow light a dedicated region of the shadow atlas instead of sharing one tile of the caster grid, so directional shadows stay high-resolution without tuning `MaxCasters` to your light count. Point/spot casters subdivide the remaining atlas area. Set it to `1.0` for directional-only scenes or `0.0` to restore the previous uniform-grid layout. Available on both backends. **Breaking (behavioral):** the `0.5` default re-lays-out existing directional shadows; use `0.0` for the previous layout.
- **3D:** instanced grid rendering can shade each cell type, sub-mesh, or the whole grid with a custom effect instead of the default PBR shader. Provide the effect per sub-mesh, per cell key, or once for the whole grid; cell types without an effect keep the default look.
- **MonoGame 2D:** consecutive lit sprites sharing the same texture and normal map collapse into a single draw call instead of one per sprite. Visuals and the `.litSprite(...)` API are unchanged.

### Changed

- **Breaking:** **Core:** `GameConfig.TargetFPS` is now `int voption` and defaults to `ValueNone` — when unset, the framework imposes no render-rate cap and leaves the backend's default framerate behavior untouched. Previously the default was `60`, which forced a fixed timestep on every game. To set a cap, use `GameConfig.withTargetFPS 60` (or `TargetFPS = ValueSome 60` inline); the old `TargetFPS = 0` "unlimited" sentinel is now simply omitting the field.
- **MonoGame 3D:** **Breaking (behavioral):** `DirectionalLightSize` is now the full height of the directional shadow ortho window in world units (was the half-size), matching the raylib backend. The same value now covers half the world area with twice the shadow texel density; double your configured value to keep the previous coverage.
- **3D:** directional shadow PCF taps are clamped to the caster's atlas tile on both backends, so the 3×3 kernel can no longer bleed into a neighboring caster's region at tile borders. All backends run the same point-sampled 3×3 PCF kernel.
- **Raylib 3D:** the directional shadow camera's far plane is tightened to the light distance plus the full ortho size plus a one-unit margin (was light distance plus twice the ortho size), spending less depth precision on empty space behind the scene. MonoGame's far plane keeps its previous coverage — it was already light distance plus the full ortho height.

### Fixed

- **Raylib 3D:** scenes made only of instanced draws now render shadows — the shadow pass previously ran only when at least one non-instanced mesh was drawn.

## [3.1.1] - 2026-07-22

### Fixed

- **MonoGame 3D:** models and primitives no longer render with the wrong texture when drawn after instanced draws. Instanced draws always uploaded and bound their material but left the material short-circuit cache stale, so a subsequent non-instanced draw whose material key matched the cached value skipped texture rebinding and sampled whatever the instanced pass had bound.

## [3.1.0] - 2026-07-19

### Added

- **Core:** a unified fluent draw DSL for 2D and 3D, identical on both backends. View code chains calls on the render buffer (`buffer.beginCamera(cam).fillCircle(...).endCamera()`) with optional parameters for anything that has a sensible default (`layer`, `tint`, `thickness`, ...). Colors use the backend-neutral `Mibo.Color`, vectors and matrices use `System.Numerics` (MonoGame converts at the boundary), and backend values — textures, fonts, cameras, shaders, models, materials, sprite/text/animation state records — pass through unchanged. Backend-only features (MonoGame's sampler-state control, raylib's explicit-palette skinned draw) are available only on the backend that supports them; calling them on the other is a compile error. The whole chain is erased at compile time and emits the same buffer commands as hand-written code — no dispatch, no allocation. See `docs/draw-dsl.md`.

### Deprecated

- **Raylib, MonoGame:** the function-based (piped) draw modules — `Draw`, `Draw3D`, `LightDraw`, `ParticleDraw` — are deprecated and will be removed in a future release. They remain functional in this release; new code should use the fluent draw DSL (see `docs/draw-dsl.md`).

## [3.0.0] - 2026-07-18

### Added

- **_Notice_**: Vulkan on windows might not render as expected. It is recommended to use DirectX either 11 or 12 rather than Vulkan there. Other platforms seem to be working as expected.

- **MonoGame:** pre-compiled shader variants for DirectX 12 (`.dx12.mgfx`) and Vulkan (`.vk.mgfx`) now ship alongside the existing DirectX 11 and OpenGL variants. `ShaderLoader` routes to the correct variant based on `PlatformInfo.GraphicsBackend`, so games running on the new native backends load matching shaders automatically. All five effects (`LitSprite`, `LitSpriteNormalMap`, `Instanced`, `ForwardPbr`, `DepthShadow`) compile for all four profiles.

### Changed

- **MonoGame — Breaking:** `Mibo.MonoGame` now builds against MonoGame 3.8.5 (`MonoGame.Framework.Native` 3.8.5, up from 3.8.4.1). Consumers must update their MonoGame host/runtime packages to 3.8.5 to match; mixing 3.8.4.1 host packages with this version fails to load the backend types at runtime.
- **Templates:** the MonoGame templates (`mibo-mg-2d`/`mibo-mg-3d`) move to MonoGame 3.8.5 and now ship three thin clients: `DesktopGL` (OpenGL, unchanged), `DesktopVK` (Vulkan, new), and `WindowsDX12` (DirectX 12, replacing the DirectX 11 `WindowsDX` client). The mgcb dotnet tools pinned in the templates move to 3.8.5 to match. Raylib templates are unchanged.

### Fixed

- **MonoGame 3D:** instanced draws no longer render garbage or flicker on the DirectX 12 backend. The per-instance world-matrix buffer is now a dynamic vertex buffer, so each instanced draw keeps its own matrices; previously, staging several instance groups per frame could make every draw read the last group's data, showing terrain chunks and repeated models in the wrong place or not at all depending on the camera angle.

## [2.2.0] - 2026-07-16

### Added

- **Core:** `LayeredGrid3D` — a cubic layered grid (`create`/`getOrAddLayer` plus `LayeredLayout3D.layer`) for stacking independent `CellGrid3D` layers by integer index, mirroring `LayeredGrid2D` (2D) and `LayeredHexGrid3D` (3D hex).

### Changed

- **Raylib 3D:** instanced shadow casters (`Draw3D.drawMeshInstanced`) now render into the shadow atlas as one instanced draw per mesh instead of being unrolled into one draw per instance. Scenes with many instanced shadow casters (e.g. block-grid terrain) no longer spend most of their frame budget on thousands of individual shadow draws per frame.

## [2.1.0] - 2026-07-10

### Changed

- **Cameras 3D — Breaking:** the 3D camera API is simplified and unified across backends. `Camera3D.create position target fov` replaces `lookAt`/`orthographic` (defaults: up = `Vector3.Up`, near = `0.1f`, far = `1000f`); `orbit` drops near/far/aspect params; `screenPointToRay` (MonoGame) now takes `Camera3D`. New `withUp`/`asOrthographic` (both backends) and `withNearFar` (MonoGame) modifiers override the defaults. The bare `Camera` type (`{ View; Projection }`) is removed — it was returned by the old constructors but never consumed by the renderer. Aspect ratio is computed from the active viewport at render time.

## [2.0.1] - 2026-07-08

### Fixed

- **Raylib:** games no longer abort on exit with `malloc: pointer being freed was not allocated`. Two native-lifetime fixes: model animations loaded via `IAssets.ModelAnimations` were freed through a pinned managed array (they're now released through raylib's native pointer), and the 3D forward pipeline double-freed its shaders on shutdown — raylib 6.0's `UnloadMaterial` now destroys the material's shader and map textures (not just its maps), so the pipeline frees only the maps it allocated and unloads each shader once.

## [2.0.0] - 2026-07-08

### Added

- **Cameras (parity):** the MonoGame and raylib `Camera2D`/`Camera3D` modules now offer the same set of operations. MonoGame `Camera2D` gains `viewportBounds`, `screenToWorld`/`worldToScreen`, `smoothFollow`/`clampTarget`, and the full `render`/`withViewport`/`withClear`/`splitScreen*` config-builder surface it lacked; raylib `Camera3D` gains `lookAt`/`orthographic`/`orbit`/`screenPointToRay` (wrapping `Raylib.GetScreenToWorldRay`). Closes the camera API-surface gap between the backends.
- **Docs:** new "MonoGame type quirks" reference collects the raylib-vs-MonoGame type differences that first-time MonoGame users hit — `System.Numerics` vs `Microsoft.Xna.Framework` math (Core layout/spatial/light APIs take `System.Numerics` on both backends, so a bare `Vector2` resolves to the wrong type on MonoGame), float vs int `Rectangle`, `Color` constructors, the `IAssets` namespace and asset-path conventions, and the live window size via `ctx.WindowWidth`/`ctx.WindowHeight`. The affected guide pages now cross-link to it.
- **Templates:** the starters now steer AI assistants (and readers) to the API reference for exact signatures and to the guides only for general usage; the MonoGame starters additionally require reading the type-quirks reference before writing code.

### Changed

- **MonoGame — Breaking:** the camera modules are consolidated into a single `Camera2D`/`Camera3D` surface that mirrors the raylib layout. The standalone `Camera2DConfig` module is removed — its builders (`render`/`withViewport`/`splitScreen*`) now live in the `Camera2D` module, and `withClearColor` is renamed `withClear`. `Camera2D.smoothFollow`/`clampTarget` now return a new camera instead of mutating in place (the camera's fields are immutable).
- **Raylib — Breaking:** the 2D camera readers (`viewportBounds`/`screenToWorld`/`worldToScreen`) and `Camera3D.screenPointToRay` now take the camera by read-only reference (`inref`), so call sites must pass `&camera` (the `smoothFollow`/`clampTarget` mutators already used `byref`). This skips copying the native `Camera2D`/`Camera3D` structs on per-frame reads. All raylib camera helpers are now `inline`.
- **Culling — Breaking:** `Culling.isGenericVisible` is renamed `isVisibleBox` (it tests a bounding box against the frustum — the new name says what it does).

### Removed

- **3D — Breaking:** removed `ShadowAtlasConfig.ShowDebugOverlay` and the raylib `ShadowAtlas.RenderDebugOverlay` overlay — a dev-time diagnostic that leaked into the preview builds. No config flag overlays the shadow atlas on screen anymore.
- **Cameras — Breaking:** removed `Camera3DConfig.PostProcessPasses` and the `Camera3D.withPostProcess`/`withoutPostProcess` builders — the pipelines never read them (v2 post-processing is command-driven via `Draw3D.postProcess`), so they were no-ops. Also removed `Camera2D.overlay`/`Camera3D.overlay` — they only set a viewport and a black clear (no compositing); the equivalent is `render |> withViewport |> withClear`, and on-top layering is draw order.

### Fixed

- **Docs:** the Culling guide now covers both backends (raylib's `Frustum` vs MonoGame's native `BoundingFrustum`, and the raylib `Camera2D.viewportBounds &camera` form), instead of describing only the raylib types.

## [2.0.0-rc-003] - 2026-07-07

### Added

- **2D:** post-process effects can now read the scene's lighting data and camera transform. The post-process context exposes the active `LightContext2D` (point lights, directional lights, ambient, occluders) and the last `Camera2D` — so a post-process shader can bloom lit areas, apply light-tinted color grading, or anchor effects in world space.

- **3D:** point-light shadows are no longer fixed to look straight down. Set `PointLight3D.ShadowDirection` (or use the `withShadowDirection` builder) to aim the single-face shadow map toward your geometry — e.g. `Vector3.UnitX` for a wall sconce. Defaults to down (−Y) when unset.

### Fixed

- **Raylib 3D:** specular highlights and Fresnel effects rendered incorrectly on non-skinned meshes when no shadow-casting light was present.
- **MonoGame 3D:** per-frame rendering overhead reduced — lighting data is now uploaded once per frame instead of once per draw call.
- **Raylib 3D:** normal-mapped meshes now light correctly when consecutive draws share a material but differ in world transform (the normal matrix was previously cached per material instead of per draw).
- **Raylib 3D:** normal-mapped instanced meshes now light correctly when individual instances are rotated or non-uniformly scaled.

### Changed

- **Raylib 3D:** the maximum distance at which point/spot lights cast shadows is now configurable via `ShadowAtlasConfig.MaxShadowLightDistance` (default 50 world units). Increase it for large-world games (RTS, open-world); decrease for tighter scenes.

## [2.0.0-rc-002] - 2026-07-07

### Added

- **3D:** model-aware post-processing — `Draw3D.postProcessWithDepth` exposes camera-POV scene depth to post-process passes (fog, depth-of-field, SSAO). The depth texture stores non-linear NDC z (`[0,1]`, 0=near, 1=far); linearize with the camera's near/far. raylib renders the scene into a custom framebuffer with a sampleable depth-texture attachment (no extra geometry pass); MonoGame re-renders opaque geometry into a dedicated R32F target. Use plain `Draw3D.postProcess` for color-only effects so the depth-production cost is skipped. See `docs/graphics3d/overview.md` → "Post-processing" and `docs/shaders.md` → "Post-process shaders" for the depth-texture contract and shader binding requirements.

- **3D:** instanced draws inside a `beginEffect`/`endEffect` scope are now shaded by your own shader/effect when it opts into instancing — raylib declares `in mat4 instanceTransform;`, MonoGame exposes an `Instanced` technique. Effects that don't opt in keep the previous PBR-instanced fallback. Skinned + instanced isn't supported (no per-instance bone palette). See the "Instancing (opt-in)" section of `docs/shader-uniforms.md`.

- **MonoGame 2D:** `Draw.setSamplerState` sets the sprite-batch sampler state for subsequent sprites (e.g. `SamplerState.PointClamp`), mirroring `setBlend` — use it to stop tiles sampled from a gutterless spritesheet from bleeding at the edges. It flushes the batch on change and defaults to the previous behavior (`SamplerState.LinearClamp`). On raylib, use the new `Texture.filter` helper instead.
- **Raylib:** a `Texture` helper module lets you configure a loaded texture's sampler in a pipe — `filter`, `wrap`, and `mipmaps` (e.g. `assets.Texture "tiles.png" |> Texture.filter TextureFilter.Point`, or `|> Texture.wrap TextureWrap.Repeat`). These override the load-time trilinear+mipmaps default (needed for point-filtered tile atlases and repeating backgrounds). MonoGame controls sampling per draw via `Draw.setSamplerState` instead.

### Changed

- **Templates:** the MonoGame 2D and 3D templates now keep the shared library under `src/`, ship the MonoGame content pipeline (`Content/Content.mgcb`, built by each thin client via `MonoGame.Content.Builder.Task`), and expose `create()` as a ready-to-run `MonoGameProgram` with the content root already configured — the thin clients just construct `MiboGame` and run, instead of each wiring up `MonoGameProgram.ofProgram`.

### Fixed

- **3D:** a custom `beginEffect`/`endEffect` shader/effect that opts into shadows now receives them correctly. The scene-upload path bound the shadow atlas under the wrong sampler name on MonoGame (`texture5` instead of `shadowAtlas` — the name `mgfxc` exposes samplers under), never uploaded the per-caster `shadowBiases`, and omitted the bias from the `ShadowResult` bundle entirely. Declare `sampler2D shadowAtlas : register(s5)` (MonoGame) / the `shadowAtlas`/`shadowBiases` uniforms (raylib) to opt in.

- **Docs:** asset access in the program/assets/animation guides pointed at a non-existent `ctx.Assets` member — fixed to `GameContext.getService<IAssets> ctx`. The Core layout APIs (`CellGrid2D`/`LayeredGrid2D`) always take `System.Numerics.Vector2`, which collides with `Microsoft.Xna.Framework.Vector2` in MonoGame projects; the layout and camera guides now flag this and qualify the calls. The `SpriteState` reference now lists every field (`Rotation`/`Origin`/`NormalMap`), not just `Color`/`Layer`.

## [2.0.0-rc-001] - 2026-07-01

### Added

- **Core: backend-neutral `Color` type** — a byte RGBA struct (`Mibo.Color`) with `toVector3`/`toVector4` conversions and named constants (`White`, `Black`, `Red`, etc.). Shared light/camera definitions use this instead of a backend-specific `Color`. Each backend provides inlineable `op_Implicit` conversions to/from its native color type.
- **Core: shared 3D light definitions** — `AmbientLight3D`, `DirectionalLight3D`, `PointLight3D`, and `SpotLight3D` (with their builder modules) now live in `Mibo.Core` using `Mibo.Color` + `System.Numerics.Vector3`. Both backends previously carried byte-for-byte identical copies; now there is one implementation.
- **Core: shared `Animation3DState` playback clock** — the pure state machine (`create`, `play`, `blendTo`, `update`, etc.) and `Animation3DClipsInfo` (clip names + keyframe counts) now live in `Mibo.Core`. The clock operates on ints/floats only — no backend types. Each backend builds `Animation3DClipsInfo` at load time from its native clip data and delegates playback to the Core functions.
- **Core: mouse capture** — `IInput.SetMouseCapture(MouseCapture)` lets games request pointer-locked, unlimited-rotation mouse input via a backend-neutral contract. Raylib uses native `DisableCursor`/`EnableCursor`; MonoGame re-centers the mouse inside its own `Poll()` so no external `GameComponent` is needed.

### Changed

- **Core — Breaking:** `AmbientLight3D.Color`, `DirectionalLight3D.Color`, `PointLight3D.Color`, and `SpotLight3D.Color` are now `Mibo.Color` (were `Raylib_cs.Color` / `Microsoft.Xna.Framework.Color`). Use `Mibo.Color.White` etc. when constructing lights, or rely on the implicit conversion from your backend's native color. Light `Direction`/`Position` fields are now `System.Numerics.Vector3` (were native on the MonoGame backend).
- **Core — Breaking:** `Animation3DClips` gains a `ClipsInfo: Animation3DClipsInfo` field. The `Animation3DState` playback functions (`create`/`play`/`blendTo`/`update`/etc.) on both backends now delegate to the Core implementation. The public API is unchanged, but the struct field layout of the backend-specific `Animation3DState` types is internal — construct states via the module functions.
- **Core:** `Animation3DState.update` blend target wrapping now respects `Loop = false` consistently across both backends (previously raylib always wrapped the blend target regardless of the loop flag).

- **MonoGame: device-level config callback** — `MonoGameProgram` wraps a Core `Program` and carries `(Game * GraphicsDeviceManager -> unit)` callbacks (`ofProgram` + `withConfig`) that `MiboGame` runs in its constructor, after the Core `GameConfig` but before `Initialize` / `GraphicsDevice` creation. Use this for `GraphicsProfile`, vsync (`SynchronizeWithVerticalRetrace`), `IsFullScreen`, `Window.AllowUserResizing`, `Content.RootDirectory`, and other properties that need direct device-manager access. `MiboGame` now takes a `MonoGameProgram` instead of a raw `Program`. `MonoGameProgram.withInputMapper` now operates on the wrapper.
- **MonoGame: host & program** — `MiboGame(program)` is the MonoGame game host (subclasses `Microsoft.Xna.Framework.Game`, drives the shared `ElmishLoop`). `MonoGameProgram.withInputMapper` registers the MonoGame-backed input mapper (and calls `withInput`). `MonoGameGameContext` accessors (`getGraphicsDevice`/`getContentManager`/`getGame`) retrieve MonoGame handles from the Core `GameContext` service registry. MonoGame `IAssets` exposes the typed loaders (`Texture`/`Font`/`Sound`/`Model`/`Effect`/`ModelAnimations`/`AnimatedMesh`) and extends the portable `IAssetCache`.
- **Core: `IAssetCache`** — backend-neutral asset cache interface (`Get`/`Create`/`GetOrCreate`/`Clear`/`Dispose`) that portable code depends on; the backend `IAssets` extends it.
- **Docs: migration guide** — `docs/migration-from-monogame.md`, a before/after guide for moving from the original monolithic `Mibo` package to `Mibo.Core` + `Mibo.MonoGame` (program setup, GameContext, input, assets, the renamed 2D/3D rendering stacks, animation, cameras, the content pipeline, and a Raylib-backend appendix).
- **Docs: shader uniform reference** — `docs/shader-uniforms.md` lists the exact uniform names the 3D `beginEffect`/`endEffect` scope uploads (matrices, lights, shadows, material, bones, `time`), the `drawMeshEffect` and `drawImmediate` contracts, and the 2D lit-sprite layout, so a custom shader can declare just what it consumes. Worked HLSL + GLSL examples included.
- **MonoGame 3D: per-group custom shading** — `Draw3D.beginEffect`/`endEffect` shade the draws between them with a user-supplied `Effect` instead of PBR. The effect inherits the scene's camera, lights, shadows, material, bones, and a `time` clock **by declaring the matching uniform names**; uniforms it doesn't declare are skipped. Scopes don't persist across cameras. Lets you render toon/water/vignette alongside the default PBR scene.
- **MonoGame 3D: extensible pipeline** — `ForwardPipelineBase` (abstract; owns the gather + frame orchestration + a virtual `Shade`) with `ForwardPipeline` as the thin PBR subclass. Override `Shade` to plug a different shading strategy; it receives the per-frame scene (lights, bones, shadow output, `time`). Register the same way: `Renderer3D.create (ForwardPipeline()) view`.
- **MonoGame 3D: `drawImmediate` receives a `SceneContext`** — the raw `GraphicsDevice` plus the gathered scene (camera, lights, shadows, `time`). For fully-custom draws (water/refraction, screen-space, multi-pass) that want device control without re-gathering the scene.
- **Raylib 3D: per-group custom shading** — `Draw3D.beginEffect`/`endEffect` shade the draws between them with a user-supplied `Shader` instead of PBR. The shader inherits the scene's camera, lights, shadows, material, bones, and a `time` clock **by declaring the matching uniform names**; uniforms it doesn't declare are skipped. Scopes don't persist across cameras. Lets you render toon/water/vignette alongside the default PBR scene.- **Raylib 3D: extensible pipeline** — `ForwardPipelineBase` (abstract; owns the gather + frame orchestration + a virtual `Shade`) with `ForwardPbrPipeline` as the thin PBR subclass. Override `Shade` to plug a different shading strategy; it receives the per-frame scene (lights, shadow output, `time`). Register the same way: `Renderer3D.create (ForwardPbrPipeline()) view`.
- **Raylib 3D: `drawImmediate` receives a `SceneContext`** — the gathered scene (camera, view/projection matrices, lights, shadows, `time`). For fully-custom draws (water/refraction, screen-space, multi-pass) that want the scene data without re-gathering it. Mirrors the MonoGame `SceneContext` minus the device field (raylib uses global device state).
- **Raylib 3D: `time` uniform** in the scene-data contract. Shaders opt into animation (ripples, flowing textures) by declaring `time`. `IRenderPipeline3D.Execute` gains a `GameTime` argument.
- **MonoGame 3D: `time` uniform** in the scene-data contract. Shaders opt into animation (ripples, flowing textures) by declaring `time`. `IRenderPipeline3D.Execute` gains a `GameTime` argument (MonoGame backend only).
- **MonoGame 3D: PBR shading** — models, animated models, primitives, and instanced geometry route through a Cook-Torrance PBR effect (ambient + 1 directional + up to 8 point + up to 4 spot lights, emission, opacity, tiling, optional normal maps). Imported models keep their authored look; a `MaterialKey` short-circuit skips re-uploading unchanged materials. Per-draw `normalMatrix`; instanced normals transform by the per-instance world matrix; the instanced shader negates the directional light direction.
- **MonoGame 3D: shadows** — directional, point, and spot lights that set `CastsShadows` render depth into an `R32F` atlas (sampled with 3×3 PCF; OpenGL uses `RasterizerState` polygon-offset + a `shadowTexelSize` uniform since SM3.0 has no `dFdx`/`textureSize`). Per-light frustum culling skips casters outside each light's view (accounting for transform scale). Static models, primitives, instanced geometry, and animated models all cast; animated models render depth-only with matching bone semantics (not frustum-culled — a bare mesh part has no reachable bounds). A per-light shadow index replaces the per-fragment caster scan. Configure via `ShadowAtlasConfig`/`ShadowBiasConfig`; `EnableShadows`/`DisableShadows`/`SetShadowOrigin` are honored. Only the first shadow-casting directional light is registered (the shader samples slot 0).
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
- **Raylib / MonoGame 3D:** `Draw3D.modelWith` and `modelWithPerMesh` draw a model with your own `Material3D` — the whole model, or per sub-mesh — instead of the material baked into the file. One call covers any override shape, so you don't reach for a different API per property. MonoGame also gains `animatedModelWith` / `animatedModelWithPerMesh` for skinned models.
- **Docs:** multi-backend documentation. The site now covers the Core, Raylib, and MonoGame packages: the rendering, shaders, assets, input, and lighting pages show both backends side-by-side where their APIs diverge (the pipeline types, GLSL vs HLSL effects, loose-file vs content-pipeline assets, and viewport coordinate conventions). The original raylib-only docs are preserved as a frozen archive for the prior release.
- **Raylib 3D — Breaking:** `IRenderPipeline3D.Execute` gains a `GameTime` argument (surfaced to shaders as the `time` uniform and passed to `drawImmediate` callbacks). Custom raylib pipelines must add the parameter. Matches the MonoGame backend.
- **Raylib 3D — Breaking (behavioral):** `Draw3D.drawImmediate` callback changed from `unit -> unit` to `SceneContext -> unit`. The callback now receives the frame's gathered scene (camera, view/projection, lights, shadows, `time`) instead of no data.
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
- **Raylib 3D:** textures and model materials now load with mipmaps and trilinear filtering. Loaded surfaces previously rendered with point filtering, so 3D models looked flat and matte compared to other backends.
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
