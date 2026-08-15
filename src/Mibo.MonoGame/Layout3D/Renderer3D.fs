namespace Mibo.Layout3D

open System.Buffers
open System.Collections.Generic
open System.Runtime.InteropServices
open Microsoft.Xna.Framework
open Mibo.Elmish.Graphics3D
open Mibo.Layout3D

/// <summary>
/// Contextual object for instanced cell-grid rendering on the MonoGame backend.
/// Bundles the key/material/transform functions and manages internal reusable
/// storage and snapshot pooling to avoid per-frame allocations.
/// </summary>
/// <remarks>
/// Ported from <c>Mibo.Raylib/Layout3D/Renderer3D.fs</c> with type swaps per §5/§6.2:
/// <list>
/// <item><c>Raylib_cs.Mesh</c> → <see cref="T:Mibo.Elmish.Graphics3D.PrimitiveMesh"/></item>
/// <item><c>System.Numerics.Matrix4x4</c> → <see cref="T:Microsoft.Xna.Framework.Matrix"/>
/// (converted at the Core↔backend boundary via <c>Conversions</c>).</item>
/// <item><c>System.Numerics.Vector3</c> → <see cref="T:Microsoft.Xna.Framework.Vector3"/> (same).</item>
/// </list>
/// The layout logic itself lives in <c>Mibo.Core.Layout3D</c> (backend-agnostic) and is
/// reused unchanged; only the renderer glue is ported.
/// </remarks>
type InstancedRenderContext<'T, 'K when 'K: equality>
  (
    [<InlineIfLambda>] getKey: 'T -> 'K,
    [<InlineIfLambda>] getMeshesAndMaterial:
      'T -> struct (PrimitiveMesh * Material3D)[],
    [<InlineIfLambda>] getTransform: Vector3 -> 'T -> Matrix
  ) =

  let storage = Dictionary<'K, struct (ResizeArray<Matrix> * 'T)>()
  let snapshotPool = ResizeArray<struct (Matrix[] * int)>()

  // Per-sub-mesh shader resolver. ValueNone on the primary ctor; the overload ctor
  // installs a ValueSome. EmitInstanced branches on it so a context built with the
  // triple-overload wraps each ValueSome sub-mesh draw in its own BeginEffect/EndEffect.
  let mutable perMeshShader
    : ('T
        -> struct (PrimitiveMesh *
        Material3D *
        Microsoft.Xna.Framework.Graphics.Effect voption)[]) voption =
    ValueNone

  // ModelPart resolver. ValueNone on the primary ctor; the parts-overload ctor
  // installs a ValueSome. Emit paths branch on it FIRST: each group draws one
  // instanced command per part, with the part's own bone folded into a pooled
  // per-part snapshot and the part's real buffer offsets (zero-copy wraps of
  // shared content-pipeline buffers — offset 0,0 would draw the first part's
  // triangles).
  let mutable partsResolver: ('T -> ModelPart[]) voption = ValueNone

  member internal _.Storage = storage
  member internal _.SnapshotPool = snapshotPool
  member _.GetKey = getKey
  member _.GetMeshesAndMaterial = getMeshesAndMaterial
  member _.GetTransform = getTransform

  /// <summary>Installs the per-sub-mesh shader resolver. Internal — set by the
  /// overload constructor that returns (mesh, material, shader) triples.</summary>
  member internal _.SetPerMeshShaderResolver f = perMeshShader <- ValueSome f

  member internal _.PerMeshShaderResolver = perMeshShader

  member internal _.SetPartsResolver f = partsResolver <- ValueSome f

  member internal _.PartsResolver = partsResolver

  /// <summary>
  /// Overload constructor for per-sub-mesh shaders: each triple may carry an
  /// <c>Effect voption</c>. A <c>ValueSome</c> effect wraps that sub-mesh's
  /// instanced draw in its own <c>BeginEffect</c>/<c>EndEffect</c> scope;
  /// <c>ValueNone</c> uses the default PBR instanced path. Existing
  /// two-element contexts are unaffected.
  /// </summary>
  new
    (
      getKey: 'T -> 'K,
      getMeshesMaterialAndShader:
        'T
          -> struct (PrimitiveMesh *
          Material3D *
          Microsoft.Xna.Framework.Graphics.Effect voption)[],
      getTransform: Vector3 -> 'T -> Matrix
    ) as this =
    InstancedRenderContext(
      getKey,
      (fun sample ->
        getMeshesMaterialAndShader sample
        |> Array.map(fun struct (mesh, material, _) -> struct (mesh, material))),
      getTransform
    )

    then this.SetPerMeshShaderResolver getMeshesMaterialAndShader

  /// <summary>
  /// Constructs a context over zero-copy <see cref="T:Mibo.Elmish.Graphics3D.ModelPart"/> slices
  /// (e.g. <see cref="M:Mibo.Elmish.Graphics3D.ModelParts.ofModel"/> results): each group emits one
  /// instanced command per part, folding the part's own absolute bone into a pooled
  /// per-part snapshot and passing the part's real buffer offsets. The
  /// <paramref name="getTransform"/> bone fold of the two-element ctor must NOT be
  /// used here — the context applies per-part bones itself.
  /// </summary>
  new
    (
      getKey: 'T -> 'K,
      getParts: 'T -> ModelPart[],
      getTransform: Vector3 -> 'T -> Matrix
    ) as this =
    // The pairs wrapper exists only for direct GetMeshesAndMaterial calls —
    // the emit paths branch on the parts resolver first and never reach it,
    // so the Array.map never runs on the draw path.
    InstancedRenderContext(
      getKey,
      (fun sample ->
        getParts sample
        |> Array.map(fun part -> struct (part.Mesh, part.Material))),
      getTransform
    )

    then this.SetPartsResolver getParts

  /// <summary>
  /// Records one <see cref="T:Mibo.Elmish.Graphics3D.ModelPart"/> instanced draw: folds the part's
  /// absolute bone into a pooled per-part snapshot (reusing the group snapshot for
  /// identity bones) and passes the part's real buffer offsets.
  /// </summary>
  member private _.EmitPartInstanced
    (buffer: RenderBuffer3D, part: ModelPart, snapshot: Matrix[], count: int)
    =
    let transforms =
      if part.Bone = Matrix.Identity then
        snapshot
      else
        let folded = ArrayPool<Matrix>.Shared.Rent count

        for j = 0 to count - 1 do
          folded[j] <- part.Bone * snapshot[j]

        snapshotPool.Add struct (folded, count)
        folded

    buffer.Add(
      Command3D.DrawInstanced(
        part.Mesh,
        transforms,
        ValueNone,
        part.Material,
        count,
        part.VertexOffset,
        part.StartIndex
      )
    )

  /// <summary>
  /// Returns pooled snapshot arrays to <see cref="T:System.Buffers.ArrayPool`1"/>
  /// and clears internal tracking state. Call once per frame <b>before</b>
  /// invoking <c>renderInstanced</c> or <c>renderVolumeInstanced</c>.
  /// </summary>
  /// <remarks>
  /// Skippable if GC pressure from instanced rendering is acceptable,
  /// but recommended for steady-state zero-alloc rendering.
  /// </remarks>
  member _.ResetFrameBuffers() =
    for i = 0 to snapshotPool.Count - 1 do
      let struct (arr, _) = snapshotPool[i]
      ArrayPool<Matrix>.Shared.Return arr

    snapshotPool.Clear()

  member internal this.EmitInstanced(buffer: RenderBuffer3D) =
    let groups = this.Storage
    let snapshots = this.SnapshotPool

    for KeyValue(_, struct (transforms, sample)) in groups do
      if transforms.Count > 0 then
        let count = transforms.Count
        let snapshot = ArrayPool<Matrix>.Shared.Rent count
        let span = CollectionsMarshal.AsSpan transforms

        for i = 0 to count - 1 do
          snapshot[i] <- span[i]

        snapshots.Add struct (snapshot, count)

        match this.PartsResolver with
        | ValueSome getParts ->
          // Parts path — one draw per part, bone folded and offsets honored.
          let parts = getParts sample

          for pi = 0 to parts.Length - 1 do
            this.EmitPartInstanced(buffer, parts[pi], snapshot, count)
        | ValueNone ->
          match this.PerMeshShaderResolver with
          | ValueNone ->
            // Legacy path — one DrawInstanced per sub-mesh, default PBR instanced shader.
            let meshesAndMaterials = this.GetMeshesAndMaterial sample

            for mi = 0 to meshesAndMaterials.Length - 1 do
              let struct (mesh, material) = meshesAndMaterials[mi]

              buffer.Add(
                Command3D.DrawInstanced(
                  mesh,
                  snapshot,
                  ValueNone,
                  material,
                  count,
                  0,
                  0
                )
              )
          | ValueSome triples ->
            // Per-sub-mesh path — wrap each ValueSome sub-mesh in its own BeginEffect/EndEffect.
            let arr = triples sample

            for mi = 0 to arr.Length - 1 do
              let struct (mesh, material, shader) = arr[mi]

              match shader with
              | ValueNone ->
                buffer.Add(
                  Command3D.DrawInstanced(
                    mesh,
                    snapshot,
                    ValueNone,
                    material,
                    count,
                    0,
                    0
                  )
                )
              | ValueSome s ->
                buffer.Add(Command3D.BeginEffect s)

                buffer.Add(
                  Command3D.DrawInstanced(
                    mesh,
                    snapshot,
                    ValueNone,
                    material,
                    count,
                    0,
                    0
                  )
                )

                buffer.Add(Command3D.EndEffect)

  /// <summary>
  /// Emits instanced draws with one <c>BeginEffect</c>/<c>EndEffect</c> scope per grid
  /// key when <paramref name="shaderForKey"/> returns <c>ValueSome</c>. <c>ValueNone</c>
  /// falls through to the default PBR instanced path for that key. Ignores any
  /// per-sub-mesh resolver to avoid nesting effect scopes.
  /// </summary>
  member internal this.EmitInstancedWithEffect
    (
      buffer: RenderBuffer3D,
      shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption
    ) =
    let groups = this.Storage
    let snapshots = this.SnapshotPool

    for KeyValue(key, struct (transforms, sample)) in groups do
      if transforms.Count > 0 then
        let count = transforms.Count
        let snapshot = ArrayPool<Matrix>.Shared.Rent count
        let span = CollectionsMarshal.AsSpan transforms

        for i = 0 to count - 1 do
          snapshot[i] <- span[i]

        snapshots.Add struct (snapshot, count)

        let scope = shaderForKey key

        match scope with
        | ValueSome s -> buffer.Add(Command3D.BeginEffect s)
        | ValueNone -> ()

        // Use the triples resolver directly when present: routing through
        // GetMeshesAndMaterial would run its Array.map wrapper and allocate a fresh array
        // per group per frame (the shader component is unused here — the per-key scope
        // supersedes per-sub-mesh shaders).
        match this.PartsResolver with
        | ValueSome getParts ->
          // Parts path — one draw per part, bone folded and offsets honored.
          let parts = getParts sample

          for pi = 0 to parts.Length - 1 do
            this.EmitPartInstanced(buffer, parts[pi], snapshot, count)
        | ValueNone ->
          match this.PerMeshShaderResolver with
          | ValueSome triples ->
            let meshMaterialShaders = triples sample

            for mi = 0 to meshMaterialShaders.Length - 1 do
              let struct (mesh, material, _) = meshMaterialShaders[mi]

              buffer.Add(
                Command3D.DrawInstanced(
                  mesh,
                  snapshot,
                  ValueNone,
                  material,
                  count,
                  0,
                  0
                )
              )
          | ValueNone ->
            let meshesAndMaterials = this.GetMeshesAndMaterial sample

            for mi = 0 to meshesAndMaterials.Length - 1 do
              let struct (mesh, material) = meshesAndMaterials[mi]

              buffer.Add(
                Command3D.DrawInstanced(
                  mesh,
                  snapshot,
                  ValueNone,
                  material,
                  count,
                  0,
                  0
                )
              )

        match scope with
        | ValueSome _ -> buffer.Add(Command3D.EndEffect)
        | ValueNone -> ()

/// <summary>
/// Cell-grid and hex-grid renderers for the MonoGame backend. Mirrors
/// <c>Mibo.Raylib/Layout3D/Renderer3D.fs</c> at the renderer-glue layer; layout logic
/// is reused from <c>Mibo.Core.Layout3D</c>.
/// </summary>
module CellGridRenderer3D =

  let inline render
    (grid: CellGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> CellGrid3D.iter(fun x y z content ->
      // §5: convert Numerics→XNA at the Core boundary.
      let worldPos =
        CellGrid3D.getWorldPos x y z grid |> Conversions.fromNumericsVector3

      renderCell worldPos content)

  let inline renderVolume
    (bounds: Mibo.Layout3D.BoundingBox)
    (grid: CellGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> CellGrid3D.iterVolume bounds (fun x y z content ->
      let worldPos =
        CellGrid3D.getWorldPos x y z grid |> Conversions.fromNumericsVector3

      renderCell worldPos content)

  let inline renderWithIndices
    (grid: CellGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: int -> int -> int -> Vector3 -> 'T -> unit)
    : unit =
    grid
    |> CellGrid3D.iter(fun x y z content ->
      let worldPos =
        CellGrid3D.getWorldPos x y z grid |> Conversions.fromNumericsVector3

      renderCell x y z worldPos content)

  /// <summary>
  /// Renders a cell grid using GPU instancing. Cells are grouped by a key function,
  /// and each group emits one <c>DrawInstanced</c> per sub-mesh.
  /// </summary>
  let renderInstanced
    (ctx: InstancedRenderContext<'T, 'K>)
    (grid: CellGrid3D<'T>)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> CellGrid3D.iter(fun x y z content ->
      let worldPos =
        CellGrid3D.getWorldPos x y z grid |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer

  /// <summary>
  /// Like <c>renderInstanced</c> but restricted to a bounding volume.
  /// </summary>
  let renderVolumeInstanced
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: Mibo.Layout3D.BoundingBox)
    (grid: CellGrid3D<'T>)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> CellGrid3D.iterVolume bounds (fun x y z content ->
      let worldPos =
        CellGrid3D.getWorldPos x y z grid |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer

  /// <summary>
  /// Like <c>renderInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>. A <c>ValueNone</c> key uses the default PBR path.
  /// Whole-grid shading: pass <c>fun _ -> ValueSome effect</c>.
  /// </summary>
  let renderInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (grid: CellGrid3D<'T>)
    (shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> CellGrid3D.iter(fun x y z content ->
      let worldPos =
        CellGrid3D.getWorldPos x y z grid |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)

  /// <summary>
  /// Like <c>renderVolumeInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>.
  /// </summary>
  let renderVolumeInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: Mibo.Layout3D.BoundingBox)
    (grid: CellGrid3D<'T>)
    (shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> CellGrid3D.iterVolume bounds (fun x y z content ->
      let worldPos =
        CellGrid3D.getWorldPos x y z grid |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)


/// <summary>
/// Hex-grid renderers for the MonoGame backend. Mirrors the cell-grid renderers but for
/// <see cref="T:Mibo.Layout3D.HexGrid3D"/>; layout logic is reused from <c>Mibo.Core.Layout3D</c>.
/// </summary>
module HexGrid3DRenderer =

  let inline render
    (grid: HexGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> HexGrid3D.iter(fun col row layer content ->
      let worldPos =
        HexGrid3D.getWorldPos col row layer grid
        |> Conversions.fromNumericsVector3

      renderCell worldPos content)

  let inline renderVolume
    (bounds: Mibo.Layout3D.BoundingBox)
    (grid: HexGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> HexGrid3D.iterVolume bounds (fun col row layer content ->
      let worldPos =
        HexGrid3D.getWorldPos col row layer grid
        |> Conversions.fromNumericsVector3

      renderCell worldPos content)

  let inline renderWithIndices
    (grid: HexGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: int -> int -> int -> Vector3 -> 'T -> unit)
    : unit =
    grid
    |> HexGrid3D.iter(fun col row layer content ->
      let worldPos =
        HexGrid3D.getWorldPos col row layer grid
        |> Conversions.fromNumericsVector3

      renderCell col row layer worldPos content)

  /// <summary>
  /// Renders a hex grid using GPU instancing. Cells are grouped by a key function,
  /// and each group emits one <c>DrawInstanced</c> per sub-mesh.
  /// </summary>
  let renderInstanced
    (ctx: InstancedRenderContext<'T, 'K>)
    (grid: HexGrid3D<'T>)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> HexGrid3D.iter(fun col row layer content ->
      let worldPos =
        HexGrid3D.getWorldPos col row layer grid
        |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer

  /// <summary>
  /// Like <c>renderInstanced</c> but restricted to a bounding volume.
  /// </summary>
  let renderVolumeInstanced
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: Mibo.Layout3D.BoundingBox)
    (grid: HexGrid3D<'T>)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> HexGrid3D.iterVolume bounds (fun col row layer content ->
      let worldPos =
        HexGrid3D.getWorldPos col row layer grid
        |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer

  /// <summary>
  /// Like <c>renderInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>. A <c>ValueNone</c> key uses the default PBR path.
  /// Whole-grid shading: pass <c>fun _ -> ValueSome effect</c>.
  /// </summary>
  let renderInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (grid: HexGrid3D<'T>)
    (shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> HexGrid3D.iter(fun col row layer content ->
      let worldPos =
        HexGrid3D.getWorldPos col row layer grid
        |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)

  /// <summary>
  /// Like <c>renderVolumeInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>.
  /// </summary>
  let renderVolumeInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: Mibo.Layout3D.BoundingBox)
    (grid: HexGrid3D<'T>)
    (shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> HexGrid3D.iterVolume bounds (fun col row layer content ->
      let worldPos =
        HexGrid3D.getWorldPos col row layer grid
        |> Conversions.fromNumericsVector3

      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)


// ─────────────────────────────────────────────────────────────────────────────
// Fluent Draw DSL entry points for grid instancing.
//
// These live on InstancedRenderContext (not on RenderBuffer3D) because F#'s
// SRTP member-constraint resolution only sees type-augmentations defined in
// the type's own declaration file. The context is declared in this file, so
// members added here are visible to the Core Draw SRTP constraints. They
// delegate to the CellGridRenderer3D/HexGrid3DRenderer functions below.
// ─────────────────────────────────────────────────────────────────────────────

type InstancedRenderContext<'T, 'K when 'K: equality> with

  /// <summary>Emit instanced draw commands for every occupied cell of <paramref name="grid"/>,
  /// shaded by the default PBR instanced path.</summary>
  member ctx.RenderCellGridInstanced(buffer, grid: CellGrid3D<'T>) =
    CellGridRenderer3D.renderInstanced ctx grid buffer

  /// <summary>Emit instanced draw commands for every occupied cell of <paramref name="grid"/>,
  /// grouping cells by <paramref name="shaderForKey"/>: cells whose key maps to an effect are
  /// shaded by it (when it opts into instancing), keys mapped to ValueNone keep the default PBR
  /// instanced path. See docs/graphics3d/instancing.md.</summary>
  member ctx.RenderCellGridInstanced
    (
      buffer,
      grid: CellGrid3D<'T>,
      shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption
    ) =
    CellGridRenderer3D.renderInstancedWithEffect ctx grid shaderForKey buffer

  /// <summary>Emit instanced draw commands for the occupied cells of <paramref name="grid"/>
  /// inside <paramref name="bounds"/>, shaded by the default PBR instanced path.</summary>
  member ctx.RenderCellGridVolumeInstanced
    (buffer, bounds: Mibo.Layout3D.BoundingBox, grid: CellGrid3D<'T>)
    =
    CellGridRenderer3D.renderVolumeInstanced ctx bounds grid buffer

  /// <summary>Emit instanced draw commands for the occupied cells of <paramref name="grid"/>
  /// inside <paramref name="bounds"/>, grouping cells by <paramref name="shaderForKey"/>:
  /// cells whose key maps to an effect are shaded by it (when it opts into instancing), keys
  /// mapped to ValueNone keep the default PBR instanced path. See docs/graphics3d/instancing.md.</summary>
  member ctx.RenderCellGridVolumeInstanced
    (
      buffer,
      bounds: Mibo.Layout3D.BoundingBox,
      grid: CellGrid3D<'T>,
      shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption
    ) =
    CellGridRenderer3D.renderVolumeInstancedWithEffect
      ctx
      bounds
      grid
      shaderForKey
      buffer

  /// <summary>Emit instanced draw commands for every occupied cell of the hex
  /// <paramref name="grid"/>, shaded by the default PBR instanced path.</summary>
  member ctx.RenderHexGridInstanced(buffer, grid: HexGrid3D<'T>) =
    HexGrid3DRenderer.renderInstanced ctx grid buffer

  /// <summary>Emit instanced draw commands for every occupied cell of the hex
  /// <paramref name="grid"/>, grouping cells by <paramref name="shaderForKey"/>: cells whose key
  /// maps to an effect are shaded by it (when it opts into instancing), keys mapped to ValueNone
  /// keep the default PBR instanced path. See docs/graphics3d/instancing.md.</summary>
  member ctx.RenderHexGridInstanced
    (
      buffer,
      grid: HexGrid3D<'T>,
      shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption
    ) =
    HexGrid3DRenderer.renderInstancedWithEffect ctx grid shaderForKey buffer

  /// <summary>Emit instanced draw commands for the occupied cells of the hex
  /// <paramref name="grid"/> inside <paramref name="bounds"/>, shaded by the default PBR
  /// instanced path.</summary>
  member ctx.RenderHexGridVolumeInstanced
    (buffer, bounds: Mibo.Layout3D.BoundingBox, grid: HexGrid3D<'T>)
    =
    HexGrid3DRenderer.renderVolumeInstanced ctx bounds grid buffer

  /// <summary>Emit instanced draw commands for the occupied cells of the hex
  /// <paramref name="grid"/> inside <paramref name="bounds"/>, grouping cells by
  /// <paramref name="shaderForKey"/>: cells whose key maps to an effect are shaded by it (when
  /// it opts into instancing), keys mapped to ValueNone keep the default PBR instanced path.
  /// See docs/graphics3d/instancing.md.</summary>
  member ctx.RenderHexGridVolumeInstanced
    (
      buffer,
      bounds: Mibo.Layout3D.BoundingBox,
      grid: HexGrid3D<'T>,
      shaderForKey: 'K -> Microsoft.Xna.Framework.Graphics.Effect voption
    ) =
    HexGrid3DRenderer.renderVolumeInstancedWithEffect
      ctx
      bounds
      grid
      shaderForKey
      buffer
