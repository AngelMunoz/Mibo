namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open MonoGame.Framework.Utilities
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D

// ------------------------------------------------------------------
// Internal helpers
// ------------------------------------------------------------------

[<AutoOpen>]
module private ForwardHelpers =

  // LightBuffers lives in SceneContext.fs; referenced here as Pipelines.LightBuffers.

  /// <summary>Maps a <see cref="T:Mibo.Elmish.Graphics2D.BlendMode"/> to the corresponding
  /// MonoGame <see cref="T:Microsoft.Xna.Framework.Graphics.BlendState"/> for the 3D pass.
  /// The 3D shaders (ForwardPbr, BasicEffect billboards/lines) output STRAIGHT color,
  /// so AlphaBlend maps to the straight-alpha blend state — premultiplied
  /// BlendState.AlphaBlend would add the tint at full strength regardless of alpha
  /// (deliberately differs from Renderer2D, whose SpriteBatch path is premultiplied).</summary>
  let toBlendState(mode: BlendMode) : BlendState =
    match mode with
    | BlendMode.AlphaBlend -> BlendState.NonPremultiplied
    | BlendMode.NonPremultiplied -> BlendState.NonPremultiplied
    | BlendMode.Additive -> BlendState.Additive
    | BlendMode.Opaque -> BlendState.Opaque

  /// <summary>Billboard depth state: opaque quads write depth; transparent ones only read.</summary>
  let toBillboardDepthState(mode: BlendMode) : DepthStencilState =
    match mode with
    | BlendMode.Opaque -> DepthStencilState.Default
    | _ -> DepthStencilState.DepthRead

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

  // applyLighting lives in PbrShading.fs (private helper there).

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

// ------------------------------------------------------------------
// Per-camera-block light scoping
// ------------------------------------------------------------------

/// <summary>
/// Light-state transitions for multi-camera-block frames: applying one light command in-order,
/// loading a materialized light set into a live buffer set, and resetting the live buffers at a
/// camera block's start.
/// </summary>
module internal LightScoping =

  /// <summary>Applies one light command in-order: ambient overwrites; directional/point/spot append.</summary>
  let inline apply (lights: LightBuffers) (cmd: Command3D) =
    match cmd with
    | Command3D.SetAmbientLight a -> lights.Ambient <- ValueSome a
    | Command3D.AddDirectionalLight d -> lights.DirLights.Add d
    | Command3D.AddPointLight p -> lights.PointLights.Add p
    | Command3D.AddSpotLight s -> lights.SpotLights.Add s
    | _ -> ()

  /// <summary>Loads a materialized light set into a live buffer set, replacing its contents.</summary>
  let inline loadSet (set: BlockLightSet) (lights: LightBuffers) =
    lights.Ambient <- set.Ambient
    lights.DirLights.Clear()
    lights.DirLights.AddRange(set.DirLights)
    lights.PointLights.Clear()
    lights.PointLights.AddRange(set.PointLights)
    lights.SpotLights.Clear()
    lights.SpotLights.AddRange(set.SpotLights)

  /// <summary>
  /// Applies one light command during the forward pass: to the live buffers always, and to the
  /// frame defaults when no camera block is open — between-block commands update the defaults,
  /// so a later block that resets sees them.
  /// </summary>
  let inline applyInOrder
    (lights: LightBuffers)
    (defaults: LightBuffers)
    (inBlock: bool)
    (cmd: Command3D)
    =
    apply lights cmd

    if not inBlock then
      apply defaults cmd

  /// <summary>
  /// Advances to the next camera block and, when that block carries its own light commands,
  /// resets the live buffers to the frame defaults — the block's commands are applied in-order
  /// as the forward loop reaches them. A block without light commands leaves the live buffers
  /// untouched (inheritance). Returns whether the buffers were reset.
  /// </summary>
  let inline resetForBlock
    (plan: BlockPlan)
    (defaults: LightBuffers)
    (lights: LightBuffers)
    (blockIndex: byref<int>)
    : bool =
    blockIndex <- blockIndex + 1

    if plan.Blocks[blockIndex].HasLightCommands then
      LightBuffers.copyInto defaults lights
      true
    else
      false

  /// <summary>
  /// Replays the buffer's camera and light commands the way the multi-camera-block forward
  /// pass does — both buffers start empty, between-block commands accumulate into the
  /// defaults, and each block resets (own commands) or inherits at its start — returning
  /// every block's live light set at its close. Test hook pinning that live shading matches
  /// the <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.BlockPlan"/>; allocates a snapshot per
  /// block, so it is not for the hot path.
  /// </summary>
  let replay
    (buffer: RenderBuffer3D)
    (plan: BlockPlan)
    (lights: LightBuffers)
    (defaults: LightBuffers)
    : BlockLightSet[] =
    let sets = ResizeArray<BlockLightSet>(plan.BlockCount)
    let mutable blockIndex = -1
    let mutable inBlock = false

    let closeBlock() =
      if inBlock then
        sets.Add {
          Ambient = lights.Ambient
          DirLights = lights.DirLights.ToArray()
          PointLights = lights.PointLights.ToArray()
          SpotLights = lights.SpotLights.ToArray()
        }

        inBlock <- false

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command3D.BeginCamera _
      | Command3D.BeginCameraConfig _ ->
        closeBlock()
        resetForBlock plan defaults lights &blockIndex |> ignore
        inBlock <- true
      | Command3D.EndCamera -> closeBlock()
      | Command3D.SetAmbientLight _
      | Command3D.AddDirectionalLight _
      | Command3D.AddPointLight _
      | Command3D.AddSpotLight _ as cmd ->
        applyInOrder lights defaults inBlock cmd
      | _ -> ()

    closeBlock()
    sets.ToArray()

// ── Where things live ──
// MaterialKey / materialKey          → PbrShading.fs (PBR handlers own the short-circuit)
// PbrEffectParams + upload helpers (uploadLights/uploadMaterial/bindTextures)
//   + pooled light scratch + null-safe setters (setVec2../setVec4Array/colorToVec4)
//   + buildPbrParams (PbrUniforms.build) → PbrUniforms.fs (referenced as PbrUniforms.*)
// ShadowEffectParams / buildShadowParams, ShadowMeshDraw / ShadowSkinnedDraw
//   + the shadow pass body + the 3 ViewProj builders → ShadowPass.fs

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
/// adapted to MonoGame conventions (plain <c>float4x4</c>,
/// <c>mul(position, matrix)</c>, right-handed math, OpenGL SM3.0 cap).
/// <c>Material3D.fromModelMeshPart</c> reads each model part's baked native effect
/// (<c>BasicEffect</c>/<c>SkinnedEffect</c>) into a <c>Material3D</c> so the authored look
/// survives the swap to the PBR effect.
/// </para>
/// <para>
/// Lighting budget: 1 ambient + 1 directional + up to 8 point + up to 4 spot lights, all bound
/// to the PBR effect. Directional/point/spot shadows render to an <c>R32F</c> atlas
/// (<c>DepthShadow.fx</c>) and are sampled with manual 3×3 PCF. In frames with more than one
/// camera block, lights are scoped per block: a block that issues light commands resets to the
/// frame defaults (light commands issued outside any camera block) plus its own commands,
/// applied in-order; a block without light commands inherits the previous block's set.
/// Single-camera frames gather lights frame-globally.
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
  (?shadowAtlas: ShadowAtlasConfig, ?shadowBias: ShadowBiasConfig) =

  let atlasCfg = defaultArg shadowAtlas ShadowAtlasConfig.defaults
  let biasCfg = defaultArg shadowBias ShadowBiasConfig.defaults

  // LightBuffers.create — LightBuffers.defaults is a shared module-level instance; two
  // pipelines built from it would alias each other's light accumulators.
  let lights: Pipelines.LightBuffers = Pipelines.LightBuffers.create 3 8 4

  // Frame-default light set for multi-camera-block frames: rebuilt in-order by the forward
  // pass each frame (between-block commands accumulate); a block that issues its own light
  // commands resets the live buffers from this.
  // LightBuffers.create — LightBuffers.defaults is a shared module-level instance.
  let defaultLights: Pipelines.LightBuffers =
    Pipelines.LightBuffers.create 3 8 4

  // Scratch for a block's final light set (loaded from the block plan) when running that
  // block's shadow pass — the live buffers trail the block's own in-order commands at block
  // start, so the pass can't read them.
  let blockLights: Pipelines.LightBuffers = Pipelines.LightBuffers.create 3 8 4

  // PBR shading: the lazily-loaded PBR effect + params, the BasicEffect fallback, the instancing
  // effect + growable instance vertex buffer + staging, the MaterialKey short-circuit cache, and the
  // bone-transforms scratch are all owned by PbrResources and driven by PbrShading.* (PbrShading.fs).
  let pbrRes = PbrResources()

  // Deferred transparent draws for the forward pass (materials with 0 < Opacity < 1): collected
  // inline by the default Shade, sorted far-to-near by camera distance at flush points (camera
  // boundaries, DrawImmediate, end of frame), then drawn after all opaque geometry with alpha
  // blending + depth-read. Grow-only, reused across frames.
  let transparentDraws = ResizeArray<TransparentEntry>()

  // Cached far-to-near comparer for the transparent sort — one object for the pipeline
  // lifetime, no per-frame allocation (List.Sort with a comparer sorts in place).
  let transparentComparer: IComparer<TransparentEntry> =
    { new IComparer<TransparentEntry> with
        member _.Compare(a, b) =
          let dist(e: TransparentEntry) =
            match e with
            | TransparentEntry.SingleDraw d -> d.DistanceSq
            | TransparentEntry.InstancedDraw d -> d.DistanceSq
            | TransparentEntry.SkinnedInstanceDraw d -> d.DistanceSq
            | TransparentEntry.SkinnedInstancedCommand d -> d.DistanceSq

          (dist b).CompareTo(dist a)
    }

  // Shadow pass: all shadow state (atlas, depth effect + params, origin, raster, pooled
  // caster/skinned/scratch arrays, per-light slot mappings, frustum, bone palette) is owned
  // by ShadowResources and driven by ShadowPass.run. See ShadowPass.fs.
  let shadowRes = ShadowResources(atlasCfg, biasCfg)
  // bonePaletteScratch is shared between the shadow pass and the forward-pass skinned handlers;
  // alias it from the shadow resources. (Read/written in place — never reassigned by either path.)
  let bonePaletteScratch = shadowRes.BonePaletteScratch

  // The palette-chunk cache is shared between the shadow pass and the forward pass: both
  // passes stage the same skinned-instanced palettes each frame, so the first pass to run
  // stages + uploads and the second reuses the same chunk textures (PaletteChunkCache).
  let paletteChunks = pbrRes.PaletteChunks
  do shadowRes.PaletteChunks <- paletteChunks

  // Same sharing for the per-instance world-row staging (InstanceWorldCache): the chunk
  // plan is shared, so one staging pass per frame serves both passes' VB uploads.
  let instanceWorlds = pbrRes.InstanceWorlds
  do shadowRes.InstanceWorlds <- instanceWorlds

  // B8 billboards + lines: lazily-created unlit BasicEffects (one textured+alpha for
  // billboards, one vertex-color for lines) and a pooled CPU vertex staging array for
  // DrawUserIndexedPrimitives. Created on first use against the real device.
  let mutable billboardEffect: BasicEffect voption = ValueNone
  let mutable lineEffect: BasicEffect voption = ValueNone

  // Lazily-created unlit vertex-color effect + triangle for clearing a camera block's
  // viewport region: gd.Clear ignores the viewport (D3D ClearRenderTargetView semantics),
  // so a block clear is drawn as an NDC fullscreen triangle, which covers exactly the
  // active viewport on every backend.
  let mutable clearEffect: BasicEffect = null
  let clearVerts = Array.zeroCreate<VertexPositionColor> 3

  // Post-process: a fullscreen quad created against the device on the first post-process frame.
  let mutable fullScreenQuad: FullScreenQuad voption = ValueNone

  // Scene depth (R32F, NDC z in [0,1]) for post-process distance effects (fog/DOF/SSAO). Lazily
  // created/resized, reused across frames, disposed at shutdown. Rendered by ShadowPass.renderSceneDepth
  // reusing the geometry the shadow pass already collected — no second buffer scan. Only produced when
  // the view emits PostProcessWithDepth actions; frames with only color-only PostProcess skip it.
  let mutable sceneDepthRT: RenderTarget2D voption = ValueNone

  let mutable billboardStaging: VertexPositionColorTexture[] =
    Array.zeroCreate<VertexPositionColorTexture> 256
  // Shared index pattern for N quads: [0,1,2, 0,2,3] offset by quad*4. Grown on demand.
  let mutable billboardIndices: int[] = Array.zeroCreate<int>(64 * 6)
  // Reused across DrawLine3D calls — avoids per-call heap allocation on the hot path.
  // 6 slots: 2 for the LineList path, 6 for the DX12 camera-facing quad path.
  let mutable lineStaging: VertexPositionColorTexture[] =
    Array.zeroCreate<VertexPositionColorTexture> 6

  // Half-width (world units) of the camera-facing quad the DX12 backend draws
  // for 3D lines — its PSOs hardcode a TRIANGLE topology type, so line
  // topologies render as triangles there (2-vertex draws render nothing).
  let line3DQuadHalfWidth = 0.015f

  // ----------------------------------------------------------------
  // Per-draw shading hook — overridable.
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
        PbrShading.drawModel(
          gd,
          &state,
          &frame,
          pbrRes,
          model,
          transform,
          ValueNone,
          transparentDraws
        )
      | Command3D.DrawModelWith(model, transform, matOverride) ->
        PbrShading.drawModel(
          gd,
          &state,
          &frame,
          pbrRes,
          model,
          transform,
          ValueSome matOverride,
          transparentDraws
        )
      | Command3D.DrawAnimatedModel(model, transform, bones) ->
        PbrShading.drawAnimatedModel(
          gd,
          &state,
          &frame,
          pbrRes,
          model,
          transform,
          bones,
          ValueNone,
          transparentDraws
        )
      | Command3D.DrawAnimatedModelWith(model, transform, bones, matOverride) ->
        PbrShading.drawAnimatedModel(
          gd,
          &state,
          &frame,
          pbrRes,
          model,
          transform,
          bones,
          ValueSome matOverride,
          transparentDraws
        )
      | Command3D.DrawPrimitive(mesh,
                                transform,
                                material,
                                vertexOffset,
                                startIndex) ->
        PbrShading.drawPrimitive(
          gd,
          &state,
          &frame,
          pbrRes,
          mesh,
          transform,
          material,
          vertexOffset,
          startIndex,
          transparentDraws
        )
      | Command3D.DrawInstanced(mesh,
                                transforms,
                                colors,
                                material,
                                instanceCount,
                                vertexOffset,
                                startIndex) ->
        // Same three-tier gate as the non-instanced draws: invisible draws nothing,
        // opaque (material and every instance color) draws inline, transparent defers
        // to the sorted pass as one batch keyed by its centroid.
        let count =
          if isNull transforms then
            0
          else
            min instanceCount transforms.Length

        if count > 0 && material.Opacity > 0.0f then
          if
            material.Opacity >= 1.0f
            && not(Opacity.anyTransparentInstanceColor colors)
          then
            PbrShading.drawInstanced(
              gd,
              &state,
              &frame,
              pbrRes,
              mesh,
              transforms,
              colors,
              material,
              instanceCount,
              vertexOffset,
              startIndex
            )
          else
            transparentDraws.Add(
              TransparentEntry.InstancedDraw {
                Mesh = mesh
                Transforms = transforms
                Colors = colors
                Material = material
                InstanceCount = count
                VertexOffset = vertexOffset
                StartIndex = startIndex
                DistanceSq =
                  Opacity.instanceCentroidDistanceSq(
                    state.CurrentCamera.Position,
                    transforms,
                    count
                  )
              }
            )
      | Command3D.DrawAnimatedModelInstanced(model,
                                             transforms,
                                             palettes,
                                             matOverride,
                                             colors,
                                             instanceCount,
                                             boneCount) ->
        // Skinned + instanced classification. A whole-model invisible override draws
        // nothing. On OpenGL the command goes straight through — its per-instance
        // fallback classifies per part/instance into the shared list (finer sort keys).
        // On the real-instancing backends any transparent part (or instance color)
        // defers the whole command as one batch — parts cannot split without losing
        // the single instanced draw call.
        let transformCount = if isNull transforms then 0 else transforms.Length

        let paletteLen = if isNull palettes then 0 else palettes.Length
        let count = min instanceCount transformCount

        let allInvisible =
          match matOverride with
          | ValueSome(MaterialOverride.All m) -> m.Opacity <= 0.0f
          | _ -> false

        if count > 0 && paletteLen > 0 && boneCount > 0 && not allInvisible then
          if Opacity.isOpenGLBackend() then
            PbrShading.drawAnimatedModelInstanced(
              gd,
              &state,
              &frame,
              pbrRes,
              SkinnedInstancedTarget.PbrTarget,
              model,
              transforms,
              palettes,
              matOverride,
              colors,
              instanceCount,
              boneCount,
              ValueSome transparentDraws
            )
          elif
            Opacity.animatedModelAnyTransparentPart(model, matOverride)
            || Opacity.anyTransparentInstanceColor colors
          then
            transparentDraws.Add(
              TransparentEntry.SkinnedInstancedCommand {
                Model = model
                Transforms = transforms
                Palettes = palettes
                MatOverride = matOverride
                Colors = colors
                InstanceCount = count
                BoneCount = boneCount
                DistanceSq =
                  Opacity.instanceCentroidDistanceSq(
                    state.CurrentCamera.Position,
                    transforms,
                    count
                  )
              }
            )
          else
            PbrShading.drawAnimatedModelInstanced(
              gd,
              &state,
              &frame,
              pbrRes,
              SkinnedInstancedTarget.PbrTarget,
              model,
              transforms,
              palettes,
              matOverride,
              colors,
              instanceCount,
              boneCount,
              ValueNone
            )
      | _ -> ()
    | ValueSome userEffect ->
      // Per-group scope: shade with the user effect via name-resolved SceneUpload. The effect
      // inherits scene data (camera/lights/material/bones), NOT the PBR shader itself.
      // SceneUpload binds PointClamp on sampler slot 5 for user shaders (they do their own
      // PCF); save/restore so the scope can't clobber the backend-specific shadow sampler
      // the built-in PBR path depends on for the rest of the frame.
      let savedSampler = gd.SamplerStates[5]

      try
        PbrShading.shadeWithEffect(gd, &state, &frame, pbrRes, userEffect, draw)
      finally
        gd.SamplerStates[5] <- savedSampler


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
  // rotationDeg spins the quad around the view axis (degrees, CCW in quad space);
  // 0 keeps the exact unrotated math (no trig on the common path).
  static member private EmitQuad
    (
      staging: VertexPositionColorTexture[],
      offset: int,
      world: Matrix,
      size: Vector2,
      color: Color,
      texWidth: float32,
      texHeight: float32,
      texRect: Rectangle,
      rotationDeg: float32
    ) =
    let halfW = size.X * 0.5f
    let halfH = size.Y * 0.5f
    // Unit quad corners (centered on origin, +Y up, +X right), transformed by the billboard matrix.
    let c0, c1, c2, c3 =
      if rotationDeg = 0.0f then
        Vector3.Transform(Vector3(-halfW, -halfH, 0.0f), world),
        Vector3.Transform(Vector3(halfW, -halfH, 0.0f), world),
        Vector3.Transform(Vector3(halfW, halfH, 0.0f), world),
        Vector3.Transform(Vector3(-halfW, halfH, 0.0f), world)
      else
        // Rotate the 2D corner offsets around the view axis before the billboard transform.
        let rad = rotationDeg * (MathF.PI / 180.0f)
        let cos = MathF.Cos rad
        let sin = MathF.Sin rad
        // (x, y) -> (x*cos - y*sin, x*sin + y*cos)
        let x0 = -halfW * cos - (-halfH) * sin
        let y0 = -halfW * sin + (-halfH) * cos
        let x1 = halfW * cos - (-halfH) * sin
        let y1 = halfW * sin + (-halfH) * cos
        let x2 = halfW * cos - halfH * sin
        let y2 = halfW * sin + halfH * cos
        let x3 = -halfW * cos - halfH * sin
        let y3 = -halfW * sin + halfH * cos

        Vector3.Transform(Vector3(x0, y0, 0.0f), world),
        Vector3.Transform(Vector3(x1, y1, 0.0f), world),
        Vector3.Transform(Vector3(x2, y2, 0.0f), world),
        Vector3.Transform(Vector3(x3, y3, 0.0f), world)

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
    (gd: GraphicsDevice, state: byref<ForwardState>, billboard: Billboard3D)
    =
    let texture = billboard.Texture
    let cam = state.CurrentCamera
    let camFwd = cam.Target - cam.Position

    let world =
      Matrix.CreateBillboard(billboard.Position, cam.Position, cam.Up, camFwd)

    if billboardStaging.Length < 4 then
      billboardStaging <- Array.zeroCreate<VertexPositionColorTexture> 4

    // All-zero/empty source rect = full texture.
    let texRect =
      if
        billboard.SourceRect.Width <= 0 || billboard.SourceRect.Height <= 0
      then
        Rectangle(0, 0, texture.Width, texture.Height)
      else
        billboard.SourceRect

    ForwardPipelineBase.EmitQuad(
      billboardStaging,
      0,
      world,
      billboard.Size,
      billboard.Color,
      float32 texture.Width,
      float32 texture.Height,
      texRect,
      billboard.Rotation
    )

    let effect = this.ensureBillboardEffect gd
    effect.Texture <- texture
    effect.World <- Matrix.Identity
    effect.View <- state.View
    effect.Projection <- state.Projection
    effect.Alpha <- 1.0f

    gd.BlendState <- toBlendState billboard.Blend
    gd.DepthStencilState <- toBillboardDepthState billboard.Blend

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
    (gd: GraphicsDevice, state: byref<ForwardState>, batch: BillboardBatch3D)
    =
    let count = batch.Count

    if count <= 0 then
      ()
    else
      // NOTE: This batch path uses only textures[0] — a true multi-texture batch would need
      // a texture atlas or texture array. Splitting by texture (one draw call per distinct
      // texture) is the standard SpriteBatch approach; the sample's particles all share one
      // texture, so the common case is one draw call. Group by texture when that's not true.
      let cam = state.CurrentCamera
      let camFwd = cam.Target - cam.Position
      let texture = batch.Textures[0]
      let texW = float32 texture.Width
      let texH = float32 texture.Height
      let fullRect = Rectangle(0, 0, texture.Width, texture.Height)
      // Null/short arrays = all defaults; indexed defensively per item.
      let rotations = batch.Rotations
      let sourceRects = batch.SourceRects

      let vertCount = count * 4
      let idxCount = count * 6

      if billboardStaging.Length < vertCount then
        billboardStaging <-
          Array.zeroCreate<VertexPositionColorTexture> vertCount

      if billboardIndices.Length < idxCount then
        billboardIndices <- Array.zeroCreate<int> idxCount

      for i = 0 to count - 1 do
        let world =
          Matrix.CreateBillboard(
            batch.Positions[i],
            cam.Position,
            cam.Up,
            camFwd
          )

        let rotation =
          if isNull rotations || i >= rotations.Length then
            0.0f
          else
            rotations[i]

        let texRect =
          if
            isNull sourceRects
            || i >= sourceRects.Length
            || sourceRects[i].Width <= 0
            || sourceRects[i].Height <= 0
          then
            fullRect
          else
            sourceRects[i]

        ForwardPipelineBase.EmitQuad(
          billboardStaging,
          i * 4,
          world,
          batch.Sizes[i],
          batch.Colors[i],
          texW,
          texH,
          texRect,
          rotation
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

      gd.BlendState <- toBlendState batch.Blend
      gd.DepthStencilState <- toBillboardDepthState batch.Blend

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

    // BasicEffect outputs straight color — straight-alpha blend (same
    // convention as the translucent mesh pass; premultiplied AlphaBlend
    // would add the tint at full strength).
    gd.BlendState <- BlendState.NonPremultiplied

    if PlatformInfo.GraphicsBackend = GraphicsBackend.DirectX12 then
      // Line topologies render as triangles on DX12 — emit a camera-facing quad.
      let dir = finish - start
      let len = dir.Length()

      if len > 0.0f then
        let dirN = dir / len
        let mid = (start + finish) * 0.5f

        let mutable side =
          Vector3.Cross(dirN, mid - state.CurrentCamera.Position)

        if side.LengthSquared() < 0.000001f then
          // line points at the camera — any perpendicular basis works
          side <- Vector3.Cross(dirN, Vector3.Up)

        if side.LengthSquared() < 0.000001f then
          side <- Vector3.Cross(dirN, Vector3.UnitX)

        side <- Vector3.Normalize(side) * line3DQuadHalfWidth

        lineStaging[0] <-
          VertexPositionColorTexture(start + side, color, Vector2.Zero)

        lineStaging[1] <-
          VertexPositionColorTexture(finish + side, color, Vector2.Zero)

        lineStaging[2] <-
          VertexPositionColorTexture(finish - side, color, Vector2.Zero)

        lineStaging[3] <-
          VertexPositionColorTexture(start + side, color, Vector2.Zero)

        lineStaging[4] <-
          VertexPositionColorTexture(finish - side, color, Vector2.Zero)

        lineStaging[5] <-
          VertexPositionColorTexture(start - side, color, Vector2.Zero)

        let prevRaster = gd.RasterizerState
        gd.RasterizerState <- RasterizerState.CullNone

        for p in effect.CurrentTechnique.Passes do
          p.Apply()

          gd.DrawUserPrimitives(PrimitiveType.TriangleList, lineStaging, 0, 2)
          |> ignore

        gd.RasterizerState <- prevRaster
    else
      for p in effect.CurrentTechnique.Passes do
        p.Apply()

        gd.DrawUserPrimitives(PrimitiveType.LineList, lineStaging, 0, 1)
        |> ignore

    gd.BlendState <- BlendState.Opaque

  // ----------------------------------------------------------------
  // Shadow pass — delegates to ShadowPass.run (see ShadowPass.fs)
  // ----------------------------------------------------------------

  /// <summary>
  /// Runs one shadow pass over a buffer slice: collects casters from <c>[startIdx, endIdx)</c>,
  /// renders depth to the atlas, then uploads shadow uniforms to the PBR effect. The body lives
  /// in <c>ShadowPass.run</c>; this member just forwards the pipeline's resources + config.
  /// Ensures the PBR effect is loaded first (shadow uniforms upload to it).
  /// </summary>
  member private this.runShadowPass
    (gd: GraphicsDevice)
    (args: ShadowPass.ShadowPassArgs)
    =
    // Ensure the PBR effect is loaded BEFORE the pass uploads shadow uniforms to it.
    PbrShading.ensureEffect(gd, pbrRes) |> ignore

    // PbrParams is injected here — callers leave it ValueNone.
    ShadowPass.run gd atlasCfg biasCfg shadowRes {
      args with
          PbrParams = pbrRes.Params
    }
    |> fun r -> shadowRes.ShadowResult <- r // stash for the forward pass (Shade / user-effect scopes)

  /// <summary>
  /// Multi-camera-block block start: resets the live light buffers when the block carries its
  /// own light commands (a block without any inherits them untouched), then renders this
  /// block's shadow map — from the block's final light set, shadow origin, and buffer slice —
  /// before any of its draws, and reseats the scene bundle's shadow state from the pass.
  /// </summary>
  member private this.beginShadowedBlock
    (
      gd: GraphicsDevice,
      buffer: RenderBuffer3D,
      plan: BlockPlan,
      blockIndex: byref<int>,
      camera: Camera3D,
      scene: byref<ForwardFrame>
    ) =
    if LightScoping.resetForBlock plan defaultLights lights &blockIndex then
      pbrRes.LightsDirty <- true

    let block = plan.Blocks[blockIndex]
    LightScoping.loadSet block.Lights blockLights
    shadowRes.Origin <- block.ShadowOrigin

    this.runShadowPass gd {
      Lights = blockLights
      PbrParams = ValueNone
      Buffer = buffer
      StartIndex = block.StartIndex
      EndIndex = block.EndIndex
      InitialCastEnabled = block.InitialCastEnabled
      Camera = camera
      NeedsDepth = false
      Precollected = false
    }

    scene.PointShadowSlots <- shadowRes.PointShadowSlots
    scene.SpotShadowSlots <- shadowRes.SpotShadowSlots
    scene.Shadows <- shadowRes.ShadowResult

    // The block refreshed both the light set and the shadow state — the DX12
    // grouped effect must re-upload its frame constants before its next draw.
    pbrRes.GroupedUniformsDirty <- true

  /// <summary>
  /// Draws a solid-color NDC fullscreen triangle, which covers exactly the active viewport.
  /// Used to clear a camera block's viewport region: <c>gd.Clear</c> ignores the viewport on
  /// D3D-style backends (<c>ClearRenderTargetView</c> semantics), so an unclipped block clear
  /// would wipe previously rendered camera blocks (split-screen). Color-only — the caller
  /// saves/restores device state and leaves depth untouched.
  /// </summary>
  member private _.clearViewport (gd: GraphicsDevice) (c: Color) =
    if obj.ReferenceEquals(clearEffect, null) then
      clearEffect <-
        new BasicEffect(
          gd,
          VertexColorEnabled = true,
          LightingEnabled = false,
          TextureEnabled = false
        )

    clearVerts[0] <- VertexPositionColor(Vector3(-1.f, -1.f, 0.f), c)
    clearVerts[1] <- VertexPositionColor(Vector3(3.f, -1.f, 0.f), c)
    clearVerts[2] <- VertexPositionColor(Vector3(-1.f, 3.f, 0.f), c)

    clearEffect.World <- Matrix.Identity
    clearEffect.View <- Matrix.Identity
    clearEffect.Projection <- Matrix.Identity

    gd.DepthStencilState <- DepthStencilState.None
    gd.BlendState <- BlendState.Opaque
    gd.RasterizerState <- RasterizerState.CullNone

    for pass in clearEffect.CurrentTechnique.Passes do
      pass.Apply()

      gd.DrawUserPrimitives<VertexPositionColor>(
        PrimitiveType.TriangleList,
        clearVerts,
        0,
        1
      )

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
        pbrRes.HasLastGroupedMaterial <- false
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

      match pbrRes.InstanceColorVertexBuffer with
      | ValueSome vb ->
        vb.Dispose()
        pbrRes.InstanceColorVertexBuffer <- ValueNone
      | ValueNone -> ()

      match pbrRes.InstancePaletteVertexBuffer with
      | ValueSome vb ->
        vb.Dispose()
        pbrRes.InstancePaletteVertexBuffer <- ValueNone
      | ValueNone -> ()

      match pbrRes.InstancePaletteColorVertexBuffer with
      | ValueSome vb ->
        vb.Dispose()
        pbrRes.InstancePaletteColorVertexBuffer <- ValueNone
      | ValueNone -> ()

      (paletteChunks :> IDisposable).Dispose()

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

      match shadowRes.InstanceVertexBuffer with
      | ValueSome vb ->
        vb.Dispose()
        shadowRes.InstanceVertexBuffer <- ValueNone
      | ValueNone -> ()

      match shadowRes.SkinnedInstancedVertexBuffer with
      | ValueSome vb ->
        vb.Dispose()
        shadowRes.SkinnedInstancedVertexBuffer <- ValueNone
      | ValueNone -> ()

      match fullScreenQuad with
      | ValueSome q ->
        (q :> IDisposable).Dispose()
        fullScreenQuad <- ValueNone
      | ValueNone -> ()

      match sceneDepthRT with
      | ValueSome rt ->
        rt.Dispose()
        sceneDepthRT <- ValueNone
      | ValueNone -> ()

    member this.Execute(gameCtx, gameTime, buffer, rtPool) =
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
      // s5 (shadow atlas) is bound per-shadow-pass to PointClamp by ShadowPass.fs
      // (point-sampled depth for the manual 3×3 PCF); set a safe default here.
      gd.SamplerStates[5] <- SamplerState.PointClamp
      // s6 (bone-palette texture for skinned + instanced draws): point-sampled — the
      // palette texels are exact matrix rows, filtering would blend unrelated bones.
      gd.SamplerStates[6] <- SamplerState.PointClamp

      // Return last frame's bone-palette chunk textures to the shared pool before any
      // draw of this frame re-acquires them (per-frame lifetime — see PaletteChunkCache).
      paletteChunks.ReleaseAll()
      instanceWorlds.ReleaseAll()

      // Pre-scan — capture camera, shadow state, and post-process actions in one pass.
      // The block plan walks the buffer once for the per-camera-block light scoping; frames
      // with more than one camera block scope lights per block, single-camera frames gather
      // lights frame-globally below and skip the walk (and its allocations) entirely — the
      // counter is maintained by the buffer on Add.
      Pipelines.LightBuffers.clear lights
      shadowRes.Origin <- ValueNone

      let multiBlock = buffer.CameraBlockCount > 1

      // Scene depth is needed when at least one PostProcessWithDepth action exists. Also
      // feeds the shadow pass's geometry collection gate below.
      let needsDepth = buffer.DepthPostProcessCount > 0

      // Single-camera frames collect shadow/scene-depth geometry inline in the pre-scan
      // walk — one less full buffer walk per frame (was: pre-scan + collect + forward).
      // Gated on the frame possibly needing geometry: a depth-needing post-process, or at
      // least one shadow-casting light in the buffer (ShadowCasterLightCount — zero means
      // provably no caster, so collection would be discarded work). Multi-block frames keep
      // per-block collection in their block shadow passes (slices + per-block initial state).
      let collectInline =
        not multiBlock && (needsDepth || buffer.ShadowCasterLightCount > 0)

      if collectInline then
        ShadowPass.beginCollect shadowRes true

      let plan =
        if multiBlock then
          BlockPlan.build buffer
        else
          BlockPlan.empty

      let mutable state: ForwardState = {
        HasCamera = false
        View = Matrix.Identity
        Projection = Matrix.Identity
        CurrentCamera = Unchecked.defaultof<Camera3D>
        CurrentConfig = ValueNone
        SavedViewport = gd.Viewport
      }

      // Post-process actions collected during the pre-scan and drained after the forward pass
      // renders the scene to an offscreen target. Allocated only when the view emits at least one
      // (buffer.PostProcessCount), so frames with no post-processing skip both the allocation and
      // the per-command scan.
      let ppActions: ResizeArray<PostProcessContext3D -> unit> voption =
        if buffer.PostProcessCount > 0 then
          ValueSome(ResizeArray(buffer.PostProcessCount))
        else
          ValueNone

      // Pre-scan: camera and shadow commands (shadow origin / toggle) need to be known before
      // the shadow pass runs. Single-camera frames also gather lights frame-globally here —
      // multi-block frames scope lights per camera block in the forward pass instead.
      // Draw commands are handled in the forward pass; single-camera frames ALSO collect
      // shadow/scene-depth geometry inline here (collectInline).
      for i = 0 to buffer.Count - 1 do
        let cmd = buffer[i]

        if collectInline then
          ShadowPass.collectCommand gd shadowRes cmd

        match cmd with
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

        | Command3D.SetAmbientLight _
        | Command3D.AddDirectionalLight _
        | Command3D.AddPointLight _
        | Command3D.AddSpotLight _ as cmd ->
          if not multiBlock then
            LightScoping.apply lights cmd
        | Command3D.SetShadowOrigin origin ->
          shadowRes.Origin <- ValueSome origin
        | Command3D.PostProcess action
        | Command3D.PostProcessWithDepth action ->
          match ppActions with
          | ValueSome list -> list.Add action
          | ValueNone -> ()
        | _ -> ()

      // Multi-camera-block frames: start the persistent defaults empty — the forward pass
      // builds them in-order (between-block commands accumulate; each block resets to the
      // defaults-so-far or inherits the running set at its BeginCamera), so live shading
      // matches the block plan by construction.
      if multiBlock then
        Pipelines.LightBuffers.clear defaultLights
        // Per-block shadow passes reseat ShadowResult at each block start; don't leak last
        // frame's result into a DrawImmediate before the first block.
        shadowRes.ShadowResult <- ValueNone

      // Shadow pass: single-camera frames run one pass up front; geometry was already
      // collected inline in the pre-scan when this frame can need it (collectInline).
      // Multi-block frames run one pass per camera block at its BeginCamera/BeginCameraConfig
      // in the forward loop instead.
      if state.HasCamera && not multiBlock then
        this.runShadowPass gd {
          Lights = lights
          PbrParams = ValueNone
          Buffer = buffer
          StartIndex = 0
          EndIndex = buffer.Count
          InitialCastEnabled = true
          Camera = state.CurrentCamera
          NeedsDepth = needsDepth
          Precollected = collectInline
        }

      // Forward pass
      // Lights are seeded (frame-global for single-camera frames, the frame defaults for
      // multi-block frames) and shadow state is gathered; the camera is re-established per
      // block below. activeEffect tracks the per-group shading scope (beginEffect/endEffect):
      // ValueNone → default PBR path; ValueSome e → shade with the user effect. Scopes do NOT
      // persist across cameras — a new camera block (BeginCamera/BeginCameraConfig) and EndCamera
      // both reset it, so a forgotten endEffect can't leak a user effect into the next view.
      pbrRes.LightsDirty <- true
      pbrRes.GroupedUniformsDirty <- true
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

      // Transparent flush: draws the deferred transparents (sorted far-to-near) with alpha
      // blending + depth-read, then clears the list. The PBR fragment outputs STRAIGHT
      // color (ForwardPbr.fx), so the blend state is the straight-alpha NonPremultiplied —
      // premultiplied AlphaBlend would add the tint at full strength regardless of opacity.
      // Opaque geometry already wrote depth and
      // the far-to-near sort guarantees each successive transparent is nearer, so it passes
      // the depth test against everything already drawn. Called at camera boundaries, before
      // DrawImmediate, and at the end of the frame — must run while the deferring camera's
      // matrices/viewport are still current. No-op when nothing was deferred. Inline so the
      // body can take the address of the enclosing mutable state/scene at each call site
      // (a closure cannot capture byrefs).
      let inline flushTransparents() =
        if transparentDraws.Count > 0 then
          let prevBlend = gd.BlendState
          let prevDepth = gd.DepthStencilState

          gd.BlendState <- BlendState.NonPremultiplied
          gd.DepthStencilState <- DepthStencilState.DepthRead
          transparentDraws.Sort(transparentComparer)

          for i = 0 to transparentDraws.Count - 1 do
            // Each case re-enters its own path under the flush's blend state: single
            // draws through drawTransparent, plain batches through drawInstanced, and a
            // deferred skinned-instanced command through drawAnimatedModelInstanced
            // (deferList ValueNone — no re-defer).
            match transparentDraws[i] with
            | TransparentEntry.SingleDraw d ->
              PbrShading.drawTransparent(gd, &state, &scene, pbrRes, d)
            | TransparentEntry.SkinnedInstanceDraw d ->
              // One instance's part from the GL per-instance fallback: its palette is
              // read as a slice of the command's flat array (see the capture site).
              PbrShading.drawTransparentSkinnedInstance(
                gd,
                &state,
                &scene,
                pbrRes,
                d
              )
            | TransparentEntry.InstancedDraw d ->
              PbrShading.drawInstanced(
                gd,
                &state,
                &scene,
                pbrRes,
                d.Mesh,
                d.Transforms,
                d.Colors,
                d.Material,
                d.InstanceCount,
                d.VertexOffset,
                d.StartIndex
              )
            | TransparentEntry.SkinnedInstancedCommand d ->
              PbrShading.drawAnimatedModelInstanced(
                gd,
                &state,
                &scene,
                pbrRes,
                SkinnedInstancedTarget.PbrTarget,
                d.Model,
                d.Transforms,
                d.Palettes,
                d.MatOverride,
                d.Colors,
                d.InstanceCount,
                d.BoneCount,
                ValueNone
              )

          gd.BlendState <- prevBlend
          gd.DepthStencilState <- prevDepth
          transparentDraws.Clear()

      // Running camera-block index into the block plan; advanced at each
      // BeginCamera/BeginCameraConfig below (multi-block frames only).
      let mutable blockIndex = -1

      // When post-process commands are present, render the forward pass to an offscreen target
      // so each action can sample the scene texture. Otherwise render direct to the back-buffer.
      let usePostProcess = buffer.PostProcessCount > 0

      let sceneRT: RenderTarget2D voption =
        if usePostProcess then
          let target = rtPool.Acquire(gameCtx.WindowWidth, gameCtx.WindowHeight)
          gd.SetRenderTarget(target)
          gd.Clear(Microsoft.Xna.Framework.Color.Black)
          ValueSome target
        else
          ValueNone

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        // ── Camera ──
        | Command3D.BeginCamera cam ->
          // Transparents sort by the camera that deferred them — flush before switching to
          // the new camera's matrices (state still holds the previous camera's view).
          flushTransparents()

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

          // New camera block: scopes don't persist across cameras.
          activeEffect <- ValueNone

          // Multi-block frames: reset-or-inherit the lights, then render this block's
          // shadow map before any of its draws.
          if multiBlock then
            this.beginShadowedBlock(
              gd,
              buffer,
              plan,
              &blockIndex,
              state.CurrentCamera,
              &scene
            )

        | Command3D.BeginCameraConfig cfg ->
          // Transparents sort by the camera that deferred them — flush before applying the
          // new block's viewport/matrices (the previous camera's viewport is still active).
          flushTransparents()

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
          | ValueSome c ->
            match cfg.Viewport with
            | ValueSome _ ->
              // gd.Clear ignores the viewport, so the block clear is drawn: an NDC
              // fullscreen triangle covers exactly the active viewport. Color-only —
              // depth is untouched, matching the fullscreen gd.Clear path below.
              let prevDepth = gd.DepthStencilState
              let prevBlend = gd.BlendState
              let prevRaster = gd.RasterizerState

              this.clearViewport gd c

              gd.DepthStencilState <- prevDepth
              gd.BlendState <- prevBlend
              gd.RasterizerState <- prevRaster
            | ValueNone -> gd.Clear(ClearOptions.Target, c.ToVector4(), 1.0f, 0)
          | ValueNone -> ()

          // New camera block: scopes don't persist across cameras.
          activeEffect <- ValueNone

          // Multi-block frames: reset-or-inherit the lights, then render this block's
          // shadow map before any of its draws.
          if multiBlock then
            this.beginShadowedBlock(
              gd,
              buffer,
              plan,
              &blockIndex,
              state.CurrentCamera,
              &scene
            )

        | Command3D.EndCamera ->
          if state.HasCamera then
            // Transparents sort by the camera that deferred them — flush before the
            // viewport restore (the deferring camera's viewport is still active).
            flushTransparents()

            // Restore fullscreen viewport + mark camera inactive so subsequent draws are skipped
            // until the next BeginCamera (matches the B5-B9 single-pass semantics; without this,
            // draws after EndCamera would dispatch with stale matrices).
            gd.Viewport <- state.SavedViewport
            state.HasCamera <- false

          // EndCamera closes any open effect scope.
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
        | Command3D.DrawModelWith _
        | Command3D.DrawAnimatedModel _
        | Command3D.DrawAnimatedModelWith _
        | Command3D.DrawAnimatedModelInstanced _
        | Command3D.DrawPrimitive _
        | Command3D.DrawInstanced _ ->
          if state.HasCamera then
            this.Shade(gd, &state, &scene, activeEffect, buffer[i])

        | Command3D.DrawMeshEffect(part, transform, effect) ->
          if state.HasCamera then
            this.handleDrawMeshEffect(gd, &state, part, transform, effect)

        // ── Billboards / lines (B8) ──
        | Command3D.DrawBillboard billboard ->
          if state.HasCamera then
            this.handleDrawBillboard(gd, &state, billboard)

        | Command3D.DrawBillboardBatch batch ->
          if state.HasCamera then
            this.handleDrawBillboardBatch(gd, &state, batch)

        | Command3D.DrawLine3D(s, f, color) ->
          if state.HasCamera then
            this.handleDrawLine3D(gd, &state, s, f, color)

        // ── Lighting ──
        // Multi-block frames apply light commands in-order (a mid-block command affects only
        // subsequent draws; between-block commands also update the frame defaults). Single-camera
        // frames gathered lights frame-globally in the pre-scan — no-op here.
        | Command3D.SetAmbientLight _
        | Command3D.AddDirectionalLight _
        | Command3D.AddPointLight _
        | Command3D.AddSpotLight _ as cmd ->
          if multiBlock then
            LightScoping.applyInOrder lights defaultLights state.HasCamera cmd
            pbrRes.LightsDirty <- true
            pbrRes.GroupedUniformsDirty <- true

        // ── Shadow state (consumed in the shadow pass; no-op here) ──
        | Command3D.SetShadowOrigin _
        | Command3D.EnableShadows
        | Command3D.DisableShadows -> ()

        // Post-process actions were collected in the pre-scan and run after the scene
        // renders to an offscreen target; nothing to do during the forward pass.
        | Command3D.PostProcess _
        | Command3D.PostProcessWithDepth _ -> ()

        // ── Escape hatch: full device control + the gathered scene data ──
        | Command3D.DrawImmediate action ->
          // Transparents emitted before the immediate block draw before it (they were
          // deferred earlier in the buffer).
          flushTransparents()

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

      // ── Transparent flush (end of frame) — state still holds the last camera's
      // matrices/viewport, which the scene-depth pre-pass below also reads. ──
      flushTransparents()

      // ── Scene depth pre-pass (camera-POV, reusing collected geometry) ──
      // Only runs when PostProcessWithDepth actions exist. Single-camera frames reuse the
      // geometry the up-front shadow pass collected; multi-block frames re-collect the full
      // buffer below. renderSceneDepth re-renders it from the camera VP into an R32F target.
      let sceneDepth: RenderTarget2D voption =
        if needsDepth && usePostProcess then
          // Ensure the depth target matches the back-buffer size.
          let w = gameCtx.WindowWidth
          let h = gameCtx.WindowHeight

          match sceneDepthRT with
          | ValueSome rt when rt.Width = w && rt.Height = h -> ()
          | _ ->
            (match sceneDepthRT with
             | ValueSome rt -> rt.Dispose()
             | ValueNone -> ())

            sceneDepthRT <-
              ValueSome(
                new RenderTarget2D(
                  gd,
                  w,
                  h,
                  false,
                  SurfaceFormat.Single,
                  DepthFormat.Depth24,
                  0,
                  RenderTargetUsage.DiscardContents
                )
              )

          // Multi-block frames collected geometry per block (each block's shadow pass saw only
          // its own slice); re-collect the full buffer once so scene depth keeps the
          // union-of-all-geometry behavior. Safe to overwrite the pooled arrays here — every
          // per-block shadow pass has already rendered. The depth effect may never have loaded
          // if no block had casters.
          if multiBlock then
            ShadowPass.ensureDepthEffect gd shadowRes
            ShadowPass.collectGeometry gd buffer 0 buffer.Count true shadowRes

          // Reuse the camera VP the forward pass computed (correct viewport aspect). The forward
          // pass captured it in state.View * state.Projection during BeginCamera — in multi-block
          // frames that is the LAST block's camera.
          match struct (shadowRes.Effect, shadowRes.Params, sceneDepthRT) with
          | ValueSome eff, ValueSome prms, ValueSome rt ->
            ShadowPass.renderSceneDepth
              gd
              shadowRes
              eff
              prms
              (state.View * state.Projection)
              rt

            ValueSome rt
          | _ -> ValueNone
        else
          ValueNone

      // ── Post-process: ping-pong the scene through each action ──
      match struct (sceneRT, ppActions) with
      | ValueNone, _ -> ()
      | ValueSome _, ValueNone -> ()
      | ValueSome sceneTarget, ValueSome actions ->
        // Return to the back-buffer before draining (the forward pass drew into sceneTarget).
        gd.SetRenderTarget(null)

        match fullScreenQuad with
        | ValueNone -> fullScreenQuad <- ValueSome(new FullScreenQuad(gd))
        | ValueSome _ -> ()

        let mutable src = sceneTarget

        let quad =
          fullScreenQuad |> ValueOption.defaultValue Unchecked.defaultof<_>

        for i = 0 to actions.Count - 1 do
          let isLast = i = actions.Count - 1

          // Last action draws to the back-buffer (null); earlier actions ping-pong through
          // pooled targets. The destination is set (and, for intermediate targets, cleared)
          // before the action runs — the action samples `src`, not the destination.
          let dst =
            if isLast then
              null
            else
              rtPool.Acquire(src.Width, src.Height)

          gd.SetRenderTarget(dst)

          if not isLast then
            gd.Clear(
              ClearOptions.Target,
              Microsoft.Xna.Framework.Color.Black,
              0.0f,
              0
            )

          let ppCtx: PostProcessContext3D = {
            Source = src
            Depth = sceneDepth
            Width = src.Width
            Height = src.Height
            Time = frameTime
            Device = gd
            Quad = quad
            Context = gameCtx
          }

          actions[i]ppCtx

          if not isLast then
            src <- dst

// ------------------------------------------------------------------
// ForwardPipeline — the default PBR subclass
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
  (?shadowAtlas: ShadowAtlasConfig, ?shadowBias: ShadowBiasConfig) =
  inherit
    ForwardPipelineBase(?shadowAtlas = shadowAtlas, ?shadowBias = shadowBias)
