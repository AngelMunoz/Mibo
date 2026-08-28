namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open MonoGame.Framework.Utilities
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// Opacity — the shared material-opacity classification helpers. Compiled before
// both ShadowPass and PbrShading: the shadow/scene-depth collector and the forward
// dispatch must classify a draw with the same tiers (>= 1 opaque, 0 < x < 1
// transparent, <= 0 invisible) so a deferred transparent never casts a shadow or
// writes scene depth, whatever the draw kind.
// ─────────────────────────────────────────────────────────────────────────────

module Opacity =

  /// <summary>True on the OpenGL backend, which has no vertex texture fetch — skinned +
  /// instanced draws fall back to per-instance skinned draws there (their transparency
  /// classification is per part/instance, not per batch).</summary>
  let inline isOpenGLBackend() =
    PlatformInfo.GraphicsBackend = GraphicsBackend.OpenGL

  /// Sort key for a deferred instanced batch: squared distance from the camera to the
  /// average instance translation. The batch sorts as one unit; ordering between its
  /// instances stays submission order (the accepted batch-level approximation).
  let inline instanceCentroidDistanceSq
    (cameraPos: Vector3, transforms: Matrix[], count: int)
    =
    let mutable acc = Vector3.Zero

    for i = 0 to count - 1 do
      acc <- acc + transforms[i].Translation

    let centroid = acc / float32 count
    Vector3.DistanceSquared(cameraPos, centroid)

  /// True when any per-instance tint color has alpha below 255 — the shader multiplies
  /// it into the final opacity, so such a batch must defer even with an opaque material.
  /// One byte compare per instance, once per command.
  let inline anyTransparentInstanceColor(colors: Color[] voption) =
    match colors with
    | ValueNone -> false
    | ValueSome Null -> false
    | ValueSome cs ->
      let mutable found = false
      let mutable i = 0

      while not found && i < cs.Length do
        if cs[i].A < 255uy then
          found <- true

        i <- i + 1

      found

  /// True when any submesh part of the model resolves to a non-opaque material under the
  /// override (whole-model <c>All</c> short-circuits). The skinned-instanced command
  /// defers as one batch when this holds — parts cannot split without losing the single
  /// instanced draw call.
  let animatedModelAnyTransparentPart
    (model: Model, matOverride: MaterialOverride voption)
    =
    match matOverride with
    | ValueSome(MaterialOverride.All m) -> m.Opacity < 1.0f
    | _ ->
      let mutable found = false
      let mutable partIndex = 0

      for mesh in model.Meshes do
        for part in mesh.MeshParts do
          if not found then
            let mat =
              match matOverride with
              | ValueSome(MaterialOverride.PerMesh f) -> f partIndex
              | _ -> Material3D.fromModelMeshPart part

            if mat.Opacity < 1.0f then
              found <- true

          partIndex <- partIndex + 1

      found
