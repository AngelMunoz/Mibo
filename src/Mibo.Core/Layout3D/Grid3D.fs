namespace Mibo.Layout3D

open System.Numerics

/// A simple axis-aligned bounding box struct for volume queries.
[<Struct>]
type BoundingBox = { Min: Vector3; Max: Vector3 }

[<Struct>]
type CellGrid3D<'T> = {
  Origin: Vector3
  CellSize: Vector3
  Width: int
  Height: int
  Depth: int
  Cells: 'T voption[]
}

module CellGrid3D =
  let inline private toIndex x y z width height =
    x + y * width + z * width * height

  let create
    width
    height
    depth
    (cellSize: Vector3)
    (origin: Vector3)
    : CellGrid3D<'T> =
    {
      Origin = origin
      CellSize = cellSize
      Width = width
      Height = height
      Depth = depth
      Cells = Array.create (width * height * depth) ValueNone
    }

  let inline set x y z (content: 'T) (grid: CellGrid3D<'T>) : unit =
    if
      x >= 0
      && x < grid.Width
      && y >= 0
      && y < grid.Height
      && z >= 0
      && z < grid.Depth
    then
      let idx = toIndex x y z grid.Width grid.Height
      grid.Cells.[idx] <- ValueSome content

  let inline get x y z (grid: CellGrid3D<'T>) : 'T voption =
    if
      x >= 0
      && x < grid.Width
      && y >= 0
      && y < grid.Height
      && z >= 0
      && z < grid.Depth
    then
      let idx = toIndex x y z grid.Width grid.Height
      grid.Cells.[idx]
    else
      ValueNone

  let inline clear x y z (grid: CellGrid3D<'T>) : unit =
    if
      x >= 0
      && x < grid.Width
      && y >= 0
      && y < grid.Height
      && z >= 0
      && z < grid.Depth
    then
      let idx = toIndex x y z grid.Width grid.Height
      grid.Cells.[idx] <- ValueNone

  let inline getWorldPos x y z (grid: CellGrid3D<'T>) : Vector3 =
    Vector3(
      grid.Origin.X + float32 x * grid.CellSize.X,
      grid.Origin.Y + float32 y * grid.CellSize.Y,
      grid.Origin.Z + float32 z * grid.CellSize.Z
    )

  let inline iter
    ([<InlineIfLambda>] action: int -> int -> int -> 'T -> unit)
    (grid: CellGrid3D<'T>)
    : unit =
    let w = grid.Width
    let wh = w * grid.Height

    for i in 0 .. grid.Cells.Length - 1 do
      match grid.Cells.[i] with
      | ValueSome content ->
        let x = i % w
        let y = (i / w) % grid.Height
        let z = i / wh
        action x y z content
      | ValueNone -> ()

  let inline iterVolume
    (bounds: BoundingBox)
    ([<InlineIfLambda>] action: int -> int -> int -> 'T -> unit)
    (grid: CellGrid3D<'T>)
    : unit =
    let startX = max 0 (int((bounds.Min.X - grid.Origin.X) / grid.CellSize.X))
    let startY = max 0 (int((bounds.Min.Y - grid.Origin.Y) / grid.CellSize.Y))
    let startZ = max 0 (int((bounds.Min.Z - grid.Origin.Z) / grid.CellSize.Z))

    let endX =
      min
        (grid.Width - 1)
        (int((bounds.Max.X - grid.Origin.X) / grid.CellSize.X))

    let endY =
      min
        (grid.Height - 1)
        (int((bounds.Max.Y - grid.Origin.Y) / grid.CellSize.Y))

    let endZ =
      min
        (grid.Depth - 1)
        (int((bounds.Max.Z - grid.Origin.Z) / grid.CellSize.Z))

    let w = grid.Width
    let wh = w * grid.Height

    for z in startZ..endZ do
      let zOffset = z * wh

      for y in startY..endY do
        let yzOffset = zOffset + y * w

        for x in startX..endX do
          let idx = yzOffset + x

          match grid.Cells.[idx] with
          | ValueSome content -> action x y z content
          | ValueNone -> ()
