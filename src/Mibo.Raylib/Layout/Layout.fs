namespace Mibo.Layout

open CellGrid2D

[<Struct>]
type GridSection2D<'T> = {
  BackingGrid: CellGrid2D<'T>
  OffsetX: int
  OffsetY: int
  Width: int
  Height: int
}

[<AutoOpen>]
module LayoutHelpers =
  let createSection(grid: CellGrid2D<'T>) : GridSection2D<'T> = {
    BackingGrid = grid
    OffsetX = 0
    OffsetY = 0
    Width = grid.Width
    Height = grid.Height
  }

  let inline setLocal
    (lx: int)
    (ly: int)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    let gx = section.OffsetX + lx
    let gy = section.OffsetY + ly

    if
      gx >= 0
      && gx < section.BackingGrid.Width
      && gy >= 0
      && gy < section.BackingGrid.Height
    then
      set gx gy content section.BackingGrid

module Layout =
  let inline run
    ([<InlineIfLambda>] f: GridSection2D<'T> -> GridSection2D<'T>)
    (grid: CellGrid2D<'T>)
    : CellGrid2D<'T> =
    let section = createSection grid
    let result = f section
    result.BackingGrid

  let inline section
    x
    y
    ([<InlineIfLambda>] f: GridSection2D<'T> -> GridSection2D<'T>)
    (parent: GridSection2D<'T>)
    : GridSection2D<'T> =
    let x = max 0 (min parent.Width x)
    let y = max 0 (min parent.Height y)

    let childSection = {
      BackingGrid = parent.BackingGrid
      OffsetX = parent.OffsetX + x
      OffsetY = parent.OffsetY + y
      Width = max 0 (parent.Width - x)
      Height = max 0 (parent.Height - y)
    }

    f childSection |> ignore
    parent

  let inline padding
    n
    ([<InlineIfLambda>] f: GridSection2D<'T> -> GridSection2D<'T>)
    (parent: GridSection2D<'T>)
    : GridSection2D<'T> =
    let n = max 0 n

    if n = 0 then
      f parent |> ignore
      parent
    else
      let childSection = {
        BackingGrid = parent.BackingGrid
        OffsetX = parent.OffsetX + n
        OffsetY = parent.OffsetY + n
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
    ([<InlineIfLambda>] f: GridSection2D<'T> -> GridSection2D<'T>)
    (parent: GridSection2D<'T>)
    : GridSection2D<'T> =
    let left = max 0 left
    let top = max 0 top
    let right = max 0 right
    let bottom = max 0 bottom

    let childSection = {
      BackingGrid = parent.BackingGrid
      OffsetX = parent.OffsetX + left
      OffsetY = parent.OffsetY + top
      Width = max 0 (parent.Width - left - right)
      Height = max 0 (parent.Height - top - bottom)
    }

    f childSection |> ignore
    parent

  let inline center
    w
    h
    ([<InlineIfLambda>] f: GridSection2D<'T> -> GridSection2D<'T>)
    (parent: GridSection2D<'T>)
    : GridSection2D<'T> =
    let w = max 0 (min parent.Width w)
    let h = max 0 (min parent.Height h)
    let x = (parent.Width - w) / 2
    let y = (parent.Height - h) / 2

    let childSection = {
      BackingGrid = parent.BackingGrid
      OffsetX = parent.OffsetX + x
      OffsetY = parent.OffsetY + y
      Width = w
      Height = h
    }

    f childSection |> ignore
    parent

  let inline flowX
    step
    (stamps: (GridSection2D<'T> -> GridSection2D<'T>) seq)
    (parent: GridSection2D<'T>)
    : GridSection2D<'T> =
    let mutable i = 0

    for stamp in stamps do
      section (i * step) 0 stamp parent |> ignore
      i <- i + 1

    parent

  let inline flowY
    step
    (stamps: (GridSection2D<'T> -> GridSection2D<'T>) seq)
    (parent: GridSection2D<'T>)
    : GridSection2D<'T> =
    let mutable i = 0

    for stamp in stamps do
      section 0 (i * step) stamp parent |> ignore
      i <- i + 1

    parent

  let set x y content (section: GridSection2D<'T>) : GridSection2D<'T> =
    setLocal x y content section
    section

  let repeatX
    x
    y
    count
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    if y >= 0 && y < section.Height then
      let x1 = max 0 x
      let x2 = min section.Width (x + count)

      if x2 > x1 then
        let grid = section.BackingGrid
        let gw = grid.Width
        let startX = section.OffsetX + x1
        let gy = section.OffsetY + y
        let idxBase = gy * gw + startX

        for i in 0 .. x2 - x1 - 1 do
          grid.Cells.[idxBase + i] <- ValueSome content

    section

  let repeatY
    x
    y
    count
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    if x >= 0 && x < section.Width then
      let y1 = max 0 y
      let y2 = min section.Height (y + count)

      if y2 > y1 then
        let grid = section.BackingGrid
        let gw = grid.Width
        let gx = section.OffsetX + x
        let startY = section.OffsetY + y1
        let idxBase = startY * gw + gx

        for i in 0 .. y2 - y1 - 1 do
          grid.Cells.[idxBase + i * gw] <- ValueSome content

    section

  let fill
    x
    y
    width
    height
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let x1 = max 0 x
    let y1 = max 0 y
    let x2 = min section.Width (x + width)
    let y2 = min section.Height (y + height)

    if x2 > x1 && y2 > y1 then
      let grid = section.BackingGrid
      let gw = grid.Width
      let startX = section.OffsetX + x1
      let startY = section.OffsetY + y1
      let fillW = x2 - x1
      let fillH = y2 - y1

      for fy in 0 .. fillH - 1 do
        let rowStart = startX + (startY + fy) * gw

        for fx in 0 .. fillW - 1 do
          grid.Cells.[rowStart + fx] <- ValueSome content

    section

  let border
    x
    y
    width
    height
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    if width > 0 && height > 0 then
      section
      |> repeatX x y width content
      |> repeatX x (y + height - 1) width content
      |> repeatY x (y + 1) (height - 2) content
      |> repeatY (x + width - 1) (y + 1) (height - 2) content
    else
      section

  let rect
    x
    y
    width
    height
    borderContent
    fillContent
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    section
    |> fill x y width height fillContent
    |> border x y width height borderContent

  let corners
    x
    y
    width
    height
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    section
    |> set x y content
    |> set (x + width - 1) y content
    |> set x (y + height - 1) content
    |> set (x + width - 1) (y + height - 1) content

  let scatterBorder
    x
    y
    width
    height
    count
    seed
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    if width > 0 && height > 0 then
      let rng = System.Random(seed)

      for _ in 1..count do
        let side = rng.Next(0, 4)

        match side with
        | 0 -> setLocal (x + rng.Next(0, width)) y content section
        | 1 ->
          setLocal (x + rng.Next(0, width)) (y + height - 1) content section
        | 2 -> setLocal x (y + rng.Next(0, height)) content section
        | 3 ->
          setLocal (x + width - 1) (y + rng.Next(0, height)) content section
        | _ -> ()

    section

  let scatterLine
    x1
    y1
    x2
    y2
    count
    seed
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let dx = abs(x2 - x1)
    let dy = abs(y2 - y1)
    let dm = max dx dy

    if dm > 0 then
      let rng = System.Random(seed)

      for _ in 1..count do
        let t = rng.NextDouble()
        let lx = x1 + int(float(x2 - x1) * t)
        let ly = y1 + int(float(y2 - y1) * t)
        setLocal lx ly content section

    section

  let checkerBorder
    x
    y
    width
    height
    odd
    even
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    for bx in 0 .. width - 1 do
      let top = if bx % 2 = 0 then odd else even
      let bottom = if (bx + height - 1) % 2 = 0 then odd else even
      setLocal (x + bx) y top section |> ignore
      setLocal (x + bx) (y + height - 1) bottom section |> ignore

    for by in 1 .. height - 2 do
      let left = if by % 2 = 0 then odd else even
      let right = if (by + width - 1) % 2 = 0 then odd else even
      setLocal x (y + by) left section |> ignore
      setLocal (x + width - 1) (y + by) right section |> ignore

    section

  let inline generate
    x
    y
    width
    height
    ([<InlineIfLambda>] generator: int -> int -> 'T)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let x1 = max 0 x
    let y1 = max 0 y
    let x2 = min section.Width (x + width)
    let y2 = min section.Height (y + height)

    if x2 > x1 && y2 > y1 then
      let grid = section.BackingGrid
      let gw = grid.Width
      let startX = section.OffsetX + x1
      let startY = section.OffsetY + y1
      let fillW = x2 - x1
      let fillH = y2 - y1

      for fy in 0 .. fillH - 1 do
        let ly = y1 + fy
        let rowStart = startX + (startY + fy) * gw

        for fx in 0 .. fillW - 1 do
          let lx = x1 + fx
          grid.Cells.[rowStart + fx] <- ValueSome(generator lx ly)

    section

  let inline iter
    x
    y
    width
    height
    ([<InlineIfLambda>] action: int -> int -> 'T voption -> unit)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let w = section.BackingGrid.Width

    for fx in x .. x + width - 1 do
      for fy in y .. y + height - 1 do
        let gx = section.OffsetX + fx
        let gy = section.OffsetY + fy

        if
          gx >= 0
          && gx < section.BackingGrid.Width
          && gy >= 0
          && gy < section.BackingGrid.Height
        then
          let idx = gx + gy * w
          let cell = section.BackingGrid.Cells.[idx]
          action fx fy cell

    section

  let inline map
    x
    y
    width
    height
    ([<InlineIfLambda>] mapping: 'T -> 'T)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let w = section.BackingGrid.Width

    for fx in x .. x + width - 1 do
      for fy in y .. y + height - 1 do
        let gx = section.OffsetX + fx
        let gy = section.OffsetY + fy

        if
          gx >= 0
          && gx < section.BackingGrid.Width
          && gy >= 0
          && gy < section.BackingGrid.Height
        then
          let idx = gx + gy * w

          match section.BackingGrid.Cells.[idx] with
          | ValueSome content ->
            section.BackingGrid.Cells.[idx] <- ValueSome(mapping content)
          | ValueNone -> ()

    section

  let line
    x1
    y1
    x2
    y2
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let dx = abs(x2 - x1)
    let dy = -abs(y2 - y1)
    let sx = if x1 < x2 then 1 else -1
    let sy = if y1 < y2 then 1 else -1
    let mutable err = dx + dy
    let mutable cx = x1
    let mutable cy = y1

    while not(cx = x2 && cy = y2) do
      setLocal cx cy content section
      let e2 = 2 * err

      if e2 >= dy then
        err <- err + dy
        cx <- cx + sx

      if e2 <= dx then
        err <- err + dx
        cy <- cy + sy

    setLocal cx cy content section
    section

  let circle
    cx
    cy
    radius
    filled
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let mutable x = radius
    let mutable y = 0
    let mutable err = 1 - x

    let plot xc yc x y =
      if filled then
        let drawLine x1 x2 y =
          let startX = min x1 x2
          let endX = max x1 x2

          for i in startX..endX do
            setLocal (xc + i) (yc + y) content section

        drawLine (-x) x y
        drawLine (-x) x (-y)
        drawLine (-y) y x
        drawLine (-y) y (-x)
      else
        setLocal (xc + x) (yc + y) content section
        setLocal (xc - x) (yc + y) content section
        setLocal (xc + x) (yc - y) content section
        setLocal (xc - x) (yc - y) content section
        setLocal (xc + y) (yc + x) content section
        setLocal (xc - y) (yc + x) content section
        setLocal (xc + y) (yc - x) content section
        setLocal (xc - y) (yc - x) content section

    while x >= y do
      plot cx cy x y
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
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
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
                setLocal x y content section

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
            setLocal cx cy content section
            let e2 = 2 * err

            if e2 >= dy then
              err <- err + dy
              cx <- cx + sx

            if e2 <= dx then
              err <- err + dx
              cy <- cy + sy

          setLocal cx cy content section

        if points.Length > 1 then
          for i in 0 .. points.Length - 2 do
            let struct (x1, y1) = points.[i]
            let struct (x2, y2) = points.[i + 1]
            drawLineSegment (x1, y1) (x2, y2)

          let struct (lastX, lastY) = points.[points.Length - 1]
          let struct (firstX, firstY) = points.[0]
          drawLineSegment (lastX, lastY) (firstX, firstY)

      section

  let checker odd even (section: GridSection2D<'T>) : GridSection2D<'T> =
    let grid = section.BackingGrid
    let gw = grid.Width
    let startX = section.OffsetX
    let startY = section.OffsetY

    for fy in 0 .. section.Height - 1 do
      let rowStart = startX + (startY + fy) * gw

      for fx in 0 .. section.Width - 1 do
        let content = if (fx + fy) % 2 = 0 then odd else even
        grid.Cells.[rowStart + fx] <- ValueSome content

    section

  let scatter
    count
    seed
    content
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let rng = System.Random(seed)

    for _ in 1..count do
      let x = rng.Next(0, section.Width)
      let y = rng.Next(0, section.Height)
      setLocal x y content section

    section

  let clear x y width height (section: GridSection2D<'T>) : GridSection2D<'T> =
    let x1 = max 0 x
    let y1 = max 0 y
    let x2 = min section.Width (x + width)
    let y2 = min section.Height (y + height)

    if x2 > x1 && y2 > y1 then
      let grid = section.BackingGrid
      let gw = grid.Width
      let startX = section.OffsetX + x1
      let startY = section.OffsetY + y1
      let fillW = x2 - x1
      let fillH = y2 - y1

      for fy in 0 .. fillH - 1 do
        let rowStart = startX + (startY + fy) * gw

        for fx in 0 .. fillW - 1 do
          grid.Cells.[rowStart + fx] <- ValueNone

    section

  let replace
    oldContent
    newContent
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let w = section.BackingGrid.Width

    for x in 0 .. section.Width - 1 do
      for y in 0 .. section.Height - 1 do
        let gx = section.OffsetX + x
        let gy = section.OffsetY + y

        if
          gx >= 0
          && gx < section.BackingGrid.Width
          && gy >= 0
          && gy < section.BackingGrid.Height
        then
          let idx = gx + gy * w

          match section.BackingGrid.Cells.[idx] with
          | ValueSome c when c = oldContent ->
            section.BackingGrid.Cells.[idx] <- ValueSome newContent
          | _ -> ()

    section

  let replaceScatter
    oldContent
    newContent
    (probability: float32)
    seed
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let rng = System.Random(seed)

    for x in 0 .. section.Width - 1 do
      for y in 0 .. section.Height - 1 do
        let gx = section.OffsetX + x
        let gy = section.OffsetY + y

        if
          gx >= 0
          && gx < section.BackingGrid.Width
          && gy >= 0
          && gy < section.BackingGrid.Height
        then
          let idx = gx + gy * section.BackingGrid.Width

          match section.BackingGrid.Cells.[idx] with
          | ValueSome c when c = oldContent ->
            if float32(rng.NextDouble()) < probability then
              section.BackingGrid.Cells.[idx] <- ValueSome newContent
          | _ -> ()

    section

  let inline scatterStamp
    count
    seed
    ([<InlineIfLambda>] stamp: GridSection2D<'T> -> GridSection2D<'T>)
    (section': GridSection2D<'T>)
    : GridSection2D<'T> =
    let rng = System.Random(seed)

    for _ in 1..count do
      let x = rng.Next(0, section'.Width)
      let y = rng.Next(0, section'.Height)
      section' |> section x y stamp |> ignore

    section'

  let setIfEmpty x y content (section: GridSection2D<'T>) : GridSection2D<'T> =
    let gx = section.OffsetX + x
    let gy = section.OffsetY + y

    if
      gx >= 0
      && gx < section.BackingGrid.Width
      && gy >= 0
      && gy < section.BackingGrid.Height
    then
      let w = section.BackingGrid.Width
      let idx = gx + gy * w
      let cell = &section.BackingGrid.Cells.[idx]

      if cell.IsNone then
        cell <- ValueSome content

    section
