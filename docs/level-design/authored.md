---
title: Authored Maps
category: Level Design
categoryindex: 8
index: 4
---

# Authored Maps

Mibo.Markup adds a text front-end to Flow: level documents you can edit while the game runs, keep in version control, and hand to a level designer who does not read F#. A document parses and lays out once. The game never sees the text.

Two rules:

1. **Content and layout are separate.** A node's body paints its own box in local coordinates. Layout lives in node properties (`x=`, `y=`, `w=`, `h=`, `pack=`, ...). Layout edits touch properties only.
2. **The engine owns statements; the game owns vocabulary.** Every statement (`fill`, `set`, `generate`, ...) means the same in every game. Cell words, kernels, and elements are the game's.

## The node tree

Every front-end produces the same `Node` tree: a kind, positional args, named properties, and children. `Doc.resolve` reads the tree, not the text. KDL spells scalars as positional arguments; XML spells them as attributes.

A document holds exactly one map. A stray root node or a second map fails the build. Layers split the map; see [Layers](layers.html). The map takes its dimensions from either channel, or one of each.

```fsharp
open Mibo.Markup

match Kdl.parse src with
| Error e -> printfn "%s" e          // "unknown element 'plaza' (12:5)"
| Ok roots -> ...
```

KDL documents carry node positions, so errors point at the authoring line. XML does not track positions; XML resolution errors name the element, and XML parse errors carry the parser's line and position.

## XML

Elements are nodes and children. Attributes are the scalar channel. Comments and whitespace are free.

```xml
<map w="36" h="20">
  <!-- the meadow -->
  <field><fill cell="grass" /></field>
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13">
    <boulder /><boulder /><boulder />
  </plot>
</map>
```

An attribute value types as int, then finite float, then word. `x="1"` is a number, `cell="grass"` is a word, `f="NaN"` stays a word. An `element` definition names itself with the `name` attribute. A non-whitespace text node fails the parse, so a forgotten statement never disappears.

## KDL

`Kdl.parse` reads KDL 2.0. Bare values are positional args. `name=value` pairs are properties. `/-` comments out a whole node.

```kdl
map 36 20 {
    field { fill grass }
    plot x=1 y=1 w=5 h=5 pack=scatter seed=13 { boulder; boulder; boulder }
}
```

`#null`, `#inf`, and `#nan` fail the parse. `/-` works before whole nodes only. An integer past the int32 range becomes a decimal in both front-ends. KDL typed literals (hex, underscores, quoted numbers) have no XML equivalent.

Both front-ends resolve the same document to the same level. The tests build one document in both syntaxes and compare the grids cell for cell.

## The resolver

`Doc.resolve` turns a `Node` tree into an `Item` tree. Templates expand. Words resolve against the game's surface. Layout properties merge through the style cascade: solver defaults, then document `style name` rules in order, then inline properties.

```fsharp
match Kdl.parse src with
| Error e -> printfn "%s" e                   // parse errors, positioned
| Ok roots ->
    match Doc.resolve surface src roots with
    | Error e -> printfn "%s" e               // resolution errors, positioned
    | Ok items -> ...                         // the Item tree, ready to emit
```

The surface holds the game's words, kernels, and elements. Build it once. A word names one cell value. A kernel is a per-cell rule, used by `generate`. An element is a named body of statements with an optional size, declared in F# or as an `element` template in the document. Two container kinds exist beside the game's elements: `plot` (a plain container) and `grid` (a container with the `cols`/`rows`/`areas` template).

The surface is a record, and every field is required. A complete declaration for a game whose cells carry a span:

```fsharp
open System.Collections.Frozen
open System.Collections.Generic
open Mibo.Layout
open Mibo.Markup

// The game's cell type. The span and the height are game data.
type Tile = {
  Kind: TileKind
  Span: InstanceSpan
  Height: float32
}

let frozen (pairs: (string * 'T)[]) =
  let table = Dictionary<string, 'T>()

  for name, value in pairs do
    table[name] <- value

  table.ToFrozenDictionary()

// The game's vocabulary. A word names one cell value.
let tile kind = { Kind = kind; Span = One; Height = 1f }

let surface: Doc.Surface<Tile> = {
  Words =
    frozen [|
      "grass", tile Grass
      "wall", { tile Wall with Height = 2f }
      "slab", { tile Slab with Height = 0.2f; Span = Span(4, 2) }
    |]
  Kernels =
    frozen [|
      "field", Doc.Gen2(fun x y -> if (x + y) % 7 = 0 then tile Tree else tile Grass)
    |]
  Elements =
    frozen [|
      "hut", {
        Name = "hut"
        Extent = ValueSome { W = 3; H = 3 }
        Body = [|
          Doc.Op.Fill(tile Grass)
          Doc.Op.Border(ValueSome { X = 0; Y = 0; W = 3; H = 3 }, tile Wall)
          Doc.Op.Set(ValueSome { X = 1; Y = 1 }, Start, Start, tile Chest)
        |]
      }
    |]
  // Required fields. ValueNone means "every cell covers one cell" and
  // "a statement cannot size one".
  Span = ValueSome(fun cell -> cell.Span)
  WithSpan = ValueSome(fun cell span -> { cell with Span = span })
}
```

`Span` and `WithSpan` are required by the record. `ValueNone` on both keeps the map as it was before spans existed.

With both fields, `set` sizes one instance: `set 3 9 slab spanX=16 spanZ=6` in KDL, `spanX="16" spanZ="6"` in XML. `fill`, `fillRect`, `border`, and `rect` refuse a spanning word. The build reports each layer's occupancy, so a query answers with the instance that owns a cell. See [Instances and Occupancy](instances.html) and [3D from 2D](three-d.html).

Paint is data. A body resolves to `Op` values: `Fill`, `FillRect`, `Set`, `Border`, `Rect`, `Generate`. The interpreter runs them at render time through the `Layout` ops. The union is closed. Games extend through elements and words, not new cases.

A `style` rule names itself with a word argument in KDL (`style thicket w=6 h=5`) and with the `name` property in XML (`<style name="thicket" w="6" h="5" />`). The name is the rule's key, not a style.

Styles carry layout only. `w=`/`h=` size. `x=`/`y=` place (stack pack only; a flow or scatter child with `x=`/`y=` fails the build, and negative values clamp to zero). `hplace=`/`vplace=`/`place=` align. `pack=` is stack, flow, or scatter. `pad=` clamps at zero. `gapx=`/`gapy=` must match; the Flow grid takes one gap. `seed=` sets a seed. Flow placement takes `area=`, `col=`, `row=`, `colspan=`, and `rowspan=`; col, row, and spans below one fail. Declared `cols`/`rows` (ratios, `fixed n`, `auto`) imply flow packing.

## The emitter

`DocFlow.build` (KDL) and `DocFlow.buildXml` (XML) run the whole pipeline: parse, resolve, emit to Flow stamps, one `Flow.run`. Every layout channel uses the framework's primitives: exact placement is `Flow.at`, stack alignment is `Dock` flags, flow packing is `Flow.grid` with areas and slots, `auto` tracks size from the children's footprints, and scatter is `Flow.scatter`.

```fsharp
match DocFlow.build (surface, src) with        // or DocFlow.buildXml
| Ok grid -> ...          // a CellGrid2D<Tile>, painted and query-ready
| Error e -> printfn "%s" e
```

A document that declares layers builds through `DocFlow.buildLayers`/`DocFlow.buildLayersXml`, which return one grid per layer. `build` and `buildXml` keep their signatures: a document that resolves to two or more layers fails and names them. See [Layers](layers.html).

Parse and resolution errors carry document positions (KDL). Emitter failures name the container, the child, and the channel (a gap mismatch, a bad slot, an unknown area, a mixed pack).

## Landmarks

Every element reports its rectangle through the tag channel, under its own name. `plaza` twice is two rectangles. The anonymous `plot` container reports under `plot`, so a document without named elements still answers what painted a cell. Nothing reports under `map`; its rectangle is the whole grid.

`DocFlow.build` returns the grid alone. To keep the landmarks, run the steps yourself.

```fsharp
// Parse, resolve, and paint a document, keeping both halves of what
// `Flow.run` returns: the grid, and the landmarks it recorded beside it.
let buildWithLandmarks (src: string) =
    Kdl.parse src                                        // or Xml.parse
    |> Result.bind (fun roots ->
        Doc.resolve surface src roots
        |> Result.bind (fun items ->
            // the map node states the size; the resolved root is what the
            // emitter lays out
            Doc.findMapNode roots
            |> ValueOption.map (fun node ->
                Doc.dimsOf(src, node) |> Result.map (fun dims -> items, dims))
            |> ValueOption.defaultValue (Error "the document holds no map node")))
    |> Result.bind (fun (items, dims) ->
        match items with
        | [| root |] -> Ok struct (root, dims)
        | many -> Error $"the document resolved to {many.Length} roots")
    |> Result.map (fun struct (root, dims) ->
        let grid =
            CellGrid2D.create dims.W dims.H (Vector2(32f, 32f)) Vector2.Zero

        grid |> Flow.run (DocFlow.emit root))
```

```fsharp
match buildWithLandmarks src with
| Ok struct (_, marks) ->
    Flow.taggedRects "plaza" marks        // every plaza, newest first
    Flow.isTag "plaza" { X = 3; Y = 4 } marks
| Error e -> printfn "%s" e
```

Element names ride the tag channel because a document may use one name many times. The registry allocates one bit grid per name per build. A document with thousands of elements is a memory decision, not a correctness one.

## Live reload

Watch the document, debounce about 250 ms, re-run `DocFlow.build`, and swap the level on success. On failure, show the error and keep the last good level. Error builds nothing, so a broken document never half-paints a running game.
