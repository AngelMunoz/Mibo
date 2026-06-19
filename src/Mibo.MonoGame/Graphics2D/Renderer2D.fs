namespace Mibo.Elmish.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting

/// <summary>A single post-processing pass applied to the rendered scene.</summary>
[<Struct>]
type PostProcessPass = {

  /// <summary>Effect used for this pass. Receives the scene/render-target as the active texture.</summary>
  Effect: Effect

  /// <summary>
  /// Optional callback to set effect parameters before rendering the fullscreen quad.
  /// Called once per frame when this pass executes. The <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>
  /// technique pass is already applied when this callback runs.
  /// </summary>
  OnSetup: (Effect -> GameContext -> unit) voption
}

/// <summary>Post-processing chain for 2D rendering.</summary>
module PostProcess2D =

  /// <summary>
  /// Applies a chain of post-processing passes via ping-pong render targets.
  /// The scene is already rendered to <paramref name="sceneTarget"/>. Each pass
  /// renders to a pooled RT (except the last, which renders to the backbuffer).
  /// </summary>
  let apply
    (
      ctx: GameContext,
      sceneTarget: RenderTarget2D,
      passes: PostProcessPass[],
      rtPool: IRenderTargetPool,
      spriteBatch: SpriteBatch
    ) =
    let mutable src: Texture2D = sceneTarget
    let w = ctx.WindowWidth
    let h = ctx.WindowHeight

    for i = 0 to passes.Length - 1 do
      let pass = passes[i]
      let isLast = i = passes.Length - 1

      let dst: RenderTarget2D voption =
        if isLast then
          ValueNone
        else
          ValueSome(rtPool.Acquire(w, h))

      let gd = sceneTarget.GraphicsDevice

      match dst with
      | ValueSome target ->
        gd.SetRenderTarget(target)
        gd.Clear(Color.Black)
      | ValueNone -> gd.SetRenderTarget(null)

      match pass.OnSetup with
      | ValueSome f -> f pass.Effect ctx
      | ValueNone -> ()

      // Apply every pass in the technique (multi-pass effects are supported).
      // Use SpriteSortMode.Immediate so each Draw is flushed with the effect's
      // current technique/pass applied; SpriteBatch re-applies the active effect
      // on each Draw in Immediate mode, so iterating the passes here drives the
      // effect's multi-pass logic correctly. Guard against empty techniques.
      let techniquePasses = pass.Effect.CurrentTechnique.Passes

      if techniquePasses.Count > 0 then
        let srcRect = Rectangle(0, 0, w, h)
        let destRect = Rectangle(0, 0, w, h)

        // MonoGame stores RenderTarget2D data upside-down relative to normal
        // textures and the back buffer, so copying RT -> RT/back buffer via
        // SpriteBatch requires FlipVertically. This is consistent across the
        // DirectX and OpenGL backends (SpriteBatch normalizes orientation),
        // so the flip is unconditional here on purpose.
        spriteBatch.Begin(
          SpriteSortMode.Immediate,
          BlendState.Opaque,
          SamplerState.LinearClamp,
          DepthStencilState.None,
          RasterizerState.CullNone,
          pass.Effect
        )

        for pIdx = 0 to techniquePasses.Count - 1 do
          techniquePasses.[pIdx].Apply()

          spriteBatch.Draw(
            src,
            destRect,
            srcRect,
            Color.White,
            0.0f,
            Vector2.Zero,
            SpriteEffects.FlipVertically,
            0.0f
          )

        spriteBatch.End()

      match dst with
      | ValueSome target -> src <- target
      | ValueNone -> ()

/// <summary>Configuration for the <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/></summary>
[<Struct>]
type Renderer2DConfig = {

  /// <summary>
  /// Optional post-processing passes. Applied in order after the scene is rendered
  /// to a render target, chaining via pooled render targets between passes.
  /// The last pass renders directly to the backbuffer.
  /// </summary>
  PostProcess: PostProcessPass[] voption

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
  /// Default configuration: no post-processing, black clear color.
  /// Suitable for most 2D games that don't need screen-space effects.
  /// </summary>
  let defaults: Renderer2DConfig = {
    PostProcess = ValueNone
    ClearColor = ValueSome Color.Black
  }

  /// <summary>
  /// Configuration that skips clearing the background.
  /// Use when this renderer composites on top of another renderer's output.
  /// </summary>
  let noClear: Renderer2DConfig = {
    PostProcess = ValueNone
    ClearColor = ValueNone
  }

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
    mutable Shader: Effect voption
    mutable HasRenderTarget: bool
    mutable RenderTarget: RenderTarget2D voption
    WindowWidth: int
    WindowHeight: int
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
    /// Per-renderer scratch buffer for the lit-sprite quad draw. Kept on the
    /// resources struct (rather than a module-level mutable) so layered/stacked
    /// Renderer2D instances don't clobber each other's in-progress quad.
    QuadVerts: VertexPositionColorTexture[]
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
      rasterizer: RasterizerState,
      effect: Effect voption
    ) =
    sb.Begin(
      SpriteSortMode.Deferred,
      toBlendState blend,
      SamplerState.LinearClamp,
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

  let inline private flushBatches(res: RenderResources) =
    res.SpriteBatch.End()
    res.PrimitiveBatch.Flush()

  let inline private restartBatches
    (res: RenderResources)
    (state: byref<RendererState>)
    =
    let matrix = currentMatrix &state
    let raster = currentRasterizer &state
    beginSpriteBatch(res.SpriteBatch, matrix, state.Blend, raster, state.Shader)
    res.PrimitiveBatch.SetTransform(matrix)
    res.PrimitiveBatch.SetBlendState(toBlendState state.Blend)
    res.PrimitiveBatch.SetRasterizerState(raster)
    res.PrimitiveBatch.SetEffect(state.Shader)

  let inline private endAndRestart
    (res: RenderResources)
    (state: byref<RendererState>)
    =
    flushBatches res
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
    endAndRestart res &state

  let private beginCameraConfig
    (config: Camera2DConfig)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    flushBatches res
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
    flushBatches res
    popFrame gd res &state
    restartBatches res &state

  // ── Escape hatch ──────────────────────────────────────────────

  let private drawImmediate
    (action: unit -> unit)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    flushBatches res
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
      // Outer rect
      let outer = [| tl; tr; br; bl; tl |]
      let x0i = float32 rect.X + half
      let y0i = float32 rect.Y + half
      let x1i = x0i + float32 rect.Width - thickness
      let y1i = y0i + float32 rect.Height - thickness
      let tli = Vector2(x0i, y0i)
      let tri = Vector2(x1i, y0i)
      let bri = Vector2(x1i, y1i)
      let bli = Vector2(x0i, y1i)
      // Inner rect (reversed winding so the whole outline is one continuous strip)
      let inner = [| tli; bli; bri; tri; tli |]
      pb.AddTriangleStrip(Array.append outer inner, color)

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
  // Bypasses SpriteBatch: draws the sprite directly with the lit Effect
  // and DrawUserPrimitives so the shader gets world-position + texture
  // + normal-map binding. Mirrors raylib's handleLitSprite.

  let private handleLitSprite
    (lightCtx: LightContext2D)
    (sprite: SpriteState)
    (state: byref<RendererState>)
    (res: RenderResources)
    (gd: GraphicsDevice)
    =
    // 1. Flush both batches — lit sprite draws outside the batch pipeline
    flushBatches res

    // 2. Select effect (plain vs normal-map)
    let effect =
      match sprite.NormalMap with
      | ValueSome _ -> lightCtx.NormalMapEffect
      | ValueNone -> lightCtx.Effect

    lightCtx.ShaderActive <- true

    // 3. Lazy uniform upload (once per frame on first lit sprite)
    if lightCtx.UniformsDirty then
      lightCtx.UploadUniforms()
      lightCtx.UniformsDirty <- false

    lightCtx.EnsureLocationsCached()

    // 4. Set MatrixTransform = projection * view (camera)
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
    let matrixTransform = projection * view

    let param = effect.Parameters["MatrixTransform"]

    if param <> null then
      param.SetValue(matrixTransform)

    // 5. Set texture and normal map
    let texParam = effect.Parameters["Texture"]

    if texParam <> null then
      texParam.SetValue(sprite.Texture)

    match sprite.NormalMap with
    | ValueSome nm ->
      let nmParam = lightCtx.NormalMapParameter

      if nmParam <> null then
        nmParam.SetValue(nm)
    | ValueNone -> ()

    // 6. Build quad vertices (two triangles) in screen space
    let dest = sprite.Dest
    let src = sprite.Source
    let origin = sprite.Origin
    let rotation = sprite.Rotation
    let color = sprite.Color

    // Compute UVs from source rect (normalized to texture size)
    let texW = float32 sprite.Texture.Width
    let texH = float32 sprite.Texture.Height
    let u0 = float32 src.X / texW
    let v0 = float32 src.Y / texH
    let u1 = float32(src.X + src.Width) / texW
    let v1 = float32(src.Y + src.Height) / texH

    // Compute 4 corners with origin/rotation applied
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

    // Per-renderer scratch quad buffer (res.QuadVerts) — avoids per-draw heap
    // allocation in the hot path (AGENTS.md: avoid allocations in hot paths)
    // while keeping each Renderer2D instance isolated.
    let quadVerts = res.QuadVerts

    quadVerts[0] <-
      VertexPositionColorTexture(
        Vector3(tl.X, tl.Y, 0.0f),
        color,
        Vector2(u0, v0)
      )

    quadVerts[1] <-
      VertexPositionColorTexture(
        Vector3(tr.X, tr.Y, 0.0f),
        color,
        Vector2(u1, v0)
      )

    quadVerts[2] <-
      VertexPositionColorTexture(
        Vector3(br.X, br.Y, 0.0f),
        color,
        Vector2(u1, v1)
      )

    quadVerts[3] <-
      VertexPositionColorTexture(
        Vector3(tl.X, tl.Y, 0.0f),
        color,
        Vector2(u0, v0)
      )

    quadVerts[4] <-
      VertexPositionColorTexture(
        Vector3(br.X, br.Y, 0.0f),
        color,
        Vector2(u1, v1)
      )

    quadVerts[5] <-
      VertexPositionColorTexture(
        Vector3(bl.X, bl.Y, 0.0f),
        color,
        Vector2(u0, v1)
      )

    // 7. Draw with the lit effect
    let prevBlend = gd.BlendState
    let prevDepth = gd.DepthStencilState
    let prevRaster = gd.RasterizerState

    gd.BlendState <- toBlendState state.Blend
    gd.DepthStencilState <- DepthStencilState.None
    gd.RasterizerState <- currentRasterizer &state

    for pass in effect.CurrentTechnique.Passes do
      pass.Apply()

      gd.DrawUserPrimitives(PrimitiveType.TriangleList, quadVerts, 0, 2)
      |> ignore

    gd.BlendState <- prevBlend
    gd.DepthStencilState <- prevDepth
    gd.RasterizerState <- prevRaster

    // 8. Re-begin both batches for subsequent non-lit commands
    restartBatches res &state

  let private handleEndLighting
    (lightCtx: LightContext2D)
    (state: byref<RendererState>)
    (res: RenderResources)
    =
    if lightCtx.ShaderActive then
      lightCtx.ShaderActive <- false
      lightCtx.UniformsDirty <- true

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
      match buffer[i] with
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
        endAndRestart res &state

      | Command2D.EndShader _ ->
        flushBatches res
        popFrame gd res &state
        restartBatches res &state

      // Render Targets
      | Command2D.BeginTarget(target, _) ->
        pushFrame res &state
        state.HasRenderTarget <- true
        state.RenderTarget <- ValueSome target
        flushBatches res
        gd.SetRenderTarget(target)
        restartBatches res &state

      | Command2D.EndTarget _ ->
        flushBatches res
        popFrame gd res &state
        restartBatches res &state

      // Render State
      | Command2D.SetBlend(mode, _) ->
        state.Blend <- mode
        endAndRestart res &state

      | Command2D.SetScissor(x, y, w, h, _) ->
        flushBatches res
        state.HasScissor <- true
        state.ScissorRect <- Rectangle(x, y, w, h)
        gd.ScissorRectangle <- state.ScissorRect
        restartBatches res &state

      | Command2D.ClearScissor _ ->
        state.HasScissor <- false
        endAndRestart res &state

      | Command2D.SetLineWidth(width, _) -> pb.LineWidth <- width

      | Command2D.SetViewport(x, y, w, h, _) ->
        flushBatches res
        state.HasCustomViewport <- true
        gd.Viewport <- Viewport(x, y, w, h)
        state.Viewport <- gd.Viewport
        restartBatches res &state

      // Escape Hatches
      | Command2D.DrawImmediate(action, _) -> drawImmediate action &state res gd

      | Command2D.Clear(color, _) ->
        flushBatches res
        sb.GraphicsDevice.Clear(color)
        restartBatches res &state

      // Lighting
      | Command2D.NoopLight _ -> ()

      | Command2D.LitSprite(lightCtx, sprite) ->
        handleLitSprite lightCtx sprite &state res gd

      | Command2D.EndLighting(lightCtx, _) ->
        handleEndLighting lightCtx &state res

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

  // Per-instance lit-sprite quad scratch buffer (two triangles = 6 verts).
  // Instance-scoped so stacked Renderer2D instances don't clobber each other.
  let _quadVerts = Array.zeroCreate<VertexPositionColorTexture> 6

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
    | ValueSome _ -> ()

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, _gameTime) =
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
        QuadVerts = _quadVerts
      }

      match config.PostProcess with
      | ValueNone ->
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
          sb.End()
          pb.End()
      | ValueSome passes ->
        let pool = _rtPool.Value
        let sceneRT = pool.Acquire(ctx.WindowWidth, ctx.WindowHeight)
        gd.SetRenderTarget(sceneRT)
        state.HasRenderTarget <- true
        state.RenderTarget <- ValueSome sceneRT

        match config.ClearColor with
        | ValueSome c -> gd.Clear(c)
        | ValueNone -> ()

        // Render the scene to the render target, then run post-processing.
        // Wrapped in try/finally so pooled render targets are always released
        // (and the back-buffer restored) even if execute or a post-process pass
        // throws — otherwise an exception leaks the sceneRT and any RTs acquired
        // by PostProcess2D.apply forever, growing GPU memory each frame.
        let mutable sceneDone = false

        try
          CommandHandlers.execute(&state, buffer, res, gd)
          sb.End()
          pb.End()
          sceneDone <- true
          gd.SetRenderTarget(null)
          PostProcess2D.apply(ctx, sceneRT, passes, pool, sb)
        finally
          // If execute threw before the batches were ended, close them so the
          // renderer stays usable next frame (Begin guards against re-entrancy).
          if not sceneDone then
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
