namespace Mibo.Elmish.Next.Graphics2D

open Mibo.Elmish.Next.Graphics2D.Base

open System.Numerics

// ─────────────────────────────────────────────────────────────────
// Neutral particle data (no resource handles)
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Neutral particle data. The backend's public <c>Particle2D</c> struct
/// (with native <c>Color</c>/<c>Rectangle</c>) is converted to this by
/// the DSL at <c>Add</c> time.
/// </summary>
[<Struct>]
type ParticleData = {
  Position: Vector2
  Size: Vector2
  Rotation: float32
  SourceRect: Rect
  Color: Color
}

// ─────────────────────────────────────────────────────────────────
// Core Command2D
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Closed set of backend-neutral 2D render commands.
/// Stored in a <see cref="T:Mibo.Elmish.Next.Graphics2D.RenderBuffer2DBase"/> and
/// dispatched via pattern matching — no interface boxing.
/// </summary>
/// <remarks>
/// Resource fields are opaque <c>int&lt;Resource&gt;</c> handles resolved by the
/// backend renderer at dispatch time. Geometry, color, and layer fields are
/// backend-neutral (<see cref="T:Mibo.Elmish.Next.Graphics2D.Color"/>,
/// <see cref="T:Mibo.Elmish.Next.Graphics2D.Rect"/>, <c>System.Numerics</c>).
/// </remarks>
[<RequireQualifiedAccess; Struct>]
type Command2D =
  // Sprite & Text
  | Sprite of
    spriteTexture: int<Texture> *
    spriteDest: Rect *
    spriteSource: Rect *
    spriteOrigin: Vector2 *
    spriteRotation: float32 *
    spriteColor: Color *
    layer: int<RenderLayer>
  | Text of
    textFont: int<Font> *
    textValue: string *
    textPosition: Vector2 *
    textFontSize: float32 *
    textSpacing: float32 *
    textColor: Color *
    layer: int<RenderLayer>
  // Rectangles
  | FillRect of fillRect: Rect * fillColor: Color * layer: int<RenderLayer>
  | RectOutline of
    outlineRect: Rect *
    outlineThickness: float32 *
    outlineColor: Color *
    layer: int<RenderLayer>
  | FillRectRounded of
    roundedRect: Rect *
    roundedFillRoundness: float32 *
    roundedFillSegments: int *
    roundedFillColor: Color *
    layer: int<RenderLayer>
  | RectRoundedOutline of
    roundedOutlineRect: Rect *
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
    gradRect: Rect *
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
  | BeginCamera of beginCameraCam: Camera2DState * layer: int<RenderLayer>
  | BeginCameraConfig of config: Camera2DConfig * layer: int<RenderLayer>
  | EndCamera of layer: int<RenderLayer>
  | BeginShader of beginShaderVal: int<Shader> * layer: int<RenderLayer>
  | EndShader of layer: int<RenderLayer>
  | BeginTarget of beginTargetVal: int<RenderTarget> * layer: int<RenderLayer>
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
  | LitSprite of
    lightCtx: int<LightContext> *
    litTexture: int<Texture> *
    litDest: Rect *
    litSource: Rect *
    litOrigin: Vector2 *
    litRotation: float32 *
    litColor: Color *
    litNormalMap: int<Texture> voption *
    layer: int<RenderLayer>
  | EndLighting of endLightingCtx: int<LightContext> * layer: int<RenderLayer>
  // Shadow Control
  | EnableShadows of
    enableShadowsCtx: int<LightContext> *
    layer: int<RenderLayer>
  | DisableShadows of
    disableShadowsCtx: int<LightContext> *
    layer: int<RenderLayer>
  // Particles
  | Particle of
    particleTexture: int<Texture> *
    particleData: ParticleData[] *
    particleCount: int *
    layer: int<RenderLayer>
