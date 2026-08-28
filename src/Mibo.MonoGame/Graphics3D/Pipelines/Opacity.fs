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

  /// <summary>
  /// Which instances of an instanced command a staging pass keeps. The per-instance tint
  /// alpha makes single instances semi-transparent while their neighbors stay opaque, so
  /// transparency is decided per instance: an instance is opaque when its tint alpha is 255
  /// (instances past the colors array clamp to white, matching the staging contract). The
  /// filter applies at row-staging time — the kept instances' rows are written compacted,
  /// and the command's arrays and palette layout stay untouched.
  /// </summary>
  [<RequireQualifiedAccess>]
  type InstanceOpacityFilter =
    | All
    | OpaqueOnly
    | TransparentOnly

  /// <summary>
  /// Which parts (draw units) of a skinned + instanced command a pass keeps: the parts whose
  /// resolved material is opaque, the transparent ones, or all. Materials resolve per part
  /// (<c>All</c> override or authored part material / <c>PerMesh</c> resolver), so a command
  /// with mixed part opacities draws its opaque parts inline (shadows, depth writes) and
  /// defers only its transparent parts.
  /// </summary>
  [<RequireQualifiedAccess>]
  type SkinnedInstancedUnitFilter =
    | All
    | OpaqueOnly
    | TransparentOnly

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

  /// <summary>
  /// Count-aware whole-batch probe: true when any of the first <paramref name="count"/>
  /// instances has a tint alpha below 255. The count matters — a colors array longer than
  /// the clamped instance count must not classify instances that never draw.
  /// </summary>
  let inline anyTransparentInstanceColor(colors: Color[] voption, count: int) =
    match colors with
    | ValueNone -> false
    | ValueSome Null -> false
    | ValueSome cs ->
      let mutable found = false
      let mutable i = 0
      let n = min count cs.Length

      while not found && i < n do
        if cs[i].A < 255uy then
          found <- true

        i <- i + 1

      found

  /// <summary>
  /// The per-instance split decision in one scan: how many of the first
  /// <paramref name="count"/> instances are transparent (tint alpha below 255), and the
  /// squared distance from <paramref name="cameraPos"/> to the transparent instances'
  /// centroid — the deferred subset's sort key. Zero transparent instances returns
  /// <c>(0, 0)</c>; null transforms or colors return <c>(0, 0)</c> (nothing to split).
  /// </summary>
  let inline instanceSubsetStats
    (
      cameraPos: Vector3,
      transforms: Matrix[],
      colors: Color[] voption,
      count: int
    ) =
    let transformCount = if isNull transforms then 0 else transforms.Length
    let n = min count transformCount

    match colors with
    | ValueNone -> struct (0, 0.0f)
    | ValueSome Null -> struct (0, 0.0f)
    | ValueSome cs ->
      let mutable transparent = 0
      let mutable acc = Vector3.Zero
      let mutable i = 0

      while i < n do
        if i < cs.Length && cs[i].A < 255uy then
          transparent <- transparent + 1
          acc <- acc + transforms[i].Translation

        i <- i + 1

      if transparent = 0 then
        struct (0, 0.0f)
      else
        let centroid = acc / float32 transparent
        struct (transparent, Vector3.DistanceSquared(cameraPos, centroid))

  /// <summary>
  /// One walk over the model's parts: (any part transparent, any part opaque, any part
  /// invisible) under the override, resolved exactly as the forward pass resolves
  /// materials. Transparent is <c>0 &lt; Opacity &lt; 1</c>, invisible is
  /// <c>Opacity &lt;= 0</c> — the shadow collector must tell them apart: neither
  /// casts, but only a command whose every part is opaque may take the merged fast
  /// path (an invisible part must not ride a merged group). Whole-model <c>All</c>
  /// short-circuits all three.
  /// </summary>
  let animatedModelPartOpacityMix
    (model: Model, matOverride: MaterialOverride voption)
    =
    match matOverride with
    | ValueSome(MaterialOverride.All m) ->
      if m.Opacity >= 1.0f then struct (false, true, false)
      elif m.Opacity > 0.0f then struct (true, false, false)
      else struct (false, false, true)
    | _ ->
      let mutable anyTransparent = false
      let mutable anyOpaque = false
      let mutable anyInvisible = false
      let mutable partIndex = 0

      for mesh in model.Meshes do
        for part in mesh.MeshParts do
          if not(anyTransparent && anyOpaque && anyInvisible) then
            let mat =
              match matOverride with
              | ValueSome(MaterialOverride.PerMesh f) -> f partIndex
              | _ -> Material3D.fromModelMeshPart part

            if mat.Opacity >= 1.0f then anyOpaque <- true
            elif mat.Opacity > 0.0f then anyTransparent <- true
            else anyInvisible <- true

          partIndex <- partIndex + 1

      struct (anyTransparent, anyOpaque, anyInvisible)
