namespace Mibo.Elmish

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// A universal Camera definition containing View and Projection matrices.
/// </summary>
/// <remarks>
/// This struct is renderer-agnostic - both 2D and 3D renderers use the same type.
/// Use the <see cref="T:Mibo.Elmish.Camera2D"/> or <see cref="T:Mibo.Elmish.Camera3D"/> modules to create cameras.
/// </remarks>
/// <example>
/// <code>
/// // 2D camera centered on player
/// let camera = Camera2D.create playerPos 1.0f viewportSize
///
/// // 3D camera looking at origin
/// let camera = Camera3D.lookAt position Vector3.Zero Vector3.Up fov aspect 0.1f 1000f
/// </code>
/// </example>
type Camera = {
  /// The view matrix (camera position/rotation, transforms world to view space).
  View: Matrix
  /// The projection matrix (perspective/orthographic, transforms view to clip space).
  Projection: Matrix
}

/// <summary>
/// Helper functions for 2D Cameras (Orthographic projection).
/// </summary>
/// <remarks>
/// Use these for top-down, side-scrolling, or any 2D game rendering.
/// </remarks>
module Camera2D =

  /// <summary>Calculates the visible world bounds for the camera.</summary>
  /// <remarks>Useful for 2D culling (QuadTree queries, sprite visibility checks). Returns a <see cref="T:Microsoft.Xna.Framework.Rectangle"/> in world coordinates covering the visible area.</remarks>
  let viewportBounds (camera: Camera) (viewport: Viewport) : Rectangle =
    let inverseView = Matrix.Invert(camera.View)
    let tl = Vector2.Transform(Vector2.Zero, inverseView)

    let br =
      Vector2.Transform(
        Vector2(float32 viewport.Width, float32 viewport.Height),
        inverseView
      )

    // Handle rotation/scale making tl/br not min/max
    let minX = min tl.X br.X
    let maxX = max tl.X br.X
    let minY = min tl.Y br.Y
    let maxY = max tl.Y br.Y

    Rectangle(int minX, int minY, int(maxX - minX), int(maxY - minY))

  /// <summary>
  /// Creates a standard 2D Camera centered on the position.
  /// </summary>
  /// <param name="position">Center of the camera in world units</param>
  /// <param name="zoom">Scale factor (1.0 = pixel perfect, 2.0 = 2x zoom in)</param>
  /// <param name="viewportSize">The size of the screen/viewport in pixels</param>
  /// <example>
  /// <code>
  /// let camera = Camera2D.create playerPosition 1.0f (Point(800, 600))
  /// </code>
  /// </example>
  let create
    (position: Vector2)
    (zoom: float32)
    (viewportSize: Point)
    : Camera =
    let vpW = float32 viewportSize.X
    let vpH = float32 viewportSize.Y

    // Transform: Translate to origin (0,0) -> Scale -> Translate to Screen Center
    let view =
      Matrix.CreateTranslation(float32 -position.X, float32 -position.Y, 0.0f)
      * Matrix.CreateScale(zoom, zoom, 1.0f)
      * Matrix.CreateTranslation(vpW * 0.5f, vpH * 0.5f, 0.0f)

    let projection =
      Matrix.CreateOrthographicOffCenter(0.0f, vpW, vpH, 0.0f, 0.0f, 1.0f)

    { View = view; Projection = projection }

  /// Converts a screen position (pixels) to world position using the camera.
  ///
  /// Useful for mouse picking in 2D games.
  let screenToWorld (camera: Camera) (screenPos: Vector2) : Vector2 =
    let invertedView = Matrix.Invert(camera.View)
    Vector2.Transform(screenPos, invertedView)

  /// Converts a world position to screen position (pixels).
  let worldToScreen (camera: Camera) (worldPos: Vector2) : Vector2 =
    Vector2.Transform(worldPos, camera.View)


/// <summary>
/// Helper functions for 3D Cameras (Perspective projection).
/// </summary>
/// <remarks>
/// Use these for first-person, third-person, or any 3D game rendering.
/// </remarks>
module Camera3D =

  /// <summary>
  /// Creates a camera that looks at a target from a position.
  /// </summary>
  /// <param name="position">Camera position in world space</param>
  /// <param name="target">Point the camera is looking at</param>
  /// <param name="up">Up vector (typically Vector3.Up)</param>
  /// <param name="fov">Field of view in radians (e.g., MathHelper.PiOver4)</param>
  /// <param name="aspectRatio">Width / Height of the viewport</param>
  /// <param name="nearPlane">Near clipping distance (objects closer are not rendered)</param>
  /// <param name="farPlane">Far clipping distance (objects farther are not rendered)</param>
  /// <example>
  /// <code>
  /// let camera = Camera3D.lookAt
  ///     (Vector3(0f, 10f, 20f))  // position
  ///     Vector3.Zero              // target
  ///     Vector3.Up                // up
  ///     MathHelper.PiOver4        // 45° FOV
  ///     (16f / 9f)                // aspect ratio
  ///     0.1f                      // near plane
  ///     1000f                     // far plane
  /// </code>
  /// </example>
  let lookAt
    (position: Vector3)
    (target: Vector3)
    (up: Vector3)
    (fov: float32)
    (aspectRatio: float32)
    (nearPlane: float32)
    (farPlane: float32)
    : Camera =
    {
      View = Matrix.CreateLookAt(position, target, up)
      Projection =
        Matrix.CreatePerspectiveFieldOfView(
          fov,
          aspectRatio,
          nearPlane,
          farPlane
        )
    }

  /// <summary>
  /// Creates an orbiting camera using spherical coordinates.
  /// </summary>
  /// <remarks>
  /// Useful for third-person cameras, inspection views, or editor cameras.
  /// </remarks>
  /// <param name="target">Point the camera orbits around</param>
  /// <param name="yaw">Horizontal rotation angle in radians</param>
  /// <param name="pitch">Vertical rotation angle in radians</param>
  /// <param name="radius">Distance from target</param>
  /// <param name="fov">Field of view in radians</param>
  /// <param name="aspect">Aspect ratio</param>
  /// <param name="near">Near plane</param>
  /// <param name="far">Far plane</param>
  let orbit
    (target: Vector3)
    (yaw: float32)
    (pitch: float32)
    (radius: float32)
    (fov: float32)
    (aspect: float32)
    (near: float32)
    (far: float32)
    : Camera =
    let position =
      Vector3(
        radius * sin(yaw) * cos(pitch),
        radius * sin(pitch),
        radius * cos(yaw) * cos(pitch)
      )
      + target

    lookAt position target Vector3.Up fov aspect near far

  /// <summary>
  /// Creates a ray from screen coordinates for mouse/touch picking.
  /// </summary>
  /// <remarks>
  /// The ray originates at the camera's near plane at the screen position
  /// and points into the scene. Use <see cref="T:Microsoft.Xna.Framework.Ray"/>.Intersects to test against geometry.
  /// </remarks>
  /// <example>
  /// <code>
  /// let ray = Camera3D.screenPointToRay camera mousePos viewport
  /// if ray.Intersects(boundingBox).HasValue then
  ///     // Mouse is hovering over object
  /// </code>
  /// </example>
  let screenPointToRay
    (camera: Camera)
    (screenPos: Vector2)
    (viewport: Viewport)
    : Ray =
    let nearPoint = Vector3(screenPos.X, screenPos.Y, 0.0f)
    let farPoint = Vector3(screenPos.X, screenPos.Y, 1.0f)

    let nearSource =
      viewport.Unproject(
        nearPoint,
        camera.Projection,
        camera.View,
        Matrix.Identity
      )

    let farSource =
      viewport.Unproject(
        farPoint,
        camera.Projection,
        camera.View,
        Matrix.Identity
      )

    let direction = farSource - nearSource
    direction.Normalize()

    Ray(nearSource, direction)

  /// <summary>
  /// Calculates the <see cref="T:Microsoft.Xna.Framework.BoundingFrustum"/> for the camera.
  /// </summary>
  /// <remarks>
  /// The frustum represents the visible volume of the camera. Useful for
  /// 3D culling (octree queries, sphere/box visibility checks).
  /// </remarks>
  /// <example>
  /// <code>
  /// let frustum = Camera3D.boundingFrustum camera
  /// if Culling.isVisible frustum entitySphere then
  ///     // Entity is visible, render it
  /// </code>
  /// </example>
  let boundingFrustum(camera: Camera) : BoundingFrustum =
    BoundingFrustum(camera.View * camera.Projection)
