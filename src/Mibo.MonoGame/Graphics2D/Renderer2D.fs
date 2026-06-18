namespace Mibo.Elmish.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

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
  /// Default configuration: black clear color.
  /// Suitable for most 2D games that don't need screen-space effects.
  /// </summary>
  let defaults: Renderer2DConfig = { ClearColor = ValueSome Color.Black }

  /// <summary>
  /// Configuration that skips clearing the background.
  /// Use when this renderer composites on top of another renderer's output.
  /// </summary>
  let noClear: Renderer2DConfig = { ClearColor = ValueNone }

// ═══════════════════════════════════════════════════════════════════
// Private command handlers — extracted from Renderer2D for readability
// ═══════════════════════════════════════════════════════════════════

module private CommandHandlers =

  /// <summary>Mutable renderer state threaded through command dispatch byref.</summary>
  [<Struct>]
  type RendererState = {
    mutable Camera: Camera2D voption
    WindowWidth: int
    WindowHeight: int
  }

  /// <summary>
  /// Backend resources the command handlers close over. The MonoGame analog
  /// of raylib's implicit global batch + primitives. Mirrors how the raylib
  /// handlers reach raylib's batch without it being passed in — here the
  /// SpriteBatch, PrimitiveBatch, and procedural textures are captured once
  /// per frame.
  /// </summary>
  [<Struct>]
  type RenderResources = {
    SpriteBatch: SpriteBatch
    PrimitiveBatch: PrimitiveBatch
    WhitePixel: Texture2D
  }

  // ── Batch lifecycle ─────────────────────────────────────────────
  // The SpriteBatch state (blend/sampler/depth/rasterizer) is renderer-internal:
  // userland only expresses intent via Draw.* commands, never framework state
  // objects. Consolidated here so there is a single source of truth. This is
  // the MonoGame analog of raylib's implicit batch defaults.

  let beginSpriteBatch(sb: SpriteBatch, matrix: Matrix) =
    sb.Begin(
      SpriteSortMode.Deferred,
      BlendState.NonPremultiplied,
      SamplerState.LinearClamp,
      DepthStencilState.None,
      RasterizerState.CullNone,
      null,
      matrix
    )

  let inline private currentMatrix(state: byref<RendererState>) : Matrix =
    match state.Camera with
    | ValueSome c -> Camera2D.toMatrix c
    | ValueNone -> Matrix.Identity

  let inline private flushBatches(res: RenderResources) =
    res.SpriteBatch.End()
    res.PrimitiveBatch.Flush()

  let inline private endAndRestart
    (res: RenderResources)
    (state: byref<RendererState>)
    (newCamera: Camera2D voption)
    =
    flushBatches res

    beginSpriteBatch(
      res.SpriteBatch,
      match newCamera with
      | ValueSome c -> Camera2D.toMatrix c
      | ValueNone -> Matrix.Identity
    )

    res.PrimitiveBatch.SetTransform(
      match newCamera with
      | ValueSome c -> Camera2D.toMatrix c
      | ValueNone -> Matrix.Identity
    )

  // ── Camera state management ─────────────────────────────────────
  // Analog of raylib's beginCamera/endCamera, which flush the active batch
  // (Rlgl.DrawRenderBatchActive) and re-enter BeginMode2D. Here we End the
  // SpriteBatch + PrimitiveBatch and re-Begin with the camera's transform matrix.

  let private beginCamera
    (c: Camera2D)
    (state: byref<RendererState>)
    (res: RenderResources)
    =
    endAndRestart res &state (ValueSome c)
    state.Camera <- ValueSome c

  let private endCamera(state: byref<RendererState>, res: RenderResources) =
    match state.Camera with
    | ValueSome _ ->
      endAndRestart res &state ValueNone
      state.Camera <- ValueNone
    | ValueNone -> ()

  // ── Escape hatch ────────────────────────────────────────────────
  // Analog of raylib's drawImmediate: flush the batches, exit camera,
  // run the action, then restore. MVP has no shader, so only camera is saved.

  let private drawImmediate
    (action: unit -> unit)
    (state: byref<RendererState>)
    (res: RenderResources)
    =
    flushBatches res
    let savedCam = state.Camera

    try
      action()
    finally
      beginSpriteBatch(
        res.SpriteBatch,
        match savedCam with
        | ValueSome c -> Camera2D.toMatrix c
        | ValueNone -> Matrix.Identity
      )

      res.PrimitiveBatch.SetTransform(
        match savedCam with
        | ValueSome c -> Camera2D.toMatrix c
        | ValueNone -> Matrix.Identity
      )

      state.Camera <- savedCam

  // ── Primitive tessellation helpers ──────────────────────────────
  // These mirror raylib's DrawCircleV / DrawRectangleLinesEx etc. by
  // decomposing high-level shapes into PrimitiveBatch calls.

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
    let points = Array.zeroCreate<Vector2>(segments + 3)
    points[0] <- center

    for i = 0 to segments + 1 do
      let angle = startRad + float32 i * step

      points[i + 1] <-
        Vector2(
          center.X + MathF.Cos(angle) * radius,
          center.Y + MathF.Sin(angle) * radius
        )

    pb.AddTriangleFan(points, color)

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
    let points = Array.zeroCreate<Vector2>((segments + 1) * 2 + 2)
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
    let total = quarter * 4
    let path = Array.zeroCreate<Vector2>(total + 1)

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
      pb.AddTriangleFan(path, color)

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

  // ── Main dispatch ───────────────────────────────────────────────

  let execute
    (state: byref<RendererState>, buffer: RenderBuffer2D, res: RenderResources)
    =
    let sb = res.SpriteBatch
    let pb = res.PrimitiveBatch

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      // Sprite & Text
      | Command2D.Sprite(texture, dest, source, origin, rotation, color, _) ->
        sb.Draw(
          texture,
          dest,
          Nullable source,
          color,
          rotation,
          origin,
          SpriteEffects.None,
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

      // Camera
      | Command2D.BeginCamera(camera, _) -> beginCamera camera &state res
      | Command2D.EndCamera _ -> endCamera(&state, res)

      // Escape Hatches
      | Command2D.DrawImmediate(action, _) -> drawImmediate action &state res
      | Command2D.Clear(color, _) -> sb.GraphicsDevice.Clear(color)

    state.Camera <- ValueNone

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
/// State-transition commands (<c>BeginCamera</c>, <c>EndCamera</c>, <c>DrawImmediate</c>)
/// flush both batches and re-open them with updated transform settings.
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
  let mutable _gd: GraphicsDevice voption = ValueNone

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
      _gd <- ValueSome gd
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

      match config.ClearColor with
      | ValueSome c -> gd.Clear(c)
      | ValueNone -> ()

      let sb = _spriteBatch.Value
      let pb = _primitiveBatch.Value

      let initialMatrix =
        match _camera with
        | ValueSome c -> Camera2D.toMatrix c
        | ValueNone -> Matrix.Identity

      CommandHandlers.beginSpriteBatch(sb, initialMatrix)
      pb.Begin(initialMatrix)

      let mutable state: CommandHandlers.RendererState = {
        Camera = _camera
        WindowWidth = _windowWidth
        WindowHeight = _windowHeight
      }

      let res: CommandHandlers.RenderResources = {
        SpriteBatch = sb
        PrimitiveBatch = pb
        WhitePixel = _whitePixel.Value
      }

      CommandHandlers.execute(&state, buffer, res)

      sb.End()
      pb.End()

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
