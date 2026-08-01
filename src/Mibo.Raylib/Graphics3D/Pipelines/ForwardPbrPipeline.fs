#nowarn 9

namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Buffers
open System.Collections.Generic
open System.Numerics
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ------------------------------------------------------------------
// NativePtr helpers — void* with DisableRuntimeMarshalling requires
// explicit fixed + NativePtr.toVoidPtr.
// ------------------------------------------------------------------
[<AutoOpen>]
module internal NativeHelpers =

  let inline setShaderInt (shader: Shader) (loc: int) (value: int) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Int
    )

  let inline setShaderFloat (shader: Shader) (loc: int) (value: float32) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Float
    )

  let inline setShaderVec2 (shader: Shader) (loc: int) (v: Vector2) =
    use p = fixed &v

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec2
    )

  let inline setShaderVec3 (shader: Shader) (loc: int) (v: Vector3) =
    use p = fixed &v

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec3
    )

  let inline setShaderVec4 (shader: Shader) (loc: int) (v: Vector4) =
    use p = fixed &v

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec4
    )

  let inline setShaderIVec2 (shader: Shader) (loc: int) (x: int) (y: int) =
    let mutable v = struct (x, y)
    use p = fixed &v

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.IVec2
    )

  let inline rlSetUniformInt (loc: int) (value: int) =
    use p = fixed &value

    Rlgl.SetUniform(
      loc,
      NativePtr.toVoidPtr p,
      int ShaderUniformDataType.Int,
      1
    )

// ------------------------------------------------------------------
// Normal Matrix Helper
// ------------------------------------------------------------------

[<AutoOpen>]
module internal NormalMatrixHelpers =

  let inline computeNormalMatrix(model: Matrix4x4) =
    let mutable inv = Matrix4x4.Identity
    Matrix4x4.Invert(model, &inv) |> ignore
    Matrix4x4.Transpose inv

// ------------------------------------------------------------------
// Material Cache Key
// ------------------------------------------------------------------

[<Struct>]
type internal MaterialKey = {
  AlbedoMapId: uint
  RoughnessMapId: uint
  MetallicMapId: uint
  NormalMapId: uint
  EmissionMapId: uint
  AlbedoColor: Color
  Roughness: float32
  Metallic: float32
  EmissionColor: Color
  Opacity: float32
  TilingX: float32
  TilingY: float32
}

module internal MaterialKey =

  let inline fromMaterial3D(mat: inref<Material3D>) : MaterialKey = {
    AlbedoMapId =
      match mat.AlbedoMap with
      | ValueSome t -> t.Id
      | ValueNone -> 0u
    RoughnessMapId =
      match mat.RoughnessMap with
      | ValueSome t -> t.Id
      | ValueNone -> 0u
    MetallicMapId =
      match mat.MetallicMap with
      | ValueSome t -> t.Id
      | ValueNone -> 0u
    NormalMapId =
      match mat.NormalMap with
      | ValueSome t -> t.Id
      | ValueNone -> 0u
    EmissionMapId =
      match mat.EmissionMap with
      | ValueSome t -> t.Id
      | ValueNone -> 0u
    AlbedoColor = mat.AlbedoColor
    Roughness = mat.Roughness
    Metallic = mat.Metallic
    EmissionColor = mat.EmissionColor
    Opacity = mat.Opacity
    TilingX = mat.Tiling.X
    TilingY = mat.Tiling.Y
  }

// ------------------------------------------------------------------
// Leaf Uniform Structs (immutable — [<IsReadOnly; Struct>])
// ------------------------------------------------------------------

[<IsReadOnly; Struct>]
type internal MaterialUniforms = {
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
type internal AmbientUniforms = { Color: int; Intensity: int }

[<IsReadOnly; Struct>]
type internal DirLightUniforms = {
  Dir: int
  Color: int
  Intensity: int
  CastsShadows: int
}

[<IsReadOnly; Struct>]
type internal PointLightUniforms = {
  Count: int
  Pos: int[]
  Color: int[]
  Intensity: int[]
  Radius: int[]
  Falloff: int[]
  ShadowIdx: int[]
}

[<IsReadOnly; Struct>]
type internal SpotLightUniforms = {
  Count: int
  Pos: int[]
  Dir: int[]
  Color: int[]
  Intensity: int[]
  Radius: int[]
  InnerCutoff: int[]
  OuterCutoff: int[]
  ShadowIdx: int[]
}

[<IsReadOnly; Struct>]
type internal ShadowUniforms = {
  Pass: int
  Atlas: int
  CasterCount: int
  ViewProjs: int[]
  UVOffsets: int[]
  LightPositions: int[]
  Biases: int[]
  Types: int[]
}

// ------------------------------------------------------------------
// Composite Location Struct (immutable after creation)
// ------------------------------------------------------------------

[<IsReadOnly; Struct>]
type internal ShaderLocations = {
  Shader: Shader
  Cached: bool
  Material: MaterialUniforms
  Ambient: AmbientUniforms
  DirLight: DirLightUniforms
  PointLights: PointLightUniforms
  SpotLights: SpotLightUniforms
  Shadow: ShadowUniforms
  CameraPos: int
  ShadowNormalMatrix: int
  Bones: int // -1 for non-skinned variants
  BonePalette: int // -1 for non skinned-instanced variants
  BonePaletteSize: int // -1 for non skinned-instanced variants
}

// ------------------------------------------------------------------
// Material Cache (mutable — class-style struct)
// ------------------------------------------------------------------

[<Struct>]
type internal MaterialCache =
  val mutable cache: Dictionary<MaterialKey, Material>
  val mutable LastKey: MaterialKey
  val mutable HasLast: bool
  val mutable LastMaterial: Material

  new(capacity: int) =
    {
      cache = Dictionary<MaterialKey, Material>(capacity)
      LastKey = Unchecked.defaultof<MaterialKey>
      HasLast = false
      LastMaterial = Unchecked.defaultof<Material>
    }

// ------------------------------------------------------------------
// Shader Variant (mutable — class-style struct)
// ------------------------------------------------------------------

[<Struct>]
type internal ShaderVariant =
  val Locs: ShaderLocations
  val mutable MaterialCache: MaterialCache
  val mutable LightsDirty: bool
  val mutable LastMaterialKey: MaterialKey
  val mutable HasLastMaterial: bool

  new(locs: ShaderLocations, matCache: MaterialCache) =
    {
      Locs = locs
      MaterialCache = matCache
      LightsDirty = true
      LastMaterialKey = Unchecked.defaultof<MaterialKey>
      HasLastMaterial = false
    }

// ------------------------------------------------------------------
// Shadow Depth Resources (immutable — bundles shadow shader + material)
// ------------------------------------------------------------------

[<IsReadOnly; Struct>]
type internal ShadowDepthResources = {
  Shader: Shader
  SkinnedShader: Shader
  InstancedShader: Shader
  SkinnedInstancedShader: Shader
  Material: Material
  SkinnedMaterial: Material
  InstancedMaterial: Material
  SkinnedInstancedMaterial: Material
  NormalMatrixLoc: int
  SkinnedNormalMatrixLoc: int
  BoneLoc: int
  BonePaletteLoc: int
  BonePaletteSizeLoc: int
}

// ------------------------------------------------------------------
// Bone Palette Texture Pool (skinned + instanced draws)
// ------------------------------------------------------------------

[<AutoOpen>]
module internal PaletteTextureHelpers =

  /// Maximum palette-texture height — skinned-instanced draws chunk instances so
  /// each chunk's palette texture stays within this many rows (one per instance).
  let maxPaletteTextureRows = 2048

  /// Texture unit the bone-palette sampler is bound to during skinned-instanced
  /// draws. 14 is free: material maps occupy units 0-10 (MAX_MATERIAL_MAPS) and
  /// the shadow atlas owns 15.
  let paletteTextureSlot = 14

  /// Create an RGBA32F palette texture (point-filtered — texels are fetched
  /// exactly via texelFetch). Allocates zeroed once; contents are replaced by
  /// UpdateTexture on every use.
  let createPaletteTexture (width: int) (height: int) : Texture2D =
    let bytes = Array.zeroCreate<byte>(width * height * 16)
    use pb = fixed &bytes[0]

    let id =
      Rlgl.LoadTexture(
        NativePtr.toVoidPtr pb,
        width,
        height,
        PixelFormat.UncompressedR32G32B32A32,
        1
      )

    let tex =
      Texture2D(
        Id = id,
        Width = width,
        Height = height,
        Mipmaps = 1,
        Format = PixelFormat.UncompressedR32G32B32A32
      )

    Raylib.SetTextureFilter(tex, TextureFilter.Point)
    tex

/// <summary>
/// Pool of RGBA32F bone-palette textures for skinned-instanced draws, keyed by
/// (width, height). Textures are acquired per chunk and returned once per frame
/// (<see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PaletteTexturePool.ReleaseAll"/>):
/// a chunk's texture must stay alive until the rlgl batch flushes, so reusing a
/// single texture across chunks would let a later chunk's upload overwrite texels
/// an in-flight batched draw still reads.
/// </summary>
/// <remarks>
/// Also carries a per-frame upload cache: the shadow and forward passes render
/// the same command list, so each chunk's palette slice would otherwise upload
/// twice per frame. <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PaletteTexturePool.TryGetUploaded"/>
/// returns the texture a chunk already uploaded this frame (keyed by palettes
/// array reference + chunk offset — both passes share the same command arrays);
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PaletteTexturePool.RememberUploaded"/>
/// records it. Cleared by <c>ReleaseAll</c>.
/// </remarks>
type internal PaletteTexturePool() =

  let pool = Dictionary<struct (int * int), Queue<Texture2D>>()
  let inUse = ResizeArray<Texture2D>()
  let uploaded = Dictionary<struct (Matrix4x4[] * int), Texture2D>()
  let mutable transformScratch = Array.zeroCreate<Matrix4x4> 64

  member _.Acquire(width: int, height: int) : Texture2D =
    let key = struct (width, height)

    match pool.TryGetValue key with
    | true, queue when queue.Count > 0 ->
      let tex = queue.Dequeue()
      inUse.Add tex
      tex
    | _ ->
      let tex = createPaletteTexture width height
      inUse.Add tex
      tex

  /// Texture a chunk already uploaded this frame, if any.
  member _.TryGetUploaded
    (palettes: Matrix4x4[], chunkStart: int)
    : Texture2D voption =
    match uploaded.TryGetValue(struct (palettes, chunkStart)) with
    | true, tex -> ValueSome tex
    | false, _ -> ValueNone

  /// Record a chunk's freshly uploaded texture for this frame.
  member _.RememberUploaded
    (palettes: Matrix4x4[], chunkStart: int, tex: Texture2D)
    =
    uploaded[struct (palettes, chunkStart)] <- tex

  /// Growable scratch for slicing per-chunk transform runs without allocating.
  member _.GetTransformScratch(needed: int) : Matrix4x4[] =
    if transformScratch.Length < needed then
      transformScratch <- Array.zeroCreate<Matrix4x4> needed

    transformScratch

  member _.ReleaseAll() =
    for tex in inUse do
      let key = struct (tex.Width, tex.Height)

      match pool.TryGetValue key with
      | true, queue -> queue.Enqueue tex
      | false, _ ->
        let queue = Queue<Texture2D>()
        queue.Enqueue tex
        pool[key] <- queue

    inUse.Clear()
    uploaded.Clear()

  member _.UnloadAll() =
    for tex in inUse do
      Raylib.UnloadTexture tex

    inUse.Clear()

    for KeyValue(_, queue) in pool do
      for tex in queue do
        Raylib.UnloadTexture tex

      queue.Clear()

    pool.Clear()

// ------------------------------------------------------------------
// Frame State (uses voption)
// ------------------------------------------------------------------

[<IsReadOnly; Struct>]
type internal FrameState = {
  Camera: Camera3D voption
  ShadowOrigin: Vector3 voption
}

// ------------------------------------------------------------------
// Shadow Pass Helpers
// ------------------------------------------------------------------

[<AutoOpen>]
module internal ShadowPassHelpers =

  [<Struct>]
  type MeshDraw = {
    Mesh: Mesh
    Transform: Matrix4x4
    Bones: Matrix4x4[] voption
  }

  /// A collected instanced draw for the shadow pass. Unlike individual
  /// `MeshDraw` entries, this carries the full per-instance transform array
  /// and renders via `DrawMeshInstanced` — one GPU draw call per entry,
  /// not one per instance. This is critical for instanced-heavy scenes (e.g.
  /// block-grid terrain) where unrolling would produce thousands of
  /// individual shadow draws. `Palettes` carries the flat per-instance bone
  /// palettes for skinned-instanced draws (ValueNone for plain instanced
  /// draws); entries partition by it at render time, so skinned-instanced
  /// draws go through the depth skinned-instanced shader.
  [<Struct>]
  type InstancedMeshDraw = {
    Mesh: Mesh
    Transforms: Matrix4x4[]
    Palettes: Matrix4x4[] voption
    InstanceCount: int
    BoneCount: int
  }

  let collectMeshDraws
    (
      buffer: RenderBuffer3D,
      startIdx: int,
      endIdx: int,
      initialShadowsEnabled: bool
    ) =
    let pool = ArrayPool<MeshDraw>.Shared
    let instPool = ArrayPool<InstancedMeshDraw>.Shared

    let mutable meshCount = 0
    let mutable instancedCount = 0
    let mutable shadowsEnabled = initialShadowsEnabled
    let mutable i = startIdx

    while i < endIdx do
      match buffer[i] with
      | Command3D.DisableShadows -> shadowsEnabled <- false
      | Command3D.EnableShadows -> shadowsEnabled <- true
      | Command3D.DrawMesh _ when shadowsEnabled -> meshCount <- meshCount + 1
      | Command3D.DrawSkinnedMesh _ when shadowsEnabled ->
        meshCount <- meshCount + 1
      | Command3D.DrawModel(model, _) when shadowsEnabled ->
        meshCount <- meshCount + model.MeshCount
      | Command3D.DrawModelWith(model, _, _) when shadowsEnabled ->
        meshCount <- meshCount + model.MeshCount
      | Command3D.DrawMeshInstanced _ when shadowsEnabled ->
        instancedCount <- instancedCount + 1
      | Command3D.DrawSkinnedMeshInstanced _ when shadowsEnabled ->
        instancedCount <- instancedCount + 1
      | _ -> ()

      i <- i + 1

    let arr = pool.Rent(max meshCount 1)
    let instArr = instPool.Rent(max instancedCount 1)
    let mutable count = 0
    let mutable icount = 0
    let mutable skinnedStart = count
    shadowsEnabled <- initialShadowsEnabled
    i <- startIdx

    while i < endIdx do
      match buffer[i] with
      | Command3D.DisableShadows -> shadowsEnabled <- false
      | Command3D.EnableShadows -> shadowsEnabled <- true
      | Command3D.DrawMesh(mesh, transform, _) when shadowsEnabled ->
        arr[count] <- {
          Mesh = mesh
          Transform = transform
          Bones = ValueNone
        }

        count <- count + 1
      | Command3D.DrawSkinnedMesh(mesh, transform, _, bones) when shadowsEnabled ->
        arr[count] <- {
          Mesh = mesh
          Transform = transform
          Bones = ValueSome bones
        }

        count <- count + 1
      | Command3D.DrawModel(model, transform) when shadowsEnabled ->
        for mi = 0 to model.MeshCount - 1 do
          let mesh = NativePtr.get model.Meshes mi

          arr[count] <- {
            Mesh = mesh
            Transform = transform
            Bones = ValueNone
          }

          count <- count + 1
      | Command3D.DrawModelWith(model, transform, _) when shadowsEnabled ->
        for mi = 0 to model.MeshCount - 1 do
          let mesh = NativePtr.get model.Meshes mi

          arr[count] <- {
            Mesh = mesh
            Transform = transform
            Bones = ValueNone
          }

          count <- count + 1
      | Command3D.DrawMeshInstanced(mesh, transforms, _, instanceCount) when
        shadowsEnabled
        ->
        instArr[icount] <- {
          Mesh = mesh
          Transforms = transforms
          Palettes = ValueNone
          InstanceCount = instanceCount
          BoneCount = 0
        }

        icount <- icount + 1
      | Command3D.DrawSkinnedMeshInstanced(mesh,
                                           transforms,
                                           palettes,
                                           _,
                                           instanceCount,
                                           boneCount) when shadowsEnabled ->
        instArr[icount] <- {
          Mesh = mesh
          Transforms = transforms
          Palettes = ValueSome palettes
          InstanceCount = instanceCount
          BoneCount = boneCount
        }

        icount <- icount + 1
      | _ -> ()

      i <- i + 1

    // Partition: move skinned draws to end
    let mutable writeIdx = 0

    for j = 0 to count - 1 do
      match arr[j].Bones with
      | ValueNone ->
        if writeIdx <> j then
          arr[writeIdx] <- arr[j]

        writeIdx <- writeIdx + 1
      | ValueSome _ -> ()

    skinnedStart <- writeIdx

    for j = 0 to count - 1 do
      match arr[j].Bones with
      | ValueSome _ ->
        if writeIdx <> j then
          arr[writeIdx] <- arr[j]

        writeIdx <- writeIdx + 1
      | ValueNone -> ()

    struct (arr, count, skinnedStart, instArr, icount)

  /// <summary>
  /// Register shadow casters for every shadow-casting light into the caller-provided slot
  /// arrays (grow-only scratch owned by the pipeline, pre-filled with -1 by the caller):
  ///  - <c>pointShadowSlots</c>: indexed by <c>lights.PointLights</c> buffer position;
  ///    value is the caster's flat shader-array index, or -1 if the light doesn't cast / atlas full.
  ///  - <c>spotShadowSlots</c>: same shape for spot lights.
  /// Returns true when at least one caster was registered.
  ///
  /// The flat index equals the order in which casters are registered (dir first, then point,
  /// then spot), which matches <c>ShadowAtlas.PrepareUniforms</c>'s flattening order — so the
  /// value uploaded to <c>pointLightShadowIdx[i]</c> indexes <c>shadowViewProjs[idx]</c> correctly.
  /// </summary>
  let collectShadowCasters
    (
      lights: LightBuffers,
      atlas: ShadowAtlas,
      pointShadowSlots: int[],
      spotShadowSlots: int[]
    ) =
    let mutable hasCasters = false
    let mutable casterSlot = 0

    let tryAdd casterType pos dir target bias =
      match atlas.AddCaster(casterType, pos, dir, target, true, bias) with
      | ValueSome _ ->
        hasCasters <- true
        let slot = casterSlot
        casterSlot <- casterSlot + 1
        ValueSome slot
      | ValueNone -> ValueNone

    // Only the first directional light is shaded (and uploaded) by the forward shader,
    // so only it can cast — a non-casting DirLights[0] means no directional caster,
    // even if a later directional light has CastsShadows set.
    if lights.DirLights.Count > 0 && lights.DirLights[0].CastsShadows then
      let dir = lights.DirLights[0]

      tryAdd
        ShadowCasterType.Directional
        Vector3.Zero
        dir.Direction
        Vector3.Zero
        ValueNone
      |> ignore

    for i = 0 to lights.PointLights.Count - 1 do
      let pt = lights.PointLights[i]

      if pt.CastsShadows then
        let shadowDir =
          match pt.ShadowDirection with
          | ValueSome d -> d
          | ValueNone -> -Vector3.UnitY

        match
          tryAdd
            ShadowCasterType.Point
            pt.Position
            shadowDir
            Vector3.Zero
            pt.ShadowBias
        with
        | ValueSome slot -> pointShadowSlots[i] <- slot
        | ValueNone -> ()

    for i = 0 to lights.SpotLights.Count - 1 do
      let sp = lights.SpotLights[i]

      if sp.CastsShadows then
        match
          tryAdd
            ShadowCasterType.Spot
            sp.Position
            sp.Direction
            (sp.Position + sp.Direction)
            sp.ShadowBias
        with
        | ValueSome slot -> spotShadowSlots[i] <- slot
        | ValueNone -> ()

    hasCasters

  /// Build an orthographic camera for directional-light shadow rendering.
  let createDirectionalShadowCamera
    (
      caster: ShadowCasterData,
      frameState: inref<FrameState>,
      atlasCfg: ShadowAtlasConfig,
      activeCamera: Camera3D
    ) =
    let lightFromDir = Vector3.Normalize(-caster.LightDirection)

    let rawOrigin =
      match frameState.ShadowOrigin with
      | ValueSome origin -> origin
      | ValueNone ->
        match atlasCfg.OriginStrategy with
        | ShadowOriginStrategy.CameraTarget -> activeCamera.Target
        | ShadowOriginStrategy.SceneCenter -> Vector3.Zero
        | ShadowOriginStrategy.Custom f -> f activeCamera

    let gridSize = atlasCfg.GridSnapSize

    let snappedX =
      if gridSize > 0.0f then
        MathF.Round(rawOrigin.X / gridSize) * gridSize
      else
        rawOrigin.X

    let snappedZ =
      if gridSize > 0.0f then
        MathF.Round(rawOrigin.Z / gridSize) * gridSize
      else
        rawOrigin.Z

    let shadowOrigin = Vector3(snappedX, rawOrigin.Y, snappedZ)

    let lightDistance =
      match atlasCfg.DirectionalLightDistance with
      | ValueSome d -> d
      | ValueNone -> 100.0f

    let lightPos = shadowOrigin + lightFromDir * lightDistance

    let safeUp =
      if abs caster.LightDirection.Y > 0.99f then
        Vector3.UnitZ
      else
        Vector3.UnitY

    let orthoSize =
      match atlasCfg.DirectionalLightSize with
      | ValueSome s -> s
      | ValueNone -> 50.0f

    let shadowNear = 1.0f
    // The light sits at shadowOrigin + lightFromDir*lightDistance. Caster geometry lies
    // within orthoSize of the origin on the near side, so the farthest it can be from the
    // light is lightDistance + orthoSize. The previous (+orthoSize*2) doubled the z-range
    // and wasted depth precision on empty space behind the scene (more shadow acne). Keep
    // a one-unit margin so casters at the ortho boundary don't clip at the far plane.
    // Matches the MonoGame backend (ShadowPass.fs).
    let shadowFar = lightDistance + orthoSize + 1.0f

    Rlgl.SetClipPlanes(float shadowNear, float shadowFar)

    Camera3D(
      Position = lightPos,
      Target = shadowOrigin,
      Up = safeUp,
      FovY = orthoSize,
      Projection = CameraProjection.Orthographic
    )

// ------------------------------------------------------------------
// Per-camera-block light scoping
// ------------------------------------------------------------------

/// <summary>
/// Light-state transitions for multi-camera-block frames: applying one light command in-order,
/// loading a materialized light set into a live buffer set, and resetting the live buffers at a
/// camera block's start.
/// </summary>
module internal LightScoping =

  /// <summary>Applies one light command in-order: ambient overwrites; directional/point/spot append.</summary>
  let inline apply (lights: LightBuffers) (cmd: Command3D) =
    match cmd with
    | Command3D.SetAmbientLight a -> lights.Ambient <- ValueSome a
    | Command3D.AddDirectionalLight d -> lights.DirLights.Add d
    | Command3D.AddPointLight p -> lights.PointLights.Add p
    | Command3D.AddSpotLight s -> lights.SpotLights.Add s
    | _ -> ()

  /// <summary>Loads a materialized light set into a live buffer set, replacing its contents.</summary>
  let inline loadSet (set: BlockLightSet) (lights: LightBuffers) =
    lights.Ambient <- set.Ambient
    lights.DirLights.Clear()
    lights.DirLights.AddRange(set.DirLights)
    lights.PointLights.Clear()
    lights.PointLights.AddRange(set.PointLights)
    lights.SpotLights.Clear()
    lights.SpotLights.AddRange(set.SpotLights)

  /// <summary>
  /// Advances to the next camera block and, when that block carries its own light commands,
  /// resets the live buffers to the frame defaults — the block's commands are applied in-order
  /// as the forward loop reaches them. A block without light commands leaves the live buffers
  /// untouched (inheritance). Returns whether the buffers were reset.
  /// </summary>
  let inline resetForBlock
    (plan: BlockPlan)
    (defaults: LightBuffers)
    (lights: LightBuffers)
    (blockIndex: byref<int>)
    : bool =
    blockIndex <- blockIndex + 1

    if plan.Blocks[blockIndex].HasLightCommands then
      LightBuffers.copyInto defaults lights
      true
    else
      false

  /// <summary>
  /// Replays the buffer's camera and light commands the way the multi-camera-block forward
  /// pass does — both buffers start empty, between-block commands accumulate into the
  /// defaults, and each block resets (own commands) or inherits at its start — returning
  /// every block's live light set at its close. Test hook pinning that live shading matches
  /// the <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.BlockPlan"/>; allocates a snapshot per
  /// block, so it is not for the hot path.
  /// </summary>
  let replay
    (buffer: RenderBuffer3D)
    (plan: BlockPlan)
    (lights: LightBuffers)
    (defaults: LightBuffers)
    : BlockLightSet[] =
    let sets = ResizeArray<BlockLightSet>(plan.BlockCount)
    let mutable blockIndex = -1
    let mutable inBlock = false

    let closeBlock() =
      if inBlock then
        sets.Add {
          Ambient = lights.Ambient
          DirLights = lights.DirLights.ToArray()
          PointLights = lights.PointLights.ToArray()
          SpotLights = lights.SpotLights.ToArray()
        }

        inBlock <- false

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command3D.BeginCamera _
      | Command3D.BeginCameraConfig _ ->
        closeBlock()
        resetForBlock plan defaults lights &blockIndex |> ignore
        inBlock <- true
      | Command3D.EndCamera -> closeBlock()
      | Command3D.SetAmbientLight _
      | Command3D.AddDirectionalLight _
      | Command3D.AddPointLight _
      | Command3D.AddSpotLight _ as cmd ->
        apply lights cmd

        if not inBlock then
          apply defaults cmd
      | _ -> ()

    closeBlock()
    sets.ToArray()

// ------------------------------------------------------------------
// Pure / near-pure functions
// ------------------------------------------------------------------

[<AutoOpen>]
module internal PipelineFunctions =

  /// Create an empty LightBuffers instance.
  let createLightBuffers(maxPt: int, maxSp: int) : LightBuffers =
    LightBuffers.create 1 maxPt maxSp

  let inline colorToVec3(c: Mibo.Color) = Mibo.Color.toVector3 c

  let inline colorToVec4(c: Mibo.Color) = Mibo.Color.toVector4 c

  let inline nativeColorToVec4(c: Color) =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  /// Cache point light shader locations.
  let cachePointLightLocs(shader: Shader, maxPt: int) =
    let pos = Array.zeroCreate<int> maxPt
    let color = Array.zeroCreate<int> maxPt
    let intensity = Array.zeroCreate<int> maxPt
    let radius = Array.zeroCreate<int> maxPt
    let falloff = Array.zeroCreate<int> maxPt
    let shadowIdx = Array.zeroCreate<int> maxPt

    for i = 0 to maxPt - 1 do
      pos[i] <- Raylib.GetShaderLocation(shader, $"pointLightPos[{i}]")
      color[i] <- Raylib.GetShaderLocation(shader, $"pointLightColor[{i}]")

      intensity[i] <-
        Raylib.GetShaderLocation(shader, $"pointLightIntensity[{i}]")

      radius[i] <- Raylib.GetShaderLocation(shader, $"pointLightRadius[{i}]")
      falloff[i] <- Raylib.GetShaderLocation(shader, $"pointLightFalloff[{i}]")

      shadowIdx[i] <-
        Raylib.GetShaderLocation(shader, $"pointLightShadowIdx[{i}]")

    {
      Count = Raylib.GetShaderLocation(shader, "pointLightCount")
      Pos = pos
      Color = color
      Intensity = intensity
      Radius = radius
      Falloff = falloff
      ShadowIdx = shadowIdx
    }

  /// Cache spot light shader locations.
  let cacheSpotLightLocs(shader: Shader, maxSp: int) =
    let pos = Array.zeroCreate<int> maxSp
    let dir = Array.zeroCreate<int> maxSp
    let color = Array.zeroCreate<int> maxSp
    let intensity = Array.zeroCreate<int> maxSp
    let radius = Array.zeroCreate<int> maxSp
    let innerCutoff = Array.zeroCreate<int> maxSp
    let outerCutoff = Array.zeroCreate<int> maxSp
    let shadowIdx = Array.zeroCreate<int> maxSp

    for i = 0 to maxSp - 1 do
      pos[i] <- Raylib.GetShaderLocation(shader, $"spotLightPos[{i}]")
      dir[i] <- Raylib.GetShaderLocation(shader, $"spotLightDir[{i}]")
      color[i] <- Raylib.GetShaderLocation(shader, $"spotLightColor[{i}]")

      intensity[i] <-
        Raylib.GetShaderLocation(shader, $"spotLightIntensity[{i}]")

      radius[i] <- Raylib.GetShaderLocation(shader, $"spotLightRadius[{i}]")

      innerCutoff[i] <-
        Raylib.GetShaderLocation(shader, $"spotLightInnerCutoff[{i}]")

      outerCutoff[i] <-
        Raylib.GetShaderLocation(shader, $"spotLightOuterCutoff[{i}]")

      shadowIdx[i] <-
        Raylib.GetShaderLocation(shader, $"spotLightShadowIdx[{i}]")

    {
      Count = Raylib.GetShaderLocation(shader, "spotLightCount")
      Pos = pos
      Dir = dir
      Color = color
      Intensity = intensity
      Radius = radius
      InnerCutoff = innerCutoff
      OuterCutoff = outerCutoff
      ShadowIdx = shadowIdx
    }

  /// Cache shadow shader locations.
  let cacheShadowLocs(shader: Shader, maxCasters: int) =
    let viewProjs = Array.zeroCreate<int> maxCasters
    let uvOffsets = Array.zeroCreate<int> maxCasters
    let lightPositions = Array.zeroCreate<int> maxCasters
    let biases = Array.zeroCreate<int> maxCasters
    let types = Array.zeroCreate<int> maxCasters

    let locs = {
      Pass = Raylib.GetShaderLocation(shader, "shadowPass")
      Atlas = Raylib.GetShaderLocation(shader, "shadowAtlas")
      CasterCount = Raylib.GetShaderLocation(shader, "shadowCasterCount")
      ViewProjs = viewProjs
      UVOffsets = uvOffsets
      LightPositions = lightPositions
      Biases = biases
      Types = types
    }

    rlSetUniformInt locs.Atlas 15

    for i = 0 to maxCasters - 1 do
      viewProjs[i] <- Raylib.GetShaderLocation(shader, $"shadowViewProjs[{i}]")
      uvOffsets[i] <- Raylib.GetShaderLocation(shader, $"shadowUVOffsets[{i}]")

      lightPositions[i] <-
        Raylib.GetShaderLocation(shader, $"shadowLightPositions[{i}]")

      biases[i] <- Raylib.GetShaderLocation(shader, $"shadowBiases[{i}]")
      types[i] <- Raylib.GetShaderLocation(shader, $"shadowTypes[{i}]")

    locs

  /// Single parameterized location cache replacing 3x duplication.
  let cacheLocations
    (shader: Shader, maxPt: int, maxSp: int, maxCasters: int)
    : ShaderLocations =
    let matLocs = {
      AlbedoColor = Raylib.GetShaderLocation(shader, "albedoColor")
      Roughness = Raylib.GetShaderLocation(shader, "roughness")
      Metallic = Raylib.GetShaderLocation(shader, "metallic")
      EmissionColor = Raylib.GetShaderLocation(shader, "emissionColor")
      Opacity = Raylib.GetShaderLocation(shader, "opacity")
      Tiling = Raylib.GetShaderLocation(shader, "tiling")
      UseNormalMap = Raylib.GetShaderLocation(shader, "useNormalMap")
      NormalMatrix = Raylib.GetShaderLocation(shader, "normalMatrix")
    }

    let ambLocs = {
      Color = Raylib.GetShaderLocation(shader, "ambientColor")
      Intensity = Raylib.GetShaderLocation(shader, "ambientIntensity")
    }

    let dlLocs = {
      Dir = Raylib.GetShaderLocation(shader, "dirLightDir")
      Color = Raylib.GetShaderLocation(shader, "dirLightColor")
      Intensity = Raylib.GetShaderLocation(shader, "dirLightIntensity")
      CastsShadows = Raylib.GetShaderLocation(shader, "dirLightCastsShadows")
    }

    let ptLocs = cachePointLightLocs(shader, maxPt)
    let spLocs = cacheSpotLightLocs(shader, maxSp)
    let shadowLocs = cacheShadowLocs(shader, maxCasters)

    {
      Shader = shader
      Cached = true
      Material = matLocs
      Ambient = ambLocs
      DirLight = dlLocs
      PointLights = ptLocs
      SpotLights = spLocs
      Shadow = shadowLocs
      CameraPos = Raylib.GetShaderLocation(shader, "cameraPos")
      ShadowNormalMatrix = Raylib.GetShaderLocation(shader, "normalMatrix")
      Bones = Raylib.GetShaderLocation(shader, "boneMatrices[0]")
      BonePalette = Raylib.GetShaderLocation(shader, "bonePalette")
      BonePaletteSize = Raylib.GetShaderLocation(shader, "bonePaletteSize")
    }

  /// Single parameterized light upload replacing 3x duplication.
  let uploadLights
    (
      shader: Shader,
      variant: inref<ShaderVariant>,
      lights: LightBuffers,
      maxPt: int,
      maxSp: int,
      pointShadowSlots: int[],
      spotShadowSlots: int[]
    ) =
    let locs = variant.Locs

    match lights.Ambient with
    | ValueNone ->
      setShaderVec3 shader locs.Ambient.Color Vector3.Zero
      setShaderFloat shader locs.Ambient.Intensity 0.0f
    | ValueSome a ->
      setShaderVec3 shader locs.Ambient.Color (colorToVec3 a.Color)
      setShaderFloat shader locs.Ambient.Intensity a.Intensity

    match lights.DirLights.Count with
    | 0 ->
      setShaderVec3 shader locs.DirLight.Dir Vector3.Zero
      setShaderVec3 shader locs.DirLight.Color Vector3.Zero
      setShaderFloat shader locs.DirLight.Intensity 0.0f
      setShaderInt shader locs.DirLight.CastsShadows 0
    | _ ->
      let d = lights.DirLights[0]
      setShaderVec3 shader locs.DirLight.Dir d.Direction
      setShaderVec3 shader locs.DirLight.Color (colorToVec3 d.Color)
      setShaderFloat shader locs.DirLight.Intensity d.Intensity

      setShaderInt
        shader
        locs.DirLight.CastsShadows
        (if d.CastsShadows then 1 else 0)

    let ptCount = min lights.PointLights.Count maxPt
    setShaderInt shader locs.PointLights.Count ptCount

    for i = 0 to ptCount - 1 do
      let l = lights.PointLights[i]
      setShaderVec3 shader locs.PointLights.Pos[i] l.Position
      setShaderVec3 shader locs.PointLights.Color[i] (colorToVec3 l.Color)
      setShaderFloat shader locs.PointLights.Intensity[i] l.Intensity
      setShaderFloat shader locs.PointLights.Radius[i] l.Radius
      setShaderFloat shader locs.PointLights.Falloff[i] l.Falloff

      let slot =
        if i < pointShadowSlots.Length then
          pointShadowSlots[i]
        else
          -1

      setShaderInt shader locs.PointLights.ShadowIdx[i] slot

    let spCount = min lights.SpotLights.Count maxSp
    setShaderInt shader locs.SpotLights.Count spCount

    for i = 0 to spCount - 1 do
      let s: SpotLight3D = lights.SpotLights[i]
      setShaderVec3 shader locs.SpotLights.Pos[i] s.Position
      setShaderVec3 shader locs.SpotLights.Dir[i] s.Direction
      setShaderVec3 shader locs.SpotLights.Color[i] (colorToVec3 s.Color)
      setShaderFloat shader locs.SpotLights.Intensity[i] s.Intensity
      setShaderFloat shader locs.SpotLights.Radius[i] s.Radius
      setShaderFloat shader locs.SpotLights.InnerCutoff[i] s.InnerCutoff
      setShaderFloat shader locs.SpotLights.OuterCutoff[i] s.OuterCutoff

      let slot =
        if i < spotShadowSlots.Length then
          spotShadowSlots[i]
        else
          -1

      setShaderInt shader locs.SpotLights.ShadowIdx[i] slot

  /// Single parameterized material uniform setter replacing 3x duplication.
  let setMaterialUniforms
    (shader: Shader, matLocs: inref<MaterialUniforms>, mat3d: inref<Material3D>)
    =
    setShaderVec4
      shader
      matLocs.AlbedoColor
      (nativeColorToVec4 mat3d.AlbedoColor)

    setShaderFloat shader matLocs.Roughness mat3d.Roughness
    setShaderFloat shader matLocs.Metallic mat3d.Metallic

    setShaderVec4
      shader
      matLocs.EmissionColor
      (nativeColorToVec4 mat3d.EmissionColor)

    setShaderFloat shader matLocs.Opacity mat3d.Opacity
    setShaderVec2 shader matLocs.Tiling mat3d.Tiling

    let useNormal =
      match mat3d.NormalMap with
      | ValueSome _ -> 1
      | ValueNone -> 0

    setShaderInt shader matLocs.UseNormalMap useNormal

  /// Single parameterized material cache lookup/creation replacing 3x duplication.
  let getOrCreate
    (
      variant: byref<ShaderVariant>,
      shader: Shader,
      mat3d: inref<Material3D>,
      key: inref<MaterialKey>
    ) : Material =
    let mc = variant.MaterialCache

    if mc.HasLast && key = mc.LastKey then
      mc.LastMaterial
    else
      match mc.cache.TryGetValue key with
      | true, mat ->
        variant.MaterialCache.LastKey <- key
        variant.MaterialCache.LastMaterial <- mat
        variant.MaterialCache.HasLast <- true
        mat
      | false, _ ->
        let mutable mat = Raylib.LoadMaterialDefault()
        mat.Shader <- shader

        match mat3d.AlbedoMap with
        | ValueSome t ->
          Raylib.SetMaterialTexture(&mat, MaterialMapIndex.Albedo, t)
        | ValueNone -> ()

        match mat3d.RoughnessMap with
        | ValueSome t ->
          Raylib.SetMaterialTexture(&mat, MaterialMapIndex.Roughness, t)
        | ValueNone -> ()

        match mat3d.MetallicMap with
        | ValueSome t ->
          Raylib.SetMaterialTexture(&mat, MaterialMapIndex.Metalness, t)
        | ValueNone -> ()

        match mat3d.NormalMap with
        | ValueSome t ->
          Raylib.SetMaterialTexture(&mat, MaterialMapIndex.Normal, t)
        | ValueNone -> ()

        match mat3d.EmissionMap with
        | ValueSome t ->
          Raylib.SetMaterialTexture(&mat, MaterialMapIndex.Emission, t)
        | ValueNone -> ()

        variant.MaterialCache.cache[key] <- mat
        variant.MaterialCache.LastKey <- key
        variant.MaterialCache.LastMaterial <- mat
        variant.MaterialCache.HasLast <- true
        mat

  /// Upload shadow atlas uniforms to a single shader.
  let uploadShadowUniformsForShader
    (
      shader: Shader,
      shadowLocs: inref<ShadowUniforms>,
      cameraLoc: int,
      atlas: ShadowAtlas,
      cameraPos: Vector3,
      maxCasters: int
    ) =
    if atlas.Fbo.Depth.Id <> 0u then
      Rlgl.EnableShader shader.Id
      Rlgl.ActiveTextureSlot 15
      Rlgl.EnableTexture atlas.Fbo.Depth.Id
      rlSetUniformInt shadowLocs.Atlas 15
      Rlgl.ActiveTextureSlot 0

    let count = min atlas.ActiveCasterCount maxCasters

    for i = 0 to count - 1 do
      Raylib.SetShaderValueMatrix(
        shader,
        shadowLocs.ViewProjs[i],
        atlas.ViewProjs[i]
      )

      setShaderVec4 shader shadowLocs.UVOffsets[i] atlas.UVOffsets[i]
      setShaderVec3 shader shadowLocs.LightPositions[i] atlas.LightPositions[i]
      setShaderFloat shader shadowLocs.Biases[i] atlas.Biases[i]
      setShaderInt shader shadowLocs.Types[i] atlas.CasterTypes[i]

    setShaderInt shader shadowLocs.CasterCount atlas.ActiveCasterCount
    setShaderVec3 shader cameraLoc cameraPos
    setShaderInt shader shadowLocs.Pass 0

  /// <summary>
  /// Builds the shadow result from the atlas's current state: <c>ValueNone</c> when no caster
  /// registered or none fit the atlas; otherwise the packed uniforms + per-light slot mappings
  /// a custom shader reads to opt into shadow sampling.
  /// </summary>
  let inline shadowResultOf
    (atlas: ShadowAtlas)
    (atlasCfg: ShadowAtlasConfig)
    (hasCasters: bool)
    (dirCasts: bool)
    (pointSlots: int[])
    (spotSlots: int[])
    : ShadowResult voption =
    if hasCasters && atlas.ActiveCasterCount > 0 then
      ValueSome {
        Atlas = atlas.Fbo.Depth
        ViewProjs = atlas.ViewProjs
        UVOffsets = atlas.UVOffsets
        ActiveCasterCount = atlas.ActiveCasterCount
        TexelSize = 1.0f / float32 atlasCfg.Resolution
        Biases = atlas.Biases
        DirLightCastsShadows = dirCasts
        PointLightShadowIdx = pointSlots
        SpotLightShadowIdx = spotSlots
      }
    else
      ValueNone

  /// Upload shadow atlas uniforms to all four shader variants.
  let uploadShadowUniforms
    (
      hasCasters: bool,
      forward: inref<ShaderVariant>,
      instanced: inref<ShaderVariant>,
      skinned: inref<ShaderVariant>,
      skinnedInstanced: inref<ShaderVariant>,
      atlas: ShadowAtlas,
      cameraPos: Vector3,
      maxCasters: int
    ) =
    if hasCasters then
      atlas.PrepareUniforms()
      let fwd = forward.Locs
      let inst = instanced.Locs
      let sk = skinned.Locs
      let skInst = skinnedInstanced.Locs

      uploadShadowUniformsForShader(
        fwd.Shader,
        &fwd.Shadow,
        fwd.CameraPos,
        atlas,
        cameraPos,
        maxCasters
      )

      uploadShadowUniformsForShader(
        inst.Shader,
        &inst.Shadow,
        inst.CameraPos,
        atlas,
        cameraPos,
        maxCasters
      )

      uploadShadowUniformsForShader(
        sk.Shader,
        &sk.Shadow,
        sk.CameraPos,
        atlas,
        cameraPos,
        maxCasters
      )

      uploadShadowUniformsForShader(
        skInst.Shader,
        &skInst.Shadow,
        skInst.CameraPos,
        atlas,
        cameraPos,
        maxCasters
      )

  /// Upload bone matrices to skinned shader — uses ReadOnlySpan for no-copy.
  /// Palettes arrive in plain System.Numerics row-major layout
  /// (<c>InverseBindPose[i] * pose[i]</c>); SetShaderValueMatrix expects the
  /// transposed (raylib-native) layout, so transpose each matrix here. This is
  /// the non-instanced path — at most 128 bones per draw, so the per-matrix
  /// transpose stays off the instanced hot path.
  let inline uploadBoneMatrices
    (shader: Shader, boneLoc: int, bones: ReadOnlySpan<Matrix4x4>)
    =
    let count = min bones.Length 128

    for i = 0 to count - 1 do
      Raylib.SetShaderValueMatrix(
        shader,
        boneLoc + i,
        Matrix4x4.Transpose bones[i]
      )

  /// Upload a palette slice into a palette texture: texel (boneIndex*4+c, instance)
  /// receives floats 4c..4c+3 of the instance's bone matrix. The palettes array
  /// holds <c>InverseBindPose[i] * pose[i]</c> in plain System.Numerics row-major
  /// layout — exactly the texel layout the shader's getBoneMatrix expects — so
  /// the contiguous slice uploads verbatim: one pinned copy, no staging array,
  /// no per-matrix work.
  let uploadPaletteChunk
    (palettes: Matrix4x4[])
    (offset: int)
    (tex: Texture2D)
    =
    use p = fixed &palettes[offset]
    Raylib.UpdateTexture(tex, NativePtr.toVoidPtr p)

  /// Draw a skinned-instanced mesh in chunks of at most maxPaletteTextureRows
  /// instances: upload each chunk's palette slice to a pooled palette texture,
  /// bind it to paletteTextureSlot, and issue one DrawMeshInstanced per chunk.
  /// gl_InstanceID indexes the chunk-local palette row, so the texture height
  /// matches the chunk's instance count. Chunks already uploaded this frame
  /// (e.g. by the shadow pass — both passes render the same command list) are
  /// rebound without re-uploading.
  let drawSkinnedInstancedChunks
    (
      pool: PaletteTexturePool,
      shader: Shader,
      bonePaletteLoc: int,
      bonePaletteSizeLoc: int,
      mesh: Mesh,
      mat: Material,
      transforms: Matrix4x4[],
      palettes: Matrix4x4[],
      instanceCount: int,
      boneCount: int
    ) =
    // A boneless command (boneCount = 0 — only possible from a manually
    // constructed DrawSkinnedMeshInstanced) no-ops: pinning the empty palette
    // slice and acquiring a zero-width palette texture would both throw.
    if boneCount > 0 then
      let mutable start = 0

      while start < instanceCount do
        let chunkCount = min maxPaletteTextureRows (instanceCount - start)

        let tex =
          match pool.TryGetUploaded(palettes, start) with
          | ValueSome cached -> cached
          | ValueNone ->
            let tex = pool.Acquire(boneCount * 4, chunkCount)
            uploadPaletteChunk palettes (start * boneCount) tex
            pool.RememberUploaded(palettes, start, tex)
            tex

        Rlgl.EnableShader shader.Id
        Rlgl.ActiveTextureSlot paletteTextureSlot
        Rlgl.EnableTexture tex.Id
        rlSetUniformInt bonePaletteLoc paletteTextureSlot

        if bonePaletteSizeLoc >= 0 then
          setShaderIVec2 shader bonePaletteSizeLoc (boneCount * 4) chunkCount

        Rlgl.ActiveTextureSlot 0

        // raylib draws instances 0..count-1 of the array, so a chunk past the first
        // needs a sliced transforms array. The unchunked case passes it through
        // untouched (no allocation on the common path); chunked slices copy into
        // a pooled scratch buffer instead of allocating per chunk.
        let chunkTransforms =
          if chunkCount = instanceCount then
            transforms
          else
            let scratch = pool.GetTransformScratch chunkCount
            Array.Copy(transforms, start, scratch, 0, chunkCount)
            scratch

        Raylib.DrawMeshInstanced(mesh, mat, chunkTransforms, chunkCount)
        start <- start + chunkCount

  /// Clear all light buffers.
  let inline clearLights(lights: LightBuffers) = LightBuffers.clear lights

  /// Warm material caches for a single material using the appropriate variant.
  let inline warmMaterial
    (
      forward: byref<ShaderVariant>,
      instanced: byref<ShaderVariant>,
      skinned: byref<ShaderVariant>,
      forwardShader: Shader,
      instancedShader: Shader,
      skinnedShader: Shader,
      mat: inref<Material3D>,
      variant: int
    ) =
    let key = MaterialKey.fromMaterial3D &mat

    match variant with
    | 1 -> getOrCreate(&forward, forwardShader, &mat, &key) |> ignore
    | 2 -> getOrCreate(&instanced, instancedShader, &mat, &key) |> ignore
    | 3 -> getOrCreate(&skinned, skinnedShader, &mat, &key) |> ignore
    | _ -> ()

  /// Apply camera config: viewport and clear color.
  let applyCameraConfig(cfg: inref<Camera3DConfig>, gameCtx: GameContext) =
    match cfg.Viewport with
    | ValueSome vp ->
      let x = int(vp.X * float32 gameCtx.WindowWidth)
      let y = int(vp.Y * float32 gameCtx.WindowHeight)
      let w = int(vp.Width * float32 gameCtx.WindowWidth)
      let h = int(vp.Height * float32 gameCtx.WindowHeight)

      match cfg.ClearColor with
      | ValueSome color ->
        Rlgl.EnableScissorTest()
        Rlgl.Scissor(x, y, w, h)
        Raylib.ClearBackground color
        Rlgl.DisableScissorTest()
      | ValueNone -> ()

      Rlgl.Viewport(x, y, w, h)
    | ValueNone ->
      match cfg.ClearColor with
      | ValueSome color -> Raylib.ClearBackground color
      | ValueNone -> ()

  /// Handle a single forward draw: begin shader, upload lights, set material, draw, end shader.
  let inline handleDrawMesh
    (
      shader: Shader,
      variant: byref<ShaderVariant>,
      lights: LightBuffers,
      maxPt: int,
      maxSp: int,
      pointShadowSlots: int[],
      spotShadowSlots: int[],
      currentCamera: Camera3D,
      mesh: Mesh,
      transform: Matrix4x4,
      material: Material3D
    ) =
    Raylib.BeginShaderMode shader

    if variant.LightsDirty then
      uploadLights(
        shader,
        &variant,
        lights,
        maxPt,
        maxSp,
        pointShadowSlots,
        spotShadowSlots
      )

      variant.LightsDirty <- false

    setShaderVec3 shader variant.Locs.CameraPos currentCamera.Position
    setShaderInt shader variant.Locs.Shadow.Pass 0

    let nm = computeNormalMatrix transform
    Raylib.SetShaderValueMatrix(shader, variant.Locs.Material.NormalMatrix, nm)
    let key = MaterialKey.fromMaterial3D &material

    if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
      setMaterialUniforms(shader, &variant.Locs.Material, &material)
      variant.LastMaterialKey <- key
      variant.HasLastMaterial <- true

    let mat = getOrCreate(&variant, shader, &material, &key)
    Raylib.DrawMesh(mesh, mat, transform)
    Raylib.EndShaderMode()

  /// Handle model draw: iterate meshes, upload lights once, draw each.
  let inline handleDrawModel
    (
      shader: Shader,
      variant: byref<ShaderVariant>,
      lights: LightBuffers,
      maxPt: int,
      maxSp: int,
      pointShadowSlots: int[],
      spotShadowSlots: int[],
      currentCamera: Camera3D,
      model: Model,
      transform: Matrix4x4,
      matOverride: MaterialOverride voption
    ) =
    Raylib.BeginShaderMode shader

    if variant.LightsDirty then
      uploadLights(
        shader,
        &variant,
        lights,
        maxPt,
        maxSp,
        pointShadowSlots,
        spotShadowSlots
      )

      variant.LightsDirty <- false

    setShaderVec3 shader variant.Locs.CameraPos currentCamera.Position
    setShaderInt shader variant.Locs.Shadow.Pass 0

    let nm = computeNormalMatrix transform
    Raylib.SetShaderValueMatrix(shader, variant.Locs.Material.NormalMatrix, nm)

    for mi = 0 to model.MeshCount - 1 do
      let mesh = NativePtr.get model.Meshes mi
      let matIdx = NativePtr.get model.MeshMaterial mi
      let raylibMat = NativePtr.get model.Materials matIdx

      let mat3d =
        match matOverride with
        | ValueNone -> Material3D.fromRaylibMaterial raylibMat
        | ValueSome(MaterialOverride.All m) -> m
        | ValueSome(MaterialOverride.PerMesh f) -> f mi

      let key = MaterialKey.fromMaterial3D &mat3d

      if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
        setMaterialUniforms(shader, &variant.Locs.Material, &mat3d)
        variant.LastMaterialKey <- key
        variant.HasLastMaterial <- true

      let mat = getOrCreate(&variant, shader, &mat3d, &key)
      Raylib.DrawMesh(mesh, mat, transform)

    Raylib.EndShaderMode()

  /// Handle skinned mesh draw: shader switch, lights, bones, material, draw.
  let inline handleDrawSkinnedMesh
    (
      shader: Shader,
      variant: byref<ShaderVariant>,
      lights: LightBuffers,
      maxPt: int,
      maxSp: int,
      pointShadowSlots: int[],
      spotShadowSlots: int[],
      currentCamera: Camera3D,
      mesh: Mesh,
      transform: Matrix4x4,
      material: Material3D,
      bones: Matrix4x4[]
    ) =
    Raylib.BeginShaderMode shader

    if variant.LightsDirty then
      uploadLights(
        shader,
        &variant,
        lights,
        maxPt,
        maxSp,
        pointShadowSlots,
        spotShadowSlots
      )

      variant.LightsDirty <- false

    setShaderVec3 shader variant.Locs.CameraPos currentCamera.Position
    setShaderInt shader variant.Locs.Shadow.Pass 0
    let nm = computeNormalMatrix transform
    Raylib.SetShaderValueMatrix(shader, variant.Locs.Material.NormalMatrix, nm)
    let key = MaterialKey.fromMaterial3D &material

    if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
      setMaterialUniforms(shader, &variant.Locs.Material, &material)
      variant.LastMaterialKey <- key
      variant.HasLastMaterial <- true

    uploadBoneMatrices(shader, variant.Locs.Bones, ReadOnlySpan bones)
    let mat = getOrCreate(&variant, shader, &material, &key)
    Raylib.DrawMesh(mesh, mat, transform)
    Raylib.EndShaderMode()

  /// Handle instanced mesh draw: shader switch, lights, material, draw.
  let inline handleDrawMeshInstanced
    (
      shader: Shader,
      variant: byref<ShaderVariant>,
      lights: LightBuffers,
      maxPt: int,
      maxSp: int,
      pointShadowSlots: int[],
      spotShadowSlots: int[],
      currentCamera: Camera3D,
      mesh: Mesh,
      transforms: Matrix4x4[],
      material: Material3D,
      instanceCount: int
    ) =
    Raylib.BeginShaderMode shader

    if variant.LightsDirty then
      uploadLights(
        shader,
        &variant,
        lights,
        maxPt,
        maxSp,
        pointShadowSlots,
        spotShadowSlots
      )

      variant.LightsDirty <- false

    setShaderVec3 shader variant.Locs.CameraPos currentCamera.Position
    setShaderInt shader variant.Locs.Shadow.Pass 0

    let key = MaterialKey.fromMaterial3D &material

    if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
      setMaterialUniforms(shader, &variant.Locs.Material, &material)

      variant.LastMaterialKey <- key
      variant.HasLastMaterial <- true

    let mat = getOrCreate(&variant, shader, &material, &key)
    Raylib.DrawMeshInstanced(mesh, mat, transforms, instanceCount)
    Raylib.EndShaderMode()

  /// Handle skinned + instanced mesh draw: shader switch, lights, material,
  /// then chunked palette-texture draws (one DrawMeshInstanced per chunk).
  /// The mvp uniform is view-projection only — raylib uploads it that way for
  /// instanced draws, since no per-instance model is pushed onto the matrix
  /// stack; the per-instance transform comes from the instanceTransform VBO.
  let inline handleDrawSkinnedMeshInstanced
    (
      shader: Shader,
      variant: byref<ShaderVariant>,
      lights: LightBuffers,
      maxPt: int,
      maxSp: int,
      pointShadowSlots: int[],
      spotShadowSlots: int[],
      currentCamera: Camera3D,
      pool: PaletteTexturePool,
      mesh: Mesh,
      transforms: Matrix4x4[],
      palettes: Matrix4x4[],
      material: Material3D,
      instanceCount: int,
      boneCount: int
    ) =
    Raylib.BeginShaderMode shader

    if variant.LightsDirty then
      uploadLights(
        shader,
        &variant,
        lights,
        maxPt,
        maxSp,
        pointShadowSlots,
        spotShadowSlots
      )

      variant.LightsDirty <- false

    setShaderVec3 shader variant.Locs.CameraPos currentCamera.Position
    setShaderInt shader variant.Locs.Shadow.Pass 0

    let key = MaterialKey.fromMaterial3D &material

    if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
      setMaterialUniforms(shader, &variant.Locs.Material, &material)

      variant.LastMaterialKey <- key
      variant.HasLastMaterial <- true

    let mat = getOrCreate(&variant, shader, &material, &key)

    drawSkinnedInstancedChunks(
      pool,
      shader,
      variant.Locs.BonePalette,
      variant.Locs.BonePaletteSize,
      mesh,
      mat,
      transforms,
      palettes,
      instanceCount,
      boneCount
    )

    Raylib.EndShaderMode()

  /// True when a billboard source rect is the all-zero sentinel (= full texture).
  let inline isZeroSourceRect(rect: Rectangle) =
    rect.X = 0.0f && rect.Y = 0.0f && rect.Width = 0.0f && rect.Height = 0.0f

  /// Resolve a billboard source rect: the all-zero sentinel means full texture.
  let inline resolveSourceRect (texture: Texture2D) (rect: Rectangle) =
    if isZeroSourceRect rect then
      Rectangle(0.0f, 0.0f, float32 texture.Width, float32 texture.Height)
    else
      rect

  /// Handle billboard draw using default shader.
  let inline handleDrawBillboard(currentCamera: Camera3D, bb: Billboard3D) =
    Rlgl.EnableShader(Rlgl.GetShaderIdDefault())

    let source = resolveSourceRect bb.Texture bb.SourceRect

    Raylib.BeginBlendMode bb.Blend

    Raylib.DrawBillboardPro(
      currentCamera,
      bb.Texture,
      source,
      bb.Position,
      currentCamera.Up,
      bb.Size,
      bb.Size * 0.5f,
      bb.Rotation,
      bb.Color
    )

    Raylib.EndBlendMode()

  /// Handle billboard batch draw using default shader.
  let inline handleDrawBillboardBatch
    (currentCamera: Camera3D, batch: BillboardBatch3D)
    =
    Rlgl.EnableShader(Rlgl.GetShaderIdDefault())
    Raylib.BeginBlendMode batch.Blend

    for bi = 0 to batch.Count - 1 do
      let tex = batch.Textures[bi]

      let source =
        if not(isNull batch.SourceRects) && bi < batch.SourceRects.Length then
          resolveSourceRect tex batch.SourceRects[bi]
        else
          Rectangle(0.0f, 0.0f, float32 tex.Width, float32 tex.Height)

      let rotation =
        if not(isNull batch.Rotations) && bi < batch.Rotations.Length then
          batch.Rotations[bi]
        else
          0.0f

      let size = batch.Sizes[bi]

      Raylib.DrawBillboardPro(
        currentCamera,
        tex,
        source,
        batch.Positions[bi],
        currentCamera.Up,
        size,
        size * 0.5f,
        rotation,
        batch.Colors[bi]
      )

    Raylib.EndBlendMode()

  /// Handle light command: add or set light, mark dirty.
  let inline handleLightCommand
    (
      lights: LightBuffers,
      command: Command3D,
      forward: byref<ShaderVariant>,
      instanced: byref<ShaderVariant>,
      skinned: byref<ShaderVariant>,
      skinnedInstanced: byref<ShaderVariant>
    ) =
    match command with
    | Command3D.SetAmbientLight l ->
      lights.Ambient <- ValueSome l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
      skinnedInstanced.LightsDirty <- true
    | Command3D.AddDirectionalLight l ->
      lights.DirLights.Add l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
      skinnedInstanced.LightsDirty <- true
    | Command3D.AddPointLight l ->
      lights.PointLights.Add l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
      skinnedInstanced.LightsDirty <- true
    | Command3D.AddSpotLight l ->
      lights.SpotLights.Add l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
      skinnedInstanced.LightsDirty <- true
    | _ -> ()

  /// Pre-scan buffer: collect camera, lights, shadow origin, and warm material caches.
  /// Returns the frame state for shadow pass.
  let preScan
    (
      buffer: RenderBuffer3D,
      lights: LightBuffers,
      gatherLights: bool,
      forward: byref<ShaderVariant>,
      instanced: byref<ShaderVariant>,
      skinned: byref<ShaderVariant>,
      skinnedInstanced: byref<ShaderVariant>,
      forwardShader: Shader,
      instancedShader: Shader,
      skinnedShader: Shader,
      skinnedInstancedShader: Shader,
      ppActions: ResizeArray<PostProcessContext3D -> unit> voption
    ) : FrameState =
    let mutable frameState = {
      Camera = ValueNone
      ShadowOrigin = ValueNone
    }

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command3D.BeginCamera cam ->
        match frameState.Camera with
        | ValueNone ->
          frameState <- {
            frameState with
                Camera = ValueSome cam
          }
        | ValueSome _ -> ()
      | Command3D.BeginCameraConfig cfg ->
        match frameState.Camera with
        | ValueNone ->
          frameState <- {
            frameState with
                Camera = ValueSome cfg.Camera
          }
        | ValueSome _ -> ()
      | Command3D.SetShadowOrigin origin ->
        frameState <- {
          frameState with
              ShadowOrigin = ValueSome origin
        }
      | Command3D.SetAmbientLight l ->
        if gatherLights then
          lights.Ambient <- ValueSome l
      | Command3D.AddDirectionalLight l ->
        if gatherLights then
          lights.DirLights.Add l
      | Command3D.AddPointLight l ->
        if gatherLights then
          lights.PointLights.Add l
      | Command3D.AddSpotLight l ->
        if gatherLights then
          lights.SpotLights.Add l
      | Command3D.DrawMesh(_, _, mat) ->
        warmMaterial(
          &forward,
          &instanced,
          &skinned,
          forwardShader,
          instancedShader,
          skinnedShader,
          &mat,
          1
        )
      | Command3D.DrawModel(model, transform) ->
        for mi = 0 to model.MeshCount - 1 do
          let matIdx = NativePtr.get model.MeshMaterial mi
          let raylibMat = NativePtr.get model.Materials matIdx
          let mat3d = Material3D.fromRaylibMaterial raylibMat

          warmMaterial(
            &forward,
            &instanced,
            &skinned,
            forwardShader,
            instancedShader,
            skinnedShader,
            &mat3d,
            1
          )
      | Command3D.DrawModelWith(model, _, matOverride) ->
        match matOverride with
        | MaterialOverride.All m ->
          warmMaterial(
            &forward,
            &instanced,
            &skinned,
            forwardShader,
            instancedShader,
            skinnedShader,
            &m,
            1
          )
        | MaterialOverride.PerMesh f ->
          for mi = 0 to model.MeshCount - 1 do
            let m = f mi

            warmMaterial(
              &forward,
              &instanced,
              &skinned,
              forwardShader,
              instancedShader,
              skinnedShader,
              &m,
              1
            )
      | Command3D.DrawSkinnedMesh(_, _, mat, _) ->
        warmMaterial(
          &forward,
          &instanced,
          &skinned,
          forwardShader,
          instancedShader,
          skinnedShader,
          &mat,
          3
        )
      | Command3D.DrawMeshInstanced(_, _, mat, _) ->
        warmMaterial(
          &forward,
          &instanced,
          &skinned,
          forwardShader,
          instancedShader,
          skinnedShader,
          &mat,
          2
        )
      | Command3D.DrawSkinnedMeshInstanced(_, _, _, mat, _, _) ->
        // The skinned-instanced variant is warmed directly — warmMaterial only
        // covers the original three variants.
        let key = MaterialKey.fromMaterial3D &mat

        getOrCreate(&skinnedInstanced, skinnedInstancedShader, &mat, &key)
        |> ignore
      | Command3D.PostProcess action
      | Command3D.PostProcessWithDepth action ->
        match ppActions with
        | ValueSome list -> list.Add action
        | ValueNone -> ()
      | _ -> ()

    frameState

  /// Render all mesh draws into a single shadow atlas region.
  /// Draws are partitioned: [0..skinnedStart) are non-skinned, [skinnedStart..meshDrawCount) are skinned.
  /// Instanced draws are rendered separately via `DrawMeshInstanced` (one GPU call per entry).
  let renderShadowRegion
    (
      shadowAtlas: ShadowAtlas,
      regionIndex: int,
      camera: Camera3D,
      resources: inref<ShadowDepthResources>,
      palettePool: PaletteTexturePool,
      meshDraws: MeshDraw[],
      meshDrawCount: int,
      skinnedStart: int,
      instancedDraws: InstancedMeshDraw[],
      instancedDrawCount: int
    ) =
    shadowAtlas.GetRegionViewport(regionIndex)
    Raylib.BeginMode3D(camera)

    let vp =
      Raymath.MatrixMultiply(
        Rlgl.GetMatrixModelview(),
        Rlgl.GetMatrixProjection()
      )

    shadowAtlas.SetRegionViewProj(regionIndex, vp)

    // ── Non-skinned batch: single BeginShaderMode block ──
    if skinnedStart > 0 then
      Raylib.BeginShaderMode resources.Shader
      let mutable lastTransform = Unchecked.defaultof<Matrix4x4>

      for i = 0 to skinnedStart - 1 do
        let draw = meshDraws[i]

        if draw.Transform <> lastTransform then
          let nm = computeNormalMatrix draw.Transform

          Raylib.SetShaderValueMatrix(
            resources.Shader,
            resources.NormalMatrixLoc,
            nm
          )

          lastTransform <- draw.Transform

        Raylib.DrawMesh(draw.Mesh, resources.Material, draw.Transform)

      Raylib.EndShaderMode()

    // ── Skinned batch: one Begin/End per mesh (bones differ per mesh) ──
    if skinnedStart < meshDrawCount then
      let mutable lastTransform = Unchecked.defaultof<Matrix4x4>

      for i = skinnedStart to meshDrawCount - 1 do
        let draw = meshDraws[i]

        Raylib.BeginShaderMode resources.SkinnedShader

        if draw.Transform <> lastTransform then
          let nm = computeNormalMatrix draw.Transform

          Raylib.SetShaderValueMatrix(
            resources.SkinnedShader,
            resources.SkinnedNormalMatrixLoc,
            nm
          )

          lastTransform <- draw.Transform

        match draw.Bones with
        | ValueSome bones ->
          uploadBoneMatrices(
            resources.SkinnedShader,
            resources.BoneLoc,
            ReadOnlySpan bones
          )
        | ValueNone -> ()

        Raylib.DrawMesh(draw.Mesh, resources.SkinnedMaterial, draw.Transform)

        Raylib.EndShaderMode()

    // ── Instanced batch: one DrawMeshInstanced per entry ──
    // Uses the instanced depth-shadow shader, which declares
    // `in mat4 instanceTransform` so raylib wires up the instance VBO.
    // The non-instanced `resources.Shader` lacks that attribute and would
    // collapse every instance to a single clip-space position.
    // Skinned-instanced entries (Palettes = ValueSome) render after the plain
    // ones with the depth skinned-instanced shader + a palette texture.
    if instancedDrawCount > 0 then
      Raylib.BeginShaderMode resources.InstancedShader

      for i = 0 to instancedDrawCount - 1 do
        let draw = instancedDraws[i]

        match draw.Palettes with
        | ValueNone ->
          Raylib.DrawMeshInstanced(
            draw.Mesh,
            resources.InstancedMaterial,
            draw.Transforms,
            draw.InstanceCount
          )
        | ValueSome _ -> ()

      Raylib.EndShaderMode()

      Raylib.BeginShaderMode resources.SkinnedInstancedShader

      for i = 0 to instancedDrawCount - 1 do
        let draw = instancedDraws[i]

        match draw.Palettes with
        | ValueSome palettes ->
          drawSkinnedInstancedChunks(
            palettePool,
            resources.SkinnedInstancedShader,
            resources.BonePaletteLoc,
            resources.BonePaletteSizeLoc,
            draw.Mesh,
            resources.SkinnedInstancedMaterial,
            draw.Transforms,
            palettes,
            draw.InstanceCount,
            draw.BoneCount
          )
        | ValueNone -> ()

      Raylib.EndShaderMode()

    Raylib.EndMode3D()

  /// Render the shadow pass — collect casters, render regions to atlas. Returns whether any
  /// caster was registered. The per-light shadow-slot mappings live in caller-owned grow-only
  /// arrays (the pipeline's fields), resized here when a pass sees more lights than any
  /// previous pass and reset to -1 ("no shadow") on entry.
  let runShadowPass
    (
      shadowAtlas: ShadowAtlas,
      atlasCfg: ShadowAtlasConfig,
      resources: inref<ShadowDepthResources>,
      palettePool: PaletteTexturePool,
      lights: LightBuffers,
      meshDraws: MeshDraw[],
      meshDrawCount: int,
      skinnedStart: int,
      instancedDraws: InstancedMeshDraw[],
      instancedDrawCount: int,
      frameState: inref<FrameState>,
      gameCtx: GameContext,
      pointSlots: byref<int[]>,
      spotSlots: byref<int[]>
    ) : bool =
    shadowAtlas.Clear()

    if pointSlots.Length < lights.PointLights.Count then
      pointSlots <- Array.create<int> lights.PointLights.Count -1

    Array.Fill(pointSlots, -1)

    if spotSlots.Length < lights.SpotLights.Count then
      spotSlots <- Array.create<int> lights.SpotLights.Count -1

    Array.Fill(spotSlots, -1)

    let mutable hasCasters = false

    match frameState.Camera with
    | ValueNone ->
      // No camera → no shadow pass; slots stay all -1 (no shadows).
      ()
    | ValueSome activeCamera ->
      // Instanced-only scenes cast shadows too (matches the MonoGame backend, which gates
      // on mesh + instanced counts).
      if meshDrawCount > 0 || instancedDrawCount > 0 then
        hasCasters <-
          collectShadowCasters(lights, shadowAtlas, pointSlots, spotSlots)

        if shadowAtlas.Count > 0 then
          Raylib.BeginTextureMode(shadowAtlas.Fbo)
          Raylib.ClearBackground(Color.White)

          for caster in shadowAtlas.Casters do
            if caster.Enabled then
              let lightPos =
                if caster.Type = ShadowCasterType.Directional then
                  activeCamera.Position
                else
                  caster.LightPosition

              let distToCamera =
                (lightPos - activeCamera.Position).LengthSquared()

              let maxShadowDist =
                atlasCfg.MaxShadowLightDistance
                * atlasCfg.MaxShadowLightDistance

              if distToCamera <= maxShadowDist then
                match caster.Type with
                | ShadowCasterType.Point ->
                  let rawDir = caster.LightDirection
                  let len = rawDir.Length()

                  let shadowDir =
                    if len > 0.0001f then rawDir / len else -Vector3.UnitY

                  let safeUp =
                    if abs shadowDir.Y > 0.99f then
                      Vector3.UnitZ
                    else
                      Vector3.UnitY

                  let ptCamera =
                    Camera3D(
                      Position = caster.LightPosition,
                      Target = caster.LightPosition + shadowDir,
                      Up = safeUp,
                      FovY = 90.0f,
                      Projection = CameraProjection.Perspective
                    )

                  renderShadowRegion(
                    shadowAtlas,
                    caster.AtlasRegion,
                    ptCamera,
                    &resources,
                    palettePool,
                    meshDraws,
                    meshDrawCount,
                    skinnedStart,
                    instancedDraws,
                    instancedDrawCount
                  )

                | ShadowCasterType.Spot ->
                  let spotDir = caster.LightDirection
                  let len = spotDir.Length()

                  let dir =
                    if len > 0.0001f then spotDir / len else -Vector3.UnitY

                  let safeUp =
                    if abs dir.Y > 0.99f then Vector3.UnitZ else Vector3.UnitY

                  let spotCamera =
                    Camera3D(
                      Position = caster.LightPosition,
                      Target = caster.LightPosition + dir,
                      Up = safeUp,
                      FovY = 90.0f,
                      Projection = CameraProjection.Perspective
                    )

                  renderShadowRegion(
                    shadowAtlas,
                    caster.AtlasRegion,
                    spotCamera,
                    &resources,
                    palettePool,
                    meshDraws,
                    meshDrawCount,
                    skinnedStart,
                    instancedDraws,
                    instancedDrawCount
                  )

                | _ ->
                  let prevNear = Rlgl.GetCullDistanceNear()
                  let prevFar = Rlgl.GetCullDistanceFar()

                  let dirCamera =
                    createDirectionalShadowCamera(
                      caster,
                      &frameState,
                      atlasCfg,
                      activeCamera
                    )

                  renderShadowRegion(
                    shadowAtlas,
                    caster.AtlasRegion,
                    dirCamera,
                    &resources,
                    palettePool,
                    meshDraws,
                    meshDrawCount,
                    skinnedStart,
                    instancedDraws,
                    instancedDrawCount
                  )

                  Rlgl.SetClipPlanes(prevNear, prevFar)

          Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)
          Raylib.EndTextureMode()

    hasCasters

// ------------------------------------------------------------------
// ForwardFrame — per-frame scene state the Shade hook reads (byref, no alloc).
// ------------------------------------------------------------------

/// <summary>Per-frame scene state passed to <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Shade"/>.</summary>
/// <remarks>
/// <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.ForwardFrame.Lights"/> is frame-global in
/// single-camera frames; in frames with more than one camera block it is scoped to the block
/// currently being drawn (reset-with-inheritance — see
/// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.LightBuffers"/>), and the shadow fields are
/// reseated from that block's shadow pass at the block's start.
/// </remarks>
[<Struct>]
type ForwardFrame = {
  /// <summary>The active light set (see type remarks).</summary>
  Lights: LightBuffers
  /// <summary>Per-light shadow atlas slots (-1 = no shadow), indexed by PointLights position.
  /// Reseated from the shadow pass output at each camera block's start.</summary>
  mutable PointShadowSlots: int[]
  /// <summary>Per-light shadow atlas slots (-1 = no shadow), indexed by SpotLights position.
  /// Reseated from the shadow pass output at each camera block's start.</summary>
  mutable SpotShadowSlots: int[]
  /// <summary>The active shadow pass output — ValueNone when no shadow-casting light.
  /// Reseated from the shadow pass output at each camera block's start.
  /// The user-effect scope uploads these uniforms by name so a custom shader can opt into shadows.</summary>
  mutable Shadows: ShadowResult voption
  /// <summary>Total elapsed game time, in seconds — the <c>time</c> uniform for animated shaders.</summary>
  Time: float32
}

// ------------------------------------------------------------------
// ForwardPipelineBase — abstract staged forward pipeline base.
//
// Owns the gather + shadow pass + forward-pass orchestration + a virtual Shade
// for per-draw shading. The default Shade routes the shaded draw kinds through
// the cached Cook-Torrance PBR shaders, or — when a user-effect scope is open
// (beginEffect/endEffect) — name-resolved SceneUpload to the user shader.
// Override Shade to plug a different shading strategy while inheriting the
// camera/light/shadow gather and orchestration.
// ------------------------------------------------------------------

/// <summary>
/// Abstract staged forward 3D pipeline base for the raylib backend. Implements
/// <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/> by dispatching
/// <see cref="T:Mibo.Elmish.Graphics3D.Command3D"/> values, split into reusable stages —
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Execute"/> (orchestration),
/// the pre-scan gather, the shadow pass, and a virtual <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Shade"/>
/// for per-draw shading. The default <c>Shade</c> routes the shaded draw kinds (mesh / skinned
/// mesh / model / instanced) through the cached Cook-Torrance PBR shaders, so models and instanced
/// geometry get PBR + point/spot lights + shadows automatically. When a user-effect scope is open
/// (<c>beginEffect</c>/<c>endEffect</c>), the default <c>Shade</c> uploads the scene data to the
/// user shader by name via <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload"/>.
/// </summary>
/// <remarks>
/// <para>
/// In frames with more than one camera block, lights and shadows are scoped per block: a block
/// that issues light commands resets to the frame defaults (light commands issued outside any
/// camera block) plus its own commands, applied in-order; a block without light commands
/// inherits the running set. Each block renders its own shadow map at its start. Single-camera
/// frames gather lights frame-globally and render one shadow map for the frame.
/// </para>
/// <para>
/// Override <c>Shade</c> to plug a different shading strategy (toon, cel, custom). The scene
/// gather, shadow pass, and forward-pass dispatch are inherited.
/// </para>
/// <para>
/// Register via:
/// <code lang="fsharp">
/// Renderer3D.create (ForwardPbrPipeline()) view
/// </code>
/// </para>
/// </remarks>
[<AbstractClass>]
type ForwardPipelineBase
  (
    ?maxPointLights: int,
    ?maxSpotLights: int,
    ?shadowAtlasConfig: ShadowAtlasConfig,
    ?shadowBiasConfig: ShadowBiasConfig
  ) =

  let maxPt = defaultArg maxPointLights 8
  let maxSp = defaultArg maxSpotLights 4

  let atlasCfg = defaultArg shadowAtlasConfig ShadowAtlasConfig.defaults
  let biasCfg = defaultArg shadowBiasConfig ShadowBiasConfig.defaults

  // ── Mutable state ─────────────────────────────────────────
  let mutable forwardShader: Shader = Unchecked.defaultof<Shader>
  let mutable instancedShader: Shader = Unchecked.defaultof<Shader>
  let mutable skinnedShader: Shader = Unchecked.defaultof<Shader>

  let mutable skinnedInstancedShader: Shader = Unchecked.defaultof<Shader>

  let mutable depthShadowShader: Shader = Unchecked.defaultof<Shader>
  let mutable depthShadowSkinnedShader: Shader = Unchecked.defaultof<Shader>
  let mutable depthShadowInstancedShader: Shader = Unchecked.defaultof<Shader>

  let mutable depthShadowSkinnedInstancedShader: Shader =
    Unchecked.defaultof<Shader>

  let mutable depthShadowMaterial: Material = Unchecked.defaultof<Material>

  let mutable depthShadowSkinnedMaterial: Material =
    Unchecked.defaultof<Material>

  let mutable depthShadowInstancedMaterial: Material =
    Unchecked.defaultof<Material>

  let mutable depthShadowSkinnedInstancedMaterial: Material =
    Unchecked.defaultof<Material>

  let mutable shadowNormalMatrixLoc: int = -1
  let mutable shadowSkinnedNormalMatrixLoc: int = -1
  let mutable shadowBoneLoc: int = -1
  let mutable shadowBonePaletteLoc: int = -1
  let mutable shadowBonePaletteSizeLoc: int = -1

  let mutable forward: ShaderVariant = Unchecked.defaultof<ShaderVariant>
  let mutable instanced: ShaderVariant = Unchecked.defaultof<ShaderVariant>
  let mutable skinned: ShaderVariant = Unchecked.defaultof<ShaderVariant>

  let mutable skinnedInstanced: ShaderVariant =
    Unchecked.defaultof<ShaderVariant>

  // Pooled RGBA32F bone-palette textures for skinned-instanced draws (both the
  // forward pass and the shadow pass). Acquired per chunk, released once per
  // frame at the end of Execute.
  let palettePool = PaletteTexturePool()

  let mutable shadowAtlas: ShadowAtlas = Unchecked.defaultof<ShadowAtlas>

  // Reusable material for the user-effect scope (shadeWithEffect). Its .Shader is set per-scope
  // and its maps are populated per-draw from the Material3D — avoids per-draw LoadMaterialDefault
  // leaks. Built lazily on first user-effect draw.
  let mutable userEffectMaterial: Material = Unchecked.defaultof<Material>
  let mutable userEffectMaterialCreated = false

  // Resolved `instanceTransform` attribute location per user shader, memoized on the first
  // instanced draw inside a beginEffect/endEffect scope (-1 = the shader doesn't declare the
  // attribute -> no opt-in -> instanced draws fall back to the PBR instanced path). Keyed by
  // the full Shader value (Id + Locs pointer), not the GL Id alone: OpenGL reuses program
  // ids after unload, so an Id-keyed cache could hand a reloaded shader its predecessor's
  // stale location. Mirrors the MonoGame IsInstanceCapable memoization (keyed by Effect
  // reference).
  let mutable instanceAttrLocs: Dictionary<Shader, int> =
    Dictionary<Shader, int>()

  // Resolved (bonePalette, bonePaletteSize) uniform locations per user shader for
  // skinned-instanced draws inside a beginEffect/endEffect scope, memoized like
  // instanceAttrLocs. bonePalette = -1 = the shader doesn't opt in (no
  // instanceTransform + bone attributes + bonePalette sampler) -> skinned-instanced
  // draws fall back to the built-in variant. bonePaletteSize = -1 is allowed (the
  // uniform is optional; the draw helper skips setting it).
  let mutable skinnedInstancedLocs: Dictionary<Shader, struct (int * int)> =
    Dictionary<Shader, struct (int * int)>()

  // Per-light shadow caster slot mapping (computed in runShadowPass, read in uploadLights).
  // Indexed by lights.PointLights/SpotLights buffer position; -1 = no shadow. Reallocated per
  // frame to match the live light counts.
  let mutable pointShadowSlots: int[] = [||]
  let mutable spotShadowSlots: int[] = [||]

  let lights: LightBuffers = createLightBuffers(maxPt, maxSp)

  // Frame-default light set for multi-camera-block frames: repopulated from the block plan
  // each frame; a block that issues its own light commands resets the live buffers from this.
  let defaultLights: LightBuffers = createLightBuffers(maxPt, maxSp)

  // Scratch for a block's final light set (loaded from the block plan) when running that
  // block's shadow pass — the live buffers trail the block's own in-order commands at block
  // start, so the pass can't read them.
  let blockLights: LightBuffers = createLightBuffers(maxPt, maxSp)

  let applyPostProcess
    (ctx: GameContext)
    (sceneTarget: RenderTexture2D)
    (rtPool: IRenderTargetPool3D)
    (actions: ResizeArray<PostProcessContext3D -> unit>)
    (depth: Texture2D voption)
    (frameTime: float32)
    =
    if actions.Count = 0 then
      ()
    else
      let mutable src = sceneTarget
      let w = ctx.WindowWidth
      let h = ctx.WindowHeight

      for i = 0 to actions.Count - 1 do
        let isLast = i = actions.Count - 1

        let dst: RenderTexture2D voption =
          if isLast then
            ValueNone
          else
            ValueSome(rtPool.Acquire(w, h))

        match dst with
        | ValueSome target ->
          Raylib.BeginTextureMode target
          Raylib.ClearBackground Color.Black
        | ValueNone -> ()

        let ppCtx: PostProcessContext3D = {
          Source = src
          Depth = depth
          Width = w
          Height = h
          Time = frameTime
          Context = ctx
        }

        actions[i]ppCtx

        match dst with
        | ValueSome target ->
          Raylib.EndTextureMode()
          src <- target
        | ValueNone -> ()

  // ----------------------------------------------------------------
  // Shadow passes — the frame-global single-camera pass and the per-block
  // multi-camera-block pass.
  // ----------------------------------------------------------------

  /// <summary>
  /// Single-camera shadow pass: collects casters frame-globally, renders the atlas, uploads
  /// shadow uniforms to all three shader variants, and returns the frame's shadow result.
  /// </summary>
  member private this.runFrameShadowPass
    (
      gameCtx: GameContext,
      buffer: RenderBuffer3D,
      resources: inref<ShadowDepthResources>,
      frameState: inref<FrameState>
    ) : ShadowResult voption =
    let struct (meshDraws, meshDrawCount, skinnedStart, instancedDraws,
                instancedDrawCount) =
      collectMeshDraws(buffer, 0, buffer.Count, true)

    let mutable hasCasters = false

    try
      hasCasters <-
        runShadowPass(
          shadowAtlas,
          atlasCfg,
          &resources,
          palettePool,
          lights,
          meshDraws,
          meshDrawCount,
          skinnedStart,
          instancedDraws,
          instancedDrawCount,
          &frameState,
          gameCtx,
          &pointShadowSlots,
          &spotShadowSlots
        )
    finally
      ArrayPool<MeshDraw>.Shared.Return(meshDraws, false)
      ArrayPool<InstancedMeshDraw>.Shared.Return(instancedDraws, false)

    match frameState.Camera with
    | ValueSome cam ->
      uploadShadowUniforms(
        hasCasters,
        &forward,
        &instanced,
        &skinned,
        &skinnedInstanced,
        shadowAtlas,
        cam.Position,
        atlasCfg.MaxCasters
      )
    | ValueNone -> ()

    shadowResultOf
      shadowAtlas
      atlasCfg
      hasCasters
      (lights.DirLights.Count > 0 && lights.DirLights[0].CastsShadows)
      pointShadowSlots
      spotShadowSlots

  /// <summary>
  /// Multi-camera-block block start: resets the live light buffers when the block carries its
  /// own light commands (a block without any inherits them untouched), then renders this
  /// block's shadow map — from the block's final light set, shadow origin, and buffer slice —
  /// and reseats the frame bundle's shadow state from the pass.
  /// </summary>
  /// <remarks>
  /// raylib has no FBO stack: <c>EndTextureMode</c> rebinds the back buffer, so under
  /// post-processing the pass can't run inside the scene target's texture mode — the caller's
  /// texture mode is unwrapped and re-wrapped around it (boundaries flush the render batch).
  /// </remarks>
  member private this.beginShadowedBlock
    (
      gameCtx: GameContext,
      buffer: RenderBuffer3D,
      resources: inref<ShadowDepthResources>,
      sceneRT: RenderTexture2D voption,
      plan: BlockPlan,
      blockIndex: byref<int>,
      camera: Camera3D,
      frame: byref<ForwardFrame>
    ) =
    if LightScoping.resetForBlock plan defaultLights lights &blockIndex then
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true

    let block = plan.Blocks[blockIndex]
    LightScoping.loadSet block.Lights blockLights

    match sceneRT with
    | ValueSome _ -> Raylib.EndTextureMode()
    | ValueNone -> ()

    // Re-wrap the caller's texture mode even when the pass throws — raylib has no FBO
    // stack, so an unwound frame would otherwise leave the pipeline outside texture mode.
    try
      let struct (meshDraws, meshDrawCount, skinnedStart, instancedDraws,
                  instancedDrawCount) =
        collectMeshDraws(
          buffer,
          block.StartIndex,
          block.EndIndex,
          block.InitialCastEnabled
        )

      let mutable blockFrame = {
        Camera = ValueSome camera
        ShadowOrigin = block.ShadowOrigin
      }

      try
        let hasC =
          runShadowPass(
            shadowAtlas,
            atlasCfg,
            &resources,
            palettePool,
            blockLights,
            meshDraws,
            meshDrawCount,
            skinnedStart,
            instancedDraws,
            instancedDrawCount,
            &blockFrame,
            gameCtx,
            &pointShadowSlots,
            &spotShadowSlots
          )

        uploadShadowUniforms(
          hasC,
          &forward,
          &instanced,
          &skinned,
          &skinnedInstanced,
          shadowAtlas,
          camera.Position,
          atlasCfg.MaxCasters
        )

        if not hasC then
          // Caster-less block: clear the flag so shaders don't sample the previous block's atlas.
          setShaderInt forwardShader forward.Locs.DirLight.CastsShadows 0
          setShaderInt instancedShader instanced.Locs.DirLight.CastsShadows 0
          setShaderInt skinnedShader skinned.Locs.DirLight.CastsShadows 0

          setShaderInt
            skinnedInstancedShader
            skinnedInstanced.Locs.DirLight.CastsShadows
            0

        frame.PointShadowSlots <- pointShadowSlots
        frame.SpotShadowSlots <- spotShadowSlots

        frame.Shadows <-
          shadowResultOf
            shadowAtlas
            atlasCfg
            hasC
            (blockLights.DirLights.Count > 0
             && blockLights.DirLights[0].CastsShadows)
            pointShadowSlots
            spotShadowSlots
      finally
        ArrayPool<MeshDraw>.Shared.Return(meshDraws, false)
        ArrayPool<InstancedMeshDraw>.Shared.Return(instancedDraws, false)
    finally
      match sceneRT with
      | ValueSome rt -> Raylib.BeginTextureMode rt
      | ValueNone -> ()

  // ----------------------------------------------------------------
  // Per-draw shading hook — overridable.
  //
  // The default implementation routes the shaded draw kinds through the cached
  // PBR fast path, or — when a user-effect scope is open (beginEffect/endEffect) —
  // name-resolved SceneUpload to the user shader. Override Shade to plug a
  // different strategy while inheriting the gather + orchestration.
  //
  // activeEffect: ValueNone on the default path → PBR; ValueSome shader → shade
  // with the user shader (it inherits scene DATA, not the PBR shader).
  //
  // PERF: the default PBR path (activeEffect = ValueNone) is dispatched inline in
  // Execute's forward loop — it does NOT route through this virtual call, to keep
  // the hot path zero-cost. Shade is invoked for user-effect scopes (ValueSome)
  // and by subclass overrides. To intercept ALL draws (including the default path),
  // override Execute instead.
  // ----------------------------------------------------------------

  /// <summary>
  /// Per-draw shading hook for user-effect scopes (beginEffect/endEffect). Override to plug a
  /// custom shading strategy (toon, cel, wireframe) while inheriting the camera/light/shadow
  /// gather and forward-pass orchestration from
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Execute"/>.
  /// </summary>
  /// <remarks>
  /// The default PBR path is dispatched inline in <c>Execute</c> for performance and does not
  /// route through this virtual call. <c>Shade</c> is invoked for user-effect scopes
  /// (<c>activeEffect = ValueSome</c>). To intercept all draws including the default PBR path,
  /// override <c>Execute</c> instead.
  /// </remarks>
  /// <param name="frame">The frame's scene bundle (lights, shadow slots, shadow output, time).</param>
  /// <param name="activeEffect">ValueNone on the default PBR path; ValueSome shader when a user-effect scope is open.</param>
  /// <param name="currentCamera">The active camera.</param>
  /// <param name="draw">The draw command to shade.</param>
  abstract Shade:
    frame: ForwardFrame *
    activeEffect: Shader voption *
    currentCamera: byref<Camera3D> *
    draw: Command3D ->
      unit

  /// <summary>
  /// Default shading: PBR cached fast path (ValueNone) or name-resolved SceneUpload to the
  /// user shader (ValueSome). DrawMeshInstanced under a user scope is shaded by the user shader
  /// when it opts into instancing (<c>in mat4 instanceTransform;</c>); otherwise it falls back
  /// to the PBR instanced path.
  /// </summary>
  default this.Shade(frame, activeEffect, currentCamera, draw) =
    match activeEffect with
    | ValueNone ->
      // Default path: cached PBR fast path.
      match draw with
      | Command3D.DrawMesh(mesh, transform, material) ->
        handleDrawMesh(
          forwardShader,
          &forward,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          mesh,
          transform,
          material
        )
      | Command3D.DrawModel(model, transform) ->
        handleDrawModel(
          forwardShader,
          &forward,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          model,
          transform,
          ValueNone
        )
      | Command3D.DrawModelWith(model, transform, matOverride) ->
        handleDrawModel(
          forwardShader,
          &forward,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          model,
          transform,
          ValueSome matOverride
        )
      | Command3D.DrawSkinnedMesh(mesh, transform, material, bones) ->
        handleDrawSkinnedMesh(
          skinnedShader,
          &skinned,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          mesh,
          transform,
          material,
          bones
        )
      | Command3D.DrawMeshInstanced(mesh, transforms, material, instanceCount) ->
        handleDrawMeshInstanced(
          instancedShader,
          &instanced,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          mesh,
          transforms,
          material,
          instanceCount
        )
      | Command3D.DrawSkinnedMeshInstanced(mesh,
                                           transforms,
                                           palettes,
                                           material,
                                           instanceCount,
                                           boneCount) ->
        handleDrawSkinnedMeshInstanced(
          skinnedInstancedShader,
          &skinnedInstanced,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          palettePool,
          mesh,
          transforms,
          palettes,
          material,
          instanceCount,
          boneCount
        )
      | _ -> ()
    | ValueSome userShader ->
      this.shadeWithEffect(frame, userShader, &currentCamera, draw)

  /// <summary>
  /// Shades a draw with a user-supplied shader via name-resolved SceneUpload. The shader inherits
  /// scene data (camera/lights/material/bones/time), NOT the PBR shader itself. DrawMeshInstanced
  /// under a user scope is shaded by the user shader when it opts into instancing
  /// (<c>in mat4 instanceTransform;</c>); otherwise it falls back to the PBR instanced path. See
  /// docs/graphics3d/instancing.md.
  /// </summary>
  member private _.shadeWithEffect
    (
      frame: ForwardFrame,
      userShader: Shader,
      currentCamera: byref<Camera3D>,
      draw: Command3D
    ) =
    let inline normalMatrixOf(world: Matrix4x4) = computeNormalMatrix world
    let camPos = currentCamera.Position

    // Capture the view/projection from raylib's current rlgl state (set by BeginMode3D).
    let view = Rlgl.GetMatrixModelview()
    let projection = Rlgl.GetMatrixProjection()

    let inline upload world material bones =
      SceneUpload.uploadToShader(
        userShader,
        view,
        projection,
        camPos,
        world,
        normalMatrixOf world,
        frame.Lights,
        frame.Shadows,
        bones,
        material,
        frame.Time
      )

    // Lazily create the reusable user-effect material on first use, then set its shader to the
    // active user shader. Maps are populated per-draw below. Avoids per-draw LoadMaterialDefault
    // leaks (the material is owned by the pipeline and unloaded at Shutdown).
    if not userEffectMaterialCreated then
      userEffectMaterial <- Raylib.LoadMaterialDefault()
      userEffectMaterialCreated <- true

    userEffectMaterial.Shader <- userShader

    // Populate the reusable material's maps from a Material3D (textures the user shader samples).
    // The material is reused across draws, so missing maps MUST be reset to the default texture —
    // otherwise the previous draw's texture leaks into this one (gemini review #53).
    let inline populateMaps(mat3d: Material3D) =
      // raylib-cs 8.0.0 has no GetTextureDefault(); GetShapesTexture() returns the default
      // 1x1 white Texture2D raylib uses for untextured draws.
      let defaultTex = Raylib.GetShapesTexture()

      Raylib.SetMaterialTexture(
        &userEffectMaterial,
        MaterialMapIndex.Albedo,
        match mat3d.AlbedoMap with
        | ValueSome t -> t
        | ValueNone -> defaultTex
      )

      Raylib.SetMaterialTexture(
        &userEffectMaterial,
        MaterialMapIndex.Roughness,
        match mat3d.RoughnessMap with
        | ValueSome t -> t
        | ValueNone -> defaultTex
      )

      Raylib.SetMaterialTexture(
        &userEffectMaterial,
        MaterialMapIndex.Metalness,
        match mat3d.MetallicMap with
        | ValueSome t -> t
        | ValueNone -> defaultTex
      )

      Raylib.SetMaterialTexture(
        &userEffectMaterial,
        MaterialMapIndex.Normal,
        match mat3d.NormalMap with
        | ValueSome t -> t
        | ValueNone -> defaultTex
      )

      Raylib.SetMaterialTexture(
        &userEffectMaterial,
        MaterialMapIndex.Emission,
        match mat3d.EmissionMap with
        | ValueSome t -> t
        | ValueNone -> defaultTex
      )

    Raylib.BeginShaderMode userShader

    match draw with
    | Command3D.DrawMesh(mesh, transform, material) ->
      upload transform material ValueNone
      populateMaps material
      Raylib.DrawMesh(mesh, userEffectMaterial, transform)

    | Command3D.DrawModel(model, transform) ->
      for mi = 0 to model.MeshCount - 1 do
        let mesh = NativePtr.get model.Meshes mi
        let matIdx = NativePtr.get model.MeshMaterial mi
        let raylibMat = NativePtr.get model.Materials matIdx
        let mat3d = Material3D.fromRaylibMaterial raylibMat
        upload transform mat3d ValueNone
        populateMaps mat3d
        Raylib.DrawMesh(mesh, userEffectMaterial, transform)

    | Command3D.DrawModelWith(model, transform, matOverride) ->
      for mi = 0 to model.MeshCount - 1 do
        let mesh = NativePtr.get model.Meshes mi

        let mat3d =
          match matOverride with
          | MaterialOverride.All m -> m
          | MaterialOverride.PerMesh f -> f mi

        upload transform mat3d ValueNone
        populateMaps mat3d
        Raylib.DrawMesh(mesh, userEffectMaterial, transform)

    | Command3D.DrawSkinnedMesh(mesh, transform, material, bones) ->
      upload transform material (ValueSome bones)
      populateMaps material
      Raylib.DrawMesh(mesh, userEffectMaterial, transform)

    | Command3D.DrawMeshInstanced(mesh, transforms, material, instanceCount) ->
      // Resolve (and memoize) the shader's `instanceTransform` attribute — the raylib opt-in for
      // instancing under a user scope. A shader that declares it shades its own instances; one
      // that doesn't falls back to the PBR instanced path.
      let attrLoc =
        match instanceAttrLocs.TryGetValue userShader with
        | true, loc -> loc
        | false, _ ->
          let loc =
            Raylib.GetShaderLocationAttrib(userShader, "instanceTransform")

          instanceAttrLocs[userShader] <- loc
          loc

      if attrLoc >= 0 then
        // Opt-in: the shader declares `in mat4 instanceTransform`, so raylib 6.0 auto-resolves
        // the dedicated SHADER_LOC_VERTEX_INSTANCETRANSFORM slot at load and DrawMeshInstanced
        // binds the per-instance VBO through it — no Locs wiring needed. matModel is identity
        // (the per-instance transform IS the model matrix); viewProj is view-projection only.
        upload Matrix4x4.Identity material ValueNone
        populateMaps material

        Raylib.DrawMeshInstanced(
          mesh,
          userEffectMaterial,
          transforms,
          instanceCount
        )
      else
        // No opt-in — fall back to the PBR instanced path (see remarks).
        Raylib.EndShaderMode()

        handleDrawMeshInstanced(
          instancedShader,
          &instanced,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          mesh,
          transforms,
          material,
          instanceCount
        )

        Raylib.BeginShaderMode userShader

    | Command3D.DrawSkinnedMeshInstanced(mesh,
                                         transforms,
                                         palettes,
                                         material,
                                         instanceCount,
                                         boneCount) ->
      // Opt-in probe (memoized): the user shader shades its own skinned-instanced
      // draws when it declares `in mat4 instanceTransform`, the bone attributes,
      // and a `bonePalette` sampler (bonePaletteSize is optional). Otherwise the
      // draws fall back to the built-in skinned-instanced variant.
      let struct (paletteLoc, paletteSizeLoc) =
        match skinnedInstancedLocs.TryGetValue userShader with
        | true, locs -> locs
        | false, _ ->
          let paletteLoc =
            if
              Raylib.GetShaderLocationAttrib(userShader, "instanceTransform")
              >= 0
              && Raylib.GetShaderLocationAttrib(userShader, "vertexBoneIndices")
                 >= 0
              && Raylib.GetShaderLocationAttrib(userShader, "vertexBoneWeights")
                 >= 0
            then
              Raylib.GetShaderLocation(userShader, "bonePalette")
            else
              -1

          let locs =
            struct (paletteLoc,
                    Raylib.GetShaderLocation(userShader, "bonePaletteSize"))

          skinnedInstancedLocs[userShader] <- locs
          locs

      if paletteLoc >= 0 then
        // Opt-in: matModel is identity (the per-instance transform IS the model
        // matrix); viewProj is view-projection only. The bone palette reaches the
        // shader through the bonePalette texture, like the built-in variant.
        upload Matrix4x4.Identity material ValueNone
        populateMaps material

        drawSkinnedInstancedChunks(
          palettePool,
          userShader,
          paletteLoc,
          paletteSizeLoc,
          mesh,
          userEffectMaterial,
          transforms,
          palettes,
          instanceCount,
          boneCount
        )
      else
        // No opt-in — fall back to the built-in skinned-instanced variant.
        Raylib.EndShaderMode()

        handleDrawSkinnedMeshInstanced(
          skinnedInstancedShader,
          &skinnedInstanced,
          frame.Lights,
          maxPt,
          maxSp,
          frame.PointShadowSlots,
          frame.SpotShadowSlots,
          currentCamera,
          palettePool,
          mesh,
          transforms,
          palettes,
          material,
          instanceCount,
          boneCount
        )

        Raylib.BeginShaderMode userShader

    | _ -> ()

    Raylib.EndShaderMode()

  // ── IRenderPipeline3D ────────────────────────────────────────

  interface IRenderPipeline3D with
    member _.Initialize() =
      forwardShader <- Shaders.loadForwardShader maxPt maxSp atlasCfg.MaxCasters

      instancedShader <-
        Shaders.loadForwardInstancedShader maxPt maxSp atlasCfg.MaxCasters

      skinnedShader <-
        Shaders.loadForwardSkinnedShader maxPt maxSp atlasCfg.MaxCasters

      skinnedInstancedShader <-
        Shaders.loadForwardSkinnedInstancedShader
          maxPt
          maxSp
          atlasCfg.MaxCasters

      // No Locs wiring needed: forwardVertexInstanced declares `in mat4 instanceTransform`,
      // so raylib 6.0 auto-resolves SHADER_LOC_VERTEX_INSTANCETRANSFORM at load and
      // DrawMeshInstanced binds the per-instance VBO through it.

      depthShadowShader <- Shaders.loadDepthShadowShader()
      depthShadowSkinnedShader <- Shaders.loadDepthShadowSkinnedShader()
      depthShadowInstancedShader <- Shaders.loadDepthShadowInstancedShader()

      depthShadowSkinnedInstancedShader <-
        Shaders.loadDepthShadowSkinnedInstancedShader()

      depthShadowMaterial <- Raylib.LoadMaterialDefault()
      depthShadowMaterial.Shader <- depthShadowShader

      depthShadowSkinnedMaterial <- Raylib.LoadMaterialDefault()
      depthShadowSkinnedMaterial.Shader <- depthShadowSkinnedShader

      depthShadowInstancedMaterial <- Raylib.LoadMaterialDefault()
      depthShadowInstancedMaterial.Shader <- depthShadowInstancedShader

      depthShadowSkinnedInstancedMaterial <- Raylib.LoadMaterialDefault()

      depthShadowSkinnedInstancedMaterial.Shader <-
        depthShadowSkinnedInstancedShader

      shadowNormalMatrixLoc <-
        Raylib.GetShaderLocation(depthShadowShader, "normalMatrix")

      shadowSkinnedNormalMatrixLoc <-
        Raylib.GetShaderLocation(depthShadowSkinnedShader, "normalMatrix")

      shadowBoneLoc <-
        Raylib.GetShaderLocation(depthShadowSkinnedShader, "boneMatrices[0]")

      shadowBonePaletteLoc <-
        Raylib.GetShaderLocation(
          depthShadowSkinnedInstancedShader,
          "bonePalette"
        )

      shadowBonePaletteSizeLoc <-
        Raylib.GetShaderLocation(
          depthShadowSkinnedInstancedShader,
          "bonePaletteSize"
        )

      shadowAtlas <- ShadowAtlas(atlasCfg, biasCfg)
      shadowAtlas.Initialize()

      let fwdLocs =
        cacheLocations(forwardShader, maxPt, maxSp, atlasCfg.MaxCasters)

      let instLocs =
        cacheLocations(instancedShader, maxPt, maxSp, atlasCfg.MaxCasters)

      let skLocs =
        cacheLocations(skinnedShader, maxPt, maxSp, atlasCfg.MaxCasters)

      let skInstLocs =
        cacheLocations(
          skinnedInstancedShader,
          maxPt,
          maxSp,
          atlasCfg.MaxCasters
        )

      forward <- ShaderVariant(fwdLocs, MaterialCache 16)
      instanced <- ShaderVariant(instLocs, MaterialCache 16)
      skinned <- ShaderVariant(skLocs, MaterialCache 16)
      skinnedInstanced <- ShaderVariant(skInstLocs, MaterialCache 16)

    member _.Shutdown() =
      // raylib 6.0 changed UnloadMaterial to destroy the material's shader AND
      // every map texture (not just the maps array). The cached/depth/user
      // materials here share the pipeline shaders (unloaded explicitly below)
      // and their map textures are owned by AssetsService — so UnloadMaterial
      // would double-free the shader and free textures it doesn't own. Free
      // only the maps array (allocated by LoadMaterialDefault) via MemFree.
      let freeMaps(mat: Material) =
        Raylib.MemFree(NativePtr.toVoidPtr mat.Maps)

      for KeyValue(_, mat) in instanced.MaterialCache.cache do
        freeMaps mat

      instanced.MaterialCache.cache.Clear()

      for KeyValue(_, mat) in skinned.MaterialCache.cache do
        freeMaps mat

      skinned.MaterialCache.cache.Clear()

      for KeyValue(_, mat) in skinnedInstanced.MaterialCache.cache do
        freeMaps mat

      skinnedInstanced.MaterialCache.cache.Clear()

      Raylib.UnloadShader forwardShader
      Raylib.UnloadShader instancedShader
      Raylib.UnloadShader skinnedShader
      Raylib.UnloadShader skinnedInstancedShader
      Raylib.UnloadShader depthShadowShader
      Raylib.UnloadShader depthShadowSkinnedShader
      Raylib.UnloadShader depthShadowInstancedShader
      Raylib.UnloadShader depthShadowSkinnedInstancedShader

      freeMaps depthShadowMaterial
      freeMaps depthShadowSkinnedMaterial
      freeMaps depthShadowInstancedMaterial
      freeMaps depthShadowSkinnedInstancedMaterial

      palettePool.UnloadAll()

      if userEffectMaterialCreated then
        freeMaps userEffectMaterial

      for KeyValue(_, mat) in forward.MaterialCache.cache do
        freeMaps mat

      forward.MaterialCache.cache.Clear()

      if shadowAtlas <> Unchecked.defaultof<ShadowAtlas> then
        shadowAtlas.Shutdown()

    member this.Execute(gameCtx, gameTime, buffer, rtPool) =
      try
        this.executeCore(gameCtx, gameTime, buffer, rtPool)
      finally
        // Return this frame's palette textures to the pool even when a draw or a
        // user callback throws — a skipped release would leak the in-use textures
        // and leave stale per-frame upload memos keyed by dead array references.
        palettePool.ReleaseAll()

  // Frame dispatch body; the IRenderPipeline3D.Execute wrapper above
  // guarantees the palette-pool release on success and on exception.
  member private this.executeCore
    (
      gameCtx: GameContext,
      gameTime: GameTime,
      buffer: RenderBuffer3D,
      rtPool: IRenderTargetPool3D
    ) =
    let frameTime = float32 gameTime.TotalTime.TotalSeconds

    // Pre-scan: gather camera, shadow origin, warm material caches, and — when present —
    // post-process actions in a single pass over the buffer. Lights are gathered here only
    // for single-camera frames; multi-block frames scope them per camera block in the
    // forward pass instead.
    clearLights lights

    // The block plan walks the buffer once for the per-camera-block light/shadow scoping.
    // Single-camera frames skip the walk (and its allocations) entirely — the counter is
    // maintained by the buffer on Add.
    let multiBlock = buffer.CameraBlockCount > 1

    let plan =
      if multiBlock then
        BlockPlan.build buffer
      else
        BlockPlan.empty

    // Allocated only when the view emits at least one post-process command, so frames with none
    // skip the allocation and the per-command scan entirely.
    let ppActions: ResizeArray<PostProcessContext3D -> unit> voption =
      if buffer.PostProcessCount > 0 then
        ValueSome(ResizeArray(buffer.PostProcessCount))
      else
        ValueNone

    let frameState =
      preScan(
        buffer,
        lights,
        not multiBlock,
        &forward,
        &instanced,
        &skinned,
        &skinnedInstanced,
        forwardShader,
        instancedShader,
        skinnedShader,
        skinnedInstancedShader,
        ppActions
      )

    forward.LightsDirty <- true
    instanced.LightsDirty <- true
    skinned.LightsDirty <- true
    skinnedInstanced.LightsDirty <- true

    let shadowResources = {
      Shader = depthShadowShader
      SkinnedShader = depthShadowSkinnedShader
      InstancedShader = depthShadowInstancedShader
      SkinnedInstancedShader = depthShadowSkinnedInstancedShader
      Material = depthShadowMaterial
      SkinnedMaterial = depthShadowSkinnedMaterial
      InstancedMaterial = depthShadowInstancedMaterial
      SkinnedInstancedMaterial = depthShadowSkinnedInstancedMaterial
      NormalMatrixLoc = shadowNormalMatrixLoc
      SkinnedNormalMatrixLoc = shadowSkinnedNormalMatrixLoc
      BoneLoc = shadowBoneLoc
      BonePaletteLoc = shadowBonePaletteLoc
      BonePaletteSizeLoc = shadowBonePaletteSizeLoc
    }

    // Shadow pass: single-camera frames run one pass up front (frame-global gather, first
    // camera). Multi-block frames run one pass per camera block at its BeginCamera in the
    // forward loop instead.
    let shadowResult: ShadowResult voption =
      if multiBlock then
        ValueNone
      else
        this.runFrameShadowPass(gameCtx, buffer, &shadowResources, &frameState)

    let mutable frame: ForwardFrame = {
      Lights = lights
      PointShadowSlots = pointShadowSlots
      SpotShadowSlots = spotShadowSlots
      Shadows = shadowResult
      Time = frameTime
    }

    // Clear lights; the forward pass re-adds them per camera block
    clearLights lights
    forward.LightsDirty <- true
    instanced.LightsDirty <- true
    skinned.LightsDirty <- true
    skinnedInstanced.LightsDirty <- true

    // Multi-camera-block frames: start the persistent defaults empty — the forward pass
    // builds them in-order (between-block commands accumulate; each block resets to the
    // defaults-so-far or inherits the running set at its BeginCamera), so live shading
    // matches the block plan by construction.
    if multiBlock then
      LightBuffers.clear defaultLights

    // Forward pass — dispatch all commands
    let mutable cameraActive = false
    let mutable currentCamera = Unchecked.defaultof<Camera3D>
    let mutable shaderActive = false
    // Running camera-block index into the block plan; advanced at each
    // BeginCamera/BeginCameraConfig below (multi-block frames only).
    let mutable blockIndex = -1
    // Per-group shading scope (beginEffect/endEffect). ValueNone → default PBR path;
    // ValueSome shader → shade with the user shader. Reset on camera boundaries.
    let mutable activeEffect: Shader voption = ValueNone

    let dispatchForwardPass(sceneRT: RenderTexture2D voption) =
      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        // ── Camera management (inline — simple state toggles) ──
        | Command3D.BeginCamera cam ->
          if cameraActive then
            if shaderActive then
              Raylib.EndShaderMode()
              shaderActive <- false

            Raylib.EndMode3D()

          // Multi-block frames: reset-or-inherit the lights, then render this block's
          // shadow map (outside the camera and scene-RT scopes) before BeginMode3D.
          if multiBlock then
            this.beginShadowedBlock(
              gameCtx,
              buffer,
              &shadowResources,
              sceneRT,
              plan,
              &blockIndex,
              cam,
              &frame
            )

          Raylib.BeginMode3D cam
          cameraActive <- true
          currentCamera <- cam
          // New camera block: scopes don't persist across cameras.
          activeEffect <- ValueNone

        | Command3D.BeginCameraConfig cfg ->
          if cameraActive then
            if shaderActive then
              Raylib.EndShaderMode()
              shaderActive <- false

            Raylib.EndMode3D()

          // Multi-block frames: reset-or-inherit the lights, then render this block's
          // shadow map (outside the camera and scene-RT scopes) before BeginMode3D.
          if multiBlock then
            this.beginShadowedBlock(
              gameCtx,
              buffer,
              &shadowResources,
              sceneRT,
              plan,
              &blockIndex,
              cfg.Camera,
              &frame
            )

          applyCameraConfig(&cfg, gameCtx)
          Raylib.BeginMode3D cfg.Camera
          cameraActive <- true
          currentCamera <- cfg.Camera
          // New camera block: scopes don't persist across cameras.
          activeEffect <- ValueNone

        | Command3D.EndCamera ->
          if cameraActive then
            if shaderActive then
              Raylib.EndShaderMode()
              shaderActive <- false

            Raylib.EndMode3D()
            cameraActive <- false

          Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)
          // EndCamera closes any open effect scope.
          activeEffect <- ValueNone

        // ── Per-group shading scope ──
        | Command3D.BeginEffect shader -> activeEffect <- ValueSome shader
        | Command3D.EndEffect -> activeEffect <- ValueNone

        // ── Drawing commands ──
        // The default PBR path (activeEffect = ValueNone) calls the inline handlers directly
        // to keep the hot path inlined (a virtual Shade call per draw regresses FPS). The
        // user-effect scope (ValueSome) and any Shade override route through this.Shade.
        | Command3D.DrawMesh _
        | Command3D.DrawModel _
        | Command3D.DrawModelWith _
        | Command3D.DrawSkinnedMesh _
        | Command3D.DrawMeshInstanced _
        | Command3D.DrawSkinnedMeshInstanced _ ->
          if cameraActive then
            match activeEffect with
            | ValueNone ->
              // Default path: inline PBR fast path (hot path — no virtual call).
              match buffer[i] with
              | Command3D.DrawMesh(mesh, transform, material) ->
                handleDrawMesh(
                  forwardShader,
                  &forward,
                  lights,
                  maxPt,
                  maxSp,
                  pointShadowSlots,
                  spotShadowSlots,
                  currentCamera,
                  mesh,
                  transform,
                  material
                )
              | Command3D.DrawModel(model, transform) ->
                handleDrawModel(
                  forwardShader,
                  &forward,
                  lights,
                  maxPt,
                  maxSp,
                  pointShadowSlots,
                  spotShadowSlots,
                  currentCamera,
                  model,
                  transform,
                  ValueNone
                )
              | Command3D.DrawModelWith(model, transform, matOverride) ->
                handleDrawModel(
                  forwardShader,
                  &forward,
                  lights,
                  maxPt,
                  maxSp,
                  pointShadowSlots,
                  spotShadowSlots,
                  currentCamera,
                  model,
                  transform,
                  ValueSome matOverride
                )
              | Command3D.DrawSkinnedMesh(mesh, transform, material, bones) ->
                handleDrawSkinnedMesh(
                  skinnedShader,
                  &skinned,
                  lights,
                  maxPt,
                  maxSp,
                  pointShadowSlots,
                  spotShadowSlots,
                  currentCamera,
                  mesh,
                  transform,
                  material,
                  bones
                )
              | Command3D.DrawMeshInstanced(mesh,
                                            transforms,
                                            material,
                                            instanceCount) ->
                handleDrawMeshInstanced(
                  instancedShader,
                  &instanced,
                  lights,
                  maxPt,
                  maxSp,
                  pointShadowSlots,
                  spotShadowSlots,
                  currentCamera,
                  mesh,
                  transforms,
                  material,
                  instanceCount
                )
              | Command3D.DrawSkinnedMeshInstanced(mesh,
                                                   transforms,
                                                   palettes,
                                                   material,
                                                   instanceCount,
                                                   boneCount) ->
                handleDrawSkinnedMeshInstanced(
                  skinnedInstancedShader,
                  &skinnedInstanced,
                  lights,
                  maxPt,
                  maxSp,
                  pointShadowSlots,
                  spotShadowSlots,
                  currentCamera,
                  palettePool,
                  mesh,
                  transforms,
                  palettes,
                  material,
                  instanceCount,
                  boneCount
                )
              | _ -> ()
            | ValueSome _ ->
              this.Shade(frame, activeEffect, &currentCamera, buffer[i])

        | Command3D.DrawBillboard bb ->
          if cameraActive then
            handleDrawBillboard(currentCamera, bb)

        | Command3D.DrawBillboardBatch batch ->
          if cameraActive then
            handleDrawBillboardBatch(currentCamera, batch)

        | Command3D.DrawLine3D(start, finish, color) ->
          if cameraActive then
            Raylib.DrawLine3D(start, finish, color)

        // ── Light commands (delegated) ──
        | Command3D.SetAmbientLight _
        | Command3D.AddDirectionalLight _
        | Command3D.AddPointLight _
        | Command3D.AddSpotLight _ as cmd ->
          handleLightCommand(
            lights,
            cmd,
            &forward,
            &instanced,
            &skinned,
            &skinnedInstanced
          )

          // Between-block commands also update the frame defaults, so a later block
          // that resets sees them.
          if multiBlock && not cameraActive then
            LightScoping.apply defaultLights cmd

        // ── Immediate mode: hand the callback the gathered scene data ──
        | Command3D.DrawImmediate action ->
          let savedCam = cameraActive
          let savedShader = shaderActive

          // Capture the view/projection from raylib's current rlgl state before exiting the
          // camera scope (AGENTS.md "VP Matrix Capture" — must read inside BeginMode3D).
          let view = Rlgl.GetMatrixModelview()
          let projection = Rlgl.GetMatrixProjection()

          if shaderActive then
            Raylib.EndShaderMode()
            shaderActive <- false

          if cameraActive then
            Raylib.EndMode3D()
            cameraActive <- false

          let ctx: SceneContext = {
            Camera = currentCamera
            View = view
            Projection = projection
            Lights = lights
            Shadows = frame.Shadows
            Time = frame.Time
          }

          try
            action ctx
          finally
            if savedCam then
              Raylib.BeginMode3D currentCamera
              cameraActive <- true

            if savedShader then
              Raylib.BeginShaderMode forwardShader
              shaderActive <- true

        // ── State toggles (inline — no-ops) ──
        | Command3D.SetShadowOrigin _ -> ()
        | Command3D.EnableShadows -> ()
        | Command3D.DisableShadows -> ()
        // Post-process actions are collected above and run after the scene renders to
        // an offscreen target; nothing to do during the forward pass.
        | Command3D.PostProcess _
        | Command3D.PostProcessWithDepth _ -> ()

      // End remaining shader/camera state after dispatch
      if shaderActive then
        Raylib.EndShaderMode()

      if cameraActive then
        Raylib.EndMode3D()

    // Render the forward pass direct, or via a scene RT when post-process commands are present.
    // When depth-needing actions exist (DepthPostProcessCount > 0), expose the scene RT's depth
    // attachment to the post-process context — OpenGL's depth buffer is directly sampleable, so
    // no separate geometry pre-pass is needed (unlike the MonoGame backend).
    match ppActions with
    | ValueNone -> dispatchForwardPass ValueNone
    | ValueSome actions ->
      // Use a depth-sampleable RT (custom FBO with a depth texture) when post-process effects
      // need to sample depth; otherwise a standard raylib RT (depth renderbuffer, cheaper).
      let sceneRT =
        if buffer.DepthPostProcessCount > 0 then
          rtPool.AcquireWithDepth(gameCtx.WindowWidth, gameCtx.WindowHeight)
        else
          rtPool.Acquire(gameCtx.WindowWidth, gameCtx.WindowHeight)

      Raylib.BeginTextureMode sceneRT
      Raylib.ClearBackground Color.Black
      dispatchForwardPass(ValueSome sceneRT)
      Raylib.EndTextureMode()

      let depth: Texture2D voption =
        if buffer.DepthPostProcessCount > 0 then
          ValueSome sceneRT.Depth
        else
          ValueNone

      applyPostProcess gameCtx sceneRT rtPool actions depth frameTime

// ------------------------------------------------------------------
// ForwardPbrPipeline — the default PBR subclass (thin).
//
// Inherits the gather + shadow pass + forward-pass orchestration from
// ForwardPipelineBase unchanged, using the base's default Cook-Torrance PBR
// Shade. Register the same way as before:
//   Renderer3D.create (ForwardPbrPipeline()) view
// To plug a different shading strategy (toon, cel, custom), build an object
// expression over ForwardPipelineBase and override Shade.
// ------------------------------------------------------------------

/// <summary>
/// The default raylib 3D forward PBR pipeline: a thin <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase"/>
/// that inherits the camera/light/shadow gather and forward-pass orchestration unchanged, using
/// the base's default Cook-Torrance PBR <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Shade"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered via:
/// <code lang="fsharp">
/// Renderer3D.create (ForwardPbrPipeline()) view
/// </code>
/// </para>
/// <para>
/// To plug a different shading strategy (toon, cel, custom), build an object expression over
/// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase"/> and override <c>Shade</c> —
/// the scene gather, shadow pass, and forward-pass dispatch are inherited:
/// <code lang="fsharp">
/// let toon =
///   { new ForwardPipelineBase() with
///       override _.Shade(frame, activeEffect, &amp;currentCamera, draw) = ... }
/// </code>
/// </para>
/// </remarks>
type ForwardPbrPipeline
  (
    ?maxPointLights: int,
    ?maxSpotLights: int,
    ?shadowAtlasConfig: ShadowAtlasConfig,
    ?shadowBiasConfig: ShadowBiasConfig
  ) =
  inherit
    ForwardPipelineBase(
      ?maxPointLights = maxPointLights,
      ?maxSpotLights = maxSpotLights,
      ?shadowAtlasConfig = shadowAtlasConfig,
      ?shadowBiasConfig = shadowBiasConfig
    )
