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
/// Use these for top-down, side-scrolling, or any 2D game rendering.
/// </remarks>
module Camera2D =

  /// <summary>Calculates the visible world bounds for a raylib Camera2D.</summary>
  let viewportBounds
    (camera: Raylib_cs.Camera2D)
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
  let create
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
  let screenToWorld
    (camera: Raylib_cs.Camera2D)
    (screenPos: Vector2)
    : Vector2 =
    Raylib.GetScreenToWorld2D(screenPos, camera)

  /// <summary>Converts a world position to screen position (pixels).</summary>
  let worldToScreen (camera: Raylib_cs.Camera2D) (worldPos: Vector2) : Vector2 =
    Raylib.GetWorldToScreen2D(worldPos, camera)

  /// <summary>
  /// Smoothly interpolate the camera target toward a world position.
  /// </summary>
  /// <param name="camera">Passed by reference so mutations are visible to the caller.</param>
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
  /// <param name="camera">Passed by reference so mutations are visible to the caller.</param>
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
  let render(camera: Raylib_cs.Camera2D) : Camera2DConfig = {
    Camera = camera
    Viewport = ValueNone
    ClearColor = ValueNone
  }

  /// <summary>Set viewport in normalized screen coordinates (0-1).</summary>
  let withViewport (viewport: Raylib_cs.Rectangle) (config: Camera2DConfig) = {
    config with
        Viewport = ValueSome viewport
  }

  /// <summary>Clear with this color before rendering.</summary>
  let withClear (color: Color) (config: Camera2DConfig) = {
    config with
        ClearColor = ValueSome color
  }

  /// <summary>Split-screen left half. Clears with given color.</summary>
  let splitScreenLeft (camera: Raylib_cs.Camera2D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen right half. Clears with given color.</summary>
  let splitScreenRight (camera: Raylib_cs.Camera2D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.5f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen top half. Clears with given color.</summary>
  let splitScreenTop (camera: Raylib_cs.Camera2D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 1.0f, 0.5f))
    |> withClear clearColor

  /// <summary>Split-screen bottom half. Clears with given color.</summary>
  let splitScreenBottom (camera: Raylib_cs.Camera2D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.5f, 1.0f, 0.5f))
    |> withClear clearColor

  /// <summary>Picture-in-picture overlay. Clears with black by default.</summary>
  let overlay (camera: Raylib_cs.Camera2D) (bounds: Raylib_cs.Rectangle) =
    render camera |> withViewport bounds |> withClear Color.Black


/// <summary>
/// Camera rendering configuration for 3D pipelines.
/// </summary>
/// <remarks>
/// <para>
/// Controls how a camera renders: viewport bounds, clear behavior, and post-processing.
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
  /// <summary>Post-process pass indices. ValueNone = all passes. ValueSome [||] = no passes.</summary>
  PostProcessPasses: int[] voption
}

/// <summary>
/// Helper functions for 3D Cameras (Perspective projection).
/// </summary>
/// <remarks>
/// Use these for first-person, third-person, or any 3D game rendering.
/// </remarks>
module Camera3D =

  // ── Rendering Config Builders ──

  /// <summary>
  /// Create a rendering config from a raylib camera.
  /// Defaults: fullscreen, no clear, all post-process passes.
  /// </summary>
  let render(camera: Raylib_cs.Camera3D) : Camera3DConfig = {
    Camera = camera
    Viewport = ValueNone
    ClearColor = ValueNone
    PostProcessPasses = ValueNone
  }

  /// <summary>Set viewport in normalized screen coordinates (0-1).</summary>
  let withViewport (viewport: Raylib_cs.Rectangle) (config: Camera3DConfig) = {
    config with
        Viewport = ValueSome viewport
  }

  /// <summary>Clear with this color before rendering.</summary>
  let withClear (color: Color) (config: Camera3DConfig) = {
    config with
        ClearColor = ValueSome color
  }

  /// <summary>Use only specific post-process pass indices.</summary>
  let withPostProcess (passes: int[]) (config: Camera3DConfig) = {
    config with
        PostProcessPasses = ValueSome passes
  }

  /// <summary>Disable post-processing for this camera.</summary>
  let withoutPostProcess(config: Camera3DConfig) = {
    config with
        PostProcessPasses = ValueSome [||]
  }

  // ── Convenience Constructors ──

  /// <summary>Split-screen left half. Clears with given color.</summary>
  let splitScreenLeft (camera: Raylib_cs.Camera3D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen right half. Clears with given color.</summary>
  let splitScreenRight (camera: Raylib_cs.Camera3D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.5f, 0.0f, 0.5f, 1.0f))
    |> withClear clearColor

  /// <summary>Split-screen top half. Clears with given color.</summary>
  let splitScreenTop (camera: Raylib_cs.Camera3D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.0f, 1.0f, 0.5f))
    |> withClear clearColor

  /// <summary>Split-screen bottom half. Clears with given color.</summary>
  let splitScreenBottom (camera: Raylib_cs.Camera3D) (clearColor: Color) =
    render camera
    |> withViewport(Raylib_cs.Rectangle(0.0f, 0.5f, 1.0f, 0.5f))
    |> withClear clearColor

  /// <summary>Picture-in-picture overlay. No post-process by default.</summary>
  let overlay (camera: Raylib_cs.Camera3D) (bounds: Raylib_cs.Rectangle) =
    render camera
    |> withViewport bounds
    |> withClear Color.Black
    |> withoutPostProcess
