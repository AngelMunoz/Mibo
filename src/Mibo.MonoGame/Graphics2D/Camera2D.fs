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
