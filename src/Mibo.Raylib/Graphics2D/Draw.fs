namespace Mibo.Elmish.Graphics2D

open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting

/// <summary>
/// Pipe-friendly drawing DSL. Each function takes a <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>
/// as its last argument, adds the corresponding command, and returns the buffer for chaining.
/// </summary>
/// <remarks>
/// <para>
/// Commands are built via <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/> and added to the buffer.
/// Partial application of styling parameters (layer, color, thickness) is supported — bind
/// them once and reuse across multiple draw calls.
/// </para>
/// <para>
/// Usage:
/// <code lang="fsharp">
/// buffer
/// |> Draw.beginCamera 0&lt;RenderLayer&gt; worldCamera
/// |> Draw.fillRect (10&lt;RenderLayer&gt;, Color.Red) groundRect
/// |> Draw.fillCircle (10&lt;RenderLayer&gt;, Color.Blue) (center, radius)
/// |> Draw.line (5&lt;RenderLayer&gt;, Color.Green) (p1, p2)
/// |> Draw.endCamera 1000&lt;RenderLayer&gt;
/// |> Draw.drop
/// </code>
/// </para>
/// </remarks>
module Draw =

  // ──────────────────────────────────────────────
  // Sprite & Text
  // ──────────────────────────────────────────────

  /// <summary>Draws a sprite from a pre-configured SpriteState.</summary>
  let inline sprite (state: SpriteState) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.sprite state)
    buffer

  /// <summary>Draws text from a pre-configured TextState.</summary>
  let inline text (state: Command2D.TextState) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.text state)
    buffer

  // ──────────────────────────────────────────────
  // Rectangles
  // ──────────────────────────────────────────────

  /// <summary>Filled rectangle. (layer, color) can be partially applied.</summary>
  let inline fillRect
    (layer: int<RenderLayer>, color: Color)
    (rect: Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.fillRect (layer, color) rect)
    buffer

  /// <summary>Rectangle outline with thickness. (layer, color, thickness) can be partially applied.</summary>
  let inline rectOutline
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (rect: Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.rectOutline (layer, color, thickness) rect)
    buffer

  /// <summary>Filled rounded rectangle. (layer, color, roundness, segments) can be partially applied.</summary>
  let inline fillRectRounded
    (layer: int<RenderLayer>, color: Color, roundness: float32, segments: int)
    (rect: Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.fillRectRounded (layer, color, roundness, segments) rect
    )

    buffer

  /// <summary>Rounded rectangle outline with thickness. (layer, color, roundness, segments, thickness) can be partially applied.</summary>
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
      Command2D.rectRoundedOutline
        (layer, color, roundness, segments, thickness)
        rect
    )

    buffer

  /// <summary>Vertical gradient rectangle. (layer) can be partially applied.</summary>
  let inline rectGradientV
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int, top: Color, bottom: Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.rectGradientV layer (x, y, w, h, top, bottom))
    buffer

  /// <summary>Horizontal gradient rectangle. (layer) can be partially applied.</summary>
  let inline rectGradientH
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int, left: Color, right: Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.rectGradientH layer (x, y, w, h, left, right))
    buffer

  /// <summary>4-corner gradient rectangle. (layer) can be partially applied.</summary>
  let inline rectGradient
    (layer: int<RenderLayer>)
    (rect: Rectangle, tl: Color, bl: Color, tr: Color, br: Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.rectGradient layer (rect, tl, bl, tr, br))
    buffer

  // ──────────────────────────────────────────────
  // Circles & Ellipses
  // ──────────────────────────────────────────────

  /// <summary>Filled circle. (layer, color) can be partially applied.</summary>
  let inline fillCircle
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.fillCircle (layer, color) (center, radius))
    buffer

  /// <summary>Circle outline. (layer, color) can be partially applied.</summary>
  let inline circleOutline
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.circleOutline (layer, color) (center, radius))
    buffer

  /// <summary>Filled circle sector (pie slice). (layer, color) can be partially applied.</summary>
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
      Command2D.circleSector
        (layer, color)
        (center, radius, startAngle, endAngle, segments)
    )

    buffer

  /// <summary>Circle sector outline. (layer, color) can be partially applied.</summary>
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
      Command2D.circleSectorOutline
        (layer, color)
        (center, radius, startAngle, endAngle, segments)
    )

    buffer

  /// <summary>Gradient circle. (layer) can be partially applied.</summary>
  let inline circleGradient
    (layer: int<RenderLayer>)
    (centerX: int, centerY: int, radius: float32, inner: Color, outer: Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.circleGradient layer (centerX, centerY, radius, inner, outer)
    )

    buffer

  /// <summary>Filled ring / arc. (layer, color) can be partially applied.</summary>
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
      Command2D.fillRing
        (layer, color)
        (center, innerR, outerR, startAngle, endAngle, segments)
    )

    buffer

  /// <summary>Ring / arc outline. (layer, color) can be partially applied.</summary>
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
      Command2D.ringOutline
        (layer, color)
        (center, innerR, outerR, startAngle, endAngle, segments)
    )

    buffer

  /// <summary>Filled ellipse. (layer, color) can be partially applied.</summary>
  let inline fillEllipse
    (layer: int<RenderLayer>, color: Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.fillEllipse (layer, color) (centerX, centerY, radiusH, radiusV)
    )

    buffer

  /// <summary>Ellipse outline. (layer, color) can be partially applied.</summary>
  let inline ellipseOutline
    (layer: int<RenderLayer>, color: Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.ellipseOutline
        (layer, color)
        (centerX, centerY, radiusH, radiusV)
    )

    buffer

  // ──────────────────────────────────────────────
  // Lines & Curves
  // ──────────────────────────────────────────────

  /// <summary>1-pixel line. (layer, color) can be partially applied.</summary>
  let inline line
    (layer: int<RenderLayer>, color: Color)
    (start: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.line (layer, color) (start, finish))
    buffer

  /// <summary>Line with custom thickness. (layer, color, thickness) can be partially applied.</summary>
  let inline lineThick
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.lineThick (layer, color, thickness) (start, finish))
    buffer

  /// <summary>Connected line segments. (layer, color) can be partially applied.</summary>
  let inline lineStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.lineStrip (layer, color) points)
    buffer

  /// <summary>Quadratic bezier curve. (layer, color, thickness) can be partially applied.</summary>
  let inline bezier
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, control: Vector2, finish: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.bezier (layer, color, thickness) (start, control, finish)
    )

    buffer

  // ──────────────────────────────────────────────
  // Triangles & Polygons
  // ──────────────────────────────────────────────

  /// <summary>Filled triangle from 3 vertices. (layer, color) can be partially applied.</summary>
  let inline triangle
    (layer: int<RenderLayer>, color: Color)
    (v1: Vector2, v2: Vector2, v3: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.triangle (layer, color) (v1, v2, v3))
    buffer

  /// <summary>Filled triangle fan. (layer, color) can be partially applied.</summary>
  let inline triangleFan
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.triangleFan (layer, color) points)
    buffer

  /// <summary>Filled triangle strip. (layer, color) can be partially applied.</summary>
  let inline triangleStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.triangleStrip (layer, color) points)
    buffer

  /// <summary>Filled regular polygon. (layer, color) can be partially applied.</summary>
  let inline fillPoly
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.fillPoly (layer, color) (center, sides, radius, rotation)
    )

    buffer

  /// <summary>Regular polygon outline with thickness. (layer, color, thickness) can be partially applied.</summary>
  let inline polyOutline
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.polyOutline
        (layer, color, thickness)
        (center, sides, radius, rotation)
    )

    buffer

  // ──────────────────────────────────────────────
  // Camera, Shader, Target
  // ──────────────────────────────────────────────

  /// <summary>Begins a 2D camera transform. (layer) can be partially applied.</summary>
  let inline beginCamera
    (layer: int<RenderLayer>)
    (camera: Camera2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.beginCamera layer camera)
    buffer

  /// <summary>Begins a 2D camera with explicit rendering config (viewport, clear). (layer) can be partially applied.</summary>
  let inline beginCameraWith
    (layer: int<RenderLayer>)
    (config: Camera2DConfig)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.beginCameraConfig layer config)
    buffer

  /// <summary>Ends the current 2D camera transform.</summary>
  let inline endCamera (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.endCamera layer)
    buffer

  /// <summary>Begins a shader mode. (layer) can be partially applied.</summary>
  let inline beginShader
    (layer: int<RenderLayer>)
    (shader: Shader)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.beginShader layer shader)
    buffer

  /// <summary>Ends the current shader mode.</summary>
  let inline endShader (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.endShader layer)
    buffer

  /// <summary>Begins rendering to a render texture. (layer) can be partially applied.</summary>
  let inline beginTarget
    (layer: int<RenderLayer>)
    (target: RenderTexture2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.beginTarget layer target)
    buffer

  /// <summary>Ends rendering to a render texture.</summary>
  let inline endTarget (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.endTarget layer)
    buffer

  // ──────────────────────────────────────────────
  // Render State
  // ──────────────────────────────────────────────

  /// <summary>Sets the blending mode. (layer) can be partially applied.</summary>
  let inline setBlend
    (layer: int<RenderLayer>)
    (mode: BlendMode)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.setBlend layer mode)
    buffer

  /// <summary>Enables scissor testing. (layer) can be partially applied.</summary>
  let inline setScissor
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.setScissor layer (x, y, w, h))
    buffer

  /// <summary>Disables scissor testing.</summary>
  let inline clearScissor (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.clearScissor layer)
    buffer

  /// <summary>Sets the line width for subsequent line draws. (layer) can be partially applied.</summary>
  let inline setLineWidth
    (layer: int<RenderLayer>)
    (width: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.setLineWidth layer width)
    buffer

  /// <summary>Sets the viewport rectangle. (layer) can be partially applied.</summary>
  let inline setViewport
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.setViewport layer (x, y, w, h))
    buffer

  // ──────────────────────────────────────────────
  // Escape Hatches
  // ──────────────────────────────────────────────

  /// <summary>Flushes raylib's batch, exits camera/shader, runs action, restores state. (layer) can be partially applied.</summary>
  let inline drawImmediate
    (layer: int<RenderLayer>)
    (action: unit -> unit)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.drawImmediate layer action)
    buffer

  /// <summary>Clears the current framebuffer to the given color.</summary>
  let inline clear
    (layer: int<RenderLayer>)
    (color: Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.clear layer color)
    buffer

  // ──────────────────────────────────────────────
  // Shadow Control
  // ──────────────────────────────────────────────

  /// <summary>Enables shadow casting for the given light context. (layer) can be partially applied.</summary>
  let inline enableShadows
    (layer: int<RenderLayer>)
    (lightCtx: LightContext2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.enableShadows layer lightCtx)
    buffer

  /// <summary>Disables shadow casting for the given light context. (layer) can be partially applied.</summary>
  let inline disableShadows
    (layer: int<RenderLayer>)
    (lightCtx: LightContext2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.disableShadows layer lightCtx)
    buffer

  /// <summary>Terminal function that discards the buffer, silencing the unused-value warning. Does nothing.</summary>
  let inline drop(_buffer: RenderBuffer2D) = ()
