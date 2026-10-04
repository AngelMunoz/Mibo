#nowarn "44"
namespace Mibo.Layout3D

open System.Buffers
open System.Collections.Generic
open System.Numerics
open System.Runtime.InteropServices
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Layout

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

  // Rectangle transform. ValueNone on the primary ctor; the `Rect` factory
  // installs a ValueSome. Every emit path then hands the anchor's rectangle to
  // it, so a game scales one model over the cells its instance covers instead
  // of re-deriving the size from the cell.
  let mutable rectTransform: (CellRect -> Vector3 -> 'T -> Matrix4x4) voption =
    ValueNone

  member internal _.Storage = storage
  member internal _.SnapshotPool = snapshotPool
  member _.GetKey = getKey
  member _.GetMeshesAndMaterial = getMeshesAndMaterial
  member _.GetTransform = getTransform

  /// <summary>Installs the rectangle transform. Internal — set by the
  /// <c>Rect</c> factory.</summary>
  member internal _.SetRectTransform f = rectTransform <- ValueSome f

  member internal _.RectTransform = rectTransform

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
  /// Builds a context whose transform receives the rectangle each instance
  /// covers — the anchor's own cell on the cell-walk members, the anchor's
  /// rectangle on the occupancy members — so one model scales to the cells it
  /// stands for:
  /// <c>InstancedRenderContext&lt;Cell, string&gt;.Rect(getKey, getMeshesAndMaterial, getTransform)</c>.
  /// </summary>
  static member Rect
    (
      getKey: 'T -> 'K,
      getMeshesAndMaterial: 'T -> struct (Raylib_cs.Mesh * Material3D)[],
      getTransform: CellRect -> Vector3 -> 'T -> Matrix4x4
    ) : InstancedRenderContext<'T, 'K> =
    // the primary transform is a tripwire: this factory installs the rectangle
    // transform before it hands the context out, so no emit path reads it
    let context =
      InstancedRenderContext(
        getKey,
        getMeshesAndMaterial,
        (fun _ _ ->
          invalidOp
            "InstancedRenderContext.Rect installs the rectangle transform before it returns")
      )

    context.SetRectTransform getTransform
    context

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

  member private _.ClearGroups() =
    for KeyValueV(_, struct (transforms, _)) in storage do
      transforms.Clear()

  /// <summary>
  /// Adds one anchor as an instance: the transform function receives the
  /// rectangle the instance covers and the anchor's base world position — the
  /// cell lifted to <c>y = 0</c>.
  /// </summary>
  member private _.AddCell (grid: CellGrid2D<'T>) (rect: CellRect) x y content =
    let footprint = CellGrid2D.getWorldPos x y grid
    let basePos = Vector3(footprint.X, 0f, footprint.Y)
    let key = getKey content

    let transform =
      match rectTransform with
      | ValueSome withRect -> withRect rect basePos content
      | ValueNone -> getTransform basePos content

    match Dictionary.tryGetValue key storage with
    | ValueSome struct (transforms, _) -> transforms.Add transform
    | ValueNone ->
      let list = ResizeArray<_>()
      list.Add transform
      storage[key] <- struct (list, content)

  /// <summary>
  /// Fills the context from every populated cell of a 2D footprint grid —
  /// square or hex — and emits the instanced draws, one per key. This is
  /// the heightmap replacement for the retired whole-volume renderers. Each
  /// cell is its own instance, so a cell that covers several cells draws at
  /// one cell; use the occupancy member for those.
  /// </summary>
  member this.RenderInstanced(buffer: RenderBuffer3D, grid: CellGrid2D<'T>) =
    this.ClearGroups()

    CellGrid2D.iter
      (fun x y content ->
        this.AddCell grid { X = x; Y = y; W = 1; H = 1 } x y content)
      grid

    this.EmitInstanced(buffer)

  /// <summary>
  /// <c>RenderInstanced</c> over the anchors of an occupancy: one instance per
  /// anchor, and each anchor's rectangle reaches the transform. A covered cell
  /// draws nothing of its own, so a plate stays one instance.
  /// </summary>
  member this.RenderInstanced
    (buffer: RenderBuffer3D, grid: CellGrid2D<'T>, occupancy: Occupancy)
    =
    this.ClearGroups()

    for i in 0 .. occupancy.Cells.Length - 1 do
      let at = occupancy.Cells.[i]

      match CellGrid2D.get at.X at.Y grid with
      | ValueSome content ->
        this.AddCell grid occupancy.Rects.[i] at.X at.Y content
      | ValueNone -> ()

    this.EmitInstanced(buffer)

  /// <summary>
  /// Like <c>RenderInstanced</c> but restricted to a world-space window
  /// (left/top/right/bottom in <c>int</c> world coordinates). Hex grids
  /// cull with an orientation-aware window. A cell-windowed walk drops a
  /// large instance once its anchor leaves the window: use the occupancy
  /// member for a map with spans.
  /// </summary>
  member this.RenderWindowInstanced
    (
      buffer: RenderBuffer3D,
      left: int,
      top: int,
      right: int,
      bottom: int,
      grid: CellGrid2D<'T>
    ) =
    this.ClearGroups()

    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y content ->
        this.AddCell grid { X = x; Y = y; W = 1; H = 1 } x y content)
      grid

    this.EmitInstanced(buffer)

  /// <summary>
  /// <c>RenderWindowInstanced</c> over the anchors of an occupancy: the
  /// world-space window becomes a cell range, and every anchor whose rectangle
  /// intersects it is drawn — so an instance that covers a window cell from an
  /// anchor outside the window stays drawn.
  /// </summary>
  member this.RenderWindowInstanced
    (
      buffer: RenderBuffer3D,
      left: int,
      top: int,
      right: int,
      bottom: int,
      grid: CellGrid2D<'T>,
      occupancy: Occupancy
    ) =
    this.ClearGroups()

    let struct (startX, endX, startY, endY) =
      CellGrid2D.visibleRange left top right bottom grid

    Occupancy.iterInWindow
      startX
      startY
      endX
      endY
      (fun at rect ->
        match CellGrid2D.get at.X at.Y grid with
        | ValueSome content -> this.AddCell grid rect at.X at.Y content
        | ValueNone -> ())
      occupancy

    this.EmitInstanced(buffer)

  /// <summary>
  /// <c>RenderInstanced</c> wrapping each key's draws in a
  /// <c>BeginEffect</c>/<c>EndEffect</c> scope when
  /// <paramref name="shaderForKey"/> returns <c>ValueSome</c>.
  /// </summary>
  member this.RenderInstancedWithEffect
    (
      buffer: RenderBuffer3D,
      grid: CellGrid2D<'T>,
      shaderForKey: 'K -> Raylib_cs.Shader voption
    ) =
    this.ClearGroups()

    CellGrid2D.iter
      (fun x y content ->
        this.AddCell grid { X = x; Y = y; W = 1; H = 1 } x y content)
      grid

    this.EmitInstancedWithEffect(buffer, shaderForKey)

  /// <summary>
  /// <c>RenderInstanced</c> with an occupancy and per-key effect scoping, like
  /// <c>RenderInstancedWithEffect</c>.
  /// </summary>
  member this.RenderInstancedWithEffect
    (
      buffer: RenderBuffer3D,
      grid: CellGrid2D<'T>,
      occupancy: Occupancy,
      shaderForKey: 'K -> Raylib_cs.Shader voption
    ) =
    this.ClearGroups()

    for i in 0 .. occupancy.Cells.Length - 1 do
      let at = occupancy.Cells.[i]

      match CellGrid2D.get at.X at.Y grid with
      | ValueSome content ->
        this.AddCell grid occupancy.Rects.[i] at.X at.Y content
      | ValueNone -> ()

    this.EmitInstancedWithEffect(buffer, shaderForKey)

  /// <summary>
  /// <c>RenderWindowInstanced</c> with per-key effect scoping, like
  /// <c>RenderInstancedWithEffect</c>.
  /// </summary>
  member this.RenderWindowInstancedWithEffect
    (
      buffer: RenderBuffer3D,
      left: int,
      top: int,
      right: int,
      bottom: int,
      grid: CellGrid2D<'T>,
      shaderForKey: 'K -> Raylib_cs.Shader voption
    ) =
    this.ClearGroups()

    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y content ->
        this.AddCell grid { X = x; Y = y; W = 1; H = 1 } x y content)
      grid

    this.EmitInstancedWithEffect(buffer, shaderForKey)

  /// <summary>
  /// <c>RenderWindowInstanced</c> with an occupancy and per-key effect
  /// scoping.
  /// </summary>
  member this.RenderWindowInstancedWithEffect
    (
      buffer: RenderBuffer3D,
      left: int,
      top: int,
      right: int,
      bottom: int,
      grid: CellGrid2D<'T>,
      occupancy: Occupancy,
      shaderForKey: 'K -> Raylib_cs.Shader voption
    ) =
    this.ClearGroups()

    let struct (startX, endX, startY, endY) =
      CellGrid2D.visibleRange left top right bottom grid

    Occupancy.iterInWindow
      startX
      startY
      endX
      endY
      (fun at rect ->
        match CellGrid2D.get at.X at.Y grid with
        | ValueSome content -> this.AddCell grid rect at.X at.Y content
        | ValueNone -> ())
      occupancy

    this.EmitInstancedWithEffect(buffer, shaderForKey)

  member internal this.EmitInstanced(buffer: RenderBuffer3D) =
    let groups = this.Storage
    let snapshots = this.SnapshotPool

    for KeyValueV(_, struct (transforms, sample)) in groups do
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

    for KeyValueV(key, struct (transforms, sample)) in groups do
      if transforms.Count > 0 then
        let count = transforms.Count
        let snapshot = ArrayPool<Matrix4x4>.Shared.Rent count
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
        match this.PerMeshShaderResolver with
        | ValueSome triples ->
          let meshMaterialShaders = triples sample

          for mi = 0 to meshMaterialShaders.Length - 1 do
            let struct (mesh, material, _) = meshMaterialShaders[mi]

            buffer.Add(Command3D.drawMeshInstanced mesh snapshot material count)
        | ValueNone ->
          let meshesAndMaterials = this.GetMeshesAndMaterial sample

          for mi = 0 to meshesAndMaterials.Length - 1 do
            let struct (mesh, material) = meshesAndMaterials[mi]

            buffer.Add(Command3D.drawMeshInstanced mesh snapshot material count)

        match scope with
        | ValueSome _ -> buffer.Add(Command3D.EndEffect)
        | ValueNone -> ()

[<System.Obsolete("Author 3D as a 2D grid with per-column height; render from your own instance data")>]
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
        let list = ResizeArray<Matrix4x4>()
        list.Add transform
        groups[key] <- struct (list, content))

    ctx.EmitInstancedWithEffect(buffer, shaderForKey)

[<System.Obsolete("Author 3D as a 2D grid with per-column height; render from your own instance data")>]
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
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

      match Dictionary.tryGetValue key groups with
      | ValueSome struct (transforms, _) -> transforms.Add transform
      | ValueNone ->
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

  /// <summary>Emit instanced draw commands for every occupied cell of <paramref name="grid"/>,
  /// shaded by the default PBR instanced path.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
  member ctx.RenderCellGridInstanced(buffer, grid: CellGrid3D<'T>) =
    CellGridRenderer3D.renderInstanced ctx grid buffer

  /// <summary>Emit instanced draw commands for every occupied cell of <paramref name="grid"/>,
  /// grouping cells by <paramref name="shaderForKey"/>: cells whose key maps to a shader are
  /// shaded by it (when it opts into instancing), keys mapped to ValueNone keep the default PBR
  /// instanced path. See docs/graphics3d/instancing.md.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
  member ctx.RenderCellGridInstanced
    (buffer, grid: CellGrid3D<'T>, shaderForKey: 'K -> Raylib_cs.Shader voption)
    =
    CellGridRenderer3D.renderInstancedWithEffect ctx grid shaderForKey buffer

  /// <summary>Emit instanced draw commands for the occupied cells of <paramref name="grid"/>
  /// inside <paramref name="bounds"/>, shaded by the default PBR instanced path.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
  member ctx.RenderCellGridVolumeInstanced
    (buffer, bounds: BoundingBox, grid: CellGrid3D<'T>)
    =
    CellGridRenderer3D.renderVolumeInstanced ctx bounds grid buffer

  /// <summary>Emit instanced draw commands for the occupied cells of <paramref name="grid"/>
  /// inside <paramref name="bounds"/>, grouping cells by <paramref name="shaderForKey"/>:
  /// cells whose key maps to a shader are shaded by it (when it opts into instancing), keys
  /// mapped to ValueNone keep the default PBR instanced path. See docs/graphics3d/instancing.md.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
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

  /// <summary>Emit instanced draw commands for every occupied cell of the hex
  /// <paramref name="grid"/>, shaded by the default PBR instanced path.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
  member ctx.RenderHexGridInstanced(buffer, grid: HexGrid3D<'T>) =
    HexGrid3DRenderer.renderInstanced ctx grid buffer

  /// <summary>Emit instanced draw commands for every occupied cell of the hex
  /// <paramref name="grid"/>, grouping cells by <paramref name="shaderForKey"/>: cells whose key
  /// maps to a shader are shaded by it (when it opts into instancing), keys mapped to ValueNone
  /// keep the default PBR instanced path. See docs/graphics3d/instancing.md.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
  member ctx.RenderHexGridInstanced
    (buffer, grid: HexGrid3D<'T>, shaderForKey: 'K -> Raylib_cs.Shader voption)
    =
    HexGrid3DRenderer.renderInstancedWithEffect ctx grid shaderForKey buffer

  /// <summary>Emit instanced draw commands for the occupied cells of the hex
  /// <paramref name="grid"/> inside <paramref name="bounds"/>, shaded by the default PBR
  /// instanced path.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
  member ctx.RenderHexGridVolumeInstanced
    (buffer, bounds: BoundingBox, grid: HexGrid3D<'T>)
    =
    HexGrid3DRenderer.renderVolumeInstanced ctx bounds grid buffer

  /// <summary>Emit instanced draw commands for the occupied cells of the hex
  /// <paramref name="grid"/> inside <paramref name="bounds"/>, grouping cells by
  /// <paramref name="shaderForKey"/>: cells whose key maps to a shader are shaded by it (when
  /// it opts into instancing), keys mapped to ValueNone keep the default PBR instanced path.
  /// See docs/graphics3d/instancing.md.</summary>
  [<System.Obsolete("Render a CellGrid2D footprint grid instead: RenderInstanced and RenderWindowInstanced")>]
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
