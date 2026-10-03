module Mibo.Markup.Tests.Doc

open Expecto
open System.Collections.Frozen
open System.Collections.Generic
open System.Numerics
open Mibo.Layout
open Mibo.Markup

let private frozen(pairs: (string * 'v) list) : FrozenDictionary<string, 'v> =
  let d = Dictionary<string, 'v>(pairs.Length)

  for k, v in pairs do
    d[k] <- v

  d.ToFrozenDictionary()

let private surface: Doc.Surface<int> = {
  Doc.Words =
    frozen [ "grass", 1; "dirt", 2; "stone", 3; "sand", 4; "way", 5; "cry", 6 ]
  Doc.Kernels = frozen [ "plain", Doc.Gen2(fun _ _ -> 7) ]
  Doc.Elements =
    let grove = {
      Doc.Name = "grove"
      Doc.Extent = ValueSome { W = 3; H = 3 }
      Doc.Body = [| Doc.Op.Fill 1 |]
    }

    frozen [ "grove", grove ]
  Doc.Span = ValueNone
  Doc.WithSpan = ValueNone
}

let private resolveKdl(src: string) : Result<Doc.Item<int>[], string> =
  Kdl.parse src |> Result.bind(fun roots -> Doc.resolve surface src roots)

/// A cell that states how many cells its instance covers, and lets a statement
/// size it — the two channels the span vocabulary adds to a surface.
[<Struct>]
type private SpanCell = { Word: string; Span: InstanceSpan }

let private spanCell word span : SpanCell = { Word = word; Span = span }
let private grassCell = spanCell "grass" One

let private spanSurface: Doc.Surface<SpanCell> = {
  Doc.Words =
    frozen [
      "grass", grassCell
      "stone", spanCell "stone" One
      "slab", spanCell "slab" (Span(4, 2))
      "hut", spanCell "hut" (Radius 1)
    ]
  Doc.Kernels = frozen [ "plain", Doc.Gen2(fun _ _ -> grassCell) ]
  Doc.Elements = frozen []
  Doc.Span = ValueSome(fun cell -> cell.Span)
  Doc.WithSpan = ValueSome(fun cell span -> { cell with Span = span })
}

let private resolveSpan(src: string) : Result<Doc.Item<SpanCell>[], string> =
  Kdl.parse src |> Result.bind(fun roots -> Doc.resolve spanSurface src roots)

[<Tests>]
let resolveTests =
  testList "Doc resolve" [
    testCase "styles cascade: rule then inline props"
    <| fun _ ->
      match
        resolveKdl
          """map 8 6 {
  element plaza { fill dirt }
  style plaza w=4 h=2 pack=flow
  plaza x=1 y=2 h=3 { fill grass }
}"""
      with
      | Ok [| root |] ->
        let plaza = root.Children[0]

        Expect.equal plaza.Name "plaza" "child name"

        Expect.equal
          plaza.Style.Size
          (ValueSome { W = 4; H = 3 })
          "the rule's w wins, the inline h wins"

        Expect.equal plaza.Style.At (ValueSome { X = 1; Y = 2 }) "inline x= y="
        Expect.equal plaza.Style.Pack (ValueSome Doc.Flow) "pack from the rule"

      | Error e -> failtest e
      | Ok _ -> failtest "expected exactly one root item"

    testCase "templates expand and merge bodies"
    <| fun _ ->
      match
        resolveKdl
          """map 8 6 {
  element thicket w=2 h=2 { fill dirt }
  thicket x=0 y=0 { border stone }
  thicket x=4 y=4 {}
}"""
      with
      | Ok [| root |] ->
        Expect.hasLength root.Children 2 "two uses of the template"

        let first = root.Children[0]

        Expect.equal
          first.Element.Extent
          (ValueSome { W = 2; H = 2 })
          "declared extent"

        Expect.equal
          first.Style.At
          (ValueSome { X = 0; Y = 0 })
          "use-site placement"

        // the declaration body paints, then the use-site body
        let scratch = CellGrid2D.create 8 6 (Vector2(1f, 1f)) Vector2.Zero

        let section: GridSection2D<int> = {
          BackingGrid = scratch
          OffsetX = 4
          OffsetY = 4
          Width = 2
          Height = 2
        }

        root.Children[1].Element.Paint section

        Expect.equal
          (CellGrid2D.get 4 4 scratch)
          (ValueSome 2)
          "the declaration body painted"

      | Error e -> failtest e
      | Ok _ -> failtest "expected exactly one root item"

    testCase "repeat duplicates known children"
    <| fun _ ->
      match
        resolveKdl
          """map 12 6 {
  plot x=1 y=1 w=9 h=4 {
    repeat 2 { grove }
  }
}"""
      with
      | Ok [| root |] ->
        let plot = root.Children[0]
        Expect.hasLength plot.Children 2 "repeat 2 duplicates the child"
        Expect.equal plot.Children[0].Name "grove" "the repeated child"

      | Error e -> failtest e
      | Ok _ -> failtest "expected exactly one root item"

    testCase "nested repeats fail on the total node cap"
    <| fun _ ->
      // each count is legal alone; the product passes the total
      match
        resolveKdl "map 4 4 {\n  repeat 500 { repeat 400 { grove } }\n}"
      with
      | Ok _ -> failtest "a nested repeat blowup must fail"
      | Error e ->
        Expect.stringContains e "past the 100000 node cap" "names the total cap"
        Expect.stringContains e "2:" "carries the line"

    testCase "tracks and areas resolve"
    <| fun _ ->
      match
        resolveKdl
          """map 10 6 {
  grid {
    cols 1 fixed 3 auto
    rows 2
    areas {
      r west main
      r side main
    }
    plot area=main { fill grass }
    grove col=0 row=0
  }
}"""
      with
      | Ok [| root |] ->
        let grid = root.Children[0]

        Expect.equal
          grid.Cols
          [| Weight 1f; Fixed 3; Auto |]
          "cols: ratio, fixed, auto"

        Expect.equal grid.Rows [| Weight 2f |] "rows: ratio"

        Expect.equal
          grid.Areas
          [| [| "west"; "main" |]; [| "side"; "main" |] |]
          "area rows"

        let plot = grid.Children[0]
        Expect.equal plot.Style.Area (ValueSome "main") "area placement"

        let grove = grid.Children[1]
        Expect.equal grove.Style.Col (ValueSome 0) "col placement"
        Expect.equal grove.Style.Row (ValueSome 0) "row placement"

      | Error e -> failtest e
      | Ok _ -> failtest "expected exactly one root item"

    testCase "statements resolve with named and positional slots"
    <| fun _ ->
      match
        resolveKdl
          """map 8 6 {
  plot {
    fill grass
    fillRect 1 1 4 2 stone
    set 0 0 way
    set hplace=end vplace=end cry
    border stone
    rect edge=stone floor=dirt
    generate plain
  }
}"""
      with
      | Ok [| root |] ->
        let plot = root.Children[0]

        Expect.equal
          plot.Children.Length
          0
          "statements are the body, not children"

        Expect.equal plot.Name "plot" "the plot resolved"

      | Error e -> failtest e
      | Ok _ -> failtest "expected exactly one root item"

    testCase "the set anchor form resolves one positional cell"
    <| fun _ ->
      // `set c` — no coordinates: the cell places by the two aligns
      match resolveKdl "map 8 6 {\n  plot {\n    set way\n  }\n}" with
      | Error e -> failtest $"the anchor form must resolve: {e}"
      | Ok [| root |] ->
        Expect.equal root.Children[0].Name "plot" "the body resolved"
      | Ok _ -> failtest "expected exactly one root item"

    testCase "a two-positional set fails loud"
    <| fun _ ->
      match resolveKdl "map 8 6 {\n  plot {\n    set way extra\n  }\n}" with
      | Ok _ -> failtest "an ambiguous set must fail"
      | Error e ->
        Expect.stringContains e "'set' wants" "names the accepted forms"

    testCase "a set mixing coordinate and align channels fails loud"
    <| fun _ ->
      // the aligns would pass the leftover check and never run
      match
        resolveKdl
          "map 8 6 {\n  plot {\n    set x=1 y=2 hplace=center way\n  }\n}"
      with
      | Ok _ -> failtest "a mixed-channel set must fail"
      | Error e -> Expect.stringContains e "not both" "names the clash"

    testCase "a set with both align spellings fails loud"
    <| fun _ ->
      // halign and hplace mean the same thing; the unpicked spelling
      // would pass unread
      match
        resolveKdl
          "map 8 6 {\n  plot {\n    set halign=start hplace=end way\n  }\n}"
      with
      | Ok _ -> failtest "a double-spelled align must fail"
      | Error e ->
        Expect.stringContains e "one spelling per axis" "names the rule"

    testCase "a map with a doubled dimension fails the build"
    <| fun _ ->
      match resolveKdl "map w=4 w=6 h=4 {\n  plot {}\n}" with
      | Ok _ -> failtest "the doubled dimension must fail"
      | Error e ->
        Expect.stringContains e "defines 'w' more than once" "names the typo"
        Expect.stringContains e "1:" "carries the line"

    testCase "an area row without names fails the build"
    <| fun _ ->
      // a names-less row would shift every row below it in the template
      match
        resolveKdl
          "map 8 6 {\n  grid {\n    cols 1 1\n    areas {\n      row\n      row names=\"road woods\"\n    }\n    plot area=road { fill grass }\n  }\n}"
      with
      | Ok _ -> failtest "the names-less area row must fail"
      | Error e -> Expect.stringContains e "an area row needs" "names the slip"

    testCase "a map with extra positional args fails the build"
    <| fun _ ->
      match resolveKdl "map 8 6 9 {\n  plot {}\n}" with
      | Ok _ -> failtest "the extra map argument must fail"
      | Error e ->
        Expect.stringContains e "extra argument" "names the leftover"
        Expect.stringContains e "1:" "carries the line"

    testCase "unknown elements fail with their position"
    <| fun _ ->
      match resolveKdl "map 8 6 {\n  plaza x=1 y=1\n}" with
      | Ok _ -> failtest "unknown element must fail"
      | Error e ->
        Expect.stringContains e "unknown element 'plaza'" "names the element"
        Expect.stringContains e "2:" "carries the line"

    testCase "unknown words fail with their position"
    <| fun _ ->
      match resolveKdl "map 8 6 {\n  plot {\n    fill moss\n  }\n}" with
      | Ok _ -> failtest "unknown word must fail"
      | Error e ->
        Expect.stringContains e "moss" "names the word"
        Expect.stringContains e "3:" "carries the line"

    testCase "unknown kernels in game element bodies fail the build"
    <| fun _ ->
      let bad: Doc.Surface<int> = {
        Doc.Words = frozen []
        Doc.Kernels = frozen [ "plain", Doc.Gen2(fun _ _ -> 7) ]
        Doc.Elements =
          let broken = {
            Doc.Name = "broken"
            Doc.Extent = ValueNone
            Doc.Body = [| Doc.Op.Generate("nope", ValueNone) |]
          }

          frozen [ "broken", broken ]
        Doc.Span = ValueNone
        Doc.WithSpan = ValueNone
      }

      match Kdl.parse "map 4 4 { broken }" with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        match Doc.resolve bad "map 4 4 { broken }" roots with
        | Ok _ -> failtest "the bad kernel must fail the build"
        | Error e ->
          Expect.stringContains e "nope" "names the kernel"
          Expect.stringContains e "broken" "names the element"

    testCase "bad properties fail with their position"
    <| fun _ ->
      match resolveKdl "map 8 6 {\n  plot x=fast y=1 {}\n}" with
      | Ok _ -> failtest "a bad property must fail"
      | Error e ->
        Expect.stringContains e "'x' wants a number" "names the property"

    testCase "a map mixes the two dimension channels"
    <| fun _ ->
      // a named dimension claims its slot, so the positional arg fills
      // the other one instead of being read as a leftover
      match resolveKdl "map 36 h=20 {\n  generate plain\n}" with
      | Ok [| root |] -> Expect.equal root.Name "map" "the mixed map resolves"
      | Ok _ -> failtest "expected exactly one root item"
      | Error e -> failtest $"a mixed map must resolve: {e}"

      match resolveKdl "map w=36 20 {\n  generate plain\n}" with
      | Ok [| root |] -> Expect.equal root.Name "map" "the other mix resolves"
      | Ok _ -> failtest "expected exactly one root item"
      | Error e -> failtest $"a mixed map must resolve: {e}"

    testCase "a map dimension past the two slots is a leftover"
    <| fun _ ->
      match resolveKdl "map 36 20 5 {\n  generate plain\n}" with
      | Ok _ -> failtest "a third dimension must fail"
      | Error e -> Expect.stringContains e "extra argument" "names the leftover"

    testCase "a root node that is not the map fails the build"
    <| fun _ ->
      match
        resolveKdl "map 4 4 {\n  generate plain\n}\nplot { fill grass }"
      with
      | Ok _ -> failtest "a stray root must fail instead of vanishing"
      | Error e ->
        Expect.stringContains e "is not a root node" "says why"
        Expect.stringContains e "plot" "names the node"

    testCase "a second map fails the build"
    <| fun _ ->
      match
        resolveKdl
          "map 4 4 {\n  generate plain\n}\nmap 2 2 {\n  generate plain\n}"
      with
      | Ok _ -> failtest "two maps must fail"
      | Error e -> Expect.stringContains e "2 map nodes" "counts them"

    testCase "a document with no map says what a map needs"
    <| fun _ ->
      match resolveKdl "map {\n  generate plain\n}" with
      | Ok _ -> failtest "a document without a map must fail"
      | Error e ->
        Expect.stringContains e "map 36 20" "names the positional form"
        Expect.stringContains e "map w=36 h=20" "names the property form"

    testCase "a repeat past the cap fails the build"
    <| fun _ ->
      match resolveKdl "map 4 4 {\n  repeat 2000000000 { grove }\n}" with
      | Ok _ -> failtest "an unbounded repeat must fail"
      | Error e ->
        Expect.stringContains e "cap" "names the cap"
        Expect.stringContains e "2:" "carries the line"

    testCase "a duplicate template name fails the build"
    <| fun _ ->
      match
        resolveKdl
          """map 8 6 {
  element plaza { fill dirt }
  element plaza { fill grass }
  plaza x=1 y=1
}"""
      with
      | Ok _ -> failtest "a duplicate template must fail"
      | Error e -> Expect.stringContains e "defined twice" "names the clash"

    testCase "a template colliding with a surface element fails the build"
    <| fun _ ->
      match
        resolveKdl
          """map 8 6 {
  element grove { fill dirt }
  grove x=1 y=1
}"""
      with
      | Ok _ -> failtest "the surface collision must fail"
      | Error e -> Expect.stringContains e "collides" "names the clash"

    testCase "tracks and areas read from the v and names properties"
    <| fun _ ->
      // the XML channel: no positional args anywhere
      let doc =
        "map 10 6 {\n  plot w=10 h=6 {\n    cols v=\"fixed 6 1 1\"\n    areas {\n      row names=\"road woods\"\n      row names=\"road lake\"\n    }\n    plot area=road { fill grass }\n  }\n}\n"

      match Kdl.parse doc with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        match Doc.resolve surface doc roots with
        | Ok [| root |] ->
          let grid = root.Children[0]

          Expect.equal
            grid.Cols
            [| Fixed 6; Weight 1f; Weight 1f |]
            "tracks from v="

          Expect.equal
            grid.Areas
            [| [| "road"; "woods" |]; [| "road"; "lake" |] |]
            "area rows from names="

          Expect.equal
            grid.Children[0].Style.Area
            (ValueSome "road")
            "the area placement resolves"

        | Error e -> failtest $"resolve failed: {e}"
        | Ok _ -> failtest "expected exactly one root item"
  ]

[<Tests>]
let resolveLayerTests =
  testList "Doc layers" [
    testCase "a two-layer document resolves, and only the layers name one"
    <| fun _ ->
      match
        resolveKdl
          """map 8 6 {
  layer ground {
    generate plain
  }
  layer decor {
    plot x=1 y=1 w=2 h=2 { fill stone }
  }
}"""
      with
      | Ok [| root |] ->
        Expect.equal root.Layer ValueNone "the map is not a layer"
        Expect.equal root.Name "map" "the root keeps its name"
        Expect.equal root.Children.Length 2 "two children, one per layer"

        let ground = root.Children[0]
        let decor = root.Children[1]

        Expect.equal
          ground.Layer
          (ValueSome "ground")
          "the first layer carries its name"

        Expect.equal ground.Name "ground" "and reports under it"
        Expect.equal decor.Layer (ValueSome "decor") "the second layer"
        Expect.equal decor.Name "decor" "and reports under its own name"

        Expect.equal
          decor.Children[0].Layer
          ValueNone
          "a plot inside a layer is not a layer"

        Expect.equal
          decor.Children[0].Name
          "plot"
          "the plot keeps the anonymous name"
      | Error e -> failtest $"resolve failed: {e}"
      | Ok _ -> failtest "expected exactly one root item"

    testCase "bare paint stays on the map, not in a layer"
    <| fun _ ->
      match
        resolveKdl
          """map 8 6 {
  fill grass
  plot x=1 y=1 w=2 h=2 { fill stone }
  layer decor { fill dirt }
}"""
      with
      | Ok [| root |] ->
        Expect.equal root.Layer ValueNone "the map's own body is not a layer"

        Expect.equal root.Element.Body.Length 1 "the map's body is its own"
        Expect.equal root.Children.Length 2 "one plain child and one layer"
        Expect.equal root.Children[0].Layer ValueNone "the plain plot"
        Expect.equal root.Children[1].Layer (ValueSome "decor") "the layer"
      | Error e -> failtest $"resolve failed: {e}"
      | Ok _ -> failtest "expected exactly one root item"

    testCase "a duplicate layer name fails at the second node"
    <| fun _ ->
      match
        resolveKdl
          "map 8 6 {\n  layer ground { fill grass }\n  layer ground { fill dirt }\n}"
      with
      | Ok _ -> failtest "the duplicate layer must fail"
      | Error e ->
        Expect.stringContains
          e
          "layer 'ground' is declared twice"
          "names the clash"

        Expect.stringContains e "3:" "carries the second node's line"

    testCase "a layer inside a plot fails"
    <| fun _ ->
      match
        resolveKdl
          "map 8 6 {\n  plot x=1 y=1 w=2 h=2 {\n    layer ground { fill grass }\n  }\n}"
      with
      | Ok _ -> failtest "a nested layer must fail"
      | Error e ->
        Expect.stringContains
          e
          "legal only directly under the map"
          "names the rule"

        Expect.stringContains e "3:" "carries the line"

    testCase "a layer inside an element fails"
    <| fun _ ->
      match
        resolveKdl
          "map 8 6 {\n  element plaza {\n    layer ground { fill grass }\n  }\n  plaza x=1 y=1\n}"
      with
      | Ok _ -> failtest "a layer in an element must fail"
      | Error e ->
        Expect.stringContains
          e
          "legal only directly under the map"
          "names the rule"

        Expect.stringContains e "3:" "carries the line"

    testCase "a layer inside a layer fails"
    <| fun _ ->
      match
        resolveKdl
          "map 8 6 {\n  layer ground {\n    layer decor { fill grass }\n  }\n}"
      with
      | Ok _ -> failtest "a layer in a layer must fail"
      | Error e ->
        Expect.stringContains
          e
          "legal only directly under the map"
          "names the rule"

        Expect.stringContains e "3:" "carries the line"

    testCase "a layer takes one name and nothing else"
    <| fun _ ->
      let rejects (doc: string) (expected: string) =
        match resolveKdl doc with
        | Ok _ -> failtest $"the document must fail: {doc}"
        | Error e -> Expect.stringContains e expected "names the rule"

      rejects
        "map 8 6 {\n  layer ground x=1 { fill grass }\n}"
        "a layer takes one name and nothing else"

      rejects
        "map 8 6 {\n  layer ground decor { fill grass }\n}"
        "a layer takes one name and nothing else"

      rejects "map 8 6 {\n  layer { fill grass }\n}" "a layer needs a name"

    testCase "an empty layer fails"
    <| fun _ ->
      let rejects doc =
        match resolveKdl doc with
        | Ok _ -> failtest $"the empty layer must fail: {doc}"
        | Error e ->
          Expect.stringContains
            e
            "holds no statements or children"
            "names the slip"

          Expect.stringContains e "2:" "carries the line"

      // a declaration is a leaf: on its own it leaves the layer painting
      // nothing, exactly as an empty layer does
      rejects "map 8 6 {\n  layer ground { }\n}"
      rejects "map 8 6 {\n  layer ground { element hedge { fill grass } }\n}"

    testCase "a style rule named layer applies to every layer"
    <| fun _ ->
      match
        resolveKdl
          "map 8 6 {\n  style layer pad=1\n  layer ground { fill grass }\n  layer decor { fill stone }\n}"
      with
      | Ok [| root |] ->
        Expect.equal
          root.Children[0].Style.Pad
          (ValueSome 1)
          "the first layer takes the rule"

        Expect.equal
          root.Children[1].Style.Pad
          (ValueSome 1)
          "so does the second"
      | Error e -> failtest $"resolve failed: {e}"
      | Ok _ -> failtest "expected exactly one root item"

    testCase "XML reads the layer name from the name property"
    <| fun _ ->
      let xml =
        """<map w="8" h="6">
  <layer name="ground">
    <generate kernel="plain" />
  </layer>
  <layer name="decor">
    <fill cell="stone" />
  </layer>
</map>"""

      match Xml.parse xml with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        match Doc.resolve surface xml roots with
        | Ok [| root |] ->
          Expect.equal root.Children.Length 2 "two layers"

          Expect.equal
            (root.Children |> Array.map(fun c -> c.Layer))
            [| ValueSome "ground"; ValueSome "decor" |]
            "both names read from the property"

          Expect.equal
            root.Children[1].Element.Extent
            ValueNone
            "the name is a key, not a style: the layer still stretches"
        | Error e -> failtest $"resolve failed: {e}"
        | Ok _ -> failtest "expected exactly one root item"
  ]

[<Tests>]
let measureTests =
  testList "Doc measure" [
    testCase "a greedy body measures the available size"
    <| fun _ ->
      let el = Doc.paintOf(surface, ValueNone, [| Doc.Op.Fill 1 |])

      Expect.equal
        (Doc.measure(el, { W = 5; H = 4 }))
        { W = 5; H = 4 }
        "fill covers all"

    testCase "a sparse body measures its bounds"
    <| fun _ ->
      let el =
        Doc.paintOf(
          surface,
          ValueNone,
          [| Doc.Op.Set(ValueSome { X = 2; Y = 1 }, Start, Start, 5) |]
        )

      Expect.equal
        (Doc.measure(el, { W = 8; H = 6 }))
        { W = 3; H = 2 }
        "the farthest cell bounds it"

    testCase "an empty body measures nothing"
    <| fun _ ->
      let el = Doc.paintOf(surface, ValueNone, [||])

      Expect.equal
        (Doc.measure(el, { W = 5; H = 4 }))
        { W = 0; H = 0 }
        "nothing painted"

    testCase "measureOps derives extents without a grid"
    <| fun _ ->
      // the emitter path: pure arithmetic, no scratch grid
      Expect.equal
        (Doc.measureOps surface [|
          Doc.Op.FillRect({ X = 1; Y = 1; W = 4; H = 2 }, 1)
        |])
        (ValueSome { W = 5; H = 3 })
        "a bounded body derives its bounds"

      Expect.equal
        (Doc.measureOps surface [| Doc.Op.Fill 1 |])
        ValueNone
        "a greedy body reports stretch"

      Expect.equal
        (Doc.measureOps surface [||])
        ValueNone
        "an empty body stretches"

      Expect.equal
        (Doc.measureOps surface [|
          Doc.Op.Set(ValueSome { X = 2; Y = 1 }, Start, Start, 5)
        |])
        (ValueSome { W = 3; H = 2 })
        "a set at (2,1) bounds to 3x2"

      Expect.equal
        (Doc.measureOps surface [| Doc.Op.Border(ValueNone, 1) |])
        ValueNone
        "a whole-box border is greedy"

    testCase "a spanning set measures the whole instance"
    <| fun _ ->
      let slab = {
        SpanCell.Word = "slab"
        Span = Span(4, 2)
      }

      Expect.equal
        (Doc.measureOps spanSurface [|
          Doc.Op.Set(ValueSome { X = 1; Y = 1 }, Start, Start, slab)
        |])
        (ValueSome { W = 5; H = 3 })
        "the instance bounds the element"

      Expect.equal
        (Doc.measureOps spanSurface [|
          Doc.Op.Set(ValueSome { X = 0; Y = 0 }, Start, Start, grassCell)
        |])
        (ValueSome { W = 1; H = 1 })
        "a plain word still measures one cell"
  ]

[<Tests>]
let spanTests =
  testList "Doc spans" [
    testCase "an area statement rejects a spanning word with its position"
    <| fun _ ->
      let documents = [
        "fill", "map 8 6 {\n  fill slab\n}"
        "fillRect", "map 8 6 {\n  fillRect 0 0 4 2 slab\n}"
        "border", "map 8 6 {\n  border slab\n}"
        "rect", "map 8 6 {\n  rect slab stone\n}"
      ]

      for label, src in documents do
        match resolveSpan src with
        | Ok _ ->
          failtestf "%s: a spanning word must fail in an area statement" label
        | Error e ->
          Expect.stringContains e "'slab'" $"{label}: names the word"
          Expect.stringContains e "spans 4x2" $"{label}: names the span"
          Expect.stringContains e "2:" $"{label}: carries the line"

    testCase "set places a spanning word and the extent follows it"
    <| fun _ ->
      match resolveSpan "map 9 9 {\n  set 1 1 slab\n}" with
      | Ok [| root |] ->
        Expect.equal
          root.Element.Extent
          (ValueSome { W = 5; H = 3 })
          "the instance bounds the element"

      | Ok _ -> failtest "expected exactly one root item"
      | Error e -> failtest $"a spanning set must resolve: {e}"

    testCase "a stated span sizes the instance"
    <| fun _ ->
      match resolveSpan "map 9 9 {\n  set 1 1 slab spanX=2 spanZ=6\n}" with
      | Ok [| root |] ->
        Expect.equal
          root.Element.Extent
          (ValueSome { W = 3; H = 7 })
          "the stated span replaces the word's own"

        let cell = root.Element.Body |> Array.head

        match cell with
        | Doc.Op.Set(_, _, _, written) ->
          Expect.equal written.Span (Span(2, 6)) "the span lands in the cell"
        | _ -> failtest "expected a set statement"

      | Ok _ -> failtest "expected exactly one root item"
      | Error e -> failtest $"a stated span must resolve: {e}"

    testCase "a stated span needs both sides"
    <| fun _ ->
      match resolveSpan "map 9 9 {\n  set 1 1 slab spanX=2\n}" with
      | Ok _ -> failtest "half a span must fail"
      | Error e ->
        Expect.stringContains e "both spanX and spanZ" "asks for the pair"

    testCase "a stated span needs a surface that can write one"
    <| fun _ ->
      let pinned: Doc.Surface<int> = {
        Doc.Words = frozen [ "slab", 9 ]
        Doc.Kernels = frozen []
        Doc.Elements = frozen []
        Doc.Span = ValueSome(fun _ -> Span(4, 2))
        Doc.WithSpan = ValueNone
      }

      let src = "map 9 9 {\n  set 1 1 slab spanX=2 spanZ=2\n}"

      match Kdl.parse src with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        match Doc.resolve pinned src roots with
        | Ok _ -> failtest "a stated span must need a surface that writes one"
        | Error e ->
          Expect.stringContains e "cannot state a span" "names the gap"

    testCase "a stated box does not size a hex span"
    <| fun _ ->
      match resolveSpan "map 9 9 {\n  set 1 1 hut spanX=2 spanZ=2\n}" with
      | Ok _ -> failtest "a box must not size a hex span"
      | Error e -> Expect.stringContains e "hex span" "names the shape"

    testCase "a span covers at least one cell"
    <| fun _ ->
      match resolveSpan "map 9 9 {\n  set 1 1 slab spanX=0 spanZ=2\n}" with
      | Ok _ -> failtest "a zero span must fail"
      | Error e -> Expect.stringContains e "at least one cell" "names the rule"

    testCase "span properties belong to set alone"
    <| fun _ ->
      match resolveSpan "map 9 9 {\n  fill grass spanX=2\n}" with
      | Ok _ -> failtest "an area statement takes no span"
      | Error e -> Expect.stringContains e "spanX" "names the argument"

    testCase "a style size must agree with the span"
    <| fun _ ->
      let document width height =
        $"map 9 9 {{\n  element deck {{ set 0 0 slab }}\n  style deck w={width} h={height}\n  deck\n}}"

      match resolveSpan(document 4 2) with
      | Ok _ -> ()
      | Error e -> failtest $"a matching style size must resolve: {e}"

      match resolveSpan(document 2 6) with
      | Ok _ -> failtest "a disagreeing style size must fail"
      | Error e ->
        Expect.stringContains e "'deck'" "names the element"
        Expect.stringContains e "states w= 2" "names the stated size"
        Expect.stringContains e "covers 4 cells across" "names the span"

    testCase "a declared extent must agree with the span"
    <| fun _ ->
      let document width height =
        $"map 9 9 {{\n  element deck w={width} h={height} {{ set 0 0 slab }}\n  deck\n}}"

      match resolveSpan(document 4 2) with
      | Ok _ -> ()
      | Error e -> failtest $"a matching declared size must resolve: {e}"

      match resolveSpan(document 5 2) with
      | Ok _ -> failtest "a disagreeing declared size must fail"
      | Error e ->
        Expect.stringContains e "'deck'" "names the element"
        Expect.stringContains e "declares w= 5" "names the declared size"

    testCase "a game element body cannot fill with a spanning word"
    <| fun _ ->
      let broken = {
        Doc.Name = "broken"
        Doc.Extent = ValueNone
        Doc.Body = [| Doc.Op.Fill(spanCell "slab" (Span(4, 2))) |]
      }

      let bad: Doc.Surface<SpanCell> = {
        spanSurface with
            Doc.Elements = frozen [ "broken", broken ]
      }

      let src = "map 6 4 { broken }"

      match Kdl.parse src with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        match Doc.resolve bad src roots with
        | Ok _ -> failtest "a spanning fill in a game element body must fail"
        | Error e ->
          Expect.stringContains e "'broken'" "names the element"
          Expect.stringContains e "spans 4x2" "names the span"
  ]
