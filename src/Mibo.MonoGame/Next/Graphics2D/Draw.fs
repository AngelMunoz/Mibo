namespace Mibo.Elmish.Next.Graphics2D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Next

module Draw =

  let inline sprite (state: SpriteState) (buffer: RenderBuffer2D) =
    let hTex = buffer.Textures.Register state.Texture

    buffer.Add(
      Command2D.Sprite(
        hTex,
        Convert.toRect state.Dest,
        Convert.toRect state.Source,
        Convert.toSysVec2 state.Origin,
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
        Convert.toSysVec2 state.Position,
        state.FontSize,
        state.Spacing,
        Convert.toColor state.Color,
        state.Layer
      )
    )

    buffer

  let inline fillRect
    (layer: int<RenderLayer>, color: Color)
    (rect: Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillRect(Convert.toRect rect, Convert.toColor color, layer)
    )

    buffer

  let inline rectOutline
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (rect: Rectangle)
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
    (layer: int<RenderLayer>, color: Color, roundness: float32, segments: int)
    (rect: Rectangle)
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
      color: Color,
      roundness: float32,
      segments: int,
      thickness: float32
    )
    (rect: Rectangle)
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
    (x: int, y: int, w: int, h: int, top: Color, bottom: Color)
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
    (x: int, y: int, w: int, h: int, left: Color, right: Color)
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
    (rect: Rectangle, tl: Color, bl: Color, tr: Color, br: Color)
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

  let inline fillCircle
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillCircle(
        Convert.toSysVec2 center,
        radius,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline circleOutline
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.CircleOutline(
        Convert.toSysVec2 center,
        radius,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline circleSector
    (layer: int<RenderLayer>, color: Color)
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
        Convert.toSysVec2 center,
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
    (layer: int<RenderLayer>, color: Color)
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
        Convert.toSysVec2 center,
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
    (centerX: int, centerY: int, radius: float32, inner: Color, outer: Color)
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
    (layer: int<RenderLayer>, color: Color)
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
        Convert.toSysVec2 center,
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
    (layer: int<RenderLayer>, color: Color)
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
        Convert.toSysVec2 center,
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
    (layer: int<RenderLayer>, color: Color)
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
    (layer: int<RenderLayer>, color: Color)
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

  let inline line
    (layer: int<RenderLayer>, color: Color)
    (start: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.Line(
        Convert.toSysVec2 start,
        Convert.toSysVec2 finish,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline lineThick
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.LineThick(
        Convert.toSysVec2 start,
        Convert.toSysVec2 finish,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  // NOTE: lineStrip/triangleFan/triangleStrip allocate a new Vector2[] per call
  // due to XNA→System.Numerics conversion. Inherent to the type mismatch.
  // Accept System.Numerics.Vector2[] directly to avoid, or pool the buffer.

  let inline lineStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =

    buffer.Add(
      Command2D.LineStrip(
        [| for v in points -> Convert.toSysVec2 v |],
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline bezier
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, control: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.Bezier(
        Convert.toSysVec2 start,
        Convert.toSysVec2 control,
        Convert.toSysVec2 finish,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline triangle
    (layer: int<RenderLayer>, color: Color)
    (v1: Vector2, v2: Vector2, v3: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.Triangle(
        Convert.toSysVec2 v1,
        Convert.toSysVec2 v2,
        Convert.toSysVec2 v3,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline triangleFan
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.TriangleFan(
        [| for v in points -> Convert.toSysVec2 v |],
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline triangleStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.TriangleStrip(
        [| for v in points -> Convert.toSysVec2 v |],
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline fillPoly
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.FillPoly(
        Convert.toSysVec2 center,
        sides,
        radius,
        rotation,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline polyOutline
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.PolyOutline(
        Convert.toSysVec2 center,
        sides,
        radius,
        rotation,
        thickness,
        Convert.toColor color,
        layer
      )
    )

    buffer

  let inline beginCamera
    (layer: int<RenderLayer>)
    (camera: Camera2DState)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.BeginCamera(camera, layer))
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
    (effect: Effect)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.BeginShader(buffer.Shaders.Register effect, layer))
    buffer

  let inline endShader (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.EndShader(layer))
    buffer

  let inline beginTarget
    (layer: int<RenderLayer>)
    (target: RenderTarget2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.BeginTarget(buffer.RenderTargets.Register target, layer)
    )

    buffer

  let inline endTarget (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.EndTarget(layer))
    buffer

  let inline setBlend
    (layer: int<RenderLayer>)
    (mode: BlendState)
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

  let inline drawImmediate
    (layer: int<RenderLayer>)
    (action: unit -> unit)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.DrawImmediate(action, layer))
    buffer

  let inline clear
    (layer: int<RenderLayer>)
    (color: Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.Clear(Convert.toColor color, layer))
    buffer

  let inline drop(_buffer: RenderBuffer2D) = ()
