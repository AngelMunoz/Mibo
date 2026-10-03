module Mibo.Core.Tests.Occupancy

open Expecto
open System.Numerics
open Mibo.Layout

/// One test cell: what it holds, how many cells its instance covers, and how
/// tall it stands.
[<Struct>]
type private Cell = {
  Value: int
  Span: InstanceSpan
  Height: float32
}

let private one value : Cell = {
  Value = value
  Span = One
  Height = 1f
}

let private spanning value span : Cell = {
  Value = value
  Span = span
  Height = 1f
}

let private plate value height span : Cell = {
  Value = value
  Span = span
  Height = height
}

let private spanOf(cell: Cell) = cell.Span
let private heightOf(cell: Cell) = cell.Height

let private squareGrid width height : CellGrid2D<Cell> =
  CellGrid2D.create width height (Vector2(1f, 1f)) Vector2.Zero

let private hexGrid width height : CellGrid2D<Cell> =
  CellGrid2D.createHex {
    Orientation = HexOrientation.PointyTop
    Width = width
    Height = height
    Radius = 1f
    Origin = Vector2.Zero
  }

let private okResult (label: string) (value: Result<'T, string>) : 'T =
  match value with
  | Ok scanned -> scanned
  | Error reason -> failtestf "%s: %s" label reason

let private errorText (label: string) (value: Result<'T, string>) : string =
  match value with
  | Ok _ -> failtestf "%s: expected an error" label
  | Error reason -> reason

let private owned (grid: CellGrid2D<Cell>) (occupancy: Occupancy) =
  let mutable count = 0

  for y in 0 .. grid.Height - 1 do
    for x in 0 .. grid.Width - 1 do
      if (Occupancy.owner x y occupancy).IsSome then
        count <- count + 1

  count

let private inWindow
  occupancy
  left
  top
  right
  bottom
  : (int * int * int * int)[] =
  let found = ResizeArray<int * int * int * int>()

  Occupancy.iterInWindow
    left
    top
    right
    bottom
    (fun at rect -> found.Add(at.X, at.Y, rect.W, rect.H))
    occupancy

  found.ToArray()

[<Tests>]
let tests =
  testList "Occupancy" [
    testList "scan" [
      testCase "a square span expands to its rectangle"
      <| fun _ ->
        let grid = squareGrid 8 6
        CellGrid2D.set 2 1 (spanning 1 (Span(3, 2))) grid
        CellGrid2D.set 6 5 (one 2) grid

        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal
          occupancy.Cells.Length
          2
          "the span and the plain cell anchor"

        Expect.equal
          occupancy.Rects.[0]
          { X = 2; Y = 1; W = 3; H = 2 }
          "the span's rect"

        Expect.equal occupancy.Claimed 0 "nothing was hidden"

        Expect.equal
          (Occupancy.owner 4 2 occupancy)
          (ValueSome { X = 2; Y = 1 })
          "a covered cell resolves to its anchor"

        Expect.equal
          (Occupancy.owner 2 1 occupancy)
          (ValueSome { X = 2; Y = 1 })
          "the anchor's own cell resolves to itself"

        Expect.equal
          (Occupancy.owner 6 5 occupancy)
          (ValueSome { X = 6; Y = 5 })
          "a plain cell owns itself"

        Expect.equal
          (Occupancy.owner 0 0 occupancy)
          ValueNone
          "an empty cell has no owner"

        Expect.equal
          (Occupancy.rectOf { X = 4; Y = 2 } occupancy)
          (ValueSome { X = 2; Y = 1; W = 3; H = 2 })
          "the rect of the owning anchor"

      testCase "a plain cell under a span is covered and counted"
      <| fun _ ->
        let grid = squareGrid 4 2

        for y in 0..1 do
          for x in 0..3 do
            CellGrid2D.set x y (one x) grid

        CellGrid2D.set 0 0 (spanning 9 (Span(2, 2))) grid

        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal
          occupancy.Claimed
          3
          "three populated cells hide under the span"

        Expect.equal
          occupancy.Cells.Length
          5
          "one span and the four cells outside it"

        Expect.equal (owned grid occupancy) 8 "every cell carries a value"

        Expect.equal
          (Occupancy.owner 1 1 occupancy)
          (ValueSome { X = 0; Y = 0 })
          "the covered cell answers with the span"

      testCase "two overlapping spans fail and name both cells"
      <| fun _ ->
        let grid = squareGrid 6 4
        CellGrid2D.set 1 1 (spanning 1 (Span(3, 2))) grid
        CellGrid2D.set 2 2 (spanning 2 (Span(2, 2))) grid

        let reason = errorText "scan" (Occupancy.scan spanOf grid)

        Expect.isTrue (reason.Contains "(1,1)") "names the first anchor"
        Expect.isTrue (reason.Contains "(2,2)") "names the second anchor"
        Expect.isTrue (reason.Contains "overlaps") "reports an overlap"

      testCase "a square span past the edge fails and names the cell"
      <| fun _ ->
        let right = squareGrid 6 4
        CellGrid2D.set 4 1 (spanning 1 (Span(3, 2))) right

        let rightReason = errorText "right edge" (Occupancy.scan spanOf right)
        Expect.isTrue (rightReason.Contains "(4,1)") "names the anchor cell"

        Expect.isTrue
          (rightReason.Contains "past the grid edge")
          "reports the edge"

        let bottom = squareGrid 6 4
        CellGrid2D.set 1 3 (spanning 1 (Span(2, 2))) bottom

        let bottomReason =
          errorText "bottom edge" (Occupancy.scan spanOf bottom)

        Expect.isTrue (bottomReason.Contains "(1,3)") "names the anchor cell"

      testCase "a hex disc past any edge fails and names the cell"
      <| fun _ ->
        let cases = [ "left", 0, 4; "right", 8, 4; "top", 4, 0; "bottom", 4, 8 ]

        for label, x, y in cases do
          let grid = hexGrid 9 9
          CellGrid2D.set x y (spanning 1 (Radius 1)) grid

          let reason = errorText label (Occupancy.scan spanOf grid)

          Expect.isTrue
            (reason.Contains $"({x},{y})")
            $"{label}: names the anchor cell"

          Expect.isTrue
            (reason.Contains "past the grid edge")
            $"{label}: reports the edge"

      testCase "a span shape must match the grid geometry"
      <| fun _ ->
        let hex = hexGrid 9 9
        CellGrid2D.set 4 4 (spanning 1 (Span(2, 2))) hex

        let hexReason =
          errorText "square span on hex" (Occupancy.scan spanOf hex)

        Expect.isTrue
          (hexReason.Contains "square grid")
          "asks for a square grid"

        Expect.isTrue (hexReason.Contains "(4,4)") "names the cell"

        let square = squareGrid 9 9
        CellGrid2D.set 4 4 (spanning 1 (Radius 1)) square

        let squareReason =
          errorText "radius on square" (Occupancy.scan spanOf square)

        Expect.isTrue (squareReason.Contains "hex grid") "asks for a hex grid"
        Expect.isTrue (squareReason.Contains "(4,4)") "names the cell"

      testCase "a span that covers nothing fails"
      <| fun _ ->
        for span in [ Span(0, 4); Span(-1, 2); Span(4, 0) ] do
          let grid = squareGrid 6 4
          CellGrid2D.set 2 2 (spanning 1 span) grid

          let reason = errorText $"{span}" (Occupancy.scan spanOf grid)

          Expect.isTrue
            (reason.Contains "spans nothing")
            $"{span} covers nothing"

        let hex = hexGrid 6 6
        CellGrid2D.set 2 2 (spanning 1 (Radius -1)) hex

        let hexReason = errorText "negative radius" (Occupancy.scan spanOf hex)

        Expect.isTrue
          (hexReason.Contains "spans nothing")
          "a negative radius covers nothing"

      testCase "a hex disc expands to a disc with a (2r+1) box"
      <| fun _ ->
        let grid = hexGrid 9 9
        CellGrid2D.set 4 4 (spanning 1 (Radius 2)) grid

        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal
          occupancy.Rects.[0]
          { X = 2; Y = 2; W = 5; H = 5 }
          "the bounding box"

        Expect.equal (owned grid occupancy) 19 "a radius-2 disc covers 19 cells"

        Expect.equal
          (Occupancy.owner 2 4 occupancy)
          (ValueSome { X = 4; Y = 4 })
          "the leftmost cell of the disc"

        Expect.equal
          (Occupancy.owner 2 2 occupancy)
          ValueNone
          "a corner of the bounding box stays outside the disc"

        Expect.equal
          (Occupancy.rectOf { X = 6; Y = 4 } occupancy)
          (ValueSome { X = 2; Y = 2; W = 5; H = 5 })
          "the rect of the disc"

      testCase "the identity forms own one cell on any geometry"
      <| fun _ ->
        let square = squareGrid 4 4
        CellGrid2D.set 3 2 (spanning 1 (Span(1, 1))) square

        let squareOccupancy = okResult "span one" (Occupancy.scan spanOf square)
        Expect.equal squareOccupancy.Cells.Length 1 "one anchor"
        Expect.equal squareOccupancy.Claimed 0 "nothing hidden"

        Expect.equal
          (Occupancy.rectOf { X = 3; Y = 2 } squareOccupancy)
          (ValueSome { X = 3; Y = 2; W = 1; H = 1 })
          "the cell owns itself"

        let hex = hexGrid 4 4
        CellGrid2D.set 2 1 (spanning 1 (Radius 0)) hex

        let hexOccupancy = okResult "radius zero" (Occupancy.scan spanOf hex)
        Expect.equal hexOccupancy.Cells.Length 1 "one anchor"

        Expect.equal
          (Occupancy.owner 2 1 hexOccupancy)
          (ValueSome { X = 2; Y = 1 })
          "the cell owns itself"

        let hexSpan = hexGrid 4 4
        CellGrid2D.set 1 1 (spanning 1 (Span(1, 1))) hexSpan

        Expect.equal
          (okResult "span one on hex" (Occupancy.scan spanOf hexSpan))
            .Cells.Length
          1
          "the identity is legal on hex"

      testCase "an empty grid scans clean"
      <| fun _ ->
        let grid = squareGrid 5 3
        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal occupancy.Cells.Length 0 "no anchors"
        Expect.equal occupancy.Claimed 0 "nothing claimed"
        Expect.equal occupancy.Owner.Length 15 "one slot per cell"

        Expect.equal
          (Occupancy.owner 2 2 occupancy)
          ValueNone
          "nothing owns anything"

        Expect.equal
          (Occupancy.rectOf { X = 2; Y = 2 } occupancy)
          ValueNone
          "no rect"

      testCase "out of range queries answer ValueNone"
      <| fun _ ->
        let grid = squareGrid 4 4
        CellGrid2D.set 1 1 (one 1) grid
        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal (Occupancy.owner -1 0 occupancy) ValueNone "negative x"
        Expect.equal (Occupancy.owner 0 -1 occupancy) ValueNone "negative y"
        Expect.equal (Occupancy.owner 4 0 occupancy) ValueNone "past the width"
        Expect.equal (Occupancy.owner 0 4 occupancy) ValueNone "past the height"

        Expect.equal
          (Occupancy.rectOf { X = 9; Y = 9 } occupancy)
          ValueNone
          "a rect outside the grid"

      testCase "identity owns every populated cell and claims nothing"
      <| fun _ ->
        let grid = squareGrid 4 3
        CellGrid2D.set 0 0 (spanning 1 (Span(3, 2))) grid
        CellGrid2D.set 3 2 (one 2) grid

        let occupancy = Occupancy.identity grid

        Expect.equal occupancy.Cells.Length 2 "one anchor per populated cell"
        Expect.equal occupancy.Claimed 0 "nothing is hidden"

        Expect.equal
          (Occupancy.rectOf { X = 0; Y = 0 } occupancy)
          (ValueSome { X = 0; Y = 0; W = 1; H = 1 })
          "the span is not expanded"

        Expect.equal
          (Occupancy.owner 1 1 occupancy)
          ValueNone
          "the neighbours stay empty"
    ]

    testList "iterInWindow" [
      testCase "an anchor is reported while any of its rect is in the window"
      <| fun _ ->
        let grid = squareGrid 10 10
        CellGrid2D.set 1 1 (spanning 1 (Span(3, 3))) grid
        CellGrid2D.set 8 8 (one 2) grid

        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal
          (inWindow occupancy 3 3 4 4)
          [| (1, 1, 3, 3) |]
          "the anchor cell is outside the window, its rect is not"

        Expect.equal
          (inWindow occupancy 5 5 7 7)
          [||]
          "a window between the two anchors"

        Expect.equal
          (inWindow occupancy 7 7 9 9)
          [| (8, 8, 1, 1) |]
          "the plain cell"

        Expect.equal
          (inWindow occupancy 0 0 9 9)
          [| (1, 1, 3, 3); (8, 8, 1, 1) |]
          "the whole map"

      testCase "a window that touches no rect reports nothing"
      <| fun _ ->
        let grid = squareGrid 6 6
        CellGrid2D.set 2 2 (spanning 1 (Span(2, 2))) grid
        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal (inWindow occupancy 4 4 5 5) [||] "past the span's rect"

        Expect.equal
          (inWindow occupancy 3 3 3 3)
          [| (2, 2, 2, 2) |]
          "the last covered cell"
    ]

    testList "code-first consumer" [
      testCase "a grid, a scan, and the queries answer a map with no Markup"
      <| fun _ ->
        let grid = squareGrid 12 8
        CellGrid2D.set 0 0 (plate 1 1f (Span(12, 4))) grid
        CellGrid2D.set 2 6 (one 2) grid
        CellGrid2D.set 9 6 (one 3) grid

        let occupancy = okResult "scan" (Occupancy.scan spanOf grid)

        Expect.equal occupancy.Cells.Length 3 "the plate and the two props"

        Expect.equal
          (Occupancy.owner 7 2 occupancy)
          (ValueSome { X = 0; Y = 0 })
          "a cell over the plate reports the plate"

        Expect.equal
          (Occupancy.owner 9 6 occupancy)
          (ValueSome { X = 9; Y = 6 })
          "a prop reports itself"

        Expect.equal
          (inWindow occupancy 8 6 11 7)
          [| (9, 6, 1, 1) |]
          "only the prop is in the window"
    ]
  ]

[<Tests>]
let stackTests =
  testList "Stack" [
    testCase "a decoration stands on the plate beneath it"
    <| fun _ ->
      let bottom = squareGrid 4 2
      let top = squareGrid 4 2

      CellGrid2D.set 0 0 (plate 1 1f (Span(4, 2))) bottom
      CellGrid2D.set 2 1 (plate 2 0.5f One) top

      let lower = okResult "scan bottom" (Occupancy.scan spanOf bottom)
      let upper = okResult "scan top" (Occupancy.scan spanOf top)
      let feet = Stack.feet [| lower; upper |] [| bottom; top |] heightOf

      Expect.equal feet.Length 2 "one array per layer"
      Expect.equal feet.[0].Length 8 "one slot per cell"
      Expect.equal feet.[0].[0] 0f "the plate stands on the plane"
      Expect.equal feet.[1].[2 + 1 * 4] 1f "the decoration stands on the plate"
      Expect.equal feet.[1].[0] 1f "the rest of the plate is level"

    testCase "three layers stack additively"
    <| fun _ ->
      let grids = [| squareGrid 3 3; squareGrid 3 3; squareGrid 3 3 |]

      CellGrid2D.set 0 0 (plate 1 1f (Span(3, 3))) grids.[0]
      CellGrid2D.set 0 0 (plate 2 0.5f (Span(3, 3))) grids.[1]
      CellGrid2D.set 1 1 (plate 3 0.25f One) grids.[2]

      let occupancies =
        grids
        |> Array.map(fun grid -> okResult "scan" (Occupancy.scan spanOf grid))

      let feet = Stack.feet occupancies grids heightOf

      Expect.equal feet.[0].[4] 0f "the bottom layer is on the plane"
      Expect.equal feet.[1].[4] 1f "one layer below"
      Expect.equal feet.[2].[4] 1.5f "two layers below"

    testCase "a spanning anchor lifts its whole rectangle"
    <| fun _ ->
      let bottom = squareGrid 5 3
      let top = squareGrid 5 3

      CellGrid2D.set 1 0 (plate 1 2f (Span(3, 2))) bottom
      CellGrid2D.set 3 1 (plate 2 1f One) top

      let lower = okResult "scan bottom" (Occupancy.scan spanOf bottom)
      let upper = okResult "scan top" (Occupancy.scan spanOf top)
      let feet = Stack.feet [| lower; upper |] [| bottom; top |] heightOf

      Expect.equal feet.[1].[1 + 0 * 5] 2f "the anchor cell"
      Expect.equal feet.[1].[3 + 1 * 5] 2f "the far corner of the rect"
      Expect.equal feet.[1].[0] 0f "a column outside the rect"
      Expect.equal feet.[1].[1 + 2 * 5] 0f "a row below the rect"

    testCase "a layer above empty cells has a zero foot"
    <| fun _ ->
      let bottom = squareGrid 3 3
      let top = squareGrid 3 3

      CellGrid2D.set 0 0 (plate 1 3f One) bottom
      CellGrid2D.set 2 2 (one 2) top

      let lower = okResult "scan bottom" (Occupancy.scan spanOf bottom)
      let upper = okResult "scan top" (Occupancy.scan spanOf top)
      let feet = Stack.feet [| lower; upper |] [| bottom; top |] heightOf

      Expect.equal feet.[1].[2 + 2 * 3] 0f "nothing stands under the cell"

    testCase "array lengths and bounds are checked"
    <| fun _ ->
      let bottom = squareGrid 3 3
      let wider = squareGrid 4 3
      let lower = okResult "scan" (Occupancy.scan spanOf bottom)

      Expect.throwsT<System.ArgumentException>
        (fun () ->
          Stack.feet [| lower |] [| bottom; bottom |] heightOf |> ignore)
        "one occupancy for each grid"

      Expect.throwsT<System.ArgumentException>
        (fun () ->
          Stack.feet [| lower; lower |] [| bottom; wider |] heightOf |> ignore)
        "every layer spans the same map"

      let feet = Stack.feet [||] [||] heightOf
      Expect.equal feet.Length 0 "no layers, no heights"
  ]
