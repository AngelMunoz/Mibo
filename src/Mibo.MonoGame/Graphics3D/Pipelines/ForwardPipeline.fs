namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D

// ------------------------------------------------------------------
// Internal helpers
// ------------------------------------------------------------------

[<AutoOpen>]
module private ForwardHelpers =

  /// <summary>Per-frame forward-rendering state, threaded byref through dispatch.</summary>
  /// <remarks>
  /// Mirrors the <c>RendererState</c> pattern from <c>Renderer2D.fs</c>: a mutable struct
  /// threaded by reference so dispatch avoids heap allocation on the hot path.
  /// </remarks>
  [<Struct>]
  type ForwardState = {
    mutable HasCamera: bool
    mutable View: Matrix
    mutable Projection: Matrix
    mutable CurrentCamera: Camera3D
    mutable CurrentConfig: Camera3DConfig voption
    mutable SavedViewport: Viewport
  }

  /// <summary>
  /// Per-pipeline light accumulator. Created once at construction; cleared and repopulated
  /// each frame (mirrors the canonical raylib <c>LightBuffers</c> double-scan pattern).
  /// </summary>
  /// <remarks>
  /// <see cref="T:Mibo.Elmish.Graphics3D.PointLight3D"/> and
  /// <see cref="T:Mibo.Elmish.Graphics3D.SpotLight3D"/> are accumulated for parity with
  /// the raylib pipeline, but have no native <c>BasicEffect</c> equivalent — they are
  /// bound only by the custom PBR path (B9). See <c>applyLighting</c>.
  /// </remarks>
  type LightBuffers = {
    mutable Ambient: AmbientLight3D voption
    DirLights: ResizeArray<DirectionalLight3D>
    PointLights: ResizeArray<PointLight3D>
    SpotLights: ResizeArray<SpotLight3D>
  }

  /// <summary>Resets all light accumulators to empty.</summary>
  let inline clearLights(lights: LightBuffers) =
    lights.Ambient <- ValueNone
    lights.DirLights.Clear()
    lights.PointLights.Clear()
    lights.SpotLights.Clear()

  /// <summary>Builds the view + projection matrices for a MonoGame <see cref="T:Mibo.Elmish.Camera3D"/>.</summary>
  /// <remarks>
  /// Uses native XNA <c>CreateLookAt</c> / <c>CreatePerspectiveFieldOfView</c> /
  /// <c>CreateOrthographic</c> in the right-handed MonoGame convention. No transpose,
  /// no raylib <c>BeginMode3D</c> capture (those are raylib-internal; see AGENTS.md §6).
  /// </remarks>
  let buildMatrices(cam: Camera3D) : struct (Matrix * Matrix) =
    let view = Matrix.CreateLookAt(cam.Position, cam.Target, cam.Up)

    let projection =
      match cam.Projection with
      | CameraProjection.Perspective ->
        Matrix.CreatePerspectiveFieldOfView(
          cam.FovY,
          // Aspect is window-dependent; the pipeline recomputes per-frame using the
          // active viewport, but the camera itself carries no aspect field. Use 1.0
          // as a neutral default; callers wanting a specific aspect should set the
          // projection directly via a custom Effect (DrawMeshEffect).
          1.0f,
          cam.NearPlane,
          cam.FarPlane
        )
      | CameraProjection.Orthographic ->
        Matrix.CreateOrthographic(
          cam.FovY,
          cam.FovY,
          cam.NearPlane,
          cam.FarPlane
        )

    struct (view, projection)

  /// <summary>Applies accumulated lighting to a <see cref="T:Microsoft.Xna.Framework.Graphics.BasicEffect"/>.</summary>
  /// <remarks>
  /// <b>The native floor.</b> <c>BasicEffect</c> exposes 1 ambient slot + up to 3 directional
  /// light slots (<c>DirectionalLight0..2</c>). There is <b>no native point/spot light</b> —
  /// those <c>AddPointLight</c>/<c>AddSpotLight</c> accumulations are collected for parity
  /// and consumed only by the custom PBR pipeline (B9). Excess directionals (4+) are clamped.
  /// Unused directional slots are disabled. Fog is off. This is the documented limitation
  /// upgraded in B9.
  /// </remarks>
  /// <remarks>
  /// Hot path: the three light slots are unrolled (not looped over a temporary array) and
  /// <see cref="M:Microsoft.Xna.Framework.Color.ToVector3"/> is used directly, so this
  /// function performs zero per-call heap allocations.
  /// </remarks>
  let applyLighting(effect: BasicEffect, lights: LightBuffers) =
    // Ambient.
    match lights.Ambient with
    | ValueSome a ->
      effect.AmbientLightColor <- a.Color.ToVector3() * a.Intensity
    | ValueNone -> effect.AmbientLightColor <- Vector3.Zero

    // Up to 3 directional lights — clamp; disable the rest. Slots unrolled (no temp array)
    // because this runs once per BasicEffect draw on the hot path.
    let dirs = lights.DirLights
    let count = dirs.Count

    // Slot 0
    if count > 0 then
      let d = dirs[0]
      effect.DirectionalLight0.Enabled <- true
      effect.DirectionalLight0.Direction <- d.Direction
      effect.DirectionalLight0.DiffuseColor <- d.Color.ToVector3() * d.Intensity
    else
      effect.DirectionalLight0.Enabled <- false

    // Slot 1
    if count > 1 then
      let d = dirs[1]
      effect.DirectionalLight1.Enabled <- true
      effect.DirectionalLight1.Direction <- d.Direction
      effect.DirectionalLight1.DiffuseColor <- d.Color.ToVector3() * d.Intensity
    else
      effect.DirectionalLight1.Enabled <- false

    // Slot 2
    if count > 2 then
      let d = dirs[2]
      effect.DirectionalLight2.Enabled <- true
      effect.DirectionalLight2.Direction <- d.Direction
      effect.DirectionalLight2.DiffuseColor <- d.Color.ToVector3() * d.Intensity
    else
      effect.DirectionalLight2.Enabled <- false

    effect.FogEnabled <- false
    effect.PreferPerPixelLighting <- true

  /// <summary>
  /// Sets <c>World</c>/<c>View</c>/<c>Projection</c> on an effect via <see cref="T:Microsoft.Xna.Framework.Graphics.IEffectMatrices"/>
  /// when the effect implements it. Returns true if set; false if the effect does not
  /// implement the interface (caller may fall back to named parameters or skip).
  /// </summary>
  let trySetMatrices
    (effect: Effect)
    (world: Matrix)
    (view: Matrix)
    (projection: Matrix)
    : bool =
    // Type-test via box: F# requires this for interface downcasts off a sealed-ish
    // reference type in some inference configurations.
    match box effect with
    | :? IEffectMatrices as m ->
      m.World <- world
      m.View <- view
      m.Projection <- projection
      true
    | _ -> false

  /// <summary>
  /// Draws a single <see cref="T:Microsoft.Xna.Framework.Graphics.ModelMeshPart"/> manually
  /// (since <c>ModelMeshPart</c> has no <c>Draw()</c> method of its own). Binds its vertex/index
  /// buffers, applies the current technique pass, and issues <c>DrawIndexedPrimitives</c>.
  /// </summary>
  /// <remarks>
  /// The caller is responsible for configuring <c>part.Effect</c> (matrices + lighting) before
  /// calling this. This mirrors the body of <c>ModelMesh.Draw()</c> from the MonoGame source.
  /// </remarks>
  let drawPart(gd: GraphicsDevice, part: ModelMeshPart) =
    if part.PrimitiveCount > 0 then
      gd.SetVertexBuffer(part.VertexBuffer)
      gd.Indices <- part.IndexBuffer

      for p in part.Effect.CurrentTechnique.Passes do
        p.Apply()

        gd.DrawIndexedPrimitives(
          PrimitiveType.TriangleList,
          part.VertexOffset,
          part.StartIndex,
          part.PrimitiveCount
        )

  // ----------------------------------------------------------------
  // PBR (B9): Cook-Torrance effect parameter cache + upload helpers
  // ----------------------------------------------------------------

  /// <summary>
  /// Structural identity key for a <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/> —
  /// texture map references + scalar/color fields. Used to skip uniform re-uploads when
  /// consecutive PBR draws share the same material (mirrors the canonical raylib
  /// <c>MaterialKey</c> short-circuit). Texture fields use reference equality (a
  /// <c>Texture2D</c> has no stable numeric ID on MonoGame, unlike raylib's <c>.Id</c>).
  /// </summary>
  [<Struct>]
  type MaterialKey = {
    AlbedoMap: Texture2D
    RoughnessMap: Texture2D
    MetallicMap: Texture2D
    NormalMap: Texture2D
    EmissionMap: Texture2D
    AlbedoColor: Color
    Roughness: float32
    Metallic: float32
    EmissionColor: Color
    Opacity: float32
    TilingX: float32
    TilingY: float32
  }

  /// <summary>Builds a <see cref="MaterialKey"/> from a material (null for absent maps).</summary>
  let inline materialKey(mat: inref<Material3D>) : MaterialKey =
    let texOrNull(t: Texture2D voption) =
      match t with
      | ValueSome x -> x
      | ValueNone -> null

    {
      AlbedoMap = texOrNull mat.AlbedoMap
      RoughnessMap = texOrNull mat.RoughnessMap
      MetallicMap = texOrNull mat.MetallicMap
      NormalMap = texOrNull mat.NormalMap
      EmissionMap = texOrNull mat.EmissionMap
      AlbedoColor = mat.AlbedoColor
      Roughness = mat.Roughness
      Metallic = mat.Metallic
      EmissionColor = mat.EmissionColor
      Opacity = mat.Opacity
      TilingX = mat.Tiling.X
      TilingY = mat.Tiling.Y
    }

  /// <summary>
  /// Cached <see cref="T:Microsoft.Xna.Framework.Graphics.EffectParameter"/> handles for the
  /// PBR effect, resolved once on load. <c>null</c> entries are valid (absent uniform) and
  /// are skipped on upload — MonoGame returns <c>null</c> from <c>Parameters["name"]</c> when
  /// the uniform is optimized out, unlike raylib's <c>-1</c> silent no-op.
  /// </summary>
  [<Struct>]
  type PbrEffectParams = {
    // Matrices
    MatModel: EffectParameter
    ViewProj: EffectParameter
    NormalMatrix: EffectParameter
    CameraPos: EffectParameter
    // Material scalars/colors
    AlbedoColor: EffectParameter
    Roughness: EffectParameter
    Metallic: EffectParameter
    EmissionColor: EffectParameter
    Opacity: EffectParameter
    Tiling: EffectParameter
    UseNormalMap: EffectParameter
    // Ambient
    AmbientColor: EffectParameter
    AmbientIntensity: EffectParameter
    // Directional
    DirLightDir: EffectParameter
    DirLightColor: EffectParameter
    DirLightIntensity: EffectParameter
    // Point lights (array params; null if MAX_POINT_LIGHTS sized out)
    PointLightCount: EffectParameter
    PointLightPos: EffectParameter
    PointLightColor: EffectParameter
    PointLightIntensity: EffectParameter
    PointLightRadius: EffectParameter
    PointLightFalloff: EffectParameter
    // Spot lights
    SpotLightCount: EffectParameter
    SpotLightPos: EffectParameter
    SpotLightDir: EffectParameter
    SpotLightColor: EffectParameter
    SpotLightIntensity: EffectParameter
    SpotLightRadius: EffectParameter
    SpotLightInnerCutoff: EffectParameter
    SpotLightOuterCutoff: EffectParameter
    // Shadow sampling (B10 directional; B11 multi-caster + per-light index)
    DirLightCastsShadows: EffectParameter
    ShadowViewProjs: EffectParameter
    ShadowUVOffsets: EffectParameter
    ShadowTexelSize: EffectParameter
    PointLightShadowIdx: EffectParameter
    SpotLightShadowIdx: EffectParameter
  }

  /// <summary>
  /// Cached <see cref="T:Microsoft.Xna.Framework.Graphics.EffectParameter"/> handles for
  /// the depth-only shadow pass effect (<c>DepthShadow.fx</c>).
  /// </summary>
  [<Struct>]
  type ShadowEffectParams = {
    MatModel: EffectParameter
    ViewProj: EffectParameter
  }

  let private param (e: Effect) (name: string) : EffectParameter =
    e.Parameters[name] // null when absent — callers null-check before SetValue.

  /// <summary>Builds the shadow-pass parameter handles once after load.</summary>
  let buildShadowParams(e: Effect) : ShadowEffectParams = {
    MatModel = param e "matModel"
    ViewProj = param e "viewProj"
  }

  /// <summary>A mesh draw collected for the shadow pass (caster geometry).</summary>
  [<Struct>]
  type ShadowMeshDraw = {
    Mesh: PrimitiveMesh
    Transform: Matrix
    /// World-space bounds (mesh.Bounds transformed by Transform). Precomputed at collection
    /// time so the per-caster frustum cull in runShadowPass is a single ContainmentType check.
    WorldBounds: BoundingSphere
  }

  /// <summary>Resolves all PBR effect parameter handles once after load.</summary>
  let buildPbrParams(e: Effect) : PbrEffectParams = {
    MatModel = param e "matModel"
    ViewProj = param e "viewProj"
    NormalMatrix = param e "normalMatrix"
    CameraPos = param e "cameraPos"
    AlbedoColor = param e "albedoColor"
    Roughness = param e "roughness"
    Metallic = param e "metallic"
    EmissionColor = param e "emissionColor"
    Opacity = param e "opacity"
    Tiling = param e "tiling"
    UseNormalMap = param e "useNormalMap"
    AmbientColor = param e "ambientColor"
    AmbientIntensity = param e "ambientIntensity"
    DirLightDir = param e "dirLightDir"
    DirLightColor = param e "dirLightColor"
    DirLightIntensity = param e "dirLightIntensity"
    PointLightCount = param e "pointLightCount"
    PointLightPos = param e "pointLightPos"
    PointLightColor = param e "pointLightColor"
    PointLightIntensity = param e "pointLightIntensity"
    PointLightRadius = param e "pointLightRadius"
    PointLightFalloff = param e "pointLightFalloff"
    SpotLightCount = param e "spotLightCount"
    SpotLightPos = param e "spotLightPos"
    SpotLightDir = param e "spotLightDir"
    SpotLightColor = param e "spotLightColor"
    SpotLightIntensity = param e "spotLightIntensity"
    SpotLightRadius = param e "spotLightRadius"
    SpotLightInnerCutoff = param e "spotLightInnerCutoff"
    SpotLightOuterCutoff = param e "spotLightOuterCutoff"
    DirLightCastsShadows = param e "dirLightCastsShadows"
    ShadowViewProjs = param e "shadowViewProjs"
    ShadowUVOffsets = param e "shadowUVOffsets"
    ShadowTexelSize = param e "shadowTexelSize"
    PointLightShadowIdx = param e "pointLightShadowIdx"
    SpotLightShadowIdx = param e "spotLightShadowIdx"
  }

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

  /// <summary>Converts an XNA <see cref="T:Microsoft.Xna.Framework.Color"/> to a normalized <see cref="T:Microsoft.Xna.Framework.Vector4"/>.</summary>
  let inline colorToVec4(c: Color) : Vector4 =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  // Pooled staging arrays for light uploads — sized to the shader's MAX_* constants,
  // reused across frames (no per-draw allocation on the hot path).
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
  /// Uploads accumulated lights (ambient + 1 directional + N point + M spot) to the PBR effect.
  /// Point/spot arrays upload only the active count; the shader early-outs via <c>*Count</c>.
  /// </summary>
  let uploadPbrLights
    (
      p: inref<PbrEffectParams>,
      lights: LightBuffers,
      pointShadowIdx: int[],
      spotShadowIdx: int[]
    ) =
    // Ambient (single slot; zeroes when absent).
    match lights.Ambient with
    | ValueSome a ->
      setVec3 p.AmbientColor (a.Color.ToVector3())
      setFloat p.AmbientIntensity a.Intensity
    | ValueNone ->
      setVec3 p.AmbientColor Vector3.Zero
      setFloat p.AmbientIntensity 0.0f

    // Directional (single slot; zeroes when absent).
    match lights.DirLights.Count with
    | 0 ->
      setVec3 p.DirLightDir Vector3.Forward
      setVec3 p.DirLightColor Vector3.Zero
      setFloat p.DirLightIntensity 0.0f
    | _ ->
      let d = lights.DirLights[0]
      setVec3 p.DirLightDir d.Direction
      setVec3 p.DirLightColor (d.Color.ToVector3())
      setFloat p.DirLightIntensity d.Intensity

    // Point lights — upload active count slots.
    let ptCount = min lights.PointLights.Count pointLightPosScratch.Length
    setInt p.PointLightCount ptCount

    for i = 0 to ptCount - 1 do
      let l = lights.PointLights[i]
      pointLightPosScratch[i] <- l.Position
      pointLightColorScratch[i] <- l.Color.ToVector3()
      pointLightIntensityScratch[i] <- l.Intensity
      pointLightRadiusScratch[i] <- l.Radius
      pointLightFalloffScratch[i] <- l.Falloff

      pointShadowIdxScratch[i] <-
        if i < pointShadowIdx.Length then pointShadowIdx[i] else -1

    setVec3Array p.PointLightPos pointLightPosScratch
    setVec3Array p.PointLightColor pointLightColorScratch
    setFloatArray p.PointLightIntensity pointLightIntensityScratch
    setFloatArray p.PointLightRadius pointLightRadiusScratch
    setFloatArray p.PointLightFalloff pointLightFalloffScratch
    setIntArray p.PointLightShadowIdx pointShadowIdxScratch

    // Spot lights — upload active count slots.
    let spCount = min lights.SpotLights.Count spotLightPosScratch.Length
    setInt p.SpotLightCount spCount

    for i = 0 to spCount - 1 do
      let l = lights.SpotLights[i]
      spotLightPosScratch[i] <- l.Position
      spotLightDirScratch[i] <- l.Direction
      spotLightColorScratch[i] <- l.Color.ToVector3()
      spotLightIntensityScratch[i] <- l.Intensity
      spotLightRadiusScratch[i] <- l.Radius
      spotLightInnerScratch[i] <- l.InnerCutoff
      spotLightOuterScratch[i] <- l.OuterCutoff

      spotShadowIdxScratch[i] <-
        if i < spotShadowIdx.Length then spotShadowIdx[i] else -1

    setVec3Array p.SpotLightPos spotLightPosScratch
    setVec3Array p.SpotLightDir spotLightDirScratch
    setVec3Array p.SpotLightColor spotLightColorScratch
    setFloatArray p.SpotLightIntensity spotLightIntensityScratch
    setFloatArray p.SpotLightRadius spotLightRadiusScratch
    setFloatArray p.SpotLightInnerCutoff spotLightInnerScratch
    setFloatArray p.SpotLightOuterCutoff spotLightOuterScratch
    setIntArray p.SpotLightShadowIdx spotShadowIdxScratch

  /// <summary>
  /// Uploads material scalars/colors. Callers gate this on a <c>MaterialKey</c>
  /// change to avoid re-uploading when consecutive draws share a material. The per-draw
  /// <c>normalMatrix</c> is NOT uploaded here — it depends on the transform, not the
  /// material, and must be set unconditionally on every draw.
  /// </summary>
  let uploadPbrMaterial(p: inref<PbrEffectParams>, mat: inref<Material3D>) =
    setVec4 p.AlbedoColor (colorToVec4 mat.AlbedoColor)
    setFloat p.Roughness mat.Roughness
    setFloat p.Metallic mat.Metallic
    setVec4 p.EmissionColor (colorToVec4 mat.EmissionColor)
    setFloat p.Opacity mat.Opacity
    setVec2 p.Tiling mat.Tiling

    let useNormal =
      match mat.NormalMap with
      | ValueSome _ -> 1
      | ValueNone -> 0

    setInt p.UseNormalMap useNormal

  /// <summary>Binds a material's 5 texture maps to sampler slots 0..4 (null = unbound).</summary>
  let bindPbrTextures(gd: GraphicsDevice, mat: inref<Material3D>) =
    let slot i (t: Texture2D voption) =
      match t with
      | ValueSome tex -> gd.Textures[i] <- tex
      | ValueNone -> gd.Textures[i] <- null

    slot 0 mat.AlbedoMap
    slot 1 mat.RoughnessMap
    slot 2 mat.NormalMap
    slot 3 mat.MetallicMap
    slot 4 mat.EmissionMap

// ------------------------------------------------------------------
// ForwardPipeline
// ------------------------------------------------------------------

/// <summary>
/// Native-first forward 3D pipeline for the MonoGame backend. Implements
/// <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/> by dispatching
/// <see cref="T:Mibo.Elmish.Graphics3D.Command3D"/> values and binding each
/// <see cref="T:Microsoft.Xna.Framework.Graphics.ModelMeshPart"/>'s own native effect
/// (<c>BasicEffect</c> etc.) with accumulated lighting.
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>native floor</b> described in the monogame3d plan (B5): a structurally
/// complete forward pipeline that binds native stock effects. It ports the dispatch
/// skeleton of <c>Mibo.Raylib/Graphics3D/Pipelines/ForwardPbrPipeline.fs</c> but binds
/// <c>BasicEffect</c> instead of custom PBR shaders. Shadows are stubbed (B10), billboards
/// and lines are stubbed (B8), custom PBR is added in B9.
/// </para>
/// <para>
/// Lighting budget: 1 ambient + up to 3 directional lights (<c>BasicEffect</c>'s limit).
/// Point/spot lights are accumulated but not bound natively — they require the custom PBR
/// pipeline (B9). Instanced/skinned dispatch wires fully in B7/B12; here <c>DrawSkinnedMesh</c>
/// binds a native <c>SkinnedEffect</c> if present.
/// </para>
/// <para>
/// Register via:
/// <code lang="fsharp">
/// Renderer3D.create (ForwardPipeline()) view
/// </code>
/// </para>
/// </remarks>
type ForwardPipeline
  (
    [<Struct>] ?postProcess: PostProcessConfig3D,
    [<Struct>] ?shadowAtlas: ShadowAtlasConfig,
    [<Struct>] ?shadowBias: ShadowBiasConfig
  ) =

  let ppConfig = ValueOption.defaultValue PostProcessConfig3D.none postProcess
  let atlasCfg = ValueOption.defaultValue ShadowAtlasConfig.defaults shadowAtlas
  let biasCfg = ValueOption.defaultValue ShadowBiasConfig.defaults shadowBias

  let lights: LightBuffers = {
    Ambient = ValueNone
    DirLights = ResizeArray<DirectionalLight3D>(3)
    PointLights = ResizeArray<PointLight3D>(8)
    SpotLights = ResizeArray<SpotLight3D>(4)
  }

  // Reused each frame to avoid per-frame allocation. Sized generously; grows if a larger
  // model is seen. A raw array (not ResizeArray) so we can pass it directly to
  // Model.CopyAbsoluteBoneTransformsTo with zero per-frame allocation or copying.
  let mutable boneTransforms = Array.zeroCreate<Matrix> 64

  // Lazily-created BasicEffect for the DrawMeshPBR fallback path (used when the custom
  // PBR effect can't be loaded — e.g. missing embedded resource). Created on first
  // DrawMeshPBR against the actual GraphicsDevice passed to Execute.
  let mutable pbrFallbackEffect: BasicEffect voption = ValueNone

  // B9 PBR: the custom Cook-Torrance effect (loads from embedded .mgfx via ShaderLoader)
  // + its cached parameter handles + a MaterialKey short-circuit to skip uniform re-uploads
  // across consecutive draws sharing the same material. Created on first PBR draw.
  let mutable pbrEffect: Effect voption = ValueNone
  let mutable pbrParams: PbrEffectParams voption = ValueNone
  let mutable pbrHasLastMaterial = false
  let mutable pbrLastKey: MaterialKey = Unchecked.defaultof<MaterialKey>

  // B10 directional shadow atlas. ShadowAtlas owns the R32F RenderTarget2D (allocated
  // lazily against the real device on first shadow pass). shadowEffect is DepthShadow.fx
  // (writes non-linear depth to .r). shadowOrigin shadows the SetShadowOrigin command;
  // shadowsEnabled gates which meshes cast (EnableShadows/DisableShadows). The pooled
  // shadowDraws array holds the mesh draws collected for the shadow pass (gated by the
  // toggle), avoiding per-frame allocation.
  let shadowAtlas = ShadowAtlas(atlasCfg, biasCfg)
  let mutable shadowEffect: Effect voption = ValueNone
  let mutable shadowParams: ShadowEffectParams voption = ValueNone
  let mutable shadowOrigin: Vector3 voption = ValueNone
  // Cached RasterizerState for the shadow pass (polygon-offset bias + back-face culling).
  // Created once on first shadow pass; disposed in Shutdown. Avoids per-frame GPU-resource
  // allocation (new RasterizerState() creates a native ID3D11RasterizerState / GL state object).
  let mutable shadowRaster: RasterizerState = null
  // Pooled scratch for the shadow pass: caster mesh draws collected from the buffer.
  let mutable shadowDraws = Array.zeroCreate<ShadowMeshDraw> 64
  // Per-light shadow caster slot mapping (computed in runShadowPass, read in uploadPbrLights).
  // Indexed by lights.PointLights/SpotLights buffer position; -1 = no shadow.
  let mutable pointShadowSlots: int[] = [||]
  let mutable spotShadowSlots: int[] = [||]
  // Pooled scratch for the multi-caster shadowViewProjs/UVOffsets upload.
  let mutable shadowViewProjsScratch = Array.zeroCreate<Matrix> 16
  let mutable shadowUVOffsetsScratch = Array.zeroCreate<Vector4> 16
  // Reused per-caster frustum (updated in-place via .Matrix) to avoid per-frame heap allocation
  // in the shadow render loop.
  let shadowFrustum = BoundingFrustum(Matrix.Identity)

  // B7 instancing: the custom Instanced effect (loads from embedded .mgfx via ShaderLoader)
  // and a growable per-instance vertex buffer. The effect has instance input semantics
  // (TEXCOORD1..4) that no stock BasicEffect provides, so instancing needs custom HLSL.
  // Created on first DrawMeshInstanced against the real device.
  let mutable instancedEffect: Effect voption = ValueNone
  let mutable instanceVertexBuffer: VertexBuffer voption = ValueNone
  // CPU staging array — packed VertexInstanceWorld rows per instance. Grows as needed.
  let mutable instanceStaging = Array.zeroCreate<VertexInstanceWorld> 64

  // B8 billboards + lines: lazily-created unlit BasicEffects (one textured+alpha for
  // billboards, one vertex-color for lines) and a pooled CPU vertex staging array for
  // DrawUserIndexedPrimitives. Created on first use against the real device.
  let mutable billboardEffect: BasicEffect voption = ValueNone
  let mutable lineEffect: BasicEffect voption = ValueNone

  let mutable billboardStaging: VertexPositionColorTexture[] =
    Array.zeroCreate<VertexPositionColorTexture> 256
  // Shared index pattern for N quads: [0,1,2, 0,2,3] offset by quad*4. Grown on demand.
  let mutable billboardIndices: int[] = Array.zeroCreate<int>(64 * 6)
  // Reused across DrawLine3D calls — avoids per-call heap allocation on the hot path.
  let mutable lineStaging: VertexPositionColorTexture[] =
    Array.zeroCreate<VertexPositionColorTexture> 2

  // ----------------------------------------------------------------
  // Dispatch helpers
  // ----------------------------------------------------------------

  /// <summary>Handles <c>DrawMesh</c>: binds the part's own native effect + lighting, draws.</summary>
  member private _.handleDrawMesh
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      part: ModelMeshPart,
      transform: Matrix
    ) =
    let effect = part.Effect

    if trySetMatrices effect transform state.View state.Projection then
      match effect with
      | :? BasicEffect as be -> applyLighting(be, lights)
      | _ -> () // Non-BasicEffect (SkinnedEffect/custom): matrices set, lighting skipped.

    drawPart(gd, part)

  /// <summary>
  /// Handles <c>DrawMeshEffect</c>: overrides the part's effect with a user-supplied one.
  /// Sets matrices via <see cref="T:Microsoft.Xna.Framework.Graphics.IEffectMatrices"/> when
  /// available; does not apply the pipeline's accumulated lighting (the caller owns the effect).
  /// </summary>
  member private _.handleDrawMeshEffect
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      part: ModelMeshPart,
      transform: Matrix,
      effect: Effect
    ) =
    trySetMatrices effect transform state.View state.Projection |> ignore
    // Temporarily swap the part's effect to draw, then restore.
    let saved = part.Effect
    part.Effect <- effect

    try
      drawPart(gd, part)
    finally
      part.Effect <- saved

  /// <summary>
  /// Handles <c>DrawModel</c>: replicates <c>Model.Draw</c>'s bone-composition loop but
  /// injects the pipeline's accumulated lighting on each <c>BasicEffect</c>.
  /// </summary>
  member private _.handleDrawModel
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      model: Model,
      transform: Matrix
    ) =
    // Grow the pre-allocated bone array if this model has more bones than we've seen.
    // Reused across frames; never shrinks. Passed directly to CopyAbsoluteBoneTransformsTo
    // with zero per-frame allocation.
    let boneCount = model.Bones.Count

    if boneTransforms.Length < boneCount then
      boneTransforms <- Array.zeroCreate<Matrix> boneCount

    model.CopyAbsoluteBoneTransformsTo(boneTransforms)

    for mesh in model.Meshes do
      let world = boneTransforms[mesh.ParentBone.Index] * transform

      for effect in mesh.Effects do
        trySetMatrices effect world state.View state.Projection |> ignore

        match effect with
        | :? BasicEffect as be -> applyLighting(be, lights)
        | _ -> ()

      mesh.Draw()

  /// <summary>
  /// Handles <c>DrawSkinnedMesh</c>: binds the part's native effect (a <c>SkinnedEffect</c>
  /// when the content pipeline produced one) and uploads bone matrices. Full skinning
  /// animation is wired in B12; B5 binds the native effect so skinned models render.
  /// </summary>
  member private _.handleDrawSkinnedMesh
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      part: ModelMeshPart,
      transform: Matrix,
      bones: Matrix[]
    ) =
    let effect = part.Effect
    trySetMatrices effect transform state.View state.Projection |> ignore

    match effect with
    | :? SkinnedEffect as se -> se.SetBoneTransforms(bones)
    | _ -> () // Non-skinned effect: ignore bones (B12 handles custom skinning HLSL).

    drawPart(gd, part)

  /// <summary>
  /// Lazily loads the custom PBR <c>Effect</c> on first PBR draw against the real device.
  /// Returns <c>true</c> when <c>pbrEffect</c>/<c>pbrParams</c> are usable; <c>false</c> when
  /// the embedded resource is missing (caller falls back to <c>BasicEffect</c>).
  /// </summary>
  member private _.ensurePbrEffect(gd: GraphicsDevice) : bool =
    match pbrEffect with
    | ValueSome _ -> true
    | ValueNone ->
      match ShaderLoader.loadEffect gd "ForwardPbr" with
      | ValueSome e ->
        pbrParams <- ValueSome(buildPbrParams e)
        pbrEffect <- ValueSome e
        true
      | ValueNone -> false

  /// <summary>
  /// Lazily loads the depth-only shadow effect (<c>DepthShadow.fx</c>) on first shadow
  /// pass. Returns <c>true</c> when loaded; <c>false</c> when the embedded resource is
  /// missing (the shadow pass is skipped — forward rendering proceeds unshadowed).
  /// </summary>
  member private _.ensureShadowEffect(gd: GraphicsDevice) : bool =
    match shadowEffect with
    | ValueSome _ -> true
    | ValueNone ->
      match ShaderLoader.loadEffect gd "DepthShadow" with
      | ValueSome e ->
        shadowParams <- ValueSome(buildShadowParams e)
        shadowEffect <- ValueSome e
        true
      | ValueNone -> false

  /// <summary>
  /// Handles <c>DrawMeshPBR</c>: draws an effectless <see cref="T:Mibo.Elmish.Graphics3D.PrimitiveMesh"/>
  /// with a <c>Material3D</c>. Per §4.1, this is the only place <c>Material3D</c> is consumed.
  /// </summary>
  /// <remarks>
  /// B9 binds the custom Cook-Torrance <c>ForwardPbr.fx</c> (ambient + directional + point +
  /// spot, emission, opacity, tiling, optional normal map) with a <c>MaterialKey</c> short-circuit
  /// to skip uniform re-uploads across consecutive draws sharing a material. When the PBR effect
  /// can't be loaded (missing embedded resource), it falls back to the B5/B6 <c>BasicEffect</c>
  /// path that maps the albedo color only — preserving the smoke-testable floor.
  /// </remarks>
  member private this.handleDrawMeshPBR
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      mesh: PrimitiveMesh,
      transform: Matrix,
      material: Material3D
    ) =
    if this.ensurePbrEffect gd then
      match pbrEffect, pbrParams with
      | ValueSome e, ValueSome p ->
        // Technique: Standard (non-instanced, non-skinned).
        e.CurrentTechnique <- e.Techniques["Standard"]

        // Normal matrix = transpose(inverse(world)) (RH; §6.2).
        let mutable t = transform
        let mutable inv = Matrix.Identity
        Matrix.Invert(&t, &inv) |> ignore
        let normalMatrix = Matrix.Transpose inv

        setMatrix p.MatModel transform
        setMatrix p.ViewProj (state.View * state.Projection)
        setMatrix p.NormalMatrix normalMatrix
        setVec3 p.CameraPos state.CurrentCamera.Position

        // Upload material uniforms only when the material changes (MaterialKey short-circuit).
        let key = materialKey &material

        if not pbrHasLastMaterial || key <> pbrLastKey then
          uploadPbrMaterial(&p, &material)
          bindPbrTextures(gd, &material)
          pbrLastKey <- key
          pbrHasLastMaterial <- true

        uploadPbrLights(&p, lights, pointShadowSlots, spotShadowSlots)

        mesh.Draw(gd, e)
      | _ -> () // unreachable (ensurePbrEffect set both)
    else
      // ── BasicEffect fallback (B5/B6 floor) — albedo color only. ──
      let effect =
        match pbrFallbackEffect with
        | ValueSome e -> e
        | ValueNone ->
          let e = new BasicEffect(gd)
          pbrFallbackEffect <- ValueSome e
          e

      let c = material.AlbedoColor

      effect.DiffuseColor <-
        Vector3(
          float32 c.R / 255.0f,
          float32 c.G / 255.0f,
          float32 c.B / 255.0f
        )

      effect.Alpha <- material.Opacity
      effect.Texture <- null
      effect.TextureEnabled <- false
      effect.VertexColorEnabled <- false
      effect.World <- transform
      effect.View <- state.View
      effect.Projection <- state.Projection
      applyLighting(effect, lights)
      mesh.Draw(gd, effect)

  /// <summary>
  /// Handles <c>DrawMeshInstanced</c>: native hardware instancing via two vertex streams
  /// (stream 0 = the mesh's <c>VertexPositionNormalTexture</c>, stream 1 = per-instance
  /// world matrices packed as <see cref="T:Mibo.Elmish.Graphics3D.VertexInstanceWorld"/>
  /// TEXCOORD1..4 rows) and <see cref="M:Microsoft.Xna.Framework.Graphics.GraphicsDevice.DrawInstancedPrimitives"/>.
  /// </summary>
  /// <remarks>
  /// B9 prefers the PBR <c>Instanced</c> technique (full Cook-Torrance lighting, all light
  /// types). When the PBR effect can't be loaded, it falls back to the B7 minimal
  /// <c>Instanced.fx</c> (flat albedo + 1 directional light).
  /// <para>
  /// Per §6.1, matrices upload as plain <c>float4x4</c> with <c>mul(position, matrix)</c>
  /// (vector LEFT). For the PBR instanced technique, <c>matModel</c> and <c>normalMatrix</c>
  /// are unused: <c>VS_Instanced</c> composes the per-instance world from the TEXCOORD1..4
  /// rows and transforms normals by it directly (correct for uniform-scale instances —
  /// rotation is orthogonal, so inverse-transpose == world).
  /// </para>
  /// </remarks>
  member private this.handleDrawMeshInstanced
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      mesh: PrimitiveMesh,
      transforms: Matrix[],
      material: Material3D,
      instanceCount: int
    ) =
    if instanceCount <= 0 then
      () // Nothing to draw.
    else
      // The instance staging array grows only when a larger batch is seen.
      if instanceStaging.Length < instanceCount then
        instanceStaging <- Array.zeroCreate<VertexInstanceWorld> instanceCount

      for i = 0 to instanceCount - 1 do
        instanceStaging[i] <- VertexInstanceWorld.Create transforms[i]

      // Lazily create / resize the instance vertex buffer.
      match instanceVertexBuffer with
      | ValueNone ->
        let vb =
          new VertexBuffer(
            gd,
            typeof<VertexInstanceWorld>,
            instanceCount,
            BufferUsage.WriteOnly
          )

        instanceVertexBuffer <- ValueSome vb
      | ValueSome vb when vb.VertexCount < instanceCount ->
        vb.Dispose()

        let vb' =
          new VertexBuffer(
            gd,
            typeof<VertexInstanceWorld>,
            instanceCount,
            BufferUsage.WriteOnly
          )

        instanceVertexBuffer <- ValueSome vb'
      | _ -> ()

      let instVB =
        match instanceVertexBuffer with
        | ValueSome vb -> vb
        | ValueNone -> Unchecked.defaultof<VertexBuffer> // unreachable (created above)

      instVB.SetData(instanceStaging, 0, instanceCount)

      // Bind two streams: mesh (per-vertex, freq 0) + instance (per-instance, freq 1).
      gd.SetVertexBuffers(
        VertexBufferBinding(mesh.Vertices, 0, 0),
        VertexBufferBinding(instVB, 0, 1)
      )

      gd.Indices <- mesh.Indices

      let viewProj = state.View * state.Projection

      if this.ensurePbrEffect gd then
        match pbrEffect, pbrParams with
        | ValueSome e, ValueSome p ->
          e.CurrentTechnique <- e.Techniques["Instanced"]

          // matModel + normalMatrix unused for instancing: VS_Instanced transforms
          // normals by the per-instance world matrix directly (rotation matrices are
          // orthogonal, so inverse-transpose = the matrix itself for uniform-scale).
          setMatrix p.ViewProj viewProj
          setVec3 p.CameraPos state.CurrentCamera.Position

          // Instanced draws always upload the material (no MaterialKey short-circuit — the
          // batch is one material across all instances).
          uploadPbrMaterial(&p, &material)
          bindPbrTextures(gd, &material)
          uploadPbrLights(&p, lights, pointShadowSlots, spotShadowSlots)

          for pass in e.CurrentTechnique.Passes do
            pass.Apply()

            gd.DrawInstancedPrimitives(
              PrimitiveType.TriangleList,
              0, // baseVertex
              0, // startIndex
              mesh.PrimitiveCount,
              instanceCount
            )
        | _ -> () // unreachable
      else
        // ── B7 fallback: minimal Instanced.fx (flat albedo + 1 directional). ──
        let effect =
          match instancedEffect with
          | ValueSome e -> e
          | ValueNone ->
            match ShaderLoader.loadEffect gd "Instanced" with
            | ValueSome e ->
              instancedEffect <- ValueSome e
              e
            | ValueNone -> Unchecked.defaultof<_>

        if obj.ReferenceEquals(effect, null) then
          ()
        else
          let c = material.AlbedoColor

          match effect.Parameters.["ViewProj"] with
          | null -> ()
          | pp -> pp.SetValue viewProj

          match effect.Parameters.["AlbedoColor"] with
          | null -> ()
          | p ->
            p.SetValue(
              Vector3(
                float32 c.R / 255.0f,
                float32 c.G / 255.0f,
                float32 c.B / 255.0f
              )
            )

          match effect.Parameters.["AmbientColor"] with
          | null -> ()
          | p ->
            let amb =
              match lights.Ambient with
              | ValueSome a -> a.Color.ToVector3() * a.Intensity
              | ValueNone -> Vector3.Zero

            p.SetValue amb

          match effect.Parameters.["DirLightDir"], lights.DirLights with
          | null, _ -> ()
          | p, dl when dl.Count > 0 ->
            let d = dl[0]
            p.SetValue d.Direction

            match effect.Parameters.["DirLightColor"] with
            | null -> ()
            | pc -> pc.SetValue(d.Color.ToVector3() * d.Intensity)
          | _, _ ->
            match effect.Parameters.["DirLightColor"] with
            | null -> ()
            | pc -> pc.SetValue Vector3.Zero

          for pass in effect.CurrentTechnique.Passes do
            pass.Apply()

            gd.DrawInstancedPrimitives(
              PrimitiveType.TriangleList,
              0, // baseVertex
              0, // startIndex
              mesh.PrimitiveCount,
              instanceCount
            )

  // ----------------------------------------------------------------
  // B8: Billboards + lines
  // ----------------------------------------------------------------

  member private _.ensureBillboardEffect(gd: GraphicsDevice) : BasicEffect =
    match billboardEffect with
    | ValueSome e -> e
    | ValueNone ->
      let e = new BasicEffect(gd)
      e.TextureEnabled <- true
      e.LightingEnabled <- false
      e.VertexColorEnabled <- true
      billboardEffect <- ValueSome e
      e

  member private _.ensureLineEffect(gd: GraphicsDevice) : BasicEffect =
    match lineEffect with
    | ValueSome e -> e
    | ValueNone ->
      let e = new BasicEffect(gd)
      e.TextureEnabled <- false
      e.LightingEnabled <- false
      e.VertexColorEnabled <- true
      lineEffect <- ValueSome e
      e

  // Emits a single camera-facing quad into the staging array at quadIndex*4.
  // UVs are normalized to [0,1] from the pixel-space source rect (BasicEffect samples
  // in normalized space — the Renderer2D lit-quad path uses the same convention).
  static member private EmitQuad
    (
      staging: VertexPositionColorTexture[],
      offset: int,
      world: Matrix,
      size: Vector2,
      color: Color,
      texWidth: float32,
      texHeight: float32,
      texRect: Rectangle
    ) =
    let halfW = size.X * 0.5f
    let halfH = size.Y * 0.5f
    // Unit quad corners (centered on origin, +Y up, +X right), transformed by the billboard matrix.
    let c0 = Vector3.Transform(Vector3(-halfW, -halfH, 0.0f), world)
    let c1 = Vector3.Transform(Vector3(halfW, -halfH, 0.0f), world)
    let c2 = Vector3.Transform(Vector3(halfW, halfH, 0.0f), world)
    let c3 = Vector3.Transform(Vector3(-halfW, halfH, 0.0f), world)
    let invW = 1.0f / texWidth
    let invH = 1.0f / texHeight
    let u0 = float32 texRect.X * invW
    let v0 = float32 texRect.Y * invH
    let u1 = float32(texRect.X + texRect.Width) * invW
    let v1 = float32(texRect.Y + texRect.Height) * invH

    staging[offset + 0] <-
      VertexPositionColorTexture(c0, color, Vector2(u0, v1))

    staging[offset + 1] <-
      VertexPositionColorTexture(c1, color, Vector2(u1, v1))

    staging[offset + 2] <-
      VertexPositionColorTexture(c2, color, Vector2(u1, v0))

    staging[offset + 3] <-
      VertexPositionColorTexture(c3, color, Vector2(u0, v0))

  member private this.handleDrawBillboard
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      texture: Texture2D,
      position: Vector3,
      size: Vector2,
      color: Color
    ) =
    let cam = state.CurrentCamera
    let camFwd = cam.Target - cam.Position
    let world = Matrix.CreateBillboard(position, cam.Position, cam.Up, camFwd)

    if billboardStaging.Length < 4 then
      billboardStaging <- Array.zeroCreate<VertexPositionColorTexture> 4

    ForwardPipeline.EmitQuad(
      billboardStaging,
      0,
      world,
      size,
      color,
      float32 texture.Width,
      float32 texture.Height,
      Rectangle(0, 0, texture.Width, texture.Height)
    )

    let effect = this.ensureBillboardEffect gd
    effect.Texture <- texture
    effect.World <- Matrix.Identity
    effect.View <- state.View
    effect.Projection <- state.Projection
    effect.Alpha <- 1.0f

    gd.BlendState <- BlendState.AlphaBlend
    gd.DepthStencilState <- DepthStencilState.DepthRead

    if billboardIndices.Length < 6 then
      billboardIndices <- Array.zeroCreate<int> 6

    billboardIndices[0] <- 0
    billboardIndices[1] <- 1
    billboardIndices[2] <- 2
    billboardIndices[3] <- 0
    billboardIndices[4] <- 2
    billboardIndices[5] <- 3

    for p in effect.CurrentTechnique.Passes do
      p.Apply()

      gd.DrawUserIndexedPrimitives(
        PrimitiveType.TriangleList,
        billboardStaging,
        0,
        4,
        billboardIndices,
        0,
        2
      )

    gd.DepthStencilState <- DepthStencilState.Default
    gd.BlendState <- BlendState.Opaque

  member private this.handleDrawBillboardBatch
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      textures: Texture2D[],
      positions: Vector3[],
      sizes: Vector2[],
      colors: Color[],
      count: int
    ) =
    if count <= 0 then
      ()
    else
      // NOTE: This batch path uses only textures[0] — a true multi-texture batch would need
      // a texture atlas or texture array. Splitting by texture (one draw call per distinct
      // texture) is the standard SpriteBatch approach; the sample's particles all share one
      // texture, so the common case is one draw call. Group by texture when that's not true.
      let cam = state.CurrentCamera
      let camFwd = cam.Target - cam.Position
      let texture = textures[0]
      let texW = float32 texture.Width
      let texH = float32 texture.Height
      let texRect = Rectangle(0, 0, texture.Width, texture.Height)

      let vertCount = count * 4
      let idxCount = count * 6

      if billboardStaging.Length < vertCount then
        billboardStaging <-
          Array.zeroCreate<VertexPositionColorTexture> vertCount

      if billboardIndices.Length < idxCount then
        billboardIndices <- Array.zeroCreate<int> idxCount

      for i = 0 to count - 1 do
        let world =
          Matrix.CreateBillboard(positions[i], cam.Position, cam.Up, camFwd)

        ForwardPipeline.EmitQuad(
          billboardStaging,
          i * 4,
          world,
          sizes[i],
          colors[i],
          texW,
          texH,
          texRect
        )

        let b = i * 6
        let v = i * 4
        billboardIndices[b + 0] <- v + 0
        billboardIndices[b + 1] <- v + 1
        billboardIndices[b + 2] <- v + 2
        billboardIndices[b + 3] <- v + 0
        billboardIndices[b + 4] <- v + 2
        billboardIndices[b + 5] <- v + 3

      let effect = this.ensureBillboardEffect gd
      effect.Texture <- texture
      effect.World <- Matrix.Identity
      effect.View <- state.View
      effect.Projection <- state.Projection
      effect.Alpha <- 1.0f

      gd.BlendState <- BlendState.AlphaBlend
      gd.DepthStencilState <- DepthStencilState.DepthRead

      for p in effect.CurrentTechnique.Passes do
        p.Apply()

        gd.DrawUserIndexedPrimitives(
          PrimitiveType.TriangleList,
          billboardStaging,
          0,
          vertCount,
          billboardIndices,
          0,
          count * 2
        )

      gd.DepthStencilState <- DepthStencilState.Default
      gd.BlendState <- BlendState.Opaque

  member private this.handleDrawLine3D
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      start: Vector3,
      finish: Vector3,
      color: Color
    ) =
    lineStaging[0] <- VertexPositionColorTexture(start, color, Vector2.Zero)
    lineStaging[1] <- VertexPositionColorTexture(finish, color, Vector2.Zero)

    let effect = this.ensureLineEffect gd
    effect.World <- Matrix.Identity
    effect.View <- state.View
    effect.Projection <- state.Projection
    effect.Alpha <- 1.0f

    gd.BlendState <- BlendState.AlphaBlend

    for p in effect.CurrentTechnique.Passes do
      p.Apply()
      gd.DrawUserPrimitives(PrimitiveType.LineList, lineStaging, 0, 1)

    gd.BlendState <- BlendState.Opaque

  // ----------------------------------------------------------------
  // Shadow pass (B10 — directional shadows only)
  // ----------------------------------------------------------------

  /// <summary>
  /// Builds the directional light's orthographic shadow ViewProj (port of canonical
  /// <c>createDirectionalShadowCamera</c>). Fixed-size ortho box positioned behind a
  /// grid-snapped origin along the light direction. §6.1: <c>CreateLookAt * CreateOrthographic</c>
  /// in application order, plain matrices, no transpose.
  /// </summary>
  member private _.buildDirectionalShadowViewProj
    (lightDir: Vector3, activeCamera: Camera3D)
    : Matrix =
    let lightFromDir = Vector3.Normalize(-lightDir)

    let rawOrigin =
      match shadowOrigin with
      | ValueSome origin -> origin
      | ValueNone ->
        match atlasCfg.OriginStrategy with
        | ShadowOriginStrategy.CameraTarget -> activeCamera.Target
        | ShadowOriginStrategy.SceneCenter -> Vector3.Zero
        | ShadowOriginStrategy.Custom f -> f activeCamera

    let snap = atlasCfg.GridSnapSize

    let snappedX =
      if snap > 0.0f then
        MathF.Round(rawOrigin.X / snap) * snap
      else
        rawOrigin.X

    let snappedZ =
      if snap > 0.0f then
        MathF.Round(rawOrigin.Z / snap) * snap
      else
        rawOrigin.Z

    let shadowOrigin = Vector3(snappedX, rawOrigin.Y, snappedZ)

    let lightDistance =
      match atlasCfg.DirectionalLightDistance with
      | ValueSome d -> d
      | ValueNone -> 100.0f

    let lightPos = shadowOrigin + lightFromDir * lightDistance

    let safeUp =
      if abs lightDir.Y > 0.99f then
        Vector3.UnitZ
      else
        Vector3.UnitY

    let orthoSize =
      match atlasCfg.DirectionalLightSize with
      | ValueSome s -> s
      | ValueNone -> 50.0f

    let shadowNear = 1.0f
    let shadowFar = lightDistance + orthoSize * 2.0f

    let view = Matrix.CreateLookAt(lightPos, shadowOrigin, safeUp)

    let proj =
      Matrix.CreateOrthographicOffCenter(
        -orthoSize,
        orthoSize,
        -orthoSize,
        orthoSize,
        shadowNear,
        shadowFar
      )

    view * proj

  /// <summary>
  /// Builds a point light's shadow ViewProj — a downward-facing 90° perspective frustum
  /// (matches canonical raylib; correct for ground-level point lights like the sample's
  /// mushrooms). §6.1: <c>CreateLookAt * CreatePerspectiveFieldOfView</c> in application order.
  /// </summary>
  member private _.buildPointShadowViewProj
    (lightPos: Vector3, lightRadius: float32)
    : Matrix =
    let view =
      Matrix.CreateLookAt(lightPos, lightPos - Vector3.UnitY, Vector3.UnitZ)

    // Dynamic near plane: CreatePerspectiveFieldOfView throws if near >= far.
    // Keep near strictly below half the radius so the frustum is always valid.
    let nearPlane = min 0.1f (lightRadius * 0.5f)

    let proj =
      Matrix.CreatePerspectiveFieldOfView(
        MathF.PI * 0.5f, // 90°
        1.0f,
        nearPlane,
        lightRadius
      )

    view * proj

  /// <summary>
  /// Builds a spot light's shadow ViewProj — a perspective frustum from the light position
  /// toward <c>pos + dir</c>, with FOV from the outer cutoff cone. §6.1 application order.
  /// </summary>
  member private _.buildSpotShadowViewProj(light: SpotLight3D) : Matrix =
    let lightDir = Vector3.Normalize light.Direction

    let safeUp =
      if abs lightDir.Y > 0.99f then
        Vector3.UnitZ
      else
        Vector3.UnitY

    let view =
      Matrix.CreateLookAt(light.Position, light.Position + lightDir, safeUp)

    // Outer cutoff is a cosine; half-angle FOV = acos(outerCutoff), full FOV = 2× that.
    let fov = 2.0f * MathF.Acos(light.OuterCutoff)

    // Dynamic near plane: CreatePerspectiveFieldOfView throws if near >= far.
    let nearPlane = min 0.1f (light.Radius * 0.5f)

    let proj =
      Matrix.CreatePerspectiveFieldOfView(fov, 1.0f, nearPlane, light.Radius)

    view * proj

  /// <summary>
  /// Runs the shadow pass: collects dir + point + spot casters (B11 extends B10's dir-only),
  /// renders depth to the atlas using <c>DepthShadow.fx</c> with
  /// <c>RasterizerState.SlopeScaleDepthBias</c>, then uploads shadow uniforms to the PBR
  /// effect for forward-pass sampling. Per-light frustum culling skips caster meshes outside
  /// each light's frustum (Layer 3 of the cost analysis).
  /// </summary>
  member private this.runShadowPass
    (gd: GraphicsDevice, state: byref<ForwardState>, buffer: RenderBuffer3D)
    =
    // Decide whether any light casts shadows (dir, point, or spot).
    let hasDirCaster = lights.DirLights |> Seq.exists(fun d -> d.CastsShadows)

    let hasPointCaster =
      lights.PointLights |> Seq.exists(fun p -> p.CastsShadows)

    let hasSpotCaster = lights.SpotLights |> Seq.exists(fun s -> s.CastsShadows)

    // Always init the per-light slot mappings (default -1 = no shadow). Even when no casters
    // exist, the forward pass reads these to upload pointLightShadowIdx/spotLightShadowIdx.
    // Reuse the arrays across frames (only reallocate if the light count grew) to avoid
    // per-frame heap allocation on the hot path.
    if pointShadowSlots.Length < lights.PointLights.Count then
      pointShadowSlots <- Array.create<int> lights.PointLights.Count -1
    else
      Array.Fill(pointShadowSlots, -1)

    if spotShadowSlots.Length < lights.SpotLights.Count then
      spotShadowSlots <- Array.create<int> lights.SpotLights.Count -1
    else
      Array.Fill(spotShadowSlots, -1)

    if not(hasDirCaster || hasPointCaster || hasSpotCaster) then
      match pbrParams with
      | ValueSome p -> setInt p.DirLightCastsShadows 0
      | ValueNone -> ()
    else
      // Lazily create the atlas RT + shadow effect.
      shadowAtlas.EnsureResources gd
      // Ensure the PBR effect is loaded BEFORE uploading shadow uniforms to it.
      this.ensurePbrEffect gd |> ignore

      if not(this.ensureShadowEffect gd) then
        () // DepthShadow.fx missing — render unshadowed.
      else
        match shadowEffect, shadowParams with
        | ValueSome depthEffect, ValueSome depthParams ->
          shadowAtlas.Clear()

          // ── Register casters: dir first (slot 0), then point, then spot ──
          // The flat shader-array index == registration order (matches PrepareUniforms).
          let mutable casterSlot = 0

          // Directional (first CastsShadows one only — matches canonical raylib).
          if hasDirCaster then
            let dirLight = lights.DirLights |> Seq.find(fun d -> d.CastsShadows)

            let vp =
              this.buildDirectionalShadowViewProj(
                dirLight.Direction,
                state.CurrentCamera
              )

            match
              shadowAtlas.AddCaster(
                ShadowCasterType.Directional,
                Vector3.Zero,
                dirLight.Direction,
                Vector3.Zero,
                true,
                ValueNone
              )
            with
            | ValueSome _ ->
              shadowAtlas.SetRegionViewProj(casterSlot, vp)
              casterSlot <- casterSlot + 1
            | ValueNone -> ()

          // Point lights. (for loop, not Seq.iteri — avoids per-frame enumerator allocation.)
          for i = 0 to lights.PointLights.Count - 1 do
            let pt = lights.PointLights[i]

            if pt.CastsShadows then
              let vp = this.buildPointShadowViewProj(pt.Position, pt.Radius)

              match
                shadowAtlas.AddCaster(
                  ShadowCasterType.Point,
                  pt.Position,
                  Vector3.Zero,
                  Vector3.Zero,
                  true,
                  pt.ShadowBias
                )
              with
              | ValueSome _ ->
                shadowAtlas.SetRegionViewProj(casterSlot, vp)
                pointShadowSlots[i] <- casterSlot
                casterSlot <- casterSlot + 1
              | ValueNone -> ()

          // Spot lights.
          for i = 0 to lights.SpotLights.Count - 1 do
            let sp = lights.SpotLights[i]

            if sp.CastsShadows then
              let vp = this.buildSpotShadowViewProj sp

              match
                shadowAtlas.AddCaster(
                  ShadowCasterType.Spot,
                  sp.Position,
                  sp.Direction,
                  sp.Position + sp.Direction,
                  true,
                  sp.ShadowBias
                )
              with
              | ValueSome _ ->
                shadowAtlas.SetRegionViewProj(casterSlot, vp)
                spotShadowSlots[i] <- casterSlot
                casterSlot <- casterSlot + 1
              | ValueNone -> ()

          if casterSlot = 0 then
            ()
          else
            // ── Collect caster meshes (PrimitiveMesh draws, gated by EnableShadows/DisableShadows) ──
            let mutable castEnabled = true
            let mutable drawCount = 0

            for i = 0 to buffer.Count - 1 do
              match buffer[i] with
              | Command3D.EnableShadows -> castEnabled <- true
              | Command3D.DisableShadows -> castEnabled <- false
              | Command3D.DrawMeshPBR(mesh, transform, _) when castEnabled ->
                if drawCount >= shadowDraws.Length then
                  Array.Resize(&shadowDraws, shadowDraws.Length * 2)

                // Precompute the world-space bounds for the frustum cull.
                // Transform the local-space bounds to world space (Transform handles scaling —
                // using the local radius directly would false-negative the cull on scaled meshes).
                let worldBounds = mesh.Bounds.Transform transform

                shadowDraws[drawCount] <- {
                  Mesh = mesh
                  Transform = transform
                  WorldBounds = worldBounds
                }

                drawCount <- drawCount + 1
              | _ -> ()

            if drawCount = 0 then
              ()
            else
              shadowAtlas.PrepareUniforms()

              // ── Render depth into the atlas ──
              let prevViewport = gd.Viewport
              let prevRaster = gd.RasterizerState
              let prevBlend = gd.BlendState
              let prevDepth = gd.DepthStencilState

              gd.SetRenderTarget(shadowAtlas.Fbo)
              gd.Clear(ClearOptions.Target, Color.White.ToVector4(), 1.0f, 0)

              // Native polygon-offset bias (replaces raylib's dFdx/dFdy shader math).
              // Cached (config-driven). For B11 the bias uses DirectionalBias as the base —
              // per-type bias swap is deferred (the shadowRaster caches one state; a per-caster
              // bias swap would need 3 rasterizers). Documented limitation.
              if obj.ReferenceEquals(shadowRaster, null) then
                let sr = new RasterizerState()
                sr.CullMode <- CullMode.CullClockwiseFace
                sr.DepthBias <- biasCfg.DirectionalBias
                sr.SlopeScaleDepthBias <- biasCfg.SlopeScaleBias
                shadowRaster <- sr

              gd.RasterizerState <- shadowRaster
              gd.BlendState <- BlendState.Opaque
              gd.DepthStencilState <- DepthStencilState.Default

              depthEffect.CurrentTechnique <- depthEffect.Techniques["Depth"]

              for caster in shadowAtlas.Casters do
                if caster.Enabled then
                  let casterVP =
                    shadowAtlas.GetRegionViewProj caster.AtlasRegion

                  gd.Viewport <-
                    shadowAtlas.GetRegionViewport(caster.AtlasRegion)
                  // Per-light frustum cull (Layer 3 of the cost analysis): skip meshes the
                  // caster's frustum can't see. Update the cached frustum in-place (avoids
                  // per-caster BoundingFrustum allocation every frame).
                  shadowFrustum.Matrix <- casterVP

                  for d = 0 to drawCount - 1 do
                    let draw = shadowDraws[d]

                    if Culling.isVisible shadowFrustum draw.WorldBounds then
                      setMatrix depthParams.MatModel draw.Transform
                      setMatrix depthParams.ViewProj casterVP

                      for pass in depthEffect.CurrentTechnique.Passes do
                        pass.Apply()
                        draw.Mesh.Draw(gd, depthEffect)

              // ── Restore device state ──
              (gd.SetRenderTarget null)
              gd.Viewport <- prevViewport
              gd.RasterizerState <- prevRaster
              gd.BlendState <- prevBlend
              gd.DepthStencilState <- prevDepth

              // ── Upload shadow uniforms to the PBR effect ──
              match pbrParams with
              | ValueSome p ->
                setInt p.DirLightCastsShadows (if hasDirCaster then 1 else 0)

                // Multi-caster: upload the full packed arrays (shadowViewProjs + shadowUVOffsets).
                let active = shadowAtlas.ActiveCasterCount

                if active > 0 then
                  if shadowViewProjsScratch.Length < active then
                    shadowViewProjsScratch <- Array.zeroCreate<Matrix> active
                    shadowUVOffsetsScratch <- Array.zeroCreate<Vector4> active

                  let vpArr = shadowAtlas.ViewProjs
                  let uvArr = shadowAtlas.UVOffsets

                  for i = 0 to active - 1 do
                    shadowViewProjsScratch[i] <- vpArr[i]
                    shadowUVOffsetsScratch[i] <- uvArr[i]

                  setMatrixArray p.ShadowViewProjs shadowViewProjsScratch
                  setVec4Array p.ShadowUVOffsets shadowUVOffsetsScratch

                let texel = 1.0f / float32 atlasCfg.Resolution
                setVec2 p.ShadowTexelSize (Vector2(texel, texel))

                gd.Textures[5] <- shadowAtlas.Fbo
                gd.SamplerStates[5] <- SamplerState.PointClamp
              | ValueNone -> ()
        | _ -> ()

  // ----------------------------------------------------------------
  // IRenderPipeline3D
  // ----------------------------------------------------------------

  interface IRenderPipeline3D with

    /// <summary>
    /// Called once at construction. The native floor needs no shader loading — effects
    /// come from the content pipeline / are created lazily. Reserved for B9 (PBR shader load).
    /// </summary>
    member _.Initialize() = ()

    /// <summary>
    /// Called once at disposal. Releases lazily-created GPU resources: the PBR effect, the
    /// PBR fallback effect, the B7 instanced effect + instance vertex buffer, and the B8
    /// billboard/line effects.
    /// </summary>
    member _.Shutdown() =
      match pbrEffect with
      | ValueSome e ->
        e.Dispose()
        pbrEffect <- ValueNone
        pbrParams <- ValueNone
        pbrHasLastMaterial <- false
      | ValueNone -> ()

      match pbrFallbackEffect with
      | ValueSome e ->
        e.Dispose()
        pbrFallbackEffect <- ValueNone
      | ValueNone -> ()

      match instancedEffect with
      | ValueSome e ->
        e.Dispose()
        instancedEffect <- ValueNone
      | ValueNone -> ()

      match instanceVertexBuffer with
      | ValueSome vb ->
        vb.Dispose()
        instanceVertexBuffer <- ValueNone
      | ValueNone -> ()

      match billboardEffect with
      | ValueSome e ->
        e.Dispose()
        billboardEffect <- ValueNone
      | ValueNone -> ()

      match lineEffect with
      | ValueSome e ->
        e.Dispose()
        lineEffect <- ValueNone
      | ValueNone -> ()

      shadowAtlas.Release()

      if not(obj.ReferenceEquals(shadowRaster, null)) then
        shadowRaster.Dispose()
        shadowRaster <- null

      match shadowEffect with
      | ValueSome e ->
        e.Dispose()
        shadowEffect <- ValueNone
        shadowParams <- ValueNone
      | ValueNone -> ()

    member this.Execute(gameCtx, buffer, _rtPool) =
      let gd = MonoGameGameContext.getGraphicsDevice gameCtx

      // ── Device defaults for opaque 3D rendering ──
      gd.DepthStencilState <- DepthStencilState.Default
      gd.RasterizerState <- RasterizerState.CullCounterClockwise
      gd.BlendState <- BlendState.Opaque
      gd.SamplerStates[0] <- SamplerState.LinearWrap

      // ── Step 1: Pre-scan — capture camera + lights + shadow state ──
      clearLights lights
      shadowOrigin <- ValueNone

      let mutable state: ForwardState = {
        HasCamera = false
        View = Matrix.Identity
        Projection = Matrix.Identity
        CurrentCamera = Unchecked.defaultof<Camera3D>
        CurrentConfig = ValueNone
        SavedViewport = gd.Viewport
      }

      // Pre-scan: lights, camera, and shadow commands (shadow origin / toggle) need to be
      // known before the shadow pass runs. Draw commands are handled in the forward pass.
      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.BeginCamera cam ->
          let struct (v, p) = buildMatrices cam
          state.HasCamera <- true
          state.View <- v
          state.Projection <- p
          state.CurrentCamera <- cam
          state.CurrentConfig <- ValueNone

        | Command3D.BeginCameraConfig cfg ->
          let struct (v, p) = buildMatrices cfg.Camera
          state.HasCamera <- true
          state.View <- v
          state.Projection <- p
          state.CurrentCamera <- cfg.Camera
          state.CurrentConfig <- ValueSome cfg

        | Command3D.SetAmbientLight a -> lights.Ambient <- ValueSome a
        | Command3D.AddDirectionalLight d -> lights.DirLights.Add d
        | Command3D.AddPointLight p -> lights.PointLights.Add p
        | Command3D.AddSpotLight s -> lights.SpotLights.Add s
        | Command3D.SetShadowOrigin origin -> shadowOrigin <- ValueSome origin
        | _ -> ()

      // ── Step 2: Shadow pass (directional shadows only; B10) ──
      if state.HasCamera then
        this.runShadowPass(gd, &state, buffer)

      // ── Step 3: Forward pass ──
      // Lights + camera are already in `state`/`lights`; draw commands dispatch here.
      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        // ── Camera ──
        | Command3D.BeginCamera _ -> ()

        | Command3D.BeginCameraConfig cfg ->
          // Apply viewport + clear color (deferred from pre-scan so clearing happens here).
          match cfg.Viewport with
          | ValueSome rect -> gd.Viewport <- Viewport(rect)
          | ValueNone -> ()

          match cfg.ClearColor with
          | ValueSome c -> gd.Clear(ClearOptions.Target, c.ToVector4(), 1.0f, 0)
          | ValueNone -> ()

        | Command3D.EndCamera ->
          if state.HasCamera then
            // Restore fullscreen viewport + mark camera inactive so subsequent draws are skipped
            // until the next BeginCamera (matches the B5-B9 single-pass semantics; without this,
            // draws after EndCamera would dispatch with stale matrices).
            gd.Viewport <- state.SavedViewport
            state.HasCamera <- false

        // ── Drawing ──
        | Command3D.DrawMesh(part, transform) ->
          if state.HasCamera then
            this.handleDrawMesh(gd, &state, part, transform)

        | Command3D.DrawMeshEffect(part, transform, effect) ->
          if state.HasCamera then
            this.handleDrawMeshEffect(gd, &state, part, transform, effect)

        | Command3D.DrawModel(model, transform) ->
          if state.HasCamera then
            this.handleDrawModel(gd, &state, model, transform)

        | Command3D.DrawSkinnedMesh(part, transform, bones) ->
          if state.HasCamera then
            this.handleDrawSkinnedMesh(gd, &state, part, transform, bones)

        | Command3D.DrawMeshPBR(mesh, transform, material) ->
          if state.HasCamera then
            this.handleDrawMeshPBR(gd, &state, mesh, transform, material)

        | Command3D.DrawMeshInstanced(mesh, transforms, material, instanceCount) ->
          if state.HasCamera then
            this.handleDrawMeshInstanced(
              gd,
              &state,
              mesh,
              transforms,
              material,
              instanceCount
            )

        // ── Billboards / lines (B8) ──
        | Command3D.DrawBillboard(texture, position, size, color) ->
          if state.HasCamera then
            this.handleDrawBillboard(gd, &state, texture, position, size, color)

        | Command3D.DrawBillboardBatch(textures, positions, sizes, colors, count) ->
          if state.HasCamera then
            this.handleDrawBillboardBatch(
              gd,
              &state,
              textures,
              positions,
              sizes,
              colors,
              count
            )

        | Command3D.DrawLine3D(s, f, color) ->
          if state.HasCamera then
            this.handleDrawLine3D(gd, &state, s, f, color)

        // ── Lighting (already consumed in pre-scan; no-op here) ──
        | Command3D.SetAmbientLight _
        | Command3D.AddDirectionalLight _
        | Command3D.AddPointLight _
        | Command3D.AddSpotLight _ -> ()

        // ── Shadow state (consumed in the shadow pass; no-op here) ──
        | Command3D.SetShadowOrigin _
        | Command3D.EnableShadows
        | Command3D.DisableShadows -> ()

        // ── Escape hatch ──
        | Command3D.DrawImmediate action ->
          let savedHasCamera = state.HasCamera
          let savedViewport = gd.Viewport

          try
            action()
          finally
            // Restore viewport; camera state is logical (matrices), nothing to restore on gd.
            gd.Viewport <- savedViewport
            state.HasCamera <- savedHasCamera
      // Post-process gate: B5 ships with no passes (PostProcessConfig3D.none), so this
      // branch is never taken. The scene renders directly to the back-buffer. B9 wires
      // the full post-process chain.
      match ppConfig.Passes with
      | ValueNone
      | ValueSome [||] -> ()
      | _ ->
        // Full post-process ping-pong lands in B9. Until then, passes are unsupported.
        // Silently ignored rather than throwing so the pipeline stays usable.
        ()
