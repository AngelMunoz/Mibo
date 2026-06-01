namespace Mibo.Elmish.Graphics2D

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting

/// <summary>Unit of measure for 2D render layer ordering.</summary>
[<Measure>]
type RenderLayer

/// <summary>State required to render a 2D sprite via DrawTexturePro.</summary>
[<Struct>]
type SpriteState = {
  Texture: Texture2D
  Dest: Rectangle
  Source: Rectangle
  Origin: Vector2
  Rotation: float32
  Color: Color
  Layer: int<RenderLayer>
  /// <summary>Optional normal map texture for per-pixel lighting. When present,
  /// the lit shader samples this to compute diffuse lighting per pixel.</summary>
  NormalMap: Texture2D voption
}

/// <summary>
/// Closed set of 2D render commands. Stored in <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>
/// and dispatched via pattern matching — no interface boxing.
/// </summary>
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
    textFont: Font *
    textValue: string *
    textPosition: Vector2 *
    textFontSize: float32 *
    textSpacing: float32 *
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
  // Camera, Shader, Target
  | BeginCamera of beginCameraCam: Camera2D * layer: int<RenderLayer>
  | BeginCameraConfig of config: Camera2DConfig * layer: int<RenderLayer>
  | EndCamera of layer: int<RenderLayer>
  | BeginShader of beginShaderVal: Shader * layer: int<RenderLayer>
  | EndShader of layer: int<RenderLayer>
  | BeginTarget of beginTargetVal: RenderTexture2D * layer: int<RenderLayer>
  | EndTarget of layer: int<RenderLayer>
  // Render State
  | SetBlend of setBlendMode: BlendMode * layer: int<RenderLayer>
  | SetScissor of
    scissorX: int *
    scissorY: int *
    scissorW: int *
    scissorH: int *
    layer: int<RenderLayer>
  | ClearScissor of layer: int<RenderLayer>
  | SetLineWidth of lineWidthVal: float32 * layer: int<RenderLayer>
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
  | LitSprite of litLightCtx: LightContext2D * litSprite: SpriteState
  | EndLighting of endLightingCtx: LightContext2D * layer: int<RenderLayer>
  // Shadow Control
  | EnableShadows of enableShadowsCtx: LightContext2D * layer: int<RenderLayer>
  | DisableShadows of
    disableShadowsCtx: LightContext2D *
    layer: int<RenderLayer>
  // Particles
  | Particle of
    particleTexture: Texture2D *
    particleData: Particle2D[] *
    particleCount: int *
    layer: int<RenderLayer>

/// <summary>
/// Factory functions that create <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/> values.
/// </summary>
module Command2D =

  /// <summary>State required to render a 2D sprite via DrawTexturePro.</summary>
  /// <summary>State required to render 2D text via raylib's DrawTextEx.</summary>
  [<Struct>]
  type TextState = {
    Font: Font
    Text: string
    Position: Vector2
    FontSize: float32
    Spacing: float32
    Color: Color
    Layer: int<RenderLayer>
  }

  // Sprite & Text
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

  let inline text(state: TextState) =
    Command2D.Text(
      state.Font,
      state.Text,
      state.Position,
      state.FontSize,
      state.Spacing,
      state.Color,
      state.Layer
    )

  // Rectangles
  let inline fillRect
    (layer: int<RenderLayer>, color: Color)
    (rect: Rectangle)
    =
    Command2D.FillRect(rect, color, layer)

  let inline rectOutline
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (rect: Rectangle)
    =
    Command2D.RectOutline(rect, thickness, color, layer)

  let inline fillRectRounded
    (layer: int<RenderLayer>, color: Color, roundness: float32, segments: int)
    (rect: Rectangle)
    =
    Command2D.FillRectRounded(rect, roundness, segments, color, layer)

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

  let inline rectGradientV
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int, top: Color, bottom: Color)
    =
    Command2D.RectGradientV(x, y, w, h, top, bottom, layer)

  let inline rectGradientH
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int, left: Color, right: Color)
    =
    Command2D.RectGradientH(x, y, w, h, left, right, layer)

  let inline rectGradient
    (layer: int<RenderLayer>)
    (rect: Rectangle, tl: Color, bl: Color, tr: Color, br: Color)
    =
    Command2D.RectGradient(rect, tl, bl, tr, br, layer)

  // Circles & Ellipses
  let inline fillCircle
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    =
    Command2D.FillCircle(center, radius, color, layer)

  let inline circleOutline
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    =
    Command2D.CircleOutline(center, radius, color, layer)

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

  let inline circleGradient
    (layer: int<RenderLayer>)
    (centerX: int, centerY: int, radius: float32, inner: Color, outer: Color)
    =
    Command2D.CircleGradient(centerX, centerY, radius, inner, outer, layer)

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

  let inline fillEllipse
    (layer: int<RenderLayer>, color: Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    =
    Command2D.FillEllipse(centerX, centerY, radiusH, radiusV, color, layer)

  let inline ellipseOutline
    (layer: int<RenderLayer>, color: Color)
    (centerX: int, centerY: int, radiusH: float32, radiusV: float32)
    =
    Command2D.EllipseOutline(centerX, centerY, radiusH, radiusV, color, layer)

  // Lines & Curves
  let inline line
    (layer: int<RenderLayer>, color: Color)
    (start: Vector2, finish: Vector2)
    =
    Command2D.Line(start, finish, color, layer)

  let inline lineThick
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, finish: Vector2)
    =
    Command2D.LineThick(start, finish, thickness, color, layer)

  let inline lineStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    =
    Command2D.LineStrip(points, color, layer)

  let inline bezier
    (layer: int<RenderLayer>, color: Color, thickness: float32)
    (start: Vector2, control: Vector2, finish: Vector2)
    =
    Command2D.Bezier(start, control, finish, thickness, color, layer)

  // Triangles & Polygons
  let inline triangle
    (layer: int<RenderLayer>, color: Color)
    (v1: Vector2, v2: Vector2, v3: Vector2)
    =
    Command2D.Triangle(v1, v2, v3, color, layer)

  let inline triangleFan
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    =
    Command2D.TriangleFan(points, color, layer)

  let inline triangleStrip
    (layer: int<RenderLayer>, color: Color)
    (points: Vector2[])
    =
    Command2D.TriangleStrip(points, color, layer)

  let inline fillPoly
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, sides: int, radius: float32, rotation: float32)
    =
    Command2D.FillPoly(center, sides, radius, rotation, color, layer)

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

  // Camera, Shader, Target
  let inline beginCamera (layer: int<RenderLayer>) (camera: Camera2D) =
    Command2D.BeginCamera(camera, layer)

  let inline beginCameraConfig
    (layer: int<RenderLayer>)
    (config: Camera2DConfig)
    =
    Command2D.BeginCameraConfig(config, layer)

  let inline endCamera(layer: int<RenderLayer>) = Command2D.EndCamera(layer)

  let inline beginShader (layer: int<RenderLayer>) (shader: Shader) =
    Command2D.BeginShader(shader, layer)

  let inline endShader(layer: int<RenderLayer>) = Command2D.EndShader(layer)

  let inline beginTarget (layer: int<RenderLayer>) (target: RenderTexture2D) =
    Command2D.BeginTarget(target, layer)

  let inline endTarget(layer: int<RenderLayer>) = Command2D.EndTarget(layer)

  // Render State
  let inline setBlend (layer: int<RenderLayer>) (mode: BlendMode) =
    Command2D.SetBlend(mode, layer)

  let inline setScissor
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    =
    Command2D.SetScissor(x, y, w, h, layer)

  let inline clearScissor(layer: int<RenderLayer>) =
    Command2D.ClearScissor(layer)

  let inline setLineWidth (layer: int<RenderLayer>) (width: float32) =
    Command2D.SetLineWidth(width, layer)

  let inline setViewport
    (layer: int<RenderLayer>)
    (x: int, y: int, w: int, h: int)
    =
    Command2D.SetViewport(x, y, w, h, layer)

  // Escape Hatches
  let inline drawImmediate (layer: int<RenderLayer>) (action: unit -> unit) =
    Command2D.DrawImmediate(action, layer)

  let inline clear (layer: int<RenderLayer>) (color: Color) =
    Command2D.Clear(color, layer)

  // Shadow Control
  let inline enableShadows
    (layer: int<RenderLayer>)
    (lightCtx: LightContext2D)
    =
    Command2D.EnableShadows(lightCtx, layer)

  let inline disableShadows
    (layer: int<RenderLayer>)
    (lightCtx: LightContext2D)
    =
    Command2D.DisableShadows(lightCtx, layer)

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.SpriteState"/>.</summary>
module SpriteState =

  /// <summary>Creates a sprite state with required fields. Defaults: Origin=Zero, Rotation=0, Color=White, Layer=0, NormalMap=None.</summary>
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

  let inline withOrigin (v: Vector2) (s: SpriteState) = { s with Origin = v }

  let inline withRotation (v: float32) (s: SpriteState) = {
    s with
        Rotation = v
  }

  let inline withColor (v: Color) (s: SpriteState) = { s with Color = v }

  let inline withLayer (v: int<RenderLayer>) (s: SpriteState) = {
    s with
        Layer = v
  }

  let inline withNormalMap (v: Texture2D) (s: SpriteState) = {
    s with
        NormalMap = ValueSome v
  }

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.Command2D.TextState"/>.</summary>
module TextState =

  /// <summary>Creates a text state with required fields. Defaults: FontSize=20, Spacing=1, Color=White, Layer=0.</summary>
  let create
    (font: Font, text: string, position: Vector2)
    : Command2D.TextState =
    {
      Font = font
      Text = text
      Position = position
      FontSize = 20.0f
      Spacing = 1.0f
      Color = Color.White
      Layer = 0<RenderLayer>
    }

  let inline withFontSize (v: float32) (s: Command2D.TextState) = {
    s with
        FontSize = v
  }

  let inline withSpacing (v: float32) (s: Command2D.TextState) = {
    s with
        Spacing = v
  }

  let inline withColor (v: Color) (s: Command2D.TextState) = {
    s with
        Color = v
  }

  let inline withLayer (v: int<RenderLayer>) (s: Command2D.TextState) = {
    s with
        Layer = v
  }
