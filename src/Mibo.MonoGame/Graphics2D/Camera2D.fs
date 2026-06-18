namespace Mibo.Elmish

open Microsoft.Xna.Framework

/// <summary>
/// A 2D camera definition for the MonoGame backend.
/// Produces a transform matrix for <c>SpriteBatch.Begin</c>.
/// </summary>
/// <remarks>
/// Mirrors the raylib <c>Camera2D</c> concept: position, zoom, rotation, and origin.
/// Use <see cref="M:Mibo.Elmish.Camera2D.create"/> to construct one and
/// <see cref="M:Mibo.Elmish.Camera2D.toMatrix"/> to get the <c>SpriteBatch</c> transform.
/// </remarks>
[<Struct>]
type Camera2D = {
  /// <summary>World position the camera is centered on.</summary>
  Position: Vector2

  /// <summary>Zoom factor (1.0 = no zoom, &gt;1 = zoom in, &lt;1 = zoom out).</summary>
  Zoom: float32

  /// <summary>Rotation in radians around the origin.</summary>
  Rotation: float32

  /// <summary>
  /// The point on screen where the camera is anchored (typically viewport center).
  /// </summary>
  Origin: Vector2
}

/// <summary>Constructors and utilities for <see cref="T:Mibo.Elmish.Camera2D"/>.</summary>
module Camera2D =

  /// <summary>
  /// Creates a <see cref="T:Mibo.Elmish.Camera2D"/> centered on the given position.
  /// </summary>
  /// <param name="position">World position the camera follows.</param>
  /// <param name="zoom">Zoom factor (1.0 = default).</param>
  /// <param name="viewportSize">Size of the viewport in pixels (used to compute origin).</param>
  let create
    (position: Vector2)
    (zoom: float32)
    (viewportSize: Vector2)
    : Camera2D =
    {
      Position = position
      Zoom = zoom
      Rotation = 0.0f
      Origin = Vector2(viewportSize.X * 0.5f, viewportSize.Y * 0.5f)
    }

  /// <summary>
  /// Computes the <c>SpriteBatch</c> transform matrix for this camera.
  /// </summary>
  /// <remarks>
  /// The matrix applies: translate by -position, rotate, scale by zoom,
  /// then translate by origin. Pass this to <c>SpriteBatch.Begin(transformMatrix=...)</c>.
  /// </remarks>
  let toMatrix(c: Camera2D) : Matrix =
    Matrix.CreateTranslation(-c.Position.X, -c.Position.Y, 0.0f)
    * Matrix.CreateRotationZ(c.Rotation)
    * Matrix.CreateScale(c.Zoom)
    * Matrix.CreateTranslation(c.Origin.X, c.Origin.Y, 0.0f)

/// <summary>
/// Camera rendering configuration for 2D multi-camera support.
/// MonoGame analogue of the raylib-side <c>Camera2DConfig</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the raylib version, <c>Viewport</c> is expressed in **pixels**
/// as a <see cref="T:Microsoft.Xna.Framework.Rectangle"/>, since MonoGame’s
/// <c>GraphicsDevice.Viewport</c> and scissor rectangles are pixel-based.
/// <c>ValueNone</c> means fullscreen (no custom viewport).
/// </para>
/// <para>
/// <c>ClearColor</c> doubles as the clear signal:
/// <c>ValueNone</c> = don’t clear (overlay on existing content),
/// <c>ValueSome color</c> = clear with this color before rendering.
/// </para>
/// </remarks>
[<Struct>]
type Camera2DConfig = {
  /// <summary>The MonoGame 2D camera for rendering.</summary>
  Camera: Camera2D
  /// <summary>Viewport in pixel coordinates. ValueNone = fullscreen.</summary>
  Viewport: Rectangle voption
  /// <summary>Clear color before rendering. ValueNone = don’t clear.</summary>
  ClearColor: Color voption
}

/// <summary>Convenience values and modifiers for <see cref="T:Mibo.Elmish.Camera2DConfig"/>.</summary>
module Camera2DConfig =

  /// <summary>Default configuration: fullscreen, no clear.</summary>
  let defaults: Camera2DConfig = {
    Camera = Camera2D.create Vector2.Zero 1.0f (Vector2(800.0f, 600.0f))
    Viewport = ValueNone
    ClearColor = ValueNone
  }

  /// <summary>Sets the pixel viewport.</summary>
  let withViewport
    (viewport: Rectangle)
    (config: Camera2DConfig)
    : Camera2DConfig =
    {
      config with
          Viewport = ValueSome viewport
    }

  /// <summary>Sets the clear color.</summary>
  let withClearColor (color: Color) (config: Camera2DConfig) : Camera2DConfig = {
    config with
        ClearColor = ValueSome color
  }

  /// <summary>Disables clearing.</summary>
  let noClear(config: Camera2DConfig) : Camera2DConfig = {
    config with
        ClearColor = ValueNone
  }
