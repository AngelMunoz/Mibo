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
module private NativeHelpersV2 =

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
module private NormalMatrixHelpersV2 =

  let inline computeNormalMatrix(model: Matrix4x4) =
    let mutable inv = Matrix4x4.Identity
    Matrix4x4.Invert(model, &inv) |> ignore
    Matrix4x4.Transpose inv

// ------------------------------------------------------------------
// Material Cache Key
// ------------------------------------------------------------------

[<Struct>]
type private MaterialKeyV2 = {
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

module private MaterialKeyV2 =

  let inline fromMaterial3D(mat: inref<Material3D>) : MaterialKeyV2 = {
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
type private MaterialUniforms = {
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
type private AmbientUniforms = { Color: int; Intensity: int }

[<IsReadOnly; Struct>]
type private DirLightUniforms = {
  Dir: int
  Color: int
  Intensity: int
  CastsShadows: int
}

[<IsReadOnly; Struct>]
type private PointLightUniforms = {
  Count: int
  Pos: int[]
  Color: int[]
  Intensity: int[]
  Radius: int[]
  Falloff: int[]
}

[<IsReadOnly; Struct>]
type private SpotLightUniforms = {
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
type private ShadowUniforms = {
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
type private ShaderLocations = {
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
type private MaterialCache =
  val mutable cache: Dictionary<MaterialKeyV2, Material>
  val mutable LastKey: MaterialKeyV2
  val mutable HasLast: bool
  val mutable LastMaterial: Material

  new(capacity: int) =
    {
      cache = Dictionary<MaterialKeyV2, Material>(capacity)
      LastKey = Unchecked.defaultof<MaterialKeyV2>
      HasLast = false
      LastMaterial = Unchecked.defaultof<Material>
    }

// ------------------------------------------------------------------
// Shader Variant (mutable — class-style struct)
// ------------------------------------------------------------------

[<Struct>]
type private ShaderVariant =
  val Locs: ShaderLocations
  val mutable MaterialCache: MaterialCache
  val mutable LightsDirty: bool

  new(locs: ShaderLocations, matCache: MaterialCache) =
    {
      Locs = locs
      MaterialCache = matCache
      LightsDirty = true
    }

// ------------------------------------------------------------------
// Light Buffers (reference type)
// ------------------------------------------------------------------

type private LightBuffers = {
  Ambient: ResizeArray<AmbientLight3D>
  DirLights: ResizeArray<DirectionalLight3D>
  PointLights: ResizeArray<PointLight3D>
  SpotLights: ResizeArray<SpotLight3D>
}

// ------------------------------------------------------------------
// Frame State (uses voption)
// ------------------------------------------------------------------

[<IsReadOnly; Struct>]
type private FrameState = {
  Camera: Camera3D voption
  ShadowOrigin: Vector3 voption
}

// ------------------------------------------------------------------
// Shadow Pass Helpers
// ------------------------------------------------------------------

[<AutoOpen>]
module private ShadowPassHelpersV2 =

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

    struct (arr, count)

// ------------------------------------------------------------------
// Pure / near-pure functions
// ------------------------------------------------------------------

[<AutoOpen>]
module private PipelineFunctions =

  let colorToVec3(c: Color) =
    Vector3(float32 c.R / 255.0f, float32 c.G / 255.0f, float32 c.B / 255.0f)

  let colorToVec4(c: Color) =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  /// Single parameterized location cache replacing 3x duplication.
  let cacheLocations
    (shader: Shader, maxPt: int, maxSp: int, maxCasters: int)
    : ShaderLocations =
    let ptPos = Array.zeroCreate<int> maxPt
    let ptColor = Array.zeroCreate<int> maxPt
    let ptIntensity = Array.zeroCreate<int> maxPt
    let ptRadius = Array.zeroCreate<int> maxPt
    let ptFalloff = Array.zeroCreate<int> maxPt

    let spPos = Array.zeroCreate<int> maxSp
    let spDir = Array.zeroCreate<int> maxSp
    let spColor = Array.zeroCreate<int> maxSp
    let spIntensity = Array.zeroCreate<int> maxSp
    let spRadius = Array.zeroCreate<int> maxSp
    let spInner = Array.zeroCreate<int> maxSp
    let spOuter = Array.zeroCreate<int> maxSp

    let shadowViewProjs = Array.zeroCreate<int> maxCasters
    let shadowUVOffsets = Array.zeroCreate<int> maxCasters
    let shadowLightPositions = Array.zeroCreate<int> maxCasters
    let shadowBiases = Array.zeroCreate<int> maxCasters
    let shadowTypes = Array.zeroCreate<int> maxCasters

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

    for i = 0 to maxPt - 1 do
      ptPos[i] <- Raylib.GetShaderLocation(shader, $"pointLightPos[{i}]")
      ptColor[i] <- Raylib.GetShaderLocation(shader, $"pointLightColor[{i}]")

      ptIntensity[i] <-
        Raylib.GetShaderLocation(shader, $"pointLightIntensity[{i}]")

      ptRadius[i] <- Raylib.GetShaderLocation(shader, $"pointLightRadius[{i}]")

      ptFalloff[i] <-
        Raylib.GetShaderLocation(shader, $"pointLightFalloff[{i}]")

    let ptLocs = {
      Count = Raylib.GetShaderLocation(shader, "pointLightCount")
      Pos = ptPos
      Color = ptColor
      Intensity = ptIntensity
      Radius = ptRadius
      Falloff = ptFalloff
    }

    for i = 0 to maxSp - 1 do
      spPos[i] <- Raylib.GetShaderLocation(shader, $"spotLightPos[{i}]")
      spDir[i] <- Raylib.GetShaderLocation(shader, $"spotLightDir[{i}]")
      spColor[i] <- Raylib.GetShaderLocation(shader, $"spotLightColor[{i}]")

      spIntensity[i] <-
        Raylib.GetShaderLocation(shader, $"spotLightIntensity[{i}]")

      spRadius[i] <- Raylib.GetShaderLocation(shader, $"spotLightRadius[{i}]")

      spInner[i] <-
        Raylib.GetShaderLocation(shader, $"spotLightInnerCutoff[{i}]")

      spOuter[i] <-
        Raylib.GetShaderLocation(shader, $"spotLightOuterCutoff[{i}]")

    let spLocs = {
      Count = Raylib.GetShaderLocation(shader, "spotLightCount")
      Pos = spPos
      Dir = spDir
      Color = spColor
      Intensity = spIntensity
      Radius = spRadius
      InnerCutoff = spInner
      OuterCutoff = spOuter
    }

    let shadowLocs = {
      Pass = Raylib.GetShaderLocation(shader, "shadowPass")
      Atlas = Raylib.GetShaderLocation(shader, "shadowAtlas")
      CasterCount = Raylib.GetShaderLocation(shader, "shadowCasterCount")
      ViewProjs = shadowViewProjs
      UVOffsets = shadowUVOffsets
      LightPositions = shadowLightPositions
      Biases = shadowBiases
      Types = shadowTypes
    }

    rlSetUniformInt shadowLocs.Atlas 15

    for i = 0 to maxCasters - 1 do
      shadowViewProjs[i] <-
        Raylib.GetShaderLocation(shader, $"shadowViewProjs[{i}]")

      shadowUVOffsets[i] <-
        Raylib.GetShaderLocation(shader, $"shadowUVOffsets[{i}]")

      shadowLightPositions[i] <-
        Raylib.GetShaderLocation(shader, $"shadowLightPositions[{i}]")

      shadowBiases[i] <- Raylib.GetShaderLocation(shader, $"shadowBiases[{i}]")
      shadowTypes[i] <- Raylib.GetShaderLocation(shader, $"shadowTypes[{i}]")

    let cameraPosLoc = Raylib.GetShaderLocation(shader, "cameraPos")
    let shadowNormalMatrixLoc = Raylib.GetShaderLocation(shader, "normalMatrix")
    let bonesLoc = Raylib.GetShaderLocation(shader, "boneMatrices[0]")

    {
      Shader = shader
      Cached = true
      Material = matLocs
      Ambient = ambLocs
      DirLight = dlLocs
      PointLights = ptLocs
      SpotLights = spLocs
      Shadow = shadowLocs
      CameraPos = cameraPosLoc
      ShadowNormalMatrix = shadowNormalMatrixLoc
      Bones = bonesLoc
    }

  /// Single parameterized light upload replacing 3x duplication.
  let uploadLights
    (
      shader: Shader,
      variant: inref<ShaderVariant>,
      lights: LightBuffers,
      maxPt: int,
      maxSp: int
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
      key: inref<MaterialKeyV2>
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
      cameraPos: Vector3
    ) =
    if atlas.Fbo.Depth.Id <> 0u then
      Rlgl.EnableShader shader.Id
      Rlgl.ActiveTextureSlot 15
      Rlgl.EnableTexture atlas.Fbo.Depth.Id
      rlSetUniformInt shadowLocs.Atlas 15
      Rlgl.ActiveTextureSlot 0

    let count = min atlas.ActiveCasterCount shadowLocs.CasterCount

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
    if hasCasters && cameraPos <> Unchecked.defaultof<Vector3> then
      atlas.PrepareUniforms()
      let fwd = forward.Locs
      let inst = instanced.Locs
      let sk = skinned.Locs

      uploadShadowUniformsForShader(
        fwd.Shader,
        &fwd.Shadow,
        fwd.CameraPos,
        atlas,
        cameraPos
      )

      uploadShadowUniformsForShader(
        inst.Shader,
        &inst.Shadow,
        inst.CameraPos,
        atlas,
        cameraPos
      )

      uploadShadowUniformsForShader(
        sk.Shader,
        &sk.Shadow,
        sk.CameraPos,
        atlas,
        cameraPos
      )

  /// Upload bone matrices to skinned shader — uses ReadOnlySpan for no-copy.
  let uploadBoneMatrices
    (shader: Shader, boneLoc: int, bones: ReadOnlySpan<Matrix4x4>)
    =
    let count = min bones.Length 128

    for i = 0 to count - 1 do
      Raylib.SetShaderValueMatrix(shader, boneLoc + i, bones[i])

  /// Render the shadow pass — all casters to atlas.
  let runShadowPass
    (
      shadowAtlas: ShadowAtlas,
      atlasCfg: ShadowAtlasConfig,
      depthShadowShader: Shader,
      depthShadowSkinnedShader: Shader,
      depthShadowMaterial: Material,
      depthShadowSkinnedMaterial: Material,
      shadowNormalMatrixLoc: int,
      shadowBoneLoc: int,
      lights: LightBuffers,
      meshDraws: MeshDraw[],
      meshDrawCount: int,
      frameState: inref<FrameState>,
      gameCtx: GameContext
    ) =
    shadowAtlas.Clear()
    let mutable hasCasters = false

    match frameState.Camera with
    | ValueNone -> ()
    | ValueSome activeCamera ->
      if meshDrawCount > 0 then
        for dir in lights.DirLights do
          if dir.CastsShadows then
            hasCasters <- true

            shadowAtlas.AddCaster(
              ShadowCasterType.Directional,
              Vector3.Zero,
              dir.Direction,
              Vector3.Zero,
              true,
              ValueNone
            )
            |> ignore

        for pt in lights.PointLights do
          if pt.CastsShadows then
            hasCasters <- true

            shadowAtlas.AddCaster(
              ShadowCasterType.Point,
              pt.Position,
              Vector3.Zero,
              Vector3.Zero,
              true,
              pt.ShadowBias
            )
            |> ignore

        for sp in lights.SpotLights do
          if sp.CastsShadows then
            hasCasters <- true

            shadowAtlas.AddCaster(
              ShadowCasterType.Spot,
              sp.Position,
              sp.Direction,
              sp.Position + sp.Direction,
              true,
              sp.ShadowBias
            )
            |> ignore

        if shadowAtlas.Count > 0 then
          Raylib.BeginTextureMode(shadowAtlas.Fbo)
          Raylib.ClearBackground(Color.White)

          let inline renderShadowRegion (regionIndex: int) (camera: Camera3D) =
            shadowAtlas.GetRegionViewport(regionIndex)
            Raylib.BeginMode3D(camera)

            let vp =
              Raymath.MatrixMultiply(
                Rlgl.GetMatrixModelview(),
                Rlgl.GetMatrixProjection()
              )

            shadowAtlas.SetRegionViewProj(regionIndex, vp)

            for i = 0 to meshDrawCount - 1 do
              let draw = meshDraws[i]
              let nm = computeNormalMatrix draw.Transform

              match draw.Bones with
              | ValueSome bones ->
                Raylib.BeginShaderMode depthShadowSkinnedShader

                Raylib.SetShaderValueMatrix(
                  depthShadowSkinnedShader,
                  shadowNormalMatrixLoc,
                  nm
                )

                let shadowBoneCount = min bones.Length 128

                for bi = 0 to shadowBoneCount - 1 do
                  Raylib.SetShaderValueMatrix(
                    depthShadowSkinnedShader,
                    shadowBoneLoc + bi,
                    bones[bi]
                  )

                Raylib.DrawMesh(
                  draw.Mesh,
                  depthShadowSkinnedMaterial,
                  draw.Transform
                )

                Raylib.EndShaderMode()
              | ValueNone ->
                Raylib.SetShaderValueMatrix(
                  depthShadowShader,
                  shadowNormalMatrixLoc,
                  nm
                )

                Raylib.DrawMesh(draw.Mesh, depthShadowMaterial, draw.Transform)

            Raylib.EndMode3D()

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

                  renderShadowRegion caster.AtlasRegion ptCamera

                | ShadowCasterType.Spot ->
                  let spotCamera =
                    Camera3D(
                      Position = caster.LightPosition,
                      Target = caster.LightPosition + caster.LightDirection,
                      Up = Vector3.UnitY,
                      FovY = 90.0f,
                      Projection = CameraProjection.Perspective
                    )

                  renderShadowRegion caster.AtlasRegion spotCamera

                | _ ->
                  // Directional light
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

                  let prevNear = Rlgl.GetCullDistanceNear()
                  let prevFar = Rlgl.GetCullDistanceFar()
                  Rlgl.SetClipPlanes(float shadowNear, float shadowFar)

                  let dirCamera =
                    Camera3D(
                      Position = lightPos,
                      Target = shadowOrigin,
                      Up = safeUp,
                      FovY = orthoSize,
                      Projection = CameraProjection.Orthographic
                    )

                  renderShadowRegion caster.AtlasRegion dirCamera
                  Rlgl.SetClipPlanes(prevNear, prevFar)

          Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)
          Raylib.EndTextureMode()

    hasCasters

// ------------------------------------------------------------------
// ForwardPbrPipelineV2 — closure-over-object-expression factory
// ------------------------------------------------------------------

/// <summary>
/// Refactored <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/> implementation.
/// Eliminates 3x shader variant duplication by using parameterized ShaderVariant structs.
/// No PipelineContext class — all mutable state lives in the create closure.
/// </summary>
/// <summary>
/// Refactored Forward PBR pipeline. Eliminates 3x shader variant duplication
/// by using parameterized ShaderVariant structs. No PipelineContext class —
/// all mutable state lives in the object-expression closure.
/// </summary>
/// <remarks>
/// Implements the same <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/> interface.
/// Swap by changing one line:
/// <code>
/// Renderer3D.create (ForwardPbrPipelineV2()) view
/// </code>
/// </remarks>
type ForwardPbrPipelineV2
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

  let mutable forward: ShaderVariant = Unchecked.defaultof<ShaderVariant>
  let mutable instanced: ShaderVariant = Unchecked.defaultof<ShaderVariant>
  let mutable skinned: ShaderVariant = Unchecked.defaultof<ShaderVariant>

  let mutable shadowAtlas: ShadowAtlas = Unchecked.defaultof<ShadowAtlas>

  let lights: LightBuffers = {
    Ambient = ResizeArray<AmbientLight3D> 1
    DirLights = ResizeArray<DirectionalLight3D> 1
    PointLights = ResizeArray<PointLight3D> maxPt
    SpotLights = ResizeArray<SpotLight3D> maxSp
  }

  let mutable lightsDirty = true

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

    member _.Execute gameCtx buffer rtPool =
      // ── Pre-scan: first camera, lights, shadow origin, warm materials ──
      let mutable frameState = {
        Camera = ValueNone
        ShadowOrigin = ValueNone
      }

      lights.Ambient.Clear()
      lights.DirLights.Clear()
      lights.PointLights.Clear()
      lights.SpotLights.Clear()

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
        | Command3D.AddDirectionalLight l -> lights.DirLights.Add l
        | Command3D.AddPointLight l -> lights.PointLights.Add l
        | Command3D.AddSpotLight l -> lights.SpotLights.Add l
        | Command3D.SetAmbientLight l ->
          lights.Ambient.Clear()
          lights.Ambient.Add l
        | Command3D.DrawMesh(_, _, mat) ->
          let key = MaterialKeyV2.fromMaterial3D &mat
          getOrCreate(&forward, forwardShader, &mat, &key) |> ignore
        | Command3D.DrawSkinnedMesh(_, _, mat, _) ->
          let key = MaterialKeyV2.fromMaterial3D &mat
          getOrCreate(&skinned, skinnedShader, &mat, &key) |> ignore
        | Command3D.DrawMeshInstanced(_, _, mat, _) ->
          let key = MaterialKeyV2.fromMaterial3D &mat
          getOrCreate(&instanced, instancedShader, &mat, &key) |> ignore
        | _ -> ()

      // Mark all variants dirty so lights get uploaded on first use
      lightsDirty <- true
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true

      // ── Shadow pass ──
      let struct (meshDraws, meshDrawCount) = collectMeshDraws buffer

      let mutable hasShadowCasters = false

      try
        hasShadowCasters <-
          runShadowPass(
            shadowAtlas,
            atlasCfg,
            depthShadowShader,
            depthShadowSkinnedShader,
            depthShadowMaterial,
            depthShadowSkinnedMaterial,
            skinned.Locs.ShadowNormalMatrix,
            skinned.Locs.Bones,
            lights,
            meshDraws,
            meshDrawCount,
            &frameState,
            gameCtx
          )
      finally
        ArrayPool<MeshDraw>.Shared.Return(meshDraws, false)

      // Upload shadow atlas uniforms to all shaders
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

      // Clear lights for forward pass (dispatch will re-add them)
      lights.Ambient.Clear()
      lights.DirLights.Clear()
      lights.PointLights.Clear()
      lights.SpotLights.Clear()
      lightsDirty <- true
      forward.LightsDirty <- true
      instanced.LightsDirty <- true
      skinned.LightsDirty <- true

      // ── Forward pass ──
      let mutable cameraActive = false
      let mutable currentCamera = Unchecked.defaultof<Camera3D>
      let mutable shaderActive = false
      let mutable activeVariant = 0 // 0=none, 1=forward, 2=instanced

      let ensureShaderActive (targetShader: Shader) (variantId: int) =
        if not shaderActive then
          Raylib.BeginShaderMode targetShader
          shaderActive <- true
          activeVariant <- variantId

      let ensureShaderInactive() =
        if shaderActive then
          Raylib.EndShaderMode()
          shaderActive <- false
          activeVariant <- 0

      let dispatchForwardPass() =
        for i = 0 to buffer.Count - 1 do
          match buffer[i] with
          | Command3D.BeginCamera cam ->
            if cameraActive then
              ensureShaderInactive()
              Raylib.EndMode3D()

            Raylib.BeginMode3D cam
            cameraActive <- true
            currentCamera <- cam

          | Command3D.BeginCameraConfig cfg ->
            if cameraActive then
              ensureShaderInactive()
              Raylib.EndMode3D()

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
                Raylib.ClearBackground(color)
                Rlgl.DisableScissorTest()
              | ValueNone -> ()

              Rlgl.Viewport(x, y, w, h)
            | ValueNone ->
              match cfg.ClearColor with
              | ValueSome color -> Raylib.ClearBackground(color)
              | ValueNone -> ()

            Raylib.BeginMode3D cfg.Camera
            cameraActive <- true
            currentCamera <- cfg.Camera

          | Command3D.EndCamera ->
            if cameraActive then
              ensureShaderInactive()
              Raylib.EndMode3D()
              cameraActive <- false

            Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)

          | Command3D.DrawMesh(mesh, transform, material) ->
            if cameraActive then
              ensureShaderActive forwardShader 1

              if lightsDirty || forward.LightsDirty then
                uploadLights(forwardShader, &forward, lights, maxPt, maxSp)
                forward.LightsDirty <- false

              let nm = computeNormalMatrix transform

              setMaterialUniforms(
                forwardShader,
                &forward.Locs.Material,
                &material,
                nm
              )

              let key = MaterialKeyV2.fromMaterial3D &material
              let mat = getOrCreate(&forward, forwardShader, &material, &key)
              Raylib.DrawMesh(mesh, mat, transform)

          | Command3D.DrawModel(model, transform) ->
            if cameraActive then
              ensureShaderActive forwardShader 1

              if lightsDirty || forward.LightsDirty then
                uploadLights(forwardShader, &forward, lights, maxPt, maxSp)
                forward.LightsDirty <- false

              let nm = computeNormalMatrix transform

              for mi = 0 to model.MeshCount - 1 do
                let mesh = NativePtr.get model.Meshes mi
                let matIdx = NativePtr.get model.MeshMaterial mi
                let raylibMat = NativePtr.get model.Materials matIdx
                let mat3d = Material3D.fromRaylibMaterial raylibMat

                setMaterialUniforms(
                  forwardShader,
                  &forward.Locs.Material,
                  &mat3d,
                  nm
                )

                let key = MaterialKeyV2.fromMaterial3D &mat3d
                let mat = getOrCreate(&forward, forwardShader, &mat3d, &key)
                Raylib.DrawMesh(mesh, mat, transform)

          | Command3D.DrawBillboard(texture, position, size, color) ->
            if cameraActive then
              ensureShaderInactive()
              Rlgl.EnableShader(Rlgl.GetShaderIdDefault())

              let source =
                Rectangle(
                  0.0f,
                  0.0f,
                  float32 texture.Width,
                  float32 texture.Height
                )

              Raylib.DrawBillboardRec(
                currentCamera,
                texture,
                source,
                position,
                size,
                color
              )

          | Command3D.DrawLine3D(start, finish, color) ->
            if cameraActive then
              Raylib.DrawLine3D(start, finish, color)

          | Command3D.DrawSkinnedMesh(mesh, transform, material, bones) ->
            if cameraActive then
              ensureShaderInactive()
              Raylib.BeginShaderMode skinnedShader
              shaderActive <- true
              activeVariant <- 0

              if lightsDirty || skinned.LightsDirty then
                uploadLights(skinnedShader, &skinned, lights, maxPt, maxSp)
                skinned.LightsDirty <- false

              setShaderVec3
                skinnedShader
                skinned.Locs.CameraPos
                currentCamera.Position

              setShaderInt skinnedShader skinned.Locs.Shadow.Pass 0

              let nm = computeNormalMatrix transform

              setMaterialUniforms(
                skinnedShader,
                &skinned.Locs.Material,
                &material,
                nm
              )

              uploadBoneMatrices(
                skinnedShader,
                skinned.Locs.Bones,
                ReadOnlySpan bones
              )

              let key = MaterialKeyV2.fromMaterial3D &material
              let mat = getOrCreate(&skinned, skinnedShader, &material, &key)
              Raylib.DrawMesh(mesh, mat, transform)
              ensureShaderInactive()

          | Command3D.DrawMeshInstanced(mesh,
                                        transforms,
                                        material,
                                        instanceCount) ->
            if cameraActive then
              ensureShaderInactive()
              Raylib.BeginShaderMode instancedShader
              shaderActive <- true
              activeVariant <- 2

              if lightsDirty || instanced.LightsDirty then
                uploadLights(instancedShader, &instanced, lights, maxPt, maxSp)
                instanced.LightsDirty <- false

              setShaderVec3
                instancedShader
                instanced.Locs.CameraPos
                currentCamera.Position

              setShaderInt instancedShader instanced.Locs.Shadow.Pass 0

              setMaterialUniforms(
                instancedShader,
                &instanced.Locs.Material,
                &material,
                Matrix4x4.Identity
              )

              let key = MaterialKeyV2.fromMaterial3D &material

              let mat =
                getOrCreate(&instanced, instancedShader, &material, &key)

              Raylib.DrawMeshInstanced(mesh, mat, transforms, instanceCount)
              ensureShaderInactive()

          | Command3D.DrawBillboardBatch(textures,
                                         positions,
                                         sizes,
                                         colors,
                                         count) ->
            if cameraActive then
              ensureShaderInactive()
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

          | Command3D.SetAmbientLight light ->
            lights.Ambient.Clear()
            lights.Ambient.Add light
            lightsDirty <- true

          | Command3D.AddDirectionalLight light ->
            lights.DirLights.Add light
            lightsDirty <- true

          | Command3D.AddPointLight light ->
            lights.PointLights.Add light
            lightsDirty <- true

          | Command3D.AddSpotLight light ->
            lights.SpotLights.Add light
            lightsDirty <- true

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

          | Command3D.SetShadowOrigin _ -> ()
          | Command3D.EnableShadows -> ()
          | Command3D.DisableShadows -> ()

        // End remaining shader/camera state after dispatch
        if shaderActive then
          Raylib.EndShaderMode()

        if cameraActive then
          Raylib.EndMode3D()

      // ── Dispatch: direct or via scene RT for post-process ──
      match ppConfig.Passes with
      | ValueNone
      | ValueSome [||] ->
        // No post-process — dispatch directly to default framebuffer
        dispatchForwardPass()
      | _ ->
        // Post-process enabled — render into scene RT first
        let sceneRT = rtPool.Acquire(gameCtx.WindowWidth, gameCtx.WindowHeight)
        Raylib.BeginTextureMode sceneRT
        Raylib.ClearBackground Color.Black
        dispatchForwardPass()
        Raylib.EndTextureMode()
        applyPostProcess gameCtx sceneRT rtPool

      if atlasCfg.ShowDebugOverlay then
        shadowAtlas.RenderDebugOverlay(
          gameCtx.WindowWidth,
          gameCtx.WindowHeight
        )
