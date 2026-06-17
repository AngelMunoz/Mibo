namespace Mibo.Elmish.Next.Camera2D

open System
open System.Numerics
open Mibo.Elmish.Next.Graphics2D
open Mibo.Elmish.Next.Graphics2D.Base

/// <summary>Helper functions for 2D Cameras (Orthographic projection).</summary>
/// <remarks>
/// Use these for top-down, side-scrolling, or any 2D game rendering.
/// These functions operate on the backend-neutral <see cref="T:Mibo.Elmish.Next.Graphics2D.Camera2DState"/>.
/// </remarks>
module Camera2D =

  /// <summary>Calculates the visible world bounds for a Camera2DState.</summary>
  let viewportBounds
    (camera: Camera2DState)
    (width: float32)
    (height: float32)
    : Rect =
    let visibleW = width / camera.Zoom
    let visibleH = height / camera.Zoom
    let halfW = visibleW * 0.5f
    let halfH = visibleH * 0.5f

    {
      X = camera.Target.X - halfW
      Y = camera.Target.Y - halfH
      Width = visibleW
      Height = visibleH
    }

  /// <summary>
  /// Creates a <c>Camera2DState</c> centered on the given position.
  /// </summary>
  let create
    (position: Vector2)
    (zoom: float32)
    (viewportSize: Vector2)
    : Camera2DState =
    {
      Offset = Vector2(viewportSize.X * 0.5f, viewportSize.Y * 0.5f)
      Target = position
      Rotation = 0.0f
      Zoom = zoom
    }

  /// <summary>Smoothly interpolate the camera target toward a world position.</summary>
  /// <param name="camera">Passed by reference so mutations are visible to the caller.</param>
  let inline smoothFollow
    (camera: byref<Camera2DState>)
    (target: Vector2)
    (speed: float32)
    =
    let mutable t = camera.Target
    t.X <- t.X + (target.X - t.X) * speed
    t.Y <- t.Y + (target.Y - t.Y) * speed
    camera.Target <- t

  /// <summary>
  /// Clamp the camera target to a world bounds rectangle.
  /// </summary>
  /// <param name="camera">Passed by reference so mutations are visible to the caller.</param>
  let inline clampTarget
    (camera: byref<Camera2DState>)
    (minX: float32)
    (minY: float32)
    (maxX: float32)
    (maxY: float32)
    =
    let mutable t = camera.Target
    t.X <- MathF.Max(minX, MathF.Min(t.X, maxX))
    t.Y <- MathF.Max(minY, MathF.Min(t.Y, maxY))
    camera.Target <- t

  // ── Rendering Config Builders ──

  /// <summary>
  /// Create a rendering config from a 2D camera.
  /// Defaults: fullscreen, no clear.
  /// </summary>
  let render(camera: Camera2DState) : Camera2DConfig = {
    Camera = camera
    Viewport = ValueNone
    ClearColor = ValueNone
  }

  /// <summary>Set viewport in normalized screen coordinates (0-1).</summary>
  let withViewport (viewport: Rect) (config: Camera2DConfig) = {
    config with
        Viewport = ValueSome viewport
  }

  /// <summary>Clear with this color before rendering.</summary>
  let withClear (color: Color) (config: Camera2DConfig) = {
    config with
        ClearColor = ValueSome color
  }

  /// <summary>Split-screen left half. Clears with given color.</summary>
  let splitScreenLeft (camera: Camera2DState) (clearColor: Color) =
    render camera
    |> withViewport {
      X = 0.0f
      Y = 0.0f
      Width = 0.5f
      Height = 1.0f
    }
    |> withClear clearColor

  /// <summary>Split-screen right half. Clears with given color.</summary>
  let splitScreenRight (camera: Camera2DState) (clearColor: Color) =
    render camera
    |> withViewport {
      X = 0.5f
      Y = 0.0f
      Width = 0.5f
      Height = 1.0f
    }
    |> withClear clearColor

  /// <summary>Split-screen top half. Clears with given color.</summary>
  let splitScreenTop (camera: Camera2DState) (clearColor: Color) =
    render camera
    |> withViewport {
      X = 0.0f
      Y = 0.0f
      Width = 1.0f
      Height = 0.5f
    }
    |> withClear clearColor

  /// <summary>Split-screen bottom half. Clears with given color.</summary>
  let splitScreenBottom (camera: Camera2DState) (clearColor: Color) =
    render camera
    |> withViewport {
      X = 0.0f
      Y = 0.5f
      Width = 1.0f
      Height = 0.5f
    }
    |> withClear clearColor

  /// <summary>Picture-in-picture overlay. Clears with black by default.</summary>
  let overlay (camera: Camera2DState) (bounds: Rect) =
    render camera
    |> withViewport bounds
    |> withClear { R = 0uy; G = 0uy; B = 0uy; A = 255uy }
