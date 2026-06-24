namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// SceneUpload — effect-agnostic scene-data upload (v2 pipeline-staging, Phase 2+4).
//
// Resolves the PBR/uniform parameter NAMES on ANY effect and uploads the gathered
// scene (matrices + material + lights + bones). Absent parameters return null from
// Parameters["name"] (MonoGame) and are skipped — so an effect that declares only a
// subset of the contract (e.g. a toon shader with matModel/viewProj/dirLight*/albedoColor)
// inherits exactly what it declares and nothing more.
//
// This is NOT the PBR hot path. The default pipeline shades via the cached
// PbrEffectParams (ForwardHelpers) to skip re-resolving names every draw. SceneUpload
// is the path a user-effect scope (beginEffect/endEffect) takes — name resolution per
// call is fine there because user-effect scopes are not per-frame-bulk hot paths.
//
// Per the v2 spec §3: a user effect inherits scene DATA (camera, lights, material,
// bones), not the PBR shader itself. Shadow uniforms are intentionally NOT uploaded
// here — they are a PBR-pipeline concern (bound to slot 5 during the shadow pass);
// an effect that wants shadows declares and binds them itself.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Effect-agnostic scene-data upload: resolves uniform names on any <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>.</summary>
/// <remarks>
/// <para>
/// <b>What is uploaded</b> (when the uniform is present on the target effect; absent uniforms are
/// skipped — MonoGame returns null from <c>Parameters["name"]</c>): matrices
/// (<c>matModel</c>/<c>viewProj</c>/<c>normalMatrix</c>/<c>cameraPos</c>), material
/// (<c>albedoColor</c>, <c>texture0..4</c>, <c>roughness</c>/<c>metallic</c>/<c>emissionColor</c>/
/// <c>opacity</c>/<c>tiling</c>/<c>useNormalMap</c>), lights (ambient + 1 directional + N point +
/// M spot, including per-light shadow indices), shadows (the atlas + <c>shadowViewProjs</c>/
/// <c>shadowUVOffsets</c>/<c>shadowTexelSize</c>/<c>dirLightCastsShadows</c>, when the frame has a
/// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ShadowResult"/>), and bones
/// (<c>boneMatrices[128]</c>, only when supplied).
/// </para>
/// <para>
/// <b>Shadows are opt-in by declaration.</b> A user effect that wants shadow sampling declares the
/// shadow uniforms (<c>shadowViewProjs</c>, <c>shadowUVOffsets</c>, <c>shadowTexelSize</c>,
/// <c>dirLightCastsShadows</c>, <c>pointLightShadowIdx</c>, <c>spotLightShadowIdx</c>) and a
/// <c>texture5</c> sampler; when the frame's shadow pass produced an atlas, those uniforms are
/// uploaded by name and the atlas is bound to sampler slot 5 (PointClamp). An effect that declares
/// none of them renders unshadowed — no cost, no sampling. When the frame has no shadow-casting
/// light, <c>dirLightCastsShadows</c> is set to 0 and nothing else shadow-related is uploaded.
/// </para>
/// </remarks>
module SceneUpload =

  // ── null-safe setters (mirror ForwardHelpers' set* but self-contained: this module
  //    can't see the private ForwardHelpers). null params = absent uniform = no-op. ──
  let inline private setVec2 (p: EffectParameter) (v: Vector2) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setVec3 (p: EffectParameter) (v: Vector3) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setVec4 (p: EffectParameter) (v: Vector4) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setFloat (p: EffectParameter) (v: float32) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setInt (p: EffectParameter) (v: int) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setMatrix (p: EffectParameter) (m: Matrix) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue m

  let inline private setVec3Array (p: EffectParameter) (v: Vector3[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setFloatArray (p: EffectParameter) (v: float32[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setIntArray (p: EffectParameter) (v: int[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setMatrixArray (p: EffectParameter) (v: Matrix[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  let inline private setVec4Array (p: EffectParameter) (v: Vector4[]) =
    if not(obj.ReferenceEquals(p, null)) then
      p.SetValue v

  /// <summary>Binds the shadow atlas to a named sampler texture param (texture5 slot).</summary>
  let inline private setTex5 (pp: EffectParameter) (tex: Texture2D) =
    if not(obj.ReferenceEquals(pp, null)) then
      pp.SetValue tex

  /// <summary>Converts an XNA <see cref="T:Microsoft.Xna.Framework.Color"/> to a normalized <see cref="T:Microsoft.Xna.Framework.Vector4"/>.</summary>
  let inline private colorToVec4(c: Color) : Vector4 =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  let inline private param (effect: Effect) (name: string) : EffectParameter =
    effect.Parameters[name]

  // ── Pooled staging arrays for light uploads — sized to the shader's MAX_* constants,
  //    reused across calls (no per-call allocation). Mirrors the PBR path's scratch. ──
  let mutable private pointPos = Array.zeroCreate<Vector3> 8
  let mutable private pointColor = Array.zeroCreate<Vector3> 8
  let mutable private pointIntensity = Array.zeroCreate<float32> 8
  let mutable private pointRadius = Array.zeroCreate<float32> 8
  let mutable private pointFalloff = Array.zeroCreate<float32> 8
  let mutable private spotPos = Array.zeroCreate<Vector3> 4
  let mutable private spotDir = Array.zeroCreate<Vector3> 4
  let mutable private spotColor = Array.zeroCreate<Vector3> 4
  let mutable private spotIntensity = Array.zeroCreate<float32> 4
  let mutable private spotRadius = Array.zeroCreate<float32> 4
  let mutable private spotInner = Array.zeroCreate<float32> 4
  let mutable private spotOuter = Array.zeroCreate<float32> 4
  let mutable private pointShadowIdx = Array.zeroCreate<int> 8
  let mutable private spotShadowIdx = Array.zeroCreate<int> 4

  /// <summary>
  /// Uploads the full scene-data contract to <paramref name="effect"/> by resolving each
  /// uniform by name. Absent uniforms are skipped (MonoGame returns null). Uploads:
  /// matrices (matModel/viewProj/normalMatrix/cameraPos), material (albedoColor, maps
  /// texture0..4, roughness/metallic/emissionColor/opacity/tiling/useNormalMap), lights
  /// (ambient + 1 directional + N point + M spot), and bones (boneMatrices[128], only when
  /// <paramref name="bones"/> is ValueSome).
  /// </summary>
  /// <param name="effect">The target effect (user-owned; its <c>CurrentTechnique</c> is selected by the caller).</param>
  /// <param name="view">Active camera view matrix.</param>
  /// <param name="projection">Active camera projection matrix.</param>
  /// <param name="cameraPos">Active camera world position.</param>
  /// <param name="world">The draw's world/model matrix.</param>
  /// <param name="normalMatrix">transpose(inverse(world)).</param>
  /// <param name="lights">The frame's accumulated lights.</param>
  /// <param name="bones">Bone palette (ValueSome for skinned draws; ValueNone otherwise).</param>
  /// <param name="material">The draw's material.</param>
  let uploadToEffect
    (
      gd: GraphicsDevice,
      effect: Effect,
      view: Matrix,
      projection: Matrix,
      cameraPos: Vector3,
      world: Matrix,
      normalMatrix: Matrix,
      lights: LightBuffers,
      shadows: ShadowResult voption,
      bones: Matrix[] voption,
      material: Material3D
    ) : unit =
    let p name = param effect name

    // ── Matrices ──
    setMatrix (p "matModel") world
    setMatrix (p "viewProj") (view * projection)
    setMatrix (p "normalMatrix") normalMatrix
    setVec3 (p "cameraPos") cameraPos

    // ── Material scalars/colors ──
    setVec4 (p "albedoColor") (colorToVec4 material.AlbedoColor)
    setFloat (p "roughness") material.Roughness
    setFloat (p "metallic") material.Metallic
    setVec4 (p "emissionColor") (colorToVec4 material.EmissionColor)
    setFloat (p "opacity") material.Opacity
    setVec2 (p "tiling") material.Tiling

    setInt
      (p "useNormalMap")
      (match material.NormalMap with
       | ValueSome _ -> 1
       | ValueNone -> 0)

    // ── Material textures (sampler textures bound via the effect's own params at Apply). ──
    let inline setTex (name: string) (t: Texture2D voption) =
      let pp = p name

      if not(obj.ReferenceEquals(pp, null)) then
        match t with
        | ValueSome tex -> pp.SetValue tex
        | ValueNone -> pp.SetValue(null: Texture)

    setTex "texture0" material.AlbedoMap
    setTex "texture1" material.RoughnessMap
    setTex "texture2" material.NormalMap
    setTex "texture3" material.MetallicMap
    setTex "texture4" material.EmissionMap

    // ── Lights: ambient (single slot) ──
    match lights.Ambient with
    | ValueSome a ->
      setVec3 (p "ambientColor") (a.Color.ToVector3())
      setFloat (p "ambientIntensity") a.Intensity
    | ValueNone ->
      setVec3 (p "ambientColor") Vector3.Zero
      setFloat (p "ambientIntensity") 0.0f

    // ── Lights: directional (single slot) ──
    match lights.DirLights.Count with
    | 0 ->
      setVec3 (p "dirLightDir") Vector3.Forward
      setVec3 (p "dirLightColor") Vector3.Zero
      setFloat (p "dirLightIntensity") 0.0f
    | _ ->
      let d = lights.DirLights[0]
      setVec3 (p "dirLightDir") d.Direction
      setVec3 (p "dirLightColor") (d.Color.ToVector3())
      setFloat (p "dirLightIntensity") d.Intensity

    // ── Lights: point (upload active count slots) ──
    let ptCount = min lights.PointLights.Count pointPos.Length

    let ptShadowIdx =
      match shadows with
      | ValueSome s -> s.PointLightShadowIdx
      | ValueNone -> null

    setInt (p "pointLightCount") ptCount

    for i = 0 to ptCount - 1 do
      let l = lights.PointLights[i]
      pointPos[i] <- l.Position
      pointColor[i] <- l.Color.ToVector3()
      pointIntensity[i] <- l.Intensity
      pointRadius[i] <- l.Radius
      pointFalloff[i] <- l.Falloff

      pointShadowIdx[i] <-
        if ptShadowIdx <> null && i < ptShadowIdx.Length then
          ptShadowIdx[i]
        else
          -1

    setVec3Array (p "pointLightPos") pointPos
    setVec3Array (p "pointLightColor") pointColor
    setFloatArray (p "pointLightIntensity") pointIntensity
    setFloatArray (p "pointLightRadius") pointRadius
    setFloatArray (p "pointLightFalloff") pointFalloff
    setIntArray (p "pointLightShadowIdx") pointShadowIdx

    // ── Lights: spot (upload active count slots) ──
    let spCount = min lights.SpotLights.Count spotPos.Length

    let spShadowIdx =
      match shadows with
      | ValueSome s -> s.SpotLightShadowIdx
      | ValueNone -> null

    setInt (p "spotLightCount") spCount

    for i = 0 to spCount - 1 do
      let l = lights.SpotLights[i]
      spotPos[i] <- l.Position
      spotDir[i] <- l.Direction
      spotColor[i] <- l.Color.ToVector3()
      spotIntensity[i] <- l.Intensity
      spotRadius[i] <- l.Radius
      spotInner[i] <- l.InnerCutoff
      spotOuter[i] <- l.OuterCutoff

      spotShadowIdx[i] <-
        if spShadowIdx <> null && i < spShadowIdx.Length then
          spShadowIdx[i]
        else
          -1

    setVec3Array (p "spotLightPos") spotPos
    setVec3Array (p "spotLightDir") spotDir
    setVec3Array (p "spotLightColor") spotColor
    setFloatArray (p "spotLightIntensity") spotIntensity
    setFloatArray (p "spotLightRadius") spotRadius
    setFloatArray (p "spotLightInnerCutoff") spotInner
    setFloatArray (p "spotLightOuterCutoff") spotOuter
    setIntArray (p "spotLightShadowIdx") spotShadowIdx

    // ── Shadows (opt-in: a user effect that declares these uniforms inherits shadow sampling). ──
    // The atlas texture is bound to sampler slot 5 (PointClamp) when shadows are active. Absent
    // uniforms are skipped (null), so an effect that doesn't declare shadow sampling is unaffected.
    match shadows with
    | ValueSome s ->
      setInt
        (p "dirLightCastsShadows")
        (if s.DirLightCastsShadows then 1 else 0)

      setMatrixArray (p "shadowViewProjs") s.ViewProjs
      setVec4Array (p "shadowUVOffsets") s.UVOffsets
      setVec2 (p "shadowTexelSize") (Vector2(s.TexelSize, s.TexelSize))
      // Bind the atlas to the effect's own sampler param (texture5) — like the material maps, the
      // sampler is read from the effect's parameters at Apply(), not gd.Textures[]. Also set slot 5
      // so an effect that samples the atlas via the fixed texture slot (not a named param) works.
      setTex5 (p "texture5") s.Atlas
      gd.Textures[5] <- s.Atlas
      gd.SamplerStates[5] <- SamplerState.PointClamp
    | ValueNone -> setInt (p "dirLightCastsShadows") 0

    // ── Bones (only for skinned draws) ──
    match bones with
    | ValueSome bs -> setMatrixArray (p "boneMatrices") bs
    | ValueNone -> ()
