namespace Mibo.Elmish

open System
open Microsoft.Xna.Framework

// ─────────────────────────────────────────────────────────────
// 2D Camera
// ─────────────────────────────────────────────────────────────

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

/// <summary>
/// Camera rendering configuration for 2D multi-camera support.
/// MonoGame analogue of the raylib-side <c>Camera2DConfig</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the raylib version, <c>Viewport</c> is expressed in **pixels**
/// as a <see cref="T:Microsoft.Xna.Framework.Rectangle"/>, since MonoGame's
/// <c>GraphicsDevice.Viewport</c> and scissor rectangles are pixel-based.
/// <c>ValueNone</c> means fullscreen (no custom viewport).
/// </para>
/// <para>
/// <c>ClearColor</c> doubles as the clear signal:
/// <c>ValueNone</c> = don't clear (overlay on existing content),
/// <c>ValueSome color</c> = clear with this color before rendering.
/// </para>
/// </remarks>
[<Struct>]
type Camera2DConfig = {
  /// <summary>The MonoGame 2D camera for rendering.</summary>
  Camera: Camera2D
  /// <summary>Viewport in pixel coordinates. ValueNone = fullscreen.</summary>
  Viewport: Rectangle voption
  /// <summary>Clear color before rendering. ValueNone = don't clear.</summary>
  ClearColor: Color voption
}

/// <summary>
/// Helper functions for 2D Cameras (Orthographic projection).
/// </summary>
/// <remarks>
/// Use these for top-down, side-scrolling, or any 2D game rendering.
/// The camera struct's fields are immutable, so the movement helpers
/// (<c>smoothFollow</c> / <c>clampTarget</c>) return a new camera rather than
/// mutating in place.
/// </remarks>
module Camera2D =

  /// <summary>
  /// Creates a <see cref="T:Mibo.Elmish.Camera2D"/> centered on the given position.
  /// </summary>
  /// <param name="position">World position the camera follows.</param>
  /// <param name="zoom">Zoom factor (1.0 = default).</param>
  /// <param name="viewportSize">Size of the viewport in pixels (used to compute origin).</param>
  let inline create
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
  let inline toMatrix(c: Camera2D) : Matrix =
    Matrix.CreateTranslation(-c.Position.X, -c.Position.Y, 0.0f)
    * Matrix.CreateRotationZ(c.Rotation)
    * Matrix.CreateScale(c.Zoom)
    * Matrix.CreateTranslation(c.Origin.X, c.Origin.Y, 0.0f)

  /// <summary>Calculates the visible world bounds for a MonoGame <see cref="T:Mibo.Elmish.Camera2D"/>.</summary>
  /// <remarks>The result is a pixel <c>Rectangle</c> (MonoGame's <c>Rectangle</c> is int-based).</remarks>
  let inline viewportBounds
    (camera: Camera2D)
    (width: float32)
    (height: float32)
    : Rectangle =
    let visibleW = width / camera.Zoom
    let visibleH = height / camera.Zoom
    let halfW = visibleW * 0.5f
    let halfH = visibleH * 0.5f

    Rectangle(
      int(camera.Position.X - halfW),
      int(camera.Position.Y - halfH),
      int visibleW,
      int visibleH
    )

  /// <summary>Converts a screen position (pixels) to world position.</summary>
  let inline screenToWorld (camera: Camera2D) (screenPos: Vector2) : Vector2 =
    let mutable m = toMatrix camera
    let mutable inv = Matrix()
    Matrix.Invert(&m, &inv) |> ignore
    Vector2.Transform(screenPos, inv)

  /// <summary>Converts a world position to screen position (pixels).</summary>
  let inline worldToScreen (camera: Camera2D) (worldPos: Vector2) : Vector2 =
    Vector2.Transform(worldPos, toMatrix camera)

  /// <summary>
  /// Smoothly interpolate the camera position toward a world position, returning a new camera.
  /// </summary>
  let inline smoothFollow
    (camera: Camera2D)
    (target: Vector2)
    (speed: float32)
    : Camera2D =
    {
      camera with
          Position =
            Vector2(
              camera.Position.X + (target.X - camera.Position.X) * speed,
              camera.Position.Y + (target.Y - camera.Position.Y) * speed
            )
    }

  /// <summary>
  /// Clamp the camera position to a world bounds rectangle, returning a new camera.
  /// </summary>
  let inline clampTarget
    (camera: Camera2D)
    (minX: float32)
    (minY: float32)
    (maxX: float32)
    (maxY: float32)
    : Camera2D =
    {
      camera with
          Position =
            Vector2(
              MathF.Max(minX, MathF.Min(camera.Position.X, maxX)),
              MathF.Max(minY, MathF.Min(camera.Position.Y, maxY))
            )
    }

  // ── Rendering Config Builders ──

  /// <summary>
  /// Create a rendering config from a 2D camera.
  /// Defaults: fullscreen, no clear.
  /// </summary>
  let inline render(camera: Camera2D) : Camera2DConfig = {
    Camera = camera
    Viewport = ValueNone
    ClearColor = ValueNone
  }

  /// <summary>Set viewport in pixel coordinates.</summary>
  let inline withViewport
    (viewport: Rectangle)
    (config: Camera2DConfig)
    : Camera2DConfig =
    {
      config with
          Viewport = ValueSome viewport
    }

  /// <summary>Clear with this color before rendering.</summary>
  let inline withClear
    (color: Color)
    (config: Camera2DConfig)
    : Camera2DConfig =
    {
      config with
          ClearColor = ValueSome color
    }

  /// <summary>Split-screen left half. Clears with given color.</summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenLeft
    (camera: Camera2D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera2DConfig =
    let halfWidth = bounds.Width / 2

    render camera
    |> withViewport(Rectangle(bounds.X, bounds.Y, halfWidth, bounds.Height))
    |> withClear clearColor

  /// <summary>Split-screen right half. Clears with given color.</summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenRight
    (camera: Camera2D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera2DConfig =
    let halfWidth = bounds.Width / 2

    render camera
    |> withViewport(
      Rectangle(bounds.X + halfWidth, bounds.Y, halfWidth, bounds.Height)
    )
    |> withClear clearColor

  /// <summary>Split-screen top half. Clears with given color.</summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenTop
    (camera: Camera2D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera2DConfig =
    let halfHeight = bounds.Height / 2

    render camera
    |> withViewport(Rectangle(bounds.X, bounds.Y, bounds.Width, halfHeight))
    |> withClear clearColor

  /// <summary>Split-screen bottom half. Clears with given color.</summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenBottom
    (camera: Camera2D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera2DConfig =
    let halfHeight = bounds.Height / 2

    render camera
    |> withViewport(
      Rectangle(bounds.X, bounds.Y + halfHeight, bounds.Width, halfHeight)
    )
    |> withClear clearColor

// ─────────────────────────────────────────────────────────────
// 3D Camera
// ─────────────────────────────────────────────────────────────

/// <summary>
/// A universal Camera definition containing View and Projection matrices.
/// </summary>
/// <remarks>
/// This struct is renderer-agnostic — both 2D and 3D renderers use the same concept.
/// It is a struct (not a reference record) because it flows through the view function
/// every frame; keeping it stack-allocated avoids per-frame Gen0 pressure on the hot path.
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
[<Struct>]
type Camera = {
  /// <summary>The view matrix (camera position/rotation, transforms world to view space).</summary>
  View: Matrix
  /// <summary>The projection matrix (perspective/orthographic, transforms view to clip space).</summary>
  Projection: Matrix
}

/// <summary>Camera projection mode.</summary>
[<RequireQualifiedAccess>]
type CameraProjection =
  | Perspective
  | Orthographic

/// <summary>
/// 3D camera definition for the MonoGame backend.
/// </summary>
/// <remarks>
/// Position, target, and up define the view transform.
/// FovY, near/far planes, and projection mode define the projection transform.
/// Use <see cref="M:Mibo.Elmish.Camera3D"/> builders to construct one.
/// </remarks>
[<Struct>]
type Camera3D = {
  /// <summary>Camera position in world space.</summary>
  Position: Vector3
  /// <summary>Point the camera is looking at.</summary>
  Target: Vector3
  /// <summary>Up vector (typically <c>Vector3.Up</c>).</summary>
  Up: Vector3
  /// <summary>Vertical field of view in radians (for perspective) or height in world units (for orthographic).</summary>
  FovY: float32
  /// <summary>Near clipping plane distance.</summary>
  NearPlane: float32
  /// <summary>Far clipping plane distance.</summary>
  FarPlane: float32
  /// <summary>Projection mode.</summary>
  Projection: CameraProjection
}

/// <summary>
/// Represents a 3D ray with an origin position and normalized direction.
/// </summary>
[<Struct>]
type Ray = {
  /// <summary>Origin of the ray in world space.</summary>
  Position: Vector3
  /// <summary>Normalized direction vector.</summary>
  Direction: Vector3
}

/// <summary>
/// Camera rendering configuration for 3D pipelines.
/// MonoGame analogue of the raylib-side <c>Camera3DConfig</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Viewport</c> is expressed in **pixels** as a <see cref="T:Microsoft.Xna.Framework.Rectangle"/>,
/// since MonoGame's <c>GraphicsDevice.Viewport</c> is pixel-based.
/// <c>ValueNone</c> means fullscreen (no custom viewport).
/// </para>
/// <para>
/// <c>ClearColor</c> doubles as the clear signal:
/// <c>ValueNone</c> = don't clear (overlay on existing content),
/// <c>ValueSome color</c> = clear with this color before rendering.
/// </para>
/// </remarks>
[<Struct>]
type Camera3DConfig = {
  /// <summary>The MonoGame 3D camera for rendering.</summary>
  Camera: Camera3D
  /// <summary>Viewport in pixel coordinates. ValueNone = fullscreen.</summary>
  Viewport: Rectangle voption
  /// <summary>Clear color before rendering. ValueNone = don't clear.</summary>
  ClearColor: Color voption
}

/// <summary>
/// Helper functions for 3D Cameras (Perspective / Orthographic projection).
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
  /// <param name="up">Up vector (typically Vector3.UnitY)</param>
  /// <param name="fov">Field of view in radians (e.g., MathF.PI / 4.0f)</param>
  /// <param name="aspectRatio">Width / Height of the viewport</param>
  /// <param name="nearPlane">Near clipping distance (objects closer are not rendered)</param>
  /// <param name="farPlane">Far clipping distance (objects farther are not rendered)</param>
  /// <example>
  /// <code>
  /// let camera = Camera3D.lookAt
  ///     (Vector3(0f, 10f, 20f))  // position
  ///     Vector3.Zero              // target
  ///     Vector3.Up                // up
  ///     (MathF.PI / 4.0f)        // 45° FOV
  ///     (16f / 9f)                // aspect ratio
  ///     0.1f                      // near plane
  ///     1000f                     // far plane
  /// </code>
  /// </example>
  let inline lookAt
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
  /// Creates an orthographic camera that looks at a target from a position.
  /// </summary>
  /// <param name="position">Camera position in world space</param>
  /// <param name="target">Point the camera is looking at</param>
  /// <param name="up">Up vector (typically Vector3.UnitY)</param>
  /// <param name="width">Width of the orthographic view volume in world units</param>
  /// <param name="height">Height of the orthographic view volume in world units</param>
  /// <param name="nearPlane">Near clipping distance</param>
  /// <param name="farPlane">Far clipping distance</param>
  let inline orthographic
    (position: Vector3)
    (target: Vector3)
    (up: Vector3)
    (width: float32)
    (height: float32)
    (nearPlane: float32)
    (farPlane: float32)
    : Camera =
    {
      View = Matrix.CreateLookAt(position, target, up)
      Projection = Matrix.CreateOrthographic(width, height, nearPlane, farPlane)
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
  let inline orbit
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
        radius * MathF.Sin(yaw) * MathF.Cos(pitch),
        radius * MathF.Sin(pitch),
        radius * MathF.Cos(yaw) * MathF.Cos(pitch)
      )
      + target

    lookAt position target Vector3.UnitY fov aspect near far

  /// <summary>
  /// Creates a ray from screen coordinates for mouse/touch picking.
  /// </summary>
  /// <remarks>
  /// The ray originates at the camera's near plane at the screen position
  /// and points into the scene.
  /// </remarks>
  /// <param name="camera">The camera to compute the ray for.</param>
  /// <param name="screenPos">The screen position in pixels.</param>
  /// <param name="viewportWidth">Viewport width in pixels.</param>
  /// <param name="viewportHeight">Viewport height in pixels.</param>
  let screenPointToRay
    (camera: Camera)
    (screenPos: Vector2)
    (viewportWidth: float32)
    (viewportHeight: float32)
    : Ray =
    let mutable viewProj = camera.View * camera.Projection
    let mutable invertedViewProj = Matrix()
    Matrix.Invert(&viewProj, &invertedViewProj)


    let nx = 2.0f * screenPos.X / viewportWidth - 1.0f
    let ny = 1.0f - 2.0f * screenPos.Y / viewportHeight

    let nearClip = Vector4(nx, ny, 0.0f, 1.0f)
    let farClip = Vector4(nx, ny, 1.0f, 1.0f)

    let nearWorld = Vector4.Transform(nearClip, invertedViewProj)
    let farWorld = Vector4.Transform(farClip, invertedViewProj)

    let nearPos = Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z) / nearWorld.W

    let farPos = Vector3(farWorld.X, farWorld.Y, farWorld.Z) / farWorld.W

    let direction = Vector3.Normalize(farPos - nearPos)

    {
      Position = nearPos
      Direction = direction
    }

  // ── Rendering Config Builders ──

  /// <summary>
  /// Create a rendering config from a MonoGame 3D camera.
  /// Defaults: fullscreen, no clear.
  /// </summary>
  let inline render(camera: Camera3D) : Camera3DConfig = {
    Camera = camera
    Viewport = ValueNone
    ClearColor = ValueNone
  }

  /// <summary>Set viewport in pixel coordinates.</summary>
  let inline withViewport
    (viewport: Rectangle)
    (config: Camera3DConfig)
    : Camera3DConfig =
    {
      config with
          Viewport = ValueSome viewport
    }

  /// <summary>Clear with this color before rendering.</summary>
  let inline withClear
    (color: Color)
    (config: Camera3DConfig)
    : Camera3DConfig =
    {
      config with
          ClearColor = ValueSome color
    }

  // ── Convenience Constructors ──

  /// <summary>
  /// Split-screen left half. Clears with given color.
  /// </summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenLeft
    (camera: Camera3D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera3DConfig =
    let halfWidth = bounds.Width / 2

    render camera
    |> withViewport(Rectangle(bounds.X, bounds.Y, halfWidth, bounds.Height))
    |> withClear clearColor

  /// <summary>
  /// Split-screen right half. Clears with given color.
  /// </summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenRight
    (camera: Camera3D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera3DConfig =
    let halfWidth = bounds.Width / 2

    render camera
    |> withViewport(
      Rectangle(bounds.X + halfWidth, bounds.Y, halfWidth, bounds.Height)
    )
    |> withClear clearColor

  /// <summary>
  /// Split-screen top half. Clears with given color.
  /// </summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenTop
    (camera: Camera3D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera3DConfig =
    let halfHeight = bounds.Height / 2

    render camera
    |> withViewport(Rectangle(bounds.X, bounds.Y, bounds.Width, halfHeight))
    |> withClear clearColor

  /// <summary>
  /// Split-screen bottom half. Clears with given color.
  /// </summary>
  /// <param name="bounds">Parent viewport bounds in pixels (typically the window size).</param>
  let inline splitScreenBottom
    (camera: Camera3D)
    (clearColor: Color)
    (bounds: Rectangle)
    : Camera3DConfig =
    let halfHeight = bounds.Height / 2

    render camera
    |> withViewport(
      Rectangle(bounds.X, bounds.Y + halfHeight, bounds.Width, halfHeight)
    )
    |> withClear clearColor
