namespace Mibo.Elmish.Next.Graphics2D

open Mibo.Elmish.Next.Graphics2D.Base

open System.Numerics
open Mibo.Elmish.Next

// ─────────────────────────────────────────────────────────────────
// Draw DSL — pipe-friendly, buffer carries registries
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Pipe-friendly 2D drawing DSL.
/// Each function takes a <see cref="T:Mibo.Elmish.Next.Graphics2D.RenderBuffer2D"/>
/// as its last argument, adds the corresponding command, and returns the buffer.
/// </summary>
/// <remarks>
/// Users pass native raylib types (SpriteState, Color, Rectangle, Shader, etc.).
/// The DSL converts to neutral types and resolves resource handles via the
/// buffer's own registries — no global state, no partial application.
/// </remarks>
module Draw =

  // ── Sprite & Text ──────────────────────────────────────────────

  let inline sprite (state: SpriteState) (buffer: RenderBuffer2D) =
    let hTex = buffer.Textures.Register state.Texture

    buffer.Add(
      Command2D.Sprite(
        hTex,
        Convert.toRect state.Dest,
        Convert.toRect state.Source,
        state.Origin,
        state.Rotation,
        Convert.toColor state.Color,
        state.Layer
      )
    )

    buffer

  let inline text (state: TextState) (buffer: RenderBuffer2D) =
    let hFont = buffer.Fonts.Register state.Font

    buffer.Add(
      Command2D.Text(
        hFont,
        state.Text,
        state.Position,
        state.FontSize,
        state.Spacing,
        Convert.toColor state.Color,
        state.Layer
      )
    )

    buffer

  // ── Rectangles ─────────────────────────────────────────────────

  let inline fillRect
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (rect: Raylib_cs.Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillRect(Convert.toRect rect, Convert.toColor color, layer)
    )

    buffer

  let inline rectOutline
    (layer: int<RenderLayer>, color: Raylib_cs.Color, thickness: float32)
    (rect: Raylib_cs.Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.RectOutline(
        Convert.toRect rect,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline fillRectRounded
    (
      layer: int<RenderLayer>,
      color: Raylib_cs.Color,
      roundness: float32,
      segments: int
    )
    (rect: Raylib_cs.Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillRectRounded(
        Convert.toRect rect,
        roundness,
        segments,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline rectRoundedOutline
    (
      layer: int<RenderLayer>,
      color: Raylib_cs.Color,
      roundness: float32,
      segments: int,
      thickness: float32
    )
    (rect: Raylib_cs.Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.RectRoundedOutline(
        Convert.toRect rect,
        roundness,
        segments,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline rectGradientV
    (layer: int<RenderLayer>)
    (
      x: int,
      y: int,
      w: int,
      h: int,
      top: Raylib_cs.Color,
      bottom: Raylib_cs.Color
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.RectGradientV(
        x,
        y,
        w,
        h,
        Convert.toColor top,
        Convert.toColor bottom,
        layer
      )
    )

    buffer

  let inline rectGradientH
    (layer: int<RenderLayer>)
    (
      x: int,
      y: int,
      w: int,
      h: int,
      left: Raylib_cs.Color,
      right: Raylib_cs.Color
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.RectGradientH(
        x,
        y,
        w,
        h,
        Convert.toColor left,
        Convert.toColor right,
        layer
      )
    )

    buffer

  let inline rectGradient
    (layer: int<RenderLayer>)
    (
      rect: Raylib_cs.Rectangle,
      tl: Raylib_cs.Color,
      bl: Raylib_cs.Color,
      tr: Raylib_cs.Color,
      br: Raylib_cs.Color
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.RectGradient(
        Convert.toRect rect,
        Convert.toColor tl,
        Convert.toColor bl,
        Convert.toColor tr,
        Convert.toColor br,
        layer
      )
    )

    buffer

  // ── Circles & Ellipses ─────────────────────────────────────────

  let inline fillCircle
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (center: Vector2, radius: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillCircle(center, radius, Convert.toColor color, layer)
    )

    buffer

  let inline circleOutline
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (center: Vector2, radius: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.CircleOutline(center, radius, Convert.toColor color, layer)
    )

    buffer

  let inline circleSector
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      segments: int
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.CircleSector(
        center,
        radius,
        startAngle,
        endAngle,
        segments,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline circleSectorOutline
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      segments: int
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.CircleSectorOutline(
        center,
        radius,
        startAngle,
        endAngle,
        segments,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline circleGradient
    (layer: int<RenderLayer>)
    (
      centerX: int,
      centerY: int,
      radius: float32,
      inner: Raylib_cs.Color,
      outer: Raylib_cs.Color
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.CircleGradient(
        centerX,
        centerY,
        radius,
        Convert.toColor inner,
        Convert.toColor outer,
        layer
      )
    )

    buffer

  let inline fillRing
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (
      center: Vector2,
      innerR: float32,
      outerR: float32,
      startAngle: float32,
      endAngle: float32,
      segments: int
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillRing(
        center,
        innerR,
        outerR,
        startAngle,
        endAngle,
        segments,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline ringOutline
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (
      center: Vector2,
      innerR: float32,
      outerR: float32,
      startAngle: float32,
      endAngle: float32,
      segments: int
    )
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.RingOutline(
        center,
        innerR,
        outerR,
        startAngle,
        endAngle,
        segments,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline fillEllipse
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillEllipse(
        centerX,
        centerY,
        radiusH,
        radiusV,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline ellipseOutline
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.EllipseOutline(
        centerX,
        centerY,
        radiusH,
        radiusV,
        Convert.toColor color,
        layer
      )
    )

    buffer

  // ── Lines & Curves ─────────────────────────────────────────────

  let inline line
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (start: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.Line(start, finish, Convert.toColor color, layer))

    buffer

  let inline lineThick
    (layer: int<RenderLayer>, color: Raylib_cs.Color, thickness: float32)
    (start: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.LineThick(
        start,
        finish,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline lineStrip
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.LineStrip(points, Convert.toColor color, layer))

    buffer

  let inline bezier
    (layer: int<RenderLayer>, color: Raylib_cs.Color, thickness: float32)
    (start: Vector2, control: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.Bezier(
        start,
        control,
        finish,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  // ── Triangles & Polygons ───────────────────────────────────────

  let inline triangle
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (v1: Vector2, v2: Vector2, v3: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.Triangle(v1, v2, v3, Convert.toColor color, layer))

    buffer

  let inline triangleFan
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.TriangleFan(points, Convert.toColor color, layer))

    buffer

  let inline triangleStrip
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.TriangleStrip(points, Convert.toColor color, layer))

    buffer

  let inline fillPoly
    (layer: int<RenderLayer>, color: Raylib_cs.Color)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillPoly(
        center,
        sides,
        radius,
        rotation,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline polyOutline
    (layer: int<RenderLayer>, color: Raylib_cs.Color, thickness: float32)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.PolyOutline(
        center,
        sides,
        radius,
        rotation,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  // ── Camera, Shader, Target ─────────────────────────────────────

  let inline beginCamera
    (layer: int<RenderLayer>)
    (camera: Raylib_cs.Camera2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.BeginCamera(Convert.toCamera2DState camera, layer))

    buffer

  let inline beginCameraWith
    (layer: int<RenderLayer>)
    (config: Camera2DConfig)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.BeginCameraConfig(config, layer))
    buffer

  let inline endCamera (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.EndCamera(layer))
    buffer

  let inline beginShader
    (layer: int<RenderLayer>)
    (shader: Raylib_cs.Shader)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.BeginShader(buffer.Shaders.Register shader, layer))

    buffer

  let inline endShader (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.EndShader(layer))
    buffer

  let inline beginTarget
    (layer: int<RenderLayer>)
    (target: Raylib_cs.RenderTexture2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.BeginTarget(buffer.RenderTargets.Register target, layer)
    )

    buffer

  let inline endTarget (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.EndTarget(layer))
    buffer

  // ── Render State ───────────────────────────────────────────────

  let inline setBlend
    (layer: int<RenderLayer>)
    (mode: Raylib_cs.BlendMode)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.SetBlend(Convert.toBlendMode mode, layer))
    buffer

  let inline setScissor
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.SetScissor(x, y, w, h, layer))
    buffer

  let inline clearScissor (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.ClearScissor(layer))
    buffer

  let inline setLineWidth
    (layer: int<RenderLayer>)
    (width: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.SetLineWidth(width, layer))
    buffer

  let inline setViewport
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.SetViewport(x, y, w, h, layer))
    buffer

  // ── Escape Hatches ─────────────────────────────────────────────

  let inline drawImmediate
    (layer: int<RenderLayer>)
    (action: unit -> unit)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.DrawImmediate(action, layer))
    buffer

  let inline clear
    (layer: int<RenderLayer>)
    (color: Raylib_cs.Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.Clear(Convert.toColor color, layer))
    buffer

  // ── Shadow Control lives in LightDraw (mutates lightCtx) ────────

  // ── Terminal ───────────────────────────────────────────────────

  let inline drop(_buffer: RenderBuffer2D) = ()
