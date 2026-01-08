namespace Mibo.Elmish

open Microsoft.Xna.Framework

/// <summary>
/// Generic helper functions for visibility culling.
/// </summary>
/// <remarks>
/// These helpers operate on standard MonoGame primitives (BoundingFrustum, BoundingSphere, etc)
/// to separate spatial partitioning logic from rendering logic.
/// </remarks>
module Culling =

  /// <summary>Checks if a bounding sphere is within the view frustum.</summary>
  /// <remarks>Returns true if fully inside or intersecting (partially visible). Use with <see cref="M:Mibo.Elmish.Camera3D.boundingFrustum"/> to get the frustum.</remarks>
  let inline isVisible (frustum: BoundingFrustum) (sphere: BoundingSphere) =
    let containment = frustum.Contains(sphere)
    containment <> ContainmentType.Disjoint

  /// <summary>Checks if a bounding box is within the view frustum.</summary>
  /// <remarks>Returns true if fully inside or intersecting (partially visible). Useful for culling axis-aligned geometry or spatial partition nodes.</remarks>
  let inline isGenericVisible (frustum: BoundingFrustum) (box: BoundingBox) =
    let containment = frustum.Contains(box)
    containment <> ContainmentType.Disjoint

  /// <summary>Checks if a 2D rectangle intersects with the visible camera bounds.</summary>
  /// <remarks>Use with <see cref="M:Mibo.Elmish.Camera2D.viewportBounds"/> to get the view bounds.</remarks>
  /// <example>
  /// <code>
  /// let viewBounds = Camera2D.viewportBounds camera viewport
  /// if Culling.isVisible2D viewBounds sprite.Bounds then
  ///     // Render sprite
  /// </code>
  /// </example>
  let inline isVisible2D (viewBounds: Rectangle) (itemBounds: Rectangle) =
    viewBounds.Intersects(itemBounds)
