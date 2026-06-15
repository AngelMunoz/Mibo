namespace Mibo.Layout

open System.Numerics

type CellGrid2D<'T> = {
  Origin: Vector2
  CellSize: Vector2
  Width: int
  Height: int
  Cells: 'T voption[]
}

module CellGrid2D =
  let inline private toIndex x y width = x + y * width

  let create
    width
    height
    (cellSize: Vector2)
    (origin: Vector2)
    : CellGrid2D<'T> =
    {
      Origin = origin
      CellSize = cellSize
      Width = width
      Height = height
      Cells = Array.create (width * height) ValueNone
    }

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

  let inline getWorldPos x y (grid: CellGrid2D<'T>) : Vector2 =
    Vector2(
      grid.Origin.X + float32 x * grid.CellSize.X,
      grid.Origin.Y + float32 y * grid.CellSize.Y
    )

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

  let inline iterVisible
    (left: int)
    (top: int)
    (right: int)
    (bottom: int)
    ([<InlineIfLambda>] action: int -> int -> 'T -> unit)
    (grid: CellGrid2D<'T>)
    : unit =
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
