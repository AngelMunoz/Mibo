#nowarn "9"

namespace Mibo.Elmish.Graphics3D.Pipelines

open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// SceneUpload — shader-agnostic scene-data upload (the user-effect scope path).
//
// Resolves uniform NAMES on ANY raylib Shader and uploads the gathered scene
// (matrices + material + lights + bones + time + shadows). Absent locations
// return -1 from GetShaderLocation (raylib) and are skipped — so a shader that
// declares only a subset of the contract (e.g. a toon shader with
// matModel/viewProj/dirLight*/albedoColor) inherits exactly what it declares
// and nothing more.
//
// This is NOT the PBR hot path. The default pipeline shades via the cached
// ShaderLocations (the PBR handlers) to skip re-resolving names every draw.
// SceneUpload is the path a user-effect scope (beginEffect/endEffect) takes —
// name resolution per call is fine there because user-effect scopes are not
// per-frame-bulk hot paths.
//
// Mirrors the MonoGame backend's SceneUpload.fs contract. The shadow atlas is
// bound to texture slot 15 (the raylib convention the PBR path uses).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Shader-agnostic scene-data upload: resolves uniform names on any <see cref="T:Raylib_cs.Shader"/>.</summary>
/// <remarks>
/// <para>
/// <b>What is uploaded</b> (when the uniform is present on the target shader; absent locations are
/// skipped — raylib returns -1 from <c>GetShaderLocation</c>): matrices
/// (<c>matModel</c>/<c>viewProj</c>/<c>normalMatrix</c>/<c>cameraPos</c>), material
/// (<c>albedoColor</c>, <c>texture0..4</c>, <c>roughness</c>/<c>metallic</c>/<c>emissionColor</c>/
/// <c>opacity</c>/<c>tiling</c>/<c>useNormalMap</c>), lights (ambient + 1 directional + N point +
/// M spot, including per-light shadow indices), shadows (the atlas + <c>shadowViewProjs[]</c>/
/// <c>shadowUVOffsets[]</c>/<c>shadowTexelSize</c>/<c>dirLightCastsShadows</c>, when the frame has a
/// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ShadowResult"/>), and bones
/// (<c>boneMatrices</c>, only when supplied).
/// </para>
/// <para>
/// <b>Shadows are opt-in by declaration.</b> A user shader that wants shadow sampling declares the
/// shadow uniforms (<c>shadowViewProjs[]</c>, <c>shadowUVOffsets[]</c>, <c>shadowTexelSize</c>,
/// <c>dirLightCastsShadows</c>, <c>pointLightShadowIdx[]</c>, <c>spotLightShadowIdx[]</c>) and a
/// <c>shadowAtlas</c> sampler; when the frame's shadow pass produced an atlas, those uniforms are
/// uploaded by name and the atlas is bound to sampler slot 15. A shader that declares none of them
/// renders unshadowed — no cost, no sampling. When the frame has no shadow-casting light,
/// <c>dirLightCastsShadows</c> is set to 0 and nothing else shadow-related is uploaded.
/// </para>
/// </remarks>
module SceneUpload =

  // ── -1-safe setters. -1 location = absent uniform = no-op. ──
  // DisableRuntimeMarshalling requires fixed + NativePtr.toVoidPtr for scalars/vecs.
  let inline private setInt (shader: Shader) (loc: int) (v: int) =
    if loc >= 0 then
      use p = fixed &v

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Int
      )

  let inline private setFloat (shader: Shader) (loc: int) (v: float32) =
    if loc >= 0 then
      use p = fixed &v

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Float
      )

  let inline private setVec2 (shader: Shader) (loc: int) (v: Vector2) =
    if loc >= 0 then
      use p = fixed &v

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Vec2
      )

  let inline private setVec3 (shader: Shader) (loc: int) (v: Vector3) =
    if loc >= 0 then
      use p = fixed &v

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Vec3
      )

  let inline private setVec4 (shader: Shader) (loc: int) (v: Vector4) =
    if loc >= 0 then
      use p = fixed &v

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Vec4
      )

  let inline private loc (shader: Shader) (name: string) =
    Raylib.GetShaderLocation(shader, name)

  let inline private colorToVec4(c: Color) : Vector4 =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  let inline private colorToVec3(c: Mibo.Color) : Vector3 =
    Mibo.Color.toVector3 c

  /// <summary>
  /// Uploads the full scene-data contract to <paramref name="shader"/> by resolving each uniform
  /// by name. Absent uniforms are skipped (raylib returns -1). Uploads matrices (matModel/viewProj/
  /// normalMatrix/cameraPos), the <c>time</c> animation clock, material (albedoColor, maps, roughness/
  /// metallic/emissionColor/opacity/tiling/useNormalMap), lights (ambient + 1 directional + N point +
  /// M spot), shadows (when a ShadowResult is present), and bones (boneMatrices, only when
  /// <paramref name="bones"/> is ValueSome).
  /// </summary>
  /// <param name="shader">The target shader (user-owned).</param>
  /// <param name="view">Active camera view matrix.</param>
  /// <param name="projection">Active camera projection matrix.</param>
  /// <param name="cameraPos">Active camera world position.</param>
  /// <param name="world">The draw's world/model matrix.</param>
  /// <param name="normalMatrix">transpose(inverse(world)).</param>
  /// <param name="lights">The frame's accumulated lights.</param>
  /// <param name="shadows">The frame's shadow pass output (ValueNone when no shadow-casting light).</param>
  /// <param name="bones">Bone palette (ValueSome for skinned draws; ValueNone otherwise).</param>
  /// <param name="material">The draw's material.</param>
  /// <param name="time">Total elapsed game time, in seconds — the <c>time</c> uniform for animated shaders.</param>
  let uploadToShader
    (
      shader: Shader,
      view: Matrix4x4,
      projection: Matrix4x4,
      cameraPos: Vector3,
      world: Matrix4x4,
      normalMatrix: Matrix4x4,
      lights: LightBuffers,
      shadows: ShadowResult voption,
      bones: Matrix4x4[] voption,
      material: Material3D,
      time: float32
    ) : unit =
    // ── Matrices ──
    let matModelLoc = loc shader "matModel"
    let viewProjLoc = loc shader "viewProj"
    let normalMatrixLoc = loc shader "normalMatrix"
    let cameraPosLoc = loc shader "cameraPos"

    if matModelLoc >= 0 then
      Raylib.SetShaderValueMatrix(shader, matModelLoc, world)

    // Compose with Raymath.MatrixMultiply (not System.Numerics '*') so the result matches the
    // proven shadow VP (ForwardPbrPipeline: Rlgl matrices arrive in raylib's column-major rlMatrix
    // layout; the System.Numerics operator treats them as row-major and yields a different matrix).
    if viewProjLoc >= 0 then
      Raylib.SetShaderValueMatrix(
        shader,
        viewProjLoc,
        Raymath.MatrixMultiply(view, projection)
      )

    if normalMatrixLoc >= 0 then
      Raylib.SetShaderValueMatrix(shader, normalMatrixLoc, normalMatrix)

    setVec3 shader cameraPosLoc cameraPos

    // ── Time (animation clock; opt-in — absent on shaders that don't declare `time`) ──
    setFloat shader (loc shader "time") time

    // ── Material scalars/colors ──
    setVec4 shader (loc shader "albedoColor") (colorToVec4 material.AlbedoColor)
    setFloat shader (loc shader "roughness") material.Roughness
    setFloat shader (loc shader "metallic") material.Metallic

    setVec4
      shader
      (loc shader "emissionColor")
      (colorToVec4 material.EmissionColor)

    setFloat shader (loc shader "opacity") material.Opacity
    setVec2 shader (loc shader "tiling") material.Tiling

    setInt
      shader
      (loc shader "useNormalMap")
      (match material.NormalMap with
       | ValueSome _ -> 1
       | ValueNone -> 0)

    // ── Lights: ambient (single slot) ──
    match lights.Ambient with
    | ValueSome a ->
      setVec3 shader (loc shader "ambientColor") (colorToVec3 a.Color)
      setFloat shader (loc shader "ambientIntensity") a.Intensity
    | ValueNone ->
      setVec3 shader (loc shader "ambientColor") Vector3.Zero
      setFloat shader (loc shader "ambientIntensity") 0.0f

    // ── Lights: directional (single slot) ──
    match lights.DirLights.Count with
    | 0 ->
      setVec3 shader (loc shader "dirLightDir") Vector3.Zero
      setVec3 shader (loc shader "dirLightColor") Vector3.Zero
      setFloat shader (loc shader "dirLightIntensity") 0.0f
    | _ ->
      let d = lights.DirLights[0]
      setVec3 shader (loc shader "dirLightDir") d.Direction
      setVec3 shader (loc shader "dirLightColor") (colorToVec3 d.Color)
      setFloat shader (loc shader "dirLightIntensity") d.Intensity

    // ── Lights: point (upload active count slots, per-element names) ──
    let ptCount = lights.PointLights.Count

    let ptShadowIdx =
      match shadows with
      | ValueSome s -> s.PointLightShadowIdx
      | ValueNone -> null

    setInt shader (loc shader "pointLightCount") ptCount

    for i = 0 to ptCount - 1 do
      let l = lights.PointLights[i]
      setVec3 shader (loc shader $"pointLightPos[%d{i}]") l.Position

      setVec3
        shader
        (loc shader $"pointLightColor[%d{i}]")
        (colorToVec3 l.Color)

      setFloat shader (loc shader $"pointLightIntensity[%d{i}]") l.Intensity
      setFloat shader (loc shader $"pointLightRadius[%d{i}]") l.Radius
      setFloat shader (loc shader $"pointLightFalloff[%d{i}]") l.Falloff

      let idx =
        if ptShadowIdx <> null && i < ptShadowIdx.Length then
          ptShadowIdx[i]
        else
          -1

      setInt shader (loc shader $"pointLightShadowIdx[%d{i}]") idx

    // ── Lights: spot (upload active count slots, per-element names) ──
    let spCount = lights.SpotLights.Count

    let spShadowIdx =
      match shadows with
      | ValueSome s -> s.SpotLightShadowIdx
      | ValueNone -> null

    setInt shader (loc shader "spotLightCount") spCount

    for i = 0 to spCount - 1 do
      let s = lights.SpotLights[i]
      setVec3 shader (loc shader $"spotLightPos[%d{i}]") s.Position
      setVec3 shader (loc shader $"spotLightDir[%d{i}]") s.Direction
      setVec3 shader (loc shader $"spotLightColor[%d{i}]") (colorToVec3 s.Color)
      setFloat shader (loc shader $"spotLightIntensity[%d{i}]") s.Intensity
      setFloat shader (loc shader $"spotLightRadius[%d{i}]") s.Radius
      setFloat shader (loc shader $"spotLightInnerCutoff[%d{i}]") s.InnerCutoff
      setFloat shader (loc shader $"spotLightOuterCutoff[%d{i}]") s.OuterCutoff

      let idx =
        if spShadowIdx <> null && i < spShadowIdx.Length then
          spShadowIdx[i]
        else
          -1

      setInt shader (loc shader $"spotLightShadowIdx[%d{i}]") idx

    // ── Shadows (opt-in: a user shader that declares these uniforms inherits shadow sampling). ──
    // The atlas texture is bound to sampler slot 15 when shadows are active. Absent locations are
    // skipped (-1), so a shader that doesn't declare shadow sampling is unaffected.
    match shadows with
    | ValueSome s ->
      setInt
        shader
        (loc shader "dirLightCastsShadows")
        (if s.DirLightCastsShadows then 1 else 0)

      setFloat shader (loc shader "shadowTexelSize") s.TexelSize

      for i = 0 to s.ActiveCasterCount - 1 do
        if i < s.ViewProjs.Length then
          let vpLoc = loc shader $"shadowViewProjs[%d{i}]"

          if vpLoc >= 0 then
            Raylib.SetShaderValueMatrix(shader, vpLoc, s.ViewProjs[i])

        if i < s.UVOffsets.Length then
          setVec4 shader (loc shader $"shadowUVOffsets[%d{i}]") s.UVOffsets[i]

      // Bind the atlas to texture slot 15 (the raylib convention the PBR path uses).
      if s.Atlas.Id <> 0u then
        let atlasLoc = loc shader "shadowAtlas"

        if atlasLoc >= 0 then
          let slot = 15
          use p = fixed &slot

          Rlgl.SetUniform(
            atlasLoc,
            NativePtr.toVoidPtr p,
            int ShaderUniformDataType.Int,
            1
          )

        Rlgl.EnableShader shader.Id
        Rlgl.ActiveTextureSlot 15
        Rlgl.EnableTexture s.Atlas.Id
        Rlgl.ActiveTextureSlot 0
    | ValueNone -> setInt shader (loc shader "dirLightCastsShadows") 0

    // ── Bones (only for skinned draws) ──
    match bones with
    | ValueSome bs ->
      let boneLoc = loc shader "boneMatrices[0]"

      if boneLoc >= 0 then
        let count = min bs.Length 128

        for i = 0 to count - 1 do
          Raylib.SetShaderValueMatrix(shader, boneLoc + i, bs[i])
    | ValueNone -> ()
