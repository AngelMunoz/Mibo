namespace Mibo.Elmish.Graphics2D.Lighting

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics2D.Lighting.ColorHelpers

/// <summary>
/// Central GPU-state holder for 2D lighting. Owns two compiled effects
/// (plain and normal-mapped) loaded from embedded <c>.mgfx</c> resources via
/// <see cref="T:Mibo.Elmish.Graphics2D.ShaderLoader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the raylib <c>LightContext2D</c>. The context collects lights and
/// occluders each frame (via the <c>DirLights</c>/<c>PointLights</c>/<c>Occluders</c>
/// <c>ResizeArray</c>s), then uploads them as shader uniforms on the first lit
/// sprite draw. Uniforms are uploaded to <b>both</b> effects so the renderer
/// can switch between plain and normal-mapped per sprite without re-uploading.
/// </para>
/// <para>
/// The user is responsible for calling <c>Reset()</c> at the start of each
/// frame's view (the renderer does not call it).
/// </para>
/// </remarks>
type LightContext2D
  (
    gd: GraphicsDevice,
    ?litEffect: Effect,
    ?litNormalMapEffect: Effect,
    ?maxDirLights: int,
    ?maxPointLights: int,
    ?maxOccluders: int,
    ?softness: float32,
    ?maxShadowDistance: float32
  ) =
  let maxDir = defaultArg maxDirLights 4
  let maxPoint = defaultArg maxPointLights 16
  let maxOcclud = defaultArg maxOccluders 128
  let shadowSoftness = defaultArg softness 0.05f
  let shadowMaxDist = defaultArg maxShadowDistance 5000.0f

  // Load effects from embedded resources if not user-supplied. Track which
  // ones we created so Dispose only releases effects we own — disposing a
  // caller-supplied Effect would cause a double-dispose / ObjectDisposedException
  // when the caller later disposes it themselves.
  let (litFx, ownsLitFx) =
    match litEffect with
    | Some e -> (e, false)
    | None ->
      match ShaderLoader.loadEffect gd "LitSprite" with
      | ValueSome e -> (e, true)
      | ValueNone ->
        failwith "LightContext2D: embedded LitSprite effect not found"

  let (nmFx, ownsNmFx) =
    match litNormalMapEffect with
    | Some e -> (e, false)
    | None ->
      match ShaderLoader.loadEffect gd "LitSpriteNormalMap" with
      | ValueSome e -> (e, true)
      | ValueNone ->
        failwith "LightContext2D: embedded LitSpriteNormalMap effect not found"

  let dirLights = ResizeArray<DirectionalLight2D>()
  let pointLights = ResizeArray<PointLight2D>()
  let occluders = ResizeArray<Occluder2D>()
  let mutable ambientColor = Color(0uy, 0uy, 0uy, 255uy)

  // Pre-allocated scratch arrays for whole-array uniform uploads.
  //
  // MonoGame's EffectPass.Apply() only re-uploads constant buffers that are
  // marked dirty, and per-element writes through param.Elements[i].SetValue(...)
  // do not reliably mark the parent buffer dirty. As a result shader *array*
  // uniforms (e.g. the directional light direction) would stick at their
  // first-frame values — the sun's shadow rendered but never swept, while the
  // scalar AmbientColor (set via the top-level SetValue) kept updating. raylib
  // is unaffected because SetShaderValue writes through imperatively every
  // call.
  //
  // Uploading each array via the top-level param.SetValue(array) overload
  // always marks the buffer dirty and re-uploads every frame. These scratch
  // buffers are reused per upload to avoid per-frame heap allocation
  // (AGENTS.md: avoid heap allocations in hot paths). Each is sized to its
  // shader-side MAX_* constant; the shader bounds its loops by the *Count
  // uniforms, so the unused tail is never sampled.
  let dirDirsBuf = Array.zeroCreate<Vector2> maxDir
  let dirColorsBuf = Array.zeroCreate<Vector3> maxDir
  let dirIntsBuf = Array.zeroCreate<float32> maxDir
  let pointPosBuf = Array.zeroCreate<Vector2> maxPoint
  let pointColorsBuf = Array.zeroCreate<Vector3> maxPoint
  let pointIntsBuf = Array.zeroCreate<float32> maxPoint
  let pointRadiiBuf = Array.zeroCreate<float32> maxPoint
  let pointFalloffsBuf = Array.zeroCreate<float32> maxPoint
  let occludersBuf = Array.zeroCreate<Vector4> maxOcclud

  // Uniform parameter caches — plain effect
  let mutable locsCached = false
  let mutable paramAmbient: EffectParameter = null
  let mutable paramDirCount: EffectParameter = null
  let mutable paramDirDirs: EffectParameter = null
  let mutable paramDirColors: EffectParameter = null
  let mutable paramDirIntensities: EffectParameter = null
  let mutable paramDirShadowIdx: EffectParameter = null
  let mutable paramPointCount: EffectParameter = null
  let mutable paramPointPos: EffectParameter = null
  let mutable paramPointColors: EffectParameter = null
  let mutable paramPointIntensities: EffectParameter = null
  let mutable paramPointRadii: EffectParameter = null
  let mutable paramPointFalloffs: EffectParameter = null
  let mutable paramPointShadowIdx: EffectParameter = null
  let mutable paramOccluders: EffectParameter = null
  let mutable paramOccluderCount: EffectParameter = null
  let mutable paramSoftness: EffectParameter = null
  let mutable paramMaxDist: EffectParameter = null

  // Uniform parameter caches — normal-map effect
  let mutable nmLocsCached = false
  let mutable nmParamAmbient: EffectParameter = null
  let mutable nmParamDirCount: EffectParameter = null
  let mutable nmParamDirDirs: EffectParameter = null
  let mutable nmParamDirColors: EffectParameter = null
  let mutable nmParamDirIntensities: EffectParameter = null
  let mutable nmParamDirShadowIdx: EffectParameter = null
  let mutable nmParamPointCount: EffectParameter = null
  let mutable nmParamPointPos: EffectParameter = null
  let mutable nmParamPointColors: EffectParameter = null
  let mutable nmParamPointIntensities: EffectParameter = null
  let mutable nmParamPointRadii: EffectParameter = null
  let mutable nmParamPointFalloffs: EffectParameter = null
  let mutable nmParamPointShadowIdx: EffectParameter = null
  let mutable nmParamOccluders: EffectParameter = null
  let mutable nmParamOccluderCount: EffectParameter = null
  let mutable nmParamSoftness: EffectParameter = null
  let mutable nmParamMaxDist: EffectParameter = null
  let mutable nmParamNormalMap: EffectParameter = null

  // Cache all EffectParameter references for a given effect
  let cacheParams
    (fx: Effect)
    (
      setAmbient,
      setDirCount,
      setDirDirs,
      setDirColors,
      setDirIntensities,
      setDirShadowIdx,
      setPointCount,
      setPointPos,
      setPointColors,
      setPointIntensities,
      setPointRadii,
      setPointFalloffs,
      setPointShadowIdx,
      setOccluders,
      setOccluderCount,
      setSoftness,
      setMaxDist,
      setNormalMap: EffectParameter -> unit
    ) =
    let tryGet(name: string) =
      let p = fx.Parameters[name]
      p

    setAmbient(tryGet "AmbientColor")
    setDirCount(tryGet "DirLightCount")
    setDirDirs(tryGet "DirLightDirs")
    setDirColors(tryGet "DirLightColors")
    setDirIntensities(tryGet "DirLightIntensities")
    setDirShadowIdx(tryGet "DirLightShadowIdx")
    setPointCount(tryGet "PointLightCount")
    setPointPos(tryGet "PointLightPos")
    setPointColors(tryGet "PointLightColors")
    setPointIntensities(tryGet "PointLightIntensities")
    setPointRadii(tryGet "PointLightRadii")
    setPointFalloffs(tryGet "PointLightFalloffs")
    setPointShadowIdx(tryGet "PointLightShadowIdx")
    setOccluders(tryGet "Occluders")
    setOccluderCount(tryGet "OccluderCount")
    setSoftness(tryGet "ShadowSoftness")
    setMaxDist(tryGet "ShadowMaxDistance")
    setNormalMap(tryGet "NormalMap")

  let cacheLocations() =
    if not locsCached then
      cacheParams
        litFx
        ((fun p -> paramAmbient <- p),
         (fun p -> paramDirCount <- p),
         (fun p -> paramDirDirs <- p),
         (fun p -> paramDirColors <- p),
         (fun p -> paramDirIntensities <- p),
         (fun p -> paramDirShadowIdx <- p),
         (fun p -> paramPointCount <- p),
         (fun p -> paramPointPos <- p),
         (fun p -> paramPointColors <- p),
         (fun p -> paramPointIntensities <- p),
         (fun p -> paramPointRadii <- p),
         (fun p -> paramPointFalloffs <- p),
         (fun p -> paramPointShadowIdx <- p),
         (fun p -> paramOccluders <- p),
         (fun p -> paramOccluderCount <- p),
         (fun p -> paramSoftness <- p),
         (fun p -> paramMaxDist <- p),
         ignore)

      locsCached <- true

  let cacheNmLocations() =
    if not nmLocsCached then
      cacheParams
        nmFx
        ((fun p -> nmParamAmbient <- p),
         (fun p -> nmParamDirCount <- p),
         (fun p -> nmParamDirDirs <- p),
         (fun p -> nmParamDirColors <- p),
         (fun p -> nmParamDirIntensities <- p),
         (fun p -> nmParamDirShadowIdx <- p),
         (fun p -> nmParamPointCount <- p),
         (fun p -> nmParamPointPos <- p),
         (fun p -> nmParamPointColors <- p),
         (fun p -> nmParamPointIntensities <- p),
         (fun p -> nmParamPointRadii <- p),
         (fun p -> nmParamPointFalloffs <- p),
         (fun p -> nmParamPointShadowIdx <- p),
         (fun p -> nmParamOccluders <- p),
         (fun p -> nmParamOccluderCount <- p),
         (fun p -> nmParamSoftness <- p),
         (fun p -> nmParamMaxDist <- p),
         (fun p -> nmParamNormalMap <- p))

      nmLocsCached <- true

  // Upload the whole occluder array via the top-level SetValue(array) overload
  // so the constant buffer is marked dirty and re-uploaded every frame (per-
  // element Elements[i].SetValue does not reliably do so). The shader bounds
  // its SDF loop by OccluderCount, so the unused tail beyond `count` is never
  // sampled. occludersBuf is reused each upload (AGENTS.md).
  let uploadOccluderArray
    (param: EffectParameter)
    (ocs: ResizeArray<Occluder2D>)
    (count: int)
    =
    if param <> null then
      let n = min count occludersBuf.Length

      for i = 0 to n - 1 do
        let o = ocs[i]
        occludersBuf[i] <- Vector4(o.P1.X, o.P1.Y, o.P2.X, o.P2.Y)

      param.SetValue(occludersBuf)

  // Upload uniforms to one effect
  let uploadToShader
    (
      fx: Effect,
      pAmbient: EffectParameter,
      pDirCount: EffectParameter,
      pDirDirs: EffectParameter,
      pDirColors: EffectParameter,
      pDirInts: EffectParameter,
      pDirShadow: EffectParameter,
      pPointCount: EffectParameter,
      pPointPos: EffectParameter,
      pPointColors: EffectParameter,
      pPointInts: EffectParameter,
      pPointRadii: EffectParameter,
      pPointFalloffs: EffectParameter,
      pPointShadow: EffectParameter,
      pOccluders: EffectParameter,
      pOccluderCount: EffectParameter,
      pSoftness: EffectParameter,
      pMaxDist: EffectParameter,
      shadowsEnabled: bool
    ) =
    if pAmbient <> null then
      pAmbient.SetValue(colorToVec3 ambientColor)

    let dirCount = min dirLights.Count maxDir

    if pDirCount <> null then
      pDirCount.SetValue(dirCount)

    for i = 0 to dirCount - 1 do
      let l = dirLights[i]
      dirDirsBuf[i] <- l.Direction
      dirColorsBuf[i] <- colorToVec3 l.Color
      dirIntsBuf[i] <- l.Intensity

      // DirLightShadowIdx is static per light (CastsShadows never changes
      // frame-to-frame), so the per-element path is sufficient here.
      if pDirShadow <> null then
        pDirShadow.Elements[i].SetValue(if l.CastsShadows then 0 else -1)

    // Whole-array uploads force the constant buffer dirty and re-upload every
    // frame — see the scratch-buffer note above.
    if pDirDirs <> null then
      pDirDirs.SetValue(dirDirsBuf)

    if pDirColors <> null then
      pDirColors.SetValue(dirColorsBuf)

    if pDirInts <> null then
      pDirInts.SetValue(dirIntsBuf)

    let ptCount = min pointLights.Count maxPoint

    if pPointCount <> null then
      pPointCount.SetValue(ptCount)

    for i = 0 to ptCount - 1 do
      let l = pointLights[i]
      pointPosBuf[i] <- l.Position
      pointColorsBuf[i] <- colorToVec3 l.Color
      pointIntsBuf[i] <- l.Intensity
      pointRadiiBuf[i] <- l.Radius
      pointFalloffsBuf[i] <- l.Falloff

      // PointLightShadowIdx is static per light; per-element upload is fine.
      if pPointShadow <> null then
        pPointShadow.Elements[i].SetValue(if l.CastsShadows then 0 else -1)

    if pPointPos <> null then
      pPointPos.SetValue(pointPosBuf)

    if pPointColors <> null then
      pPointColors.SetValue(pointColorsBuf)

    if pPointInts <> null then
      pPointInts.SetValue(pointIntsBuf)

    if pPointRadii <> null then
      pPointRadii.SetValue(pointRadiiBuf)

    if pPointFalloffs <> null then
      pPointFalloffs.SetValue(pointFalloffsBuf)

    let ocCount = if shadowsEnabled then min occluders.Count maxOcclud else 0

    if pOccluderCount <> null then
      pOccluderCount.SetValue(ocCount)

    uploadOccluderArray pOccluders occluders ocCount

    if pSoftness <> null then
      pSoftness.SetValue(shadowSoftness)

    if pMaxDist <> null then
      pMaxDist.SetValue(shadowMaxDist)

  // ── Public state ──────────────────────────────────────────────
  member val ShaderActive = false with get, set
  member val UniformsDirty = true with get, set
  member val ShadowsEnabled = true with get, set

  member _.Ambient
    with get () = ambientColor
    and set (c: Color) = ambientColor <- c

  member _.DirLights = dirLights
  member _.PointLights = pointLights
  member _.Occluders = occluders

  member _.Effect = litFx
  member _.NormalMapEffect = nmFx

  /// <summary>The normal-map effect's <c>normalMap</c> sampler parameter.</summary>
  member _.NormalMapParameter =
    cacheNmLocations()
    nmParamNormalMap

  /// <summary>Caches shader parameter locations for both effects (idempotent).</summary>
  member _.EnsureLocationsCached() =
    cacheLocations()
    cacheNmLocations()

  /// <summary>Uploads all light/occluder uniforms to both effects.</summary>
  member this.UploadUniforms() =
    this.EnsureLocationsCached()
    let shadows = this.ShadowsEnabled

    uploadToShader(
      litFx,
      paramAmbient,
      paramDirCount,
      paramDirDirs,
      paramDirColors,
      paramDirIntensities,
      paramDirShadowIdx,
      paramPointCount,
      paramPointPos,
      paramPointColors,
      paramPointIntensities,
      paramPointRadii,
      paramPointFalloffs,
      paramPointShadowIdx,
      paramOccluders,
      paramOccluderCount,
      paramSoftness,
      paramMaxDist,
      shadows
    )

    uploadToShader(
      nmFx,
      nmParamAmbient,
      nmParamDirCount,
      nmParamDirDirs,
      nmParamDirColors,
      nmParamDirIntensities,
      nmParamDirShadowIdx,
      nmParamPointCount,
      nmParamPointPos,
      nmParamPointColors,
      nmParamPointIntensities,
      nmParamPointRadii,
      nmParamPointFalloffs,
      nmParamPointShadowIdx,
      nmParamOccluders,
      nmParamOccluderCount,
      nmParamSoftness,
      nmParamMaxDist,
      shadows
    )

  /// <summary>
  /// Clears all lights, occluders, and resets state. Call at the start of each
  /// frame's view (the renderer does not call this).
  /// </summary>
  member this.Reset() =
    dirLights.Clear()
    pointLights.Clear()
    occluders.Clear()
    ambientColor <- Color(0uy, 0uy, 0uy, 255uy)
    this.ShaderActive <- false
    this.UniformsDirty <- true
    this.ShadowsEnabled <- true

  interface IDisposable with
    member _.Dispose() =
      // Only dispose effects this context created from embedded resources;
      // caller-supplied effects remain owned by the caller.
      if ownsLitFx then
        litFx.Dispose()

      if ownsNmFx then
        nmFx.Dispose()
