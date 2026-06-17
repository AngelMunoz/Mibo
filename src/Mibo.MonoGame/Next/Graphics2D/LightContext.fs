namespace Mibo.Elmish.Next.Graphics2D

open System
open System.Collections.Generic
open System.Reflection
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D.Lighting

[<Struct>]
type private EffectParams = {
  AmbientColor: EffectParameter
  DirLightCount: EffectParameter
  DirDir: EffectParameter[]
  DirColor: EffectParameter[]
  DirIntensity: EffectParameter[]
  DirShadow: EffectParameter[]
  PointLightCount: EffectParameter
  PtPos: EffectParameter[]
  PtColor: EffectParameter[]
  PtIntensity: EffectParameter[]
  PtRadius: EffectParameter[]
  PtFalloff: EffectParameter[]
  PtShadow: EffectParameter[]
  Oc: EffectParameter[]
  OccluderCount: EffectParameter
  ShadowSoftness: EffectParameter
  ShadowMaxDistance: EffectParameter
}

type LightContext2D
  (
    graphicsDevice: GraphicsDevice,
    ?maxDirLights: int,
    ?maxPointLights: int,
    ?maxOccluders: int,
    ?softness: float32,
    ?maxShadowDistance: float32
  ) =

  let maxDir = defaultArg maxDirLights 4
  let maxPoint = defaultArg maxPointLights 16
  let maxOc = defaultArg maxOccluders 128
  let shadowSoftness = defaultArg softness 0.05f
  let shadowMaxDist = defaultArg maxShadowDistance 5000.0f

  let asm = Assembly.GetExecutingAssembly()

  let loadShader(name) =
    let dxName = $"Mibo.MonoGame.Content.Shaders.{name}.dx.mgfx"
    let oglName = $"Mibo.MonoGame.Content.Shaders.{name}.ogl.mgfx"

    let stream =
      let s = asm.GetManifestResourceStream(dxName)

      if s <> null then
        s
      else
        asm.GetManifestResourceStream(oglName)

    use s = stream
    let bytes = Array.zeroCreate(int s.Length)
    s.Read(bytes, 0, bytes.Length) |> ignore
    new Effect(graphicsDevice, bytes)

  let litEffect = loadShader "LitSprite"
  let nmEffect = loadShader "LitSpriteNormalMap"

  let dirLights = ResizeArray<DirectionalLight2D>()
  let pointLights = ResizeArray<PointLight2D>()
  let occluders = ResizeArray<Occluder2D>()

  let blackAmbient: Mibo.Elmish.Next.Graphics2D.Base.Color = {
    R = 0uy
    G = 0uy
    B = 0uy
    A = 255uy
  }

  let mutable ambientColor = blackAmbient

  // ── Cached EffectParameter references (avoid per-frame string alloc + linear search) ──

  let cache(effect: Effect) =
    let p(name: string) = effect.Parameters[name]

    let dirDir = Array.init maxDir (fun i -> p $"DirLightDirs[{i}]")
    let dirColor = Array.init maxDir (fun i -> p $"DirLightColors[{i}]")

    let dirIntensity =
      Array.init maxDir (fun i -> p $"DirLightIntensities[{i}]")

    let dirShadow = Array.init maxDir (fun i -> p $"DirLightShadowIdx[{i}]")
    let ptPos = Array.init maxPoint (fun i -> p $"PointLightPos[{i}]")
    let ptColor = Array.init maxPoint (fun i -> p $"PointLightColors[{i}]")

    let ptIntensity =
      Array.init maxPoint (fun i -> p $"PointLightIntensities[{i}]")

    let ptRadius = Array.init maxPoint (fun i -> p $"PointLightRadii[{i}]")
    let ptFalloff = Array.init maxPoint (fun i -> p $"PointLightFalloffs[{i}]")
    let ptShadow = Array.init maxPoint (fun i -> p $"PointLightShadowIdx[{i}]")
    let oc = Array.init maxOc (fun i -> p $"Occluders[{i}]")

    {
      AmbientColor = p "AmbientColor"
      DirLightCount = p "DirLightCount"
      DirDir = dirDir
      DirColor = dirColor
      DirIntensity = dirIntensity
      DirShadow = dirShadow
      PointLightCount = p "PointLightCount"
      PtPos = ptPos
      PtColor = ptColor
      PtIntensity = ptIntensity
      PtRadius = ptRadius
      PtFalloff = ptFalloff
      PtShadow = ptShadow
      Oc = oc
      OccluderCount = p "OccluderCount"
      ShadowSoftness = p "ShadowSoftness"
      ShadowMaxDistance = p "ShadowMaxDistance"
    }

  let litParams = cache litEffect
  let nmParams = cache nmEffect

  let colorToVec3(c: Mibo.Elmish.Next.Graphics2D.Base.Color) : Vector3 =
    Vector3(float32 c.R / 255.0f, float32 c.G / 255.0f, float32 c.B / 255.0f)

  let uploadToEffect (p: EffectParams) (shadowsEnabled: bool) =
    p.AmbientColor.SetValue(colorToVec3 ambientColor)

    let dirCount = min dirLights.Count maxDir
    p.DirLightCount.SetValue(dirCount)

    for i = 0 to dirCount - 1 do
      let l = dirLights[i]
      p.DirDir[i].SetValue(l.Direction)
      p.DirColor[i].SetValue(colorToVec3 l.Color)
      p.DirIntensity[i].SetValue(l.Intensity)
      p.DirShadow[i].SetValue(if l.CastsShadows then 0 else -1)

    let ptCount = min pointLights.Count maxPoint
    p.PointLightCount.SetValue(ptCount)

    for i = 0 to ptCount - 1 do
      let l = pointLights[i]
      p.PtPos[i].SetValue(l.Position)
      p.PtColor[i].SetValue(colorToVec3 l.Color)
      p.PtIntensity[i].SetValue(l.Intensity)
      p.PtRadius[i].SetValue(l.Radius)
      p.PtFalloff[i].SetValue(l.Falloff)
      p.PtShadow[i].SetValue(if l.CastsShadows then 0 else -1)

    let ocCount = if shadowsEnabled then min occluders.Count maxOc else 0
    p.OccluderCount.SetValue(ocCount)

    for i = 0 to ocCount - 1 do
      let o = occluders[i]
      p.Oc[i].SetValue(Vector4(o.P1.X, o.P1.Y, o.P2.X, o.P2.Y))

    p.ShadowSoftness.SetValue(shadowSoftness)
    p.ShadowMaxDistance.SetValue(shadowMaxDist)

  member val ShaderActive = false with get, set
  member val UniformsDirty = true with get, set
  member val ShadowsEnabled = true with get, set

  member _.EnsureLocationsCached() = ()

  member this.Reset() =
    dirLights.Clear()
    pointLights.Clear()
    occluders.Clear()
    ambientColor <- blackAmbient
    this.ShaderActive <- false
    this.UniformsDirty <- true
    this.ShadowsEnabled <- true

  member _.Ambient
    with get () = ambientColor
    and set (v) = ambientColor <- v

  member _.DirLights = dirLights
  member _.PointLights = pointLights
  member _.Occluders = occluders

  member _.Shader = litEffect
  member _.NormalMapShader = nmEffect

  member this.UploadUniforms() =
    uploadToEffect litParams this.ShadowsEnabled
    uploadToEffect nmParams this.ShadowsEnabled

  interface IDisposable with
    member _.Dispose() =
      litEffect.Dispose()
      nmEffect.Dispose()

// ─────────────────────────────────────────────────────────────────
// LightContext registry (scoped to buffer instance)
// ─────────────────────────────────────────────────────────────────

type MgLightContextRegistry() =
  let fwd = Dictionary<LightContext2D, int<LightContext>>()
  let rev = ResizeArray<LightContext2D>()

  member _.Register(ctx: LightContext2D) =
    match fwd.TryGetValue ctx with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<LightContext>
      rev.Add ctx
      fwd[ctx] <- h
      h

  member _.Resolve(h: int<LightContext>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()
