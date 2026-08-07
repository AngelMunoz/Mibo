namespace Mibo.Layout3D

open System.Collections.Generic
open System.Numerics
open Mibo.Layout

type LayeredHexGrid3D<'T> = {
  Width: int
  Height: int
  Depth: int
  HexSize: float32
  LayerHeight: float32
  Origin: Vector3
  Orientation: HexOrientation
  Layers: Dictionary<int, HexGrid3D<'T>>
}

module LayeredHexGrid3D =
  let create
    width
    height
    depth
    (hexSize: float32)
    (layerHeight: float32)
    (origin: Vector3)
    (orientation: HexOrientation)
    : LayeredHexGrid3D<'T> =
    {
      Width = width
      Height = height
      Depth = depth
      HexSize = hexSize
      LayerHeight = layerHeight
      Origin = origin
      Orientation = orientation
      Layers = Dictionary()
    }

  let getOrAddLayer
    index
    (grid: LayeredHexGrid3D<'T>)
    : struct (HexGrid3D<'T> * LayeredHexGrid3D<'T>) =
    let mutable existing = Unchecked.defaultof<HexGrid3D<'T>>

    if grid.Layers.TryGetValue(index, &existing) then
      struct (existing, grid)
    else
      let newGrid =
        HexGrid3D.create
          grid.Width
          grid.Height
          grid.Depth
          grid.HexSize
          grid.LayerHeight
          grid.Origin
          grid.Orientation

      grid.Layers.Add(index, newGrid)
      struct (newGrid, grid)

module LayeredHexLayout3D =
  let inline layer
    index
    ([<InlineIfLambda>] f: HexGrid3DSection<'T> -> HexGrid3DSection<'T>)
    (grid: LayeredHexGrid3D<'T>)
    : LayeredHexGrid3D<'T> =
    let struct (targetGrid, updatedContainer) =
      LayeredHexGrid3D.getOrAddLayer index grid

    HexLayout3D.run f targetGrid |> ignore

    updatedContainer
