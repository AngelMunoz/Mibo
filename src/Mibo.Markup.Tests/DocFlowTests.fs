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
        "wideA", element "wideA" 2 2 8
        "wideB", element "wideB" 2 2 9
        "wide4", element "wide4" 4 2 7
      ]
    Doc.Span = ValueNone
    Doc.WithSpan = ValueNone
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

    plot w=20 h=8 cols="fixed 6 1 1" rows="fixed 3 1" areas="road woods; road lake" {
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

    plot w=16 h=8 cols="fixed 4 1" rows="fixed 3 1" {
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
    plot w=10 h=12 cols="1 1" {
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

    testCase "flow children stack onto the next row"
    <| fun _ ->
      // three flow children in two columns: the third wraps to row 1
      // instead of failing once row 0 is full
      buildGolden(
        """map 6 4 {
    generate grass
    plot w=6 h=4 cols="1 1" {
        plot { fill path }
        plot { fill block }
        plot { fill sand }
    }
}
""",
        "flow wraps",
        golden(
          6,
          4,
          fun s ->
            s |> Layout.fill 0 0 6 4 1 |> ignore
            s |> Layout.fill 0 0 3 2 5 |> ignore // the first free cell
            s |> Layout.fill 3 0 3 2 7 |> ignore // the second column
            s |> Layout.fill 0 2 3 2 4 |> ignore // wrapped to the next row
        )
      )

    testCase "a flow pack without declared tracks stacks its children"
    <| fun _ ->
      // one implicit column, so every child takes its own row
      buildGolden(
        """map 4 2 {
    generate grass
    plot w=4 h=2 pack=flow {
        plot { fill path }
        plot { fill block }
    }
}
""",
        "flow without tracks",
        golden(
          4,
          2,
          fun s ->
            s |> Layout.fill 0 0 4 2 1 |> ignore
            s |> Layout.fill 0 0 4 1 5 |> ignore
            s |> Layout.fill 0 1 4 1 7 |> ignore
        )
      )

    testCase "a flow child claims the tracks its own size needs"
    <| fun _ ->
      // one-cell tracks, two-by-two children: each child takes two tracks on
      // each axis, so the pair fills the map instead of overlapping
      buildGolden(
        """map 4 2 {
    generate grass
    plot w=4 h=2 cols="fixed 1 fixed 1 fixed 1 fixed 1" {
        wideA
        wideB
    }
}
""",
        "flow by footprint",
        golden(
          4,
          2,
          fun s ->
            s |> Layout.fill 0 0 4 2 1 |> ignore
            s |> Layout.fill 0 0 2 2 8 |> ignore
            s |> Layout.fill 2 0 2 2 9 |> ignore
        )
      )

    testCase "a flow child wraps when its tracks no longer fit the row"
    <| fun _ ->
      // three two-by-two children in four one-cell tracks: two fill the first
      // row of tracks, and the third wraps to the row below them
      buildGolden(
        """map 4 4 {
    generate grass
    plot w=4 h=4 cols="fixed 1 fixed 1 fixed 1 fixed 1" {
        wideA
        wideB
        wideA
    }
}
""",
        "flow wraps by footprint",
        golden(
          4,
          4,
          fun s ->
            s |> Layout.fill 0 0 4 4 1 |> ignore
            s |> Layout.fill 0 0 2 2 8 |> ignore
            s |> Layout.fill 2 0 2 2 9 |> ignore
            s |> Layout.fill 0 2 2 2 8 |> ignore
        )
      )

    testCase "a flow child wider than the declared tracks fails"
    <| fun _ ->
      let doc =
        """map 2 2 {
    plot w=2 h=2 cols="fixed 1 fixed 1" {
        wide4
    }
}
"""

      match DocFlow.build(surface, doc) with
      | Ok _ -> failtest "a child wider than the tracks must fail the build"
      | Error e ->
        Expect.stringContains e "'wide4'" "names the child"
        Expect.stringContains e "cannot hold" "says the tracks are too small"
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

    testCase "a document without a map reports what the resolver found"
    <| fun _ ->
      // the build resolves first and looks for the map node after, so a
      // document the resolver rejects never reaches a tripwire arm
      match build "element hedge { fill grass }\n" with
      | Ok _ -> failtest "a document with no map must not build"
      | Error e -> Expect.stringContains e "needs a map node" "names the gap"

    testCase "a zero slot span fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 cols="1 1" {
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
    plot w=8 h=6 cols="1 1" {
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
    plot w=8 h=6 cols="1 1" {
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
    plot w=8 h=6 gapx=1 gapy=2 cols="1 1" {
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
    plot w=8 h=6 cols="1 1" areas="road woods" {
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
    plot w=8 h=6 cols="1 1" {
        plot col=5 row=0 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "an out-of-grid slot must fail"
      | Error e ->
        Expect.stringContains
          e
          "runs past the 2 declared cols"
          "names the tracks"

        Expect.stringContains e "'plot'" "names the container"

    testCase "a col with a span past the declared tracks fails the build"
    <| fun _ ->
      // the span rides along: col=1 colspan=2 needs three columns
      match
        build
          """map 8 6 {
    plot w=8 h=6 cols="1 1" {
        plot col=1 colspan=2 row=0 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "a span past the grid must fail"
      | Error e ->
        Expect.stringContains
          e
          "runs past the 2 declared cols"
          "names the tracks"

        Expect.stringContains e "'plot'" "names the container"

    testCase "an area child with a stated span fails the build"
    <| fun _ ->
      // the area fixes the extent; a span next to it would be dropped
      match
        build
          """map 8 6 {
    plot w=8 h=6 cols="1 1" areas="road woods" {
        plot area=road colspan=2 { fill sand }
    }
}
"""
      with
      | Ok _ -> failtest "an area child with a span must fail"
      | Error e ->
        Expect.stringContains
          e
          "colspan=/rowspan= needs col= or row="
          "names the clash"

        Expect.stringContains e "'plot'" "names the container"

    testCase "a span on a plain flow child fails the build"
    <| fun _ ->
      match
        build
          """map 8 6 {
    plot w=8 h=6 cols="1 1" {
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

    testCase "a style rule names itself in either channel"
    <| fun _ ->
      // KDL spells the rule's name as a word argument, XML as the `name`
      // property; the property is the rule's key and must not be applied
      // as a style of its own
      let kdl =
        """map 8 6 {
    element hedge { border stone }
    style hedge w=4 h=2
    hedge x=2 y=2
}
"""

      let xml =
        """<map w="8" h="6">
  <element name="hedge"><border cell="stone" /></element>
  <style name="hedge" w="4" h="2" />
  <hedge x="2" y="2" />
</map>"""

      match DocFlow.build(surface, kdl), DocFlow.buildXml(surface, xml) with
      | Ok a, Ok b ->
        let mutable diffs = 0

        for y in 0 .. a.Height - 1 do
          for x in 0 .. a.Width - 1 do
            if CellGrid2D.get x y a <> CellGrid2D.get x y b then
              diffs <- diffs + 1

        Expect.equal diffs 0 "both channels read the rule's name"
      | kdlRes, xmlRes -> failtest(sprintf "kdl=%A xml=%A" kdlRes xmlRes)

    testCase "tracks and areas read the same in either syntax"
    <| fun _ ->
      // `rows="1"` arrives as a number in XML and a word in KDL, and  ";" separates the
      // template rows in both
      let kdl =
        """map 12 4 {
    generate grass
    plot w=12 h=4 cols="fixed 6 1 1" rows="1" areas="road woods; road lake" {
        plot area=road { fill path }
        plot area=woods { fill block }
        plot area=lake { fill sand }
    }
}
"""

      let xml =
        """<map w="12" h="4">
  <generate kernel="grass" />
  <plot w="12" h="4" cols="fixed 6 1 1" rows="1" areas="road woods; road lake">
    <plot area="road"><fill cell="path" /></plot>
    <plot area="woods"><fill cell="block" /></plot>
    <plot area="lake"><fill cell="sand" /></plot>
  </plot>
</map>"""

      match DocFlow.build(surface, kdl), DocFlow.buildXml(surface, xml) with
      | Ok a, Ok b ->
        let mutable diffs = 0

        for y in 0 .. a.Height - 1 do
          for x in 0 .. a.Width - 1 do
            if CellGrid2D.get x y a <> CellGrid2D.get x y b then
              diffs <- diffs + 1

        Expect.equal diffs 0 "both syntaxes place the same tracks and areas"
      | kdlRes, xmlRes -> failtest(sprintf "kdl=%A xml=%A" kdlRes xmlRes)
  ]

/// The step-by-step build a caller runs when it needs the landmarks:
/// parse, resolve, locate the map node, then lay the emitted root over a
/// grid and keep what `Flow.run` hands back. `DocFlow.build` returns the
/// grid alone.
///
/// Every step already returns a `Result` or a `ValueOption`, so the
/// pipeline binds rather than nests.
let private buildWithLandmarks
  (src: string)
  : Result<struct (CellGrid2D<int> * Landmarks), string> =
  Kdl.parse src
  |> Result.bind(fun roots ->
    Doc.resolve surface src roots
    |> Result.bind(fun items ->
      Doc.findMapNode roots
      |> ValueOption.map(fun node ->
        Doc.dimsOf(src, node) |> Result.map(fun dims -> items, dims))
      |> ValueOption.defaultValue(Error "the document holds no map node")))
  |> Result.bind(fun (items, dims) ->
    match items with
    | [| root |] -> Ok struct (root, dims)
    | many -> Error $"the document resolved to {many.Length} roots")
  |> Result.map(fun struct (root, dims) ->
    let grid = CellGrid2D.create dims.W dims.H (Vector2(1f, 1f)) Vector2.Zero

    Flow.run (DocFlow.emit root) grid)

let private rectOf(x: int, y: int, w: int, h: int) : CellRect = {
  X = x
  Y = y
  W = w
  H = h
}

[<Tests>]
let landmarkTests =
  testList "DocFlow landmarks" [
    testCase "every use of an element reports its own resolved rectangle"
    <| fun _ ->
      let doc =
        """map 12 8 {
    element plaza w=3 h=2 { rect edge=stone floor=grass }
    plaza x=1 y=1
    plaza x=7 y=5
}
"""

      match buildWithLandmarks doc with
      | Error e -> failtest $"the build failed: {e}"
      | Ok struct (_, marks) ->
        let rects = Flow.taggedRects "plaza" marks

        Expect.equal rects.Length 2 "two uses, two rectangles"
        Expect.contains rects (rectOf(1, 1, 3, 2)) "the first use's rectangle"
        Expect.contains rects (rectOf(7, 5, 3, 2)) "the second use's rectangle"

    testCase "the per-cell bit grid answers inside and outside a region"
    <| fun _ ->
      let doc =
        """map 12 8 {
    element plaza w=3 h=2 { rect edge=stone floor=grass }
    plaza x=1 y=1
}
"""

      match buildWithLandmarks doc with
      | Error e -> failtest $"the build failed: {e}"
      | Ok struct (_, marks) ->
        Expect.isTrue
          (Flow.isTag "plaza" { X = 1; Y = 1 } marks)
          "the region's corner is tagged"

        Expect.isTrue
          (Flow.isTag "plaza" { X = 3; Y = 2 } marks)
          "the region's far corner is tagged"

        Expect.isFalse
          (Flow.isTag "plaza" { X = 4; Y = 2 } marks)
          "one cell past the region is not"

        Expect.isFalse
          (Flow.isTag "plaza" { X = 0; Y = 0 } marks)
          "one cell before the region is not"

    testCase "the anonymous plot reports under its own word"
    <| fun _ ->
      let doc =
        """map 10 6 {
    plot x=2 y=1 w=4 h=3 { fill sand }
}
"""

      match buildWithLandmarks doc with
      | Error e -> failtest $"the build failed: {e}"
      | Ok struct (_, marks) ->
        Expect.contains
          (Flow.taggedRects "plot" marks)
          (rectOf(2, 1, 4, 3))
          "the plot's rectangle"

        Expect.isTrue
          (Flow.isTag "plot" { X = 5; Y = 3 } marks)
          "a cell inside the plot"

    testCase "a nested element reports inside its container"
    <| fun _ ->
      let doc =
        """map 14 9 {
    element yard w=8 h=5 { fill grass }
    element shed w=3 h=2 { fill dirt }
    yard x=2 y=2 { shed x=1 y=1 }
}
"""

      match buildWithLandmarks doc with
      | Error e -> failtest $"the build failed: {e}"
      | Ok struct (_, marks) ->
        let yard = Flow.taggedRects "yard" marks
        let shed = Flow.taggedRects "shed" marks

        Expect.equal yard.Length 1 "one yard"
        Expect.equal shed.Length 1 "one shed"
        Expect.contains yard (rectOf(2, 2, 8, 5)) "the yard's rectangle"

        Expect.contains
          shed
          (rectOf(3, 3, 3, 2))
          "the shed's rectangle, offset inside the yard"
  ]

/// The emitted layers of a KDL document, without painting them: parse,
/// resolve, then `emitLayers`. Each step already returns a `Result`, so
/// the pipeline binds rather than nests.
let private emitOf(src: string) : struct (string * Stamp<int>)[] =
  Kdl.parse src
  |> Result.bind(fun roots -> Doc.resolve surface src roots)
  |> function
    | Error e -> failtest $"the document did not build: {e}"
    | Ok [| root |] -> DocFlow.emitLayers root
    | Ok many -> failtest $"expected exactly one root item, got {many.Length}"

let private layerNames(built: DocFlow.BuiltLayer<int>[]) : string[] =
  built |> Array.map(fun l -> l.Name)

let private equalGrid<'T when 'T: equality>
  (a: CellGrid2D<'T>)
  (b: CellGrid2D<'T>)
  (what: string)
  =
  Expect.equal
    struct (a.Width, a.Height)
    struct (b.Width, b.Height)
    $"{what}: the same size"

  let mutable diffs = 0

  for y in 0 .. a.Height - 1 do
    for x in 0 .. a.Width - 1 do
      if CellGrid2D.get x y a <> CellGrid2D.get x y b then
        diffs <- diffs + 1

  Expect.equal diffs 0 $"{what}: the same cells"

/// Two layers: `ground` fills the map, `decor` paints a 2x2 block.
let private twoLayerKdl =
  """map 8 6 {
    layer ground {
        fill grass
    }

    layer decor {
        fillRect 1 1 2 2 stone
    }
}
"""

/// The map's own paint, a non-layer child, and one stated layer: three
/// sources of cells, `main` first.
let private mainPlusLayerKdl =
  """map 8 6 {
    fill grass

    plot x=4 y=4 w=3 h=2 { fill sand }

    layer decor {
        fillRect 1 1 2 2 stone
    }
}
"""

let private twoLayerXml =
  """<map w="8" h="6">
  <layer name="ground">
    <fill cell="grass" />
  </layer>
  <layer name="decor">
    <fillRect x="1" y="1" w="2" h="2" cell="stone" />
  </layer>
</map>"""

[<Tests>]
let layerBuildTests =
  testList "DocFlow layers" [
    testCase "each layer builds its own grid, in layer order"
    <| fun _ ->
      match DocFlow.buildLayers(surface, twoLayerKdl) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok built ->
        Expect.equal (layerNames built) [| "ground"; "decor" |] "bottom first"

        Expect.equal
          struct (built[0].Grid.Width, built[0].Grid.Height)
          struct (8, 6)
          "a layer spans the map"

        Expect.equal
          struct (built[1].Grid.Width, built[1].Grid.Height)
          struct (8, 6)
          "the upper layer spans it too"

        Expect.equal
          (CellGrid2D.get 0 0 built[0].Grid)
          (ValueSome 1)
          "the ground layer paints the first cell"

        Expect.equal
          (CellGrid2D.get 7 5 built[0].Grid)
          (ValueSome 1)
          "and the last one"

        Expect.equal
          (CellGrid2D.get 1 1 built[1].Grid)
          (ValueSome 3)
          "the decor layer paints inside"

        Expect.equal
          (CellGrid2D.get 2 2 built[1].Grid)
          (ValueSome 3)
          "and to the end of its block"

        Expect.equal
          (CellGrid2D.get 0 0 built[1].Grid)
          ValueNone
          "the upper layer keeps its empty cells empty"

        Expect.equal
          (CellGrid2D.get 7 5 built[1].Grid)
          ValueNone
          "with no ground showing through"

    testCase "a partial layer still paints at its own cells"
    <| fun _ ->
      // the stretch case: every statement is area-bound, so the layer
      // measures a partial footprint. A plain stack child would dock it
      // at the map's top-left corner instead of at its own rows.
      let doc =
        """map 8 6 {
    layer road {
        fillRect 0 2 8 1 way
    }
}
"""

      match DocFlow.buildLayers(surface, doc) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok built ->
        Expect.equal (layerNames built) [| "road" |] "one layer"

        Expect.equal
          (CellGrid2D.get 0 2 built[0].Grid)
          (ValueSome 6)
          "the band paints on its own row"

        Expect.equal
          (CellGrid2D.get 7 2 built[0].Grid)
          (ValueSome 6)
          "and across the map"

        Expect.equal
          (CellGrid2D.get 0 0 built[0].Grid)
          ValueNone
          "nothing above it"

        Expect.equal
          (CellGrid2D.get 0 3 built[0].Grid)
          ValueNone
          "nothing below it"

    testCase "the map's bare paint is main, below every layer"
    <| fun _ ->
      let doc = mainPlusLayerKdl

      match DocFlow.buildLayers(surface, doc) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok built ->
        Expect.equal
          (layerNames built)
          [| "main"; "decor" |]
          "main comes first, the stated layer after it"

        Expect.equal
          (CellGrid2D.get 0 0 built[0].Grid)
          (ValueSome 1)
          "main paints the map body"

        Expect.equal
          (CellGrid2D.get 5 5 built[0].Grid)
          (ValueSome 4)
          "and its non-layer children"

        Expect.equal
          (CellGrid2D.get 1 1 built[1].Grid)
          (ValueSome 3)
          "the layer paints its own grid"

        Expect.equal
          (CellGrid2D.get 0 0 built[1].Grid)
          ValueNone
          "which is empty elsewhere"

    testCase "a map with only layers has no main"
    <| fun _ ->
      let emitted = emitOf twoLayerKdl

      Expect.equal
        (emitted |> Array.map(fun struct (name, _) -> name))
        [| "ground"; "decor" |]
        "two entries, no main"

    testCase "a layer reports no region of its own"
    <| fun _ ->
      // a layer's rectangle is the whole map, so tagging it would answer
      // "the whole map" for every cell no element covers
      match DocFlow.buildLayers(surface, twoLayerKdl) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok built ->
        Expect.equal
          (Flow.taggedRects "ground" built[0].Landmarks)
          []
          "the ground layer's own name is not a region"

        Expect.equal
          (Flow.taggedRects "decor" built[1].Landmarks)
          []
          "the decor layer's own name is not a region"

    testCase "a document without layers emits one entry named main"
    <| fun _ ->
      let doc =
        """map 8 6 {
    fill grass
    plot x=1 y=1 w=2 h=2 { fill stone }
}
"""

      let emitted = emitOf doc
      Expect.equal (emitted |> Array.length) 1 "one layer"

      Expect.equal
        (emitted |> Array.map(fun struct (name, _) -> name))
        [| "main" |]
        "named main"

      match DocFlow.build(surface, doc) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok grid ->
        Expect.equal (CellGrid2D.get 1 1 grid) (ValueSome 3) "and it paints"

    testCase "KDL and XML build the same layers cell for cell"
    <| fun _ ->
      match
        DocFlow.buildLayers(surface, twoLayerKdl),
        DocFlow.buildLayersXml(surface, twoLayerXml)
      with
      | Ok kdl, Ok xml ->
        Expect.equal
          (layerNames kdl)
          (layerNames xml)
          "the same names, in order"

        Expect.equal kdl.Length xml.Length "the same number of layers"

        for i in 0 .. kdl.Length - 1 do
          Expect.equal kdl[i].Name xml[i].Name $"layer {i}: the same name"

          equalGrid kdl[i].Grid xml[i].Grid $"layer '{kdl[i].Name}'"
      | kdlRes, xmlRes -> failtest $"kdl=%A{kdlRes} xml=%A{xmlRes}"

    testCase "build names the layers of a multi-layer document"
    <| fun _ ->
      let reported(doc: string) =
        match DocFlow.build(surface, doc) with
        | Ok _ -> failtest "a two-layer document must not build as one grid"
        | Error e -> e

      let stated = reported twoLayerKdl

      Expect.stringContains stated "the document holds 2 layers" "counts them"
      Expect.stringContains stated "'ground', 'decor'" "names them"
      Expect.stringContains stated "DocFlow.buildLayers" "points at the plural"

      // the map's own body is layer 0, so the message names it first
      let withMain = reported mainPlusLayerKdl

      Expect.stringContains withMain "'main', 'decor'" "names main first"

    testCase "build succeeds on a one-layer document"
    <| fun _ ->
      let doc =
        """map 8 6 {
    layer ground {
        fill grass
    }
}
"""

      match DocFlow.build(surface, doc) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok grid ->
        Expect.equal (CellGrid2D.get 0 0 grid) (ValueSome 1) "the layer's cells"
        Expect.equal (CellGrid2D.get 7 5 grid) (ValueSome 1) "across the map"

    testCase "build succeeds on an XML document with one layer"
    <| fun _ ->
      let xml =
        """<map w="8" h="6">
  <layer name="decor">
    <fillRect x="1" y="1" w="2" h="2" cell="stone" />
  </layer>
</map>"""

      match DocFlow.buildXml(surface, xml) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok grid ->
        Expect.equal (CellGrid2D.get 1 1 grid) (ValueSome 3) "the layer's cells"
        Expect.equal (CellGrid2D.get 0 0 grid) ValueNone "and nothing else"
  ]

/// A cell that states how many cells its instance covers.
[<Struct>]
type private SpanCell = { Word: string; Span: InstanceSpan }

let private spanCell word span : SpanCell = { Word = word; Span = span }
let private grassCell = spanCell "grass" One
let private slabCell = spanCell "slab" (Span(3, 2))

/// A kernel that plants a spanning cell wherever the predicate holds, so a
/// document can make the scan meet spans the resolver never saw.
let private plantingKernel(plant: int -> int -> SpanCell voption) =
  Doc.Gen2(fun x y -> plant x y |> ValueOption.defaultValue grassCell)

let private spanSurface: Doc.Surface<SpanCell> = {
  Doc.Words = frozen [ "grass", grassCell; "slab", slabCell ]
  Doc.Kernels =
    frozen [
      "planted",
      plantingKernel(fun x y ->
        if x = 0 && y = 0 then ValueSome slabCell else ValueNone)
      "twice",
      plantingKernel(fun x y ->
        if (x = 0 && y = 0) || (x = 1 && y = 1) then
          ValueSome slabCell
        else
          ValueNone)
    ]
  Doc.Elements = frozen []
  Doc.Span = ValueSome(fun cell -> cell.Span)
  Doc.WithSpan = ValueSome(fun cell span -> { cell with Span = span })
}

let private spanKdl =
  """map 6 4 {
    layer ground {
        fill grass
        set 0 0 slab
    }
}
"""

let private spanXml =
  """<map w="6" h="4">
  <layer name="ground">
    <fill cell="grass" />
    <set x="0" y="0" cell="slab" />
  </layer>
</map>"""

[<Tests>]
let spanTests =
  testList "DocFlow spans" [
    testCase "buildLayers hands back the occupancy of every layer"
    <| fun _ ->
      match DocFlow.buildLayers(spanSurface, spanKdl) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok layers ->
        Expect.equal layers.Length 1 "one layer"
        Expect.equal layers[0].Name "ground" "the layer's name"

        let occupancy = layers[0].Occupancy

        Expect.equal
          occupancy.Cells.Length
          19
          "the span plus the cells it leaves"

        Expect.equal occupancy.Claimed 5 "the cells the span hides"

        Expect.equal
          (Occupancy.owner 2 1 occupancy)
          (ValueSome { X = 0; Y = 0 })
          "a covered cell answers with the span"

        Expect.equal
          (Occupancy.rectOf { X = 2; Y = 1 } occupancy)
          (ValueSome { X = 0; Y = 0; W = 3; H = 2 })
          "the span's rect"

    testCase "both syntaxes agree on the grids and the occupancies"
    <| fun _ ->
      match
        DocFlow.buildLayers(spanSurface, spanKdl),
        DocFlow.buildLayersXml(spanSurface, spanXml)
      with
      | Ok kdl, Ok xml ->
        equalGrid kdl[0].Grid xml[0].Grid "the two syntaxes"

        Expect.equal
          kdl[0].Occupancy.Cells.Length
          xml[0].Occupancy.Cells.Length
          "the anchor counts"

        Expect.equal
          kdl[0].Occupancy.Claimed
          xml[0].Occupancy.Claimed
          "the claimed counts"

        Expect.equal
          (Occupancy.rectOf { X = 1; Y = 1 } kdl[0].Occupancy)
          (Occupancy.rectOf { X = 1; Y = 1 } xml[0].Occupancy)
          "the same rect"
      | kdlRes, xmlRes -> failtest $"kdl=%A{kdlRes} xml=%A{xmlRes}"

    testCase
      "a kernel that makes two spans meet fails, naming the layer and the cell"
    <| fun _ ->
      let doc =
        """map 6 4 {
    layer ground {
        generate twice
    }
}
"""

      match DocFlow.buildLayers(spanSurface, doc) with
      | Ok _ -> failtest "two overlapping spans must fail the build"
      | Error e ->
        Expect.stringContains e "layer 'ground'" "names the layer"
        Expect.stringContains e "(0,0)" "names the first anchor"
        Expect.stringContains e "(1,1)" "names the second anchor"

    testCase "a kernel-made span builds when nothing overlaps"
    <| fun _ ->
      let doc =
        """map 6 4 {
    layer ground {
        generate planted
    }
}
"""

      match DocFlow.buildLayers(spanSurface, doc) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok layers ->
        // the kernel paints every cell, so the span and the cells it leaves
        // are the anchors
        Expect.equal
          layers[0].Occupancy.Cells.Length
          19
          "the span and the plain cells"

        Expect.equal layers[0].Occupancy.Claimed 5 "the cells the span hides"

        Expect.equal
          (Occupancy.owner 1 1 layers[0].Occupancy)
          (ValueSome { X = 0; Y = 0 })
          "the kernel's span owns the cells it covers"

    testCase "a single-grid build refuses a spanning document"
    <| fun _ ->
      let doc =
        """map 6 4 {
    fill grass
    set 0 0 slab
}
"""

      match DocFlow.build(spanSurface, doc) with
      | Ok _ -> failtest "a spanning document must not build as one grid"
      | Error e ->
        Expect.stringContains e "spanning word" "names the cause"
        Expect.stringContains e "DocFlow.buildLayers" "points at the plural"

    testCase "a set inside a layer keeps its stated span"
    <| fun _ ->
      let doc =
        """map 6 4 {
    layer ground {
        set 0 0 slab spanX=3 spanZ=2
    }
}
"""

      match DocFlow.buildLayers(spanSurface, doc) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok layers ->
        printfn
          "DEBUG layercell %A anchors %d"
          (CellGrid2D.get 0 0 layers[0].Grid
           |> ValueOption.map(fun cell -> cell.Span))
          layers[0].Occupancy.Cells.Length

        Expect.equal layers[0].Occupancy.Cells.Length 1 "one instance"

    testCase "a map with no spans builds as it always did"
    <| fun _ ->
      let doc =
        """map 6 4 {
    fill grass
}
"""

      match DocFlow.build(spanSurface, doc) with
      | Error e -> failtest $"the build failed: {e}"
      | Ok grid ->
        Expect.equal (CellGrid2D.get 3 2 grid) (ValueSome grassCell) "painted"
  ]
