namespace Mibo.Layout

open System.Numerics

/// The orientation of a hex grid's cells.
[<Struct>]
type HexOrientation =
  | PointyTop
  | FlatTop

/// The geometry of a `CellGrid2D`. Hexagons are stored as offset columns
/// and rows in the same rectangular array as squares, so cell storage and
/// layout authoring are identical; only world positions and spatial
/// queries differ.
[<Struct>]
type CellGeometry =
  | Square
  | Hex of orientation: HexOrientation

/// Hex construction parameters for `CellGrid2D.createHex`: the cell
/// orientation, the extents in cells, the hex radius (center to corner),
/// and the world-space origin.
[<Struct>]
type HexSpec = {
  Orientation: HexOrientation
  Width: int
  Height: int
  Radius: float32
  Origin: Vector2
}

/// A 2D grid of optional cells. Square by default; `CellGrid2D.createHex`
/// builds the hex variant over the same storage.
type CellGrid2D<'T> = {
  Origin: Vector2
  CellSize: Vector2
  Geometry: CellGeometry
  Width: int
  Height: int
  Cells: 'T voption[]
}

module CellGrid2D =
  let inline private toIndex x y width = x + y * width

  /// The width and height of a hexagon's bounding box from its radius.
  let inline private hexDimensions
    (size: float32)
    (orientation: HexOrientation)
    =
    match orientation with
    | PointyTop -> struct (size * sqrt 3f, size * 2f)
    | FlatTop -> struct (size * 2f, size * sqrt 3f)

  /// Creates a square grid. `cellSize` is the cell's width and height.
  let create
    width
    height
    (cellSize: Vector2)
    (origin: Vector2)
    : CellGrid2D<'T> =
    {
      Origin = origin
      CellSize = cellSize
      Geometry = CellGeometry.Square
      Width = width
      Height = height
      Cells = Array.create (width * height) ValueNone
    }

  /// Creates a hex grid: hexagons of the spec's `Radius` (center to corner)
  /// stored as offset columns and rows. `CellSize` holds the hexagon's
  /// bounding box.
  let createHex(spec: HexSpec) : CellGrid2D<'T> =
    let struct (hexW, hexH) = hexDimensions spec.Radius spec.Orientation

    {
      Origin = spec.Origin
      CellSize = Vector2(hexW, hexH)
      Geometry = CellGeometry.Hex spec.Orientation
      Width = spec.Width
      Height = spec.Height
      Cells = Array.create (spec.Width * spec.Height) ValueNone
    }

  /// The hex orientation of a hex-geometry grid. Throws for square grids.
  let hexOrientation(grid: CellGrid2D<'T>) : HexOrientation =
    match grid.Geometry with
    | CellGeometry.Hex orientation -> orientation
    | CellGeometry.Square ->
      invalidArg "grid" "a square grid carries no hex orientation"

  /// The hex radius (center to corner) of a hex-geometry grid. Throws for
  /// square grids.
  let hexRadius(grid: CellGrid2D<'T>) : float32 =
    match grid.Geometry with
    | CellGeometry.Hex PointyTop -> grid.CellSize.Y / 2f
    | CellGeometry.Hex FlatTop -> grid.CellSize.X / 2f
    | CellGeometry.Square ->
      invalidArg "grid" "a square grid carries no hex size"

  let inline set x y (content: 'T) (grid: CellGrid2D<'T>) : unit =
    if x >= 0 && x < grid.Width && y >= 0 && y < grid.Height then
      let idx = toIndex x y grid.Width
      grid.Cells.[idx] <- ValueSome content

  let inline get x y (grid: CellGrid2D<'T>) : 'T voption =
    if x >= 0 && x < grid.Width && y >= 0 && y < grid.Height then
      let idx = toIndex x y grid.Width
      grid.Cells.[idx]
    else
      ValueNone

  let inline clear x y (grid: CellGrid2D<'T>) : unit =
    if x >= 0 && x < grid.Width && y >= 0 && y < grid.Height then
      let idx = toIndex x y grid.Width
      grid.Cells.[idx] <- ValueNone

  /// The world position of a cell. Square grids step by `CellSize`; hex
  /// grids stagger every other row (pointy top) or column (flat top).
  let inline getWorldPos x y (grid: CellGrid2D<'T>) : Vector2 =
    match grid.Geometry with
    | CellGeometry.Square ->
      Vector2(
        grid.Origin.X + float32 x * grid.CellSize.X,
        grid.Origin.Y + float32 y * grid.CellSize.Y
      )
    | CellGeometry.Hex PointyTop ->
      let hexW = grid.CellSize.X
      let hexH = grid.CellSize.Y

      let px =
        grid.Origin.X + float32 x * hexW + (if y % 2 = 1 then hexW / 2f else 0f)

      let py = grid.Origin.Y + float32 y * hexH * 0.75f
      Vector2(px + hexW / 2f, py + hexH / 2f)
    | CellGeometry.Hex FlatTop ->
      let hexW = grid.CellSize.X
      let hexH = grid.CellSize.Y
      let px = grid.Origin.X + float32 x * hexW * 0.75f

      let py =
        grid.Origin.Y + float32 y * hexH + (if x % 2 = 1 then hexH / 2f else 0f)

      Vector2(px + hexW / 2f, py + hexH / 2f)

  let inline iter
    ([<InlineIfLambda>] action: int -> int -> 'T -> unit)
    (grid: CellGrid2D<'T>)
    : unit =
    let w = grid.Width

    for i in 0 .. grid.Cells.Length - 1 do
      match grid.Cells.[i] with
      | ValueSome content ->
        let x = i % w
        let y = i / w
        action x y content
      | ValueNone -> ()

  /// Iterates the cells that intersect a pixel rect. Square grids only —
  /// hex culling depends on the orientation, so hex grids cull with
  /// hex-aware code instead.
  let inline iterVisible
    (left: int)
    (top: int)
    (right: int)
    (bottom: int)
    ([<InlineIfLambda>] action: int -> int -> 'T -> unit)
    (grid: CellGrid2D<'T>)
    : unit =
    match grid.Geometry with
    | CellGeometry.Square -> ()
    | CellGeometry.Hex _ ->
      invalidArg
        "grid"
        "iterVisible culls square grids; hex grids need hex-aware culling"

    let startX = max 0 ((left - int grid.Origin.X) / int grid.CellSize.X)
    let startY = max 0 ((top - int grid.Origin.Y) / int grid.CellSize.Y)

    let endX =
      min (grid.Width - 1) ((right - int grid.Origin.X) / int grid.CellSize.X)

    let endY =
      min (grid.Height - 1) ((bottom - int grid.Origin.Y) / int grid.CellSize.Y)

    let w = grid.Width

    for y in startY..endY do
      let yOffset = y * w

      for x in startX..endX do
        let idx = yOffset + x

        match grid.Cells.[idx] with
        | ValueSome content -> action x y content
        | ValueNone -> ()
