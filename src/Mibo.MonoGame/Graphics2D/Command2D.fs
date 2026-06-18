namespace Mibo.Elmish.Graphics2D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting

/// <summary>Unit of measure for 2D render layer ordering.</summary>
[<Measure>]
type RenderLayer

/// <summary>State required to render a 2D sprite via SpriteBatch.Draw.</summary>
[<Struct>]
type SpriteState = {
  /// <summary>The texture to draw.</summary>
  Texture: Texture2D

  /// <summary>Destination rectangle on screen (in pixels).</summary>
  Dest: Rectangle

  /// <summary>Source rectangle within the texture (in texels).</summary>
  Source: Rectangle

  /// <summary>Origin point for rotation and positioning (relative to Dest).</summary>
  Origin: Vector2

  /// <summary>Rotation in radians around Origin.</summary>
  Rotation: float32

  /// <summary>Tint color (multiplied with texture).</summary>
  Color: Color

  /// <summary>Render layer for ordering.</summary>
  Layer: int<RenderLayer>

  /// <summary>
  /// Optional normal map for per-pixel lighting. When <c>ValueSome</c>, the lit
  /// shader uses the normal-map variant; when <c>ValueNone</c>, the plain variant.
  /// </summary>
  NormalMap: Texture2D voption
}

/// <summary>State required to render 2D text via SpriteBatch.DrawString.</summary>
[<Struct>]
type TextState = {
  /// <summary>The sprite font to use.</summary>
  Font: SpriteFont

  /// <summary>The text string to draw.</summary>
  Text: string

  /// <summary>Top-left position on screen (in pixels).</summary>
  Position: Vector2

  /// <summary>Uniform scale factor applied to the font (1.0 = default size).</summary>
  Scale: float32

  /// <summary>Tint color.</summary>
  Color: Color

  /// <summary>Render layer for ordering.</summary>
  Layer: int<RenderLayer>
}

/// <summary>
/// Closed set of 2D render commands. Stored in <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>
/// and dispatched via pattern matching — no interface boxing.
/// </summary>
/// <remarks>
/// Each case carries a <c>layer: int&lt;RenderLayer&gt;</c> for stable layer sorting.
/// This is the MonoGame backend's equivalent of the raylib <c>Command2D</c> DU,
/// using MonoGame types (<c>Texture2D</c>, <c>SpriteFont</c>, <c>Rectangle</c>, etc.).
/// </remarks>
/// <summary>
/// MonoGame-native blend-mode abstraction. Maps to <see cref="T:Microsoft.Xna.Framework.Graphics.BlendState"/>.
/// </summary>
type BlendMode =
  | AlphaBlend
  | NonPremultiplied
  | Additive
  | Opaque

/// <summary>
/// Closed set of 2D render commands. Stored in <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>
/// and dispatched via pattern matching — no interface boxing.
/// </summary>
/// <remarks>
/// Each case carries a <c>layer: int&lt;RenderLayer&gt;</c> for stable layer sorting.
/// This is the MonoGame backend's equivalent of the raylib <c>Command2D</c> DU,
/// using MonoGame types (<c>Texture2D</c>, <c>SpriteFont</c>, <c>Rectangle</c>, etc.).
/// </remarks>
[<RequireQualifiedAccess; Struct>]
type Command2D =
  // Sprite & Text
  | Sprite of
    spriteTexture: Texture2D *
    spriteDest: Rectangle *
    spriteSource: Rectangle *
    spriteOrigin: Vector2 *
    spriteRotation: float32 *
    spriteColor: Color *
    layer: int<RenderLayer>
  | Text of
    textFont: SpriteFont *
    textValue: string *
    textPosition: Vector2 *
    textScale: float32 *
    textColor: Color *
    layer: int<RenderLayer>
  // Rectangles
  | FillRect of fillRect: Rectangle * fillColor: Color * layer: int<RenderLayer>
  | RectOutline of
    outlineRect: Rectangle *
    outlineThickness: float32 *
    outlineColor: Color *
    layer: int<RenderLayer>
  | FillRectRounded of
    roundedRect: Rectangle *
    roundedFillRoundness: float32 *
    roundedFillSegments: int *
    roundedFillColor: Color *
    layer: int<RenderLayer>
  | RectRoundedOutline of
    roundedOutlineRect: Rectangle *
    roundedOutlineRoundness: float32 *
    roundedOutlineSegments: int *
    roundedOutlineThickness: float32 *
    roundedOutlineColor: Color *
    layer: int<RenderLayer>
  | RectGradientV of
    gradVX: int *
    gradVY: int *
    gradVW: int *
    gradVH: int *
    gradVTop: Color *
    gradVBottom: Color *
    layer: int<RenderLayer>
  | RectGradientH of
    gradHX: int *
    gradHY: int *
    gradHW: int *
    gradHH: int *
    gradHLeft: Color *
    gradHRight: Color *
    layer: int<RenderLayer>
  | RectGradient of
    gradRect: Rectangle *
    gradTL: Color *
    gradBL: Color *
    gradTR: Color *
    gradBR: Color *
    layer: int<RenderLayer>
  // Circles & Ellipses
  | FillCircle of
    circleCenter: Vector2 *
    circleRadius: float32 *
    circleColor: Color *
    layer: int<RenderLayer>
  | CircleOutline of
    circleOutCenter: Vector2 *
    circleOutRadius: float32 *
    circleOutColor: Color *
    layer: int<RenderLayer>
  | CircleSector of
    sectorCenter: Vector2 *
    sectorRadius: float32 *
    sectorStartAngle: float32 *
    sectorEndAngle: float32 *
    sectorSegments: int *
    sectorColor: Color *
    layer: int<RenderLayer>
  | CircleSectorOutline of
    sectorOutCenter: Vector2 *
    sectorOutRadius: float32 *
    sectorOutStartAngle: float32 *
    sectorOutEndAngle: float32 *
    sectorOutSegments: int *
    sectorOutColor: Color *
    layer: int<RenderLayer>
  | CircleGradient of
    circleGradCenterX: int *
    circleGradCenterY: int *
    circleGradRadius: float32 *
    circleGradInner: Color *
    circleGradOuter: Color *
    layer: int<RenderLayer>
  | FillRing of
    ringCenter: Vector2 *
    ringInnerR: float32 *
    ringOuterR: float32 *
    ringStartAngle: float32 *
    ringEndAngle: float32 *
    ringSegments: int *
    ringColor: Color *
    layer: int<RenderLayer>
  | RingOutline of
    ringOutCenter: Vector2 *
    ringOutInnerR: float32 *
    ringOutOuterR: float32 *
    ringOutStartAngle: float32 *
    ringOutEndAngle: float32 *
    ringOutSegments: int *
    ringOutColor: Color *
    layer: int<RenderLayer>
  | FillEllipse of
    ellipseCenterX: int *
    ellipseCenterY: int *
    ellipseRadiusH: float32 *
    ellipseRadiusV: float32 *
    ellipseColor: Color *
    layer: int<RenderLayer>
  | EllipseOutline of
    ellipseOutCenterX: int *
    ellipseOutCenterY: int *
    ellipseOutRadiusH: float32 *
    ellipseOutRadiusV: float32 *
    ellipseOutColor: Color *
    layer: int<RenderLayer>
  // Lines & Curves
  | Line of
    lineStart: Vector2 *
    lineFinish: Vector2 *
    lineColor: Color *
    layer: int<RenderLayer>
  | LineThick of
    lineThickStart: Vector2 *
    lineThickFinish: Vector2 *
    lineThickThickness: float32 *
    lineThickColor: Color *
    layer: int<RenderLayer>
  | LineStrip of
    stripPoints: Vector2[] *
    stripColor: Color *
    layer: int<RenderLayer>
  | Bezier of
    bezierStart: Vector2 *
    bezierControl: Vector2 *
    bezierFinish: Vector2 *
    bezierThickness: float32 *
    bezierColor: Color *
    layer: int<RenderLayer>
  // Triangles & Polygons
  | Triangle of
    triV1: Vector2 *
    triV2: Vector2 *
    triV3: Vector2 *
    triColor: Color *
    layer: int<RenderLayer>
  | TriangleFan of
    fanPoints: Vector2[] *
    fanColor: Color *
    layer: int<RenderLayer>
  | TriangleStrip of
    stripTriPoints: Vector2[] *
    stripTriColor: Color *
    layer: int<RenderLayer>
  | FillPoly of
    polyCenter: Vector2 *
    polySides: int *
    polyRadius: float32 *
    polyRotation: float32 *
    polyColor: Color *
    layer: int<RenderLayer>
  | PolyOutline of
    polyOutCenter: Vector2 *
    polyOutSides: int *
    polyOutRadius: float32 *
    polyOutRotation: float32 *
    polyOutThickness: float32 *
    polyOutColor: Color *
    layer: int<RenderLayer>
  // Camera & Targets
  | BeginCamera of beginCameraCam: Camera2D * layer: int<RenderLayer>
  | BeginCameraConfig of config: Camera2DConfig * layer: int<RenderLayer>
  | EndCamera of layer: int<RenderLayer>
  // Shaders
  | BeginShader of shader: Effect * layer: int<RenderLayer>
  | EndShader of layer: int<RenderLayer>
  // Render Targets
  | BeginTarget of target: RenderTarget2D * layer: int<RenderLayer>
  | EndTarget of layer: int<RenderLayer>
  // Render State
  | SetBlend of blend: BlendMode * layer: int<RenderLayer>
  | SetScissor of
    scissorX: int *
    scissorY: int *
    scissorW: int *
    scissorH: int *
    layer: int<RenderLayer>
  | ClearScissor of layer: int<RenderLayer>
  | SetLineWidth of lineWidth: float32 * layer: int<RenderLayer>
  | SetViewport of
    viewportX: int *
    viewportY: int *
    viewportW: int *
    viewportH: int *
    layer: int<RenderLayer>
  // Escape Hatches
  | DrawImmediate of action: (unit -> unit) * layer: int<RenderLayer>
  | Clear of clearColor: Color * layer: int<RenderLayer>
  // Lighting
  | NoopLight of layer: int<RenderLayer>
  | LitSprite of lightCtx: LightContext2D * sprite: SpriteState
  | EndLighting of lightCtx: LightContext2D * layer: int<RenderLayer>
  | EnableShadows of lightCtx: LightContext2D * layer: int<RenderLayer>
  | DisableShadows of lightCtx: LightContext2D * layer: int<RenderLayer>

/// <summary>
/// Factory functions that create <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/> values.
/// </summary>
/// <remarks>
/// Each function is <c>inline</c> and curried for partial application of styling
/// parameters (layer, color) before the geometry argument.
/// </remarks>
module Command2D =

  // Sprite & Text

  /// <summary>Creates a sprite command from a pre-configured SpriteState.</summary>
  let inline sprite(state: SpriteState) =
    Command2D.Sprite(
      state.Texture,
      state.Dest,
      state.Source,
      state.Origin,
      state.Rotation,
      state.Color,
      state.Layer
    )

  /// <summary>Creates a text command from a pre-configured TextState.</summary>
  let inline text(state: TextState) =
    Command2D.Text(
      state.Font,
      state.Text,
      state.Position,
      state.Scale,
      state.Color,
      state.Layer
    )

  // Rectangles

  /// <summary>Filled rectangle. (layer, color) can be partially applied.</summary>
  let inline fillRect
    (layer: int<RenderLayer>, color: Color)
    (rect: Rectangle)
    =
    Command2D.FillRect(rect, color, layer)

  /// <summary>Rectangle outline with thickness. (layer, color, thickness) can be partially applied.</summary>
  let inline rectOutline
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (rect: Rectangle)
    =
    Command2D.RectOutline(rect, thickness, color, layer)

  /// <summary>Filled rounded rectangle. (layer, color, roundness, segments) can be partially applied.</summary>
  let inline fillRectRounded
    (layer: int<RenderLayer>, color: Color, roundness: float32, segments: int)
    (rect: Rectangle)
    =
    Command2D.FillRectRounded(rect, roundness, segments, color, layer)

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
    =
    Command2D.RectRoundedOutline(
      rect,
      roundness,
      segments,
      thickness,
      color,
      layer
    )

  /// <summary>Vertical gradient rectangle. (layer) can be partially applied.</summary>
  let inline rectGradientV
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int, top: Color, bottom: Color)
    =
    Command2D.RectGradientV(x, y, w, h, top, bottom, layer)

  /// <summary>Horizontal gradient rectangle. (layer) can be partially applied.</summary>
  let inline rectGradientH
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int, left: Color, right: Color)
    =
    Command2D.RectGradientH(x, y, w, h, left, right, layer)

  /// <summary>4-corner gradient rectangle. (layer) can be partially applied.</summary>
  let inline rectGradient
    (layer: int<RenderLayer>)
    (rect: Rectangle, tl: Color, bl: Color, tr: Color, br: Color)
    =
    Command2D.RectGradient(rect, tl, bl, tr, br, layer)

  // Circles & Ellipses

  /// <summary>Filled circle. (layer, color) can be partially applied.</summary>
  let inline fillCircle
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    =
    Command2D.FillCircle(center, radius, color, layer)

  /// <summary>Circle outline. (layer, color) can be partially applied.</summary>
  let inline circleOutline
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    =
    Command2D.CircleOutline(center, radius, color, layer)

  /// <summary>Filled circle sector (pie slice). (layer, color) can be partially applied.</summary>
  let inline circleSector
    (layer: int<RenderLayer>, color: Color)
    (
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      segments: int
    ) =
    Command2D.CircleSector(
      center,
      radius,
      startAngle,
      endAngle,
      segments,
      color,
      layer
    )

  /// <summary>Circle sector outline. (layer, color) can be partially applied.</summary>
  let inline circleSectorOutline
    (layer: int<RenderLayer>, color: Color)
    (
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      segments: int
    ) =
    Command2D.CircleSectorOutline(
      center,
      radius,
      startAngle,
      endAngle,
      segments,
      color,
      layer
    )

  /// <summary>Gradient circle. (layer) can be partially applied.</summary>
  let inline circleGradient
    (layer: int<RenderLayer>)
    (centerX: int, centerY: int, radius: float32, inner: Color, outer: Color)
    =
    Command2D.CircleGradient(centerX, centerY, radius, inner, outer, layer)

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
    ) =
    Command2D.FillRing(
      center,
      innerR,
      outerR,
      startAngle,
      endAngle,
      segments,
      color,
      layer
    )

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
    ) =
    Command2D.RingOutline(
      center,
      innerR,
      outerR,
      startAngle,
      endAngle,
      segments,
      color,
      layer
    )

  /// <summary>Filled ellipse. (layer, color) can be partially applied.</summary>
  let inline fillEllipse
    (layer: int<RenderLayer>, color: Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    =
    Command2D.FillEllipse(centerX, centerY, radiusH, radiusV, color, layer)

  /// <summary>Ellipse outline. (layer, color) can be partially applied.</summary>
  let inline ellipseOutline
    (layer: int<RenderLayer>, color: Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    =
    Command2D.EllipseOutline(centerX, centerY, radiusH, radiusV, color, layer)

  // Lines & Curves

  /// <summary>1-pixel line. (layer, color) can be partially applied.</summary>
  let inline line
    (layer: int<RenderLayer>, color: Color)
    (start: Vector2, finish: Vector2)
    =
    Command2D.Line(start, finish, color, layer)

  /// <summary>Line with custom thickness. (layer, color, thickness) can be partially applied.</summary>
  let inline lineThick
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, finish: Vector2)
    =
    Command2D.LineThick(start, finish, thickness, color, layer)

  /// <summary>Connected line segments. (layer, color) can be partially applied.</summary>
  let inline lineStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    =
    Command2D.LineStrip(points, color, layer)

  /// <summary>Quadratic bezier curve. (layer, color, thickness) can be partially applied.</summary>
  let inline bezier
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, control: Vector2, finish: Vector2)
    =
    Command2D.Bezier(start, control, finish, thickness, color, layer)

  // Triangles & Polygons

  /// <summary>Filled triangle from 3 vertices. (layer, color) can be partially applied.</summary>
  let inline triangle
    (layer: int<RenderLayer>, color: Color)
    (v1: Vector2, v2: Vector2, v3: Vector2)
    =
    Command2D.Triangle(v1, v2, v3, color, layer)

  /// <summary>Filled triangle fan. (layer, color) can be partially applied.</summary>
  let inline triangleFan
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    =
    Command2D.TriangleFan(points, color, layer)

  /// <summary>Filled triangle strip. (layer, color) can be partially applied.</summary>
  let inline triangleStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    =
    Command2D.TriangleStrip(points, color, layer)

  /// <summary>Filled regular polygon. (layer, color) can be partially applied.</summary>
  let inline fillPoly
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    =
    Command2D.FillPoly(center, sides, radius, rotation, color, layer)

  /// <summary>Regular polygon outline with thickness. (layer, color, thickness) can be partially applied.</summary>
  let inline polyOutline
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    =
    Command2D.PolyOutline(
      center,
      sides,
      radius,
      rotation,
      thickness,
      color,
      layer
    )

  // Camera & Config

  /// <summary>Begins a 2D camera transform. (layer) can be partially applied.</summary>
  let inline beginCamera (layer: int<RenderLayer>) (camera: Camera2D) =
    Command2D.BeginCamera(camera, layer)

  /// <summary>Begins a 2D camera with viewport/clear config. (layer) can be partially applied.</summary>
  let inline beginCameraConfig
    (layer: int<RenderLayer>)
    (config: Camera2DConfig)
    =
    Command2D.BeginCameraConfig(config, layer)

  /// <summary>Ends the current 2D camera transform.</summary>
  let inline endCamera(layer: int<RenderLayer>) = Command2D.EndCamera(layer)

  // Shaders

  /// <summary>Begins a custom shader/effect block. (layer) can be partially applied.</summary>
  let inline beginShader (layer: int<RenderLayer>) (shader: Effect) =
    Command2D.BeginShader(shader, layer)

  /// <summary>Ends the current shader block.</summary>
  let inline endShader(layer: int<RenderLayer>) = Command2D.EndShader(layer)

  // Render Targets

  /// <summary>Begins rendering to a render target. (layer) can be partially applied.</summary>
  let inline beginTarget (layer: int<RenderLayer>) (target: RenderTarget2D) =
    Command2D.BeginTarget(target, layer)

  /// <summary>Ends the current render target and resumes back-buffer rendering.</summary>
  let inline endTarget(layer: int<RenderLayer>) = Command2D.EndTarget(layer)

  // Render State

  /// <summary>Sets the active blend mode. (layer) can be partially applied.</summary>
  let inline setBlend (layer: int<RenderLayer>) (mode: BlendMode) =
    Command2D.SetBlend(mode, layer)

  /// <summary>Enables a scissor rectangle. (layer) can be partially applied.</summary>
  let inline setScissor
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    =
    Command2D.SetScissor(x, y, w, h, layer)

  /// <summary>Disables the scissor rectangle.</summary>
  let inline clearScissor(layer: int<RenderLayer>) =
    Command2D.ClearScissor(layer)

  /// <summary>Sets the default line width for thick line primitives.</summary>
  let inline setLineWidth (layer: int<RenderLayer>) (width: float32) =
    Command2D.SetLineWidth(width, layer)

  /// <summary>Sets the device viewport. (layer) can be partially applied.</summary>
  let inline setViewport
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    =
    Command2D.SetViewport(x, y, w, h, layer)

  // Escape Hatches

  /// <summary>
  /// Flushes the SpriteBatch, exits camera, runs the action, then restores state.
  /// (layer) can be partially applied.
  /// </summary>
  let inline drawImmediate (layer: int<RenderLayer>) (action: unit -> unit) =
    Command2D.DrawImmediate(action, layer)

  /// <summary>Clears the current framebuffer to the given color.</summary>
  let inline clear (layer: int<RenderLayer>) (color: Color) =
    Command2D.Clear(color, layer)

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.SpriteState"/>.</summary>
module SpriteState =

  /// <summary>
  /// Creates a sprite state with required fields.
  /// Defaults: Origin=Zero, Rotation=0, Color=White, Layer=0.
  /// </summary>
  let create
    (texture: Texture2D, dest: Rectangle, source: Rectangle)
    : SpriteState =
    {
      Texture = texture
      Dest = dest
      Source = source
      Origin = Vector2.Zero
      Rotation = 0.0f
      Color = Color.White
      Layer = 0<RenderLayer>
      NormalMap = ValueNone
    }

  /// <summary>Sets the origin point for rotation/positioning.</summary>
  let inline withOrigin (v: Vector2) (s: SpriteState) = { s with Origin = v }

  /// <summary>Sets the rotation in radians.</summary>
  let inline withRotation (v: float32) (s: SpriteState) = {
    s with
        Rotation = v
  }

  /// <summary>Sets the tint color.</summary>
  let inline withColor (v: Color) (s: SpriteState) = { s with Color = v }

  /// <summary>Sets the render layer.</summary>
  let inline withLayer (v: int<RenderLayer>) (s: SpriteState) = {
    s with
        Layer = v
  }

  /// <summary>Sets the normal map texture for per-pixel lighting.</summary>
  let inline withNormalMap (v: Texture2D) (s: SpriteState) = {
    s with
        NormalMap = ValueSome v
  }

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.TextState"/>.</summary>
module TextState =

  /// <summary>
  /// Creates a text state with required fields.
  /// Defaults: Scale=1.0, Color=White, Layer=0.
  /// </summary>
  let create(font: SpriteFont, text: string, position: Vector2) : TextState = {
    Font = font
    Text = text
    Position = position
    Scale = 1.0f
    Color = Color.White
    Layer = 0<RenderLayer>
  }

  /// <summary>Sets the uniform scale factor (1.0 = default font size).</summary>
  let inline withScale (v: float32) (s: TextState) = { s with Scale = v }

  /// <summary>Sets the tint color.</summary>
  let inline withColor (v: Color) (s: TextState) = { s with Color = v }

  /// <summary>Sets the render layer.</summary>
  let inline withLayer (v: int<RenderLayer>) (s: TextState) = {
    s with
        Layer = v
  }
