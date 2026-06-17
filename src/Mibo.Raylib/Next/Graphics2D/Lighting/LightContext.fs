#nowarn "9"

namespace Mibo.Elmish.Next.Graphics2D.Lighting

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D.Base

// All uniform locations for one shader variant. Held in a mutable record so
// the lit and normal-map shaders share a single caching/upload path instead
// of duplicating ~100 lines of GetShaderLocation + setShader* calls.
[<Struct>]
type ShaderUniformLocations = {
  mutable Cached: bool
  mutable AmbientColor: int
  mutable DirCount: int
  DirDirs: int[]
  DirColors: int[]
  DirIntensities: int[]
  DirShadowIdx: int[]
  mutable PointCount: int
  PointPos: int[]
  PointColors: int[]
  PointIntensities: int[]
  PointRadii: int[]
  PointFalloffs: int[]
  PointShadowIdx: int[]
  Occluders: int[]
  mutable OccluderCount: int
  mutable Softness: int
  mutable MaxDist: int
  mutable NormalMap: int
}

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
  // Track ownership so Dispose only unloads shaders this context created via
  // the default loaders. User-supplied shaders may be shared across contexts or
  // managed elsewhere, so unloading them risks double-free / use-after-free.
  let ownsLitShader = litShader.IsNone
  let ownsNmShader = litNormalMapShader.IsNone

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

  let mkLocations() : ShaderUniformLocations = {
    Cached = false
    AmbientColor = -1
    DirCount = -1
    DirDirs = Array.zeroCreate<int> maxDir
    DirColors = Array.zeroCreate<int> maxDir
    DirIntensities = Array.zeroCreate<int> maxDir
    DirShadowIdx = Array.zeroCreate<int> maxDir
    PointCount = -1
    PointPos = Array.zeroCreate<int> maxPoint
    PointColors = Array.zeroCreate<int> maxPoint
    PointIntensities = Array.zeroCreate<int> maxPoint
    PointRadii = Array.zeroCreate<int> maxPoint
    PointFalloffs = Array.zeroCreate<int> maxPoint
    PointShadowIdx = Array.zeroCreate<int> maxPoint
    Occluders = Array.zeroCreate<int> maxOccluderCount
    OccluderCount = -1
    Softness = -1
    MaxDist = -1
    NormalMap = -1
  }

  let mutable litLocs = mkLocations()
  let mutable nmLocs = mkLocations()

  let cacheLocationsFor (shader: Shader) (locs: byref<ShaderUniformLocations>) =

    if not locs.Cached then
      locs.AmbientColor <- Raylib.GetShaderLocation(shader, "ambientColor")
      locs.DirCount <- Raylib.GetShaderLocation(shader, "dirLightCount")

      for i = 0 to maxDir - 1 do
        locs.DirDirs[i] <-
          Raylib.GetShaderLocation(shader, $"dirLightDirs[{i}]")

        locs.DirColors[i] <-
          Raylib.GetShaderLocation(shader, $"dirLightColors[{i}]")

        locs.DirIntensities[i] <-
          Raylib.GetShaderLocation(shader, $"dirLightIntensities[{i}]")

        locs.DirShadowIdx[i] <-
          Raylib.GetShaderLocation(shader, $"dirLightShadowIdx[{i}]")

      locs.PointCount <- Raylib.GetShaderLocation(shader, "pointLightCount")

      for i = 0 to maxPoint - 1 do
        locs.PointPos[i] <-
          Raylib.GetShaderLocation(shader, $"pointLightPos[{i}]")

        locs.PointColors[i] <-
          Raylib.GetShaderLocation(shader, $"pointLightColors[{i}]")

        locs.PointIntensities[i] <-
          Raylib.GetShaderLocation(shader, $"pointLightIntensities[{i}]")

        locs.PointRadii[i] <-
          Raylib.GetShaderLocation(shader, $"pointLightRadii[{i}]")

        locs.PointFalloffs[i] <-
          Raylib.GetShaderLocation(shader, $"pointLightFalloffs[{i}]")

        locs.PointShadowIdx[i] <-
          Raylib.GetShaderLocation(shader, $"pointLightShadowIdx[{i}]")

      for i = 0 to maxOccluderCount - 1 do
        locs.Occluders[i] <- Raylib.GetShaderLocation(shader, $"occluders[{i}]")

      locs.OccluderCount <- Raylib.GetShaderLocation(shader, "occluderCount")
      locs.Softness <- Raylib.GetShaderLocation(shader, "shadowSoftness")
      locs.MaxDist <- Raylib.GetShaderLocation(shader, "shadowMaxDistance")
      locs.NormalMap <- Raylib.GetShaderLocation(shader, "normalMap")
      locs.Cached <- true

  let cacheLocations() = cacheLocationsFor litShader &litLocs
  let cacheNmLocations() = cacheLocationsFor nmShader &nmLocs

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
    (locs: ShaderUniformLocations)
    (shadowsEnabled: bool)
    =
    setShaderVec3 shader locs.AmbientColor (colorToVec3 ambientColor)

    let dirCount = min dirLights.Count maxDir
    setShaderInt shader locs.DirCount dirCount

    for i = 0 to dirCount - 1 do
      let l = dirLights[i]
      setShaderVec2 shader locs.DirDirs[i] l.Direction
      setShaderVec3 shader locs.DirColors[i] (colorToVec3 l.Color)
      setShaderFloat shader locs.DirIntensities[i] l.Intensity

      setShaderInt
        shader
        locs.DirShadowIdx[i]
        (if l.CastsShadows then 0 else -1)

    let ptCount = min pointLights.Count maxPoint
    setShaderInt shader locs.PointCount ptCount

    for i = 0 to ptCount - 1 do
      let l = pointLights[i]
      setShaderVec2 shader locs.PointPos[i] l.Position
      setShaderVec3 shader locs.PointColors[i] (colorToVec3 l.Color)
      setShaderFloat shader locs.PointIntensities[i] l.Intensity
      setShaderFloat shader locs.PointRadii[i] l.Radius
      setShaderFloat shader locs.PointFalloffs[i] l.Falloff

      setShaderInt
        shader
        locs.PointShadowIdx[i]
        (if l.CastsShadows then 0 else -1)

    let ocCount =
      if shadowsEnabled then
        min occluders.Count maxOccluderCount
      else
        0

    setShaderInt shader locs.OccluderCount ocCount

    for i = 0 to ocCount - 1 do
      let o = occluders[i]

      setShaderVec4
        shader
        locs.Occluders[i]
        (Vector4(o.P1.X, o.P1.Y, o.P2.X, o.P2.Y))

    setShaderFloat shader locs.Softness shadowSoftness
    setShaderFloat shader locs.MaxDist shadowMaxDist

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
  member _.LocNormalMap = nmLocs.NormalMap

  /// <summary>Uploads all accumulated light data to the GPU for both shader variants.</summary>
  member this.UploadUniforms() =
    cacheLocations()
    cacheNmLocations()
    uploadToShader litShader litLocs this.ShadowsEnabled
    uploadToShader nmShader nmLocs this.ShadowsEnabled

  interface IDisposable with
    member _.Dispose() =
      if ownsLitShader then
        Raylib.UnloadShader(litShader)

      if ownsNmShader then
        Raylib.UnloadShader(nmShader)
