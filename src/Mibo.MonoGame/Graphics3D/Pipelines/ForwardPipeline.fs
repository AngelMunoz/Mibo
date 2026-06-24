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

/// <summary>Per-frame forward-rendering state, threaded byref through dispatch.</summary>
/// <remarks>
/// Mirrors the <c>RendererState</c> pattern from <c>Renderer2D.fs</c>: a mutable struct
/// threaded by reference so dispatch avoids heap allocation on the hot path. Public because the
/// staged base's virtual <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Shade"/>
/// exposes it (byref) to subclass / object-expression overrides — a shading strategy needs the
/// active camera's view/projection. It is repopulated each frame by the gather + forward-pass;
/// overrides read it, they should not mutate it.
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

[<AutoOpen>]
module private ForwardHelpers =

  // LightBuffers + clearLights moved to SceneData.fs (public) in Phase 1 of the v2
  // pipeline-staging work. ForwardPipeline references them as Pipelines.LightBuffers.

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
          // active viewport (see perspectiveProjection), but the camera itself carries
          // no aspect field. Use 1.0 as a neutral default; callers wanting a specific
          // aspect should set the projection directly via a custom Effect (DrawMeshEffect).
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

  /// <summary>
  /// Recomputes the perspective projection with the correct aspect ratio for the
  /// given viewport width/height. Called in the forward pass after the viewport is
  /// applied, since the camera carries no aspect field and the active viewport
  /// (custom or fullscreen) isn't known at pre-scan time. Orthographic cameras are
  /// returned unchanged (no aspect correction).
  /// </summary>
  let perspectiveProjection
    (cam: Camera3D)
    (viewportWidth: float32)
    (viewportHeight: float32)
    : Matrix =
    match cam.Projection with
    | CameraProjection.Perspective ->
      let aspect =
        if viewportHeight > 0.0f then
          viewportWidth / viewportHeight
        else
          1.0f

      Matrix.CreatePerspectiveFieldOfView(
        cam.FovY,
        aspect,
        cam.NearPlane,
        cam.FarPlane
      )
    | CameraProjection.Orthographic ->
      Matrix.CreateOrthographic(cam.FovY, cam.FovY, cam.NearPlane, cam.FarPlane)

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
  /// <summary>Applies accumulated lighting to any <see cref="T:Microsoft.Xna.Framework.Graphics.IEffectLights"/> effect (<c>BasicEffect</c>, <c>SkinnedEffect</c>, etc.).</summary>
  /// <remarks>
  /// <b>The native floor.</b> <c>IEffectLights</c> exposes 1 ambient slot + up to 3 directional
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
  let applyLighting(effect: IEffectLights, lights: LightBuffers) =
    // Ambient.
    match lights.Ambient with
    | ValueSome a ->
      effect.AmbientLightColor <- a.Color.ToVector3() * a.Intensity
    | ValueNone -> effect.AmbientLightColor <- Vector3.Zero

    // Up to 3 directional lights — clamp; disable the rest. Slots unrolled (no temp array)
    // because this runs once per effect draw on the hot path.
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

    // FogEnabled is on IEffectFog (BasicEffect/SkinnedEffect both implement it),
    // not on IEffectLights. PreferPerPixelLighting is on BasicEffect/SkinnedEffect
    // directly (no shared interface). Set both via type-test.
    match box effect with
    | :? IEffectFog as f -> f.FogEnabled <- false
    | _ -> ()

    match box effect with
    | :? BasicEffect as be -> be.PreferPerPixelLighting <- true
    | :? SkinnedEffect as se -> se.PreferPerPixelLighting <- true
    | _ -> ()

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

// PbrEffectParams (and its semantic sub-records Matrix/Material/Ambient/DirLight/
// PointLights/SpotLights/Shadow) moved to PbrUniforms.fs in the v2 pipeline-staging
// refactor. The upload helpers (uploadLights/uploadMaterial/bindTextures) + pooled
// light scratch arrays moved there too. ForwardPipeline references them as
// PbrUniforms.build / PbrUniforms.uploadLights / etc.

// ShadowEffectParams + buildShadowParams, ShadowMeshDraw, ShadowSkinnedDraw all moved
// to ShadowPass.fs in the v2 refactor (along with the pass body + the 3 ViewProj builders).

// buildPbrParams moved to PbrUniforms.fs (PbrUniforms.build).

// The null-safe setters (setVec2/.../setVec4Array/colorToVec4), the pooled light
// scratch arrays, and the PBR upload helpers (uploadLights/uploadMaterial/bindTextures)
// all moved to PbrUniforms.fs in the v2 refactor. Call sites reference them directly
// as PbrUniforms.* — no aliases.

// ------------------------------------------------------------------
// ForwardPipeline
// ------------------------------------------------------------------

/// <summary>
/// Staged forward 3D pipeline base for the MonoGame backend. Implements
/// <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/> by dispatching
/// <see cref="T:Mibo.Elmish.Graphics3D.Command3D"/> values, split into reusable stages —
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Execute"/> (orchestration),
/// the pre-scan gather, the shadow pass, and a virtual <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Shade"/>
/// for per-draw shading. The default <c>Shade</c> routes the shaded draw kinds (model / animated
/// model / primitive / instanced) through the custom Cook-Torrance PBR effect (<c>ForwardPbr.fx</c>),
/// so imported models and instanced geometry get PBR + point/spot lights + shadows automatically.
/// The only native-effect paths left are the billboards/lines (unlit <c>BasicEffect</c>) and
/// <c>DrawMeshEffect</c> (user-supplied effect escape hatch).
/// </summary>
/// <remarks>
/// <para>
/// Ports the dispatch skeleton of <c>Mibo.Raylib/Graphics3D/Pipelines/ForwardPbrPipeline.fs</c>,
/// adapted to MonoGame per the monogame3d plan §6 conventions (plain <c>float4x4</c>,
/// <c>mul(position, matrix)</c>, right-handed math, OpenGL SM3.0 cap).
/// <c>Material3D.fromModelMeshPart</c> reads each model part's baked native effect
/// (<c>BasicEffect</c>/<c>SkinnedEffect</c>) into a <c>Material3D</c> so the authored look
/// survives the swap to the PBR effect.
/// </para>
/// <para>
/// Lighting budget: 1 ambient + 1 directional + up to 8 point + up to 4 spot lights, all bound
/// to the PBR effect. Directional/point/spot shadows render to an <c>R32F</c> atlas
/// (<c>DepthShadow.fx</c>) and are sampled with manual 3×3 PCF.
/// </para>
/// <para>
/// Register via:
/// <code lang="fsharp">
/// Renderer3D.create (ForwardPipeline()) view
/// </code>
/// </para>
/// </remarks>
[<AbstractClass>]
type ForwardPipelineBase
  (
    ?postProcess: PostProcessConfig3D,
    ?shadowAtlas: ShadowAtlasConfig,
    ?shadowBias: ShadowBiasConfig
  ) =

  let ppConfig = defaultArg postProcess PostProcessConfig3D.none
  let atlasCfg = defaultArg shadowAtlas ShadowAtlasConfig.defaults
  let biasCfg = defaultArg shadowBias ShadowBiasConfig.defaults

  let lights: Pipelines.LightBuffers = Pipelines.LightBuffers.defaults

  // Reused each frame to avoid per-frame allocation. Sized generously; grows if a larger
  // model is seen. A raw array (not ResizeArray) so we can pass it directly to
  // Model.CopyAbsoluteBoneTransformsTo with zero per-frame allocation or copying.
  let mutable boneTransforms = Array.zeroCreate<Matrix> 64

  // Lazily-created BasicEffect for the DrawPrimitive fallback path (used when the custom
  // PBR effect can't be loaded — e.g. missing embedded resource). Created on first
  // DrawPrimitive against the actual GraphicsDevice passed to Execute.
  let mutable pbrFallbackEffect: BasicEffect voption = ValueNone

  // B9 PBR: the custom Cook-Torrance effect (loads from embedded .mgfx via ShaderLoader)
  // + its cached parameter handles + a MaterialKey short-circuit to skip uniform re-uploads
  // across consecutive draws sharing the same material. Created on first PBR draw.
  let mutable pbrEffect: Effect voption = ValueNone
  let mutable pbrParams: PbrEffectParams voption = ValueNone
  let mutable pbrHasLastMaterial = false
  let mutable pbrLastKey: MaterialKey = Unchecked.defaultof<MaterialKey>

  // Shadow pass: all shadow state (atlas, depth effect + params, origin, raster, pooled
  // caster/skinned/scratch arrays, per-light slot mappings, frustum, bone palette) is owned
  // by ShadowResources and driven by ShadowPass.run. See ShadowPass.fs.
  let shadowRes = ShadowResources(atlasCfg, biasCfg)
  // bonePaletteScratch is also written by the forward-pass skinned handlers (handleDrawAnimatedModel,
  // shadeWithEffect); alias it from the shadow resources so both paths share the one pooled array.
  // (The shadow pass reads/writes bonePaletteScratch in place; it never reassigns the field, so an
  //  alias is safe here — unlike the slot arrays, which ShadowPass.run can reassign.)
  let bonePaletteScratch = shadowRes.BonePaletteScratch

  // Instancing: the custom Instanced effect (loads from embedded .mgfx via ShaderLoader)
  // and a growable per-instance vertex buffer. The effect has instance input semantics
  // (TEXCOORD1..4) that no stock BasicEffect provides, so instancing needs custom HLSL.
  // Created on first DrawInstanced against the real device.
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
  // Staging hooks — overridable per-draw shading (v2 pipeline-staging).
  //
  // The default implementation is the Cook-Torrance PBR path: each shaded
  // draw kind (model / animated model / primitive / instanced) routes through
  // the custom ForwardPbr.fx via the handleDraw* members above. A subclass (or
  // object expression over ForwardPipeline) overrides Shade to plug a different
  // shading strategy while inheriting the camera/light/shadow gather and the
  // forward-pass orchestration from Execute.
  //
  // activeEffect: a user-effect scope opened by BeginEffect (Phase 2+4).
  // ValueNone on the default path → the pipeline's own PBR effect. The base
  // passes it through unchanged so the default PBR Shade is scope-unaware.
  // ----------------------------------------------------------------

  abstract Shade:
    gd: GraphicsDevice *
    state: byref<ForwardState> *
    activeEffect: Effect voption *
    draw: Command3D ->
      unit

  default this.Shade(gd, state, activeEffect, draw) =
    match activeEffect with
    | ValueNone ->
      // Default path: cached PBR fast path (no behavior change vs. pre-staging).
      this.shadePbr(gd, &state, draw)
    | ValueSome userEffect ->
      // Per-group scope: shade with the user effect via name-resolved SceneUpload.
      // The effect inherits scene data (camera/lights/material/bones), NOT the PBR shader.
      this.shadeWithEffect(gd, &state, userEffect, draw)

  /// <summary>Default PBR shading (cached <c>PbrEffectParams</c> fast path). Routes each shaded
  /// draw kind to its <c>handleDraw*</c> member.</summary>
  member private this.shadePbr(gd, state, draw) =
    match draw with
    | Command3D.DrawModel(model, transform) ->
      this.handleDrawModel(gd, &state, model, transform)
    | Command3D.DrawAnimatedModel(model, transform, bones) ->
      this.handleDrawAnimatedModel(gd, &state, model, transform, bones)
    | Command3D.DrawPrimitive(mesh, transform, material) ->
      this.handleDrawPrimitive(gd, &state, mesh, transform, material)
    | Command3D.DrawInstanced(mesh, transforms, material, instanceCount) ->
      this.handleDrawInstanced(
        gd,
        &state,
        mesh,
        transforms,
        material,
        instanceCount
      )
    | _ -> ()

  /// <summary>
  /// User-effect shading: uploads the gathered scene data (matrices + material + lights + bones)
  /// to <paramref name="effect"/> via <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload.uploadToEffect"/>
  /// (name-resolved; absent uniforms skipped), then draws through the effect's own
  /// <c>CurrentTechnique</c>. The effect inherits scene DATA, not the PBR shader (v2 §3).
  /// </summary>
  /// <remarks>
  /// <c>DrawInstanced</c> under a user scope falls back to the cached PBR instanced path: hardware
  /// instancing needs a vertex stream (TEXCOORD1..4) a generic inherited effect won't declare, so
  /// the inheritance contract doesn't cover it. Use the PBR <c>Instanced</c> technique for bulk.
  /// </remarks>
  member private this.shadeWithEffect(gd, state, effect, draw) =
    let camPos = state.CurrentCamera.Position

    // normalMatrix = transpose(inverse(world)) (RH; §6.2).
    let normalMatrixOf(world: Matrix) =
      let mutable t = world
      let mutable inv = Matrix.Identity
      Matrix.Invert(&t, &inv) |> ignore
      Matrix.Transpose inv

    match draw with
    | Command3D.DrawPrimitive(mesh, transform, material) ->
      SceneUpload.uploadToEffect(
        effect,
        state.View,
        state.Projection,
        camPos,
        transform,
        normalMatrixOf transform,
        lights,
        ValueNone,
        material
      )

      mesh.Draw(gd, effect)

    | Command3D.DrawModel(model, transform) ->
      let boneCount = model.Bones.Count

      if boneTransforms.Length < boneCount then
        boneTransforms <- Array.zeroCreate<Matrix> boneCount

      model.CopyAbsoluteBoneTransformsTo(boneTransforms)

      for mesh in model.Meshes do
        let world = boneTransforms[mesh.ParentBone.Index] * transform

        for part in mesh.MeshParts do
          let mat = Material3D.fromModelMeshPart part

          SceneUpload.uploadToEffect(
            effect,
            state.View,
            state.Projection,
            camPos,
            world,
            normalMatrixOf world,
            lights,
            ValueNone,
            mat
          )

          let saved = part.Effect
          part.Effect <- effect

          try
            drawPart(gd, part)
          finally
            part.Effect <- saved

    | Command3D.DrawAnimatedModel(model, transform, bones) ->
      let boneCount = model.Bones.Count

      if boneTransforms.Length < boneCount then
        boneTransforms <- Array.zeroCreate<Matrix> boneCount

      model.CopyAbsoluteBoneTransformsTo(boneTransforms)

      // Bone palette (tail zero-filled to identity) — matches the PBR skinned path. Uploaded
      // once per draw, shared across parts. A user effect with a boneMatrices[128] slot inherits it.
      let palCount = min bones.Length bonePaletteScratch.Length

      for i = 0 to palCount - 1 do
        bonePaletteScratch[i] <- bones[i]

      for i = palCount to bonePaletteScratch.Length - 1 do
        bonePaletteScratch[i] <- Matrix.Identity

      for mesh in model.Meshes do
        let world = boneTransforms[mesh.ParentBone.Index] * transform

        for part in mesh.MeshParts do
          let mat = Material3D.fromModelMeshPart part

          SceneUpload.uploadToEffect(
            effect,
            state.View,
            state.Projection,
            camPos,
            world,
            normalMatrixOf world,
            lights,
            ValueSome bonePaletteScratch,
            mat
          )

          let saved = part.Effect
          part.Effect <- effect

          try
            drawPart(gd, part)
          finally
            part.Effect <- saved

    | Command3D.DrawInstanced(mesh, transforms, material, instanceCount) ->
      // Instancing under a user scope falls back to the PBR path (see remarks).
      this.handleDrawInstanced(
        gd,
        &state,
        mesh,
        transforms,
        material,
        instanceCount
      )

    | _ -> ()

  // ----------------------------------------------------------------
  // Dispatch helpers
  // ----------------------------------------------------------------

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
  /// Handles <c>DrawModel</c>: routes every mesh part through the PBR effect. For each part
  /// the baked native effect (<c>BasicEffect</c>/<c>SkinnedEffect</c>) is read into a
  /// <c>Material3D</c> via <c>Material3D.fromModelMeshPart</c>, the part's effect is swapped
  /// to the PBR <c>Standard</c> technique around the draw, and lighting/shadows come from the
  /// pipeline's accumulated lights. This replaces the native-effect path: imported models now
  /// get PBR + point/spot lights + shadows instead of flat BasicEffect.
  /// </summary>
  member private this.handleDrawModel
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      model: Model,
      transform: Matrix
    ) =
    if this.ensurePbrEffect gd then
      match pbrEffect, pbrParams with
      | ValueSome e, ValueSome p ->
        let boneCount = model.Bones.Count

        if boneTransforms.Length < boneCount then
          boneTransforms <- Array.zeroCreate<Matrix> boneCount

        model.CopyAbsoluteBoneTransformsTo(boneTransforms)

        e.CurrentTechnique <- e.Techniques["Standard"]

        for mesh in model.Meshes do
          let world = boneTransforms[mesh.ParentBone.Index] * transform

          // normalMatrix = transpose(inverse(world)) (RH; §6.2)
          let mutable t = world
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore
          let normalMatrix = Matrix.Transpose inv

          PbrUniforms.setMatrix p.Matrix.MatModel world

          PbrUniforms.setMatrix
            p.Matrix.ViewProj
            (state.View * state.Projection)

          PbrUniforms.setMatrix p.Matrix.NormalMatrix normalMatrix
          PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

          PbrUniforms.uploadLights(
            &p,
            lights,
            shadowRes.PointShadowSlots,
            shadowRes.SpotShadowSlots
          )

          for part in mesh.MeshParts do
            let mat = Material3D.fromModelMeshPart part

            // Material uniform short-circuit (MaterialKey).
            let key = materialKey &mat

            if not pbrHasLastMaterial || key <> pbrLastKey then
              PbrUniforms.uploadMaterial(&p, &mat)
              PbrUniforms.bindTextures(&p, &mat)
              pbrLastKey <- key
              pbrHasLastMaterial <- true

            // Swap the part's effect to PBR around the draw — drawPart applies
            // part.Effect.CurrentTechnique.Passes, so the PBR technique must be bound
            // on the part's own Effect slot. Same pattern as the shadow pass.
            let saved = part.Effect
            part.Effect <- e

            try
              drawPart(gd, part)
            finally
              part.Effect <- saved
      | _ -> () // unreachable (ensurePbrEffect set both)

  /// <summary>
  /// Handles <c>DrawAnimatedModel</c>: routes the model's parts through PBR, applying the
  /// <c>Skinned</c> technique (with the supplied bone palette) to parts whose native effect is a
  /// <c>SkinnedEffect</c> (the content-pipeline signal that the vertex buffer carries
  /// <c>BLENDINDICES0</c>/<c>BLENDWEIGHT0</c>), and the <c>Standard</c> technique to the rest.
  /// The content pipeline bakes bone indices/weights but discards the animation clips, so bone
  /// matrices come from <c>Animation3DState.computeBonePalette</c> at runtime. The material
  /// (DiffuseColor/Texture/Alpha) is read per part via <c>Material3D.fromModelMeshPart</c>, the
  /// part's effect is swapped to PBR around the draw, and the bone palette is uploaded to the
  /// shader's <c>boneMatrices[128]</c>. The bone palette is uploaded once per draw (shared across
  /// all skinned parts), tail zero-filled to identity.
  /// </summary>
  member private this.handleDrawAnimatedModel
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      model: Model,
      transform: Matrix,
      bones: Matrix[]
    ) =
    if this.ensurePbrEffect gd then
      match pbrEffect, pbrParams with
      | ValueSome e, ValueSome p ->
        let boneCount = model.Bones.Count

        if boneTransforms.Length < boneCount then
          boneTransforms <- Array.zeroCreate<Matrix> boneCount

        model.CopyAbsoluteBoneTransformsTo(boneTransforms)

        // Bone palette (tail zero-filled to identity) — uploaded once per draw, shared by all
        // skinned parts. Matches the shadow pass's skinned-caster path.
        let palCount = min bones.Length bonePaletteScratch.Length

        for i = 0 to palCount - 1 do
          bonePaletteScratch[i] <- bones[i]

        for i = palCount to bonePaletteScratch.Length - 1 do
          bonePaletteScratch[i] <- Matrix.Identity

        for mesh in model.Meshes do
          let world = boneTransforms[mesh.ParentBone.Index] * transform

          let mutable t = world
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore

          PbrUniforms.setMatrix p.Matrix.MatModel world

          PbrUniforms.setMatrix
            p.Matrix.ViewProj
            (state.View * state.Projection)

          PbrUniforms.setMatrix p.Matrix.NormalMatrix (Matrix.Transpose inv)
          PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

          PbrUniforms.uploadLights(
            &p,
            lights,
            shadowRes.PointShadowSlots,
            shadowRes.SpotShadowSlots
          )

          for part in mesh.MeshParts do
            let isSkinned =
              match part.Effect with
              | :? SkinnedEffect -> true
              | _ -> false

            if isSkinned then
              e.CurrentTechnique <- e.Techniques["Skinned"]
              PbrUniforms.setMatrixArray p.Matrix.Bones bonePaletteScratch
            else
              e.CurrentTechnique <- e.Techniques["Standard"]

            let mat = Material3D.fromModelMeshPart part

            // Material uniform short-circuit (MaterialKey).
            let key = materialKey &mat

            if not pbrHasLastMaterial || key <> pbrLastKey then
              PbrUniforms.uploadMaterial(&p, &mat)
              PbrUniforms.bindTextures(&p, &mat)
              pbrLastKey <- key
              pbrHasLastMaterial <- true

            let saved = part.Effect
            part.Effect <- e

            try
              drawPart(gd, part)
            finally
              part.Effect <- saved
      | _ -> () // unreachable (ensurePbrEffect set both)

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
        pbrParams <- ValueSome(PbrUniforms.build e)
        pbrEffect <- ValueSome e
        true
      | ValueNone -> false

  // The depth-only shadow effect is now loaded lazily inside ShadowPass.run (against
  // shadowRes.Effect/Params), so this pipeline no longer owns an ensureShadowEffect hook.

  /// <summary>
  /// Handles <c>DrawPrimitive</c>: draws an effectless <see cref="T:Mibo.Elmish.Graphics3D.PrimitiveMesh"/>
  /// with a <c>Material3D</c> via the custom PBR effect.
  /// </summary>
  /// <remarks>
  /// Binds the custom Cook-Torrance <c>ForwardPbr.fx</c> (ambient + directional + point +
  /// spot, emission, opacity, tiling, optional normal map) with a <c>MaterialKey</c> short-circuit
  /// to skip uniform re-uploads across consecutive draws sharing a material. When the PBR effect
  /// can't be loaded (missing embedded resource), it falls back to the <c>BasicEffect</c>
  /// path that maps the albedo color only — preserving the smoke-testable floor.
  /// </remarks>
  member private this.handleDrawPrimitive
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

        PbrUniforms.setMatrix p.Matrix.MatModel transform
        PbrUniforms.setMatrix p.Matrix.ViewProj (state.View * state.Projection)
        PbrUniforms.setMatrix p.Matrix.NormalMatrix normalMatrix
        PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

        // Upload material uniforms only when the material changes (MaterialKey short-circuit).
        let key = materialKey &material

        if not pbrHasLastMaterial || key <> pbrLastKey then
          PbrUniforms.uploadMaterial(&p, &material)
          PbrUniforms.bindTextures(&p, &material)
          pbrLastKey <- key
          pbrHasLastMaterial <- true

        PbrUniforms.uploadLights(
          &p,
          lights,
          shadowRes.PointShadowSlots,
          shadowRes.SpotShadowSlots
        )

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
  /// Handles <c>DrawInstanced</c>: native hardware instancing via two vertex streams
  /// (stream 0 = the mesh's <c>VertexPositionNormalTexture</c>, stream 1 = per-instance
  /// world matrices packed as <see cref="T:Mibo.Elmish.Graphics3D.VertexInstanceWorld"/>
  /// TEXCOORD1..4 rows) and <see cref="M:Microsoft.Xna.Framework.Graphics.GraphicsDevice.DrawInstancedPrimitives"/>.
  /// </summary>
  /// <remarks>
  /// Prefers the PBR <c>Instanced</c> technique (full Cook-Torrance lighting, all light
  /// types). When the PBR effect can't be loaded, it falls back to the minimal
  /// <c>Instanced.fx</c> (flat albedo + 1 directional light).
  /// <para>
  /// Per §6.1, matrices upload as plain <c>float4x4</c> with <c>mul(position, matrix)</c>
  /// (vector LEFT). For the PBR instanced technique, <c>matModel</c> and <c>normalMatrix</c>
  /// are unused: <c>VS_Instanced</c> composes the per-instance world from the TEXCOORD1..4
  /// rows and transforms normals by it directly (correct for uniform-scale instances —
  /// rotation is orthogonal, so inverse-transpose == world).
  /// </para>
  /// </remarks>
  member private this.handleDrawInstanced
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
          PbrUniforms.setMatrix p.Matrix.ViewProj viewProj
          PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

          // Instanced draws always upload the material (no MaterialKey short-circuit — the
          // batch is one material across all instances).
          PbrUniforms.uploadMaterial(&p, &material)
          PbrUniforms.bindTextures(&p, &material)

          PbrUniforms.uploadLights(
            &p,
            lights,
            shadowRes.PointShadowSlots,
            shadowRes.SpotShadowSlots
          )

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

    ForwardPipelineBase.EmitQuad(
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

        ForwardPipelineBase.EmitQuad(
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
  // Shadow pass — delegates to ShadowPass.run (see ShadowPass.fs)
  // ----------------------------------------------------------------

  /// <summary>
  /// Runs the shadow pass: collects dir + point + spot casters, renders depth to the atlas, then
  /// uploads shadow uniforms to the PBR effect. The body lives in <c>ShadowPass.run</c>; this
  /// member just forwards the pipeline's resources + config. Ensures the PBR effect is loaded
  /// first (shadow uniforms upload to it).
  /// </summary>
  member private this.runShadowPass
    (gd: GraphicsDevice, state: byref<ForwardState>, buffer: RenderBuffer3D)
    =
    // Ensure the PBR effect is loaded BEFORE the pass uploads shadow uniforms to it.
    this.ensurePbrEffect gd |> ignore

    ShadowPass.run
      gd
      atlasCfg
      biasCfg
      shadowRes
      lights
      pbrParams
      buffer
      state.CurrentCamera

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

      shadowRes.Atlas.Release()

      if not(obj.ReferenceEquals(shadowRes.Raster, null)) then
        shadowRes.Raster.Dispose()
        shadowRes.Raster <- null

      match shadowRes.Effect with
      | ValueSome e ->
        e.Dispose()
        shadowRes.Effect <- ValueNone
        shadowRes.Params <- ValueNone
      | ValueNone -> ()

    member this.Execute(gameCtx, buffer, _rtPool) =
      let gd = MonoGameGameContext.getGraphicsDevice gameCtx

      // ── Device defaults for opaque 3D rendering ──
      gd.DepthStencilState <- DepthStencilState.Default
      gd.RasterizerState <- RasterizerState.CullCounterClockwise
      gd.BlendState <- BlendState.Opaque
      gd.SamplerStates[0] <- SamplerState.LinearWrap
      // PBR material maps (albedo s0, roughness s1, normal s2, metallic s3, emission s4)
      // and the shadow atlas (s5) all need explicit sampler states — the PS reads all of them.
      // Missing slots sampled the albedo map as black (the cube rendered black).
      gd.SamplerStates[1] <- SamplerState.LinearWrap
      gd.SamplerStates[2] <- SamplerState.LinearWrap
      gd.SamplerStates[3] <- SamplerState.LinearWrap
      gd.SamplerStates[4] <- SamplerState.LinearWrap
      // s5 is set per-shadow-pass to PointClamp; set a safe default here.
      gd.SamplerStates[5] <- SamplerState.PointClamp

      // ── Step 1: Pre-scan — capture camera + lights + shadow state ──
      Pipelines.LightBuffers.clear lights
      shadowRes.Origin <- ValueNone

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
        | Command3D.SetShadowOrigin origin ->
          shadowRes.Origin <- ValueSome origin
        | _ -> ()

      // ── Step 2: Shadow pass (directional shadows only; B10) ──
      if state.HasCamera then
        this.runShadowPass(gd, &state, buffer)

      // ── Step 3: Forward pass ──
      // Lights + camera are already in `state`/`lights`; draw commands dispatch here.
      // activeEffect tracks the per-group shading scope (beginEffect/endEffect, §7.2):
      // ValueNone → default PBR path; ValueSome e → shade with the user effect. Scopes do NOT
      // persist across cameras — a new camera block (BeginCamera/BeginCameraConfig) and EndCamera
      // both reset it, so a forgotten endEffect can't leak a user effect into the next view.
      let mutable activeEffect: Effect voption = ValueNone

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        // ── Camera ──
        | Command3D.BeginCamera _ ->
          // Recompute the projection aspect against the saved (fullscreen) viewport,
          // since buildMatrices used a neutral aspect=1.0 in the pre-scan.
          let vp = state.SavedViewport

          state.Projection <-
            perspectiveProjection
              state.CurrentCamera
              (float32 vp.Width)
              (float32 vp.Height)

          // New camera block: scopes don't persist across cameras (§7.2).
          activeEffect <- ValueNone

        | Command3D.BeginCameraConfig cfg ->
          // Apply viewport + clear color (deferred from pre-scan so clearing happens here).
          match cfg.Viewport with
          | ValueSome rect -> gd.Viewport <- Viewport(rect)
          | ValueNone -> ()

          // Recompute the projection aspect against the now-active viewport
          // (custom rect or fullscreen). buildMatrices used aspect=1.0 in the pre-scan.
          let vp = gd.Viewport

          state.Projection <-
            perspectiveProjection
              cfg.Camera
              (float32 vp.Width)
              (float32 vp.Height)

          match cfg.ClearColor with
          | ValueSome c -> gd.Clear(ClearOptions.Target, c.ToVector4(), 1.0f, 0)
          | ValueNone -> ()

          // New camera block: scopes don't persist across cameras (§7.2).
          activeEffect <- ValueNone

        | Command3D.EndCamera ->
          if state.HasCamera then
            // Restore fullscreen viewport + mark camera inactive so subsequent draws are skipped
            // until the next BeginCamera (matches the B5-B9 single-pass semantics; without this,
            // draws after EndCamera would dispatch with stale matrices).
            gd.Viewport <- state.SavedViewport
            state.HasCamera <- false

          // EndCamera closes any open effect scope (§7.2).
          activeEffect <- ValueNone

        // ── Per-group shading scope ──
        | Command3D.BeginEffect effect -> activeEffect <- ValueSome effect
        | Command3D.EndEffect -> activeEffect <- ValueNone

        // ── Drawing ──
        // Shaded draw kinds (model / animated model / primitive / instanced) go through the
        // virtual Shade so a subclass / object expression can override per-draw shading while
        // inheriting the camera/light/shadow gather and forward-pass orchestration. activeEffect
        // is the current scope (ValueNone on the default path). The default Shade branches on it:
        // PBR-cached fast path when None, SceneUpload name-resolved path when Some.
        | Command3D.DrawModel _
        | Command3D.DrawAnimatedModel _
        | Command3D.DrawPrimitive _
        | Command3D.DrawInstanced _ ->
          if state.HasCamera then
            this.Shade(gd, &state, activeEffect, buffer[i])

        | Command3D.DrawMeshEffect(part, transform, effect) ->
          if state.HasCamera then
            this.handleDrawMeshEffect(gd, &state, part, transform, effect)

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

// ------------------------------------------------------------------
// ForwardPipeline — the default PBR subclass (v2 pipeline-staging)
// ------------------------------------------------------------------

/// <summary>
/// The default MonoGame 3D forward pipeline: a thin <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase"/>
/// that inherits the camera/light/shadow gather and forward-pass orchestration unchanged, using
/// the base's default Cook-Torrance PBR <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ForwardPipelineBase.Shade"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered via:
/// <code lang="fsharp">
/// Renderer3D.create (ForwardPipeline()) view
/// </code>
/// </para>
/// <para>
/// To plug a different shading strategy (toon, cel, custom), build an object expression over
/// <c>ForwardPipeline()</c> and override <c>Shade</c> — the scene gather, shadow pass, and
/// forward-pass dispatch are inherited:
/// <code lang="fsharp">
/// let toon =
///   { new ForwardPipeline() with
///       override _.Shade(gd, state, activeEffect, draw) = ... }
/// </code>
/// </para>
/// </remarks>
type ForwardPipeline
  (
    ?postProcess: PostProcessConfig3D,
    ?shadowAtlas: ShadowAtlasConfig,
    ?shadowBias: ShadowBiasConfig
  ) =
  inherit
    ForwardPipelineBase(
      ?postProcess = postProcess,
      ?shadowAtlas = shadowAtlas,
      ?shadowBias = shadowBias
    )
