namespace Mibo.Layout3D

open System.Buffers
open System.Collections.Generic
open System.Numerics
open System.Runtime.InteropServices
open Mibo.Elmish.Graphics3D

/// <summary>
/// Contextual object for instanced cell grid rendering.
/// Bundles the key/material/transform functions and manages internal reusable
/// storage and snapshot pooling to avoid per-frame allocations.
/// </summary>
type InstancedRenderContext<'T, 'K when 'K: equality>
  (
    [<InlineIfLambda>] getKey: 'T -> 'K,
    [<InlineIfLambda>] getMeshesAndMaterial:
      'T -> struct (Raylib_cs.Mesh * Material3D)[],
    [<InlineIfLambda>] getTransform: Vector3 -> 'T -> Matrix4x4
  ) =

  let storage = Dictionary<'K, struct (ResizeArray<Matrix4x4> * 'T)>()
  let snapshotPool = ResizeArray<struct (Matrix4x4[] * int)>()

  member internal _.Storage = storage
  member internal _.SnapshotPool = snapshotPool
  member _.GetKey = getKey
  member _.GetMeshesAndMaterial = getMeshesAndMaterial
  member _.GetTransform = getTransform

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
      ArrayPool<Matrix4x4>.Shared.Return arr

    snapshotPool.Clear()

  member internal this.EmitInstanced(buffer: RenderBuffer3D) =
    let groups = this.Storage
    let snapshots = this.SnapshotPool

    for KeyValue(_, struct (transforms, sample)) in groups do
      if transforms.Count > 0 then
        let count = transforms.Count
        let snapshot = ArrayPool<Matrix4x4>.Shared.Rent count
        let span = CollectionsMarshal.AsSpan transforms

        for i = 0 to count - 1 do
          snapshot[i] <- span[i]

        snapshots.Add struct (snapshot, count)
        let meshesAndMaterials = this.GetMeshesAndMaterial sample

        for mi = 0 to meshesAndMaterials.Length - 1 do
          let struct (mesh, material) = meshesAndMaterials[mi]

          buffer.Add(Command3D.drawMeshInstanced mesh snapshot material count)

module CellGridRenderer3D =

  let inline render
    (grid: CellGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> CellGrid3D.iter(fun x y z content ->
      let worldPos = CellGrid3D.getWorldPos x y z grid
      renderCell worldPos content)

  let inline renderVolume
    (bounds: BoundingBox)
    (grid: CellGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> CellGrid3D.iterVolume bounds (fun x y z content ->
      let worldPos = CellGrid3D.getWorldPos x y z grid
      renderCell worldPos content)

  let inline renderWithIndices
    (grid: CellGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: int -> int -> int -> Vector3 -> 'T -> unit)
    : unit =
    grid
    |> CellGrid3D.iter(fun x y z content ->
      let worldPos = CellGrid3D.getWorldPos x y z grid
      renderCell x y z worldPos content)

  /// <summary>
  /// Renders a cell grid using GPU instancing. Cells are grouped by a key function,
  /// and each group emits one <c>DrawMeshInstanced</c> per sub-mesh.
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
      let worldPos = CellGrid3D.getWorldPos x y z grid
      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix4x4>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer

  /// <summary>
  /// Like <c>renderInstanced</c> but restricted to a bounding volume.
  /// </summary>
  let renderVolumeInstanced
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: BoundingBox)
    (grid: CellGrid3D<'T>)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> CellGrid3D.iterVolume bounds (fun x y z content ->
      let worldPos = CellGrid3D.getWorldPos x y z grid
      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix4x4>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer

module HexGrid3DRenderer =

  let inline render
    (grid: HexGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> HexGrid3D.iter(fun col row layer content ->
      let worldPos = HexGrid3D.getWorldPos col row layer grid
      renderCell worldPos content)

  let inline renderVolume
    (bounds: BoundingBox)
    (grid: HexGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: Vector3 -> 'T -> unit)
    : unit =
    grid
    |> HexGrid3D.iterVolume bounds (fun col row layer content ->
      let worldPos = HexGrid3D.getWorldPos col row layer grid
      renderCell worldPos content)

  let inline renderWithIndices
    (grid: HexGrid3D<'T>)
    ([<InlineIfLambda>] renderCell: int -> int -> int -> Vector3 -> 'T -> unit)
    : unit =
    grid
    |> HexGrid3D.iter(fun col row layer content ->
      let worldPos = HexGrid3D.getWorldPos col row layer grid
      renderCell col row layer worldPos content)

  /// <summary>
  /// Renders a hex grid using GPU instancing. Cells are grouped by a key function,
  /// and each group emits one <c>DrawMeshInstanced</c> per sub-mesh.
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
      let worldPos = HexGrid3D.getWorldPos col row layer grid
      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix4x4>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer

  /// <summary>
  /// Like <c>renderInstanced</c> but restricted to a bounding volume.
  /// </summary>
  let renderVolumeInstanced
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: BoundingBox)
    (grid: HexGrid3D<'T>)
    (buffer: RenderBuffer3D)
    : unit =
    let groups = ctx.Storage

    for kvp in groups do
      let struct (transforms, _) = kvp.Value
      transforms.Clear()

    grid
    |> HexGrid3D.iterVolume bounds (fun col row layer content ->
      let worldPos = HexGrid3D.getWorldPos col row layer grid
      let key = ctx.GetKey content
      let transform = ctx.GetTransform worldPos content

      match groups.TryGetValue key with
      | true, struct (transforms, _) -> transforms.Add transform
      | false, _ ->
        let list = ResizeArray<Matrix4x4>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstanced buffer
