namespace Mibo.Elmish.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting

/// <summary>Configuration for the <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/></summary>
[<Struct>]
type Renderer2DConfig = {

  /// <summary>
  /// Background clear color applied before rendering commands.
  /// <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueNone"/> skips clearing entirely,
  /// which is useful when composing multiple renderers (e.g., 2D overlay on 3D scene).
  /// <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueSome"/> clears with the specified color.
  /// </summary>
  ClearColor: Color voption
}

/// <summary>Convenience values and functions for <see cref="T:Mibo.Elmish.Graphics2D.Renderer2DConfig"/></summary>
module Renderer2DConfig =

  /// <summary>
  /// Default configuration: black clear color. Post-processing is driven by
  /// <c>Command2D.PostProcess</c> emitted from the view, not configured here.
  /// </summary>
  let defaults: Renderer2DConfig = { ClearColor = ValueSome Color.Black }

  /// <summary>
  /// Configuration that skips clearing the background.
  /// Use when this renderer composites on top of another renderer's output.
  /// </summary>
  let noClear: Renderer2DConfig = { ClearColor = ValueNone }

// ═══════════════════════════════════════════════════════════════════
// Post-process drain — ping-pongs the scene through each emitted action
// ═══════════════════════════════════════════════════════════════════

module private PostProcessDrain =

  /// <summary>
  /// Runs each post-process action in order, ping-ponging the scene texture through
  /// pooled render targets. Each action receives the current source as a
  /// <see cref="T:Mibo.Elmish.Graphics2D.PostProcessContext2D"/> and owns its effect +
  /// fullscreen-quad draw. The last action draws to the back-buffer.
  /// </summary>
  let apply
    (ctx: GameContext)
    (gd: GraphicsDevice)
    (sceneTarget: RenderTarget2D)
    (lights: Lighting.LightContext2D voption)
    (camera: Camera2D voption)
    (rtPool: IRenderTargetPool)
    (quad: Mibo.Elmish.Graphics3D.FullScreenQuad)
    (actions: ResizeArray<PostProcessContext2D -> unit>)
    (frameTime: float32)
    =
    let mutable src: RenderTarget2D = sceneTarget
    let w = ctx.WindowWidth
    let h = ctx.WindowHeight

    for i = 0 to actions.Count - 1 do
      let isLast = i = actions.Count - 1

      let dst: RenderTarget2D voption =
        if isLast then
          ValueNone
        else
          ValueSome(rtPool.Acquire(w, h))

      match dst with
      | ValueSome target ->
        gd.SetRenderTarget(target)
        gd.Clear(ClearOptions.Target, Color.Black, 0.0f, 0)
      | ValueNone -> gd.SetRenderTarget(null)

      let ppCtx: PostProcessContext2D = {
        Source = src
        Width = w
        Height = h
        Time = frameTime
        Device = gd
        Quad = quad
        Lights = lights
        Camera = camera
        Context = ctx
      }

      actions[i]ppCtx

      match dst with
      | ValueSome target -> src <- target
      | ValueNone -> ()

// ═══════════════════════════════════════════════════════════════════
// Lit-sprite vertex tessellation — pure CPU math, factored out so the
// renderer's batcher and the unit tests share one source of truth.
// Produces a 4-vertex indexed quad (TL, TR, BR, BL) with the same
// world-space positions, UVs, and flip/rotation/origin handling the
// legacy per-sprite DrawUserPrimitives path used.
// ═══════════════════════════════════════════════════════════════════

/// <summary>Indexed quad vertex layout produced by lit-sprite tessellation.</summary>
/// <remarks>
/// Vertices are ordered TL (top-left), TR (top-right), BR (bottom-right), BL (bottom-left).
/// The index pattern <c>[0;1;2; 0;2;3]</c> winds two triangles (TL,TR,BR) and (TL,BR,BL),
/// matching the legacy 6-vertex DrawUserPrimitives layout so visuals are identical.
/// Positions are world-space destination coordinates (the lit shader derives
/// <c>WorldPos = input.Position.xy</c>); the shared <c>MatrixTransform = view * projection</c>
/// is applied in the vertex shader.
/// </remarks>
[<Struct>]
type internal LitQuadVerts = {
  TL: VertexPositionColorTexture
  TR: VertexPositionColorTexture
  BR: VertexPositionColorTexture
  BL: VertexPositionColorTexture
}

/// <summary>Pure tessellation of a lit-sprite <see cref="SpriteState"/> into a 4-vertex quad.</summary>
/// <remarks>
/// Reproduces the corner-transform / UV / flip math from the original per-sprite draw path
/// verbatim. Negative <c>Source.Width</c>/<c>Height</c> signal a flip (same convention the
/// unlit SpriteBatch path turns into FlipHorizontally/FlipVertically); the flip is folded
/// into the UVs by swapping u0/u1 (v0/v1) so UVs stay in [0,1].
/// </remarks>
module internal LitBatchTessellation =

  /// <summary>
  /// Compute the UVs for the sprite's source rect, folding negative source
  /// width/height (flip) into u0/u1 (v0/v1) swaps. Returns (u0, u1, v0, v1)
  /// where (u0,v0) maps to the TL corner and (u1,v1) to the BR corner.
  /// </summary>
  let computeUvs
    (src: Rectangle)
    (texW: float32)
    (texH: float32)
    : struct (float32 * float32 * float32 * float32) =
    let flipX = src.Width < 0
    let flipY = src.Height < 0
    let srcW = if flipX then -src.Width else src.Width
    let srcH = if flipY then -src.Height else src.Height

    let uLeft = float32 src.X / texW
    let uRight = float32(src.X + srcW) / texW
    let vTop = float32 src.Y / texH
    let vBot = float32(src.Y + srcH) / texH

    // u0/v0 map to the TL corner, u1 to the right column, v1 to the bottom row;
    // swapping on the flipped axis mirrors it.
    let u0 = if flipX then uRight else uLeft
    let u1 = if flipX then uLeft else uRight
    let v0 = if flipY then vBot else vTop
    let v1 = if flipY then vTop else vBot
    struct (u0, u1, v0, v1)

  /// <summary>
  /// Compute the four world-space destination corners with origin/rotation applied.
  /// Matches the legacy transformCorner helper exactly.
  /// </summary>
  let computeCorners
    (dest: Rectangle)
    (origin: Vector2)
    (rotation: float32)
    : struct (Vector2 * Vector2 * Vector2 * Vector2) =
    let cosR = cos rotation
    let sinR = sin rotation

    let transformCorner(lx: float32, ly: float32) =
      let tx = lx - origin.X
      let ty = ly - origin.Y
      let rx = tx * cosR - ty * sinR
      let ry = tx * sinR + ty * cosR
      Vector2(float32 dest.X + rx + origin.X, float32 dest.Y + ry + origin.Y)

    let tl = transformCorner(0.0f, 0.0f)
    let tr = transformCorner(float32 dest.Width, 0.0f)
    let bl = transformCorner(0.0f, float32 dest.Height)
    let br = transformCorner(float32 dest.Width, float32 dest.Height)
    struct (tl, tr, bl, br)

  /// <summary>Tessellate a lit sprite into a 4-vertex indexed quad.</summary>
  /// <param name="texW">The albedo texture's pixel width (for UV normalization).</param>
  /// <param name="texH">The albedo texture's pixel height.</param>
  /// <returns>The four quad vertices (TL, TR, BR, BL) in world space.</returns>
  let tessellate
    (sprite: SpriteState)
    (texW: float32)
    (texH: float32)
    : LitQuadVerts =
    let struct (u0, u1, v0, v1) = computeUvs sprite.Source texW texH

    let struct (tl, tr, bl, br) =
      computeCorners sprite.Dest sprite.Origin sprite.Rotation

    let color = sprite.Color

    {
      TL =
        VertexPositionColorTexture(
          Vector3(tl.X, tl.Y, 0.0f),
          color,
          Vector2(u0, v0)
        )
      TR =
        VertexPositionColorTexture(
          Vector3(tr.X, tr.Y, 0.0f),
          color,
          Vector2(u1, v0)
        )
      BR =
        VertexPositionColorTexture(
          Vector3(br.X, br.Y, 0.0f),
          color,
          Vector2(u1, v1)
        )
      BL =
        VertexPositionColorTexture(
          Vector3(bl.X, bl.Y, 0.0f),
          color,
          Vector2(u0, v1)
        )
    }

  /// <summary>
  /// Index pattern for one tessellated quad: two triangles wound TL,TR,BR / TL,BR,BL.
  /// Add <c>baseVertex</c> to each to offset within a larger vertex buffer.
  /// </summary>
  let writeIndices (indices: int[]) (offset: int) (baseVertex: int) =
    indices[offset] <- baseVertex + 0
    indices[offset + 1] <- baseVertex + 1
    indices[offset + 2] <- baseVertex + 2
    indices[offset + 3] <- baseVertex + 0
    indices[offset + 4] <- baseVertex + 2
    indices[offset + 5] <- baseVertex + 3

  /// <summary>
  /// Pure batch-key change predicate. Returns true when the current sub-batch
  /// key (effect, texture, normalMap) differs from the incoming sprite's, so the
  /// accumulator must flush before appending. Exposed for unit testing the
  /// flush-trigger logic without a GraphicsDevice.
  /// </summary>
  /// <param name="hasBatch">Whether the accumulator currently has pending geometry.</param>
  /// <param name="curEffect">The effect reference of the current pending batch.</param>
  /// <param name="curTexture">The albedo texture reference of the current pending batch.</param>
  /// <param name="curNormalMap">The normal-map reference of the current pending batch.</param>
  /// <param name="effect">The incoming sprite's effect reference.</param>
  /// <param name="texture">The incoming sprite's albedo texture reference.</param>
  /// <param name="normalMap">The incoming sprite's normal-map reference.</param>
  let batchKeyChanged
    (hasBatch: bool)
    (curEffect: obj)
    (curTexture: obj)
    (curNormalMap: obj voption)
    (effect: obj)
    (texture: obj)
    (normalMap: obj voption)
    : bool =
    not hasBatch
    || not(obj.ReferenceEquals(curEffect, effect))
    || not(obj.ReferenceEquals(curTexture, texture))
    || (match curNormalMap, normalMap with
        | ValueSome a, ValueSome b -> not(obj.ReferenceEquals(a, b))
        | ValueSome _, ValueNone -> true
        | ValueNone, ValueSome _ -> true
        | ValueNone, ValueNone -> false)

// ═══════════════════════════════════════════════════════════════════
// Private command handlers — extracted from Renderer2D for readability
// ═══════════════════════════════════════════════════════════════════

module private CommandHandlers =

  /// <summary>
  /// Saved renderer frame pushed onto the stack on BeginCamera/BeginShader/BeginTarget
  /// and popped on the corresponding End, mirroring raylib's mode-stack.
  /// </summary>
  [<Struct>]
  type internal CameraFrame = {
    Camera: Camera2D voption
    Viewport: Viewport
    HasCustomViewport: bool
    HasScissor: bool
    ScissorRect: Rectangle
    Blend: BlendMode
    Sampler: SamplerState
    Shader: Effect voption
    HasRenderTarget: bool
    RenderTarget: RenderTarget2D voption
  }

  /// <summary>Mutable renderer state threaded through command dispatch byref.</summary>
  [<Struct>]
  type RendererState = {
    mutable Camera: Camera2D voption
    mutable Viewport: Viewport
    mutable HasCustomViewport: bool
    mutable HasScissor: bool
    mutable ScissorRect: Rectangle
    mutable Blend: BlendMode
    mutable Sampler: SamplerState
    mutable Shader: Effect voption
    mutable HasRenderTarget: bool
    mutable RenderTarget: RenderTarget2D voption
    WindowWidth: int
    WindowHeight: int
  }

  // ── Lit-sprite accumulator ────────────────────────────────────
  // Replaces the legacy one-draw-per-lit-sprite path. Consecutive lit
  // sprites sharing the same (effect, texture, normalMap) collapse into a
  // single DrawUserIndexedPrimitives call. Vertices carry world-space XY
  // (the lit shader derives WorldPos = input.Position.xy); the shared
  // MatrixTransform = view * projection is uploaded once per flush. The
  // accumulator is drained by flushBatches (so every state-transition
  // command flushes it automatically) and on lit-run exit.

  /// <summary>Mutable state for the lit-sprite batch accumulator.</summary>
  /// <remarks>
  /// One instance per <see cref="Renderer2D"/> (held on <see cref="RenderResources"/>)
  /// so stacked renderers don't clobber each other. All scratch arrays are reused
  /// across frames and flushes (AGENTS.md: avoid allocations in hot paths) and grown
  /// in place when exceeded.
  /// </remarks>
  [<Struct>]
  type LitBatchState = {
    /// Reused quad vertex buffer (4 verts per sprite), grown on demand.
    mutable Verts: VertexPositionColorTexture[]
    /// Reused index buffer (6 indices per sprite), grown on demand.
    mutable Indices: int[]
    /// Live vertex cursor (number of verts written this batch).
    mutable VertCount: int
    /// Live index cursor (number of indices written this batch).
    mutable IndexCount: int

    // ── Current sub-batch key: flush when any of these change ──
    // A strict (effect, texture, normalMap) key guarantees every sprite in a
    // batch samples the correct normal map (no last-wins sampler bug) and the
    // correct albedo; it also mirrors raylib's effective texture-id batching.
    mutable CurEffect: Effect
    mutable CurTexture: Texture2D
    mutable CurNormalMap: Texture2D voption
    /// The active lighting context (owns the effects + uniforms to upload).
    mutable CurLightCtx: LightContext2D
    /// True when there is pending lit geometry awaiting submission.
    mutable HasBatch: bool

    /// True when the SpriteBatch/PrimitiveBatch are currently in the Ended
    /// (suspended) state because handleLitSprite's entry flush drained them for
    /// a lit run. Distinct from HasBatch: a non-lit command interleaved inside
    /// a lighting block (e.g. SetSamplerState between two lit sprites) drains
    /// the lit geometry and reopens the batches via the exit guard, clearing
    /// this — so a later EndLighting knows NOT to restartBatches a second time
    /// (which would double-Begin). Cleared by handleEndLighting and
    /// flushLitRunAndReopen after they reopen.
    mutable BatchesSuspended: bool

    // ── Cached EffectParameter handles ──
    // Kill the per-draw effect.Parameters["..."] dictionary lookups the legacy
    // path did for MatrixTransform and Texture. Recached whenever the effect
    // instance changes.
    mutable CachedEffect: Effect
    mutable CachedMatrixParam: EffectParameter
    mutable CachedTexParam: EffectParameter

    // ── Render state for the flush (set via restartBatches/litBatchReset) ──
    mutable Transform: Matrix
    mutable Blend: BlendState
    mutable Rasterizer: RasterizerState
  }

  /// <summary>
  /// Backend resources the command handlers close over. The MonoGame analog
  /// of raylib's implicit global batch + primitives.
  /// </summary>
  type RenderResources = {
    SpriteBatch: SpriteBatch
    PrimitiveBatch: PrimitiveBatch
    WhitePixel: Texture2D
    mutable Stack: CameraFrame list
    /// Per-renderer lit-sprite accumulator. Kept on the resources struct
    /// (rather than a module-level mutable) so layered/stacked Renderer2D
    /// instances don't clobber each other's in-progress batch.
    mutable LitBatch: LitBatchState
  }

  // ── BlendMode helpers ─────────────────────────────────────────

  let toBlendState(mode: BlendMode) : BlendState =
    match mode with
    | BlendMode.AlphaBlend -> BlendState.AlphaBlend
    | BlendMode.NonPremultiplied -> BlendState.NonPremultiplied
    | BlendMode.Additive -> BlendState.Additive
    | BlendMode.Opaque -> BlendState.Opaque

  let defaultRasterizer = RasterizerState.CullNone

  let scissorRasterizer =
    let r = new RasterizerState()
    r.ScissorTestEnable <- true
    r.CullMode <- CullMode.None
    r

  // ── Batch lifecycle ───────────────────────────────────────────

  let beginSpriteBatch
    (
      sb: SpriteBatch,
      matrix: Matrix,
      blend: BlendMode,
      sampler: SamplerState,
      rasterizer: RasterizerState,
      effect: Effect voption
    ) =
    sb.Begin(
      SpriteSortMode.Deferred,
      toBlendState blend,
      sampler,
      DepthStencilState.None,
      rasterizer,
      (match effect with
       | ValueSome e -> e
       | ValueNone -> null),
      matrix
    )

  let inline private currentMatrix(state: byref<RendererState>) : Matrix =
    match state.Camera with
    | ValueSome c -> Camera2D.toMatrix c
    | ValueNone -> Matrix.Identity

  let inline private currentRasterizer
    (state: byref<RendererState>)
    : RasterizerState =
    if state.HasScissor then
      scissorRasterizer
    else
      defaultRasterizer

  // ── Lit-sprite batch accumulator functions ────────────────────

  let litBatchInit() : LitBatchState = {
    Verts = Array.zeroCreate<VertexPositionColorTexture> 2048
    Indices = Array.zeroCreate<int> 3072
    VertCount = 0
    IndexCount = 0
    CurEffect = null
    CurTexture = null
    CurNormalMap = ValueNone
    CurLightCtx = Unchecked.defaultof<_>
    HasBatch = false
    BatchesSuspended = false
    CachedEffect = null
    CachedMatrixParam = null
    CachedTexParam = null
    Transform = Matrix.Identity
    Blend = BlendState.NonPremultiplied
    Rasterizer = defaultRasterizer
  }

  /// Grow the vertex/index arrays (double capacity, copy contents). Called only
  /// when a sprite would overflow the current buffers — amortized O(1) per sprite.
  let private litBatchEnsureCapacity
    (st: byref<LitBatchState>)
    (extraVerts: int)
    (extraIndices: int)
    =
    if st.VertCount + extraVerts > st.Verts.Length then
      let nextCap = max (st.Verts.Length * 2) (st.VertCount + extraVerts)
      let next = Array.zeroCreate<VertexPositionColorTexture> nextCap
      Array.Copy(st.Verts, next, st.VertCount)
      st.Verts <- next

    if st.IndexCount + extraIndices > st.Indices.Length then
      let nextCap = max (st.Indices.Length * 2) (st.IndexCount + extraIndices)
      let next = Array.zeroCreate<int> nextCap
      Array.Copy(st.Indices, next, st.IndexCount)
      st.Indices <- next

  /// (Re)cache the MatrixTransform and Texture EffectParameter handles for an
  /// effect. Avoids the per-draw effect.Parameters["..."] dictionary lookup the
  /// legacy path paid for every sprite.
  let private litBatchCacheEffectParams
    (st: byref<LitBatchState>)
    (effect: Effect)
    =
    if not(obj.ReferenceEquals(st.CachedEffect, effect)) then
      st.CachedEffect <- effect
      st.CachedMatrixParam <- effect.Parameters["MatrixTransform"]
      st.CachedTexParam <- effect.Parameters["Texture"]

  /// Submit the pending lit geometry as one DrawUserIndexedPrimitives call,
  /// then reset the cursors. No-op when there is nothing pending.
  /// Uploads light uniforms once (gated by UniformsDirty) and binds the
  /// shared MatrixTransform/Texture/(NormalMap) once per flush.
  let litBatchFlush (st: byref<LitBatchState>) (gd: GraphicsDevice) =
    if not st.HasBatch || st.IndexCount = 0 then
      ()

    else
      let lightCtx = st.CurLightCtx
      let effect = st.CurEffect

      // Upload uniforms once per flush (dirty gate), to both effect variants.
      // Matches the legacy per-sprite cadence: Reset/EndLighting/EnableShadows/
      // DisableShadows set UniformsDirty; the first flush after that re-uploads.
      lightCtx.EnsureLocationsCached()

      if lightCtx.UniformsDirty then
        lightCtx.UploadUniforms()
        lightCtx.UniformsDirty <- false

      litBatchCacheEffectParams &st effect

      // MatrixTransform = view * projection (row-vector convention — see the
      // comment in the legacy handleLitSprite). MUST be view * projection; using
      // projection * view sends vertices to garbage clip coords (invisible).
      let matrixParam = st.CachedMatrixParam

      if matrixParam <> null then
        matrixParam.SetValue(st.Transform)

      let texParam = st.CachedTexParam

      if texParam <> null then
        texParam.SetValue(st.CurTexture)

      match st.CurNormalMap with
      | ValueSome nm ->
        let nmParam = lightCtx.NormalMapParameter

        if nmParam <> null then
          nmParam.SetValue(nm)
      | ValueNone -> ()

      let prevBlend = gd.BlendState
      let prevDepth = gd.DepthStencilState
      let prevRaster = gd.RasterizerState

      gd.BlendState <- st.Blend
      gd.DepthStencilState <- DepthStencilState.None
      gd.RasterizerState <- st.Rasterizer

      let primitiveCount = st.IndexCount / 3

      for pass in effect.CurrentTechnique.Passes do
        pass.Apply()
        // The parameter SetValue calls above are null-guarded so a malformed
        // effect (missing MatrixTransform/Texture params) degrades to the
        // shader's defaults instead of crashing — the draw itself always runs,
        // matching the legacy per-sprite path.
        gd.DrawUserIndexedPrimitives(
          PrimitiveType.TriangleList,
          st.Verts,
          0,
          st.VertCount,
          st.Indices,
          0,
          primitiveCount
        )
        |> ignore

      gd.BlendState <- prevBlend
      gd.DepthStencilState <- prevDepth
      gd.RasterizerState <- prevRaster

      st.VertCount <- 0
      st.IndexCount <- 0
      st.HasBatch <- false

  /// Append a lit sprite to the accumulator. Flushes first if the
  /// (effect, texture, normalMap) key changed since the previous sprite.
  /// The caller selects the effect (plain vs normal-map) from sprite.NormalMap
  /// exactly as the legacy path did.
  let litBatchAdd
    (st: byref<LitBatchState>)
    (lightCtx: LightContext2D)
    (effect: Effect)
    (sprite: SpriteState)
    (gd: GraphicsDevice)
    =
    let texture = sprite.Texture
    let normalMap = sprite.NormalMap

    // Strict batch key: flush on any change so every sprite samples the
    // correct albedo and normal map.
    let curNmBox =
      match st.CurNormalMap with
      | ValueSome t -> ValueSome(box t)
      | ValueNone -> ValueNone

    let nmBox =
      match normalMap with
      | ValueSome t -> ValueSome(box t)
      | ValueNone -> ValueNone

    let keyChanged =
      LitBatchTessellation.batchKeyChanged
        st.HasBatch
        st.CurEffect
        st.CurTexture
        curNmBox
        effect
        texture
        nmBox

    if keyChanged && st.HasBatch then
      litBatchFlush &st gd

    st.CurEffect <- effect
    st.CurTexture <- texture
    st.CurNormalMap <- normalMap
    st.CurLightCtx <- lightCtx

    // Tessellate into the reused arrays (pure CPU math — see LitBatchTessellation).
    litBatchEnsureCapacity &st 4 6

    let texW = float32 texture.Width
    let texH = float32 texture.Height
    let q = LitBatchTessellation.tessellate sprite texW texH
    let baseVertex = st.VertCount

    st.Verts[baseVertex] <- q.TL
    st.Verts[baseVertex + 1] <- q.TR
    st.Verts[baseVertex + 2] <- q.BR
    st.Verts[baseVertex + 3] <- q.BL
    st.VertCount <- baseVertex + 4

    LitBatchTessellation.writeIndices st.Indices st.IndexCount baseVertex
    st.IndexCount <- st.IndexCount + 6

    st.HasBatch <- true

  /// Store the current transform/blend/rasterizer for the next flush. Does NOT
  /// flush — the surrounding flushBatches already drained the batch. Parallel to
  /// PrimitiveBatch.SetTransform/SetBlendState/SetRasterizerState.
  let litBatchReset
    (st: byref<LitBatchState>)
    (matrix: Matrix)
    (blend: BlendState)
    (rasterizer: RasterizerState)
    =
    st.Transform <- matrix
    st.Blend <- blend
    st.Rasterizer <- rasterizer

  let inline private flushBatches (res: RenderResources) (gd: GraphicsDevice) =
    // Drain the lit accumulator first so its geometry is submitted in order
    // relative to the pending SpriteBatch/PrimitiveBatch draws.
    litBatchFlush &res.LitBatch gd
    res.SpriteBatch.End()
    res.PrimitiveBatch.Flush()

  let inline private restartBatches
    (res: RenderResources)
    (state: byref<RendererState>)
    =
    let matrix = currentMatrix &state
    let raster = currentRasterizer &state

    beginSpriteBatch(
      res.SpriteBatch,
      matrix,
      state.Blend,
      state.Sampler,
      raster,
      state.Shader
    )

    res.PrimitiveBatch.SetTransform(matrix)
    res.PrimitiveBatch.SetBlendState(toBlendState state.Blend)
    res.PrimitiveBatch.SetRasterizerState(raster)
    res.PrimitiveBatch.SetEffect(state.Shader)
    // Re-arm the lit accumulator with the current transform/blend/rasterizer so
    // the next flush uses them. litBatchReset does not flush (flushBatches did).
    litBatchReset &res.LitBatch matrix (toBlendState state.Blend) raster

  let inline private endAndRestart
    (res: RenderResources)
    (state: byref<RendererState>)
    (gd: GraphicsDevice)
    =
    flushBatches res gd
    restartBatches res &state

  // ── Camera / viewport stack ───────────────────────────────────

  let private pushFrame (res: RenderResources) (state: byref<RendererState>) =
    res.Stack <-
      {
        Camera = state.Camera
        Viewport = state.Viewport
        HasCustomViewport = state.HasCustomViewport
        HasScissor = state.HasScissor
        ScissorRect = state.ScissorRect
        Blend = state.Blend
        Sampler = state.Sampler
        Shader = state.Shader
        HasRenderTarget = state.HasRenderTarget
        RenderTarget = state.RenderTarget
      }
      :: res.Stack

  let private popFrame
    (gd: GraphicsDevice)
    (res: RenderResources)
    (state: byref<RendererState>)
    =
    match res.Stack with
    | [] -> ()
    | frame :: rest ->
      res.Stack <- rest
      state.Camera <- frame.Camera
      state.Viewport <- frame.Viewport
      state.HasCustomViewport <- frame.HasCustomViewport
      state.HasScissor <- frame.HasScissor
      state.ScissorRect <- frame.ScissorRect
      state.Blend <- frame.Blend
      state.Sampler <- frame.Sampler
      state.Shader <- frame.Shader
      state.HasRenderTarget <- frame.HasRenderTarget
      state.RenderTarget <- frame.RenderTarget
      gd.Viewport <- frame.Viewport

      if frame.HasScissor then
        gd.ScissorRectangle <- frame.ScissorRect

      if frame.HasRenderTarget then
        match frame.RenderTarget with
        | ValueSome rt -> gd.SetRenderTarget(rt)
        | ValueNone -> gd.SetRenderTarget(null)
      else
        gd.SetRenderTarget(null)

  // ── Camera state management ───────────────────────────────────

  let private beginCamera
    (c: Camera2D)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    pushFrame res &state
    state.Camera <- ValueSome c
    endAndRestart res &state gd

  let private beginCameraConfig
    (config: Camera2DConfig)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    flushBatches res gd
    pushFrame res &state
    state.Camera <- ValueSome config.Camera

    match config.Viewport with
    | ValueSome vp ->
      gd.Viewport <- Viewport(vp.X, vp.Y, vp.Width, vp.Height)
      state.Viewport <- gd.Viewport
      state.HasCustomViewport <- true
    | ValueNone -> ()

    match config.ClearColor with
    | ValueSome c -> gd.Clear(c)
    | ValueNone -> ()

    restartBatches res &state

  let private endCamera
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    flushBatches res gd
    popFrame gd res &state
    restartBatches res &state

  // ── Escape hatch ──────────────────────────────────────────────

  let private drawImmediate
    (action: unit -> unit)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    flushBatches res gd
    pushFrame res &state
    state.Camera <- ValueNone
    state.Shader <- ValueNone
    gd.SetRenderTarget(null)

    match state.RenderTarget with
    | ValueSome _ -> state.HasRenderTarget <- false
    | ValueNone -> ()

    try
      action()
    finally
      popFrame gd res &state
      restartBatches res &state

  // ── Primitive tessellation helpers ────────────────────────────

  let inline private vpc(position: Vector2, color: Color) =
    VertexPositionColor(Vector3(position.X, position.Y, 0.0f), color)

  let private fillCircle
    (pb: PrimitiveBatch)
    (center: Vector2)
    (radius: float32)
    (color: Color)
    =
    if radius <= 0.0f then
      ()
    else

    let segments = max 3 (int(radius / 2.0f) + 8)
    let step = MathF.PI * 2.0f / float32 segments
    let points = Array.zeroCreate<Vector2>(segments + 2)
    points[0] <- center

    for i = 0 to segments do
      let angle = float32 i * step

      points[i + 1] <-
        Vector2(
          center.X + MathF.Cos(angle) * radius,
          center.Y + MathF.Sin(angle) * radius
        )

    pb.AddTriangleFan(points, color)

  let private circleOutline
    (pb: PrimitiveBatch)
    (center: Vector2)
    (radius: float32)
    (color: Color)
    =
    if radius <= 0.0f then
      ()
    else

    let segments = max 3 (int(radius / 2.0f) + 8)
    let step = MathF.PI * 2.0f / float32 segments
    let points = Array.zeroCreate<Vector2>(segments + 1)

    for i = 0 to segments do
      let angle = float32 i * step

      points[i] <-
        Vector2(
          center.X + MathF.Cos(angle) * radius,
          center.Y + MathF.Sin(angle) * radius
        )

    pb.AddLineStrip(points, color)

  let private circleSector
    (pb: PrimitiveBatch)
    (center: Vector2)
    (radius: float32)
    (startAngle: float32)
    (endAngle: float32)
    (segments: int)
    (color: Color)
    =
    if radius <= 0.0f then
      ()
    else

    let segments = max 3 segments
    let startRad = MathHelper.ToRadians(startAngle)
    let endRad = MathHelper.ToRadians(endAngle)
    let sweep = endRad - startRad
    let step = sweep / float32 segments
    // Open fan: center + rim points from startAngle to endAngle (inclusive).
    // We do NOT close the loop — closing would draw a chord across the arc mouth.
    let points = Array.zeroCreate<Vector2>(segments + 2)
    points[0] <- center

    // Rim points run from startAngle to endAngle inclusive: that's segments+1
    // points (indices 0..segments), stored at points[1..segments+1].
    for i = 0 to segments do
      let angle = startRad + float32 i * step

      points[i + 1] <-
        Vector2(
          center.X + MathF.Cos(angle) * radius,
          center.Y + MathF.Sin(angle) * radius
        )

    pb.AddTriangleFan(points, color, closeLoop = false)

  let private circleSectorOutline
    (pb: PrimitiveBatch)
    (center: Vector2)
    (radius: float32)
    (startAngle: float32)
    (endAngle: float32)
    (segments: int)
    (color: Color)
    =
    if radius <= 0.0f then
      ()
    else

    let segments = max 3 segments
    let startRad = MathHelper.ToRadians(startAngle)
    let endRad = MathHelper.ToRadians(endAngle)
    let sweep = endRad - startRad
    let step = sweep / float32 segments
    let points = Array.zeroCreate<Vector2>(segments + 1)

    for i = 0 to segments do
      let angle = startRad + float32 i * step

      points[i] <-
        Vector2(
          center.X + MathF.Cos(angle) * radius,
          center.Y + MathF.Sin(angle) * radius
        )

    pb.AddLineStrip(points, color)

  let private circleGradient
    (pb: PrimitiveBatch)
    (centerX: int)
    (centerY: int)
    (radius: float32)
    (inner: Color)
    (outer: Color)
    =
    if radius <= 0.0f then
      ()
    else

    let center = Vector2(float32 centerX, float32 centerY)
    let segments = max 3 (int(radius / 2.0f) + 8)
    let step = MathF.PI * 2.0f / float32 segments
    let verts = Array.zeroCreate<VertexPositionColor>((segments + 1) * 3)

    for i = 0 to segments do
      let a0 = float32 i * step
      let a1 = float32(i + 1) * step
      let v0 = Vector2(center.X, center.Y)

      let v1 =
        Vector2(
          center.X + MathF.Cos(a0) * radius,
          center.Y + MathF.Sin(a0) * radius
        )

      let v2 =
        Vector2(
          center.X + MathF.Cos(a1) * radius,
          center.Y + MathF.Sin(a1) * radius
        )

      let baseIdx = i * 3
      verts[baseIdx + 0] <- vpc(v0, inner)
      verts[baseIdx + 1] <- vpc(v1, outer)
      verts[baseIdx + 2] <- vpc(v2, outer)

    pb.AddTriangles(verts)

  let private fillRing
    (pb: PrimitiveBatch)
    (center: Vector2)
    (innerR: float32)
    (outerR: float32)
    (startAngle: float32)
    (endAngle: float32)
    (segments: int)
    (color: Color)
    =
    if innerR <= 0.0f || outerR <= innerR then
      ()
    else

    let segments = max 3 segments
    let startRad = MathHelper.ToRadians(startAngle)
    let endRad = MathHelper.ToRadians(endAngle)
    let sweep = endRad - startRad
    let step = sweep / float32 segments
    let verts = Array.zeroCreate<VertexPositionColor>((segments + 1) * 6)

    for i = 0 to segments do
      let a0 = startRad + float32 i * step
      let a1 = startRad + float32(i + 1) * step
      let c0 = MathF.Cos(a0)
      let s0 = MathF.Sin(a0)
      let c1 = MathF.Cos(a1)
      let s1 = MathF.Sin(a1)
      let p0 = Vector2(center.X + c0 * innerR, center.Y + s0 * innerR)
      let p1 = Vector2(center.X + c0 * outerR, center.Y + s0 * outerR)
      let p2 = Vector2(center.X + c1 * outerR, center.Y + s1 * outerR)
      let p3 = Vector2(center.X + c1 * innerR, center.Y + s1 * innerR)
      let baseIdx = i * 6
      verts[baseIdx + 0] <- vpc(p0, color)
      verts[baseIdx + 1] <- vpc(p1, color)
      verts[baseIdx + 2] <- vpc(p2, color)
      verts[baseIdx + 3] <- vpc(p0, color)
      verts[baseIdx + 4] <- vpc(p2, color)
      verts[baseIdx + 5] <- vpc(p3, color)

    pb.AddTriangles(verts)

  let private ringOutline
    (pb: PrimitiveBatch)
    (center: Vector2)
    (innerR: float32)
    (outerR: float32)
    (startAngle: float32)
    (endAngle: float32)
    (segments: int)
    (color: Color)
    =
    if innerR <= 0.0f || outerR <= innerR then
      ()
    else

    let segments = max 3 segments
    let startRad = MathHelper.ToRadians(startAngle)
    let endRad = MathHelper.ToRadians(endAngle)
    let sweep = endRad - startRad
    let step = sweep / float32 segments
    let points = Array.zeroCreate<Vector2>((segments + 1) * 2)
    let mutable idx = 0

    for i = 0 to segments do
      let a = startRad + float32 i * step
      let c = MathF.Cos(a)
      let s = MathF.Sin(a)
      points[idx] <- Vector2(center.X + c * outerR, center.Y + s * outerR)
      idx <- idx + 1
      points[idx] <- Vector2(center.X + c * innerR, center.Y + s * innerR)
      idx <- idx + 1

    pb.AddTriangleStrip(points, color)

  let private fillEllipse
    (pb: PrimitiveBatch)
    (centerX: int)
    (centerY: int)
    (radiusH: float32)
    (radiusV: float32)
    (color: Color)
    =
    if radiusH <= 0.0f || radiusV <= 0.0f then
      ()
    else

    let center = Vector2(float32 centerX, float32 centerY)
    let segments = max 3 (int(max radiusH radiusV / 2.0f) + 8)
    let step = MathF.PI * 2.0f / float32 segments
    let points = Array.zeroCreate<Vector2>(segments + 2)
    points[0] <- center

    for i = 0 to segments do
      let angle = float32 i * step

      points[i + 1] <-
        Vector2(
          center.X + MathF.Cos(angle) * radiusH,
          center.Y + MathF.Sin(angle) * radiusV
        )

    pb.AddTriangleFan(points, color)

  let private ellipseOutline
    (pb: PrimitiveBatch)
    (centerX: int)
    (centerY: int)
    (radiusH: float32)
    (radiusV: float32)
    (color: Color)
    =
    if radiusH <= 0.0f || radiusV <= 0.0f then
      ()
    else

    let center = Vector2(float32 centerX, float32 centerY)
    let segments = max 3 (int(max radiusH radiusV / 2.0f) + 8)
    let step = MathF.PI * 2.0f / float32 segments
    let points = Array.zeroCreate<Vector2>(segments + 1)

    for i = 0 to segments do
      let angle = float32 i * step

      points[i] <-
        Vector2(
          center.X + MathF.Cos(angle) * radiusH,
          center.Y + MathF.Sin(angle) * radiusV
        )

    pb.AddLineStrip(points, color)

  let private fillRectGradientV
    (pb: PrimitiveBatch)
    (x: int)
    (y: int)
    (w: int)
    (h: int)
    (top: Color)
    (bottom: Color)
    =
    if w <= 0 || h <= 0 then
      ()
    else

    let x0 = float32 x
    let y0 = float32 y
    let x1 = x0 + float32 w
    let y1 = y0 + float32 h
    let tl = Vector2(x0, y0)
    let tr = Vector2(x1, y0)
    let bl = Vector2(x0, y1)
    let br = Vector2(x1, y1)

    pb.AddTriangles(
      [|
        vpc(tl, top)
        vpc(tr, top)
        vpc(br, bottom)
        vpc(tl, top)
        vpc(br, bottom)
        vpc(bl, bottom)
      |]
    )

  let private fillRectGradientH
    (pb: PrimitiveBatch)
    (x: int)
    (y: int)
    (w: int)
    (h: int)
    (left: Color)
    (right: Color)
    =
    if w <= 0 || h <= 0 then
      ()
    else

    let x0 = float32 x
    let y0 = float32 y
    let x1 = x0 + float32 w
    let y1 = y0 + float32 h
    let tl = Vector2(x0, y0)
    let tr = Vector2(x1, y0)
    let bl = Vector2(x0, y1)
    let br = Vector2(x1, y1)

    pb.AddTriangles(
      [|
        vpc(tl, left)
        vpc(tr, right)
        vpc(br, right)
        vpc(tl, left)
        vpc(br, right)
        vpc(bl, left)
      |]
    )

  let private fillRectGradient
    (pb: PrimitiveBatch)
    (rect: Rectangle)
    (tlColor: Color)
    (blColor: Color)
    (trColor: Color)
    (brColor: Color)
    =
    if rect.Width <= 0 || rect.Height <= 0 then
      ()
    else

    let x0 = float32 rect.X
    let y0 = float32 rect.Y
    let x1 = x0 + float32 rect.Width
    let y1 = y0 + float32 rect.Height
    let tl = Vector2(x0, y0)
    let tr = Vector2(x1, y0)
    let bl = Vector2(x0, y1)
    let br = Vector2(x1, y1)

    pb.AddTriangles(
      [|
        vpc(tl, tlColor)
        vpc(tr, trColor)
        vpc(br, brColor)
        vpc(tl, tlColor)
        vpc(br, brColor)
        vpc(bl, blColor)
      |]
    )

  let private rectOutline
    (pb: PrimitiveBatch)
    (rect: Rectangle)
    (thickness: float32)
    (color: Color)
    =
    if rect.Width <= 0 || rect.Height <= 0 || thickness <= 0.0f then
      ()
    else if

      thickness <= 1.0f
    then
      let x0 = float32 rect.X
      let y0 = float32 rect.Y
      let x1 = x0 + float32 rect.Width
      let y1 = y0 + float32 rect.Height

      let points = [|
        Vector2(x0, y0)
        Vector2(x1, y0)
        Vector2(x1, y1)
        Vector2(x0, y1)
        Vector2(x0, y0)
      |]

      pb.AddLineStrip(points, color)
    else
      let half = thickness * 0.5f
      let x0 = float32 rect.X - half
      let y0 = float32 rect.Y - half
      let x1 = x0 + float32 rect.Width + thickness
      let y1 = y0 + float32 rect.Height + thickness
      let tl = Vector2(x0, y0)
      let tr = Vector2(x1, y0)
      let br = Vector2(x1, y1)
      let bl = Vector2(x0, y1)
      let x0i = float32 rect.X + half
      let y0i = float32 rect.Y + half
      let x1i = x0i + float32 rect.Width - thickness
      let y1i = y0i + float32 rect.Height - thickness
      let tli = Vector2(x0i, y0i)
      let tri = Vector2(x1i, y0i)
      let bri = Vector2(x1i, y1i)
      let bli = Vector2(x0i, y1i)
      // Interleave outer and inner corners around the perimeter so the strip
      // forms a hollow ring (the border). The previous outer-then-inner
      // ordering made the strip's leading triangles span the full outer rect,
      // filling it solid instead of outlining it (visible as a filled square
      // where a border was expected — e.g. the minimap frame covering the map).
      let ring = [| tl; tli; tr; tri; br; bri; bl; bli; tl; tli |]

      pb.AddTriangleStrip(ring, color)

  let private roundedRectPath
    (rect: Rectangle)
    (roundness: float32)
    (segments: int)
    : Vector2[] =
    let w = float32 rect.Width
    let h = float32 rect.Height
    let r = MathHelper.Clamp(roundness, 0.0f, 1.0f) * min w h * 0.5f
    let segments = max 1 segments
    let quarter = segments
    let total = (quarter + 1) * 4
    let path = Array.zeroCreate<Vector2>(total)

    let cornerCenters = [|
      Vector2(float32 rect.X + w - r, float32 rect.Y + r)
      Vector2(float32 rect.X + w - r, float32 rect.Y + h - r)
      Vector2(float32 rect.X + r, float32 rect.Y + h - r)
      Vector2(float32 rect.X + r, float32 rect.Y + r)
    |]

    let baseAngles = [| -MathF.PI / 2.0f; 0.0f; MathF.PI / 2.0f; MathF.PI |]
    let mutable idx = 0

    for corner = 0 to 3 do
      let center = cornerCenters[corner]
      let angleBase = baseAngles[corner]

      for s = 0 to quarter do
        let angle = angleBase + float32 s / float32 quarter * (MathF.PI / 2.0f)

        path[idx] <-
          Vector2(
            center.X + MathF.Cos(angle) * r,
            center.Y + MathF.Sin(angle) * r
          )

        idx <- idx + 1

    path

  let private fillRectRounded
    (pb: PrimitiveBatch)
    (rect: Rectangle)
    (roundness: float32)
    (segments: int)
    (color: Color)
    =
    if rect.Width <= 0 || rect.Height <= 0 then
      ()
    else if

      roundness <= 0.0f
    then
      pb.AddTriangles(
        [|
          vpc(Vector2(float32 rect.X, float32 rect.Y), color)
          vpc(Vector2(float32(rect.X + rect.Width), float32 rect.Y), color)
          vpc(
            Vector2(float32(rect.X + rect.Width), float32(rect.Y + rect.Height)),
            color
          )
          vpc(Vector2(float32 rect.X, float32 rect.Y), color)
          vpc(
            Vector2(float32(rect.X + rect.Width), float32(rect.Y + rect.Height)),
            color
          )
          vpc(Vector2(float32 rect.X, float32(rect.Y + rect.Height)), color)
        |]
      )
    else
      let path = roundedRectPath rect roundness segments
      // AddTriangleFan treats points[0] as the fan center, but roundedRectPath
      // returns only the perimeter. Prepend the rect centroid so the fan
      // radiates from the center, filling the rounded rectangle correctly.
      let center =
        Vector2(
          float32 rect.X + float32 rect.Width * 0.5f,
          float32 rect.Y + float32 rect.Height * 0.5f
        )

      let fan = Array.zeroCreate<Vector2>(path.Length + 1)
      fan[0] <- center

      for i = 0 to path.Length - 1 do
        fan[i + 1] <- path[i]

      pb.AddTriangleFan(fan, color)

  let private rectRoundedOutline
    (pb: PrimitiveBatch)
    (rect: Rectangle)
    (roundness: float32)
    (segments: int)
    (thickness: float32)
    (color: Color)
    =
    if rect.Width <= 0 || rect.Height <= 0 || thickness <= 0.0f then
      ()
    else if

      roundness <= 0.0f
    then
      rectOutline pb rect thickness color
    else
      let path = roundedRectPath rect roundness segments

      if thickness <= 1.0f then
        pb.AddLineStrip(path, color)
      else
        // Build a thick outline by extruding each point along its normal.
        let half = thickness * 0.5f
        let n = path.Length
        let outer = Array.zeroCreate<Vector2> n
        let inner = Array.zeroCreate<Vector2> n

        for i = 0 to n - 1 do
          let prev = path[(i - 1 + n) % n]
          let curr = path[i]
          let next = path[(i + 1) % n]
          let tx = next.X - prev.X
          let ty = next.Y - prev.Y
          let len = sqrt(tx * tx + ty * ty)

          if len > 0.0f then
            let nx = ty / len
            let ny = -tx / len
            outer[i] <- Vector2(curr.X + nx * half, curr.Y + ny * half)
            inner[i] <- Vector2(curr.X - nx * half, curr.Y - ny * half)
          else
            outer[i] <- curr
            inner[i] <- curr

        // Produce one triangle strip that goes around outer then inner reversed.
        let strip = Array.zeroCreate<Vector2>(n * 2 + 2)

        for i = 0 to n - 1 do
          strip[i * 2] <- outer[i]
          strip[i * 2 + 1] <- inner[i]

        strip[n * 2] <- outer[0]
        strip[n * 2 + 1] <- inner[0]
        pb.AddTriangleStrip(strip, color)

  let private fillPoly
    (pb: PrimitiveBatch)
    (center: Vector2)
    (sides: int)
    (radius: float32)
    (rotation: float32)
    (color: Color)
    =
    if sides < 3 || radius <= 0.0f then
      ()
    else

    let rotationRad = MathHelper.ToRadians(rotation)
    let step = MathF.PI * 2.0f / float32 sides
    let points = Array.zeroCreate<Vector2>(sides + 2)
    points[0] <- center

    for i = 0 to sides do
      let angle = rotationRad + float32 i * step

      points[i + 1] <-
        Vector2(
          center.X + MathF.Cos(angle) * radius,
          center.Y + MathF.Sin(angle) * radius
        )

    pb.AddTriangleFan(points, color)

  let private polyOutline
    (pb: PrimitiveBatch)
    (center: Vector2)
    (sides: int)
    (radius: float32)
    (rotation: float32)
    (thickness: float32)
    (color: Color)
    =
    if sides < 3 || radius <= 0.0f || thickness <= 0.0f then
      ()
    else

    let rotationRad = MathHelper.ToRadians(rotation)
    let step = MathF.PI * 2.0f / float32 sides
    let points = Array.zeroCreate<Vector2>(sides + 1)

    for i = 0 to sides do
      let angle = rotationRad + float32 i * step

      points[i] <-
        Vector2(
          center.X + MathF.Cos(angle) * radius,
          center.Y + MathF.Sin(angle) * radius
        )

    if thickness <= 1.0f then
      pb.AddLineStrip(points, color)
    else
      pb.AddLineThick(points[sides - 1], points[0], thickness, color)

      for i = 1 to sides - 1 do
        pb.AddLineThick(points[i - 1], points[i], thickness, color)

  let private fillTriangle
    (pb: PrimitiveBatch)
    (v1: Vector2)
    (v2: Vector2)
    (v3: Vector2)
    (color: Color)
    =
    pb.AddTriangles([| vpc(v1, color); vpc(v2, color); vpc(v3, color) |])

  let private bezier
    (pb: PrimitiveBatch)
    (start: Vector2)
    (control: Vector2)
    (finish: Vector2)
    (thickness: float32)
    (color: Color)
    =
    let steps = max 2 (int(thickness * 2.0f) + 16)
    let prev = ref start

    for i = 1 to steps do
      let t = float32 i / float32 steps
      let u = 1.0f - t

      let p =
        Vector2(
          u * u * start.X + 2.0f * u * t * control.X + t * t * finish.X,
          u * u * start.Y + 2.0f * u * t * control.Y + t * t * finish.Y
        )

      pb.AddLineThick(!prev, p, thickness, color)
      prev := p

  // ── Lit sprite draw path ────────────────────────────────────────
  // Lit sprites accumulate into res.LitBatch (4 verts + 6 indices each) and
  // are submitted as one DrawUserIndexedPrimitives per (effect, texture,
  // normalMap) group — collapsing the legacy one-draw-per-sprite path.
  // Uniform upload, MatrixTransform/Texture/NormalMap binding, and the
  // blend/depth/raster state save+restore all happen once per flush (in
  // litBatchFlush), not per sprite. Mirrors raylib's batched handleLitSprite.

  let private handleLitSprite
    (lightCtx: LightContext2D)
    (sprite: SpriteState)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    // Entering a lit run: if no lit batch is active yet, flush the pending
    // SpriteBatch/PrimitiveBatch draws ONCE so they render before the lit
    // geometry. While the lit run is active the other batches stay suspended
    // (nothing should be appending to them); they are re-opened lazily on the
    // next non-lit command (see the exit guard in execute) or by handleEndLighting.
    // This preserves the lit/unlit draw-order contract the legacy per-sprite
    // flushBatches+restartBatches enforced, but once per run instead of per sprite.
    if not res.LitBatch.HasBatch then
      flushBatches res gd
      res.LitBatch.BatchesSuspended <- true

    // Select effect (plain vs normal-map) exactly as the legacy path did.
    let effect =
      match sprite.NormalMap with
      | ValueSome _ -> lightCtx.NormalMapEffect
      | ValueNone -> lightCtx.Effect

    lightCtx.ShaderActive <- true

    // MatrixTransform = view * projection is recomputed and stored on the batch
    // state every sprite. It is cheap (a viewport read + one ortho + one matrix
    // multiply) and only actually uploaded once per flush (litBatchFlush). It
    // must be view * projection (row-vector convention) — projection * view
    // sends vertices to garbage clip coords (invisible). See the comment in
    // litBatchFlush.
    let vp = gd.Viewport

    let projection =
      Matrix.CreateOrthographicOffCenter(
        0.0f,
        float32 vp.Width,
        float32 vp.Height,
        0.0f,
        0.0f,
        -1.0f
      )

    let view = currentMatrix &state
    let matrixTransform = view * projection

    litBatchReset
      &res.LitBatch
      matrixTransform
      (toBlendState state.Blend)
      (currentRasterizer &state)

    // Append to the accumulator. litBatchAdd flushes automatically when the
    // (effect, texture, normalMap) key changes. Uniform upload + GPU submission
    // happen inside litBatchFlush, gated by UniformsDirty.
    litBatchAdd &res.LitBatch lightCtx effect sprite gd

  let private handleEndLighting
    (lightCtx: LightContext2D)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    if lightCtx.ShaderActive then
      // EndLighting is a natural batch boundary: submit the block's pending lit
      // geometry in order before re-arming the dirty flag.
      litBatchFlush &res.LitBatch gd
      lightCtx.ShaderActive <- false
      lightCtx.UniformsDirty <- true

      // If the lit run left the SpriteBatch/PrimitiveBatch suspended (Ended),
      // re-open them here so the next non-lit command's flushBatches->End() is
      // balanced. The exit guard in execute keys off HasBatch, which
      // litBatchFlush just cleared, so it would NOT reopen — EndLighting must.
      // Gated on BatchesSuspended (not ShaderActive/HasBatch): a non-lit command
      // interleaved before EndLighting already reopened via the exit guard, and
      // a blind restartBatches here would double-Begin.
      if res.LitBatch.BatchesSuspended then
        restartBatches res &state
        res.LitBatch.BatchesSuspended <- false

  /// Drain any pending lit geometry and, if the lit run left the
  /// SpriteBatch/PrimitiveBatch suspended (Ended), re-open them so a subsequent
  /// `SpriteBatch.End()`/`PrimitiveBatch.End()` is balanced.
  ///
  /// Used by the renderer's frame-end cleanup: a frame may legitimately end
  /// inside a lighting block (no EndLighting before the final EndCamera), in
  /// which case the batches are still in the Ended state handleLitSprite put
  /// them in. Calling End() then would throw. This restores the Begun state
  /// using the renderer state `execute` left behind. No-op when no lit run is
  /// pending (the batches are already Begun).
  let inline flushLitRunAndReopen
    (litBatch: byref<LitBatchState>)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    if litBatch.HasBatch then
      litBatchFlush &litBatch gd
      restartBatches res &state
      litBatch.BatchesSuspended <- false

  // ── Main dispatch ─────────────────────────────────────────────

  let execute
    (
      state: byref<RendererState>,
      buffer: RenderBuffer2D,
      res: RenderResources,
      gd: GraphicsDevice
    ) =
    let sb = res.SpriteBatch
    let pb = res.PrimitiveBatch

    for i = 0 to buffer.Count - 1 do
      let cmd = buffer[i]

      // Lit-run exit guard: if we were accumulating lit geometry and the next
      // command is not part of the lighting block, submit the lit geometry in
      // order and re-open the suspended SpriteBatch/PrimitiveBatch before the
      // non-lit command runs. This is the counterpart to handleLitSprite's
      // entry flush and preserves lit/unlit draw order without flushing per sprite.
      match cmd with
      | Command2D.LitSprite _
      | Command2D.NoopLight _
      | Command2D.EndLighting _
      | Command2D.EnableShadows _
      | Command2D.DisableShadows _ -> ()
      | _ when res.LitBatch.HasBatch ->
        litBatchFlush &res.LitBatch gd
        restartBatches res &state
        // The batches were suspended for the lit run; restartBatches just
        // reopened them. Clear the flag so a later EndLighting does not
        // restartBatches a second time (double-Begin).
        res.LitBatch.BatchesSuspended <- false
      | _ -> ()

      match cmd with
      // Sprite & Text
      | Command2D.Sprite(texture, dest, source, origin, rotation, color, _) ->
        // Translate negative source rect dimensions into SpriteEffects
        let mutable effects = SpriteEffects.None
        let mutable src = source

        if src.Width < 0 then
          effects <- effects ||| SpriteEffects.FlipHorizontally
          src <- Rectangle(src.X, src.Y, -src.Width, src.Height)

        if src.Height < 0 then
          effects <- effects ||| SpriteEffects.FlipVertically
          src <- Rectangle(src.X, src.Y, src.Width, -src.Height)

        let srcOrigin =
          if dest.Width > 0 && dest.Height > 0 then
            Vector2(
              origin.X * (float32 src.Width / float32 dest.Width),
              origin.Y * (float32 src.Height / float32 dest.Height)
            )
          else
            origin

        sb.Draw(
          texture,
          dest,
          Nullable src,
          color,
          rotation,
          srcOrigin,
          effects,
          0.0f
        )

      | Command2D.Text(font, text, position, scale, color, _) ->
        sb.DrawString(
          font,
          text,
          position,
          color,
          0.0f,
          Vector2.Zero,
          scale,
          SpriteEffects.None,
          0.0f
        )

      // Rectangles
      | Command2D.FillRect(rect, color, _) ->
        sb.Draw(res.WhitePixel, rect, Nullable(), color)

      | Command2D.RectOutline(rect, thickness, color, _) ->
        rectOutline pb rect thickness color

      | Command2D.FillRectRounded(rect, roundness, segments, color, _) ->
        fillRectRounded pb rect roundness segments color

      | Command2D.RectRoundedOutline(rect,
                                     roundness,
                                     segments,
                                     thickness,
                                     color,
                                     _) ->
        rectRoundedOutline pb rect roundness segments thickness color

      | Command2D.RectGradientV(x, y, w, h, top, bottom, _) ->
        fillRectGradientV pb x y w h top bottom

      | Command2D.RectGradientH(x, y, w, h, left, right, _) ->
        fillRectGradientH pb x y w h left right

      | Command2D.RectGradient(rect, tl, bl, tr, br, _) ->
        fillRectGradient pb rect tl bl tr br

      // Circles & Ellipses
      | Command2D.FillCircle(center, radius, color, _) ->
        fillCircle pb center radius color

      | Command2D.CircleOutline(center, radius, color, _) ->
        circleOutline pb center radius color

      | Command2D.CircleSector(center,
                               radius,
                               startAngle,
                               endAngle,
                               segments,
                               color,
                               _) ->
        circleSector pb center radius startAngle endAngle segments color

      | Command2D.CircleSectorOutline(center,
                                      radius,
                                      startAngle,
                                      endAngle,
                                      segments,
                                      color,
                                      _) ->
        circleSectorOutline pb center radius startAngle endAngle segments color

      | Command2D.CircleGradient(centerX, centerY, radius, inner, outer, _) ->
        circleGradient pb centerX centerY radius inner outer

      | Command2D.FillRing(center,
                           innerR,
                           outerR,
                           startAngle,
                           endAngle,
                           segments,
                           color,
                           _) ->
        fillRing pb center innerR outerR startAngle endAngle segments color

      | Command2D.RingOutline(center,
                              innerR,
                              outerR,
                              startAngle,
                              endAngle,
                              segments,
                              color,
                              _) ->
        ringOutline pb center innerR outerR startAngle endAngle segments color

      | Command2D.FillEllipse(centerX, centerY, radiusH, radiusV, color, _) ->
        fillEllipse pb centerX centerY radiusH radiusV color

      | Command2D.EllipseOutline(centerX, centerY, radiusH, radiusV, color, _) ->
        ellipseOutline pb centerX centerY radiusH radiusV color

      // Lines & Curves
      | Command2D.Line(start, finish, color, _) ->
        pb.AddLine(start, finish, color)

      | Command2D.LineThick(start, finish, thickness, color, _) ->
        pb.AddLineThick(start, finish, thickness, color)

      | Command2D.LineStrip(points, color, _) -> pb.AddLineStrip(points, color)

      | Command2D.Bezier(start, control, finish, thickness, color, _) ->
        bezier pb start control finish thickness color

      // Triangles & Polygons
      | Command2D.Triangle(v1, v2, v3, color, _) ->
        fillTriangle pb v1 v2 v3 color

      | Command2D.TriangleFan(points, color, _) ->
        pb.AddTriangleFan(points, color)

      | Command2D.TriangleStrip(points, color, _) ->
        pb.AddTriangleStrip(points, color)

      | Command2D.FillPoly(center, sides, radius, rotation, color, _) ->
        fillPoly pb center sides radius rotation color

      | Command2D.PolyOutline(center,
                              sides,
                              radius,
                              rotation,
                              thickness,
                              color,
                              _) ->
        polyOutline pb center sides radius rotation thickness color

      // Camera & Targets
      | Command2D.BeginCamera(camera, _) -> beginCamera camera &state res gd

      | Command2D.BeginCameraConfig(config, _) ->
        beginCameraConfig config &state res gd

      | Command2D.EndCamera _ -> endCamera &state res gd

      // Shaders
      | Command2D.BeginShader(shader, _) ->
        pushFrame res &state
        state.Shader <- ValueSome shader
        endAndRestart res &state gd

      | Command2D.EndShader _ ->
        flushBatches res gd
        popFrame gd res &state
        restartBatches res &state

      // Render Targets
      | Command2D.BeginTarget(target, _) ->
        pushFrame res &state
        state.HasRenderTarget <- true
        state.RenderTarget <- ValueSome target
        flushBatches res gd
        gd.SetRenderTarget(target)
        restartBatches res &state

      | Command2D.EndTarget _ ->
        flushBatches res gd
        popFrame gd res &state
        restartBatches res &state

      // Render State
      | Command2D.SetBlend(mode, _) ->
        if state.Blend <> mode then
          state.Blend <- mode
          endAndRestart res &state gd

      | Command2D.SetSamplerState(sampler, _) ->
        if state.Sampler <> sampler then
          state.Sampler <- sampler
          endAndRestart res &state gd

      | Command2D.SetScissor(x, y, w, h, _) ->
        flushBatches res gd
        state.HasScissor <- true
        state.ScissorRect <- Rectangle(x, y, w, h)
        gd.ScissorRectangle <- state.ScissorRect
        restartBatches res &state

      | Command2D.ClearScissor _ ->
        state.HasScissor <- false
        endAndRestart res &state gd

      | Command2D.SetLineWidth(width, _) -> pb.LineWidth <- width

      | Command2D.SetViewport(x, y, w, h, _) ->
        flushBatches res gd
        state.HasCustomViewport <- true
        gd.Viewport <- Viewport(x, y, w, h)
        state.Viewport <- gd.Viewport
        restartBatches res &state

      // Escape Hatches
      | Command2D.DrawImmediate(action, _) -> drawImmediate action &state res gd

      | Command2D.Clear(color, _) ->
        flushBatches res gd
        sb.GraphicsDevice.Clear(color)
        restartBatches res &state

      // Lighting
      | Command2D.NoopLight _ -> ()

      | Command2D.LitSprite(lightCtx, sprite) ->
        handleLitSprite lightCtx sprite &state res gd

      | Command2D.EndLighting(lightCtx, _) ->
        handleEndLighting lightCtx &state res gd

      | Command2D.EnableShadows(lightCtx, _) -> lightCtx.UniformsDirty <- true

      | Command2D.DisableShadows(lightCtx, _) -> lightCtx.UniformsDirty <- true
      // Particles
      | Command2D.Particle(texture, particles, count, _) ->
        let fullSrc = Rectangle(0, 0, texture.Width, texture.Height)

        for j = 0 to count - 1 do
          let p = particles[j]
          let halfW = p.Size.X * 0.5f
          let halfH = p.Size.Y * 0.5f

          let dst =
            Rectangle(
              int(p.Position.X - halfW),
              int(p.Position.Y - halfH),
              int p.Size.X,
              int p.Size.Y
            )

          let src =
            if p.SourceRect.Width > 0 && p.SourceRect.Height > 0 then
              p.SourceRect
            else
              fullSrc

          sb.Draw(
            texture,
            dst,
            Nullable src,
            p.Color,
            0.0f,
            Vector2.Zero,
            SpriteEffects.None,
            0.0f
          )

      // Post-process actions are drained after the scene renders; nothing to do here.
      | Command2D.PostProcess _ -> ()

/// <summary>
/// A deferred 2D renderer that sorts commands by layer and executes them
/// via pattern matching on <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/>.
/// </summary>
/// <remarks>
/// <para>
/// Commands are accumulated each frame via the <c>view</c> function into a
/// <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>, sorted by layer, then executed
/// in order through a MonoGame <c>SpriteBatch</c> paired with a <c>PrimitiveBatch</c>.
/// </para>
/// <para>
/// The renderer owns one <c>SpriteBatch</c> and one <c>PrimitiveBatch</c> (created lazily
/// from the <c>GraphicsDevice</c> registered in the <see cref="T:Mibo.Elmish.GameContext"/>).
/// State-transition commands (<c>BeginCamera</c>, <c>EndCamera</c>, <c>DrawImmediate</c>,
/// <c>BeginShader</c>, <c>BeginTarget</c>, <c>SetBlend</c>, <c>SetScissor</c>, etc.)
/// flush both batches and re-open them with updated settings.
/// </para>
/// <para>
/// Register via <c>Program.withRenderer</c>:
/// <code lang="fsharp">
/// Program.mkProgram init update
/// |> Program.withRenderer(fun () -> Renderer2D.create view)
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="Model">The application model type, passed to the view function.</typeparam>
type Renderer2D<'Model>
  (
    view: GameContext -> 'Model -> RenderBuffer2D -> unit,
    config: Renderer2DConfig
  ) =

  let buffer = new RenderBuffer2D(capacity = 4096)

  let mutable _spriteBatch: SpriteBatch voption = ValueNone
  let mutable _primitiveBatch: PrimitiveBatch voption = ValueNone
  let mutable _whitePixel: Texture2D voption = ValueNone
  let mutable _rtPool: IRenderTargetPool voption = ValueNone
  // Created against the device on the first post-process frame.
  let mutable _fullScreenQuad: Mibo.Elmish.Graphics3D.FullScreenQuad voption =
    ValueNone

  // Per-instance lit-sprite accumulator (CommandHandlers.LitBatchState).
  // Instance-scoped so stacked Renderer2D instances don't clobber each other's
  // in-progress batch. Initialized against the device on first Draw.
  let mutable _litBatch: CommandHandlers.LitBatchState =
    CommandHandlers.litBatchInit()

  let mutable _camera: Camera2D voption = ValueNone
  let mutable _windowWidth = 0
  let mutable _windowHeight = 0

  let createWhitePixel(gd: GraphicsDevice) =
    let tex = new Texture2D(gd, 1, 1)
    tex.SetData([| Color.White |])
    tex

  let ensureDevice(gd: GraphicsDevice) =
    match _spriteBatch with
    | ValueNone ->
      _spriteBatch <- ValueSome(new SpriteBatch(gd))
      _primitiveBatch <- ValueSome(new PrimitiveBatch(gd))
      _whitePixel <- ValueSome(createWhitePixel gd)
      _rtPool <- ValueSome(new RenderTargetPool(gd))
      _litBatch <- CommandHandlers.litBatchInit()
    | ValueSome _ -> ()

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, gameTime) =
      _windowWidth <- ctx.WindowWidth
      _windowHeight <- ctx.WindowHeight
      buffer.Clear()

      view ctx model buffer
      buffer.Sort()

      let gd = MonoGameGameContext.getGraphicsDevice ctx
      ensureDevice gd

      let sb = _spriteBatch.Value
      let pb = _primitiveBatch.Value

      let initialMatrix =
        match _camera with
        | ValueSome c -> Camera2D.toMatrix c
        | ValueNone -> Matrix.Identity

      CommandHandlers.beginSpriteBatch(
        sb,
        initialMatrix,
        BlendMode.NonPremultiplied,
        SamplerState.LinearClamp,
        CommandHandlers.defaultRasterizer,
        ValueNone
      )

      pb.Begin(initialMatrix)

      let mutable state: CommandHandlers.RendererState = {
        Camera = _camera
        Viewport = gd.Viewport
        HasCustomViewport = false
        HasScissor = false
        ScissorRect = Rectangle.Empty
        Blend = BlendMode.NonPremultiplied
        Sampler = SamplerState.LinearClamp
        Shader = ValueNone
        HasRenderTarget = false
        RenderTarget = ValueNone
        WindowWidth = _windowWidth
        WindowHeight = _windowHeight
      }

      let res: CommandHandlers.RenderResources = {
        SpriteBatch = sb
        PrimitiveBatch = pb
        WhitePixel = _whitePixel.Value
        Stack = []
        LitBatch = _litBatch
      }

      // Arm the lit accumulator with the initial transform/blend/rasterizer so
      // the first lit flush of the frame uses them (parallel to pb.Begin above).
      CommandHandlers.litBatchReset
        &_litBatch
        initialMatrix
        (CommandHandlers.toBlendState BlendMode.NonPremultiplied)
        CommandHandlers.defaultRasterizer

      // When the view emits no PostProcess commands, take the hot path (no scene RT,
      // no collection scan, no per-frame allocation). When present, collect them, render
      // the scene to a pooled RT, and ping-pong each action through pooled RTs (the last
      // draws to the back-buffer).
      if buffer.PostProcessCount = 0 then
        match config.ClearColor with
        | ValueSome c -> gd.Clear(c)
        | ValueNone -> ()

        // Always close both batches even if execute throws — otherwise a single
        // bad frame (e.g. a throwing DrawImmediate callback) leaves the batches
        // open and every subsequent Draw fails with "Begin called while already
        // in a batch".
        try
          CommandHandlers.execute(&state, buffer, res, gd)
        finally
          // LitBatchState is a struct held by value in res.LitBatch, so execute
          // mutated a copy that diverged from the instance's _litBatch field.
          // Copy back before the trailing flush so it drains the geometry
          // execute actually accumulated (and so next frame starts from the
          // right cursors/key).
          _litBatch <- res.LitBatch

          // Invariant: HasBatch=true means the SpriteBatch/PrimitiveBatch were
          // suspended (Ended) for the lit run (see handleLitSprite's entry flush).
          // If the frame ended mid-lit-run (no EndLighting before EndCamera),
          // they are still Ended. Drain the trailing lit geometry and re-open
          // both batches so the End() below is balanced — otherwise End() throws
          // "Begin must be called before calling End" on every frame that ends
          // inside a lighting block.
          CommandHandlers.flushLitRunAndReopen &_litBatch &state res gd
          sb.End()
          pb.End()
      else
        let ppActions =
          ResizeArray<PostProcessContext2D -> unit>(buffer.PostProcessCount)

        let mutable lightCtx: Lighting.LightContext2D voption = ValueNone

        for i = 0 to buffer.Count - 1 do
          match buffer[i] with
          | Command2D.PostProcess a -> ppActions.Add a
          | Command2D.LitSprite(ctx, _)
          | Command2D.EndLighting(ctx, _)
          | Command2D.EnableShadows(ctx, _)
          | Command2D.DisableShadows(ctx, _) ->
            if lightCtx.IsNone then
              lightCtx <- ValueSome ctx
          | _ -> ()

        let pool = _rtPool.Value
        let sceneRT = pool.Acquire(ctx.WindowWidth, ctx.WindowHeight)
        gd.SetRenderTarget(sceneRT)
        state.HasRenderTarget <- true
        state.RenderTarget <- ValueSome sceneRT

        match config.ClearColor with
        | ValueSome c -> gd.Clear(c)
        | ValueNone -> ()

        // Render the scene to the render target, then drain the post-process
        // actions. Wrapped in try/finally so pooled render targets are always
        // released (and the back-buffer restored) even if execute or a post-process
        // action throws — otherwise an exception leaks the sceneRT and any RTs
        // acquired by the drain forever, growing GPU memory each frame.
        let mutable sceneDone = false

        try
          CommandHandlers.execute(&state, buffer, res, gd)
          // See hot-path note: res.LitBatch is a struct copy; sync back before
          // the trailing flush so it sees the geometry execute accumulated.
          _litBatch <- res.LitBatch
          // Submit any trailing lit run into the scene RT before the batches
          // close, and re-open the batches if the frame ended inside a lighting
          // block so the End() calls below are balanced (see flushLitRunAndReopen).
          CommandHandlers.flushLitRunAndReopen &_litBatch &state res gd
          sb.End()
          pb.End()
          sceneDone <- true
          gd.SetRenderTarget(null)

          let quad =
            match _fullScreenQuad with
            | ValueSome q -> q
            | ValueNone ->
              let q = new Mibo.Elmish.Graphics3D.FullScreenQuad(gd)
              _fullScreenQuad <- ValueSome q
              q

          PostProcessDrain.apply
            ctx
            gd
            sceneRT
            lightCtx
            state.Camera
            pool
            quad
            ppActions
            (float32 gameTime.TotalTime.TotalSeconds)
        finally
          // If execute threw before the batches were ended, close them so the
          // renderer stays usable next frame (Begin guards against re-entrancy).
          if not sceneDone then
            // Sync the struct copy back (see hot-path note) so a half-drawn
            // frame's pending lit geometry is still flushed, and so the
            // accumulator doesn't carry stale cursors into the next frame.
            _litBatch <- res.LitBatch
            // Drain the lit run and re-open the batches if the run left them
            // suspended, so the End() calls below are balanced.
            CommandHandlers.flushLitRunAndReopen &_litBatch &state res gd
            sb.End()
            pb.End()

          // Always return to the back-buffer and release pooled targets.
          gd.SetRenderTarget(null)
          pool.ReleaseAll()

      _camera <- state.Camera

  interface IDisposable with
    member _.Dispose() =
      match _spriteBatch with
      | ValueSome sb -> sb.Dispose()
      | ValueNone -> ()

      match _primitiveBatch with
      | ValueSome pb -> (pb :> IDisposable).Dispose()
      | ValueNone -> ()

      match _whitePixel with
      | ValueSome t -> t.Dispose()
      | ValueNone -> ()

      match _rtPool with
      | ValueSome pool ->
        match pool with
        | :? IDisposable as d -> d.Dispose()
        | _ -> ()
      | ValueNone -> ()

      match _fullScreenQuad with
      | ValueSome q -> (q :> IDisposable).Dispose()
      | ValueNone -> ()

      (buffer :> IDisposable).Dispose()

/// <summary>Convenience constructors for <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/></summary>
module Renderer2D =

  /// <summary>
  /// Creates a renderer with default configuration (black clear color).
  /// </summary>
  /// <param name="view">
  /// The view function that populates the render buffer each frame.
  /// Receives the game context, current model, and a mutable buffer.
  /// </param>
  let create
    (view: GameContext -> 'Model -> RenderBuffer2D -> unit)
    : IRenderer<'Model> =
    new Renderer2D<'Model>(view, Renderer2DConfig.defaults) :> IRenderer<'Model>

  /// <summary>
  /// Creates a renderer with custom configuration.
  /// </summary>
  /// <param name="config">The renderer configuration.</param>
  /// <param name="view">
  /// The view function that populates the render buffer each frame.
  /// Receives the game context, current model, and a mutable buffer.
  /// </param>
  let createWith
    (config: Renderer2DConfig)
    (view: GameContext -> 'Model -> RenderBuffer2D -> unit)
    : IRenderer<'Model> =
    new Renderer2D<'Model>(view, config) :> IRenderer<'Model>
