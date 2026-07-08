namespace Mibo.Elmish

open Microsoft.Xna.Framework

/// <summary>
/// Generic helper functions for visibility culling.
/// </summary>
/// <remarks>
/// These are thin wrappers over MonoGame's native <see cref="T:Microsoft.Xna.Framework.BoundingFrustum"/>,
/// <see cref="T:Microsoft.Xna.Framework.BoundingSphere"/>, and
/// <see cref="T:Microsoft.Xna.Framework.BoundingBox"/>.
/// </remarks>
module Culling =

  /// <summary>Checks if a bounding sphere is within the view frustum.</summary>
  /// <remarks>Returns true if fully inside or intersecting (partially visible).</remarks>
  let inline isVisible (frustum: BoundingFrustum) (sphere: BoundingSphere) =
    frustum.Contains(sphere) <> ContainmentType.Disjoint

  /// <summary>Checks if an axis-aligned bounding box is within the view frustum.</summary>
  /// <remarks>Returns true if fully inside or intersecting (partially visible). Useful for culling axis-aligned geometry or spatial partition nodes.</remarks>
  let inline isVisibleBox (frustum: BoundingFrustum) (box: BoundingBox) =
    frustum.Contains(box) <> ContainmentType.Disjoint

  /// <summary>Checks if a 2D rectangle intersects with visible camera bounds.</summary>
  /// <remarks>Use with <see cref="M:Mibo.Elmish.Camera2D.viewportBounds"/> to get the view bounds.</remarks>
  let inline isVisible2D (viewBounds: Rectangle) (itemBounds: Rectangle) =
    viewBounds.Intersects(itemBounds)
