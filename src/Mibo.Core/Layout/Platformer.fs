namespace Mibo.Layout

module Platformer =

  [<Struct>]
  type Anchor =
    | Left
    | Right

  [<Struct>]
  type StairDirection =
    | UpRight
    | UpLeft
    | DownRight
    | DownLeft

  let inline box
    width
    height
    border
    fill
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    section |> Layout.rect 0 0 width height border fill

  let inline platform
    width
    tile
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    section |> Layout.repeatX 0 0 width tile

  let inline ledge
    width
    anchor
    tile
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    match anchor with
    | Left -> section |> Layout.repeatX 0 0 width tile
    | Right -> section |> Layout.repeatX (section.Width - width) 0 width tile

  let inline wall height tile (section: GridSection2D<'T>) : GridSection2D<'T> =
    section |> Layout.repeatY 0 0 height tile

  let inline pillar
    height
    baseTile
    middleTile
    topTile
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    if height <= 0 then
      section
    elif height = 1 then
      section |> Layout.set 0 0 middleTile
    else
      section
      |> Layout.set 0 (height - 1) baseTile
      |> Layout.repeatY 0 1 (height - 2) middleTile
      |> Layout.set 0 0 topTile

  let inline stairs
    width
    tile
    direction
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let height = width

    match direction with
    | UpRight ->
      for i in 0 .. width - 1 do
        Layout.set i (height - 1 - i) tile section |> ignore
    | DownRight ->
      for i in 0 .. width - 1 do
        Layout.set i i tile section |> ignore
    | UpLeft ->
      for i in 0 .. width - 1 do
        Layout.set (width - 1 - i) (height - 1 - i) tile section |> ignore
    | DownLeft ->
      for i in 0 .. width - 1 do
        Layout.set (width - 1 - i) i tile section |> ignore

    section

  let inline slope
    width
    height
    tile
    direction
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    match direction with
    | UpRight -> Layout.line 0 (height - 1) (width - 1) 0 tile section
    | DownRight -> Layout.line 0 0 (width - 1) (height - 1) tile section
    | UpLeft -> Layout.line (width - 1) (height - 1) 0 0 tile section
    | DownLeft -> Layout.line (width - 1) 0 0 (height - 1) tile section

  let inline pit width depth (section: GridSection2D<'T>) : GridSection2D<'T> =
    Layout.clear 0 0 width depth section

  let inline gap width height (section: GridSection2D<'T>) : GridSection2D<'T> =
    Layout.clear 0 0 width height section

  let inline scatterEdges
    count
    seed
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    Layout.scatterBorder
      0
      0
      section.Width
      section.Height
      count
      seed
      content
      section

  let inline weather
    oldContent
    newContent
    probability
    seed
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    Layout.replaceScatter oldContent newContent probability seed section
