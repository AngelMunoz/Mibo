namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// PbrUniforms — the PBR effect's uniform handles, split by semantic group (v2
// pipeline-staging refactor).
//
// Mirrors the canonical raylib backend (ForwardPbrPipeline.fs): the flat 40-field
// PbrEffectParams is decomposed into per-concern sub-records (matrices / material /
// ambient / directional / point lights / spot lights / shadow), then composed back
// into a single PbrEffectParams the pipeline holds. Each field is an EffectParameter
// resolved once on load (null when the uniform is optimized out — callers null-check
// before SetValue).
//
// Sub-records are internal (the public surface is the composite PbrEffectParams name,
// so external type signatures stay stable). The upload helpers + pooled light scratch
// arrays live here too — they're upload state, not pipeline state.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Matrix uniforms: model, view-projection, normal, camera position, and the
/// skinning bone palette (B12). Bones is null on the non-skinned techniques — uploaded
/// only when present.</summary>
[<Struct>]
type internal MatrixUniforms = {
  MatModel: EffectParameter
  ViewProj: EffectParameter
  NormalMatrix: EffectParameter
  CameraPos: EffectParameter
  // Skinning (B12 Skinned technique): bone palette. null on the non-skinned
  // techniques is harmless — the animated-model path uploads only when present.
  Bones: EffectParameter
}

/// <summary>Material uniforms: scalar/color PBR factors + the 5 sampler textures.
/// MonoGame binds sampler textures from the effect's own parameters at Apply() time
/// (NOT from gd.Textures[]), so the texture maps live here as EffectParameter handles.</summary>
[<Struct>]
type internal MaterialUniforms = {
  AlbedoColor: EffectParameter
  Roughness: EffectParameter
  Metallic: EffectParameter
  EmissionColor: EffectParameter
  Opacity: EffectParameter
  Tiling: EffectParameter
  UseNormalMap: EffectParameter
  // Texture maps — EffectParameter (not gd.Textures[]) because EffectPass.Apply clobbers
  // gd.Textures[i] from the effect's own params (see ForwardPbr.fx sampler binding).
  AlbedoMapTex: EffectParameter
  RoughnessMapTex: EffectParameter
  NormalMapTex: EffectParameter
  MetallicMapTex: EffectParameter
  EmissionMapTex: EffectParameter
}

/// <summary>Ambient light uniforms (single slot).</summary>
[<Struct>]
type internal AmbientUniforms = {
  Color: EffectParameter
  Intensity: EffectParameter
}

/// <summary>Directional light uniforms (single slot).</summary>
[<Struct>]
type internal DirLightUniforms = {
  Dir: EffectParameter
  Color: EffectParameter
  Intensity: EffectParameter
}

/// <summary>Point light uniforms (array params; null if MAX_POINT_LIGHTS sized out).</summary>
[<Struct>]
type internal PointLightUniforms = {
  Count: EffectParameter
  Pos: EffectParameter
  Color: EffectParameter
  Intensity: EffectParameter
  Radius: EffectParameter
  Falloff: EffectParameter
  // Per-light shadow atlas slot (-1 = no shadow). Read by the PBR sampler.
  ShadowIdx: EffectParameter
}

/// <summary>Spot light uniforms (array params; null if MAX_SPOT_LIGHTS sized out).</summary>
[<Struct>]
type internal SpotLightUniforms = {
  Count: EffectParameter
  Pos: EffectParameter
  Dir: EffectParameter
  Color: EffectParameter
  Intensity: EffectParameter
  Radius: EffectParameter
  InnerCutoff: EffectParameter
  OuterCutoff: EffectParameter
  // Per-light shadow atlas slot (-1 = no shadow). Read by the PBR sampler.
  ShadowIdx: EffectParameter
}

/// <summary>Shadow sampling uniforms (B10 directional; B11 multi-caster + per-light index).</summary>
[<Struct>]
type internal ShadowUniforms = {
  DirLightCastsShadows: EffectParameter
  ShadowViewProjs: EffectParameter
  ShadowUVOffsets: EffectParameter
  ShadowTexelSize: EffectParameter
  ShadowAtlasTex: EffectParameter
  ShadowBiases: EffectParameter
}

/// <summary>
/// Composite of the cached PBR effect uniform handles, grouped by semantic concern. Built once on
/// load via <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrUniforms.build"/>. The sub-records are
/// the decomposition of the former flat 40-field struct; each group is uploaded by its own helper
/// (<see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrUniforms.uploadMaterial"/>,
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrUniforms.uploadLights"/>, etc.).
/// </summary>
/// <remarks>Internal — pipeline implementation detail (mirrors the raylib backend's
/// <c>internal ShaderLocations</c>); the public surface is the pipeline type itself.</remarks>
[<Struct>]
type internal PbrEffectParams = {
  Matrix: MatrixUniforms
  Material: MaterialUniforms
  Ambient: AmbientUniforms
  DirLight: DirLightUniforms
  PointLights: PointLightUniforms
  SpotLights: SpotLightUniforms
  Shadow: ShadowUniforms
}

/// <summary>Builds the PBR effect uniform handles, grouped by semantic concern, once after load.</summary>
module internal PbrUniforms =

  let inline private param (e: Effect) (name: string) : EffectParameter =
    e.Parameters[name] // null when absent — callers null-check before SetValue.

  let private buildMatrix(e: Effect) : MatrixUniforms = {
    MatModel = param e "matModel"
    ViewProj = param e "viewProj"
    NormalMatrix = param e "normalMatrix"
    CameraPos = param e "cameraPos"
    Bones = param e "boneMatrices"
  }

  let private buildMaterial(e: Effect) : MaterialUniforms = {
    AlbedoColor = param e "albedoColor"
    Roughness = param e "roughness"
    Metallic = param e "metallic"
    EmissionColor = param e "emissionColor"
    Opacity = param e "opacity"
    Tiling = param e "tiling"
    UseNormalMap = param e "useNormalMap"
    AlbedoMapTex = param e "texture0"
    RoughnessMapTex = param e "texture1"
    NormalMapTex = param e "texture2"
    MetallicMapTex = param e "texture3"
    EmissionMapTex = param e "texture4"
  }

  let private buildAmbient(e: Effect) : AmbientUniforms = {
    Color = param e "ambientColor"
    Intensity = param e "ambientIntensity"
  }

  let private buildDirLight(e: Effect) : DirLightUniforms = {
    Dir = param e "dirLightDir"
    Color = param e "dirLightColor"
    Intensity = param e "dirLightIntensity"
  }

  let private buildPointLights(e: Effect) : PointLightUniforms = {
    Count = param e "pointLightCount"
    Pos = param e "pointLightPos"
    Color = param e "pointLightColor"
    Intensity = param e "pointLightIntensity"
    Radius = param e "pointLightRadius"
    Falloff = param e "pointLightFalloff"
    ShadowIdx = param e "pointLightShadowIdx"
  }

  let private buildSpotLights(e: Effect) : SpotLightUniforms = {
    Count = param e "spotLightCount"
    Pos = param e "spotLightPos"
    Dir = param e "spotLightDir"
    Color = param e "spotLightColor"
    Intensity = param e "spotLightIntensity"
    Radius = param e "spotLightRadius"
    InnerCutoff = param e "spotLightInnerCutoff"
    OuterCutoff = param e "spotLightOuterCutoff"
    ShadowIdx = param e "spotLightShadowIdx"
  }

  let private buildShadow(e: Effect) : ShadowUniforms = {
    DirLightCastsShadows = param e "dirLightCastsShadows"
    ShadowViewProjs = param e "shadowViewProjs"
    ShadowUVOffsets = param e "shadowUVOffsets"
    ShadowTexelSize = param e "shadowTexelSize"
    // The atlas sampler is named "shadowAtlas" in ForwardPbr.fx (sampler2D shadowAtlas :
    // register(s5)). NOT "texture5" — mgfxc exposes the sampler under its HLSL name, so
    // resolving "texture5" returns null and the atlas bind silently no-ops, leaving s5
    // unbound and the forward shader sampling 0.0 (everything shadowed).
    ShadowAtlasTex = param e "shadowAtlas"
    ShadowBiases = param e "shadowBiases"
  }

  /// <summary>Resolves all PBR effect parameter handles once after load.</summary>
  let build(e: Effect) : PbrEffectParams = {
    Matrix = buildMatrix e
    Material = buildMaterial e
    Ambient = buildAmbient e
    DirLight = buildDirLight e
    PointLights = buildPointLights e
    SpotLights = buildSpotLights e
    Shadow = buildShadow e
  }

  // ── null-safe setters. null param = absent uniform = no-op. ──
  let inline setVec2 (p: EffectParameter) (v: Vector2) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setVec3 (p: EffectParameter) (v: Vector3) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setVec4 (p: EffectParameter) (v: Vector4) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setFloat (p: EffectParameter) (v: float32) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setInt (p: EffectParameter) (v: int) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setMatrix (p: EffectParameter) (m: Matrix) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue m

  let inline setVec3Array (p: EffectParameter) (v: Vector3[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setFloatArray (p: EffectParameter) (v: float32[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setIntArray (p: EffectParameter) (v: int[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setMatrixArray (p: EffectParameter) (v: Matrix[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setVec4Array (p: EffectParameter) (v: Vector4[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline setVec4Element (p: EffectParameter) (i: int) (v: Vector4) =
    if not(obj.ReferenceEquals(p, null)) && i < p.Elements.Count then
      p.Elements[i].SetValue v

  let inline setFloatElement (p: EffectParameter) (i: int) (v: float32) =
    if not(obj.ReferenceEquals(p, null)) && i < p.Elements.Count then
      p.Elements[i].SetValue v

  /// <summary>Converts an XNA <see cref="T:Microsoft.Xna.Framework.Color"/> to a normalized <see cref="T:Microsoft.Xna.Framework.Vector4"/>.</summary>
  let inline colorToVec4(c: Color) : Vector4 =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  // ── Pooled staging arrays for light uploads — sized to the shader's MAX_* constants,
  //    reused across frames (no per-draw allocation on the hot path). Module-level so every
  //    upload call shares them (the pipeline is single-threaded). ──
  let mutable private pointLightPosScratch = Array.zeroCreate<Vector3> 8
  let mutable private pointLightColorScratch = Array.zeroCreate<Vector3> 8
  let mutable private pointLightIntensityScratch = Array.zeroCreate<float32> 8
  let mutable private pointLightRadiusScratch = Array.zeroCreate<float32> 8
  let mutable private pointLightFalloffScratch = Array.zeroCreate<float32> 8
  let mutable private spotLightPosScratch = Array.zeroCreate<Vector3> 4
  let mutable private spotLightDirScratch = Array.zeroCreate<Vector3> 4
  let mutable private spotLightColorScratch = Array.zeroCreate<Vector3> 4
  let mutable private spotLightIntensityScratch = Array.zeroCreate<float32> 4
  let mutable private spotLightRadiusScratch = Array.zeroCreate<float32> 4
  let mutable private spotLightInnerScratch = Array.zeroCreate<float32> 4
  let mutable private spotLightOuterScratch = Array.zeroCreate<float32> 4
  let mutable private pointShadowIdxScratch = Array.zeroCreate<int> 8
  let mutable private spotShadowIdxScratch = Array.zeroCreate<int> 4

  /// <summary>
  /// Uploads the frame's accumulated lights (ambient + 1 directional + N point + M spot) to the
  /// PBR effect. Point/spot arrays upload only the active count; the shader early-outs via
  /// <c>*Count</c>. <paramref name="pointShadowIdx"/>/<paramref name="spotShadowIdx"/> carry the
  /// per-light shadow atlas slots (computed by the shadow pass; -1 = no shadow).
  /// </summary>
  let uploadLights
    (
      p: inref<PbrEffectParams>,
      lights: LightBuffers,
      pointShadowIdx: int[],
      spotShadowIdx: int[]
    ) =
    let ambient = &p.Ambient
    let dir = &p.DirLight
    let pt = &p.PointLights
    let sp = &p.SpotLights

    // Ambient (single slot; zeroes when absent).
    match lights.Ambient with
    | ValueSome a ->
      setVec3
        ambient.Color
        (Conversions.fromNumericsVector3(Mibo.Color.toVector3 a.Color))

      setFloat ambient.Intensity a.Intensity
    | ValueNone ->
      setVec3 ambient.Color Vector3.Zero
      setFloat ambient.Intensity 0.0f

    // Directional (single slot; zeroes when absent).
    match lights.DirLights.Count with
    | 0 ->
      setVec3 dir.Dir Vector3.Forward
      setVec3 dir.Color Vector3.Zero
      setFloat dir.Intensity 0.0f
    | _ ->
      let d = lights.DirLights[0]
      setVec3 dir.Dir (Conversions.fromNumericsVector3 d.Direction)

      setVec3
        dir.Color
        (Conversions.fromNumericsVector3(Mibo.Color.toVector3 d.Color))

      setFloat dir.Intensity d.Intensity

    // Point lights — upload active count slots.
    let ptCount = min lights.PointLights.Count pointLightPosScratch.Length
    setInt pt.Count ptCount

    for i = 0 to ptCount - 1 do
      let l = lights.PointLights[i]
      pointLightPosScratch[i] <- Conversions.fromNumericsVector3 l.Position

      pointLightColorScratch[i] <-
        Conversions.fromNumericsVector3(Mibo.Color.toVector3 l.Color)

      pointLightIntensityScratch[i] <- l.Intensity
      pointLightRadiusScratch[i] <- l.Radius
      pointLightFalloffScratch[i] <- l.Falloff

      pointShadowIdxScratch[i] <-
        if i < pointShadowIdx.Length then pointShadowIdx[i] else -1

    setVec3Array pt.Pos pointLightPosScratch
    setVec3Array pt.Color pointLightColorScratch
    setFloatArray pt.Intensity pointLightIntensityScratch
    setFloatArray pt.Radius pointLightRadiusScratch
    setFloatArray pt.Falloff pointLightFalloffScratch
    setIntArray pt.ShadowIdx pointShadowIdxScratch

    // Spot lights — upload active count slots.
    let spCount = min lights.SpotLights.Count spotLightPosScratch.Length
    setInt sp.Count spCount

    for i = 0 to spCount - 1 do
      let l = lights.SpotLights[i]
      spotLightPosScratch[i] <- Conversions.fromNumericsVector3 l.Position
      spotLightDirScratch[i] <- Conversions.fromNumericsVector3 l.Direction

      spotLightColorScratch[i] <-
        Conversions.fromNumericsVector3(Mibo.Color.toVector3 l.Color)

      spotLightIntensityScratch[i] <- l.Intensity
      spotLightRadiusScratch[i] <- l.Radius
      spotLightInnerScratch[i] <- l.InnerCutoff
      spotLightOuterScratch[i] <- l.OuterCutoff

      spotShadowIdxScratch[i] <-
        if i < spotShadowIdx.Length then spotShadowIdx[i] else -1

    setVec3Array sp.Pos spotLightPosScratch
    setVec3Array sp.Dir spotLightDirScratch
    setVec3Array sp.Color spotLightColorScratch
    setFloatArray sp.Intensity spotLightIntensityScratch
    setFloatArray sp.Radius spotLightRadiusScratch
    setFloatArray sp.InnerCutoff spotLightInnerScratch
    setFloatArray sp.OuterCutoff spotLightOuterScratch
    setIntArray sp.ShadowIdx spotShadowIdxScratch

  /// <summary>
  /// Uploads material scalars/colors. Callers gate this on a <c>MaterialKey</c> change to avoid
  /// re-uploading when consecutive draws share a material. The per-draw <c>normalMatrix</c> is NOT
  /// uploaded here — it depends on the transform, not the material, and is set unconditionally per draw.
  /// </summary>
  let uploadMaterial(p: inref<PbrEffectParams>, mat: inref<Material3D>) =
    let m = &p.Material
    setVec4 m.AlbedoColor (colorToVec4 mat.AlbedoColor)
    setFloat m.Roughness mat.Roughness
    setFloat m.Metallic mat.Metallic
    setVec4 m.EmissionColor (colorToVec4 mat.EmissionColor)
    setFloat m.Opacity mat.Opacity
    setVec2 m.Tiling mat.Tiling

    setInt
      m.UseNormalMap
      (match mat.NormalMap with
       | ValueSome _ -> 1
       | ValueNone -> 0)

  /// <summary>
  /// Binds a material's 5 texture maps to the PBR effect's texture0..4 parameters. MonoGame's
  /// <see cref="M:Microsoft.Xna.Framework.Graphics.EffectPass.Apply"/> pulls sampler textures from the
  /// effect's own parameters (EffectPass.SetShaderSamplers), NOT from <c>gd.Textures[]</c> — so the
  /// textures MUST be set here via the EffectParameter, or Apply() clobbers them to null and PBR
  /// draws sample nothing (black). <paramref name="white"/> is the 1×1 white fallback bound for any
  /// absent map so textureless materials (e.g. <c>Material3D.colored</c>) sample white instead of black.
  /// </summary>
  let bindTextures
    (p: inref<PbrEffectParams>, mat: inref<Material3D>, white: Texture2D)
    =
    let m = &p.Material

    let inline setTex (pp: EffectParameter) (t: Texture2D voption) =
      if not(obj.ReferenceEquals(pp, null)) then
        match t with
        // Annotate null as Texture: F# can't resolve the SetValue overload for an untyped null.
        | ValueSome tex -> pp.SetValue tex
        | ValueNone -> pp.SetValue white

    setTex m.AlbedoMapTex mat.AlbedoMap
    setTex m.RoughnessMapTex mat.RoughnessMap
    setTex m.NormalMapTex mat.NormalMap
    setTex m.MetallicMapTex mat.MetallicMap
    setTex m.EmissionMapTex mat.EmissionMap
