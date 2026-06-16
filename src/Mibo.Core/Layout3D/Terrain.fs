namespace Mibo.Layout3D

module Terrain =

  let inline ground
    width
    depth
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.floorXZ 0 0 0 width depth content

  let inline plateau
    width
    depth
    height
    top
    side
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.fill 0 0 0 width height depth side |> ignore
    section |> Layout3D.floorXZ 0 (height - 1) 0 width depth top

  let inline pit
    width
    depth
    dropHeight
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    section |> Layout3D.clear 0 0 0 width dropHeight depth

  let inline rampX
    width
    depth
    rise
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    if depth <= 0 || rise <= 0 then
      section
    else
      for x in 0 .. depth - 1 do
        let y = (x * rise) / depth

        for z in 0 .. width - 1 do
          for fy in 0..y do
            setLocal x fy z content section

      section

  let inline rampZ
    width
    depth
    rise
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    if depth <= 0 || rise <= 0 then
      section
    else
      for z in 0 .. depth - 1 do
        let y = (z * rise) / depth

        for x in 0 .. width - 1 do
          for fy in 0..y do
            setLocal x fy z content section

      section

  let inline path
    (points: (int * int * int) list)
    width
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    match points with
    | []
    | [ _ ] -> section
    | _ ->
      let rec drawSegments pts =
        match pts with
        | (x1, y1, z1) :: (x2, y2, z2) :: rest ->
          if width <= 1 then
            section |> Layout3D.line x1 y1 z1 x2 y2 z2 content |> ignore
          else
            let hw = width / 2

            for offset in -hw .. hw do
              section
              |> Layout3D.line (x1 + offset) y1 z1 (x2 + offset) y2 z2 content
              |> ignore

              section
              |> Layout3D.line x1 y1 (z1 + offset) x2 y2 (z2 + offset) content
              |> ignore

          drawSegments((x2, y2, z2) :: rest)
        | _ -> ()

      drawSegments points
      section

  let inline scatterAt
    y
    count
    seed
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    Layout3D.scatterXZ y count seed content section

  let inline scatter
    count
    seed
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    scatterAt 0 count seed content section

  let inline scatterSurface
    ([<InlineIfLambda>] heightFn: int -> int -> int)
    count
    seed
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    let rng = System.Random(seed)

    for _ in 1..count do
      let x = rng.Next(0, section.Width)
      let z = rng.Next(0, section.Depth)
      let y = heightFn x z
      setLocal x y z content section

    section

  let inline checkerSurface
    ([<InlineIfLambda>] heightFn: int -> int -> int)
    odd
    even
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    for fz in 0 .. section.Depth - 1 do
      for fx in 0 .. section.Width - 1 do
        let y = heightFn fx fz
        let content = if (fx + fz) % 2 = 0 then odd else even
        setLocal fx y fz content section

    section

  let inline generateSurface
    ([<InlineIfLambda>] heightFn: int -> int -> int)
    ([<InlineIfLambda>] generator: int -> int -> int -> 'T)
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    for fz in 0 .. section.Depth - 1 do
      for fx in 0 .. section.Width - 1 do
        let y = heightFn fx fz
        let content = generator fx y fz
        setLocal fx y fz content section

    section

  let inline scatterPath
    (points: (int * int * int) list)
    count
    seed
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    match points with
    | []
    | [ _ ] -> section
    | _ ->
      let rng = System.Random(seed)

      for i in 0 .. points.Length - 2 do
        let (x1, y1, z1) = points.[i]
        let (x2, y2, z2) = points.[i + 1]
        let segCount = max 1 (count / (points.Length - 1))

        section
        |> Layout3D.scatterLine x1 y1 z1 x2 y2 z2 segCount (rng.Next()) content
        |> ignore

      section

  let inline scatterStampAt
    y
    count
    seed
    ([<InlineIfLambda>] stamp: GridSection3D<'T> -> GridSection3D<'T>)
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    let rng = System.Random(seed)

    for _ in 1..count do
      let x = rng.Next(0, section.Width)
      let z = rng.Next(0, section.Depth)
      section |> Layout3D.section x y z stamp |> ignore

    section

  let inline heightmap
    ([<InlineIfLambda>] heightFn: int -> int -> int)
    content
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    for z in 0 .. section.Depth - 1 do
      for x in 0 .. section.Width - 1 do
        let h = heightFn x z

        for y in 0 .. min h (section.Height - 1) do
          setLocal x y z content section

    section

  let inline layeredHeightmap
    ([<InlineIfLambda>] heightFn: int -> int -> int)
    topLayer
    midLayer
    midDepth
    bottomLayer
    (section: GridSection3D<'T>)
    : GridSection3D<'T> =
    for z in 0 .. section.Depth - 1 do
      for x in 0 .. section.Width - 1 do
        let h = min (heightFn x z) (section.Height - 1)

        for y in 0..h do
          let content =
            if y = h then topLayer
            elif y > h - midDepth then midLayer
            else bottomLayer

          setLocal x y z content section

    section
