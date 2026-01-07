namespace Mibo.Elmish

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// A universal Camera definition containing View and Projection matrices.
/// This struct is renderer-agnostic.
[<Struct>]
type Camera = { View: Matrix; Projection: Matrix }

/// Helper functions for 2D Cameras (Orthographic).
module Camera2D =

  /// Calculates the visible world bounds for the camera.
  /// Useful for 2D culling (QuadTree queries, etc).
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

  /// Creates a standard 2D Camera centered on the position.
  /// - position: Center of the camera in World Units.
  /// - zoom: Scale factor (1.0 = pixel perfect).
  /// - viewportSize: The size of the screen/viewport in pixels.
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

  /// Converts a Screen Position (pixels) to World Position using the camera.
  let screenToWorld (camera: Camera) (screenPos: Vector2) : Vector2 =
    let invertedView = Matrix.Invert(camera.View)
    Vector2.Transform(screenPos, invertedView)

  /// Converts a World Position to Screen Position (pixels).
  let worldToScreen (camera: Camera) (worldPos: Vector2) : Vector2 =
    Vector2.Transform(worldPos, camera.View)


/// Helper functions for 3D Cameras (Perspective).
module Camera3D =

  /// Creates a standard LookAt Camera.
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

  /// Creates an Orbiting Camera (spherical coordinates).
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

  /// Creates a generic Ray from Screen Coordinates (Mouse Picking).
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

  /// Calculates the BoundingFrustum for the camera.
  /// Useful for 3D culling (Octree queries, sphere checks).
  let boundingFrustum(camera: Camera) : BoundingFrustum =
    BoundingFrustum(camera.View * camera.Projection)
