namespace Mibo.Elmish

open Microsoft.Xna.Framework

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
/// Use <see cref="M:Mibo.Elmish.Camera3D"/> builders (B3) to construct one.
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
  /// <summary>Post-process pass indices. ValueNone = all passes. ValueSome [||] = no passes.</summary>
  PostProcessPasses: int[] voption
}
