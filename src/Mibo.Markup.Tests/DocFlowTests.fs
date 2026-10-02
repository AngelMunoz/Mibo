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

    testCase "a flow child lands past a deep rowspan"
    <| fun _ ->
      // both columns claim rows 0..3, so the flow child's first free
      // cell sits on row 4 — beyond the child count that once bounded
      // the scan
      buildGolden(
        """map 10 12 {
    generate grass
    plot w=10 h=12 {
        cols 1 1
        plot col=0 row=0 rowspan=4 { fill path }
        plot col=1 row=0 rowspan=4 { fill block }
        plot { fill sand }
    }
}
""",
        "rowspan then flow",
        golden(
          10,
          12,
          fun s ->
            s |> Layout.fill 0 0 10 12 1 |> ignore
            s |> Layout.fill 0 0 5 9 5 |> ignore // rows 0..3, column 0
            s |> Layout.fill 5 0 5 9 7 |> ignore // rows 0..3, column 1
            s |> Layout.fill 0 9 5 3 4 |> ignore // the flow child takes row 4
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
        let cells = HashSet<struct (int * int)>()

        CellGrid2D.iter
          (fun x y v ->
            if v = 8 || v = 9 then
              if v = 8 then
                pines <- pines + 1
              else
                boulders <- boulders + 1

              cells.Add struct (x, y) |> ignore

              if x < 2 || x > 9 || y < 2 || y > 5 then
                outside <- outside + 1)
          g

        struct (pines, boulders, outside, cells.Count)

      match build doc, build doc with
      | Ok a, Ok b ->
        let struct (pines, boulders, outside, distinct) = count a
        Expect.equal pines 3 "three pines placed"
        Expect.equal boulders 1 "one boulder placed"
        Expect.equal outside 0 "every placement sits inside the plot"

        // no two elements share a cell: 4 elements of 1x1 occupy 4
        // distinct cells
        Expect.equal distinct 4 "four distinct occupied cells"

        let struct (pines2, boulders2, _, _) = count b
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

    testCase "a zero slot span fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 {
        cols 1 1
        plot col=0 row=0 colspan=0 rowspan=1 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "a zero span must not silently become a flow child"
      | Error e ->
        Expect.stringContains e "spans of at least one" "a loud failure"

    testCase "a negative col= fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 {
        cols 1 1
        plot col=-1 row=0 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "a negative col must not clamp to zero"
      | Error e -> Expect.stringContains e "non-negative" "a loud failure"

    testCase "x= and y= inside a flow pack fail the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 {
        cols 1 1
        plot x=1 y=1 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "exact placement must not vanish inside flow"
      | Error e ->
        Expect.stringContains e "stack pack" "the error names the working pack"

    testCase "a gap mismatch fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 gapx=1 gapy=2 {
        cols 1 1
        plot { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "a gap mismatch must fail"
      | Error e -> Expect.stringContains e "one gap" "names the restriction"

    testCase "an unknown area name fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 {
        areas {
            row names="road woods"
        }
        plot area=lake { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "an unknown area must fail"
      | Error e ->
        Expect.stringContains e "lake" "names the area"
        Expect.stringContains e "declared areas" "names the area template"
        Expect.stringContains e "'plot'" "names the container"

    testCase "a slot past the declared tracks fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 {
        cols 1 1
        plot col=5 row=0 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "an out-of-grid slot must fail"
      | Error e -> Expect.stringContains e "outside" "names the tracks"

    testCase "a span on a plain flow child fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 {
        cols 1 1
        plot colspan=2 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "a dropped span must fail"
      | Error e -> Expect.stringContains e "colspan" "names the channel"
  ]

[<Tests>]
let parityTests =
  testList "DocFlow front-end parity" [
    testCase "the same document in KDL and in XML builds the same grid"
    <| fun _ ->
      let kdl =
        """map 12 8 {
    generate grass
    element plaza w=3 h=2 { rect edge=stone floor=grass }
    plaza x=2 y=2 { fill dirt }
    plot x=6 y=1 w=4 h=4 pack=scatter seed=7 { pine; pine; boulder }
    set 1 1 way
}
"""

      let xml =
        """<map w="12" h="8">
  <generate kernel="grass" />
  <element name="plaza" w="3" h="2"><rect edge="stone" floor="grass" /></element>
  <plaza x="2" y="2"><fill cell="dirt" /></plaza>
  <plot x="6" y="1" w="4" h="4" pack="scatter" seed="7">
    <pine /><pine /><boulder />
  </plot>
  <set x="1" y="1" cell="way" />
</map>"""

      match DocFlow.build(surface, kdl), DocFlow.buildXml(surface, xml) with
      | Ok a, Ok b ->
        // goldens compare cell for cell; this is the front-end parity
        // the parse-level test cannot express (args vs properties)
        let mutable diffs = 0

        for y in 0 .. a.Height - 1 do
          for x in 0 .. a.Width - 1 do
            if CellGrid2D.get x y a <> CellGrid2D.get x y b then
              diffs <- diffs + 1

        Expect.equal diffs 0 "both syntaxes build the identical grid"

      | kdlRes, xmlRes -> failtest(sprintf "kdl=%A xml=%A" kdlRes xmlRes)
  ]
