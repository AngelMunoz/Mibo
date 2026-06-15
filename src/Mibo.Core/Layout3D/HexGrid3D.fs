namespace Mibo.Layout3D

open System.Numerics
open Mibo.Layout

[<Struct>]
type HexGrid3D<'T> = {
  Origin: Vector3
  HexSize: float32
  LayerHeight: float32
  Orientation: HexOrientation
  Width: int
  Height: int
  Depth: int
  Cells: 'T voption[]
}

module HexGrid3D =
  let inline private toIndex col row layer width depth =
    col + row * width + layer * width * depth

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
    depth
    (hexSize: float32)
    (layerHeight: float32)
    (origin: Vector3)
    (orientation: HexOrientation)
    : HexGrid3D<'T> =
    {
      Origin = origin
      HexSize = hexSize
      LayerHeight = layerHeight
      Orientation = orientation
      Width = width
      Height = height
      Depth = depth
      Cells = Array.create (width * depth * height) ValueNone
    }

  let inline set col row layer (content: 'T) (grid: HexGrid3D<'T>) : unit =
    if
      col >= 0
      && col < grid.Width
      && row >= 0
      && row < grid.Depth
      && layer >= 0
      && layer < grid.Height
    then
      let idx = toIndex col row layer grid.Width grid.Depth
      grid.Cells.[idx] <- ValueSome content

  let inline get col row layer (grid: HexGrid3D<'T>) : 'T voption =
    if
      col >= 0
      && col < grid.Width
      && row >= 0
      && row < grid.Depth
      && layer >= 0
      && layer < grid.Height
    then
      let idx = toIndex col row layer grid.Width grid.Depth
      grid.Cells.[idx]
    else
      ValueNone

  let inline clear col row layer (grid: HexGrid3D<'T>) : unit =
    if
      col >= 0
      && col < grid.Width
      && row >= 0
      && row < grid.Depth
      && layer >= 0
      && layer < grid.Height
    then
      let idx = toIndex col row layer grid.Width grid.Depth
      grid.Cells.[idx] <- ValueNone

  let inline getWorldPos col row layer (grid: HexGrid3D<'T>) : Vector3 =
    let struct (hexW, hexH) = hexDimensions grid.HexSize grid.Orientation

    let x, z =
      match grid.Orientation with
      | PointyTop ->
        let x = float32 col * hexW + (if row % 2 = 1 then hexW / 2f else 0f)

        let z = float32 row * hexH * 0.75f
        x, z
      | FlatTop ->
        let x = float32 col * hexW * 0.75f

        let z = float32 row * hexH + (if col % 2 = 1 then hexH / 2f else 0f)

        x, z

    Vector3(
      grid.Origin.X + x + hexW / 2f,
      grid.Origin.Y + float32 layer * grid.LayerHeight,
      grid.Origin.Z + z + hexH / 2f
    )

  let inline iter
    ([<InlineIfLambda>] action: int -> int -> int -> 'T -> unit)
    (grid: HexGrid3D<'T>)
    : unit =
    let w = grid.Width
    let d = grid.Depth
    let wd = w * d

    for i in 0 .. grid.Cells.Length - 1 do
      match grid.Cells.[i] with
      | ValueSome content ->
        let col = i % w
        let row = (i / w) % d
        let layer = i / wd
        action col row layer content
      | ValueNone -> ()

  let inline iterVolume
    (bounds: BoundingBox)
    ([<InlineIfLambda>] action: int -> int -> int -> 'T -> unit)
    (grid: HexGrid3D<'T>)
    : unit =
    let struct (hexW, hexH) = hexDimensions grid.HexSize grid.Orientation

    let startCol, endCol, startRow, endRow =
      match grid.Orientation with
      | PointyTop ->
        let sc = max 0 (int((bounds.Min.X - grid.Origin.X) / hexW) - 1)

        let ec =
          min (grid.Width - 1) (int((bounds.Max.X - grid.Origin.X) / hexW) + 1)

        let sr =
          max 0 (int((bounds.Min.Z - grid.Origin.Z) / (hexH * 0.75f)) - 1)

        let er =
          min
            (grid.Depth - 1)
            (int((bounds.Max.Z - grid.Origin.Z) / (hexH * 0.75f)) + 1)

        sc, ec, sr, er
      | FlatTop ->
        let sc =
          max 0 (int((bounds.Min.X - grid.Origin.X) / (hexW * 0.75f)) - 1)

        let ec =
          min
            (grid.Width - 1)
            (int((bounds.Max.X - grid.Origin.X) / (hexW * 0.75f)) + 1)

        let sr = max 0 (int((bounds.Min.Z - grid.Origin.Z) / hexH) - 1)

        let er =
          min (grid.Depth - 1) (int((bounds.Max.Z - grid.Origin.Z) / hexH) + 1)

        sc, ec, sr, er

    let startLayer =
      max 0 (int((bounds.Min.Y - grid.Origin.Y) / grid.LayerHeight))

    let endLayer =
      min
        (grid.Height - 1)
        (int((bounds.Max.Y - grid.Origin.Y) / grid.LayerHeight))

    let w = grid.Width
    let d = grid.Depth

    for layer in startLayer..endLayer do
      let layerOffset = layer * w * d

      for row in startRow..endRow do
        let rowOffset = row * w

        for col in startCol..endCol do
          let idx = layerOffset + rowOffset + col

          match grid.Cells.[idx] with
          | ValueSome content -> action col row layer content
          | ValueNone -> ()
