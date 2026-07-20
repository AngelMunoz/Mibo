// ─────────────────────────────────────────────────────────────────────────────
// The fluent Draw DSL — one backend-neutral surface for 2D and 3D.
//
// Every member is an [<Extension>] static member inline with SRTP constraints:
// the DSL itself is defined ONCE here in Core, and each backend satisfies the
// constraints with small `member inline` witnesses on its render buffer (see
// Graphics2D/DrawWitnesses.fs and Graphics3D/DrawWitnesses.fs in Mibo.Raylib
// and Mibo.MonoGame). At the call site the whole chain erases to direct
// buffer.Add(...) calls — zero dispatch, zero closure allocation.
//
// Conventions:
//   * Parameters are sorted by common usage; anything with a sensible default
//     is optional (`layer` defaults to 0<RenderLayer>).
//   * Colors are Mibo.Color (backend-neutral; converts at the boundary).
//   * Vectors are System.Numerics (raylib-native; MonoGame converts).
//   * Backend-typed things (textures, fonts, cameras, shaders, models, state
//     records) are generic handles — the witness takes the backend's own type.
//   * Members that exist on only one backend (e.g. SetSamplerState) simply
//     have a witness on one buffer — calling them on the other backend is a
//     compile error naming the missing witness.
//   * Members spanning 2D and 3D (cameras, post-process, DrawImmediate) share
//     one name; the buffer type selects the witness. 3D has no layer concept —
//     its witnesses accept and ignore `layer`.
// ─────────────────────────────────────────────────────────────────────────────
namespace Mibo.Elmish.Graphics

open System.Numerics
open System.Runtime.CompilerServices
open Mibo
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D

/// <summary>Rectangle witnesses (fills, outlines, rounded, gradients).</summary>
type WithRects2D<'T
  when 'T: (member AddFillRect:
    float32 * float32 * float32 * float32 * Color * int<RenderLayer> -> unit)
  and 'T: (member AddRectOutline:
    float32 * float32 * float32 * float32 * Color * float32 * int<RenderLayer> ->
      unit)
  and 'T: (member AddFillRectRounded:
    float32 *
    float32 *
    float32 *
    float32 *
    Color *
    float32 *
    int *
    int<RenderLayer> ->
      unit)
  and 'T: (member AddRectRoundedOutline:
    float32 *
    float32 *
    float32 *
    float32 *
    Color *
    float32 *
    int *
    float32 *
    int<RenderLayer> ->
      unit)
  and 'T: (member AddRectGradientV:
    int * int * int * int * Color * Color * int<RenderLayer> -> unit)
  and 'T: (member AddRectGradientH:
    int * int * int * int * Color * Color * int<RenderLayer> -> unit)
  and 'T: (member AddRectGradient:
    float32 *
    float32 *
    float32 *
    float32 *
    Color *
    Color *
    Color *
    Color *
    int<RenderLayer> ->
      unit)> = 'T

/// <summary>Circle, sector, and radial-gradient witnesses.</summary>
type WithCircles2D<'T
  when 'T: (member AddFillCircle:
    Vector2 * float32 * Color * int<RenderLayer> -> unit)
  and 'T: (member AddCircleOutline:
    Vector2 * float32 * Color * int<RenderLayer> -> unit)
  and 'T: (member AddCircleSector:
    Vector2 * float32 * float32 * float32 * Color * int * int<RenderLayer> ->
      unit)
  and 'T: (member AddCircleSectorOutline:
    Vector2 * float32 * float32 * float32 * Color * int * int<RenderLayer> ->
      unit)
  and 'T: (member AddCircleGradient:
    int * int * float32 * Color * Color * int<RenderLayer> -> unit)> = 'T

/// <summary>Ring / arc witnesses.</summary>
type WithRings2D<'T
  when 'T: (member AddFillRing:
    Vector2 *
    float32 *
    float32 *
    float32 *
    float32 *
    Color *
    int *
    int<RenderLayer> ->
      unit)
  and 'T: (member AddRingOutline:
    Vector2 *
    float32 *
    float32 *
    float32 *
    float32 *
    Color *
    int *
    int<RenderLayer> ->
      unit)> = 'T

/// <summary>Ellipse witnesses.</summary>
type WithEllipses2D<'T
  when 'T: (member AddFillEllipse:
    int * int * float32 * float32 * Color * int<RenderLayer> -> unit)
  and 'T: (member AddEllipseOutline:
    int * int * float32 * float32 * Color * int<RenderLayer> -> unit)> = 'T

/// <summary>Line &amp; curve witnesses.</summary>
type WithLines2D<'T
  when 'T: (member AddLine: Vector2 * Vector2 * Color * int<RenderLayer> -> unit)
  and 'T: (member AddLineThick:
    Vector2 * Vector2 * Color * float32 * int<RenderLayer> -> unit)
  and 'T: (member AddBezier:
    Vector2 * Vector2 * Vector2 * Color * float32 * int<RenderLayer> -> unit)> =
  'T

/// <summary>Triangle &amp; polygon witnesses.</summary>
type WithPolygons2D<'T
  when 'T: (member AddTriangle:
    Vector2 * Vector2 * Vector2 * Color * int<RenderLayer> -> unit)
  and 'T: (member AddFillPoly:
    Vector2 * int * float32 * float32 * Color * int<RenderLayer> -> unit)
  and 'T: (member AddPolyOutline:
    Vector2 * int * float32 * float32 * Color * float32 * int<RenderLayer> ->
      unit)> = 'T

/// <summary>All 2D shape witnesses, composed from the per-family aliases above.</summary>
type WithShapes2D<'T
  when WithRects2D<'T>
  and WithCircles2D<'T>
  and WithRings2D<'T>
  and WithEllipses2D<'T>
  and WithLines2D<'T>
  and WithPolygons2D<'T>> = 'T

/// <summary>
/// The unified fluent draw DSL. Chain members on the render buffer:
/// <code lang="fsharp">
/// buffer
///   .BeginCamera(camera)
///   .FillCircle(400f, 300f, 48f, Color.Blue)
///   .Sprite(playerSprite)
///   .EndCamera()
///   .Text(font, "HP 100", Vector2(10f, 10f), 20f, layer = 1001&lt;RenderLayer&gt;)
/// |&gt; ignore
/// </code>
/// </summary>
[<Extension>]
type Draw =

  // ──────────────────────────────────────────────
  // 2D — Sprites & Text (backend state records, pass-through)
  // ──────────────────────────────────────────────

  /// <summary>Draws a sprite from the backend's SpriteState record.</summary>
  [<Extension>]
  static member inline sprite<'B, 'S
    when 'B: (member AddSpriteState: 'S -> unit)>
    (buffer: 'B, state: 'S)
    : 'B =
    buffer.AddSpriteState state
    buffer

  /// <summary>Draws text from the backend's TextState record.</summary>
  [<Extension>]
  static member inline text<'B, 'S when 'B: (member AddTextState: 'S -> unit)>
    (buffer: 'B, state: 'S)
    : 'B =
    buffer.AddTextState state
    buffer

  /// <summary>
  /// Draws text from parts. <paramref name="size"/> maps to the backend's
  /// sizing model (raylib: font size in pixels; MonoGame: uniform scale).
  /// <paramref name="spacing"/> is used by raylib and ignored by MonoGame.
  /// </summary>
  [<Extension>]
  static member inline text<'B, 'F
    when 'B: (member AddText:
      'F * string * Vector2 * float32 * float32 * Color * int<RenderLayer> ->
        unit)>
    (
      buffer: 'B,
      font: 'F,
      text: string,
      position: Vector2,
      size: float32,
      [<Struct>] ?tint: Color,
      [<Struct>] ?spacing: float32,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddText(
      font,
      text,
      position,
      size,
      defaultValueArg spacing 1.0f,
      defaultValueArg tint Color.White,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  // ──────────────────────────────────────────────
  // 2D — Rectangles
  // ──────────────────────────────────────────────

  /// <summary>Filled rectangle. Coordinates are float pixels (rounded on MonoGame).</summary>
  [<Extension>]
  static member inline fillRect<'B when WithRects2D<'B>>
    (
      buffer: 'B,
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddFillRect(x, y, w, h, color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Rectangle outline.</summary>
  [<Extension>]
  static member inline rectOutline<'B when WithRects2D<'B>>
    (
      buffer: 'B,
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      [<Struct>] ?thickness: float32,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddRectOutline(
      x,
      y,
      w,
      h,
      color,
      defaultValueArg thickness 1.0f,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Filled rounded rectangle.</summary>
  [<Extension>]
  static member inline fillRectRounded<'B when WithRects2D<'B>>
    (
      buffer: 'B,
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      [<Struct>] ?roundness: float32,
      [<Struct>] ?segments: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddFillRectRounded(
      x,
      y,
      w,
      h,
      color,
      defaultValueArg roundness 0.5f,
      defaultValueArg segments 8,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Rounded rectangle outline.</summary>
  [<Extension>]
  static member inline rectRoundedOutline<'B when WithRects2D<'B>>
    (
      buffer: 'B,
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      [<Struct>] ?roundness: float32,
      [<Struct>] ?segments: int,
      [<Struct>] ?thickness: float32,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddRectRoundedOutline(
      x,
      y,
      w,
      h,
      color,
      defaultValueArg roundness 0.5f,
      defaultValueArg segments 8,
      defaultValueArg thickness 1.0f,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Vertical gradient rectangle (int pixel coords, matching the existing API).</summary>
  [<Extension>]
  static member inline rectGradientV<'B when WithRects2D<'B>>
    (
      buffer: 'B,
      x: int,
      y: int,
      w: int,
      h: int,
      top: Color,
      bottom: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddRectGradientV(
      x,
      y,
      w,
      h,
      top,
      bottom,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Horizontal gradient rectangle (int pixel coords, matching the existing API).</summary>
  [<Extension>]
  static member inline rectGradientH<'B when WithRects2D<'B>>
    (
      buffer: 'B,
      x: int,
      y: int,
      w: int,
      h: int,
      left: Color,
      right: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddRectGradientH(
      x,
      y,
      w,
      h,
      left,
      right,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>4-corner gradient rectangle.</summary>
  [<Extension>]
  static member inline rectGradient<'B when WithRects2D<'B>>
    (
      buffer: 'B,
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      topLeft: Color,
      bottomLeft: Color,
      topRight: Color,
      bottomRight: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddRectGradient(
      x,
      y,
      w,
      h,
      topLeft,
      bottomLeft,
      topRight,
      bottomRight,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  // ──────────────────────────────────────────────
  // 2D — Circles, Rings, Ellipses
  // ──────────────────────────────────────────────

  /// <summary>Filled circle.</summary>
  [<Extension>]
  static member inline fillCircle<'B when WithCircles2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      radius: float32,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddFillCircle(
      center,
      radius,
      color,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Circle outline.</summary>
  [<Extension>]
  static member inline circleOutline<'B when WithCircles2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      radius: float32,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddCircleOutline(
      center,
      radius,
      color,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Filled circle sector (pie slice).</summary>
  [<Extension>]
  static member inline circleSector<'B when WithCircles2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      [<Struct>] ?segments: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddCircleSector(
      center,
      radius,
      startAngle,
      endAngle,
      color,
      defaultValueArg segments 16,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Circle sector outline.</summary>
  [<Extension>]
  static member inline circleSectorOutline<'B when WithCircles2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      [<Struct>] ?segments: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddCircleSectorOutline(
      center,
      radius,
      startAngle,
      endAngle,
      color,
      defaultValueArg segments 16,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Radial gradient circle (int pixel center, matching the existing API).</summary>
  [<Extension>]
  static member inline circleGradient<'B when WithCircles2D<'B>>
    (
      buffer: 'B,
      centerX: int,
      centerY: int,
      radius: float32,
      inner: Color,
      outer: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddCircleGradient(
      centerX,
      centerY,
      radius,
      inner,
      outer,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Filled ring / arc.</summary>
  [<Extension>]
  static member inline fillRing<'B when WithRings2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      innerRadius: float32,
      outerRadius: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      [<Struct>] ?segments: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddFillRing(
      center,
      innerRadius,
      outerRadius,
      startAngle,
      endAngle,
      color,
      defaultValueArg segments 16,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Ring / arc outline.</summary>
  [<Extension>]
  static member inline ringOutline<'B when WithRings2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      innerRadius: float32,
      outerRadius: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      [<Struct>] ?segments: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddRingOutline(
      center,
      innerRadius,
      outerRadius,
      startAngle,
      endAngle,
      color,
      defaultValueArg segments 16,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Filled ellipse (int pixel center, matching the existing API).</summary>
  [<Extension>]
  static member inline fillEllipse<'B when WithEllipses2D<'B>>
    (
      buffer: 'B,
      centerX: int,
      centerY: int,
      radiusH: float32,
      radiusV: float32,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddFillEllipse(
      centerX,
      centerY,
      radiusH,
      radiusV,
      color,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Ellipse outline (int pixel center, matching the existing API).</summary>
  [<Extension>]
  static member inline ellipseOutline<'B when WithEllipses2D<'B>>
    (
      buffer: 'B,
      centerX: int,
      centerY: int,
      radiusH: float32,
      radiusV: float32,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddEllipseOutline(
      centerX,
      centerY,
      radiusH,
      radiusV,
      color,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  // ──────────────────────────────────────────────
  // 2D — Lines & Curves
  // ──────────────────────────────────────────────

  /// <summary>1-pixel line.</summary>
  [<Extension>]
  static member inline line<'B when WithLines2D<'B>>
    (
      buffer: 'B,
      start: Vector2,
      finish: Vector2,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddLine(start, finish, color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Line with custom thickness.</summary>
  [<Extension>]
  static member inline lineThick<'B when WithLines2D<'B>>
    (
      buffer: 'B,
      start: Vector2,
      finish: Vector2,
      color: Color,
      [<Struct>] ?thickness: float32,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddLineThick(
      start,
      finish,
      color,
      defaultValueArg thickness 1.0f,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Connected line segments. The point array is a backend handle
  /// (System.Numerics on raylib, XNA on MonoGame) — no per-frame conversion.
  /// Single-point members take System.Numerics and convert for free.</summary>
  [<Extension>]
  static member inline lineStrip<'B, 'P
    when 'B: (member AddLineStrip: 'P[] * Color * int<RenderLayer> -> unit)>
    (buffer: 'B, points: 'P[], color: Color, [<Struct>] ?layer: int<RenderLayer>) : 'B =
    buffer.AddLineStrip(points, color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Quadratic bezier curve.</summary>
  [<Extension>]
  static member inline bezier<'B when WithLines2D<'B>>
    (
      buffer: 'B,
      start: Vector2,
      control: Vector2,
      finish: Vector2,
      color: Color,
      [<Struct>] ?thickness: float32,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddBezier(
      start,
      control,
      finish,
      color,
      defaultValueArg thickness 1.0f,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  // ──────────────────────────────────────────────
  // 2D — Triangles & Polygons
  // ──────────────────────────────────────────────

  /// <summary>Filled triangle from 3 vertices.</summary>
  [<Extension>]
  static member inline triangle<'B when WithPolygons2D<'B>>
    (
      buffer: 'B,
      v1: Vector2,
      v2: Vector2,
      v3: Vector2,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddTriangle(v1, v2, v3, color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Filled triangle fan. Points array is a backend handle (see LineStrip).</summary>
  [<Extension>]
  static member inline triangleFan<'B, 'P
    when 'B: (member AddTriangleFan: 'P[] * Color * int<RenderLayer> -> unit)>
    (buffer: 'B, points: 'P[], color: Color, [<Struct>] ?layer: int<RenderLayer>) : 'B =
    buffer.AddTriangleFan(points, color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Filled triangle strip. Points array is a backend handle (see LineStrip).</summary>
  [<Extension>]
  static member inline triangleStrip<'B, 'P
    when 'B: (member AddTriangleStrip: 'P[] * Color * int<RenderLayer> -> unit)>
    (buffer: 'B, points: 'P[], color: Color, [<Struct>] ?layer: int<RenderLayer>) : 'B =
    buffer.AddTriangleStrip(points, color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Filled regular polygon.</summary>
  [<Extension>]
  static member inline fillPoly<'B when WithPolygons2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      sides: int,
      radius: float32,
      rotation: float32,
      color: Color,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddFillPoly(
      center,
      sides,
      radius,
      rotation,
      color,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Regular polygon outline.</summary>
  [<Extension>]
  static member inline polyOutline<'B when WithPolygons2D<'B>>
    (
      buffer: 'B,
      center: Vector2,
      sides: int,
      radius: float32,
      rotation: float32,
      color: Color,
      [<Struct>] ?thickness: float32,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddPolyOutline(
      center,
      sides,
      radius,
      rotation,
      color,
      defaultValueArg thickness 1.0f,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  // ──────────────────────────────────────────────
  // Shared — Camera (2D and 3D buffers, same witness names)
  // ──────────────────────────────────────────────

  /// <summary>Begins a camera transform (2D or 3D — the buffer selects the witness).</summary>
  [<Extension>]
  static member inline beginCamera<'B, 'C
    when 'B: (member AddBeginCamera: 'C * int<RenderLayer> -> unit)>
    (buffer: 'B, camera: 'C, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddBeginCamera(camera, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Begins a camera with explicit rendering config (viewport, clear).</summary>
  [<Extension>]
  static member inline beginCameraWith<'B, 'C
    when 'B: (member AddBeginCameraConfig: 'C * int<RenderLayer> -> unit)>
    (buffer: 'B, config: 'C, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddBeginCameraConfig(config, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Ends the current camera transform.</summary>
  [<Extension>]
  static member inline endCamera<'B
    when 'B: (member AddEndCamera: int<RenderLayer> -> unit)>
    (buffer: 'B, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddEndCamera(defaultValueArg layer 0<RenderLayer>)
    buffer

  // ──────────────────────────────────────────────
  // 2D — Shader, Render Target
  // ──────────────────────────────────────────────

  /// <summary>Begins a 2D shader/effect block.</summary>
  [<Extension>]
  static member inline beginShader<'B, 'S
    when 'B: (member AddBeginShader: 'S * int<RenderLayer> -> unit)>
    (buffer: 'B, shader: 'S, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddBeginShader(shader, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Ends the current 2D shader block.</summary>
  [<Extension>]
  static member inline endShader<'B
    when 'B: (member AddEndShader: int<RenderLayer> -> unit)>
    (buffer: 'B, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddEndShader(defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Begins rendering to a render target.</summary>
  [<Extension>]
  static member inline beginTarget<'B, 'T
    when 'B: (member AddBeginTarget: 'T * int<RenderLayer> -> unit)>
    (buffer: 'B, target: 'T, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddBeginTarget(target, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Ends rendering to a render target.</summary>
  [<Extension>]
  static member inline endTarget<'B
    when 'B: (member AddEndTarget: int<RenderLayer> -> unit)>
    (buffer: 'B, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddEndTarget(defaultValueArg layer 0<RenderLayer>)
    buffer

  // ──────────────────────────────────────────────
  // 2D — Render State
  // ──────────────────────────────────────────────

  /// <summary>Sets the blending mode (backend handle: raylib BlendMode enum or MonoGame BlendMode DU).</summary>
  [<Extension>]
  static member inline setBlend<'B, 'M
    when 'B: (member AddSetBlend: 'M * int<RenderLayer> -> unit)>
    (buffer: 'B, mode: 'M, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddSetBlend(mode, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>
  /// Sets the sampler state (e.g. SamplerState.PointClamp) — stops tile-atlas
  /// bleeding. <b>MonoGame only</b>: the raylib buffer has no witness, so
  /// calling this there is a compile error.
  /// </summary>
  [<Extension>]
  static member inline setSamplerState<'B, 'S
    when 'B: (member AddSamplerState: 'S * int<RenderLayer> -> unit)>
    (buffer: 'B, sampler: 'S, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddSamplerState(sampler, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Enables scissor testing.</summary>
  [<Extension>]
  static member inline setScissor<'B
    when 'B: (member AddSetScissor:
      int * int * int * int * int<RenderLayer> -> unit)>
    (
      buffer: 'B,
      x: int,
      y: int,
      w: int,
      h: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddSetScissor(x, y, w, h, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Disables scissor testing.</summary>
  [<Extension>]
  static member inline clearScissor<'B
    when 'B: (member AddClearScissor: int<RenderLayer> -> unit)>
    (buffer: 'B, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddClearScissor(defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Sets the line width for subsequent line draws.</summary>
  [<Extension>]
  static member inline setLineWidth<'B
    when 'B: (member AddSetLineWidth: float32 * int<RenderLayer> -> unit)>
    (buffer: 'B, width: float32, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddSetLineWidth(width, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Sets the viewport rectangle.</summary>
  [<Extension>]
  static member inline setViewport<'B
    when 'B: (member AddSetViewport:
      int * int * int * int * int<RenderLayer> -> unit)>
    (
      buffer: 'B,
      x: int,
      y: int,
      w: int,
      h: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddSetViewport(x, y, w, h, defaultValueArg layer 0<RenderLayer>)
    buffer

  // ──────────────────────────────────────────────
  // Shared — Escape Hatches
  // ──────────────────────────────────────────────

  /// <summary>
  /// Runs a fully-custom draw. 2D: action is <c>unit -&gt; unit</c> (batch is
  /// flushed, state restored). 3D: action receives the frame's SceneContext.
  /// The 3D witness ignores <paramref name="layer"/> (3D has no layers).
  /// </summary>
  [<Extension>]
  static member inline drawImmediate<'B, 'Ctx
    when 'B: (member AddDrawImmediate: ('Ctx -> unit) * int<RenderLayer> -> unit)>
    (
      buffer: 'B,
      [<InlineIfLambda>] action: 'Ctx -> unit,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddDrawImmediate(action, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Clears the current framebuffer to the given color.</summary>
  [<Extension>]
  static member inline clear<'B
    when 'B: (member AddClear: Color * int<RenderLayer> -> unit)>
    (buffer: 'B, color: Color, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddClear(color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>
  /// Enqueues a post-process pass. The action receives the backend's
  /// post-process context (2D or 3D) and runs once after the scene renders.
  /// </summary>
  [<Extension>]
  static member inline postProcess<'B, 'Ctx
    when 'B: (member AddPostProcess: ('Ctx -> unit) -> unit)>
    (buffer: 'B, [<InlineIfLambda>] action: 'Ctx -> unit)
    : 'B =
    buffer.AddPostProcess action
    buffer

  /// <summary>
  /// Enqueues a post-process pass that needs camera-POV scene depth.
  /// <b>3D only</b> — the 2D buffer has no witness.
  /// </summary>
  [<Extension>]
  static member inline postProcessWithDepth<'B, 'Ctx
    when 'B: (member AddPostProcessWithDepth: ('Ctx -> unit) -> unit)>
    (buffer: 'B, [<InlineIfLambda>] action: 'Ctx -> unit)
    : 'B =
    buffer.AddPostProcessWithDepth action
    buffer

  // ──────────────────────────────────────────────
  // 2D — Particles
  // ──────────────────────────────────────────────

  /// <summary>Adds a batched particle render command.</summary>
  [<Extension>]
  static member inline particles<'B, 'T, 'P
    when 'B: (member AddParticles: 'T * 'P[] * int * int<RenderLayer> -> unit)>
    (
      buffer: 'B,
      texture: 'T,
      particles: 'P[],
      count: int,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddParticles(
      texture,
      particles,
      count,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  // ──────────────────────────────────────────────
  // 2D — Lighting (context handle + light records, pass-through)
  // ──────────────────────────────────────────────

  /// <summary>Sets the ambient light color for this frame.</summary>
  [<Extension>]
  static member inline setAmbient<'B, 'C
    when 'B: (member AddSetAmbient: 'C * Color * int<RenderLayer> -> unit)>
    (buffer: 'B, lightCtx: 'C, color: Color, [<Struct>] ?layer: int<RenderLayer>) : 'B =
    buffer.AddSetAmbient(lightCtx, color, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Adds a 2D point light for this frame.</summary>
  [<Extension>]
  static member inline addPointLight<'B, 'C, 'L
    when 'B: (member AddPointLight: 'C * 'L * int<RenderLayer> -> unit)>
    (buffer: 'B, lightCtx: 'C, light: 'L, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddPointLight(lightCtx, light, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Adds a 2D directional light from the backend's light record.</summary>
  [<Extension>]
  static member inline addDirectionalLight<'B, 'C, 'L
    when 'B: (member AddDirectionalLightState:
      'C * 'L * int<RenderLayer> -> unit)>
    (buffer: 'B, lightCtx: 'C, light: 'L, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddDirectionalLightState(
      lightCtx,
      light,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Adds a 2D directional light from parts.</summary>
  [<Extension>]
  static member inline addDirectionalLight<'B, 'C
    when 'B: (member AddDirectionalLight:
      'C * Vector2 * Color * float32 * bool * int<RenderLayer> -> unit)>
    (
      buffer: 'B,
      lightCtx: 'C,
      direction: Vector2,
      color: Color,
      [<Struct>] ?intensity: float32,
      [<Struct>] ?castsShadows: bool,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddDirectionalLight(
      lightCtx,
      direction,
      color,
      defaultValueArg intensity 1.0f,
      defaultValueArg castsShadows false,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Adds a shadow-casting occluder segment for this frame.</summary>
  [<Extension>]
  static member inline addOccluder<'B, 'C, 'O
    when 'B: (member AddOccluder: 'C * 'O * int<RenderLayer> -> unit)>
    (buffer: 'B, lightCtx: 'C, occluder: 'O, [<Struct>] ?layer: int<RenderLayer>) : 'B =
    buffer.AddOccluder(lightCtx, occluder, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Draws a sprite with the current lighting state.</summary>
  [<Extension>]
  static member inline litSprite<'B, 'C, 'S
    when 'B: (member AddLitSprite: 'C * 'S -> unit)>
    (buffer: 'B, lightCtx: 'C, sprite: 'S)
    : 'B =
    buffer.AddLitSprite(lightCtx, sprite)
    buffer

  /// <summary>Draws an animated sprite with the current lighting state.</summary>
  [<Extension>]
  static member inline litAnimatedSprite<'B, 'C, 'R, 'A
    when 'B: (member AddLitAnimatedSprite:
      'C * 'R * 'A * int<RenderLayer> -> unit)>
    (
      buffer: 'B,
      lightCtx: 'C,
      dest: 'R,
      animSprite: 'A,
      [<Struct>] ?layer: int<RenderLayer>
    ) : 'B =
    buffer.AddLitAnimatedSprite(
      lightCtx,
      dest,
      animSprite,
      defaultValueArg layer 0<RenderLayer>
    )

    buffer

  /// <summary>Ends the current lighting pass; sprites after this point are unlit.</summary>
  [<Extension>]
  static member inline endLighting<'B, 'C
    when 'B: (member AddEndLighting: 'C * int<RenderLayer> -> unit)>
    (buffer: 'B, lightCtx: 'C, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddEndLighting(lightCtx, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Enables 2D shadow casting for subsequent draws.</summary>
  [<Extension>]
  static member inline enableShadows<'B, 'C
    when 'B: (member AddEnableShadows: 'C * int<RenderLayer> -> unit)>
    (buffer: 'B, lightCtx: 'C, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddEnableShadows(lightCtx, defaultValueArg layer 0<RenderLayer>)
    buffer

  /// <summary>Disables 2D shadow casting for subsequent draws.</summary>
  [<Extension>]
  static member inline disableShadows<'B, 'C
    when 'B: (member AddDisableShadows: 'C * int<RenderLayer> -> unit)>
    (buffer: 'B, lightCtx: 'C, [<Struct>] ?layer: int<RenderLayer>)
    : 'B =
    buffer.AddDisableShadows(lightCtx, defaultValueArg layer 0<RenderLayer>)
    buffer

  // ──────────────────────────────────────────────
  // 3D — Geometry
  // ──────────────────────────────────────────────

  /// <summary>Draws a mesh (raylib Mesh / MonoGame PrimitiveMesh) with a material.</summary>
  [<Extension>]
  static member inline mesh<'B, 'M, 'X, 'Mat
    when 'B: (member AddDrawMesh: 'M * 'X * 'Mat -> unit)>
    (buffer: 'B, mesh: 'M, transform: 'X, material: 'Mat)
    : 'B =
    buffer.AddDrawMesh(mesh, transform, material)
    buffer

  /// <summary>Draws many instances of the same mesh. Prefer over repeated Mesh calls.</summary>
  [<Extension>]
  static member inline instanced<'B, 'M, 'X, 'Mat
    when 'B: (member AddDrawInstanced: 'M * 'X[] * 'Mat * int -> unit)>
    (buffer: 'B, mesh: 'M, transforms: 'X[], material: 'Mat, instanceCount: int)
    : 'B =
    buffer.AddDrawInstanced(mesh, transforms, material, instanceCount)
    buffer

  /// <summary>Draws a model with a world transform and its authored materials.</summary>
  [<Extension>]
  static member inline model<'B, 'M, 'X
    when 'B: (member AddDrawModel: 'M * 'X -> unit)>
    (buffer: 'B, model: 'M, transform: 'X)
    : 'B =
    buffer.AddDrawModel(model, transform)
    buffer

  /// <summary>Draws a model with a whole-model material override.</summary>
  [<Extension>]
  static member inline modelWith<'B, 'M, 'X, 'Mat
    when 'B: (member AddDrawModelWith: 'M * 'X * 'Mat -> unit)>
    (buffer: 'B, model: 'M, transform: 'X, material: 'Mat)
    : 'B =
    buffer.AddDrawModelWith(model, transform, material)
    buffer

  /// <summary>Draws a model with a per-sub-mesh material resolver.</summary>
  [<Extension>]
  static member inline modelWithPerMesh<'B, 'M, 'X, 'Mat
    when 'B: (member AddDrawModelWithPerMesh: 'M * 'X * (int -> 'Mat) -> unit)>
    (
      buffer: 'B,
      model: 'M,
      transform: 'X,
      [<InlineIfLambda>] resolver: int -> 'Mat
    ) : 'B =
    buffer.AddDrawModelWithPerMesh(model, transform, resolver)
    buffer

  /// <summary>
  /// Draws an animated (skinned) model from the backend's animation state
  /// record. The witness derives the bone palette (MonoGame: from the state;
  /// raylib: applies it to the model).
  /// </summary>
  [<Extension>]
  static member inline animatedModel<'B, 'A, 'X
    when 'B: (member AddAnimatedModel: 'A * 'X -> unit)>
    (buffer: 'B, animModel: 'A, transform: 'X)
    : 'B =
    buffer.AddAnimatedModel(animModel, transform)
    buffer

  /// <summary>Draws an animated model with a whole-model material override.</summary>
  [<Extension>]
  static member inline animatedModelWith<'B, 'A, 'X, 'Mat
    when 'B: (member AddAnimatedModelWith: 'A * 'X * 'Mat -> unit)>
    (buffer: 'B, animModel: 'A, transform: 'X, material: 'Mat)
    : 'B =
    buffer.AddAnimatedModelWith(animModel, transform, material)
    buffer

  /// <summary>Draws an animated model with a per-sub-mesh material resolver.</summary>
  [<Extension>]
  static member inline animatedModelWithPerMesh<'B, 'A, 'X, 'Mat
    when 'B: (member AddAnimatedModelWithPerMesh:
      'A * 'X * (int -> 'Mat) -> unit)>
    (
      buffer: 'B,
      animModel: 'A,
      transform: 'X,
      [<InlineIfLambda>] resolver: int -> 'Mat
    ) : 'B =
    buffer.AddAnimatedModelWithPerMesh(animModel, transform, resolver)
    buffer

  /// <summary>
  /// Draws a skinned mesh with an explicit bone palette and material.
  /// <b>raylib only</b> — MonoGame's skinned path goes through AnimatedModel.
  /// </summary>
  [<Extension>]
  static member inline skinnedMesh<'B, 'M, 'X, 'Mat, 'Bones
    when 'B: (member AddSkinnedMesh: 'M * 'X * 'Mat * 'Bones -> unit)>
    (buffer: 'B, mesh: 'M, transform: 'X, material: 'Mat, bones: 'Bones)
    : 'B =
    buffer.AddSkinnedMesh(mesh, transform, material, bones)
    buffer

  /// <summary>Draws a billboard (camera-facing quad) with a texture.</summary>
  [<Extension>]
  static member inline billboard<'B, 'T
    when 'B: (member AddBillboard: 'T * Vector3 * Vector2 * Color -> unit)>
    (buffer: 'B, texture: 'T, position: Vector3, size: Vector2, color: Color)
    : 'B =
    buffer.AddBillboard(texture, position, size, color)
    buffer

  /// <summary>Draws multiple billboards in a single batch. All arrays are
  /// backend handles (XNA arrays on MonoGame) — no per-frame conversion.</summary>
  [<Extension>]
  static member inline billboardBatch<'B, 'T, 'P, 'S, 'C
    when 'B: (member AddBillboardBatch: 'T[] * 'P[] * 'S[] * 'C[] * int -> unit)>
    (
      buffer: 'B,
      textures: 'T[],
      positions: 'P[],
      sizes: 'S[],
      colors: 'C[],
      count: int
    ) : 'B =
    buffer.AddBillboardBatch(textures, positions, sizes, colors, count)
    buffer

  /// <summary>Draws a 3D line between two points.</summary>
  [<Extension>]
  static member inline line3D<'B
    when 'B: (member AddLine3D: Vector3 * Vector3 * Color -> unit)>
    (buffer: 'B, start: Vector3, finish: Vector3, color: Color)
    : 'B =
    buffer.AddLine3D(start, finish, color)
    buffer

  // ──────────────────────────────────────────────
  // 3D — Shadows & Effect Scopes
  // ──────────────────────────────────────────────

  /// <summary>Sets the shadow origin for this frame's shadow pass.</summary>
  [<Extension>]
  static member inline setShadowOrigin<'B
    when 'B: (member AddSetShadowOrigin: Vector3 -> unit)>
    (buffer: 'B, origin: Vector3)
    : 'B =
    buffer.AddSetShadowOrigin origin
    buffer

  /// <summary>Enables 3D shadow casting for subsequent geometry.</summary>
  [<Extension>]
  static member inline enableShadows<'B
    when 'B: (member AddEnableShadows3D: unit -> unit)>
    (buffer: 'B)
    : 'B =
    buffer.AddEnableShadows3D()
    buffer

  /// <summary>Disables 3D shadow casting for subsequent geometry.</summary>
  [<Extension>]
  static member inline disableShadows<'B
    when 'B: (member AddDisableShadows3D: unit -> unit)>
    (buffer: 'B)
    : 'B =
    buffer.AddDisableShadows3D()
    buffer

  /// <summary>
  /// Opens a per-group shading scope: draws until EndEffect are shaded by
  /// <paramref name="shader"/> instead of the default PBR shader, inheriting
  /// the gathered scene data (camera, lights, shadow pass, bones, time).
  /// </summary>
  [<Extension>]
  static member inline beginEffect<'B, 'S
    when 'B: (member AddBeginEffect: 'S -> unit)>
    (buffer: 'B, shader: 'S)
    : 'B =
    buffer.AddBeginEffect shader
    buffer

  /// <summary>Closes the shading scope opened by BeginEffect.</summary>
  [<Extension>]
  static member inline endEffect<'B when 'B: (member AddEndEffect: unit -> unit)>
    (buffer: 'B)
    : 'B =
    buffer.AddEndEffect()
    buffer

  // ──────────────────────────────────────────────
  // 3D — Lights (backend-neutral Core types)
  // ──────────────────────────────────────────────

  /// <summary>Sets the ambient light for the scene.</summary>
  [<Extension>]
  static member inline setAmbientLight<'B
    when 'B: (member AddSetAmbientLight: AmbientLight3D -> unit)>
    (buffer: 'B, light: AmbientLight3D)
    : 'B =
    buffer.AddSetAmbientLight light
    buffer

  /// <summary>Adds a directional light to the scene.</summary>
  [<Extension>]
  static member inline addDirectionalLight<'B
    when 'B: (member AddDirectionalLight: DirectionalLight3D -> unit)>
    (buffer: 'B, light: DirectionalLight3D)
    : 'B =
    buffer.AddDirectionalLight light
    buffer

  /// <summary>Adds a point light to the scene.</summary>
  [<Extension>]
  static member inline addPointLight<'B
    when 'B: (member AddPointLight: PointLight3D -> unit)>
    (buffer: 'B, light: PointLight3D)
    : 'B =
    buffer.AddPointLight light
    buffer

  /// <summary>Adds a spot light to the scene.</summary>
  [<Extension>]
  static member inline addSpotLight<'B
    when 'B: (member AddSpotLight: SpotLight3D -> unit)>
    (buffer: 'B, light: SpotLight3D)
    : 'B =
    buffer.AddSpotLight light
    buffer

  // ──────────────────────────────────────────────
  // Terminal
  // ──────────────────────────────────────────────

  /// <summary>Terminal function that discards the buffer, silencing the unused-value warning.</summary>
  [<Extension>]
  static member inline drop<'B>(buffer: 'B) : unit = ()
