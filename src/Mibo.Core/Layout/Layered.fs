namespace Mibo.Layout

open System.Collections.Generic
open System.Numerics

type LayeredGrid2D<'T> = {
  Width: int
  Height: int
  CellSize: Vector2
  Origin: Vector2
  Layers: Dictionary<int, CellGrid2D<'T>>
}

module LayeredGrid2D =
  let create width height cellSize origin : LayeredGrid2D<'T> = {
    Width = width
    Height = height
    CellSize = cellSize
    Origin = origin
    Layers = Dictionary()
  }

  let getOrAddLayer
    index
    (grid: LayeredGrid2D<'T>)
    : struct (CellGrid2D<'T> * LayeredGrid2D<'T>) =
    let mutable existing = Unchecked.defaultof<CellGrid2D<'T>>

    if grid.Layers.TryGetValue(index, &existing) then
      struct (existing, grid)
    else
      let newGrid =
        CellGrid2D.create grid.Width grid.Height grid.CellSize grid.Origin

      grid.Layers.Add(index, newGrid)
      struct (newGrid, grid)

module LayeredLayout =
  let inline layer
    index
    ([<InlineIfLambda>] f: GridSection2D<'T> -> GridSection2D<'T>)
    (grid: LayeredGrid2D<'T>)
    : LayeredGrid2D<'T> =
    let struct (targetGrid, updatedContainer) =
      LayeredGrid2D.getOrAddLayer index grid

    Layout.run f targetGrid |> ignore

    updatedContainer
