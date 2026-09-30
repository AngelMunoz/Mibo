namespace Mibo.Layout3D

open System.Collections.Generic
open System.Numerics
open CellGrid3D

[<System.Obsolete("A layered grid is a dictionary of grids; own the dictionary in game code")>]
type LayeredGrid3D<'T> = {
  Width: int
  Height: int
  Depth: int
  CellSize: Vector3
  Origin: Vector3
  Layers: Dictionary<int, CellGrid3D<'T>>
}

[<System.Obsolete("A layered grid is a dictionary of grids; own the dictionary in game code")>]
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
    : struct (CellGrid3D<'T> * LayeredGrid3D<'T>) =
    let mutable existing = Unchecked.defaultof<CellGrid3D<'T>>

    if grid.Layers.TryGetValue(index, &existing) then
      struct (existing, grid)
    else
      let newGrid =
        CellGrid3D.create
          grid.Width
          grid.Height
          grid.Depth
          grid.CellSize
          grid.Origin

      grid.Layers.Add(index, newGrid)
      struct (newGrid, grid)

[<System.Obsolete("A layered grid is a dictionary of grids; own the dictionary in game code")>]
module LayeredLayout3D =
  let inline layer
    index
    ([<InlineIfLambda>] f: GridSection3D<'T> -> GridSection3D<'T>)
    (grid: LayeredGrid3D<'T>)
    : LayeredGrid3D<'T> =
    let struct (targetGrid, updatedContainer) =
      LayeredGrid3D.getOrAddLayer index grid

    Layout3D.run f targetGrid |> ignore

    updatedContainer
