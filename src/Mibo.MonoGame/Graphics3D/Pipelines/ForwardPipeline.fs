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

  // applyLighting moved to PbrShading.fs (private helper there) in the v2 refactor.

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

// MaterialKey + materialKey moved to PbrShading.fs (the PBR handlers own the short-circuit).

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

  // PBR shading: the lazily-loaded PBR effect + params, the BasicEffect fallback, the instancing
  // effect + growable instance vertex buffer + staging, the MaterialKey short-circuit cache, and the
  // bone-transforms scratch are all owned by PbrResources and driven by PbrShading.* (PbrShading.fs).
  let pbrRes = PbrResources()

  // Shadow pass: all shadow state (atlas, depth effect + params, origin, raster, pooled
  // caster/skinned/scratch arrays, per-light slot mappings, frustum, bone palette) is owned
  // by ShadowResources and driven by ShadowPass.run. See ShadowPass.fs.
  let shadowRes = ShadowResources(atlasCfg, biasCfg)
  // bonePaletteScratch is shared between the shadow pass and the forward-pass skinned handlers;
  // alias it from the shadow resources. (Read/written in place — never reassigned by either path.)
  let bonePaletteScratch = shadowRes.BonePaletteScratch

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
  // Per-draw shading hook — overridable (v2 pipeline-staging).
  //
  // The default implementation delegates to PbrShading.*: the cached PBR fast path for the
  // shaded draw kinds (model / animated model / primitive / instanced), or — when a user-effect
  // scope is open (BeginEffect) — name-resolved SceneUpload to the user effect. A subclass /
  // object expression overrides Shade to plug a different strategy while inheriting the
  // camera/light/shadow gather and forward-pass orchestration from Execute.
  //
  // activeEffect: ValueNone on the default path → PBR; ValueSome e → shade with the user effect
  // (it inherits scene DATA, not the PBR shader).
  // ----------------------------------------------------------------

  abstract Shade:
    gd: GraphicsDevice *
    state: byref<ForwardState> *
    frame: byref<ForwardFrame> *
    activeEffect: Effect voption *
    draw: Command3D ->
      unit

  default this.Shade(gd, state, frame, activeEffect, draw) =
    match activeEffect with
    | ValueNone ->
      // Default path: cached PBR fast path.
      match draw with
      | Command3D.DrawModel(model, transform) ->
        PbrShading.drawModel(gd, &state, &frame, pbrRes, model, transform)
      | Command3D.DrawAnimatedModel(model, transform, bones) ->
        PbrShading.drawAnimatedModel(
          gd,
          &state,
          &frame,
          pbrRes,
          model,
          transform,
          bones
        )
      | Command3D.DrawPrimitive(mesh, transform, material) ->
        PbrShading.drawPrimitive(
          gd,
          &state,
          &frame,
          pbrRes,
          mesh,
          transform,
          material
        )
      | Command3D.DrawInstanced(mesh, transforms, material, instanceCount) ->
        PbrShading.drawInstanced(
          gd,
          &state,
          &frame,
          pbrRes,
          mesh,
          transforms,
          material,
          instanceCount
        )
      | _ -> ()
    | ValueSome userEffect ->
      // Per-group scope: shade with the user effect via name-resolved SceneUpload. The effect
      // inherits scene data (camera/lights/material/bones), NOT the PBR shader itself (v2 §3).
      PbrShading.shadeWithEffect(gd, &state, &frame, pbrRes, userEffect, draw)


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

  // The four PBR draw handlers (handleDrawModel/handleDrawAnimatedModel/handleDrawPrimitive/
  // handleDrawInstanced), ensurePbrEffect, the MaterialKey short-circuit, the PBR effect+params,
  // the BasicEffect fallback, and the instancing effect/buffers all moved to PbrShading.fs
  // (PbrShading.* / PbrResources). The default Shade delegates to them.

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
    PbrShading.ensureEffect(gd, pbrRes) |> ignore

    ShadowPass.run
      gd
      atlasCfg
      biasCfg
      shadowRes
      lights
      pbrRes.Params
      buffer
      state.CurrentCamera
    |> fun r -> shadowRes.ShadowResult <- r // stash for the forward pass (Shade / user-effect scopes)

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
      match pbrRes.Effect with
      | ValueSome e ->
        e.Dispose()
        pbrRes.Effect <- ValueNone
        pbrRes.Params <- ValueNone
        pbrRes.HasLastMaterial <- false
      | ValueNone -> ()

      match pbrRes.FallbackEffect with
      | ValueSome e ->
        e.Dispose()
        pbrRes.FallbackEffect <- ValueNone
      | ValueNone -> ()

      match pbrRes.InstancedEffect with
      | ValueSome e ->
        e.Dispose()
        pbrRes.InstancedEffect <- ValueNone
      | ValueNone -> ()

      match pbrRes.InstanceVertexBuffer with
      | ValueSome vb ->
        vb.Dispose()
        pbrRes.InstanceVertexBuffer <- ValueNone
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

    member this.Execute(gameCtx, gameTime, buffer, _rtPool) =
      let gd = MonoGameGameContext.getGraphicsDevice gameCtx
      // Total elapsed game time, in seconds — captured once per frame for the scene bundle so an
      // animated custom shader (water ripples, flowing textures) has a `time` uniform to read.
      let frameTime = float32 gameTime.TotalTime.TotalSeconds

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
      // Lights + shadow state are already gathered; the camera is re-established per block
      // below. activeEffect tracks the per-group shading scope (beginEffect/endEffect, §7.2):
      // ValueNone → default PBR path; ValueSome e → shade with the user effect. Scopes do NOT
      // persist across cameras — a new camera block (BeginCamera/BeginCameraConfig) and EndCamera
      // both reset it, so a forgotten endEffect can't leak a user effect into the next view.
      let mutable activeEffect: Effect voption = ValueNone

      // Build the per-frame scene bundle once (lights, shared bone palette, per-light shadow slots,
      // the shadow pass output) and pass it byref to Shade for the whole forward pass. A struct —
      // no per-draw allocation. This is the bundle a Shade override (use case 1) receives.
      let mutable scene: ForwardFrame = {
        Lights = lights
        BonePaletteScratch = bonePaletteScratch
        PointShadowSlots = shadowRes.PointShadowSlots
        SpotShadowSlots = shadowRes.SpotShadowSlots
        Shadows = shadowRes.ShadowResult
        Time = frameTime
      }

      // The pre-scan left HasCamera/View/CurrentCamera on the *last* camera in the buffer
      // (needed for the shadow pass above). The forward pass must NOT inherit that: each
      // camera block establishes its own matrices, and draws outside any camera block are
      // skipped. So reset to "no active camera" before the forward loop.
      state.HasCamera <- false

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        // ── Camera ──
        | Command3D.BeginCamera cam ->
          // Re-establish this camera's view (the pre-scan left the LAST camera's view in
          // state; without this, multi-camera scenes render every view from the last one).
          let struct (v, _) = buildMatrices cam

          state.View <- v
          state.CurrentCamera <- cam
          state.HasCamera <- true

          // A fullscreen camera block restores the device to the fullscreen viewport.
          gd.Viewport <- state.SavedViewport

          // Recompute the projection aspect against the saved (fullscreen) viewport,
          // since buildMatrices used a neutral aspect=1.0.
          let vp = state.SavedViewport

          state.Projection <-
            perspectiveProjection cam (float32 vp.Width) (float32 vp.Height)

          // New camera block: scopes don't persist across cameras (§7.2).
          activeEffect <- ValueNone

        | Command3D.BeginCameraConfig cfg ->
          // Apply viewport + clear color (deferred from pre-scan so clearing happens here).
          match cfg.Viewport with
          | ValueSome rect -> gd.Viewport <- Viewport(rect)
          | ValueNone -> ()

          // Re-establish this camera's view (see BeginCamera note).
          let struct (v, _) = buildMatrices cfg.Camera

          state.View <- v
          state.CurrentCamera <- cfg.Camera
          state.HasCamera <- true

          // Recompute the projection aspect against the now-active viewport
          // (custom rect or fullscreen). buildMatrices used aspect=1.0.
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
            this.Shade(gd, &state, &scene, activeEffect, buffer[i])

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

        // ── Escape hatch: full device control + the gathered scene data ──
        | Command3D.DrawImmediate action ->
          let savedHasCamera = state.HasCamera
          let savedViewport = gd.Viewport

          let ctx: Pipelines.SceneContext = {
            Device = gd
            View = state.View
            Projection = state.Projection
            Camera = state.CurrentCamera
            Lights = lights
            Shadows = scene.Shadows
            Time = scene.Time
          }

          try
            action ctx
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
///       override _.Shade(gd, state, frame, activeEffect, draw) = ... }
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
