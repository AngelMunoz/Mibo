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

  // Per-sub-mesh shader resolver. ValueNone on the primary ctor; the overload ctor
  // installs a ValueSome. EmitInstanced branches on it so a context built with the
  // triple-overload wraps each ValueSome sub-mesh draw in its own BeginEffect/EndEffect.
  let mutable perMeshShader
    : ('T -> struct (Raylib_cs.Mesh * Material3D * Raylib_cs.Shader voption)[]) voption =
    ValueNone

  member internal _.Storage = storage
  member internal _.SnapshotPool = snapshotPool
  member _.GetKey = getKey
  member _.GetMeshesAndMaterial = getMeshesAndMaterial
  member _.GetTransform = getTransform

  /// <summary>Installs the per-sub-mesh shader resolver. Internal — set by the
  /// overload constructor that returns (mesh, material, shader) triples.</summary>
  member internal _.SetPerMeshShaderResolver f = perMeshShader <- ValueSome f

  member internal _.PerMeshShaderResolver = perMeshShader

  /// <summary>
  /// Overload constructor for per-sub-mesh shaders: each triple may carry a
  /// <c>Shader voption</c>. A <c>ValueSome</c> shader wraps that sub-mesh's
  /// instanced draw in its own <c>BeginEffect</c>/<c>EndEffect</c> scope;
  /// <c>ValueNone</c> uses the default PBR instanced path. Existing
  /// two-element contexts are unaffected.
  /// </summary>
  new
    (
      getKey: 'T -> 'K,
      getMeshesMaterialAndShader:
        'T -> struct (Raylib_cs.Mesh * Material3D * Raylib_cs.Shader voption)[],
      getTransform: Vector3 -> 'T -> Matrix4x4
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

        match this.PerMeshShaderResolver with
        | ValueNone ->
          // Legacy path — one DrawMeshInstanced per sub-mesh, default PBR instanced shader.
          let meshesAndMaterials = this.GetMeshesAndMaterial sample

          for mi = 0 to meshesAndMaterials.Length - 1 do
            let struct (mesh, material) = meshesAndMaterials[mi]

            buffer.Add(Command3D.drawMeshInstanced mesh snapshot material count)
        | ValueSome triples ->
          // Per-sub-mesh path — wrap each ValueSome sub-mesh in its own BeginEffect/EndEffect.
          let arr = triples sample

          for mi = 0 to arr.Length - 1 do
            let struct (mesh, material, shader) = arr[mi]

            match shader with
            | ValueNone ->
              buffer.Add(
                Command3D.drawMeshInstanced mesh snapshot material count
              )
            | ValueSome s ->
              buffer.Add(Command3D.BeginEffect s)

              buffer.Add(
                Command3D.drawMeshInstanced mesh snapshot material count
              )

              buffer.Add(Command3D.EndEffect)

  /// <summary>
  /// Emits instanced draws with one <c>BeginEffect</c>/<c>EndEffect</c> scope per grid
  /// key when <paramref name="shaderForKey"/> returns <c>ValueSome</c>. <c>ValueNone</c>
  /// falls through to the default PBR instanced path for that key. Ignores any
  /// per-sub-mesh resolver to avoid nesting effect scopes.
  /// </summary>
  member internal this.EmitInstancedWithEffect
    (buffer: RenderBuffer3D, shaderForKey: 'K -> Raylib_cs.Shader voption)
    =
    let groups = this.Storage
    let snapshots = this.SnapshotPool

    for KeyValue(key, struct (transforms, sample)) in groups do
      if transforms.Count > 0 then
        let count = transforms.Count
        let snapshot = ArrayPool<Matrix4x4>.Shared.Rent count
        let span = CollectionsMarshal.AsSpan transforms

        for i = 0 to count - 1 do
          snapshot[i] <- span[i]

        snapshots.Add struct (snapshot, count)
        let meshesAndMaterials = this.GetMeshesAndMaterial sample

        match shaderForKey key with
        | ValueNone ->
          for mi = 0 to meshesAndMaterials.Length - 1 do
            let struct (mesh, material) = meshesAndMaterials[mi]

            buffer.Add(Command3D.drawMeshInstanced mesh snapshot material count)
        | ValueSome s ->
          buffer.Add(Command3D.BeginEffect s)

          for mi = 0 to meshesAndMaterials.Length - 1 do
            let struct (mesh, material) = meshesAndMaterials[mi]

            buffer.Add(Command3D.drawMeshInstanced mesh snapshot material count)

          buffer.Add(Command3D.EndEffect)

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

  /// <summary>
  /// Like <c>renderInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>. A <c>ValueNone</c> key uses the default PBR path.
  /// Whole-grid shading: pass <c>fun _ -> ValueSome shader</c>.
  /// </summary>
  let renderInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (grid: CellGrid3D<'T>)
    (shaderForKey: 'K -> Raylib_cs.Shader voption)
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

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)

  /// <summary>
  /// Like <c>renderVolumeInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>.
  /// </summary>
  let renderVolumeInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: BoundingBox)
    (grid: CellGrid3D<'T>)
    (shaderForKey: 'K -> Raylib_cs.Shader voption)
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

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)

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

  /// <summary>
  /// Like <c>renderInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>. A <c>ValueNone</c> key uses the default PBR path.
  /// Whole-grid shading: pass <c>fun _ -> ValueSome shader</c>.
  /// </summary>
  let renderInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (grid: HexGrid3D<'T>)
    (shaderForKey: 'K -> Raylib_cs.Shader voption)
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

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)

  /// <summary>
  /// Like <c>renderVolumeInstanced</c> but wraps each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when <paramref name="shaderForKey"/>
  /// returns <c>ValueSome</c>.
  /// </summary>
  let renderVolumeInstancedWithEffect
    (ctx: InstancedRenderContext<'T, 'K>)
    (bounds: BoundingBox)
    (grid: HexGrid3D<'T>)
    (shaderForKey: 'K -> Raylib_cs.Shader voption)
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

  member ctx.RenderCellGridInstanced(buffer, grid: CellGrid3D<'T>) =
    CellGridRenderer3D.renderInstanced ctx grid buffer

  member ctx.RenderCellGridInstanced
    (buffer, grid: CellGrid3D<'T>, shaderForKey: 'K -> Raylib_cs.Shader voption)
    =
    CellGridRenderer3D.renderInstancedWithEffect ctx grid shaderForKey buffer

  member ctx.RenderCellGridVolumeInstanced
    (buffer, bounds: BoundingBox, grid: CellGrid3D<'T>)
    =
    CellGridRenderer3D.renderVolumeInstanced ctx bounds grid buffer

  member ctx.RenderCellGridVolumeInstanced
    (
      buffer,
      bounds: BoundingBox,
      grid: CellGrid3D<'T>,
      shaderForKey: 'K -> Raylib_cs.Shader voption
    ) =
    CellGridRenderer3D.renderVolumeInstancedWithEffect
      ctx
      bounds
      grid
      shaderForKey
      buffer

  member ctx.RenderHexGridInstanced(buffer, grid: HexGrid3D<'T>) =
    HexGrid3DRenderer.renderInstanced ctx grid buffer

  member ctx.RenderHexGridInstanced
    (buffer, grid: HexGrid3D<'T>, shaderForKey: 'K -> Raylib_cs.Shader voption)
    =
    HexGrid3DRenderer.renderInstancedWithEffect ctx grid shaderForKey buffer

  member ctx.RenderHexGridVolumeInstanced
    (buffer, bounds: BoundingBox, grid: HexGrid3D<'T>)
    =
    HexGrid3DRenderer.renderVolumeInstanced ctx bounds grid buffer

  member ctx.RenderHexGridVolumeInstanced
    (
      buffer,
      bounds: BoundingBox,
      grid: HexGrid3D<'T>,
      shaderForKey: 'K -> Raylib_cs.Shader voption
    ) =
    HexGrid3DRenderer.renderVolumeInstancedWithEffect
      ctx
      bounds
      grid
      shaderForKey
      buffer
