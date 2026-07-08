namespace Mibo.Elmish

open System.Numerics
open Raylib_cs

/// <summary>
/// Describes the relationship between a frustum and a bounded volume.
/// </summary>
type ContainmentType =
  | Disjoint = 0
  | Intersects = 1
  | Contains = 2

/// <summary>
/// Represents a bounding sphere in 3D space.
/// </summary>
[<Struct>]
type BoundingSphere = { Center: Vector3; Radius: float32 }

/// <summary>
/// Represents a view frustum defined by six planes extracted from a View*Projection matrix.
/// </summary>
/// <remarks>
/// Each plane is stored as a <c>Vector4</c> where (x, y, z) is the normal and w is the distance
/// from the origin along the normal direction.
/// </remarks>
type Frustum(viewProjection: Matrix4x4) =

  // Gribb/Hartmann plane extraction adapted for row-major matrices (System.Numerics).
  // In row-major, p_clip = p * VP, so x_clip = p.col1, w_clip = p.col4, etc.
  // The frustum half-spaces are: x_clip + w_clip >= 0 (left), w_clip - x_clip >= 0 (right), etc.
  let normalizePlane(p: Vector4) =
    let len = float32(sqrt(double(p.X * p.X + p.Y * p.Y + p.Z * p.Z)))

    if len > 0.0001f then
      Vector4(p.X / len, p.Y / len, p.Z / len, p.W / len)
    else
      p

  let planes = [|
    // Left:   col1 + col4  →  x_clip + w_clip >= 0
    normalizePlane(
      Vector4(
        viewProjection.M11 + viewProjection.M14,
        viewProjection.M21 + viewProjection.M24,
        viewProjection.M31 + viewProjection.M34,
        viewProjection.M41 + viewProjection.M44
      )
    )
    // Right:  col4 - col1  →  w_clip - x_clip >= 0
    normalizePlane(
      Vector4(
        viewProjection.M14 - viewProjection.M11,
        viewProjection.M24 - viewProjection.M21,
        viewProjection.M34 - viewProjection.M31,
        viewProjection.M44 - viewProjection.M41
      )
    )
    // Bottom: col2 + col4  →  y_clip + w_clip >= 0
    normalizePlane(
      Vector4(
        viewProjection.M12 + viewProjection.M14,
        viewProjection.M22 + viewProjection.M24,
        viewProjection.M32 + viewProjection.M34,
        viewProjection.M42 + viewProjection.M44
      )
    )
    // Top:    col4 - col2  →  w_clip - y_clip >= 0
    normalizePlane(
      Vector4(
        viewProjection.M14 - viewProjection.M12,
        viewProjection.M24 - viewProjection.M22,
        viewProjection.M34 - viewProjection.M32,
        viewProjection.M44 - viewProjection.M42
      )
    )
    // Near:   col3 + col4  →  z_clip + w_clip >= 0
    normalizePlane(
      Vector4(
        viewProjection.M13 + viewProjection.M14,
        viewProjection.M23 + viewProjection.M24,
        viewProjection.M33 + viewProjection.M34,
        viewProjection.M43 + viewProjection.M44
      )
    )
    // Far:    col4 - col3  →  w_clip - z_clip >= 0
    normalizePlane(
      Vector4(
        viewProjection.M14 - viewProjection.M13,
        viewProjection.M24 - viewProjection.M23,
        viewProjection.M34 - viewProjection.M33,
        viewProjection.M44 - viewProjection.M43
      )
    )
  |]

  // Signed distance from point to plane: dot(normal.xyz, point) + d
  let dot4 (p: Vector4) (v: Vector3) = p.X * v.X + p.Y * v.Y + p.Z * v.Z + p.W

  /// <summary>Tests whether the given <see cref="T:Mibo.Elmish.BoundingSphere"/> is contained within the frustum.</summary>
  member _.Contains(sphere: BoundingSphere) =
    let mutable result = ContainmentType.Contains

    for p in planes do
      let dist = dot4 p sphere.Center

      if dist < -sphere.Radius then
        result <- ContainmentType.Disjoint
      elif abs dist < sphere.Radius then
        result <- ContainmentType.Intersects

    result

  /// <summary>Tests whether the given <see cref="T:Raylib_cs.BoundingBox"/> is contained within the frustum.</summary>
  member _.Contains(box: BoundingBox) =
    let mutable result = ContainmentType.Contains

    for p in planes do
      // Positive vertex (furthest along plane normal)
      let pvx = if p.X >= 0.0f then box.Max.X else box.Min.X
      let pvy = if p.Y >= 0.0f then box.Max.Y else box.Min.Y
      let pvz = if p.Z >= 0.0f then box.Max.Z else box.Min.Z
      let pVertex = Vector3(pvx, pvy, pvz)

      // Negative vertex (nearest along plane normal)
      let nvx = if p.X >= 0.0f then box.Min.X else box.Max.X
      let nvy = if p.Y >= 0.0f then box.Min.Y else box.Max.Y
      let nvz = if p.Z >= 0.0f then box.Min.Z else box.Max.Z
      let nVertex = Vector3(nvx, nvy, nvz)

      if result = ContainmentType.Disjoint then
        ()
      elif dot4 p pVertex < 0.0f then
        // p-vertex is behind the plane -> entire box is outside
        result <- ContainmentType.Disjoint
      elif dot4 p nVertex < 0.0f then
        // n-vertex is behind but p-vertex is in front -> box straddles
        result <- ContainmentType.Intersects

    result


/// <summary>
/// Generic helper functions for visibility culling.
/// </summary>
/// <remarks>
/// These helpers operate on the custom frustum/primitives to separate
/// spatial partitioning logic from rendering logic.
/// </remarks>
module Culling =

  /// <summary>Checks if a bounding sphere is within the view frustum.</summary>
  /// <remarks>Returns true if fully inside or intersecting (partially visible).</remarks>
  let inline isVisible (frustum: Frustum) (sphere: BoundingSphere) =
    let containment = frustum.Contains(sphere)
    containment <> ContainmentType.Disjoint

  /// <summary>Checks if a bounding box is within the view frustum.</summary>
  /// <remarks>Returns true if fully inside or intersecting (partially visible). Useful for culling axis-aligned geometry or spatial partition nodes.</remarks>
  let inline isGenericVisible (frustum: Frustum) (box: BoundingBox) =
    let containment = frustum.Contains(box)
    containment <> ContainmentType.Disjoint

  /// <summary>Checks if a 2D rectangle intersects with the visible camera bounds.</summary>
  /// <remarks>Use with <see cref="M:Mibo.Elmish.Camera2D.viewportBounds"/> to get the view bounds.</remarks>
  /// <example>
  /// <code>
  /// let viewBounds = Camera2D.viewportBounds &camera width height
  /// if Culling.isVisible2D viewBounds sprite.Bounds then
  ///     // Render sprite
  /// </code>
  /// </example>
  let inline isVisible2D
    (viewBounds: Raylib_cs.Rectangle)
    (itemBounds: Raylib_cs.Rectangle)
    =
    viewBounds.X < itemBounds.X + itemBounds.Width
    && viewBounds.X + viewBounds.Width > itemBounds.X
    && viewBounds.Y < itemBounds.Y + itemBounds.Height
    && viewBounds.Y + viewBounds.Height > itemBounds.Y
