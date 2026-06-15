namespace Mibo.Layout3D

module Interior =

  [<Struct>]
  type DoorSide =
    | North
    | South
    | East
    | West

  let inline room
    width
    height
    depth
    floor
    wall
    ceiling
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.floorXZ 0 0 0 width depth floor |> ignore
    section |> Layout3D.floorXZ 0 (height - 1) 0 width depth ceiling |> ignore
    section |> Layout3D.wallXY 0 0 0 width height wall |> ignore
    section |> Layout3D.wallXY 0 0 (depth - 1) width height wall |> ignore
    section |> Layout3D.wallYZ 0 0 0 height depth wall |> ignore
    section |> Layout3D.wallYZ (width - 1) 0 0 height depth wall |> ignore
    section

  let inline openRoom
    width
    height
    depth
    floor
    wall
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.floorXZ 0 0 0 width depth floor |> ignore
    section |> Layout3D.wallXY 0 0 0 width height wall |> ignore
    section |> Layout3D.wallXY 0 0 (depth - 1) width height wall |> ignore
    section |> Layout3D.wallYZ 0 0 0 height depth wall |> ignore
    section |> Layout3D.wallYZ (width - 1) 0 0 height depth wall |> ignore
    section

  let inline corridorX
    length
    width
    height
    floor
    wall
    ceiling
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.floorXZ 0 0 0 length width floor |> ignore
    section |> Layout3D.floorXZ 0 (height - 1) 0 length width ceiling |> ignore
    section |> Layout3D.wallXY 0 0 0 length height wall |> ignore
    section |> Layout3D.wallXY 0 0 (width - 1) length height wall |> ignore
    section

  let inline corridorZ
    length
    width
    height
    floor
    wall
    ceiling
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.floorXZ 0 0 0 width length floor |> ignore
    section |> Layout3D.floorXZ 0 (height - 1) 0 width length ceiling |> ignore
    section |> Layout3D.wallYZ 0 0 0 height length wall |> ignore
    section |> Layout3D.wallYZ (width - 1) 0 0 height length wall |> ignore
    section

  let inline doorway
    side
    doorWidth
    doorHeight
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    let roomWidth = section.Width
    let roomDepth = section.Depth
    let startY = 1
    let endY = min (startY + doorHeight - 1) (section.Height - 2)

    match side with
    | North ->
      let startX = (roomWidth - doorWidth) / 2

      for dy in startY..endY do
        for dx in 0 .. doorWidth - 1 do
          clearLocal (startX + dx) dy (roomDepth - 1) section
    | South ->
      let startX = (roomWidth - doorWidth) / 2

      for dy in startY..endY do
        for dx in 0 .. doorWidth - 1 do
          clearLocal (startX + dx) dy 0 section
    | East ->
      let startZ = (roomDepth - doorWidth) / 2

      for dy in startY..endY do
        for dz in 0 .. doorWidth - 1 do
          clearLocal (roomWidth - 1) dy (startZ + dz) section
    | West ->
      let startZ = (roomDepth - doorWidth) / 2

      for dy in startY..endY do
        for dz in 0 .. doorWidth - 1 do
          clearLocal 0 dy (startZ + dz) section

    section

  let inline stairs
    width
    rise
    run
    step
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    if rise <= 0 || run <= 0 then
      section
    else
      let stepDepth = max 1 (run / rise)

      for i in 0 .. rise - 1 do
        let y = i
        let zStart = i * stepDepth

        for z in zStart .. zStart + stepDepth - 1 do
          for x in 0 .. width - 1 do
            setLocal x y z step section

      section

  let inline shaft
    width
    depth
    height
    wall
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.wallXY 0 0 0 width height wall |> ignore
    section |> Layout3D.wallXY 0 0 (depth - 1) width height wall |> ignore
    section |> Layout3D.wallYZ 0 0 0 height depth wall |> ignore
    section |> Layout3D.wallYZ (width - 1) 0 0 height depth wall |> ignore
    section

  let inline pillar
    height
    baseTile
    middleTile
    topTile
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    if height <= 0 then
      section
    elif height = 1 then
      section |> Layout3D.set 0 0 0 middleTile
    else
      section |> Layout3D.set 0 0 0 baseTile |> ignore

      for y in 1 .. height - 2 do
        setLocal 0 y 0 middleTile section

      section |> Layout3D.set 0 (height - 1) 0 topTile

  let inline window
    side
    windowWidth
    windowHeight
    sillHeight
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    let roomWidth = section.Width
    let roomDepth = section.Depth
    let startY = max 1 sillHeight
    let endY = min (startY + windowHeight - 1) (section.Height - 2)

    match side with
    | North ->
      let startX = (roomWidth - windowWidth) / 2

      for dy in startY..endY do
        for dx in 0 .. windowWidth - 1 do
          clearLocal (startX + dx) dy (roomDepth - 1) section
    | South ->
      let startX = (roomWidth - windowWidth) / 2

      for dy in startY..endY do
        for dx in 0 .. windowWidth - 1 do
          clearLocal (startX + dx) dy 0 section
    | East ->
      let startZ = (roomDepth - windowWidth) / 2

      for dy in startY..endY do
        for dz in 0 .. windowWidth - 1 do
          clearLocal (roomWidth - 1) dy (startZ + dz) section
    | West ->
      let startZ = (roomDepth - windowWidth) / 2

      for dy in startY..endY do
        for dz in 0 .. windowWidth - 1 do
          clearLocal 0 dy (startZ + dz) section

    section

  let inline scatterWall
    side
    count
    seed
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    match side with
    | North -> Layout3D.scatterXY (section.Depth - 1) count seed content section
    | South -> Layout3D.scatterXY 0 count seed content section
    | East -> Layout3D.scatterYZ (section.Width - 1) count seed content section
    | West -> Layout3D.scatterYZ 0 count seed content section

  let inline scatterEdges
    count
    seed
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    Layout3D.scatterEdges
      0
      0
      0
      section.Width
      section.Height
      section.Depth
      count
      seed
      content
      section

  let inline weather
    oldContent
    newContent
    probability
    seed
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    Layout3D.replaceScatter oldContent newContent probability seed section
