namespace Mibo.Elmish

open System
open System.Numerics
open Raylib_cs


/// <summary>
/// Camera rendering configuration for 2D multi-camera support.
/// </summary>
/// <remarks>
/// <para>
/// Controls how a camera renders: viewport bounds and clear behavior.
/// Construct via <see cref="M:Mibo.Elmish.Camera2D.render"/> and the <c>with*</c> modifiers.
/// </para>
/// <para>
/// <c>ClearColor</c> doubles as the clear signal:
/// <c>ValueNone</c> = don't clear (overlay on existing content),
/// <c>ValueSome color</c> = clear with this color before rendering.
/// </para>
/// </remarks>
[<Struct>]
type Camera2DConfig = {
  /// <summary>The raylib 2D camera for rendering.</summary>
  Camera: Raylib_cs.Camera2D
  /// <summary>Viewport in normalized screen coordinates (0-1). ValueNone = fullscreen.</summary>
  Viewport: Raylib_cs.Rectangle voption
  /// <summary>Clear color before rendering. ValueNone = don't clear. ValueSome = clear with this color.</summary>
  ClearColor: Color voption
}

/// <summary>
/// Helper functions for 2D Cameras (Orthographic projection).
/// </summary>
/// <remarks>
/// <para>Use these for top-down, side-scrolling, or any 2D game rendering.</para>
/// <para>
/// The raylib <c>Camera2D</c> is a native mutable struct. The read-only helpers
/// (<c>viewportBounds</c>/<c>screenToWorld</c>/<c>worldToScreen</c>) take it by <c>inref</c>
/// (no copy, use <c>&amp;camera</c> at the call site), the movement helpers
/// (<c>smoothFollow</c>/<c>clampTarget</c>) take it by <c>byref</c> and mutate it in place,
/// and the config builders take it by value (the camera is stored in the config regardless).
/// </para>
/// </remarks>
module Camera2D =

  /// <summary>Calculates the visible world bounds for a raylib Camera2D.</summary>
  /// <param name="camera">Passed by read-only reference (<c>&amp;camera</c>) to avoid copying the native struct.</param>
  let inline viewportBounds
    (camera: inref<Raylib_cs.Camera2D>)
    (width: float32)
    (height: float32)
    : Raylib_cs.Rectangle =
    let visibleW = width / camera.Zoom
    let visibleH = height / camera.Zoom
    let halfW = visibleW * 0.5f
    let halfH = visibleH * 0.5f

    Raylib_cs.Rectangle(
      camera.Target.X - halfW,
      camera.Target.Y - halfH,
      visibleW,
      visibleH
    )

  /// <summary>
  /// Creates a raylib <c>Camera2D</c> centered on the given position.
  /// </summary>
  let inline create
    (position: Vector2)
    (zoom: float32)
    (viewportSize: Vector2)
    : Raylib_cs.Camera2D =
    Raylib_cs.Camera2D(
      Vector2(viewportSize.X * 0.5f, viewportSize.Y * 0.5f),
      position,
      0.0f,
      zoom
    )

  /// <summary>Converts a screen position (pixels) to world position.</summary>
  /// <param name="camera">Passed by read-only reference (<c>&amp;camera</c>) to avoid copying the native struct.</param>
  let inline screenToWorld
    (camera: inref<Raylib_cs.Camera2D>)
    (screenPos: Vector2)
    : Vector2 =
    Raylib.GetScreenToWorld2D(screenPos, camera)

  /// <summary>Converts a world position to screen position (pixels).</summary>
  /// <param name="camera">Passed by read-only reference (<c>&amp;camera</c>) to avoid copying the native struct.</param>
  let inline worldToScreen
    (camera: inref<Raylib_cs.Camera2D>)
    (worldPos: Vector2)
    : Vector2 =
    Raylib.GetWorldToScreen2D(worldPos, camera)

  /// <summary>
  /// Smoothly interpolate the camera target toward a world position.
  /// </summary>
  /// <param name="camera">Passed by reference (<c>&amp;camera</c>) so mutations are visible to the caller.</param>
  let inline smoothFollow
    (camera: byref<Raylib_cs.Camera2D>)
    (target: Vector2)
    (speed: float32)
    =
    camera.Target.X <- camera.Target.X + (target.X - camera.Target.X) * speed
    camera.Target.Y <- camera.Target.Y + (target.Y - camera.Target.Y) * speed

  /// <summary>
  /// Clamp the camera target to a world bounds rectangle.
  /// </summary>
  /// <param name="camera">Passed by reference (<c>&amp;camera</c>) so mutations are visible to the caller.</param>
  let inline clampTarget
    (camera: byref<Raylib_cs.Camera2D>)
    (minX: float32)
    (minY: float32)
    (maxX: float32)
    (maxY: float32)
    =
    camera.Target.X <- MathF.Max(minX, MathF.Min(camera.Target.X, maxX))
    camera.Target.Y <- MathF.Max(minY, MathF.Min(camera.Target.Y, maxY))

  // ── Rendering Config Builders ──

  /// <summary>
  /// Create a rendering config from a 2D camera.
  /// Defaults: fullscreen, no clear.
  /// </summary>
  let inline render(camera: Raylib_cs.Camera2D) : Camera2DConfig = {
    Camera = camera
    Viewport = ValueNone
    ClearColor = ValueNone
  }

  /// <summary>Set viewport in normalized screen coordinates (0-1).</summary>
  let inline withViewport
    (viewport: Raylib_cs.Rectangle)
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
  let inline splitScreenLeft
    (camera: Raylib_cs.Camera2D)
    (clearColor: Color)
    : Camera2DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen right half. Clears with given color.</summary>
  let inline splitScreenRight
    (camera: Raylib_cs.Camera2D)
    (clearColor: Color)
    : Camera2DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.5f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen top half. Clears with given color.</summary>
  let inline splitScreenTop
    (camera: Raylib_cs.Camera2D)
    (clearColor: Color)
    : Camera2DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 1.0f, 0.5f))
    |> withClear clearColor

  /// <summary>Split-screen bottom half. Clears with given color.</summary>
  let inline splitScreenBottom
    (camera: Raylib_cs.Camera2D)
    (clearColor: Color)
    : Camera2DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.5f, 1.0f, 0.5f))
    |> withClear clearColor


/// <summary>
/// Camera rendering configuration for 3D pipelines.
/// </summary>
/// <remarks>
/// <para>
/// Controls how a camera renders: viewport bounds and clear behavior.
/// Construct via <see cref="M:Mibo.Elmish.Camera3D.render"/> and the <c>with*</c> modifiers.
/// </para>
/// <para>
/// <c>ClearColor</c> doubles as the clear signal:
/// <c>ValueNone</c> = don't clear (overlay on existing content),
/// <c>ValueSome color</c> = clear with this color before rendering.
/// </para>
/// </remarks>
[<Struct>]
type Camera3DConfig = {
  /// <summary>The raylib camera for rendering.</summary>
  Camera: Raylib_cs.Camera3D
  /// <summary>Viewport in normalized screen coordinates (0-1). ValueNone = fullscreen.</summary>
  Viewport: Raylib_cs.Rectangle voption
  /// <summary>Clear color before rendering. ValueNone = don't clear. ValueSome = clear with this color.</summary>
  ClearColor: Color voption
}

/// <summary>
/// Helper functions for 3D Cameras (Perspective / Orthographic projection).
/// </summary>
/// <remarks>
/// <para>
/// Use these for first-person, third-person, or any 3D game rendering.
/// </para>
/// <para>
/// The raylib <c>Camera3D</c> is a native mutable struct. <c>screenPointToRay</c> takes it
/// by <c>inref</c> (use <c>&amp;camera</c>); the config builders take it by value; the
/// constructors (<c>lookAt</c>/<c>orthographic</c>/<c>orbit</c>) return a new <c>Camera3D</c>.
/// </para>
/// </remarks>
module Camera3D =

  // ── Constructors ──

  /// <summary>
  /// Creates a perspective <c>Camera3D</c> that looks at a target from a position.
  /// </summary>
  /// <remarks>
  /// Mirrors the MonoGame <c>Camera3D.lookAt</c> capability, but returns the native
  /// raylib <c>Camera3D</c> struct. The field-of-view is in **degrees** (raylib convention),
  /// unlike MonoGame's radians. Near/far planes and aspect ratio are managed internally
  /// by raylib's <c>BeginMode3D</c>, so they are not parameters here.
  /// </remarks>
  /// <param name="position">Camera position in world space.</param>
  /// <param name="target">Point the camera is looking at.</param>
  /// <param name="up">Up vector (typically <c>Vector3.UnitY</c>).</param>
  /// <param name="fovy">Vertical field of view in **degrees**.</param>
  let inline lookAt
    (position: Vector3)
    (target: Vector3)
    (up: Vector3)
    (fovy: float32)
    : Raylib_cs.Camera3D =
    Raylib_cs.Camera3D(
      position,
      target,
      up,
      fovy,
      Raylib_cs.CameraProjection.Perspective
    )

  /// <summary>
  /// Creates an orthographic <c>Camera3D</c> that looks at a target from a position.
  /// </summary>
  /// <param name="position">Camera position in world space.</param>
  /// <param name="target">Point the camera is looking at.</param>
  /// <param name="up">Up vector (typically <c>Vector3.UnitY</c>).</param>
  /// <param name="size">Vertical size of the orthographic view volume in world units (raylib's <c>fovy</c> in ortho mode).</param>
  let inline orthographic
    (position: Vector3)
    (target: Vector3)
    (up: Vector3)
    (size: float32)
    : Raylib_cs.Camera3D =
    Raylib_cs.Camera3D(
      position,
      target,
      up,
      size,
      Raylib_cs.CameraProjection.Orthographic
    )

  /// <summary>
  /// Creates an orbiting perspective <c>Camera3D</c> using spherical coordinates.
  /// </summary>
  /// <remarks>
  /// Useful for third-person cameras, inspection views, or editor cameras.
  /// The field-of-view is in **degrees** (raylib convention).
  /// </remarks>
  /// <param name="target">Point the camera orbits around.</param>
  /// <param name="yaw">Horizontal rotation angle in radians.</param>
  /// <param name="pitch">Vertical rotation angle in radians.</param>
  /// <param name="radius">Distance from target.</param>
  /// <param name="fovy">Vertical field of view in **degrees**.</param>
  let inline orbit
    (target: Vector3)
    (yaw: float32)
    (pitch: float32)
    (radius: float32)
    (fovy: float32)
    : Raylib_cs.Camera3D =
    let position =
      Vector3(
        radius * MathF.Sin(yaw) * MathF.Cos(pitch),
        radius * MathF.Sin(pitch),
        radius * MathF.Cos(yaw) * MathF.Cos(pitch)
      )
      + target

    lookAt position target Vector3.UnitY fovy

  /// <summary>
  /// Creates a ray from screen coordinates for mouse/touch picking.
  /// </summary>
  /// <remarks>
  /// Thin wrapper over raylib's native <c>Raylib.GetScreenToWorldRay</c>, returning the native
  /// <c>Raylib_cs.Ray</c> so it composes with raylib's own picking/collision helpers.
  /// Mirrors the MonoGame <c>Camera3D.screenPointToRay</c> capability.
  /// </remarks>
  /// <param name="camera">Passed by read-only reference (<c>&amp;camera</c>) to avoid copying the native struct.</param>
  /// <param name="screenPos">The screen position in pixels.</param>
  let inline screenPointToRay
    (camera: inref<Raylib_cs.Camera3D>)
    (screenPos: Vector2)
    : Raylib_cs.Ray =
    Raylib.GetScreenToWorldRay(screenPos, camera)

  // ── Rendering Config Builders ──

  /// <summary>
  /// Create a rendering config from a raylib camera.
  /// Defaults: fullscreen, no clear.
  /// </summary>
  let inline render(camera: Raylib_cs.Camera3D) : Camera3DConfig = {
    Camera = camera
    Viewport = ValueNone
    ClearColor = ValueNone
  }

  /// <summary>Set viewport in normalized screen coordinates (0-1).</summary>
  let inline withViewport
    (viewport: Raylib_cs.Rectangle)
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

  /// <summary>Split-screen left half. Clears with given color.</summary>
  let inline splitScreenLeft
    (camera: Raylib_cs.Camera3D)
    (clearColor: Color)
    : Camera3DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen right half. Clears with given color.</summary>
  let inline splitScreenRight
    (camera: Raylib_cs.Camera3D)
    (clearColor: Color)
    : Camera3DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.5f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen top half. Clears with given color.</summary>
  let inline splitScreenTop
    (camera: Raylib_cs.Camera3D)
    (clearColor: Color)
    : Camera3DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 1.0f, 0.5f))
    |> withClear clearColor

  /// <summary>Split-screen bottom half. Clears with given color.</summary>
  let inline splitScreenBottom
    (camera: Raylib_cs.Camera3D)
    (clearColor: Color)
    : Camera3DConfig =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.5f, 1.0f, 0.5f))
    |> withClear clearColor
