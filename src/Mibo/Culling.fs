namespace Mibo.Elmish

open Microsoft.Xna.Framework

/// Generic helper functions for visibility culling.
///
/// These helpers operate on standard MonoGame primitives (BoundingFrustum, BoundingSphere, etc)
/// to separate spatial partitioning logic from rendering logic.
module Culling =

  /// Checks if a bounding sphere is within the view frustum.
  /// Returns true if fully inside or intersecting (partially visible).
  let inline isVisible (frustum: BoundingFrustum) (sphere: BoundingSphere) =
    let containment = frustum.Contains(sphere)
    containment <> ContainmentType.Disjoint

  /// Checks if a bounding box is within the view frustum.
  /// Returns true if fully inside or intersecting (partially visible).
  let inline isGenericVisible (frustum: BoundingFrustum) (box: BoundingBox) =
    let containment = frustum.Contains(box)
    containment <> ContainmentType.Disjoint

  /// Checks if a 2D rectangle intersects with the 2D visible bounds.
  let inline isVisible2D (viewBounds: Rectangle) (itemBounds: Rectangle) =
    viewBounds.Intersects(itemBounds)
