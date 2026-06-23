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
  Material: Material
  SkinnedMaterial: Material
  NormalMatrixLoc: int
  SkinnedNormalMatrixLoc: int
  BoneLoc: int
}

// ------------------------------------------------------------------
// Light Buffers (reference type)
// ------------------------------------------------------------------

type internal LightBuffers = {
  Ambient: ResizeArray<AmbientLight3D>
  DirLights: ResizeArray<DirectionalLight3D>
  PointLights: ResizeArray<PointLight3D>
  SpotLights: ResizeArray<SpotLight3D>
}

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

  let collectMeshDraws(buffer: RenderBuffer3D) =
    let pool = ArrayPool<MeshDraw>.Shared

    let mutable meshCount = 0
    let mutable shadowsEnabled = true
    let mutable i = 0

    while i < buffer.Count do
      match buffer[i] with
      | Command3D.DisableShadows -> shadowsEnabled <- false
      | Command3D.EnableShadows -> shadowsEnabled <- true
      | Command3D.DrawMesh _ when shadowsEnabled -> meshCount <- meshCount + 1
      | Command3D.DrawSkinnedMesh _ when shadowsEnabled ->
        meshCount <- meshCount + 1
      | Command3D.DrawModel(model, _) when shadowsEnabled ->
        meshCount <- meshCount + model.MeshCount
      | Command3D.DrawMeshInstanced(_, _, _, instanceCount) when shadowsEnabled ->
        meshCount <- meshCount + instanceCount
      | _ -> ()

      i <- i + 1

    let arr = pool.Rent(max meshCount 1)
    let mutable count = 0
    let mutable skinnedStart = count
    shadowsEnabled <- true
    i <- 0

    while i < buffer.Count do
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
      | Command3D.DrawMeshInstanced(mesh, transforms, _, instanceCount) when
        shadowsEnabled
        ->
        for ti = 0 to instanceCount - 1 do
          arr[count] <- {
            Mesh = mesh
            Transform = transforms[ti]
            Bones = ValueNone
          }

          count <- count + 1
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

    struct (arr, count, skinnedStart)

  /// Register all shadow-casting lights with the atlas. Returns true if any casters found.
  /// <summary>
  /// Register shadow casters for every shadow-casting light. Returns:
  ///  - <c>hasCasters</c>: true if any caster was registered.
  ///  - <c>pointShadowSlots</c>: array indexed by <c>lights.PointLights</c> buffer position;
  ///    value is the caster's flat shader-array index, or -1 if the light doesn't cast / atlas full.
  ///  - <c>spotShadowSlots</c>: same shape for spot lights.
  ///
  /// The flat index equals the order in which casters are registered (dir first, then point,
  /// then spot), which matches <c>ShadowAtlas.PrepareUniforms</c>'s flattening order — so the
  /// value uploaded to <c>pointLightShadowIdx[i]</c> indexes <c>shadowViewProjs[idx]</c> correctly.
  /// </summary>
  let collectShadowCasters(lights: LightBuffers, atlas: ShadowAtlas) =
    let mutable hasCasters = false
    let mutable casterSlot = 0
    let pointShadowSlots = Array.create<int> lights.PointLights.Count -1
    let spotShadowSlots = Array.create<int> lights.SpotLights.Count -1

    let tryAdd casterType pos dir target bias =
      match atlas.AddCaster(casterType, pos, dir, target, true, bias) with
      | ValueSome _ ->
        hasCasters <- true
        let slot = casterSlot
        casterSlot <- casterSlot + 1
        ValueSome slot
      | ValueNone -> ValueNone

    for dir in lights.DirLights do
      if dir.CastsShadows then
        tryAdd
          ShadowCasterType.Directional
          Vector3.Zero
          dir.Direction
          Vector3.Zero
          ValueNone
        |> ignore

    lights.PointLights
    |> Seq.iteri(fun i pt ->
      if pt.CastsShadows then
        match
          tryAdd
            ShadowCasterType.Point
            pt.Position
            Vector3.Zero
            Vector3.Zero
            pt.ShadowBias
        with
        | ValueSome slot -> pointShadowSlots[i] <- slot
        | ValueNone -> ())

    lights.SpotLights
    |> Seq.iteri(fun i sp ->
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
        | ValueNone -> ())

    struct (hasCasters, pointShadowSlots, spotShadowSlots)

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
    let shadowFar = lightDistance + orthoSize * 2.0f

    Rlgl.SetClipPlanes(float shadowNear, float shadowFar)

    Camera3D(
      Position = lightPos,
      Target = shadowOrigin,
      Up = safeUp,
      FovY = orthoSize,
      Projection = CameraProjection.Orthographic
    )

// ------------------------------------------------------------------
// Pure / near-pure functions
// ------------------------------------------------------------------

[<AutoOpen>]
module internal PipelineFunctions =

  /// Create an empty LightBuffers instance.
  let createLightBuffers(maxPt: int, maxSp: int) : LightBuffers = {
    Ambient = ResizeArray<AmbientLight3D> 1
    DirLights = ResizeArray<DirectionalLight3D> 1
    PointLights = ResizeArray<PointLight3D> maxPt
    SpotLights = ResizeArray<SpotLight3D> maxSp
  }

  let inline colorToVec3(c: Color) =
    Vector3(float32 c.R / 255.0f, float32 c.G / 255.0f, float32 c.B / 255.0f)

  let inline colorToVec4(c: Color) =
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

    match lights.Ambient.Count with
    | 0 ->
      setShaderVec3 shader locs.Ambient.Color Vector3.Zero
      setShaderFloat shader locs.Ambient.Intensity 0.0f
    | _ ->
      let a = lights.Ambient[0]
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
    (
      shader: Shader,
      matLocs: inref<MaterialUniforms>,
      mat3d: inref<Material3D>,
      nm: Matrix4x4
    ) =
    setShaderVec4 shader matLocs.AlbedoColor (colorToVec4 mat3d.AlbedoColor)
    setShaderFloat shader matLocs.Roughness mat3d.Roughness
    setShaderFloat shader matLocs.Metallic mat3d.Metallic
    setShaderVec4 shader matLocs.EmissionColor (colorToVec4 mat3d.EmissionColor)
    setShaderFloat shader matLocs.Opacity mat3d.Opacity
    setShaderVec2 shader matLocs.Tiling mat3d.Tiling

    let useNormal =
      match mat3d.NormalMap with
      | ValueSome _ -> 1
      | ValueNone -> 0

    setShaderInt shader matLocs.UseNormalMap useNormal
    Raylib.SetShaderValueMatrix(shader, matLocs.NormalMatrix, nm)

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

  /// Upload shadow atlas uniforms to all three shader variants.
  let uploadShadowUniforms
    (
      hasCasters: bool,
      forward: inref<ShaderVariant>,
      instanced: inref<ShaderVariant>,
      skinned: inref<ShaderVariant>,
      atlas: ShadowAtlas,
      cameraPos: Vector3,
      maxCasters: int
    ) =
    if hasCasters then
      atlas.PrepareUniforms()
      let fwd = forward.Locs
      let inst = instanced.Locs
      let sk = skinned.Locs

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

  /// Upload bone matrices to skinned shader — uses ReadOnlySpan for no-copy.
  let inline uploadBoneMatrices
    (shader: Shader, boneLoc: int, bones: ReadOnlySpan<Matrix4x4>)
    =
    let count = min bones.Length 128

    for i = 0 to count - 1 do
      Raylib.SetShaderValueMatrix(shader, boneLoc + i, bones[i])

  /// Clear all light buffers.
  let inline clearLights(lights: LightBuffers) =
    lights.Ambient.Clear()
    lights.DirLights.Clear()
    lights.PointLights.Clear()
    lights.SpotLights.Clear()

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

    let nm = computeNormalMatrix transform
    let key = MaterialKey.fromMaterial3D &material

    if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
      setMaterialUniforms(shader, &variant.Locs.Material, &material, nm)
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
      model: Model,
      transform: Matrix4x4
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

    let nm = computeNormalMatrix transform

    for mi = 0 to model.MeshCount - 1 do
      let mesh = NativePtr.get model.Meshes mi
      let matIdx = NativePtr.get model.MeshMaterial mi
      let raylibMat = NativePtr.get model.Materials matIdx
      let mat3d = Material3D.fromRaylibMaterial raylibMat
      let key = MaterialKey.fromMaterial3D &mat3d

      if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
        setMaterialUniforms(shader, &variant.Locs.Material, &mat3d, nm)
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
    let key = MaterialKey.fromMaterial3D &material

    if not variant.HasLastMaterial || key <> variant.LastMaterialKey then
      setMaterialUniforms(shader, &variant.Locs.Material, &material, nm)
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
      setMaterialUniforms(
        shader,
        &variant.Locs.Material,
        &material,
        Matrix4x4.Identity
      )

      variant.LastMaterialKey <- key
      variant.HasLastMaterial <- true

    let mat = getOrCreate(&variant, shader, &material, &key)
    Raylib.DrawMeshInstanced(mesh, mat, transforms, instanceCount)
    Raylib.EndShaderMode()

  /// Handle billboard draw using default shader.
  let inline handleDrawBillboard
    (
      currentCamera: Camera3D,
      texture: Texture2D,
      position: Vector3,
      size: Vector2,
      color: Color
    ) =
    Rlgl.EnableShader(Rlgl.GetShaderIdDefault())

    let source =
      Rectangle(0.0f, 0.0f, float32 texture.Width, float32 texture.Height)

    Raylib.DrawBillboardRec(
      currentCamera,
      texture,
      source,
      position,
      size,
      color
    )

  /// Handle billboard batch draw using default shader.
  let inline handleDrawBillboardBatch
    (
      currentCamera: Camera3D,
      textures: Texture2D[],
      positions: Vector3[],
      sizes: Vector2[],
      colors: Color[],
      count: int
    ) =
    Rlgl.EnableShader(Rlgl.GetShaderIdDefault())

    for bi = 0 to count - 1 do
      let source =
        Rectangle(
          0.0f,
          0.0f,
          float32 textures[bi].Width,
          float32 textures[bi].Height
        )

      Raylib.DrawBillboardRec(
        currentCamera,
        textures[bi],
        source,
        positions[bi],
        sizes[bi],
        colors[bi]
      )

  /// Handle light command: add or set light, mark dirty.
  let inline handleLightCommand
    (
      lights: LightBuffers,
      command: Command3D,
      forward: byref<ShaderVariant>,
      instanced: byref<ShaderVariant>,
      skinned: byref<ShaderVariant>
    ) =
    match command with
    | Command3D.SetAmbientLight l ->
      lights.Ambient.Clear()
      lights.Ambient.Add l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
    | Command3D.AddDirectionalLight l ->
      lights.DirLights.Add l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
    | Command3D.AddPointLight l ->
      lights.PointLights.Add l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
    | Command3D.AddSpotLight l ->
      lights.SpotLights.Add l
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true
    | _ -> ()

  /// Pre-scan buffer: collect camera, lights, shadow origin, and warm material caches.
  /// Returns the frame state for shadow pass.
  let preScan
    (
      buffer: RenderBuffer3D,
      lights: LightBuffers,
      forward: byref<ShaderVariant>,
      instanced: byref<ShaderVariant>,
      skinned: byref<ShaderVariant>,
      forwardShader: Shader,
      instancedShader: Shader,
      skinnedShader: Shader
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
        lights.Ambient.Clear()
        lights.Ambient.Add l
      | Command3D.AddDirectionalLight l -> lights.DirLights.Add l
      | Command3D.AddPointLight l -> lights.PointLights.Add l
      | Command3D.AddSpotLight l -> lights.SpotLights.Add l
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
      | _ -> ()

    frameState

  /// Render all mesh draws into a single shadow atlas region.
  /// Draws are partitioned: [0..skinnedStart) are non-skinned, [skinnedStart..meshDrawCount) are skinned.
  let renderShadowRegion
    (
      shadowAtlas: ShadowAtlas,
      regionIndex: int,
      camera: Camera3D,
      resources: inref<ShadowDepthResources>,
      meshDraws: MeshDraw[],
      meshDrawCount: int,
      skinnedStart: int
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

    Raylib.EndMode3D()

  /// Render the shadow pass — collect casters, render regions to atlas.
  let runShadowPass
    (
      shadowAtlas: ShadowAtlas,
      atlasCfg: ShadowAtlasConfig,
      resources: inref<ShadowDepthResources>,
      lights: LightBuffers,
      meshDraws: MeshDraw[],
      meshDrawCount: int,
      skinnedStart: int,
      frameState: inref<FrameState>,
      gameCtx: GameContext
    ) =
    shadowAtlas.Clear()

    let mutable hasCasters = false
    // Per-light shadow-slot mappings; returned to the caller (the closure stores them on the
    // pipeline fields so the forward-pass handlers can read them).
    let mutable pointSlots = Array.create<int> lights.PointLights.Count -1
    let mutable spotSlots = Array.create<int> lights.SpotLights.Count -1

    match frameState.Camera with
    | ValueNone ->
      // No camera → no shadow pass; slots stay all -1 (no shadows).
      ()
    | ValueSome activeCamera ->
      if meshDrawCount > 0 then
        let struct (hasC, ptSlots, spSlots) =
          collectShadowCasters(lights, shadowAtlas)

        hasCasters <- hasC
        pointSlots <- ptSlots
        spotSlots <- spSlots

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

              let maxShadowDist = 2500.0f // 50^2

              if distToCamera <= maxShadowDist then
                match caster.Type with
                | ShadowCasterType.Point ->
                  let downTarget = caster.LightPosition - Vector3.UnitY

                  let ptCamera =
                    Camera3D(
                      Position = caster.LightPosition,
                      Target = downTarget,
                      Up = Vector3.UnitZ,
                      FovY = 90.0f,
                      Projection = CameraProjection.Perspective
                    )

                  renderShadowRegion(
                    shadowAtlas,
                    caster.AtlasRegion,
                    ptCamera,
                    &resources,
                    meshDraws,
                    meshDrawCount,
                    skinnedStart
                  )

                | ShadowCasterType.Spot ->
                  let spotCamera =
                    Camera3D(
                      Position = caster.LightPosition,
                      Target = caster.LightPosition + caster.LightDirection,
                      Up = Vector3.UnitY,
                      FovY = 90.0f,
                      Projection = CameraProjection.Perspective
                    )

                  renderShadowRegion(
                    shadowAtlas,
                    caster.AtlasRegion,
                    spotCamera,
                    &resources,
                    meshDraws,
                    meshDrawCount,
                    skinnedStart
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
                    meshDraws,
                    meshDrawCount,
                    skinnedStart
                  )

                  Rlgl.SetClipPlanes(prevNear, prevFar)

          Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)
          Raylib.EndTextureMode()

    struct (hasCasters, pointSlots, spotSlots)

// ------------------------------------------------------------------
// ForwardPbrPipeline — closure-over-object-expression factory
// ------------------------------------------------------------------

/// <summary>
/// Refactored Forward PBR pipeline. Eliminates 3x shader variant duplication
/// by using parameterized ShaderVariant structs. No PipelineContext class —
/// all mutable state lives in the object-expression closure.
/// </summary>
/// <remarks>
/// Implements the same <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/> interface.
/// Swap by changing one line:
/// <code>
/// Renderer3D.create (ForwardPbrPipeline()) view
/// </code>
/// </remarks>
type ForwardPbrPipeline
  (
    [<Struct>] ?postProcess: PostProcessConfig3D,
    [<Struct>] ?maxPointLights: int,
    [<Struct>] ?maxSpotLights: int,
    [<Struct>] ?shadowAtlasConfig: ShadowAtlasConfig,
    [<Struct>] ?shadowBiasConfig: ShadowBiasConfig
  ) =

  let ppConfig = ValueOption.defaultValue PostProcessConfig3D.none postProcess
  let maxPt = ValueOption.defaultValue 8 maxPointLights
  let maxSp = ValueOption.defaultValue 4 maxSpotLights

  let atlasCfg =
    ValueOption.defaultValue ShadowAtlasConfig.defaults shadowAtlasConfig

  let biasCfg =
    ValueOption.defaultValue ShadowBiasConfig.defaults shadowBiasConfig

  // ── Mutable state ─────────────────────────────────────────
  let mutable forwardShader: Shader = Unchecked.defaultof<Shader>
  let mutable instancedShader: Shader = Unchecked.defaultof<Shader>
  let mutable skinnedShader: Shader = Unchecked.defaultof<Shader>
  let mutable depthShadowShader: Shader = Unchecked.defaultof<Shader>
  let mutable depthShadowSkinnedShader: Shader = Unchecked.defaultof<Shader>
  let mutable postProcessShader: Shader = Unchecked.defaultof<Shader>

  let mutable depthShadowMaterial: Material = Unchecked.defaultof<Material>

  let mutable depthShadowSkinnedMaterial: Material =
    Unchecked.defaultof<Material>

  let mutable shadowNormalMatrixLoc: int = -1
  let mutable shadowSkinnedNormalMatrixLoc: int = -1
  let mutable shadowBoneLoc: int = -1

  let mutable forward: ShaderVariant = Unchecked.defaultof<ShaderVariant>
  let mutable instanced: ShaderVariant = Unchecked.defaultof<ShaderVariant>
  let mutable skinned: ShaderVariant = Unchecked.defaultof<ShaderVariant>

  let mutable shadowAtlas: ShadowAtlas = Unchecked.defaultof<ShadowAtlas>

  // Per-light shadow caster slot mapping (computed in runShadowPass, read in uploadLights).
  // Indexed by lights.PointLights/SpotLights buffer position; -1 = no shadow. Reallocated per
  // frame to match the live light counts.
  let mutable pointShadowSlots: int[] = [||]
  let mutable spotShadowSlots: int[] = [||]

  let lights: LightBuffers = {
    Ambient = ResizeArray<AmbientLight3D> 1
    DirLights = ResizeArray<DirectionalLight3D> 1
    PointLights = ResizeArray<PointLight3D> maxPt
    SpotLights = ResizeArray<SpotLight3D> maxSp
  }

  let ppPasses: PostProcessPass3D[] =
    match ppConfig.Passes with
    | ValueSome passes -> passes
    | ValueNone -> Array.empty

  let applyPostProcess
    (ctx: GameContext)
    (sceneTarget: RenderTexture2D)
    (rtPool: IRenderTargetPool3D)
    =
    let mutable src = sceneTarget
    let w = ctx.WindowWidth
    let h = ctx.WindowHeight

    for i = 0 to ppPasses.Length - 1 do
      let pass = ppPasses[i]
      let isLast = i = ppPasses.Length - 1

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

      Raylib.BeginShaderMode pass.Shader

      match pass.OnSetup with
      | ValueSome f -> f pass.Shader ctx
      | ValueNone -> ()

      let sourceRect = Raylib_cs.Rectangle(0.0f, 0.0f, float32 w, float32 -h)
      let destRect = Raylib_cs.Rectangle(0.0f, 0.0f, float32 w, float32 h)

      Raylib.DrawTexturePro(
        src.Texture,
        sourceRect,
        destRect,
        Vector2.Zero,
        0.0f,
        Color.White
      )

      Raylib.EndShaderMode()

      match dst with
      | ValueSome target ->
        Raylib.EndTextureMode()
        src <- target
      | ValueNone -> ()

  // ── IRenderPipeline3D ────────────────────────────────────────

  interface IRenderPipeline3D with
    member _.Initialize() =
      forwardShader <- Shaders.loadForwardShader maxPt maxSp atlasCfg.MaxCasters

      instancedShader <-
        Shaders.loadForwardInstancedShader maxPt maxSp atlasCfg.MaxCasters

      skinnedShader <-
        Shaders.loadForwardSkinnedShader maxPt maxSp atlasCfg.MaxCasters

      let instanceTransformLoc =
        Raylib.GetShaderLocationAttrib(instancedShader, "instanceTransform")

      NativePtr.set
        instancedShader.Locs
        (int ShaderLocationIndex.MatrixModel)
        instanceTransformLoc

      depthShadowShader <- Shaders.loadDepthShadowShader()
      depthShadowSkinnedShader <- Shaders.loadDepthShadowSkinnedShader()
      postProcessShader <- Shaders.loadPostProcessShader()

      depthShadowMaterial <- Raylib.LoadMaterialDefault()
      depthShadowMaterial.Shader <- depthShadowShader

      depthShadowSkinnedMaterial <- Raylib.LoadMaterialDefault()
      depthShadowSkinnedMaterial.Shader <- depthShadowSkinnedShader

      shadowNormalMatrixLoc <-
        Raylib.GetShaderLocation(depthShadowShader, "normalMatrix")

      shadowSkinnedNormalMatrixLoc <-
        Raylib.GetShaderLocation(depthShadowSkinnedShader, "normalMatrix")

      shadowBoneLoc <-
        Raylib.GetShaderLocation(depthShadowSkinnedShader, "boneMatrices[0]")

      shadowAtlas <- ShadowAtlas(atlasCfg, biasCfg)
      shadowAtlas.Initialize()

      let fwdLocs =
        cacheLocations(forwardShader, maxPt, maxSp, atlasCfg.MaxCasters)

      let instLocs =
        cacheLocations(instancedShader, maxPt, maxSp, atlasCfg.MaxCasters)

      let skLocs =
        cacheLocations(skinnedShader, maxPt, maxSp, atlasCfg.MaxCasters)

      forward <- ShaderVariant(fwdLocs, MaterialCache 16)
      instanced <- ShaderVariant(instLocs, MaterialCache 16)
      skinned <- ShaderVariant(skLocs, MaterialCache 16)

    member _.Shutdown() =
      for KeyValue(_, mat) in instanced.MaterialCache.cache do
        Raylib.UnloadMaterial mat

      instanced.MaterialCache.cache.Clear()

      for KeyValue(_, mat) in skinned.MaterialCache.cache do
        Raylib.UnloadMaterial mat

      skinned.MaterialCache.cache.Clear()

      Raylib.UnloadShader forwardShader
      Raylib.UnloadShader instancedShader
      Raylib.UnloadShader skinnedShader
      Raylib.UnloadShader depthShadowShader
      Raylib.UnloadShader depthShadowSkinnedShader
      Raylib.UnloadShader postProcessShader

      Raylib.UnloadMaterial depthShadowMaterial
      Raylib.UnloadMaterial depthShadowSkinnedMaterial

      for KeyValue(_, mat) in forward.MaterialCache.cache do
        Raylib.UnloadMaterial mat

      forward.MaterialCache.cache.Clear()

      if shadowAtlas <> Unchecked.defaultof<ShadowAtlas> then
        shadowAtlas.Shutdown()

    member _.Execute(gameCtx, buffer, rtPool) =
      // ── Step 1: Pre-scan buffer (camera, lights, shadow origin, warm caches) ──
      clearLights lights

      let frameState =
        preScan(
          buffer,
          lights,
          &forward,
          &instanced,
          &skinned,
          forwardShader,
          instancedShader,
          skinnedShader
        )

      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true

      // ── Step 2: Shadow pass (render all casters to atlas) ──
      let struct (meshDraws, meshDrawCount, skinnedStart) =
        collectMeshDraws buffer

      let shadowResources = {
        Shader = depthShadowShader
        SkinnedShader = depthShadowSkinnedShader
        Material = depthShadowMaterial
        SkinnedMaterial = depthShadowSkinnedMaterial
        NormalMatrixLoc = shadowNormalMatrixLoc
        SkinnedNormalMatrixLoc = shadowSkinnedNormalMatrixLoc
        BoneLoc = shadowBoneLoc
      }

      let mutable hasShadowCasters = false

      try
        let struct (hasC, ptSlots, spSlots) =
          runShadowPass(
            shadowAtlas,
            atlasCfg,
            &shadowResources,
            lights,
            meshDraws,
            meshDrawCount,
            skinnedStart,
            &frameState,
            gameCtx
          )

        hasShadowCasters <- hasC
        pointShadowSlots <- ptSlots
        spotShadowSlots <- spSlots
      finally
        ArrayPool<MeshDraw>.Shared.Return(meshDraws, false)

      // ── Step 3: Upload shadow atlas uniforms to all shaders ──
      match frameState.Camera with
      | ValueSome cam ->
        uploadShadowUniforms(
          hasShadowCasters,
          &forward,
          &instanced,
          &skinned,
          shadowAtlas,
          cam.Position,
          atlasCfg.MaxCasters
        )
      | ValueNone -> ()

      // ── Step 4: Clear lights for forward pass (dispatch will re-add them) ──
      clearLights lights
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true

      // ── Step 5: Forward pass (dispatch all commands) ──
      let mutable cameraActive = false
      let mutable currentCamera = Unchecked.defaultof<Camera3D>
      let mutable shaderActive = false

      let dispatchForwardPass() =
        for i = 0 to buffer.Count - 1 do
          match buffer[i] with
          // ── Camera management (inline — simple state toggles) ──
          | Command3D.BeginCamera cam ->
            if cameraActive then
              if shaderActive then
                Raylib.EndShaderMode()
                shaderActive <- false

              Raylib.EndMode3D()

            Raylib.BeginMode3D cam
            cameraActive <- true
            currentCamera <- cam

          | Command3D.BeginCameraConfig cfg ->
            if cameraActive then
              if shaderActive then
                Raylib.EndShaderMode()
                shaderActive <- false

              Raylib.EndMode3D()

            applyCameraConfig(&cfg, gameCtx)
            Raylib.BeginMode3D cfg.Camera
            cameraActive <- true
            currentCamera <- cfg.Camera

          | Command3D.EndCamera ->
            if cameraActive then
              if shaderActive then
                Raylib.EndShaderMode()
                shaderActive <- false

              Raylib.EndMode3D()
              cameraActive <- false

            Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)

          // ── Drawing commands (delegated to handlers) ──
          | Command3D.DrawMesh(mesh, transform, material) ->
            if cameraActive then
              handleDrawMesh(
                forwardShader,
                &forward,
                lights,
                maxPt,
                maxSp,
                pointShadowSlots,
                spotShadowSlots,
                mesh,
                transform,
                material
              )

          | Command3D.DrawModel(model, transform) ->
            if cameraActive then
              handleDrawModel(
                forwardShader,
                &forward,
                lights,
                maxPt,
                maxSp,
                pointShadowSlots,
                spotShadowSlots,
                model,
                transform
              )

          | Command3D.DrawSkinnedMesh(mesh, transform, material, bones) ->
            if cameraActive then
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
            if cameraActive then
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

          | Command3D.DrawBillboard(tex, pos, size, color) ->
            if cameraActive then
              handleDrawBillboard(currentCamera, tex, pos, size, color)

          | Command3D.DrawBillboardBatch(tex, pos, sizes, colors, count) ->
            if cameraActive then
              handleDrawBillboardBatch(
                currentCamera,
                tex,
                pos,
                sizes,
                colors,
                count
              )

          | Command3D.DrawLine3D(start, finish, color) ->
            if cameraActive then
              Raylib.DrawLine3D(start, finish, color)

          // ── Light commands (delegated) ──
          | Command3D.SetAmbientLight _
          | Command3D.AddDirectionalLight _
          | Command3D.AddPointLight _
          | Command3D.AddSpotLight _ as cmd ->
            handleLightCommand(lights, cmd, &forward, &instanced, &skinned)

          // ── Immediate mode (inline — unique save/restore pattern) ──
          | Command3D.DrawImmediate action ->
            let savedCam = cameraActive
            let savedShader = shaderActive

            if shaderActive then
              Raylib.EndShaderMode()
              shaderActive <- false

            if cameraActive then
              Raylib.EndMode3D()
              cameraActive <- false

            try
              action()
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

        // End remaining shader/camera state after dispatch
        if shaderActive then
          Raylib.EndShaderMode()

        if cameraActive then
          Raylib.EndMode3D()

      // ── Step 5b: Dispatch (direct or via scene RT for post-process) ──
      match ppConfig.Passes with
      | ValueNone
      | ValueSome [||] -> dispatchForwardPass()
      | _ ->
        let sceneRT = rtPool.Acquire(gameCtx.WindowWidth, gameCtx.WindowHeight)
        Raylib.BeginTextureMode sceneRT
        Raylib.ClearBackground Color.Black
        dispatchForwardPass()
        Raylib.EndTextureMode()
        applyPostProcess gameCtx sceneRT rtPool

      // ── Step 6: Debug overlay (optional) ──
      if atlasCfg.ShowDebugOverlay then
        shadowAtlas.RenderDebugOverlay(
          gameCtx.WindowWidth,
          gameCtx.WindowHeight
        )
