namespace Mibo.Layout

open System.Numerics

[<Struct>]
type HexOrientation =
  | PointyTop
  | FlatTop

[<Struct>]
type HexGrid<'T> = {
  Origin: Vector2
  Size: float32
  Orientation: HexOrientation
  Width: int
  Height: int
  Cells: 'T voption[]
}

module HexGrid =
  let inline private toIndex col row width = col + row * width

  let inline private hexDimensions
    (size: float32)
    (orientation: HexOrientation)
    =
    match orientation with
    | PointyTop -> struct (size * sqrt 3f, size * 2f)
    | FlatTop -> struct (size * 2f, size * sqrt 3f)

  let create
    width
    height
    (size: float32)
    (origin: Vector2)
    (orientation: HexOrientation)
    : HexGrid<'T> =
    {
      Origin = origin
      Size = size
      Orientation = orientation
      Width = width
      Height = height
      Cells = Array.create (width * height) ValueNone
    }

  let inline set col row (content: 'T) (grid: HexGrid<'T>) : unit =
    if col >= 0 && col < grid.Width && row >= 0 && row < grid.Height then
      let idx = toIndex col row grid.Width
      grid.Cells.[idx] <- ValueSome content

  let inline get col row (grid: HexGrid<'T>) : 'T voption =
    if col >= 0 && col < grid.Width && row >= 0 && row < grid.Height then
      let idx = toIndex col row grid.Width
      grid.Cells.[idx]
    else
      ValueNone

  let inline clear col row (grid: HexGrid<'T>) : unit =
    if col >= 0 && col < grid.Width && row >= 0 && row < grid.Height then
      let idx = toIndex col row grid.Width
      grid.Cells.[idx] <- ValueNone

  let inline getWorldPos col row (grid: HexGrid<'T>) : Vector2 =
    let struct (hexW, hexH) = hexDimensions grid.Size grid.Orientation

    match grid.Orientation with
    | PointyTop ->
      let x =
        grid.Origin.X
        + float32 col * hexW
        + (if row % 2 = 1 then hexW / 2f else 0f)

      let y = grid.Origin.Y + float32 row * hexH * 0.75f
      Vector2(x + hexW / 2f, y + hexH / 2f)
    | FlatTop ->
      let x = grid.Origin.X + float32 col * hexW * 0.75f

      let y =
        grid.Origin.Y
        + float32 row * hexH
        + (if col % 2 = 1 then hexH / 2f else 0f)

      Vector2(x + hexW / 2f, y + hexH / 2f)

  let inline iter
    ([<InlineIfLambda>] action: int -> int -> 'T -> unit)
    (grid: HexGrid<'T>)
    : unit =
    let w = grid.Width

    for i in 0 .. grid.Cells.Length - 1 do
      match grid.Cells.[i] with
      | ValueSome content ->
        let col = i % w
        let row = i / w
        action col row content
      | ValueNone -> ()

  let inline iterVisible
    (left: float32)
    (top: float32)
    (right: float32)
    (bottom: float32)
    ([<InlineIfLambda>] action: int -> int -> 'T -> unit)
    (grid: HexGrid<'T>)
    : unit =
    let struct (hexW, hexH) = hexDimensions grid.Size grid.Orientation

    let startCol, endCol, startRow, endRow =
      match grid.Orientation with
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
