#nowarn 9

namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ------------------------------------------------------------------
// NativePtr helpers — void* with DisableRuntimeMarshalling requires
// explicit fixed + NativePtr.toVoidPtr.
// ------------------------------------------------------------------
[<AutoOpen>]
module private NativeHelpers =

  let setShaderInt (shader: Shader) (loc: int) (value: int) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Int
    )

  let setShaderFloat (shader: Shader) (loc: int) (value: float32) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Float
    )

  let setShaderVec3 (shader: Shader) (loc: int) (v: Vector3) =
    use p = fixed &v

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec3
    )

  let setShaderVec4 (shader: Shader) (loc: int) (v: Vector4) =
    use p = fixed &v

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec4
    )

  let setShaderVec2 (shader: Shader) (loc: int) (v: Vector2) =
    use p = fixed &v

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec2
    )

  let rlSetUniformInt (loc: int) (value: int) =
    use p = fixed &value

    Rlgl.SetUniform(
      loc,
      NativePtr.toVoidPtr p,
      int ShaderUniformDataType.Int,
      1
    )

// ------------------------------------------------------------------
// Material Cache Key
// ------------------------------------------------------------------

[<Struct>]
type private MaterialKey = {
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

module private MaterialKey =
  let fromMaterial3D(mat: Material3D) : MaterialKey = {
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
// Normal Matrix Helper
// ------------------------------------------------------------------

[<AutoOpen>]
module private NormalMatrixHelpers =

  /// Pre-computes the normal matrix (inverse-transpose of model matrix) on the CPU
  /// instead of computing transpose(inverse(matModel)) per-vertex in the shader.
  let computeNormalMatrix(model: Matrix4x4) =
    let mutable inv = Matrix4x4.Identity
    Matrix4x4.Invert(model, &inv) |> ignore
    Matrix4x4.Transpose(inv)

// ------------------------------------------------------------------
// Shadow Configuration (uses ShadowAtlas types from ShadowAtlas.fs)
// ------------------------------------------------------------------

// ------------------------------------------------------------------
// Internal Context
// ------------------------------------------------------------------

type private PipelineContext
  (
    forwardShader: Shader,
    instancedShader: Shader,
    skinnedShader: Shader,
    materialCache: Dictionary<MaterialKey, Material>,
    maxPointLights: int,
    maxSpotLights: int,
    maxShadowCasters: int
  ) =

  let mutable gameCtx = Unchecked.defaultof<GameContext>
  let mutable cameraActive = false
  let mutable currentCamera = Unchecked.defaultof<Camera3D>
  let mutable shaderActive = false

  let ambient = ResizeArray<AmbientLight3D> 1
  let dirLights = ResizeArray<DirectionalLight3D> 1
  let pointLights = ResizeArray<PointLight3D> maxPointLights
  let spotLights = ResizeArray<SpotLight3D> maxSpotLights
  let mutable lightsDirty = true
  let mutable instLightsDirty = true

  let mutable activeShadowMap: RenderTexture2D =
    Unchecked.defaultof<RenderTexture2D>

  let mutable activeLightViewProj = Matrix4x4.Identity

  let mutable lastMaterialKey = Unchecked.defaultof<MaterialKey>
  let mutable hasLastMaterial = false
  let mutable lastRaylibMaterial = Unchecked.defaultof<Material>

  let mutable locsCached = false
  let mutable locAlbedoColor = -1
  let mutable locRoughness = -1
  let mutable locMetallic = -1
  let mutable locEmissionColor = -1
  let mutable locOpacity = -1
  let mutable locTiling = -1
  let mutable locUseNormalMap = -1

  let mutable locAmbientColor = -1
  let mutable locAmbientIntensity = -1
  let mutable locDirLightDir = -1
  let mutable locDirLightColor = -1
  let mutable locDirLightIntensity = -1
  let mutable locDirLightCastsShadows = -1
  let mutable locPointLightCount = -1
  let locPointLightPos = Array.zeroCreate<int> maxPointLights
  let locPointLightColor = Array.zeroCreate<int> maxPointLights
  let locPointLightIntensity = Array.zeroCreate<int> maxPointLights
  let locPointLightRadius = Array.zeroCreate<int> maxPointLights
  let locPointLightFalloff = Array.zeroCreate<int> maxPointLights

  let mutable locSpotLightCount = -1
  let locSpotLightPos = Array.zeroCreate<int> maxSpotLights
  let locSpotLightDir = Array.zeroCreate<int> maxSpotLights
  let locSpotLightColor = Array.zeroCreate<int> maxSpotLights
  let locSpotLightIntensity = Array.zeroCreate<int> maxSpotLights
  let locSpotLightRadius = Array.zeroCreate<int> maxSpotLights
  let locSpotLightInnerCutoff = Array.zeroCreate<int> maxSpotLights
  let locSpotLightOuterCutoff = Array.zeroCreate<int> maxSpotLights

  let mutable locShadowMap = -1
  let mutable locCameraPos = -1

  let mutable locNormalMatrix = -1
  let mutable locShadowNormalMatrix = -1
  let mutable locShadowPass = -1
  let mutable locShadowAtlas = -1
  let mutable locShadowCasterCount = -1
  let locShadowViewProjs = Array.zeroCreate<int> maxShadowCasters
  let locShadowUVOffsets = Array.zeroCreate<int> maxShadowCasters
  let locShadowLightPositions = Array.zeroCreate<int> maxShadowCasters
  let locShadowBiases = Array.zeroCreate<int> maxShadowCasters
  let locShadowTypes = Array.zeroCreate<int> maxShadowCasters

  let cacheLocations() =
    if not locsCached then
      locAlbedoColor <- Raylib.GetShaderLocation(forwardShader, "albedoColor")
      locRoughness <- Raylib.GetShaderLocation(forwardShader, "roughness")
      locMetallic <- Raylib.GetShaderLocation(forwardShader, "metallic")

      locEmissionColor <-
        Raylib.GetShaderLocation(forwardShader, "emissionColor")

      locOpacity <- Raylib.GetShaderLocation(forwardShader, "opacity")
      locTiling <- Raylib.GetShaderLocation(forwardShader, "tiling")
      locUseNormalMap <- Raylib.GetShaderLocation(forwardShader, "useNormalMap")
      locNormalMatrix <- Raylib.GetShaderLocation(forwardShader, "normalMatrix")

      locAmbientColor <- Raylib.GetShaderLocation(forwardShader, "ambientColor")

      locAmbientIntensity <-
        Raylib.GetShaderLocation(forwardShader, "ambientIntensity")

      locDirLightDir <- Raylib.GetShaderLocation(forwardShader, "dirLightDir")

      locDirLightColor <-
        Raylib.GetShaderLocation(forwardShader, "dirLightColor")

      locDirLightIntensity <-
        Raylib.GetShaderLocation(forwardShader, "dirLightIntensity")

      locDirLightCastsShadows <-
        Raylib.GetShaderLocation(forwardShader, "dirLightCastsShadows")

      locPointLightCount <-
        Raylib.GetShaderLocation(forwardShader, "pointLightCount")

      for i = 0 to maxPointLights - 1 do
        locPointLightPos[i] <-
          Raylib.GetShaderLocation(forwardShader, $"pointLightPos[{i}]")

        locPointLightColor[i] <-
          Raylib.GetShaderLocation(forwardShader, $"pointLightColor[{i}]")

        locPointLightIntensity[i] <-
          Raylib.GetShaderLocation(forwardShader, $"pointLightIntensity[{i}]")

        locPointLightRadius[i] <-
          Raylib.GetShaderLocation(forwardShader, $"pointLightRadius[{i}]")

        locPointLightFalloff[i] <-
          Raylib.GetShaderLocation(forwardShader, $"pointLightFalloff[{i}]")

      locSpotLightCount <-
        Raylib.GetShaderLocation(forwardShader, "spotLightCount")

      for i = 0 to maxSpotLights - 1 do
        locSpotLightPos[i] <-
          Raylib.GetShaderLocation(forwardShader, $"spotLightPos[{i}]")

        locSpotLightDir[i] <-
          Raylib.GetShaderLocation(forwardShader, $"spotLightDir[{i}]")

        locSpotLightColor[i] <-
          Raylib.GetShaderLocation(forwardShader, $"spotLightColor[{i}]")

        locSpotLightIntensity[i] <-
          Raylib.GetShaderLocation(forwardShader, $"spotLightIntensity[{i}]")

        locSpotLightRadius[i] <-
          Raylib.GetShaderLocation(forwardShader, $"spotLightRadius[{i}]")

        locSpotLightInnerCutoff[i] <-
          Raylib.GetShaderLocation(forwardShader, $"spotLightInnerCutoff[{i}]")

        locSpotLightOuterCutoff[i] <-
          Raylib.GetShaderLocation(forwardShader, $"spotLightOuterCutoff[{i}]")

      locCameraPos <- Raylib.GetShaderLocation(forwardShader, "cameraPos")
      locShadowMap <- Raylib.GetShaderLocation(forwardShader, "shadowMap")

      // Set shadowAtlas sampler to texture unit 15 to avoid material overrides
      locShadowAtlas <- Raylib.GetShaderLocation(forwardShader, "shadowAtlas")
      rlSetUniformInt locShadowAtlas 15

      locShadowPass <- Raylib.GetShaderLocation(forwardShader, "shadowPass")

      locShadowCasterCount <-
        Raylib.GetShaderLocation(forwardShader, "shadowCasterCount")

      for i = 0 to maxShadowCasters - 1 do
        locShadowViewProjs[i] <-
          Raylib.GetShaderLocation(forwardShader, $"shadowViewProjs[{i}]")

        locShadowUVOffsets[i] <-
          Raylib.GetShaderLocation(forwardShader, $"shadowUVOffsets[{i}]")

        locShadowLightPositions[i] <-
          Raylib.GetShaderLocation(forwardShader, $"shadowLightPositions[{i}]")

        locShadowBiases[i] <-
          Raylib.GetShaderLocation(forwardShader, $"shadowBiases[{i}]")

        locShadowTypes[i] <-
          Raylib.GetShaderLocation(forwardShader, $"shadowTypes[{i}]")

      locsCached <- true

  // ------------------------------------------------------------------
  // Instanced shader location cache (same fragment shader uniforms,
  // different vertex shader — locations may differ from forwardShader)
  // ------------------------------------------------------------------

  let mutable iLocsCached = false
  let mutable iLocAlbedoColor = -1
  let mutable iLocRoughness = -1
  let mutable iLocMetallic = -1
  let mutable iLocEmissionColor = -1
  let mutable iLocOpacity = -1
  let mutable iLocTiling = -1
  let mutable iLocUseNormalMap = -1

  let mutable iLocAmbientColor = -1
  let mutable iLocAmbientIntensity = -1
  let mutable iLocDirLightDir = -1
  let mutable iLocDirLightColor = -1
  let mutable iLocDirLightIntensity = -1
  let mutable iLocDirLightCastsShadows = -1
  let mutable iLocPointLightCount = -1
  let iLocPointLightPos = Array.zeroCreate<int> maxPointLights
  let iLocPointLightColor = Array.zeroCreate<int> maxPointLights
  let iLocPointLightIntensity = Array.zeroCreate<int> maxPointLights
  let iLocPointLightRadius = Array.zeroCreate<int> maxPointLights
  let iLocPointLightFalloff = Array.zeroCreate<int> maxPointLights

  let mutable iLocSpotLightCount = -1
  let iLocSpotLightPos = Array.zeroCreate<int> maxSpotLights
  let iLocSpotLightDir = Array.zeroCreate<int> maxSpotLights
  let iLocSpotLightColor = Array.zeroCreate<int> maxSpotLights
  let iLocSpotLightIntensity = Array.zeroCreate<int> maxSpotLights
  let iLocSpotLightRadius = Array.zeroCreate<int> maxSpotLights
  let iLocSpotLightInnerCutoff = Array.zeroCreate<int> maxSpotLights
  let iLocSpotLightOuterCutoff = Array.zeroCreate<int> maxSpotLights

  let mutable iLocCameraPos = -1
  let mutable iLocNormalMatrix = -1
  let mutable iLocShadowPass = -1
  let mutable iLocShadowAtlas = -1
  let mutable iLocShadowCasterCount = -1
  let iLocShadowViewProjs = Array.zeroCreate<int> maxShadowCasters
  let iLocShadowUVOffsets = Array.zeroCreate<int> maxShadowCasters
  let iLocShadowLightPositions = Array.zeroCreate<int> maxShadowCasters
  let iLocShadowBiases = Array.zeroCreate<int> maxShadowCasters
  let iLocShadowTypes = Array.zeroCreate<int> maxShadowCasters

  let cacheInstancedLocations() =
    if not iLocsCached then
      iLocAlbedoColor <-
        Raylib.GetShaderLocation(instancedShader, "albedoColor")

      iLocRoughness <- Raylib.GetShaderLocation(instancedShader, "roughness")
      iLocMetallic <- Raylib.GetShaderLocation(instancedShader, "metallic")

      iLocEmissionColor <-
        Raylib.GetShaderLocation(instancedShader, "emissionColor")

      iLocOpacity <- Raylib.GetShaderLocation(instancedShader, "opacity")
      iLocTiling <- Raylib.GetShaderLocation(instancedShader, "tiling")

      iLocUseNormalMap <-
        Raylib.GetShaderLocation(instancedShader, "useNormalMap")

      iLocNormalMatrix <-
        Raylib.GetShaderLocation(instancedShader, "normalMatrix")

      iLocAmbientColor <-
        Raylib.GetShaderLocation(instancedShader, "ambientColor")

      iLocAmbientIntensity <-
        Raylib.GetShaderLocation(instancedShader, "ambientIntensity")

      iLocDirLightDir <-
        Raylib.GetShaderLocation(instancedShader, "dirLightDir")

      iLocDirLightColor <-
        Raylib.GetShaderLocation(instancedShader, "dirLightColor")

      iLocDirLightIntensity <-
        Raylib.GetShaderLocation(instancedShader, "dirLightIntensity")

      iLocDirLightCastsShadows <-
        Raylib.GetShaderLocation(instancedShader, "dirLightCastsShadows")

      iLocPointLightCount <-
        Raylib.GetShaderLocation(instancedShader, "pointLightCount")

      for i = 0 to maxPointLights - 1 do
        iLocPointLightPos[i] <-
          Raylib.GetShaderLocation(instancedShader, $"pointLightPos[{i}]")

        iLocPointLightColor[i] <-
          Raylib.GetShaderLocation(instancedShader, $"pointLightColor[{i}]")

        iLocPointLightIntensity[i] <-
          Raylib.GetShaderLocation(instancedShader, $"pointLightIntensity[{i}]")

        iLocPointLightRadius[i] <-
          Raylib.GetShaderLocation(instancedShader, $"pointLightRadius[{i}]")

        iLocPointLightFalloff[i] <-
          Raylib.GetShaderLocation(instancedShader, $"pointLightFalloff[{i}]")

      iLocSpotLightCount <-
        Raylib.GetShaderLocation(instancedShader, "spotLightCount")

      for i = 0 to maxSpotLights - 1 do
        iLocSpotLightPos[i] <-
          Raylib.GetShaderLocation(instancedShader, $"spotLightPos[{i}]")

        iLocSpotLightDir[i] <-
          Raylib.GetShaderLocation(instancedShader, $"spotLightDir[{i}]")

        iLocSpotLightColor[i] <-
          Raylib.GetShaderLocation(instancedShader, $"spotLightColor[{i}]")

        iLocSpotLightIntensity[i] <-
          Raylib.GetShaderLocation(instancedShader, $"spotLightIntensity[{i}]")

        iLocSpotLightRadius[i] <-
          Raylib.GetShaderLocation(instancedShader, $"spotLightRadius[{i}]")

        iLocSpotLightInnerCutoff[i] <-
          Raylib.GetShaderLocation(
            instancedShader,
            $"spotLightInnerCutoff[{i}]"
          )

        iLocSpotLightOuterCutoff[i] <-
          Raylib.GetShaderLocation(
            instancedShader,
            $"spotLightOuterCutoff[{i}]"
          )

      iLocCameraPos <- Raylib.GetShaderLocation(instancedShader, "cameraPos")
      iLocShadowPass <- Raylib.GetShaderLocation(instancedShader, "shadowPass")

      iLocShadowAtlas <-
        Raylib.GetShaderLocation(instancedShader, "shadowAtlas")

      rlSetUniformInt iLocShadowAtlas 15

      iLocShadowCasterCount <-
        Raylib.GetShaderLocation(instancedShader, "shadowCasterCount")

      for i = 0 to maxShadowCasters - 1 do
        iLocShadowViewProjs[i] <-
          Raylib.GetShaderLocation(instancedShader, $"shadowViewProjs[{i}]")

        iLocShadowUVOffsets[i] <-
          Raylib.GetShaderLocation(instancedShader, $"shadowUVOffsets[{i}]")

        iLocShadowLightPositions[i] <-
          Raylib.GetShaderLocation(
            instancedShader,
            $"shadowLightPositions[{i}]"
          )

        iLocShadowBiases[i] <-
          Raylib.GetShaderLocation(instancedShader, $"shadowBiases[{i}]")

        iLocShadowTypes[i] <-
          Raylib.GetShaderLocation(instancedShader, $"shadowTypes[{i}]")

      iLocsCached <- true

  // ------------------------------------------------------------------
  // Skinned shader location cache (bone uniforms + same fragment uniforms)
  // ------------------------------------------------------------------

  let mutable sLocsCached = false
  let mutable sLocAlbedoColor = -1
  let mutable sLocRoughness = -1
  let mutable sLocMetallic = -1
  let mutable sLocEmissionColor = -1
  let mutable sLocOpacity = -1
  let mutable sLocTiling = -1
  let mutable sLocUseNormalMap = -1

  let mutable sLocAmbientColor = -1
  let mutable sLocAmbientIntensity = -1
  let mutable sLocDirLightDir = -1
  let mutable sLocDirLightColor = -1
  let mutable sLocDirLightIntensity = -1
  let mutable sLocDirLightCastsShadows = -1
  let mutable sLocPointLightCount = -1
  let sLocPointLightPos = Array.zeroCreate<int> maxPointLights
  let sLocPointLightColor = Array.zeroCreate<int> maxPointLights
  let sLocPointLightIntensity = Array.zeroCreate<int> maxPointLights
  let sLocPointLightRadius = Array.zeroCreate<int> maxPointLights
  let sLocPointLightFalloff = Array.zeroCreate<int> maxPointLights

  let mutable sLocSpotLightCount = -1
  let sLocSpotLightPos = Array.zeroCreate<int> maxSpotLights
  let sLocSpotLightDir = Array.zeroCreate<int> maxSpotLights
  let sLocSpotLightColor = Array.zeroCreate<int> maxSpotLights
  let sLocSpotLightIntensity = Array.zeroCreate<int> maxSpotLights
  let sLocSpotLightRadius = Array.zeroCreate<int> maxSpotLights
  let sLocSpotLightInnerCutoff = Array.zeroCreate<int> maxSpotLights
  let sLocSpotLightOuterCutoff = Array.zeroCreate<int> maxSpotLights

  let mutable sLocCameraPos = -1
  let mutable sLocNormalMatrix = -1
  let mutable sLocShadowPass = -1
  let mutable sLocShadowAtlas = -1
  let mutable sLocShadowCasterCount = -1
  let sLocShadowViewProjs = Array.zeroCreate<int> maxShadowCasters
  let sLocShadowUVOffsets = Array.zeroCreate<int> maxShadowCasters
  let sLocShadowLightPositions = Array.zeroCreate<int> maxShadowCasters
  let sLocShadowBiases = Array.zeroCreate<int> maxShadowCasters
  let sLocShadowTypes = Array.zeroCreate<int> maxShadowCasters

  let mutable sLocBones = -1

  let mutable skinnedLightsDirty = true

  let cacheSkinnedLocations() =
    if not sLocsCached then
      sLocAlbedoColor <- Raylib.GetShaderLocation(skinnedShader, "albedoColor")
      sLocRoughness <- Raylib.GetShaderLocation(skinnedShader, "roughness")
      sLocMetallic <- Raylib.GetShaderLocation(skinnedShader, "metallic")

      sLocEmissionColor <-
        Raylib.GetShaderLocation(skinnedShader, "emissionColor")

      sLocOpacity <- Raylib.GetShaderLocation(skinnedShader, "opacity")
      sLocTiling <- Raylib.GetShaderLocation(skinnedShader, "tiling")

      sLocUseNormalMap <-
        Raylib.GetShaderLocation(skinnedShader, "useNormalMap")

      sLocNormalMatrix <-
        Raylib.GetShaderLocation(skinnedShader, "normalMatrix")

      sLocAmbientColor <-
        Raylib.GetShaderLocation(skinnedShader, "ambientColor")

      sLocAmbientIntensity <-
        Raylib.GetShaderLocation(skinnedShader, "ambientIntensity")

      sLocDirLightDir <- Raylib.GetShaderLocation(skinnedShader, "dirLightDir")

      sLocDirLightColor <-
        Raylib.GetShaderLocation(skinnedShader, "dirLightColor")

      sLocDirLightIntensity <-
        Raylib.GetShaderLocation(skinnedShader, "dirLightIntensity")

      sLocDirLightCastsShadows <-
        Raylib.GetShaderLocation(skinnedShader, "dirLightCastsShadows")

      sLocPointLightCount <-
        Raylib.GetShaderLocation(skinnedShader, "pointLightCount")

      for i = 0 to maxPointLights - 1 do
        sLocPointLightPos[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"pointLightPos[{i}]")

        sLocPointLightColor[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"pointLightColor[{i}]")

        sLocPointLightIntensity[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"pointLightIntensity[{i}]")

        sLocPointLightRadius[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"pointLightRadius[{i}]")

        sLocPointLightFalloff[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"pointLightFalloff[{i}]")

      sLocSpotLightCount <-
        Raylib.GetShaderLocation(skinnedShader, "spotLightCount")

      for i = 0 to maxSpotLights - 1 do
        sLocSpotLightPos[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"spotLightPos[{i}]")

        sLocSpotLightDir[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"spotLightDir[{i}]")

        sLocSpotLightColor[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"spotLightColor[{i}]")

        sLocSpotLightIntensity[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"spotLightIntensity[{i}]")

        sLocSpotLightRadius[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"spotLightRadius[{i}]")

        sLocSpotLightInnerCutoff[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"spotLightInnerCutoff[{i}]")

        sLocSpotLightOuterCutoff[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"spotLightOuterCutoff[{i}]")

      sLocCameraPos <- Raylib.GetShaderLocation(skinnedShader, "cameraPos")
      sLocShadowPass <- Raylib.GetShaderLocation(skinnedShader, "shadowPass")

      sLocShadowAtlas <- Raylib.GetShaderLocation(skinnedShader, "shadowAtlas")

      rlSetUniformInt sLocShadowAtlas 15

      sLocShadowCasterCount <-
        Raylib.GetShaderLocation(skinnedShader, "shadowCasterCount")

      for i = 0 to maxShadowCasters - 1 do
        sLocShadowViewProjs[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"shadowViewProjs[{i}]")

        sLocShadowUVOffsets[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"shadowUVOffsets[{i}]")

        sLocShadowLightPositions[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"shadowLightPositions[{i}]")

        sLocShadowBiases[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"shadowBiases[{i}]")

        sLocShadowTypes[i] <-
          Raylib.GetShaderLocation(skinnedShader, $"shadowTypes[{i}]")

      sLocBones <- Raylib.GetShaderLocation(skinnedShader, "boneMatrices[0]")
      sLocsCached <- true

  let colorToVec3(c: Color) =
    Vector3(float32 c.R / 255.0f, float32 c.G / 255.0f, float32 c.B / 255.0f)

  let colorToVec4(c: Color) =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  let uploadLightsInstanced() =
    cacheInstancedLocations()

    match ambient.Count with
    | 0 ->
      setShaderVec3 instancedShader iLocAmbientColor Vector3.Zero
      setShaderFloat instancedShader iLocAmbientIntensity 0.0f
    | _ ->
      let a = ambient[0]
      setShaderVec3 instancedShader iLocAmbientColor (colorToVec3 a.Color)
      setShaderFloat instancedShader iLocAmbientIntensity a.Intensity

    match dirLights.Count with
    | 0 ->
      setShaderVec3 instancedShader iLocDirLightDir Vector3.Zero
      setShaderVec3 instancedShader iLocDirLightColor Vector3.Zero
      setShaderFloat instancedShader iLocDirLightIntensity 0.0f
      setShaderInt instancedShader iLocDirLightCastsShadows 0
    | _ ->
      let d = dirLights[0]
      setShaderVec3 instancedShader iLocDirLightDir d.Direction
      setShaderVec3 instancedShader iLocDirLightColor (colorToVec3 d.Color)
      setShaderFloat instancedShader iLocDirLightIntensity d.Intensity

      setShaderInt
        instancedShader
        iLocDirLightCastsShadows
        (if d.CastsShadows then 1 else 0)

    let ptCount = min pointLights.Count maxPointLights
    setShaderInt instancedShader iLocPointLightCount ptCount

    for i = 0 to ptCount - 1 do
      let l = pointLights[i]
      setShaderVec3 instancedShader iLocPointLightPos[i] l.Position
      setShaderVec3 instancedShader iLocPointLightColor[i] (colorToVec3 l.Color)
      setShaderFloat instancedShader iLocPointLightIntensity[i] l.Intensity
      setShaderFloat instancedShader iLocPointLightRadius[i] l.Radius
      setShaderFloat instancedShader iLocPointLightFalloff[i] l.Falloff

    let spCount = min spotLights.Count maxSpotLights
    setShaderInt instancedShader iLocSpotLightCount spCount

    for i = 0 to spCount - 1 do
      let s: SpotLight3D = spotLights[i]
      setShaderVec3 instancedShader iLocSpotLightPos[i] s.Position
      setShaderVec3 instancedShader iLocSpotLightDir[i] s.Direction
      setShaderVec3 instancedShader iLocSpotLightColor[i] (colorToVec3 s.Color)
      setShaderFloat instancedShader iLocSpotLightIntensity[i] s.Intensity
      setShaderFloat instancedShader iLocSpotLightRadius[i] s.Radius
      setShaderFloat instancedShader iLocSpotLightInnerCutoff[i] s.InnerCutoff
      setShaderFloat instancedShader iLocSpotLightOuterCutoff[i] s.OuterCutoff

    instLightsDirty <- false

  let setMaterialUniformsInstanced
    (normalMatrix: Matrix4x4)
    (mat3d: Material3D)
    =
    cacheInstancedLocations()

    setShaderVec4
      instancedShader
      iLocAlbedoColor
      (colorToVec4 mat3d.AlbedoColor)

    setShaderFloat instancedShader iLocRoughness mat3d.Roughness
    setShaderFloat instancedShader iLocMetallic mat3d.Metallic

    setShaderVec4
      instancedShader
      iLocEmissionColor
      (colorToVec4 mat3d.EmissionColor)

    setShaderFloat instancedShader iLocOpacity mat3d.Opacity
    setShaderVec2 instancedShader iLocTiling mat3d.Tiling

    let useNormal =
      match mat3d.NormalMap with
      | ValueSome _ -> 1
      | ValueNone -> 0

    setShaderInt instancedShader iLocUseNormalMap useNormal

    Raylib.SetShaderValueMatrix(instancedShader, iLocNormalMatrix, normalMatrix)

  let mutable instancedMaterialCache = Dictionary<MaterialKey, Material>()
  let mutable lastInstancedMaterialKey = Unchecked.defaultof<MaterialKey>
  let mutable hasLastInstancedMaterial = false
  let mutable lastInstancedRaylibMaterial = Unchecked.defaultof<Material>

  let getOrCreateInstancedMaterial(mat3d: Material3D) : Material =
    let key = MaterialKey.fromMaterial3D mat3d

    if hasLastInstancedMaterial && key = lastInstancedMaterialKey then
      lastInstancedRaylibMaterial
    else
      match instancedMaterialCache.TryGetValue key with
      | true, mat ->
        lastInstancedMaterialKey <- key
        lastInstancedRaylibMaterial <- mat
        hasLastInstancedMaterial <- true
        mat
      | false, _ ->

      let mutable mat = Raylib.LoadMaterialDefault()
      mat.Shader <- instancedShader

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

      instancedMaterialCache[key] <- mat
      lastInstancedMaterialKey <- key
      lastInstancedRaylibMaterial <- mat
      hasLastInstancedMaterial <- true
      mat

  let mutable skinnedMaterialCache = Dictionary<MaterialKey, Material>()
  let mutable lastSkinnedMaterialKey = Unchecked.defaultof<MaterialKey>
  let mutable hasLastSkinnedMaterial = false
  let mutable lastSkinnedRaylibMaterial = Unchecked.defaultof<Material>

  let getOrCreateSkinnedMaterial(mat3d: Material3D) : Material =
    let key = MaterialKey.fromMaterial3D mat3d

    if hasLastSkinnedMaterial && key = lastSkinnedMaterialKey then
      lastSkinnedRaylibMaterial
    else
      match skinnedMaterialCache.TryGetValue key with
      | true, mat ->
        lastSkinnedMaterialKey <- key
        lastSkinnedRaylibMaterial <- mat
        hasLastSkinnedMaterial <- true
        mat
      | false, _ ->

      let mutable mat = Raylib.LoadMaterialDefault()
      mat.Shader <- skinnedShader

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

      skinnedMaterialCache[key] <- mat
      lastSkinnedMaterialKey <- key
      lastSkinnedRaylibMaterial <- mat
      hasLastSkinnedMaterial <- true
      mat

  let uploadLightsSkinned() =
    cacheSkinnedLocations()

    match ambient.Count with
    | 0 ->
      setShaderVec3 skinnedShader sLocAmbientColor Vector3.Zero
      setShaderFloat skinnedShader sLocAmbientIntensity 0.0f
    | _ ->
      let a = ambient[0]
      setShaderVec3 skinnedShader sLocAmbientColor (colorToVec3 a.Color)
      setShaderFloat skinnedShader sLocAmbientIntensity a.Intensity

    match dirLights.Count with
    | 0 ->
      setShaderVec3 skinnedShader sLocDirLightDir Vector3.Zero
      setShaderVec3 skinnedShader sLocDirLightColor Vector3.Zero
      setShaderFloat skinnedShader sLocDirLightIntensity 0.0f
      setShaderInt skinnedShader sLocDirLightCastsShadows 0
    | _ ->
      let d = dirLights[0]
      setShaderVec3 skinnedShader sLocDirLightDir d.Direction
      setShaderVec3 skinnedShader sLocDirLightColor (colorToVec3 d.Color)
      setShaderFloat skinnedShader sLocDirLightIntensity d.Intensity

      setShaderInt
        skinnedShader
        sLocDirLightCastsShadows
        (if d.CastsShadows then 1 else 0)

    let ptCount = min pointLights.Count maxPointLights
    setShaderInt skinnedShader sLocPointLightCount ptCount

    for i = 0 to ptCount - 1 do
      let l = pointLights[i]
      setShaderVec3 skinnedShader sLocPointLightPos[i] l.Position
      setShaderVec3 skinnedShader sLocPointLightColor[i] (colorToVec3 l.Color)
      setShaderFloat skinnedShader sLocPointLightIntensity[i] l.Intensity
      setShaderFloat skinnedShader sLocPointLightRadius[i] l.Radius
      setShaderFloat skinnedShader sLocPointLightFalloff[i] l.Falloff

    let spCount = min spotLights.Count maxSpotLights
    setShaderInt skinnedShader sLocSpotLightCount spCount

    for i = 0 to spCount - 1 do
      let s: SpotLight3D = spotLights[i]
      setShaderVec3 skinnedShader sLocSpotLightPos[i] s.Position
      setShaderVec3 skinnedShader sLocSpotLightDir[i] s.Direction
      setShaderVec3 skinnedShader sLocSpotLightColor[i] (colorToVec3 s.Color)
      setShaderFloat skinnedShader sLocSpotLightIntensity[i] s.Intensity
      setShaderFloat skinnedShader sLocSpotLightRadius[i] s.Radius
      setShaderFloat skinnedShader sLocSpotLightInnerCutoff[i] s.InnerCutoff
      setShaderFloat skinnedShader sLocSpotLightOuterCutoff[i] s.OuterCutoff

    skinnedLightsDirty <- false

  let setMaterialUniformsSkinned (normalMatrix: Matrix4x4) (mat3d: Material3D) =
    cacheSkinnedLocations()

    setShaderVec4 skinnedShader sLocAlbedoColor (colorToVec4 mat3d.AlbedoColor)

    setShaderFloat skinnedShader sLocRoughness mat3d.Roughness
    setShaderFloat skinnedShader sLocMetallic mat3d.Metallic

    setShaderVec4
      skinnedShader
      sLocEmissionColor
      (colorToVec4 mat3d.EmissionColor)

    setShaderFloat skinnedShader sLocOpacity mat3d.Opacity
    setShaderVec2 skinnedShader sLocTiling mat3d.Tiling

    let useNormal =
      match mat3d.NormalMap with
      | ValueSome _ -> 1
      | ValueNone -> 0

    setShaderInt skinnedShader sLocUseNormalMap useNormal

    Raylib.SetShaderValueMatrix(skinnedShader, sLocNormalMatrix, normalMatrix)

  let uploadBoneMatrices(bones: Matrix4x4[]) =
    cacheSkinnedLocations()

    let count = min bones.Length 128

    for i = 0 to count - 1 do
      Raylib.SetShaderValueMatrix(skinnedShader, sLocBones + i, bones[i])

  let ensureShaderActive() =
    if not shaderActive then
      Raylib.BeginShaderMode forwardShader
      shaderActive <- true

  let ensureShaderInactive() =
    if shaderActive then
      Raylib.EndShaderMode()
      shaderActive <- false

  let uploadLights() =
    cacheLocations()
    ensureShaderActive()

    match ambient.Count with
    | 0 ->
      setShaderVec3 forwardShader locAmbientColor Vector3.Zero
      setShaderFloat forwardShader locAmbientIntensity 0.0f
    | _ ->
      let a = ambient[0]
      setShaderVec3 forwardShader locAmbientColor (colorToVec3 a.Color)
      setShaderFloat forwardShader locAmbientIntensity a.Intensity

    match dirLights.Count with
    | 0 ->
      setShaderVec3 forwardShader locDirLightDir Vector3.Zero
      setShaderVec3 forwardShader locDirLightColor Vector3.Zero
      setShaderFloat forwardShader locDirLightIntensity 0.0f
      setShaderInt forwardShader locDirLightCastsShadows 0
    | _ ->
      let d = dirLights[0]
      setShaderVec3 forwardShader locDirLightDir d.Direction
      setShaderVec3 forwardShader locDirLightColor (colorToVec3 d.Color)
      setShaderFloat forwardShader locDirLightIntensity d.Intensity

      setShaderInt
        forwardShader
        locDirLightCastsShadows
        (if d.CastsShadows then 1 else 0)

    let ptCount = min pointLights.Count maxPointLights
    setShaderInt forwardShader locPointLightCount ptCount

    for i = 0 to ptCount - 1 do
      let l = pointLights[i]
      setShaderVec3 forwardShader locPointLightPos[i] l.Position
      setShaderVec3 forwardShader locPointLightColor[i] (colorToVec3 l.Color)
      setShaderFloat forwardShader locPointLightIntensity[i] l.Intensity
      setShaderFloat forwardShader locPointLightRadius[i] l.Radius
      setShaderFloat forwardShader locPointLightFalloff[i] l.Falloff

    let spCount = min spotLights.Count maxSpotLights
    setShaderInt forwardShader locSpotLightCount spCount

    for i = 0 to spCount - 1 do
      let s: SpotLight3D = spotLights[i]
      setShaderVec3 forwardShader locSpotLightPos[i] s.Position
      setShaderVec3 forwardShader locSpotLightDir[i] s.Direction
      setShaderVec3 forwardShader locSpotLightColor[i] (colorToVec3 s.Color)
      setShaderFloat forwardShader locSpotLightIntensity[i] s.Intensity
      setShaderFloat forwardShader locSpotLightRadius[i] s.Radius
      setShaderFloat forwardShader locSpotLightInnerCutoff[i] s.InnerCutoff
      setShaderFloat forwardShader locSpotLightOuterCutoff[i] s.OuterCutoff

    lightsDirty <- false

  let getOrCreateMaterial(mat3d: Material3D) : Material =
    let key = MaterialKey.fromMaterial3D mat3d

    if hasLastMaterial && key = lastMaterialKey then
      lastRaylibMaterial
    else
      match materialCache.TryGetValue key with
      | true, mat ->
        lastMaterialKey <- key
        lastRaylibMaterial <- mat
        hasLastMaterial <- true
        mat
      | false, _ ->

      let mutable mat = Raylib.LoadMaterialDefault()
      mat.Shader <- forwardShader

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

      materialCache[key] <- mat
      lastMaterialKey <- key
      lastRaylibMaterial <- mat
      hasLastMaterial <- true
      mat

  let setMaterialUniforms (normalMatrix: Matrix4x4) (mat3d: Material3D) =
    cacheLocations()
    ensureShaderActive()

    let key = MaterialKey.fromMaterial3D mat3d

    if not hasLastMaterial || key <> lastMaterialKey then
      setShaderVec4 forwardShader locAlbedoColor (colorToVec4 mat3d.AlbedoColor)
      setShaderFloat forwardShader locRoughness mat3d.Roughness
      setShaderFloat forwardShader locMetallic mat3d.Metallic

      setShaderVec4
        forwardShader
        locEmissionColor
        (colorToVec4 mat3d.EmissionColor)

      setShaderFloat forwardShader locOpacity mat3d.Opacity
      setShaderVec2 forwardShader locTiling mat3d.Tiling

      let useNormal =
        match mat3d.NormalMap with
        | ValueSome _ -> 1
        | ValueNone -> 0

      setShaderInt forwardShader locUseNormalMap useNormal
      lastMaterialKey <- key
      hasLastMaterial <- true

    Raylib.SetShaderValueMatrix(forwardShader, locNormalMatrix, normalMatrix)

  let drawMeshCore
    (mesh: Mesh)
    (transform: Matrix4x4)
    (normalMatrix: Matrix4x4)
    (material: Material3D)
    =
    if cameraActive then
      if lightsDirty then
        uploadLights()

      setMaterialUniforms normalMatrix material
      let mat = getOrCreateMaterial material
      Raylib.DrawMesh(mesh, mat, transform)

  // ------------------------------------------------------------------
  // Public rendering methods (called directly by pipeline dispatch)
  // ------------------------------------------------------------------

  member _.GameContext = gameCtx

  member _.BeginCamera(cam: Camera3D) =
    if cameraActive then
      ensureShaderInactive()
      Raylib.EndMode3D()

    Raylib.BeginMode3D cam
    cameraActive <- true
    currentCamera <- cam

  member _.BeginCameraConfig
    (cfg: Camera3DConfig, windowWidth: int, windowHeight: int)
    =
    if cameraActive then
      ensureShaderInactive()
      Raylib.EndMode3D()

    // Apply viewport and clear
    match cfg.Viewport with
    | ValueSome vp ->
      let x = int(vp.X * float32 windowWidth)
      let y = int(vp.Y * float32 windowHeight)
      let w = int(vp.Width * float32 windowWidth)
      let h = int(vp.Height * float32 windowHeight)

      // Scissor-clear so we only clear the viewport region
      match cfg.ClearColor with
      | ValueSome color ->
        Rlgl.EnableScissorTest()
        Rlgl.Scissor(x, y, w, h)
        Raylib.ClearBackground(color)
        Rlgl.DisableScissorTest()
      | ValueNone -> ()

      Rlgl.Viewport(x, y, w, h)
    | ValueNone ->
      // Fullscreen — clear the whole screen
      match cfg.ClearColor with
      | ValueSome color -> Raylib.ClearBackground(color)
      | ValueNone -> ()

    Raylib.BeginMode3D cfg.Camera
    cameraActive <- true
    currentCamera <- cfg.Camera

  member _.EndCamera() =
    if cameraActive then
      ensureShaderInactive()
      Raylib.EndMode3D()
      cameraActive <- false

    // Restore viewport to full window
    Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)

  member _.DrawMesh(mesh, transform, material) =
    let normalMatrix = computeNormalMatrix transform
    drawMeshCore mesh transform normalMatrix material

  member _.DrawBillboard
    (texture: Texture2D, position: Vector3, size: Vector2, color: Color)
    =
    if cameraActive then
      ensureShaderInactive()
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

  member _.DrawLine3D(start: Vector3, finish: Vector3, color: Color) =
    if cameraActive then
      Raylib.DrawLine3D(start, finish, color)

  member _.DrawSkinnedMesh
    (mesh: Mesh, transform: Matrix4x4, material: Material3D, bones: Matrix4x4[])
    =
    if cameraActive then
      ensureShaderInactive()
      Raylib.BeginShaderMode skinnedShader
      shaderActive <- true

      if skinnedLightsDirty then
        uploadLightsSkinned()

      setShaderVec3 skinnedShader sLocCameraPos currentCamera.Position
      setShaderInt skinnedShader sLocShadowPass 0

      let normalMatrix = computeNormalMatrix transform
      setMaterialUniformsSkinned normalMatrix material
      uploadBoneMatrices bones

      let mat = getOrCreateSkinnedMaterial material
      Raylib.DrawMesh(mesh, mat, transform)

      ensureShaderInactive()

  member _.DrawMeshInstanced
    (
      mesh: Mesh,
      transforms: Matrix4x4[],
      material: Material3D,
      instanceCount: int
    ) =
    if cameraActive then
      // Switch to instanced shader
      ensureShaderInactive()
      Raylib.BeginShaderMode instancedShader
      shaderActive <- true

      if instLightsDirty then
        uploadLightsInstanced()

      // Set camera position for PBR
      setShaderVec3 instancedShader iLocCameraPos currentCamera.Position
      setShaderInt instancedShader iLocShadowPass 0

      // Use identity normal matrix — per-instance normals come from
      // mat3(instanceTransform) in the vertex shader
      setMaterialUniformsInstanced Matrix4x4.Identity material
      let mat = getOrCreateInstancedMaterial material
      Raylib.DrawMeshInstanced(mesh, mat, transforms, instanceCount)

      // Switch back to regular shader for subsequent non-instanced draws
      ensureShaderInactive()

  // TODO: Implement proper batching — compute billboard vertices manually and
  // submit in a single rlBegin(RL_QUADS)/rlEnd block with one texture bind,
  // instead of calling DrawBillboardRec per billboard (which flushes separately).
  member _.DrawBillboardBatch
    (
      textures: Texture2D[],
      positions: Vector3[],
      sizes: Vector2[],
      colors: Color[],
      count: int
    ) =
    if cameraActive then
      ensureShaderInactive()
      Rlgl.EnableShader(Rlgl.GetShaderIdDefault())

      for i = 0 to count - 1 do
        let source =
          Rectangle(
            0.0f,
            0.0f,
            float32 textures[i].Width,
            float32 textures[i].Height
          )

        Raylib.DrawBillboardRec(
          currentCamera,
          textures[i],
          source,
          positions[i],
          sizes[i],
          colors[i]
        )

  member _.AddPointLight(light: PointLight3D) =
    pointLights.Add light
    lightsDirty <- true
    instLightsDirty <- true
    skinnedLightsDirty <- true

  member _.AddDirectionalLight(light: DirectionalLight3D) =
    dirLights.Add light
    lightsDirty <- true
    instLightsDirty <- true
    skinnedLightsDirty <- true

  member _.AddSpotLight(light: SpotLight3D) =
    spotLights.Add light
    lightsDirty <- true
    instLightsDirty <- true
    skinnedLightsDirty <- true

  member _.SetAmbientLight(light: AmbientLight3D) =
    ambient.Clear()
    ambient.Add light
    lightsDirty <- true
    instLightsDirty <- true
    skinnedLightsDirty <- true

  member _.DrawImmediate(action: unit -> unit) =
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

  member internal _.WarmMaterial(mat3d: Material3D) =
    getOrCreateMaterial mat3d |> ignore

  member internal _.CacheShadowLocations(shadowShader: Shader) =
    locShadowNormalMatrix <-
      Raylib.GetShaderLocation(shadowShader, "normalMatrix")

  member internal _.UnloadInstancedMaterialCache() =
    for KeyValue(_, mat) in instancedMaterialCache do
      Raylib.UnloadMaterial mat

    instancedMaterialCache.Clear()

    for KeyValue(_, mat) in skinnedMaterialCache do
      Raylib.UnloadMaterial mat

    skinnedMaterialCache.Clear()

  member internal _.CacheInstancedShadowLocations() =
    cacheInstancedLocations() // ensure base locations are cached first
    iLocShadowPass <- Raylib.GetShaderLocation(instancedShader, "shadowPass")
    iLocShadowAtlas <- Raylib.GetShaderLocation(instancedShader, "shadowAtlas")

    iLocShadowCasterCount <-
      Raylib.GetShaderLocation(instancedShader, "shadowCasterCount")

    for i = 0 to maxShadowCasters - 1 do
      iLocShadowViewProjs[i] <-
        Raylib.GetShaderLocation(instancedShader, $"shadowViewProjs[{i}]")

      iLocShadowUVOffsets[i] <-
        Raylib.GetShaderLocation(instancedShader, $"shadowUVOffsets[{i}]")

      iLocShadowLightPositions[i] <-
        Raylib.GetShaderLocation(instancedShader, $"shadowLightPositions[{i}]")

      iLocShadowBiases[i] <-
        Raylib.GetShaderLocation(instancedShader, $"shadowBiases[{i}]")

      iLocShadowTypes[i] <-
        Raylib.GetShaderLocation(instancedShader, $"shadowTypes[{i}]")

  member internal _.LocNormalMatrix = locNormalMatrix
  member internal _.LocShadowNormalMatrix = locShadowNormalMatrix
  member internal _.LocShadowPass = locShadowPass
  member internal _.LocShadowAtlas = locShadowAtlas
  member internal _.LocShadowCasterCount = locShadowCasterCount
  member internal _.LocShadowViewProjs = locShadowViewProjs
  member internal _.LocShadowUVOffsets = locShadowUVOffsets
  member internal _.LocShadowLightPositions = locShadowLightPositions
  member internal _.LocShadowBiases = locShadowBiases
  member internal _.LocShadowTypes = locShadowTypes
  member internal _.LocCameraPos = locCameraPos

  member internal _.ILocShadowPass = iLocShadowPass
  member internal _.ILocShadowAtlas = iLocShadowAtlas
  member internal _.ILocShadowCasterCount = iLocShadowCasterCount
  member internal _.ILocShadowViewProjs = iLocShadowViewProjs
  member internal _.ILocShadowUVOffsets = iLocShadowUVOffsets
  member internal _.ILocShadowLightPositions = iLocShadowLightPositions
  member internal _.ILocShadowBiases = iLocShadowBiases
  member internal _.ILocShadowTypes = iLocShadowTypes
  member internal _.ILocCameraPos = iLocCameraPos

  member internal _.ClearLights() =
    dirLights.Clear()
    pointLights.Clear()
    spotLights.Clear()

  member internal _.DirLights = dirLights
  member internal _.PointLights = pointLights
  member internal _.SpotLights = spotLights

  member internal _.Reset
    (
      gameContext: GameContext,
      shadowFbo: RenderTexture2D,
      lightViewProj: Matrix4x4
    ) =
    gameCtx <- gameContext
    activeShadowMap <- shadowFbo
    activeLightViewProj <- lightViewProj
    ambient.Clear()
    dirLights.Clear()
    pointLights.Clear()
    spotLights.Clear()
    hasLastMaterial <- false
    hasLastSkinnedMaterial <- false
    cameraActive <- false
    shaderActive <- false
    skinnedLightsDirty <- true

  member internal _.EndAll() =
    if shaderActive then
      Raylib.EndShaderMode()
      shaderActive <- false

    if cameraActive then
      Raylib.EndMode3D()
      cameraActive <- false

// ------------------------------------------------------------------
// Shadow Pass Helpers
// ------------------------------------------------------------------

module private ShadowPassHelpers =
  [<Struct>]
  type MeshDraw = {
    Mesh: Mesh
    Transform: Matrix4x4
    Bones: Matrix4x4[] voption
  }

  let collectMeshDraws(buffer: RenderBuffer3D) =
    let pool = System.Buffers.ArrayPool<MeshDraw>.Shared

    // Pre-scan to count actual mesh draws for precise allocation.
    // Geometry between DisableShadows and EnableShadows is excluded.
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
// ForwardPbrPipeline
// ------------------------------------------------------------------

/// <summary>
/// Reference implementation of <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/>.
/// A Forward PBR pipeline with shadow atlas mapping, PBR lighting,
/// material caching, and optional post-processing.
/// </summary>
/// <remarks>
/// This is the <b>reference implementation</b>, not the engine core.
/// It demonstrates how to build a complete forward renderer on top of
/// raylib's low-level primitives while keeping geometry universal.
///
/// Features:
/// <list type="bullet">
///   <item><description>Shadow atlas mapping for multiple shadow-casting lights (directional, point, spot)</description></item>
///   <item><description>Accumulated point and directional lights (configurable max point lights via uniform arrays, default 8)</description></item>
///   <item><description>Material caching: converts <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/> to raylib <c>Material</c> on first use</description></item>
///   <item><description>Post-process via ping-pong render targets</description></item>
///   <item><description>GPU skeletal animation via <c>DrawSkinnedMesh</c> with bone matrix uniforms</description></item>
/// </list>
/// </remarks>
type ForwardPbrPipeline
  (
    ?postProcess: PostProcessConfig3D,
    ?maxPointLights: int,
    ?maxSpotLights: int,
    ?shadowAtlasConfig: ShadowAtlasConfig,
    ?shadowBiasConfig: ShadowBiasConfig
  ) =

  let ppConfig = defaultArg postProcess PostProcessConfig3D.none
  let maxPt = defaultArg maxPointLights 8
  let maxSp = defaultArg maxSpotLights 4
  let atlasCfg = defaultArg shadowAtlasConfig ShadowAtlasConfig.defaults
  let biasCfg = defaultArg shadowBiasConfig ShadowBiasConfig.defaults

  let mutable forwardShader: Shader = Unchecked.defaultof<Shader>
  let mutable instancedShader: Shader = Unchecked.defaultof<Shader>
  let mutable skinnedShader: Shader = Unchecked.defaultof<Shader>
  let mutable depthShadowShader: Shader = Unchecked.defaultof<Shader>
  let mutable depthShadowSkinnedShader: Shader = Unchecked.defaultof<Shader>
  let mutable postProcessShader: Shader = Unchecked.defaultof<Shader>
  let materialCache = Dictionary<MaterialKey, Material>()
  let mutable depthShadowMaterial: Material = Unchecked.defaultof<Material>

  let mutable depthShadowSkinnedMaterial: Material =
    Unchecked.defaultof<Material>

  let mutable shadowAtlas: ShadowAtlas = Unchecked.defaultof<ShadowAtlas>

  let mutable context: PipelineContext = Unchecked.defaultof<PipelineContext>

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

  // ------------------------------------------------------------------
  // IRenderPipeline3D
  // ------------------------------------------------------------------

  interface IRenderPipeline3D with
    member _.Initialize() =
      forwardShader <- Shaders.loadForwardShader maxPt maxSp atlasCfg.MaxCasters

      instancedShader <-
        Shaders.loadForwardInstancedShader maxPt maxSp atlasCfg.MaxCasters

      skinnedShader <-
        Shaders.loadForwardSkinnedShader maxPt maxSp atlasCfg.MaxCasters

      // Tell raylib that the per-instance model transform lives at the
      // `instanceTransform` vertex attribute.  When DrawMeshInstanced is
      // called, raylib uploads the transforms array to this attribute.
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

      context <-
        PipelineContext(
          forwardShader,
          instancedShader,
          skinnedShader,
          materialCache,
          maxPt,
          maxSp,
          atlasCfg.MaxCasters
        )

      context.CacheShadowLocations(depthShadowShader)

    member _.Shutdown() =
      context.UnloadInstancedMaterialCache()

      Raylib.UnloadShader forwardShader
      Raylib.UnloadShader instancedShader
      Raylib.UnloadShader skinnedShader
      Raylib.UnloadShader depthShadowShader
      Raylib.UnloadShader depthShadowSkinnedShader
      Raylib.UnloadShader postProcessShader

      Raylib.UnloadMaterial depthShadowMaterial
      Raylib.UnloadMaterial depthShadowSkinnedMaterial

      for KeyValue(_, mat) in materialCache do
        Raylib.UnloadMaterial mat

      materialCache.Clear()

      if shadowAtlas <> Unchecked.defaultof<ShadowAtlas> then
        shadowAtlas.Shutdown()

    member _.Execute gameCtx buffer rtPool =
      // ------------------------------------------------------------------
      // Pre-pass: collect camera, lights, shadow origin, and mesh draws
      // ------------------------------------------------------------------
      let mutable activeCamera = Unchecked.defaultof<Camera3D>
      let mutable cameraFound = false
      let mutable explicitShadowOrigin = ValueNone

      context.ClearLights()

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.BeginCamera cam ->
          if not cameraFound then
            activeCamera <- cam
            cameraFound <- true
        | Command3D.BeginCameraConfig cfg ->
          if not cameraFound then
            activeCamera <- cfg.Camera
            cameraFound <- true
        | Command3D.SetShadowOrigin origin ->
          explicitShadowOrigin <- ValueSome origin
        | Command3D.AddDirectionalLight light -> context.DirLights.Add light
        | Command3D.AddPointLight light -> context.PointLights.Add light
        | Command3D.AddSpotLight light -> context.SpotLights.Add light
        | Command3D.DrawMesh(_, _, mat) -> context.WarmMaterial(mat)
        | Command3D.DrawSkinnedMesh(_, _, mat, _) -> context.WarmMaterial(mat)
        | Command3D.DrawMeshInstanced(_, _, mat, _) -> context.WarmMaterial(mat)
        | Command3D.DrawModel(model, transform) ->
          let m = model

          for mi = 0 to m.MeshCount - 1 do
            let matIdx = NativePtr.get m.MeshMaterial mi
            let raylibMat = NativePtr.get m.Materials matIdx
            let mat3d = Material3D.fromRaylibMaterial raylibMat
            context.WarmMaterial(mat3d)
        | _ -> ()

      let struct (meshDraws, meshDrawCount) =
        ShadowPassHelpers.collectMeshDraws buffer

      let mutable hasShadowCasters = false

      try
        // ------------------------------------------------------------------
        // Shadow pass - render all shadow casters to atlas
        // ------------------------------------------------------------------

        // Clear previous frame's casters
        shadowAtlas.Clear()

        if cameraFound && meshDrawCount > 0 then
          // Register shadow casters
          for dir in context.DirLights do
            if dir.CastsShadows then
              hasShadowCasters <- true

              // Register caster with atlas
              shadowAtlas.AddCaster(
                ShadowCasterType.Directional,
                Vector3.Zero,
                dir.Direction,
                Vector3.Zero,
                true,
                ValueNone
              )
              |> ignore

          // TODO: Register point light casters
          for pt in context.PointLights do
            if pt.CastsShadows then
              hasShadowCasters <- true

              shadowAtlas.AddCaster(
                ShadowCasterType.Point,
                pt.Position,
                Vector3.Zero,
                Vector3.Zero,
                true,
                pt.ShadowBias
              )
              |> ignore

          for sp in context.SpotLights do
            if sp.CastsShadows then
              hasShadowCasters <- true

              shadowAtlas.AddCaster(
                ShadowCasterType.Spot,
                sp.Position,
                sp.Direction,
                sp.Position + sp.Direction,
                true,
                sp.ShadowBias
              )
              |> ignore

          // Render shadow passes
          if shadowAtlas.Count > 0 then
            // Set shadowPass = 1 for the shadow pass shader
            setShaderInt forwardShader context.LocShadowPass 1

            // Bind atlas FBO once for all casters. Viewport clips rasterization
            // to each caster's region, so they never interfere.
            Raylib.BeginTextureMode(shadowAtlas.Fbo)
            Raylib.ClearBackground(Color.White)

            // Render each caster to its atlas region
            let inline renderShadowRegion
              (regionIndex: int)
              (camera: Camera3D)
              =
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
                    context.LocShadowNormalMatrix,
                    nm
                  )

                  let shadowBoneLoc =
                    Raylib.GetShaderLocation(
                      depthShadowSkinnedShader,
                      "boneMatrices[0]"
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
                    context.LocShadowNormalMatrix,
                    nm
                  )

                  Raylib.DrawMesh(
                    draw.Mesh,
                    depthShadowMaterial,
                    draw.Transform
                  )

              Raylib.EndMode3D()

            for caster in shadowAtlas.Casters do
              if caster.Enabled then
                // Per-caster distance culling: skip shadow if light is too far from camera
                let lightPos =
                  if caster.Type = ShadowCasterType.Directional then
                    activeCamera.Position // directional follows camera
                  else
                    caster.LightPosition

                let distToCamera =
                  (lightPos - activeCamera.Position).LengthSquared()

                let maxShadowDist = 2500.0f // 50^2

                if distToCamera <= maxShadowDist then
                  match caster.Type with
                  | ShadowCasterType.Point ->
                    // Point light: single forward-facing shadow map
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
                    // Spot light: render once with perspective projection
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
                    // Directional light: render once with orthographic projection
                    let lightFromDir = Vector3.Normalize(-caster.LightDirection)

                    let rawOrigin =
                      match explicitShadowOrigin with
                      | ValueSome origin -> origin
                      | ValueNone ->
                        match atlasCfg.OriginStrategy with
                        | ShadowOriginStrategy.CameraTarget ->
                          activeCamera.Target
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

                    // Derive near/far from scene geometry instead of using
                    // raylib's default 0.05/4000 which wastes depth precision.
                    // near: small value to capture geometry close to the light
                    // far: light distance + ortho half-size to cover the entire shadow volume
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

            // Reset viewport to window size
            Rlgl.Viewport(0, 0, gameCtx.WindowWidth, gameCtx.WindowHeight)
            Raylib.EndTextureMode()

            // Set shadowPass = 0 for the main pass
            setShaderInt forwardShader context.LocShadowPass 0
      finally
        System.Buffers.ArrayPool<ShadowPassHelpers.MeshDraw>.Shared
          .Return(meshDraws, false)

      // ------------------------------------------------------------------
      // Forward pass — context is reset each frame, populated by commands
      // ------------------------------------------------------------------
      context.Reset(gameCtx, shadowAtlas.Fbo, Matrix4x4.Identity)

      // Upload shadow atlas uniforms
      if hasShadowCasters && cameraFound then
        shadowAtlas.PrepareUniforms()

        // Upload atlas texture
        if shadowAtlas.Fbo.Depth.Id <> 0u then
          Rlgl.EnableShader(forwardShader.Id)
          Rlgl.ActiveTextureSlot(15)
          Rlgl.EnableTexture(shadowAtlas.Fbo.Depth.Id)
          rlSetUniformInt context.LocShadowAtlas 15
          Rlgl.ActiveTextureSlot(0)

        // Upload per-caster uniforms
        let count = min shadowAtlas.ActiveCasterCount atlasCfg.MaxCasters

        for i = 0 to count - 1 do
          Raylib.SetShaderValueMatrix(
            forwardShader,
            context.LocShadowViewProjs[i],
            shadowAtlas.ViewProjs[i]
          )

          setShaderVec4
            forwardShader
            context.LocShadowUVOffsets[i]
            shadowAtlas.UVOffsets[i]

          setShaderVec3
            forwardShader
            context.LocShadowLightPositions[i]
            shadowAtlas.LightPositions[i]

          setShaderFloat
            forwardShader
            context.LocShadowBiases[i]
            shadowAtlas.Biases[i]

          setShaderInt
            forwardShader
            context.LocShadowTypes[i]
            shadowAtlas.CasterTypes[i]

        setShaderInt
          forwardShader
          context.LocShadowCasterCount
          shadowAtlas.ActiveCasterCount

        setShaderVec3 forwardShader context.LocCameraPos activeCamera.Position
        setShaderInt forwardShader context.LocShadowPass 0

        // ---- Upload the same shadow data to the instanced shader ----
        context.CacheInstancedShadowLocations()

        if shadowAtlas.Fbo.Depth.Id <> 0u then
          Rlgl.EnableShader(instancedShader.Id)
          Rlgl.ActiveTextureSlot(15)
          Rlgl.EnableTexture(shadowAtlas.Fbo.Depth.Id)
          rlSetUniformInt context.ILocShadowAtlas 15
          Rlgl.ActiveTextureSlot(0)

        for i = 0 to count - 1 do
          Raylib.SetShaderValueMatrix(
            instancedShader,
            context.ILocShadowViewProjs[i],
            shadowAtlas.ViewProjs[i]
          )

          setShaderVec4
            instancedShader
            context.ILocShadowUVOffsets[i]
            shadowAtlas.UVOffsets[i]

          setShaderVec3
            instancedShader
            context.ILocShadowLightPositions[i]
            shadowAtlas.LightPositions[i]

          setShaderFloat
            instancedShader
            context.ILocShadowBiases[i]
            shadowAtlas.Biases[i]

          setShaderInt
            instancedShader
            context.ILocShadowTypes[i]
            shadowAtlas.CasterTypes[i]

        setShaderInt
          instancedShader
          context.ILocShadowCasterCount
          shadowAtlas.ActiveCasterCount

        setShaderVec3
          instancedShader
          context.ILocCameraPos
          activeCamera.Position

        setShaderInt instancedShader context.ILocShadowPass 0

      // Render
      let dispatch(cmd: Command3D) =
        match cmd with
        | Command3D.DrawMesh(mesh, transform, material) ->
          context.DrawMesh(mesh, transform, material)
        | Command3D.DrawModel(model, transform) ->
          for mi = 0 to model.MeshCount - 1 do
            let mesh = NativePtr.get model.Meshes mi
            let matIdx = NativePtr.get model.MeshMaterial mi
            let mat = NativePtr.get model.Materials matIdx
            let mat3d = Material3D.fromRaylibMaterial mat
            context.DrawMesh(mesh, transform, mat3d)
        | Command3D.DrawBillboard(texture, position, size, color) ->
          context.DrawBillboard(texture, position, size, color)
        | Command3D.DrawLine3D(start, finish, color) ->
          context.DrawLine3D(start, finish, color)
        | Command3D.DrawSkinnedMesh(mesh, transform, material, bones) ->
          context.DrawSkinnedMesh(mesh, transform, material, bones)
        | Command3D.DrawMeshInstanced(mesh, transforms, material, instanceCount) ->
          context.DrawMeshInstanced(mesh, transforms, material, instanceCount)
        | Command3D.DrawBillboardBatch(textures, positions, sizes, colors, count) ->
          context.DrawBillboardBatch(textures, positions, sizes, colors, count)
        | Command3D.BeginCamera cam -> context.BeginCamera(cam)
        | Command3D.BeginCameraConfig cfg ->
          context.BeginCameraConfig(
            cfg,
            gameCtx.WindowWidth,
            gameCtx.WindowHeight
          )
        | Command3D.EndCamera -> context.EndCamera()
        | Command3D.SetShadowOrigin _ -> () // handled in pre-pass
        | Command3D.EnableShadows -> () // handled in shadow collection
        | Command3D.DisableShadows -> () // handled in shadow collection
        | Command3D.SetAmbientLight light -> context.SetAmbientLight(light)
        | Command3D.AddDirectionalLight light ->
          context.AddDirectionalLight(light)
        | Command3D.AddPointLight light -> context.AddPointLight(light)
        | Command3D.AddSpotLight light -> context.AddSpotLight(light)
        | Command3D.DrawImmediate action -> context.DrawImmediate(action)

      match ppConfig.Passes with
      | ValueNone
      | ValueSome [||] ->
        for i = 0 to buffer.Count - 1 do
          dispatch buffer[i]

        context.EndAll()
      | _ ->
        let sceneRT = rtPool.Acquire(gameCtx.WindowWidth, gameCtx.WindowHeight)
        Raylib.BeginTextureMode(sceneRT)
        Raylib.ClearBackground(Color.Black)

        for i = 0 to buffer.Count - 1 do
          dispatch buffer[i]

        context.EndAll()
        Raylib.EndTextureMode()
        applyPostProcess gameCtx sceneRT rtPool

      // DEBUG: Render shadow atlas as overlay in bottom-right corner
      if atlasCfg.ShowDebugOverlay then
        shadowAtlas.RenderDebugOverlay(
          gameCtx.WindowWidth,
          gameCtx.WindowHeight
        )
