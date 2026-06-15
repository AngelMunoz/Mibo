namespace Mibo.Layout

module TopDown =

  [<Struct>]
  type CorridorDirection =
    | Horizontal
    | Vertical
    | DiagonalDownRight
    | DiagonalDownLeft
    | DiagonalUpRight
    | DiagonalUpLeft

  let inline wallSegment
    length
    wall
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    section |> Layout.repeatX 0 0 length wall

  let inline doorway
    length
    wall
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let leftLength = length / 2
    let rightLength = length - leftLength - 1

    section
    |> Layout.repeatX 0 0 leftLength wall
    |> Layout.repeatX (leftLength + 1) 0 rightLength wall

  let inline corridor
    length
    width
    direction
    floor
    wall
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    match direction with
    | Horizontal ->
      let floorWidth = width - 2

      if floorWidth <= 0 then
        section
      else
        section
        |> Layout.fill 0 1 length floorWidth floor
        |> Layout.repeatX 0 0 length wall
        |> Layout.repeatX 0 (width - 1) length wall

    | Vertical ->
      let floorWidth = width - 2

      if floorWidth <= 0 then
        section
      else
        section
        |> Layout.fill 1 0 floorWidth length floor
        |> Layout.repeatY 0 0 length wall
        |> Layout.repeatY (width - 1) 0 length wall

    | DiagonalDownRight ->
      section
      |> Layout.line 0 0 (length - 1) (length - 1) floor
      |> Layout.line 1 0 length (length - 1) wall
      |> Layout.line 0 1 (length - 1) length wall

    | DiagonalDownLeft ->
      let startX = length - 1

      section
      |> Layout.line startX 0 1 (length - 1) floor
      |> Layout.line startX 1 (length - 1) length wall
      |> Layout.line (startX - 1) 0 0 (length - 1) wall

    | DiagonalUpRight ->
      section
      |> Layout.line 0 (length - 1) (length - 1) 0 floor
      |> Layout.line 1 (length - 1) length 0 wall
      |> Layout.line 0 (length - 2) (length - 1) (length - 1) wall

    | DiagonalUpLeft ->
      let startX = length - 1

      section
      |> Layout.line startX (length - 1) 1 0 floor
      |> Layout.line startX (length - 2) (length - 1) (length - 1) wall
      |> Layout.line (startX - 1) (length - 1) 0 0 wall

  let inline room
    width
    height
    floor
    wall
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    section |> Layout.rect 0 0 width height wall floor

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
