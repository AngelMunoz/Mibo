namespace Mibo.Layout

open HexGrid

[<Struct>]
type HexGridSection<'T> = {
  BackingGrid: HexGrid<'T>
  OffsetCol: int
  OffsetRow: int
  Width: int
  Height: int
}

[<AutoOpen>]
module HexLayoutHelpers =
  let createHexSection(grid: HexGrid<'T>) : HexGridSection<'T> = {
    BackingGrid = grid
    OffsetCol = 0
    OffsetRow = 0
    Width = grid.Width
    Height = grid.Height
  }

  let inline setHexLocal
    (lc: int)
    (lr: int)
    (content: 'T)
    (section: HexGridSection<'T>)
    : unit =
    let gc = section.OffsetCol + lc
    let gr = section.OffsetRow + lr

    if
      gc >= 0
      && gc < section.BackingGrid.Width
      && gr >= 0
      && gr < section.BackingGrid.Height
    then
      set gc gr content section.BackingGrid

module HexLayout =
  let inline run
    ([<InlineIfLambda>] f: HexGridSection<'T> -> HexGridSection<'T>)
    (grid: HexGrid<'T>)
    : HexGrid<'T> =
    let section = createHexSection grid
    let result = f section
    result.BackingGrid

  let inline section
    col
    row
    ([<InlineIfLambda>] f: HexGridSection<'T> -> HexGridSection<'T>)
    (parent: HexGridSection<'T>)
    : HexGridSection<'T> =
    let col = max 0 (min parent.Width col)
    let row = max 0 (min parent.Height row)

    let childSection = {
      BackingGrid = parent.BackingGrid
      OffsetCol = parent.OffsetCol + col
      OffsetRow = parent.OffsetRow + row
      Width = max 0 (parent.Width - col)
      Height = max 0 (parent.Height - row)
    }

    f childSection |> ignore
    parent

  let inline padding
    n
    ([<InlineIfLambda>] f: HexGridSection<'T> -> HexGridSection<'T>)
    (parent: HexGridSection<'T>)
    : HexGridSection<'T> =
    let n = max 0 n

    if n = 0 then
      f parent |> ignore
      parent
    else
      let childSection = {
        BackingGrid = parent.BackingGrid
        OffsetCol = parent.OffsetCol + n
        OffsetRow = parent.OffsetRow + n
        Width = max 0 (parent.Width - 2 * n)
        Height = max 0 (parent.Height - 2 * n)
      }

      f childSection |> ignore
      parent

  let inline paddingEx
    left
    top
    right
    bottom
    ([<InlineIfLambda>] f: HexGridSection<'T> -> HexGridSection<'T>)
    (parent: HexGridSection<'T>)
    : HexGridSection<'T> =
    let left = max 0 left
    let top = max 0 top
    let right = max 0 right
    let bottom = max 0 bottom

    let childSection = {
      BackingGrid = parent.BackingGrid
      OffsetCol = parent.OffsetCol + left
      OffsetRow = parent.OffsetRow + top
      Width = max 0 (parent.Width - left - right)
      Height = max 0 (parent.Height - top - bottom)
    }

    f childSection |> ignore
    parent

  let inline center
    w
    h
    ([<InlineIfLambda>] f: HexGridSection<'T> -> HexGridSection<'T>)
    (parent: HexGridSection<'T>)
    : HexGridSection<'T> =
    let w = max 0 (min parent.Width w)
    let h = max 0 (min parent.Height h)
    let col = (parent.Width - w) / 2
    let row = (parent.Height - h) / 2

    let childSection = {
      BackingGrid = parent.BackingGrid
      OffsetCol = parent.OffsetCol + col
      OffsetRow = parent.OffsetRow + row
      Width = w
      Height = h
    }

    f childSection |> ignore
    parent

  let inline flowX
    step
    (stamps: (HexGridSection<'T> -> HexGridSection<'T>) seq)
    (parent: HexGridSection<'T>)
    : HexGridSection<'T> =
    let mutable i = 0

    for stamp in stamps do
      section (i * step) 0 stamp parent |> ignore
      i <- i + 1

    parent

  let inline flowY
    step
    (stamps: (HexGridSection<'T> -> HexGridSection<'T>) seq)
    (parent: HexGridSection<'T>)
    : HexGridSection<'T> =
    let mutable i = 0

    for stamp in stamps do
      section 0 (i * step) stamp parent |> ignore
      i <- i + 1

    parent

  let set col row content (section: HexGridSection<'T>) : HexGridSection<'T> =
    setHexLocal col row content section
    section

  let repeatX
    col
    row
    count
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    if row >= 0 && row < section.Height then
      let c1 = max 0 col
      let c2 = min section.Width (col + count)

      if c2 > c1 then
        let grid = section.BackingGrid
        let gw = grid.Width
        let startCol = section.OffsetCol + c1
        let gr = section.OffsetRow + row
        let idxBase = gr * gw + startCol

        for i in 0 .. c2 - c1 - 1 do
          grid.Cells.[idxBase + i] <- ValueSome content

    section

  let repeatY
    col
    row
    count
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    if col >= 0 && col < section.Width then
      let r1 = max 0 row
      let r2 = min section.Height (row + count)

      if r2 > r1 then
        let grid = section.BackingGrid
        let gw = grid.Width
        let gc = section.OffsetCol + col
        let startRow = section.OffsetRow + r1
        let idxBase = startRow * gw + gc

        for i in 0 .. r2 - r1 - 1 do
          grid.Cells.[idxBase + i * gw] <- ValueSome content

    section

  let fill
    col
    row
    width
    height
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let c1 = max 0 col
    let r1 = max 0 row
    let c2 = min section.Width (col + width)
    let r2 = min section.Height (row + height)

    if c2 > c1 && r2 > r1 then
      let grid = section.BackingGrid
      let gw = grid.Width
      let startCol = section.OffsetCol + c1
      let startRow = section.OffsetRow + r1
      let fillW = c2 - c1
      let fillH = r2 - r1

      for fr in 0 .. fillH - 1 do
        let rowStart = startCol + (startRow + fr) * gw

        for fc in 0 .. fillW - 1 do
          grid.Cells.[rowStart + fc] <- ValueSome content

    section

  let border
    col
    row
    width
    height
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    if width > 0 && height > 0 then
      section
      |> repeatX col row width content
      |> repeatX col (row + height - 1) width content
      |> repeatY col (row + 1) (height - 2) content
      |> repeatY (col + width - 1) (row + 1) (height - 2) content
    else
      section

  let rect
    col
    row
    width
    height
    borderContent
    fillContent
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    section
    |> fill col row width height fillContent
    |> border col row width height borderContent

  let corners
    col
    row
    width
    height
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    section
    |> set col row content
    |> set (col + width - 1) row content
    |> set col (row + height - 1) content
    |> set (col + width - 1) (row + height - 1) content

  let scatterBorder
    col
    row
    width
    height
    count
    seed
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let rng = System.Random(seed)

    for _ in 1..count do
      let side = rng.Next(0, 4)

      match side with
      | 0 -> setHexLocal (col + rng.Next(0, width)) row content section
      | 1 ->
        setHexLocal
          (col + rng.Next(0, width))
          (row + height - 1)
          content
          section
      | 2 -> setHexLocal col (row + rng.Next(0, height)) content section
      | 3 ->
        setHexLocal
          (col + width - 1)
          (row + rng.Next(0, height))
          content
          section
      | _ -> ()

    section

  let scatterLine
    c1
    r1
    c2
    r2
    count
    seed
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let dc = abs(c2 - c1)
    let dr = abs(r2 - r1)
    let dm = max dc dr

    if dm > 0 then
      let rng = System.Random(seed)

      for _ in 1..count do
        let t = rng.NextDouble()
        let lc = c1 + int(float(c2 - c1) * t)
        let lr = r1 + int(float(r2 - r1) * t)
        setHexLocal lc lr content section

    section

  let checkerBorder
    col
    row
    width
    height
    odd
    even
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    for bc in 0 .. width - 1 do
      let top = if bc % 2 = 0 then odd else even
      let bottom = if (bc + height - 1) % 2 = 0 then odd else even
      setHexLocal (col + bc) row top section |> ignore
      setHexLocal (col + bc) (row + height - 1) bottom section |> ignore

    for br in 1 .. height - 2 do
      let left = if br % 2 = 0 then odd else even
      let right = if (br + width - 1) % 2 = 0 then odd else even
      setHexLocal col (row + br) left section |> ignore
      setHexLocal (col + width - 1) (row + br) right section |> ignore

    section

  let inline generate
    col
    row
    width
    height
    ([<InlineIfLambda>] generator: int -> int -> 'T)
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let c1 = max 0 col
    let r1 = max 0 row
    let c2 = min section.Width (col + width)
    let r2 = min section.Height (row + height)

    if c2 > c1 && r2 > r1 then
      let grid = section.BackingGrid
      let gw = grid.Width
      let startCol = section.OffsetCol + c1
      let startRow = section.OffsetRow + r1
      let fillW = c2 - c1
      let fillH = r2 - r1

      for fr in 0 .. fillH - 1 do
        let lr = r1 + fr
        let rowStart = startCol + (startRow + fr) * gw

        for fc in 0 .. fillW - 1 do
          let lc = c1 + fc
          grid.Cells.[rowStart + fc] <- ValueSome(generator lc lr)

    section

  let inline iter
    col
    row
    width
    height
    ([<InlineIfLambda>] action: int -> int -> 'T voption -> unit)
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let w = section.BackingGrid.Width

    for fc in col .. col + width - 1 do
      for fr in row .. row + height - 1 do
        let gc = section.OffsetCol + fc
        let gr = section.OffsetRow + fr

        if
          gc >= 0
          && gc < section.BackingGrid.Width
          && gr >= 0
          && gr < section.BackingGrid.Height
        then
          let idx = gc + gr * w
          let cell = section.BackingGrid.Cells.[idx]
          action fc fr cell

    section

  let inline map
    col
    row
    width
    height
    ([<InlineIfLambda>] mapping: 'T -> 'T)
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let w = section.BackingGrid.Width

    for fc in col .. col + width - 1 do
      for fr in row .. row + height - 1 do
        let gc = section.OffsetCol + fc
        let gr = section.OffsetRow + fr

        if
          gc >= 0
          && gc < section.BackingGrid.Width
          && gr >= 0
          && gr < section.BackingGrid.Height
        then
          let idx = gc + gr * w

          match section.BackingGrid.Cells.[idx] with
          | ValueSome content ->
            section.BackingGrid.Cells.[idx] <- ValueSome(mapping content)
          | ValueNone -> ()

    section

  let line
    c1
    r1
    c2
    r2
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let dc = abs(c2 - c1)
    let dr = -abs(r2 - r1)
    let sc = if c1 < c2 then 1 else -1
    let sr = if r1 < r2 then 1 else -1
    let mutable err = dc + dr
    let mutable cc = c1
    let mutable cr = r1

    while not(cc = c2 && cr = r2) do
      setHexLocal cc cr content section
      let e2 = 2 * err

      if e2 >= dr then
        err <- err + dr
        cc <- cc + sc

      if e2 <= dc then
        err <- err + dc
        cr <- cr + sr

    setHexLocal cc cr content section
    section

  let circle
    cc
    cr
    radius
    filled
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let mutable x = radius
    let mutable y = 0
    let mutable err = 1 - x

    let plot cx cy x y =
      if filled then
        let drawLine x1 x2 y =
          let startX = min x1 x2
          let endX = max x1 x2

          for i in startX..endX do
            setHexLocal (cx + i) (cy + y) content section

        drawLine (-x) x y
        drawLine (-x) x (-y)
        drawLine (-y) y x
        drawLine (-y) y (-x)
      else
        setHexLocal (cx + x) (cy + y) content section
        setHexLocal (cx - x) (cy + y) content section
        setHexLocal (cx + x) (cy - y) content section
        setHexLocal (cx - x) (cy - y) content section
        setHexLocal (cx + y) (cy + x) content section
        setHexLocal (cx - y) (cy + x) content section
        setHexLocal (cx + y) (cy - x) content section
        setHexLocal (cx - y) (cy - x) content section

    while x >= y do
      plot cc cr x y
      y <- y + 1

      if err < 0 then
        err <- err + 2 * y + 1
      else
        x <- x - 1
        err <- err + 2 * (y - x) + 1

    section

  let polygon
    (points: struct (int * int)[])
    filled
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    if points.Length = 0 then
      section
    else
      if filled then
        let mutable minY = System.Int32.MaxValue
        let mutable maxY = System.Int32.MinValue

        for i in 0 .. points.Length - 1 do
          let struct (_, y) = points.[i]

          if y < minY then
            minY <- y

          if y > maxY then
            maxY <- y

        for y in max 0 minY .. min (section.Height - 1) maxY do
          let nodes = System.Collections.Generic.List<int>()
          let mutable j = points.Length - 1

          for i in 0 .. points.Length - 1 do
            let struct (xi, yi) = points.[i]
            let struct (xj, yj) = points.[j]

            if (yi <= y && yj > y) || (yj <= y && yi > y) then
              let x = float xi + float(y - yi) / float(yj - yi) * float(xj - xi)
              nodes.Add(int x)

            j <- i

          nodes.Sort()
          let count = nodes.Count
          let mutable i = 0

          while i < count - 1 do
            for x in nodes.[i] .. nodes.[i + 1] do
              if x >= 0 && x < section.Width then
                setHexLocal x y content section

            i <- i + 2
      else
        let drawLineSegment (x1, y1) (x2, y2) =
          let dx = abs(x2 - x1)
          let dy = -abs(y2 - y1)
          let sx = if x1 < x2 then 1 else -1
          let sy = if y1 < y2 then 1 else -1
          let mutable err = dx + dy
          let mutable cx = x1
          let mutable cy = y1

          while not(cx = x2 && cy = y2) do
            setHexLocal cx cy content section
            let e2 = 2 * err

            if e2 >= dy then
              err <- err + dy
              cx <- cx + sx

            if e2 <= dx then
              err <- err + dx
              cy <- cy + sy

          setHexLocal cx cy content section

        if points.Length > 1 then
          for i in 0 .. points.Length - 2 do
            let struct (x1, y1) = points.[i]
            let struct (x2, y2) = points.[i + 1]
            drawLineSegment (x1, y1) (x2, y2)

          let struct (lastX, lastY) = points.[points.Length - 1]
          let struct (firstX, firstY) = points.[0]
          drawLineSegment (lastX, lastY) (firstX, firstY)

      section

  let checker odd even (section: HexGridSection<'T>) : HexGridSection<'T> =
    let grid = section.BackingGrid
    let gw = grid.Width
    let startCol = section.OffsetCol
    let startRow = section.OffsetRow

    for fr in 0 .. section.Height - 1 do
      let rowStart = startCol + (startRow + fr) * gw

      for fc in 0 .. section.Width - 1 do
        let content = if (fc + fr) % 2 = 0 then odd else even
        grid.Cells.[rowStart + fc] <- ValueSome content

    section

  let scatter
    count
    seed
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let rng = System.Random(seed)

    for _ in 1..count do
      let c = rng.Next(0, section.Width)
      let r = rng.Next(0, section.Height)
      setHexLocal c r content section

    section

  let clear
    col
    row
    width
    height
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let c1 = max 0 col
    let r1 = max 0 row
    let c2 = min section.Width (col + width)
    let r2 = min section.Height (row + height)

    if c2 > c1 && r2 > r1 then
      let grid = section.BackingGrid
      let gw = grid.Width
      let startCol = section.OffsetCol + c1
      let startRow = section.OffsetRow + r1
      let fillW = c2 - c1
      let fillH = r2 - r1

      for fr in 0 .. fillH - 1 do
        let rowStart = startCol + (startRow + fr) * gw

        for fc in 0 .. fillW - 1 do
          grid.Cells.[rowStart + fc] <- ValueNone

    section

  let replace
    oldContent
    newContent
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let w = section.BackingGrid.Width

    for c in 0 .. section.Width - 1 do
      for r in 0 .. section.Height - 1 do
        let gc = section.OffsetCol + c
        let gr = section.OffsetRow + r

        if
          gc >= 0
          && gc < section.BackingGrid.Width
          && gr >= 0
          && gr < section.BackingGrid.Height
        then
          let idx = gc + gr * w

          match section.BackingGrid.Cells.[idx] with
          | ValueSome cc when cc = oldContent ->
            section.BackingGrid.Cells.[idx] <- ValueSome newContent
          | _ -> ()

    section

  let replaceScatter
    oldContent
    newContent
    (probability: float32)
    seed
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let rng = System.Random(seed)

    for c in 0 .. section.Width - 1 do
      for r in 0 .. section.Height - 1 do
        let gc = section.OffsetCol + c
        let gr = section.OffsetRow + r

        if
          gc >= 0
          && gc < section.BackingGrid.Width
          && gr >= 0
          && gr < section.BackingGrid.Height
        then
          let idx = gc + gr * section.BackingGrid.Width

          match section.BackingGrid.Cells.[idx] with
          | ValueSome cc when cc = oldContent ->
            if float32(rng.NextDouble()) < probability then
              section.BackingGrid.Cells.[idx] <- ValueSome newContent
          | _ -> ()

    section

  let inline scatterStamp
    count
    seed
    ([<InlineIfLambda>] stamp: HexGridSection<'T> -> HexGridSection<'T>)
    (section': HexGridSection<'T>)
    : HexGridSection<'T> =
    let rng = System.Random(seed)

    for _ in 1..count do
      let c = rng.Next(0, section'.Width)
      let r = rng.Next(0, section'.Height)
      section' |> section c r stamp |> ignore

    section'

  let setIfEmpty
    col
    row
    content
    (section: HexGridSection<'T>)
    : HexGridSection<'T> =
    let gc = section.OffsetCol + col
    let gr = section.OffsetRow + row

    if
      gc >= 0
      && gc < section.BackingGrid.Width
      && gr >= 0
      && gr < section.BackingGrid.Height
    then
      let w = section.BackingGrid.Width
      let idx = gc + gr * w
      let cell = &section.BackingGrid.Cells.[idx]

      if cell.IsNone then
        cell <- ValueSome content

    section
