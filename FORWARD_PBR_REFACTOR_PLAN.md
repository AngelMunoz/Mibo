# ForwardPbrPipeline Refactor Plan

## Goal

Refactor `ForwardPbrPipeline.fs` (2167 LOC) from a monolithic class with 3× duplicated shader
variant logic into a cohesive, performant module using the closure-over-object-expression
pattern. The refactored pipeline lives in a **new file** alongside the original, implementing
the same `IRenderPipeline3D` interface for safe swap-in/swap-out testing.

## Motivation

- **70% duplication**: Three nearly-identical shader variants (forward/instanced/skinned)
  each have their own location cache, light upload, material cache, and material uniform setter
- **God-class**: `PipelineContext` is ~1300 LOC with ~30 mutable fields and ~20 internal members
- **Vestigial design**: `PipelineContext` exists because Commands were once interfaces; now
  they're DUs dispatched directly — the context is an unnecessary middleman
- **Bug-prone**: Every new uniform requires touching 3 places; copy-paste rot already exists
  (double-assignment in skinned cache, redundant shadow location re-fetch)

## Current State

```
src/Mibo.Raylib/Graphics3D/Pipelines/
├── ForwardPbrPipeline.fs    (2167 LOC — the monolith)
├── PostProcess3D.fs         (unchanged)
├── ShadowAtlas.fs           (unchanged)
├── Shaders.fs               (unchanged)
```

### Pain Points

| Concern | Forward | Instanced | Skinned | Lines |
|---------|---------|-----------|---------|-------|
| Location cache | `cacheLocations` (100 LOC) | `cacheInstancedLocations` (120 LOC) | `cacheSkinnedLocations` (105 LOC) | ~325 |
| Light upload | `uploadLights` (54 LOC) | `uploadLightsInstanced` (54 LOC) | `uploadLightsSkinned` (54 LOC) | ~162 |
| Material cache | `getOrCreateMaterial` (47 LOC) | `getOrCreateInstancedMaterial` (47 LOC) | `getOrCreateSkinnedMaterial` (47 LOC) | ~141 |
| Material uniforms | `setMaterialUniforms` (29 LOC) | `setMaterialUniformsInstanced` (29 LOC) | `setMaterialUniformsSkinned` (24 LOC) | ~82 |

### Existing Bugs

- `getOrCreateSkinnedMaterial` lines 856-858: duplicate `lastSkinnedMaterialKey <- key` (copy-paste rot)
- `UnloadInstancedMaterialCache` (line 1334): misleading name — also unloads skinned cache
- `CacheInstancedShadowLocations` (line 1345): re-runs `cacheInstancedLocations()` every frame despite being "cached"
- `GetShaderLocation(depthShadowSkinnedShader, "boneMatrices[0]")` (line 1837): uncached per-skinned-mesh per-shadow-caster per-frame string lookup
- `MaterialKey.fromMaterial3D` computed 3× per draw call (WarmMaterial, setMaterialUniforms, getOrCreate)

## Target Architecture

### File Structure

```
src/Mibo.Raylib/Graphics3D/Pipelines/
├── ForwardPbrPipeline.fs          (original — untouched)
├── ForwardPbrPipelineV2.fs        (new — refactored implementation)
├── PostProcess3D.fs
├── ShadowAtlas.fs
├── Shaders.fs
```

Both `ForwardPbrPipeline` and `ForwardPbrPipelineV2` implement `IRenderPipeline3D`.
Swap by changing one line:

```fsharp
// Original
Renderer3D.create (ForwardPbrPipeline()) view
// V2
Renderer3D.create (ForwardPbrPipelineV2()) view
```

### Module Shape

```fsharp
module ForwardPbrPipelineV2 =

  // ── Types (structs) ────────────────────────────────────────
  // MaterialUniforms, AmbientUniforms, DirLightUniforms,
  // PointLightUniforms, SpotLightUniforms, ShadowUniforms,
  // ShaderLocations, MaterialKey, MaterialCache, ShaderVariant,
  // LightBuffers, FrameState

  // ── Native helpers (setShaderInt, setShaderFloat, etc.) ────
  // Same pattern as existing NativeHelpers, module-private

  // ── Pure / near-pure functions ─────────────────────────────
  // cacheLocations, uploadLights, setMaterialUniforms,
  // getOrCreate, uploadShadowUniforms, runShadowPass,
  // uploadBoneMatrices, dispatch

  // ── Factory ────────────────────────────────────────────────
  let create (config) : IRenderPipeline3D = ...
```

No classes. No PipelineContext. All mutable state lives in the `create` closure.
Functions are module-private. The object expression is a thin orchestrator.
The object expression also implements `IDisposable` for resource cleanup.

### Type Decomposition

#### Struct Syntax Convention

**Mutable structs** use class syntax with `val mutable` fields and a constructor.
Mutable record structs require `let mutable` bindings for `byref`, which is awkward.
Class-style structs work naturally with `byref`:

```fsharp
// ✗ Avoid — mutable record struct, requires `let mutable` binding for byref
[<Struct>]
type Foo = { mutable Bar: int }

// ✓ Prefer — class-style struct, works with any binding
[<Struct>]
type Foo =
  val mutable Bar: int
  new(bar) = { Bar = bar }
```

**Immutable leaf structs** use record syntax with `[<IsReadOnly; Struct>]`:

```fsharp
[<IsReadOnly; Struct>]
type MaterialUniforms = {
  AlbedoColor: int
  Roughness: int
  // ... all immutable after creation
}
```

**Optional parameters** use `[<Struct>]` to get `ValueOption` instead of `Option`:

```fsharp
// ✓ Avoids Option heap allocation
type Config([<Struct>] ?maxLights: int) =
  let maxLights = defaultArg maxLights 8
```

#### Leaf Uniform Structs (immutable — `[<IsReadOnly; Struct>]`)

```fsharp
[<IsReadOnly; Struct>]
type MaterialUniforms = {
  AlbedoColor: int
  Roughness: int
  Metallic: int
  EmissionColor: int
  Opacity: int
  Tiling: int
  UseNormalMap: int
  NormalMatrix: int
}

[<IsReadOnly; Struct>]
type AmbientUniforms = {
  Color: int
  Intensity: int
}

[<IsReadOnly; Struct>]
type DirLightUniforms = {
  Dir: int
  Color: int
  Intensity: int
  CastsShadows: int
}

[<IsReadOnly; Struct>]
type PointLightUniforms = {
  Count: int
  Pos: int[]
  Color: int[]
  Intensity: int[]
  Radius: int[]
  Falloff: int[]
}

[<IsReadOnly; Struct>]
type SpotLightUniforms = {
  Count: int
  Pos: int[]
  Dir: int[]
  Color: int[]
  Intensity: int[]
  Radius: int[]
  InnerCutoff: int[]
  OuterCutoff: int[]
}

[<IsReadOnly; Struct>]
type ShadowUniforms = {
  Pass: int
  Atlas: int
  CasterCount: int
  ViewProjs: int[]
  UVOffsets: int[]
  LightPositions: int[]
  Biases: int[]
  Types: int[]
}
```

Note: `int[]` fields are reference types — `[<IsReadOnly>]` prevents reassigning the
array *field* but does not prevent mutating array *contents*. This is correct: the
locations are assigned once during caching and read thereafter.

#### Composite Location Struct (immutable after creation)

```fsharp
[<IsReadOnly; Struct>]
type ShaderLocations = {
  Shader: Shader
  Cached: bool
  Material: MaterialUniforms
  Ambient: AmbientUniforms
  DirLight: DirLightUniforms
  PointLights: PointLightUniforms
  SpotLights: SpotLightUniforms
  Shadow: ShadowUniforms
  CameraPos: int
  Bones: int  // -1 for non-skinned variants
}
```

#### Material Cache (mutable — class-style struct)

```fsharp
[<Struct>]
type MaterialCache =
  val private cache: Dictionary<MaterialKey, Material>
  val mutable LastKey: MaterialKey
  val mutable HasLast: bool
  val mutable LastMaterial: Material

  new(capacity: int) = {
    cache = Dictionary<MaterialKey, Material>(capacity)
    LastKey = Unchecked.defaultof<MaterialKey>
    HasLast = false
    LastMaterial = Unchecked.defaultof<Material>
  }

  member this.Cache = this.cache
```

#### Shader Variant (mutable — class-style struct, collapses 3× duplication)

```fsharp
[<Struct>]
type ShaderVariant =
  val Locs: ShaderLocations
  val mutable MaterialCache: MaterialCache
  val mutable LightsDirty: bool

  new(locs: ShaderLocations, matCache: MaterialCache) = {
    Locs = locs
    MaterialCache = matCache
    LightsDirty = true
  }
```

Three instances: `forward`, `instanced`, `skinned`. All functions operate on
`ShaderVariant` instead of having three copies.

#### Light Buffers (reference type — not a struct)

```fsharp
type LightBuffers = {
  Ambient: ResizeArray<AmbientLight3D>
  DirLights: ResizeArray<DirectionalLight3D>
  PointLights: ResizeArray<PointLight3D>
  SpotLights: ResizeArray<SpotLight3D>
}
```

Not a struct — contains reference-type fields that are already pointer-sized.

#### Frame State (uses `voption` instead of separate bool)

```fsharp
[<IsReadOnly; Struct>]
type FrameState = {
  Camera: Camera3D voption
  ShadowOrigin: Vector3 voption
}
```

`Camera3D voption` replaces the separate `Camera: Camera3D` + `CameraFound: bool` pattern.
If there's a camera it's `ValueSome cam`, otherwise `ValueNone`. No ambiguity.

`Camera3D` is ~44 bytes. `Camera3D voption` stores the value inline (no heap allocation)
with a 1-byte flag — same ballpark as separate fields, cleaner semantics.

**Important**: `FrameState.Camera` captures the **primary camera for shadow calculations**
(first camera found in the pre-pass). The pipeline supports **multiple cameras per frame**
via `BeginCamera`/`EndCamera` command pairs in the dispatch loop — each pair opens a
separate viewport with its own camera. The dispatch loop tracks this with its own
`cameraActive: bool` and `currentCamera: Camera3D` mutable state, separate from `FrameState`.

### Function Signatures

Functions use `inref` for read-only large structs, `byref` for mutation:

```fsharp
// Read-only access to variant — inref prevents accidental mutation
let private uploadLights
  (shader: Shader, variant: inref<ShaderVariant>, lights: LightBuffers,
   maxPt: int, maxSp: int)
  =

// Read-only access to material uniforms
let private setMaterialUniforms
  (shader: Shader, matLocs: inref<MaterialUniforms>, mat3d: inref<Material3D>,
   nm: Matrix4x4)
  =

// Mutates cache — byref allows writes
let private getOrCreate
  (variant: byref<ShaderVariant>, shader: Shader, mat3d: inref<Material3D>)
  : Material
  =

// Read-only access to shadow uniforms
let private uploadShadowUniforms
  (shader: Shader, shadowLocs: inref<ShadowUniforms>, cameraLoc: int,
   atlas: ShadowAtlas, cameraPos: Vector3)
  =

// ReadOnlySpan for bone matrices — no allocation, slice-friendly
let private uploadBoneMatrices
  (shader: Shader, boneLoc: int, bones: ReadOnlySpan<Matrix4x4>)
  =
```

### Dispatch

The dispatch match lives directly in the `Execute` body — no closure, no indirection:

```fsharp
{ new IRenderPipeline3D with
    member _.Execute gameCtx buffer rtPool =
      // ── Pre-scan: first camera (for shadows), lights, shadow origin ──
      let mutable frameState = { Camera = ValueNone; ShadowOrigin = ValueNone }
      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.BeginCamera cam ->
            match frameState.Camera with
            | ValueNone -> frameState <- { frameState with Camera = ValueSome cam }
            | ValueSome _ -> ()
        | Command3D.BeginCameraConfig cfg ->
            match frameState.Camera with
            | ValueNone -> frameState <- { frameState with Camera = ValueSome cfg.Camera }
            | ValueSome _ -> ()
        | Command3D.SetShadowOrigin origin ->
            frameState <- { frameState with ShadowOrigin = ValueSome origin }
        | Command3D.AddDirectionalLight l -> lights.DirLights.Add l
        | Command3D.AddPointLight l -> lights.PointLights.Add l
        | Command3D.AddSpotLight l -> lights.SpotLights.Add l
        | Command3D.DrawMesh(_, _, mat) -> warmMaterial &forward &instanced &skinned mat
        | ...

      // ── Shadow pass (uses FrameState.Camera for shadow camera positioning) ──
      runShadowPass(shadowAtlas, depthShaders, meshDraws, &frameState)

      // ── Forward pass — dispatch inline, no closure ──
      // Tracks per-dispatch camera state (multiple cameras per frame)
      let mutable cameraActive = false
      let mutable currentCamera = Unchecked.defaultof<Camera3D>

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.BeginCamera cam ->
            if cameraActive then
              ensureShaderInactive &shaderActive
              Raylib.EndMode3D()
            Raylib.BeginMode3D cam
            cameraActive <- true
            currentCamera <- cam
        | Command3D.BeginCameraConfig cfg ->
            if cameraActive then
              ensureShaderInactive &shaderActive
              Raylib.EndMode3D()
            // Apply viewport, clear, BeginMode3D
            cameraActive <- true
            currentCamera <- cfg.Camera
        | Command3D.EndCamera ->
            if cameraActive then
              ensureShaderInactive &shaderActive
              Raylib.EndMode3D()
              cameraActive <- false
            Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)
        | Command3D.DrawMesh(mesh, transform, material) ->
            if cameraActive then
              ensureShaderActive &shaderActive forward.Locs.Shader
              if forward.LightsDirty then
                uploadLights(forward.Locs.Shader, &forward, lights, maxPt, maxSp)
              let key = MaterialKey.fromMaterial3D &material
              let nm = computeNormalMatrix transform
              setMaterialUniforms(forward.Locs.Shader, &forward.Locs.Material, &material, nm, &key)
              let mat = getOrCreate(&forward, forward.Locs.Shader, &material, &key)
              Raylib.DrawMesh(mesh, mat, transform)
        | Command3D.DrawSkinnedMesh(mesh, transform, material, bones) ->
            if cameraActive then
              ensureShaderInactive &shaderActive
              Raylib.BeginShaderMode skinned.Locs.Shader
              shaderActive <- true
              if skinned.LightsDirty then
                uploadLights(skinned.Locs.Shader, &skinned, lights, maxPt, maxSp)
              // ... bone matrices, material, draw
              ensureShaderInactive &shaderActive
        | ...

      // ── Post-process ──
      applyPostProcess gameCtx sceneRT rtPool

    member _.Initialize() = ...
    member _.Shutdown() = ...

  interface IDisposable with
    member _.Dispose() = ... // cleanup resources
}
```

## High-Perf Techniques

### `inref` for read-only large structs

Zero uses in current codebase. Apply to:

| Struct | Size (est.) | Call sites | Benefit |
|--------|-------------|------------|---------|
| `Material3D` | ~100 bytes | 16 sites (drawMesh, setMaterialUniforms, getOrCreate, etc.) | No-copy, compiler-enforced read-only |
| `ShaderVariant` | ~550 bytes | uploadLights, uploadShadowUniforms | No-copy for read-only access |
| `MaterialUniforms` | 32 bytes | setMaterialUniforms | Consistency, no-copy |
| `PointLight3D` | ~48 bytes | uploadPointLights inner loop | No-copy per-light |
| `SpotLight3D` | ~64 bytes | uploadSpotLights inner loop | No-copy per-light |

### `byref` for mutation

| Struct | Call sites | Why |
|--------|------------|-----|
| `ShaderVariant` | getOrCreate (mutates MaterialCache) | Cache writes through pointer |
| `FrameState` | pre-scan loop (writes Camera, ShadowOrigin) | State accumulation |

### `Span<T>` / `ReadOnlySpan<T>`

| Where | Usage |
|-------|-------|
| `uploadBoneMatrices` | `ReadOnlySpan<Matrix4x4>` for bone array slice |
| `uploadPointLights` inner loop | `ReadOnlySpan<PointLight3D>` via `CollectionsMarshal.AsSpan` on ResizeArray |
| `uploadSpotLights` inner loop | `ReadOnlySpan<SpotLight3D>` via `CollectionsMarshal.AsSpan` on ResizeArray |
| Shadow pass mesh draws | Already uses `ArrayPool<MeshDraw>.Shared` — no change needed |

Note: `ResizeArray` doesn't directly expose `ReadOnlySpan`. Use
`CollectionsMarshal.AsSpan` (already used in `Layout3D/Renderer3D.fs:55`)
to get a `Span` from the backing array, then slice.

### `[<IsReadOnly; Struct>]` for immutable value types

Apply to all leaf uniform structs and `ShaderLocations`. Prevents accidental mutation
and enables the compiler to optimize (no defensive copies needed when reading).

### `let inline` (existing pattern)

Apply to all small helper functions (setShaderInt, colorToVec3, computeNormalMatrix, etc.).
Consistent with codebase convention (321 existing inline functions).

### `[<Struct>]` on optional parameters

Apply to constructor optional parameters to avoid `Option<T>` heap allocation:

```fsharp
// ✓ Uses ValueOption — no heap allocation
type ForwardPbrPipelineV2(
  [<Struct>] ?postProcess: PostProcessConfig3D,
  [<Struct>] ?maxPointLights: int,
  [<Struct>] ?maxSpotLights: int,
  [<Struct>] ?shadowAtlasConfig: ShadowAtlasConfig,
  [<Struct>] ?shadowBiasConfig: ShadowBiasConfig
) =
```

Consistent with `RenderBuffer3D` which already uses `[<Struct>] ?capacity: int`.

### Not using

| Technique | Why not |
|-----------|---------|
| SRTP | Zero uses in codebase; uniform helpers already zero-cost via `let inline` |
| `MethodImpl(AggressiveInlining)` | Skipped per user preference |
| `SkipLocalsInit` | Skipped per user preference |
| `StructLayout(LayoutKind.Explicit)` | No union-like types needed here |

## Phased Execution Plan

### Phase 1: Types and Pure Helpers

**Goal**: Define all types and extract pure/near-pure functions. No behavior change yet.

**New file**: `ForwardPbrPipelineV2.fs`

1. Define leaf uniform structs (immutable, `[<IsReadOnly; Struct>]`):
   `MaterialUniforms`, `AmbientUniforms`, `DirLightUniforms`,
   `PointLightUniforms`, `SpotLightUniforms`, `ShadowUniforms`

2. Define composite structs:
   - `ShaderLocations` — `[<IsReadOnly; Struct>]`, holds leaf structs + `int[]` arrays
   - `MaterialKey` — copy from existing (already `[<Struct>]`)
   - `MaterialCache` — class-style struct with `val mutable`, constructor
   - `ShaderVariant` — class-style struct with `val mutable`, constructor
   - `LightBuffers` — record (not struct, reference types)
   - `FrameState` — `[<IsReadOnly; Struct>]`, uses `Camera3D voption`

3. Copy `NativeHelpers` module (setShaderInt, setShaderFloat, etc.)

4. Copy `NormalMatrixHelpers` (computeNormalMatrix)

5. Implement pure functions:
   - `cacheLocations(shader, maxPt, maxSp, maxCasters): ShaderLocations` — single
     parameterized version replacing 3× cache functions
   - `uploadLights(shader, variant: inref<ShaderVariant>, lights: LightBuffers, maxPt, maxSp)`
   - `setMaterialUniforms(shader, matLocs: inref<MaterialUniforms>, mat3d: inref<Material3D>, nm, key: inref<MaterialKey>)`
   - `getOrCreate(variant: byref<ShaderVariant>, shader, mat3d: inref<Material3D>, key: inref<MaterialKey>): Material`
   - `uploadShadowUniforms(shader, shadowLocs: inref<ShadowUniforms>, cameraLoc, atlas, cameraPos)`
   - `uploadBoneMatrices(shader, boneLoc, bones: ReadOnlySpan<Matrix4x4>)`
   - `colorToVec3`, `colorToVec4` (copy from existing)

6. **Build check**: `dotnet build` — types and functions compile but aren't wired up yet

### Phase 2: Object Expression and Execute

**Goal**: Implement `IRenderPipeline3D` via object expression, wire up all functions.

1. Implement `ForwardPbrPipelineV2` module with `create` function:
   - Constructor params use `[<Struct>] ?` for voption optional params
   - Capture all mutable state as `let` bindings inside `create`
   - Create three `ShaderVariant` instances (forward, instanced, skinned)
   - Create `LightBuffers`
   - Implement `Initialize`, `Shutdown`, `Execute`
   - Implement `IDisposable` on the object expression

2. `Execute` implementation:
   - Pre-scan loop → `FrameState` (uses `Camera3D voption`, no separate bool)
   - Shadow pass (copy from existing, refactor to use `uploadShadowUniforms`)
   - Forward pass dispatch loop (inline match, no closure)
   - Post-process (copy from existing `applyPostProcess`)

3. **Build check**: `dotnet build`

### Phase 3: Performance Refinements

**Goal**: Apply high-perf techniques, fix existing bugs.

1. Cache `MaterialKey.fromMaterial3D` — compute once per draw in dispatch,
   pass to both `setMaterialUniforms` and `getOrCreate` via `inref<MaterialKey>`
2. Cache `GetShaderLocation(depthShadowSkinnedShader, "boneMatrices[0]")` —
   move to `cacheLocations`, reuse in shadow pass
3. Apply `inref` to all read-only large struct parameters
4. Apply `byref` to mutation parameters
5. Apply `ReadOnlySpan<Matrix4x4>` to `uploadBoneMatrices`
6. Apply `CollectionsMarshal.AsSpan` for light array iteration where beneficial
7. **Build check**: `dotnet build`

### Phase 4: Format and Verify

1. Run `dotnet fantomas .` to format all F# files
2. Swap pipeline in template/sample:
   ```fsharp
   Renderer3D.create (ForwardPbrPipelineV2.create()) view
   ```
3. Visual comparison with original pipeline
4. Run existing tests: `dotnet test`
5. Keep original `ForwardPbrPipeline.fs` as regression reference

## Estimated Size

| Component | Current LOC | Estimated V2 LOC |
|-----------|-------------|------------------|
| Types (structs) | scattered | ~120 |
| NativeHelpers | 78 | 78 (copy) |
| cacheLocations | 325 (3×) | ~130 (1× parameterized) |
| uploadLights | 162 (3×) | ~65 (1× parameterized) |
| setMaterialUniforms | 82 (3×) | ~35 (1× parameterized) |
| getOrCreate | 141 (3×) | ~55 (1× parameterized) |
| uploadShadowUniforms | ~100 (inline) | ~50 (extracted) |
| uploadBoneMatrices | 7 | 7 |
| Shadow pass | ~200 | ~180 |
| Dispatch + Execute | ~450 | ~200 |
| Post-process | ~50 | 50 |
| PipelineContext | ~1300 | 0 (eliminated) |
| **Total** | **2167** | **~700** |

## Testing Strategy

### Unit-testable (module-private, expose via `internal` for tests)

- `MaterialKey.fromMaterial3D` — pure, already testable
- `colorToVec3` / `colorToVec4` — pure
- `computeNormalMatrix` — pure

### Integration-testable (need raylib context)

- Swap `ForwardPbrPipelineV2` into existing sample projects
- Visual comparison: same scene, same commands, same output
- A/B performance comparison

### Swap mechanism

```fsharp
// In any consumer — one line change
Renderer3D.create (ForwardPbrPipelineV2.create()) view
```

Both pipelines implement `IRenderPipeline3D`. Both work with the same
`Renderer3D`, `RenderBuffer3D`, and `RenderTargetPool3D`.
