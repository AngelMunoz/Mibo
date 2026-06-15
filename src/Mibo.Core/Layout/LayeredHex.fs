namespace Mibo.Layout

open System.Collections.Generic
open System.Numerics

type LayeredHexGrid<'T> = {
  Width: int
  Height: int
  Size: float32
  Origin: Vector2
  Orientation: HexOrientation
  Layers: Dictionary<int, HexGrid<'T>>
}

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
    : HexGrid<'T> * LayeredHexGrid<'T> =
    match grid.Layers.TryGetValue index with
    | true, thing -> thing, grid
    | _ ->
      let newGrid =
        HexGrid.create
          grid.Width
          grid.Height
          grid.Size
          grid.Origin
          grid.Orientation

      grid.Layers.Add(index, newGrid)
      newGrid, grid

module LayeredHexLayout =
  let inline layer
    index
    ([<InlineIfLambda>] f: HexGridSection<'T> -> HexGridSection<'T>)
    (grid: LayeredHexGrid<'T>)
    : LayeredHexGrid<'T> =
    let targetGrid, updatedContainer = LayeredHexGrid.getOrAddLayer index grid

    HexLayout.run f targetGrid |> ignore

    updatedContainer
