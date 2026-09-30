namespace Mibo.Layout

open System.Numerics

/// Hex grids are `CellGrid2D` grids with hex geometry. Author them with
/// `CellGrid2D.createHex` and the Flow DSL.
[<System.Obsolete("Hex grids are CellGrid2D with hex geometry: build with CellGrid2D.createHex and author with the Flow DSL")>]
type HexGrid<'T> = CellGrid2D<'T>

/// Compatibility surface for code written against the retired hex grid
/// record. Every function delegates to `CellGrid2D`.
[<System.Obsolete("Hex grids are CellGrid2D with hex geometry: build with CellGrid2D.createHex and author with the Flow DSL")>]
module HexGrid =

  /// Creates a hex grid. Prefer `CellGrid2D.createHex`.
  let create
    width
    height
    (size: float32)
    (origin: Vector2)
    (orientation: HexOrientation)
    : HexGrid<'T> =
    let geometry =
      match orientation with
      | PointyTop -> CellGeometry.PointyTopHex
      | FlatTop -> CellGeometry.FlatTopHex

    CellGrid2D.createHex geometry size width height origin

  let inline set col row (content: 'T) (grid: HexGrid<'T>) : unit =
    CellGrid2D.set col row content grid

  let inline get col row (grid: HexGrid<'T>) : 'T voption =
    CellGrid2D.get col row grid

  let inline clear col row (grid: HexGrid<'T>) : unit =
    CellGrid2D.clear col row grid

  let inline getWorldPos col row (grid: HexGrid<'T>) : Vector2 =
    CellGrid2D.getWorldPos col row grid

  let inline iter
    ([<InlineIfLambda>] action: int -> int -> 'T -> unit)
    (grid: HexGrid<'T>)
    : unit =
    CellGrid2D.iter action grid

  /// Iterates the cells that intersect a pixel rect, with hex-aware
  /// culling for both orientations.
  let inline iterVisible
    (left: float32)
    (top: float32)
    (right: float32)
    (bottom: float32)
    ([<InlineIfLambda>] action: int -> int -> 'T -> unit)
    (grid: HexGrid<'T>)
    : unit =
    let hexW = grid.CellSize.X
    let hexH = grid.CellSize.Y

    let startCol, endCol, startRow, endRow =
      match CellGrid2D.hexOrientation grid with
      | PointyTop ->
        let sc = max 0 (int((left - grid.Origin.X) / hexW) - 1)

        let ec = min (grid.Width - 1) (int((right - grid.Origin.X) / hexW) + 1)

        let sr = max 0 (int((top - grid.Origin.Y) / (hexH * 0.75f)) - 1)

        let er =
          min
            (grid.Height - 1)
            (int((bottom - grid.Origin.Y) / (hexH * 0.75f)) + 1)

        sc, ec, sr, er
      | FlatTop ->
        let sc = max 0 (int((left - grid.Origin.X) / (hexW * 0.75f)) - 1)

        let ec =
          min
            (grid.Width - 1)
            (int((right - grid.Origin.X) / (hexW * 0.75f)) + 1)

        let sr = max 0 (int((top - grid.Origin.Y) / hexH) - 1)

        let er =
          min (grid.Height - 1) (int((bottom - grid.Origin.Y) / hexH) + 1)

        sc, ec, sr, er

    let w = grid.Width

    for row in startRow..endRow do
      let rowOffset = row * w

      for col in startCol..endCol do
        let idx = rowOffset + col

        match grid.Cells.[idx] with
        | ValueSome content -> action col row content
        | ValueNone -> ()
