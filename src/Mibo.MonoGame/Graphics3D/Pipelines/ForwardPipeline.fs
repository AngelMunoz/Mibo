namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
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
type ForwardPipeline([<Struct>] ?postProcess: PostProcessConfig3D) =

  let ppConfig = ValueOption.defaultValue PostProcessConfig3D.none postProcess

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

  // Lazily-created BasicEffect for the DrawMeshPBR fallback path (until B9 lands the custom
  // PBR shader). Created on first DrawMeshPBR against the actual GraphicsDevice passed to
  // Execute — BasicEffect's ctor requires a real device (it tracks the resource), so we can't
  // build it in Initialize() (no device is passed there) or capture one in a `lazy`.
  let mutable pbrFallbackEffect: BasicEffect voption = ValueNone

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
  /// Handles <c>DrawMeshPBR</c>: draws an effectless <see cref="T:Mibo.Elmish.Graphics3D.PrimitiveMesh"/>
  /// with a <c>Material3D</c>. Per §4.1, this is the only place <c>Material3D</c> is consumed.
  /// </summary>
  /// <remarks>
  /// <b>B5/B6 fallback:</b> until B9 lands the custom PBR HLSL, the material's PBR maps
  /// (albedo/normal/metallic/roughness/emission) are ignored and a lazily-created
  /// <c>BasicEffect</c> renders the albedo color with the pipeline's accumulated lighting.
  /// This keeps the PBR command path usable today (smoke-testable) and gives B9 a concrete
  /// dispatch site to replace with the PBR <c>Effect</c>.
  /// </remarks>
  member private _.handleDrawMeshPBR
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      mesh: PrimitiveMesh,
      transform: Matrix,
      material: Material3D
    ) =
    // Create the fallback BasicEffect on first use against the real device. Pattern-match
    // rather than .Value — AGENTS.md imperative #4 bans unchecked voption unwraps.
    let effect =
      match pbrFallbackEffect with
      | ValueSome e -> e
      | ValueNone ->
        let e = new BasicEffect(gd)
        pbrFallbackEffect <- ValueSome e
        e

    // Map the Material3D's albedo color → BasicEffect.DiffuseColor. Normalized to 0–1.
    // PBR maps, roughness/metallic, emission, opacity are B9's job; ignored here.
    let c = material.AlbedoColor

    effect.DiffuseColor <-
      Vector3(float32 c.R / 255.0f, float32 c.G / 255.0f, float32 c.B / 255.0f)

    effect.Alpha <- material.Opacity
    effect.Texture <- null
    effect.TextureEnabled <- false
    effect.VertexColorEnabled <- false
    effect.World <- transform
    effect.View <- state.View
    effect.Projection <- state.Projection
    applyLighting(effect, lights)
    mesh.Draw(gd, effect)

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
    /// Called once at disposal. Releases the lazily-created PBR fallback effect (if any).
    /// Reserved for B9 (PBR shader unload).
    /// </summary>
    member _.Shutdown() =
      match pbrFallbackEffect with
      | ValueSome e ->
        e.Dispose()
        pbrFallbackEffect <- ValueNone
      | ValueNone -> ()

    member this.Execute(gameCtx, buffer, _rtPool) =
      let gd = MonoGameGameContext.getGraphicsDevice gameCtx

      // ── Device defaults for opaque 3D rendering ──
      gd.DepthStencilState <- DepthStencilState.Default
      gd.RasterizerState <- RasterizerState.CullCounterClockwise
      gd.BlendState <- BlendState.Opaque
      gd.SamplerStates[0] <- SamplerState.LinearWrap

      // ── Step 1: Pre-scan — capture camera + lights (shadow pass is B10) ──
      clearLights lights

      let mutable state: ForwardState = {
        HasCamera = false
        View = Matrix.Identity
        Projection = Matrix.Identity
        CurrentCamera = Unchecked.defaultof<Camera3D>
        CurrentConfig = ValueNone
        SavedViewport = gd.Viewport
      }

      // Single-pass dispatch: camera/light/draw commands all handled inline in buffer order,
      // mirroring the canonical raylib dispatch loop (lights are applied lazily at each draw
      // from the accumulated buffers, so producer command ordering defines the lighting state).
      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        // ── Camera ──
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

          // Apply viewport + clear color.
          match cfg.Viewport with
          | ValueSome rect -> gd.Viewport <- Viewport(rect)
          | ValueNone -> ()

          match cfg.ClearColor with
          | ValueSome c -> gd.Clear(ClearOptions.Target, c.ToVector4(), 1.0f, 0)
          | ValueNone -> ()

        | Command3D.EndCamera ->
          if state.HasCamera then
            // Restore fullscreen viewport.
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

        // DrawMeshInstanced: stubbed — native hardware instancing is B7 (requires an instance
        // vertex stream + a custom HLSL vertex declaration; BasicEffect has no instance
        // semantics). The case is present in the DU so B7 wires dispatch without a breaking
        // signature change. Until B7 this is a no-op.
        | Command3D.DrawMeshInstanced _ -> ()

        // ── Billboards / lines — stubbed (full impl in B8) ──
        | Command3D.DrawBillboard _
        | Command3D.DrawBillboardBatch _
        | Command3D.DrawLine3D _ ->
          // No-op until B8. Pipeline stays usable; these primitives just don't render yet.
          ()

        // ── Lighting ──
        | Command3D.SetAmbientLight a -> lights.Ambient <- ValueSome a

        | Command3D.AddDirectionalLight d -> lights.DirLights.Add d

        | Command3D.AddPointLight p -> lights.PointLights.Add p

        | Command3D.AddSpotLight s -> lights.SpotLights.Add s

        // ── Shadow state — accepted no-ops (shadow pass is B10) ──
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
