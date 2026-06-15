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
    : CellGrid2D<'T> * LayeredGrid2D<'T> =
    match grid.Layers.TryGetValue index with
    | true, thing -> thing, grid
    | _ ->
      let newGrid =
        CellGrid2D.create grid.Width grid.Height grid.CellSize grid.Origin

      grid.Layers.Add(index, newGrid)
      newGrid, grid

module LayeredLayout =
  let inline layer
    index
    ([<InlineIfLambda>] f: GridSection2D<'T> -> GridSection2D<'T>)
    (grid: LayeredGrid2D<'T>)
    : LayeredGrid2D<'T> =
    let targetGrid, updatedContainer = LayeredGrid2D.getOrAddLayer index grid

    Layout.run f targetGrid |> ignore

    updatedContainer
