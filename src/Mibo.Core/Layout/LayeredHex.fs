#nowarn "44"
namespace Mibo.Layout

open System.Collections.Generic
open System.Numerics

[<System.Obsolete("A layered grid is a dictionary of grids; own the dictionary in game code")>]
type LayeredHexGrid<'T> = {
  Width: int
  Height: int
  Size: float32
  Origin: Vector2
  Orientation: HexOrientation
  Layers: Dictionary<int, HexGrid<'T>>
}

[<System.Obsolete("A layered grid is a dictionary of grids; own the dictionary in game code")>]
module LayeredHexGrid =
  let create
    width
    height
    (size: float32)
    (origin: Vector2)
    (orientation: HexOrientation)
    : LayeredHexGrid<'T> =
    {
      Width = width
      Height = height
      Size = size
      Origin = origin
      Orientation = orientation
      Layers = Dictionary()
    }

  let getOrAddLayer
    index
    (grid: LayeredHexGrid<'T>)
    : struct (HexGrid<'T> * LayeredHexGrid<'T>) =
    let mutable existing = Unchecked.defaultof<HexGrid<'T>>

    if grid.Layers.TryGetValue(index, &existing) then
      struct (existing, grid)
    else
      let newGrid =
        CellGrid2D.createHex {
          Orientation = grid.Orientation
          Width = grid.Width
          Height = grid.Height
          Radius = grid.Size
          Origin = grid.Origin
        }

      grid.Layers.Add(index, newGrid)
      struct (newGrid, grid)

[<System.Obsolete("A layered grid is a dictionary of grids; own the dictionary in game code")>]
module LayeredHexLayout =
  let inline layer
    index
    ([<InlineIfLambda>] f: HexGridSection<'T> -> HexGridSection<'T>)
    (grid: LayeredHexGrid<'T>)
    : LayeredHexGrid<'T> =
    let struct (targetGrid, updatedContainer) =
      LayeredHexGrid.getOrAddLayer index grid

    HexLayout.run f targetGrid |> ignore

    updatedContainer
