namespace Mibo.Layout3D

open System.Collections.Generic
open System.Numerics
open CellGrid3D

type LayeredGrid3D<'T> = {
  Width: int
  Height: int
  Depth: int
  CellSize: Vector3
  Origin: Vector3
  Layers: Dictionary<int, CellGrid3D<'T>>
}

module LayeredGrid3D =
  let create
    width
    height
    depth
    (cellSize: Vector3)
    (origin: Vector3)
    : LayeredGrid3D<'T> =
    {
      Width = width
      Height = height
      Depth = depth
      CellSize = cellSize
      Origin = origin
      Layers = Dictionary()
    }

  let getOrAddLayer
    index
    (grid: LayeredGrid3D<'T>)
    : CellGrid3D<'T> * LayeredGrid3D<'T> =
    match grid.Layers.TryGetValue index with
    | true, thing -> thing, grid
    | _ ->
      let newGrid =
        CellGrid3D.create
          grid.Width
          grid.Height
          grid.Depth
          grid.CellSize
          grid.Origin

      grid.Layers.Add(index, newGrid)
      newGrid, grid

module LayeredLayout3D =
  let inline layer
    index
    ([<InlineIfLambda>] f: GridSection3D<'T> -> GridSection3D<'T>)
    (grid: LayeredGrid3D<'T>)
    : LayeredGrid3D<'T> =
    let targetGrid, updatedContainer = LayeredGrid3D.getOrAddLayer index grid

    Layout3D.run f targetGrid |> ignore

    updatedContainer
