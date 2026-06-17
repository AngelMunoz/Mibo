#nowarn "9"

namespace Mibo.Elmish.Next.Graphics2D.Lighting

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D.Base

/// <summary>
/// Raylib Next light context. Accepts the Core.Next backend-neutral light types
/// and uploads them to the built-in SDF shadow raymarching shader.
/// </summary>
type LightContext2D
  (
    ?litShader: Shader,
    ?litNormalMapShader: Shader,
    ?maxDirLights: int,
    ?maxPointLights: int,
    ?maxOccluders: int,
    ?softness: float32,
    ?maxShadowDistance: float32
  ) =

  let maxDir = defaultArg maxDirLights 4
  let maxPoint = defaultArg maxPointLights 16
  let maxOccluderCount = defaultArg maxOccluders 128

  let litShader =
    defaultArg litShader (Mibo.Elmish.Graphics2D.Lighting.LitShader.load())

  let nmShader =
    defaultArg
      litNormalMapShader
      (Mibo.Elmish.Graphics2D.Lighting.LitShader.loadNormalMap())

  let shadowSoftness = defaultArg softness 0.05f
  let shadowMaxDist = defaultArg maxShadowDistance 5000.0f

  let dirLights = ResizeArray<DirectionalLight2D>()
  let pointLights = ResizeArray<PointLight2D>()
  let occluders = ResizeArray<Occluder2D>()

  let mutable ambientColor: Color = { R = 0uy; G = 0uy; B = 0uy; A = 255uy }

  let colorToVec3(c: Color) =
    Vector3(float32 c.R / 255.0f, float32 c.G / 255.0f, float32 c.B / 255.0f)

  // ------------------------------------------------------------------
  // Uniform locations
  // ------------------------------------------------------------------

  let mutable locsCached = false

  let mutable locAmbientColor = -1
  let mutable locDirCount = -1
  let locDirDirs = Array.zeroCreate<int> maxDir
  let locDirColors = Array.zeroCreate<int> maxDir
  let locDirIntensities = Array.zeroCreate<int> maxDir
  let locDirShadowIdx = Array.zeroCreate<int> maxDir

  let mutable locPointCount = -1
  let locPointPos = Array.zeroCreate<int> maxPoint
  let locPointColors = Array.zeroCreate<int> maxPoint
  let locPointIntensities = Array.zeroCreate<int> maxPoint
  let locPointRadii = Array.zeroCreate<int> maxPoint
  let locPointFalloffs = Array.zeroCreate<int> maxPoint
  let locPointShadowIdx = Array.zeroCreate<int> maxPoint

  let locOccluders = Array.zeroCreate<int> maxOccluderCount
  let mutable locOccluderCount = -1
  let mutable locSoftness = -1
  let mutable locMaxDist = -1
  let mutable locNormalMap = -1

  let mutable nmLocsCached = false

  let mutable nmLocAmbientColor = -1
  let mutable nmLocDirCount = -1
  let nmLocDirDirs = Array.zeroCreate<int> maxDir
  let nmLocDirColors = Array.zeroCreate<int> maxDir
  let nmLocDirIntensities = Array.zeroCreate<int> maxDir
  let nmLocDirShadowIdx = Array.zeroCreate<int> maxDir

  let mutable nmLocPointCount = -1
  let nmLocPointPos = Array.zeroCreate<int> maxPoint
  let nmLocPointColors = Array.zeroCreate<int> maxPoint
  let nmLocPointIntensities = Array.zeroCreate<int> maxPoint
  let nmLocPointRadii = Array.zeroCreate<int> maxPoint
  let nmLocPointFalloffs = Array.zeroCreate<int> maxPoint
  let nmLocPointShadowIdx = Array.zeroCreate<int> maxPoint

  let nmLocOccluders = Array.zeroCreate<int> maxOccluderCount
  let mutable nmLocOccluderCount = -1
  let mutable nmLocSoftness = -1
  let mutable nmLocMaxDist = -1
  let mutable nmLocNormalMap = -1

  let cacheLocations() =
    if not locsCached then
      locAmbientColor <- Raylib.GetShaderLocation(litShader, "ambientColor")
      locDirCount <- Raylib.GetShaderLocation(litShader, "dirLightCount")

      for i = 0 to maxDir - 1 do
        locDirDirs[i] <-
          Raylib.GetShaderLocation(litShader, $"dirLightDirs[{i}]")

        locDirColors[i] <-
          Raylib.GetShaderLocation(litShader, $"dirLightColors[{i}]")

        locDirIntensities[i] <-
          Raylib.GetShaderLocation(litShader, $"dirLightIntensities[{i}]")

        locDirShadowIdx[i] <-
          Raylib.GetShaderLocation(litShader, $"dirLightShadowIdx[{i}]")

      locPointCount <- Raylib.GetShaderLocation(litShader, "pointLightCount")

      for i = 0 to maxPoint - 1 do
        locPointPos[i] <-
          Raylib.GetShaderLocation(litShader, $"pointLightPos[{i}]")

        locPointColors[i] <-
          Raylib.GetShaderLocation(litShader, $"pointLightColors[{i}]")

        locPointIntensities[i] <-
          Raylib.GetShaderLocation(litShader, $"pointLightIntensities[{i}]")

        locPointRadii[i] <-
          Raylib.GetShaderLocation(litShader, $"pointLightRadii[{i}]")

        locPointFalloffs[i] <-
          Raylib.GetShaderLocation(litShader, $"pointLightFalloffs[{i}]")

        locPointShadowIdx[i] <-
          Raylib.GetShaderLocation(litShader, $"pointLightShadowIdx[{i}]")

      for i = 0 to maxOccluderCount - 1 do
        locOccluders[i] <-
          Raylib.GetShaderLocation(litShader, $"occluders[{i}]")

      locOccluderCount <- Raylib.GetShaderLocation(litShader, "occluderCount")
      locSoftness <- Raylib.GetShaderLocation(litShader, "shadowSoftness")
      locMaxDist <- Raylib.GetShaderLocation(litShader, "shadowMaxDistance")
      locNormalMap <- Raylib.GetShaderLocation(litShader, "normalMap")

      locsCached <- true

  let cacheNmLocations() =
    if not nmLocsCached then
      nmLocAmbientColor <- Raylib.GetShaderLocation(nmShader, "ambientColor")
      nmLocDirCount <- Raylib.GetShaderLocation(nmShader, "dirLightCount")

      for i = 0 to maxDir - 1 do
        nmLocDirDirs[i] <-
          Raylib.GetShaderLocation(nmShader, $"dirLightDirs[{i}]")

        nmLocDirColors[i] <-
          Raylib.GetShaderLocation(nmShader, $"dirLightColors[{i}]")

        nmLocDirIntensities[i] <-
          Raylib.GetShaderLocation(nmShader, $"dirLightIntensities[{i}]")

        nmLocDirShadowIdx[i] <-
          Raylib.GetShaderLocation(nmShader, $"dirLightShadowIdx[{i}]")

      nmLocPointCount <- Raylib.GetShaderLocation(nmShader, "pointLightCount")

      for i = 0 to maxPoint - 1 do
        nmLocPointPos[i] <-
          Raylib.GetShaderLocation(nmShader, $"pointLightPos[{i}]")

        nmLocPointColors[i] <-
          Raylib.GetShaderLocation(nmShader, $"pointLightColors[{i}]")

        nmLocPointIntensities[i] <-
          Raylib.GetShaderLocation(nmShader, $"pointLightIntensities[{i}]")

        nmLocPointRadii[i] <-
          Raylib.GetShaderLocation(nmShader, $"pointLightRadii[{i}]")

        nmLocPointFalloffs[i] <-
          Raylib.GetShaderLocation(nmShader, $"pointLightFalloffs[{i}]")

        nmLocPointShadowIdx[i] <-
          Raylib.GetShaderLocation(nmShader, $"pointLightShadowIdx[{i}]")

      for i = 0 to maxOccluderCount - 1 do
        nmLocOccluders[i] <-
          Raylib.GetShaderLocation(nmShader, $"occluders[{i}]")

      nmLocOccluderCount <- Raylib.GetShaderLocation(nmShader, "occluderCount")
      nmLocSoftness <- Raylib.GetShaderLocation(nmShader, "shadowSoftness")
      nmLocMaxDist <- Raylib.GetShaderLocation(nmShader, "shadowMaxDistance")
      nmLocNormalMap <- Raylib.GetShaderLocation(nmShader, "normalMap")

      nmLocsCached <- true

  // ------------------------------------------------------------------
  // Upload helpers (DisableRuntimeMarshalling safe)
  // ------------------------------------------------------------------

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

  let setShaderVec2 (shader: Shader) (loc: int) (value: Vector2) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec2
    )

  let setShaderVec3 (shader: Shader) (loc: int) (value: Vector3) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec3
    )

  let setShaderVec4 (shader: Shader) (loc: int) (value: Vector4) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec4
    )

  let uploadToShader
    (shader: Shader)
    (locAmbientColor: int)
    (locDirCount: int)
    (locDirDirs: int[])
    (locDirColors: int[])
    (locDirIntensities: int[])
    (locDirShadowIdx: int[])
    (locPointCount: int)
    (locPointPos: int[])
    (locPointColors: int[])
    (locPointIntensities: int[])
    (locPointRadii: int[])
    (locPointFalloffs: int[])
    (locPointShadowIdx: int[])
    (locOccluders: int[])
    (locOccluderCount: int)
    (locSoftness: int)
    (locMaxDist: int)
    (shadowsEnabled: bool)
    =
    setShaderVec3 shader locAmbientColor (colorToVec3 ambientColor)

    let dirCount = min dirLights.Count maxDir
    setShaderInt shader locDirCount dirCount

    for i = 0 to dirCount - 1 do
      let l = dirLights[i]
      setShaderVec2 shader locDirDirs[i] l.Direction
      setShaderVec3 shader locDirColors[i] (colorToVec3 l.Color)
      setShaderFloat shader locDirIntensities[i] l.Intensity
      setShaderInt shader locDirShadowIdx[i] (if l.CastsShadows then 0 else -1)

    let ptCount = min pointLights.Count maxPoint
    setShaderInt shader locPointCount ptCount

    for i = 0 to ptCount - 1 do
      let l = pointLights[i]
      setShaderVec2 shader locPointPos[i] l.Position
      setShaderVec3 shader locPointColors[i] (colorToVec3 l.Color)
      setShaderFloat shader locPointIntensities[i] l.Intensity
      setShaderFloat shader locPointRadii[i] l.Radius
      setShaderFloat shader locPointFalloffs[i] l.Falloff

      setShaderInt
        shader
        locPointShadowIdx[i]
        (if l.CastsShadows then 0 else -1)

    let ocCount =
      if shadowsEnabled then
        min occluders.Count maxOccluderCount
      else
        0

    setShaderInt shader locOccluderCount ocCount

    for i = 0 to ocCount - 1 do
      let o = occluders[i]

      setShaderVec4
        shader
        locOccluders[i]
        (Vector4(o.P1.X, o.P1.Y, o.P2.X, o.P2.Y))

    setShaderFloat shader locSoftness shadowSoftness
    setShaderFloat shader locMaxDist shadowMaxDist

  /// <summary>Whether the lit shader is currently active. Managed by commands.</summary>
  member val ShaderActive = false with get, set

  /// <summary>Whether light uniforms need to be re-uploaded to the GPU.</summary>
  member val UniformsDirty = true with get, set

  /// <summary>Whether shadow raymarching is enabled for this context. Default true.</summary>
  member val ShadowsEnabled = true with get, set

  /// <summary>Ensures uniform locations are cached.</summary>
  member _.EnsureLocationsCached() =
    cacheLocations()
    cacheNmLocations()

  /// <summary>Clears accumulated lights, occluders, and resets ambient to black.</summary>
  member this.Reset() =
    dirLights.Clear()
    pointLights.Clear()
    occluders.Clear()
    ambientColor <- { R = 0uy; G = 0uy; B = 0uy; A = 255uy }
    this.ShaderActive <- false
    this.UniformsDirty <- true
    this.ShadowsEnabled <- true

  /// <summary>Current ambient light color.</summary>
  member _.Ambient
    with get () = ambientColor
    and set (v) = ambientColor <- v

  /// <summary>Directional lights accumulated this frame.</summary>
  member _.DirLights = dirLights

  /// <summary>Point lights accumulated this frame.</summary>
  member _.PointLights = pointLights

  /// <summary>Occluder segments accumulated this frame.</summary>
  member _.Occluders = occluders

  /// <summary>The standard lit-sprite shader (no normal map).</summary>
  member _.Shader = litShader

  /// <summary>The normal-mapped lit-sprite shader.</summary>
  member _.NormalMapShader = nmShader

  /// <summary>Uniform location for the normalMap sampler in the normal-map shader.</summary>
  member _.LocNormalMap = nmLocNormalMap

  /// <summary>Uploads all accumulated light data to the GPU for both shader variants.</summary>
  member this.UploadUniforms() =
    cacheLocations()
    cacheNmLocations()

    uploadToShader
      litShader
      locAmbientColor
      locDirCount
      locDirDirs
      locDirColors
      locDirIntensities
      locDirShadowIdx
      locPointCount
      locPointPos
      locPointColors
      locPointIntensities
      locPointRadii
      locPointFalloffs
      locPointShadowIdx
      locOccluders
      locOccluderCount
      locSoftness
      locMaxDist
      this.ShadowsEnabled

    uploadToShader
      nmShader
      nmLocAmbientColor
      nmLocDirCount
      nmLocDirDirs
      nmLocDirColors
      nmLocDirIntensities
      nmLocDirShadowIdx
      nmLocPointCount
      nmLocPointPos
      nmLocPointColors
      nmLocPointIntensities
      nmLocPointRadii
      nmLocPointFalloffs
      nmLocPointShadowIdx
      nmLocOccluders
      nmLocOccluderCount
      nmLocSoftness
      nmLocMaxDist
      this.ShadowsEnabled

  interface IDisposable with
    member _.Dispose() =
      Raylib.UnloadShader(litShader)
      Raylib.UnloadShader(nmShader)
