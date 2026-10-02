module Mibo.Markup.Tests.DocFlow

open Expecto
open System.Collections.Frozen
open System.Collections.Generic
open System.Numerics
open Mibo.Layout
open Mibo.Markup

// Golden tests: one document per layout channel of the surface — exact
// plots and whole-map bodies, declared tracks with named areas, track
// slots with spans and flow children, alignment with padding, seeded
// scatter. The expected grids are hand-laid with the raw `Layout` ops,
// so the emitter is checked against the framework's own painting, not
// against itself.

let private frozen(pairs: (string * 'v) list) : FrozenDictionary<string, 'v> =
  let d = Dictionary<string, 'v>(pairs.Length)

  for k, v in pairs do
    d[k] <- v

  d.ToFrozenDictionary()

let private surface: Doc.Surface<int> =
  let wordCell = [
    "grass", 1
    "dirt", 2
    "stone", 3
    "sand", 4
    "path", 5
    "way", 6
    "block", 7
  ]

  let element name w h cell = {
    Doc.Name = name
    Doc.Extent = ValueSome { W = w; H = h }
    Doc.Body = [| Doc.Op.Fill cell |]
  }

  {
    Doc.Words = frozen wordCell
    Doc.Kernels =
      frozen [
        "dressed", Doc.Gen2(fun _ _ -> 1)
        "grass", Doc.Gen2(fun _ _ -> 1)
      ]
    Doc.Elements =
      frozen [
        "pine", element "pine" 1 1 8
        "boulder", element "boulder" 1 1 9
      ]
  }

let private build(src: string) : Result<CellGrid2D<int>, string> =
  DocFlow.build(surface, src)

/// A hand-laid grid: raw `Layout` ops over a plain grid.
let private golden
  (w: int, h: int, paint: GridSection2D<int> -> unit)
  : CellGrid2D<int> =
  let g = CellGrid2D.create w h (Vector2(1f, 1f)) Vector2.Zero

  let section: GridSection2D<int> = {
    BackingGrid = g
    OffsetX = 0
    OffsetY = 0
    Width = w
    Height = h
  }

  paint section
  g

let private expectGrid
  (expected: CellGrid2D<int>)
  (actual: CellGrid2D<int>)
  (name: string)
  : unit =
  Expect.equal actual.Width expected.Width $"{name}: width"
  Expect.equal actual.Height expected.Height $"{name}: height"

  let mutable diffs = 0
  let mutable first = ""

  for y in 0 .. expected.Height - 1 do
    for x in 0 .. expected.Width - 1 do
      if CellGrid2D.get x y expected <> CellGrid2D.get x y actual then
        diffs <- diffs + 1

        if first = "" then
          first <- $"({x},{y})"

  Expect.equal diffs 0 $"{name}: every cell identical (first diff {first})"

let private buildGolden(doc: string, name: string, expected: CellGrid2D<int>) =
  match build doc with
  | Ok grid -> expectGrid expected grid name
  | Error e -> failtest $"{name}: the build failed: {e}"

[<Tests>]
let goldenTests =
  testList "DocFlow goldens" [
    testCase "the corpus document: plots, templates, bodies"
    <| fun _ ->
      buildGolden(
        """map 24 10 {
    generate dressed

    plot x=16 y=0 w=3 h=3 { fill block }

    element plaza { rect edge=dirt floor=grass; set hplace=center vplace=center stone }
    plaza w=6 h=3 x=2 y=6

    element frame { rect edge=stone floor=grass }
    frame w=8 h=4 x=3 y=3 { plot x=1 y=1 w=1 h=1 { fill sand } }

    plot {
        fillRect 0 5 9 1 path
        fillRect 8 2 1 4 path
        fillRect 8 2 9 1 path
        fillRect 16 2 1 6 path
        fillRect 16 7 8 1 path
        set 0 5 way
        set 8 5 way
        set 8 2 way
        set 16 2 way
        set 16 7 way
        set 23 7 way
    }
}
""",
        "corpus",
        golden(
          24,
          10,
          fun s ->
            s |> Layout.fill 0 0 24 10 1 |> ignore // generate dressed
            s |> Layout.fill 16 0 3 3 7 |> ignore // the block plot

            // plaza: floor, dirt ring, centered stone
            s |> Layout.fill 2 6 6 3 1 |> ignore
            s |> Layout.border 2 6 6 3 2 |> ignore
            s |> Layout.set 4 7 3 |> ignore

            // frame: floor, stone ring, the nested sand plot
            s |> Layout.fill 3 3 8 4 1 |> ignore
            s |> Layout.border 3 3 8 4 3 |> ignore
            s |> Layout.set 4 4 4 |> ignore

            // the road and its waypoints
            s |> Layout.fill 0 5 9 1 5 |> ignore
            s |> Layout.fill 8 2 1 4 5 |> ignore
            s |> Layout.fill 8 2 9 1 5 |> ignore
            s |> Layout.fill 16 2 1 6 5 |> ignore
            s |> Layout.fill 16 7 8 1 5 |> ignore
            s |> Layout.set 0 5 6 |> ignore
            s |> Layout.set 8 5 6 |> ignore
            s |> Layout.set 8 2 6 |> ignore
            s |> Layout.set 16 2 6 |> ignore
            s |> Layout.set 16 7 6 |> ignore
            s |> Layout.set 23 7 6 |> ignore
        )
      )

    testCase "declared tracks with named areas"
    <| fun _ ->
      buildGolden(
        """map 20 8 {
    generate grass

    plot w=20 h=8 {
        cols fixed 6 1 1
        rows fixed 3 1
        areas {
            row road woods
            row road lake
        }
        plot area=road { fill path }
        plot area=woods { fill block }
        plot area=lake { fill sand }
    }
}
""",
        "areas",
        golden(
          20,
          8,
          fun s ->
            s |> Layout.fill 0 0 20 8 1 |> ignore
            s |> Layout.fill 0 0 6 8 5 |> ignore // road column, full height
            s |> Layout.fill 6 0 7 3 7 |> ignore // woods: the second track, fixed row
            s |> Layout.fill 6 3 7 5 4 |> ignore // lake: the second track, weighted row
        // the third declared column has no area name: the map's
        // generate fill keeps it
        )
      )

    testCase "track slots with spans and flow children"
    <| fun _ ->
      buildGolden(
        """map 16 8 {
    generate grass

    plot w=16 h=8 {
        cols fixed 4 1
        rows fixed 3 1
        plot col=0 row=0 colspan=2 rowspan=1 { fill path }
        plot col=1 row=1 { fill block }
        plot { fill sand }
    }
}
""",
        "slots",
        golden(
          16,
          8,
          fun s ->
            s |> Layout.fill 0 0 16 8 1 |> ignore
            s |> Layout.fill 0 0 16 3 5 |> ignore // the spanning slot
            s |> Layout.fill 4 3 12 5 7 |> ignore // the (1,1) slot
            s |> Layout.fill 0 3 4 5 4 |> ignore // the flow child takes the free cell
        )
      )

    testCase "alignment and padding in a stack"
    <| fun _ ->
      buildGolden(
        """map 14 9 {
    generate grass

    plot x=1 y=1 w=12 h=7 pad=2 {
        plot w=4 h=2 hplace=center vplace=end { fill sand }
        plot w=3 h=1 hplace=end vplace=start { fill block }
        plot x=1 y=2 { fill path }
    }
}
""",
        "align and pad",
        golden(
          14,
          9,
          fun s ->
            s |> Layout.fill 0 0 14 9 1 |> ignore
            s |> Layout.fill 5 4 4 2 4 |> ignore // centered, bottom of the padded box
            s |> Layout.fill 8 3 3 1 7 |> ignore // end-aligned, top
            s |> Layout.fill 4 5 7 1 5 |> ignore // from (1,2) to the inner far edge
        )
      )
  ]

[<Tests>]
let scatterTests =
  testList "DocFlow scatter" [
    testCase "seeded scatter stays inside, never overlaps, rebuilds identical"
    <| fun _ ->
      let doc =
        """map 12 8 {
    generate grass
    plot x=2 y=2 w=8 h=4 pack=scatter seed=13 { pine; pine; boulder; pine }
}
"""

      let count(g: CellGrid2D<int>) =
        let mutable pines = 0
        let mutable boulders = 0
        let mutable outside = 0

        CellGrid2D.iter
          (fun x y v ->
            if v = 8 then
              pines <- pines + 1

              if x < 2 || x > 9 || y < 2 || y > 5 then
                outside <- outside + 1
            elif v = 9 then
              boulders <- boulders + 1

              if x < 2 || x > 9 || y < 2 || y > 5 then
                outside <- outside + 1)
          g

        struct (pines, boulders, outside)

      match build doc, build doc with
      | Ok a, Ok b ->
        let struct (pines, boulders, outside) = count a
        Expect.equal pines 3 "three pines placed"
        Expect.equal boulders 1 "one boulder placed"
        Expect.equal outside 0 "every placement sits inside the plot"

        // scatter cells never overlap: 4 elements of 1x1 = 4 cells
        let struct (pines2, boulders2, _) = count b
        Expect.equal struct (pines2, boulders2) struct (3, 1) "same counts"

        expectGrid a b "scatter"

      | a, b -> failtest $"builds failed: {a} {b}"
  ]

[<Tests>]
let failureTests =
  testList "DocFlow failures" [
    testCase "resolution failures carry the document position"
    <| fun _ ->
      match build "map 8 6 {\n  unknown x=1 y=1\n}" with
      | Ok _ -> failtest "an unknown element must fail"
      | Error e ->
        Expect.stringContains e "unknown element 'unknown'" "names the element"
        Expect.stringContains e "2:" "carries the line"

    testCase "parse failures fail visibly"
    <| fun _ ->
      match build "map {" with
      | Ok _ -> failtest "malformed input must not build"
      | Error e -> Expect.isGreaterThan e.Length 0 "the error carries a message"
  ]
