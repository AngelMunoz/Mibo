---
title: Authored Maps
category: Level Design
categoryindex: 8
index: 5
---

# Authored Maps

Mibo.Markup adds a text front-end to Flow: level documents you can edit while the game runs, keep in version control, and hand to a level designer who does not read F#. A document parses and lays out once. The game never sees the text.

Two rules hold:

1. **Content and layout are separate.** A node's body paints its own box in local coordinates. Layout lives in node properties (`x=`, `w=`, `pack=`, ...).
2. **The engine owns statements; the game owns vocabulary.** `fill`, `set`, and `generate` mean the same in every game. The words, kernels, and elements are yours.

## How a document connects to the game

The **surface** is the game's lookup tables. A document names a cell value, a rule, or an element; the resolver reads the name from the surface.

```fsharp
// The game defines the names once.
let words = frozen [ "grass", Grass; "wall", Wall ]

let kernels =
  frozen [ "field", Doc.Gen2(fun x y -> if (x + y) % 5 = 0 then Wall else Grass) ]

let elements =
  frozen [|
    "hut", {
      Name = "hut"
      Extent = ValueSome { W = 3; H = 2 }
      Body = [| Doc.Op.Border(ValueNone, Wall) |]
    }
  |]

let surface: Doc.Surface<Tile> = {
  Words = words
  Kernels = kernels
  Elements = elements
  Span = ValueNone
  WithSpan = ValueNone
}
```

The document then uses those names:

```kdl
map 10 8 {
    fill grass             // Words["grass"]
    generate field         // Kernels["field"]
    hut x=5 y=2            // Elements["hut"], 3x2, placed at (5, 2)
    plot x=1 y=5 w=4 h=2 { fill wall }
}
```

An unknown name fails the build with its position. A kernel name that is not in `Kernels`, a word not in `Words`, and an element not in `Elements` or declared in the document all fail the same way.

This example is one valid shape, not a required one. The framework reads three tables and two span projections. The cell type can be a union, a record, or a struct. The `frozen` helper, the `Tile` cases, and the element bodies are the game's.

## Nodes

Every front-end produces the same `Node` tree: a kind, positional args, named properties, and children. The resolver reads the tree, not the text.

| Node | Form | Meaning |
|---|---|---|
| `map` | `map 36 20 { ... }`, `map w=36 h=20` | The document root. One per document. Holds layers, declarations, statements, and child containers. Statements outside a layer form the `main` layer. |
| `layer` | `layer ground { ... }` | One grid. Legal only under `map`. See [Layers](layers.html). |
| `plot` | `plot x=1 y=1 w=5 h=5 { ... }` | An inline container. `w`/`h` size it. `x`/`y` place it in stack pack. Omit `w`/`h` to stretch. |
| `grid` | `grid { cols fixed 4 auto; rows fixed 3 }` | The same inline container under a name that reads well with tracks. `plot` and `grid` resolve the same. |
| element usage | `hut x=5 y=2`, `hut { fill grass }` | A surface element or a declared template. Use-site properties and use-site statements merge with the declaration. |
| `element` | `element hut w=3 h=2 { ... }` | A template declaration. `w=` and `h=` state the intrinsic size and are legal together only. |
| `style` | `style hut w=4 h=3 pack=flow` | A style rule keyed by node name. Later rules win per property. |
| `repeat` | `repeat 2 { hut }`, `<repeat count="2">` | Duplicates its children. Nested repeats multiply. The expansion caps at 100000 nodes. |
| `cols` | `cols fixed 1 auto 2` | Track sizes for a container. A bare number is a weight, `fixed n` is a fixed size, `auto` sizes to the children. |
| `rows` | `rows fixed 3 auto` | The same tokens for rows. |
| `areas` | `areas { r road woods; r road lake }` | Named areas for a container. Each row node carries the names in order. |

## Statements

A statement paints the node's box. Geometry is box-local.

| Statement | Form | Paints |
|---|---|---|
| `fill` | `fill grass` | Every cell of the box. |
| `fillRect` | `fillRect 1 1 4 2 grass` | A local rectangle. |
| `set` | `set 3 9 slab spanX=16 spanZ=6` | One cell, or one spanning instance. `spanX`/`spanZ` come as a pair. |
| `set` aligned | `set hplace=center vplace=end grass` | One cell placed by alignment. |
| `border` | `border wall`, `border 1 1 4 2 wall` | The outline of the box, or of the rectangle. |
| `rect` | `rect wall floor` | The outline (`edge`) plus the fill (`floor`). |
| `generate` | `generate field`, `generate 0 0 8 4 field` | A kernel over the box, or over the rectangle. |

KDL spells arguments positionally. XML spells the same arguments as attributes: `<fillRect x="1" y="1" w="4" h="2" cell="grass" />`. Named arguments work in both: `fillRect x=1 y=1 w=4 h=2 cell=grass`.

## Properties

Properties merge through the style cascade: solver defaults, then document `style` rules in order, then inline properties.

| Property | Value | Effect |
|---|---|---|
| `w`, `h` | number | The box size in cells. |
| `x`, `y` | number | Exact cell placement. Stack pack only. Negative values clamp to zero. |
| `area` | word | Mount in a named area of the parent's template. |
| `col`, `row` | number | Mount in an explicit track slot. |
| `colspan`, `rowspan` | number | Track span for a slot. Need `col=` or `row=`. |
| `hplace`, `vplace`, `place` | `start`, `center`, `end`, `stretch` | Alignment inside the assigned box. `place` sets both axes. |
| `pack` | `stack`, `flow`, `scatter` | How the node places its children. Default: `stack`. |
| `pad` | number | Padding inside the node's box. Negative values clamp to zero. |
| `gapx`, `gapy` | number | Gap between tracks. The Flow grid takes one gap, so the two must match. |
| `seed` | number | Seed for scatter placement. |

Rules:

- `w=`/`h=` and `x=`/`y=` together size and place a box: `plot x=1 y=1 w=5 h=5`.
- A flow or scatter child cannot use `x=`/`y=`. The build fails instead of dropping the offsets.
- An area child fills its whole area. `colspan=`/`rowspan=` need `col=` or `row=`.
- Declared `cols`/`rows` imply flow packing at emit time.

## XML and KDL

`Xml.parse` reads elements as nodes and attributes as the scalar channel.

```xml
<map w="36" h="20">
  <!-- the meadow -->
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13">
    <boulder /><boulder /><boulder />
  </plot>
  <grid>
    <cols v="fixed 4 auto" />
    <rows v="fixed 3" />
    <plot x="0" y="0" w="4" h="3">
      <fill cell="grass" />
      <border cell="wall" />
    </plot>
  </grid>
</map>
```

`Kdl.parse` reads KDL 2.0. Bare values are positional args, `name=value` pairs are properties, and `/-` comments out a whole node.

```kdl
map 36 20 {
    plot x=1 y=1 w=5 h=5 pack=scatter seed=13 { boulder; boulder; boulder }

    grid {
        cols fixed 4 auto
        rows fixed 3
        plot x=0 y=0 w=4 h=3 { fill grass; border wall }
    }
}
```

Both front-ends resolve the same document to the same level. The tests build one document in both syntaxes and compare the grids cell for cell.

Facts that differ:

- KDL documents carry node positions. Errors point at the line and column.
- XML does not track positions. Resolution errors name the element. XML parse errors carry the parser's line and position.
- An attribute value types as int, then finite float, then word. `x="1"` is a number, `cell="grass"` is a word, `f="NaN"` stays a word.
- A non-whitespace text node fails the XML parse, so a forgotten statement never disappears.
- `#null`, `#inf`, and `#nan` fail the KDL parse. `/-` works before whole nodes only.
- An integer past the int32 range becomes a decimal in both front-ends.

## Declarations

`element` declares a template once and uses it many times. The declaration body runs first, then the use-site body:

```kdl
element frame { rect edge=stone floor=grass }

frame w=8 h=4 x=3 y=3 {
    plot x=1 y=1 w=1 h=1 { fill sand }
}
```

`element` also takes an intrinsic size: `element thicket w=2 h=2 { fill dirt }`. The declared `w=`/`h=` must agree with the span the body places. A style size that disagrees fails with the element name and both sizes.

A template name may not collide with a surface element.

`style` declares a reusable rule by node name:

```kdl
element plaza { fill dirt }
style plaza w=4 h=2 pack=flow
plaza x=1 y=2 h=3 { fill grass }
```

The rule applies to every `plaza`. Inline properties win over the rule.

## The surface

A fuller surface for a game whose cells carry a span:

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

`Words` maps a string to one cell value. `Kernels` maps a string to a per-cell function, used by `generate`. `Elements` maps a string to a body and an optional size. `Span` reads how many cells one instance covers. `WithSpan` writes a span a statement states.

`Span` and `WithSpan` are required by the record. `ValueNone` on both keeps the map as it was before spans existed.

With both fields, `set` sizes one instance: `set 3 9 slab spanX=16 spanZ=6` in KDL, `spanX="16" spanZ="6"` in XML. `fill`, `fillRect`, `border`, and `rect` refuse a spanning word. The build reports each layer's occupancy, so a query answers with the instance that owns a cell. See [Instances and Occupancy](instances.html).

Paint is data. A body resolves to `Op` values: `Fill`, `FillRect`, `Set`, `Border`, `Rect`, `Generate`. The interpreter runs them at render time through the `Layout` ops. The union is closed.

## The emitter

`DocFlow.build` (KDL) and `DocFlow.buildXml` (XML) run the whole pipeline and return one grid:

```fsharp
match DocFlow.build (surface, src) with
| Ok grid -> ...          // a CellGrid2D<Tile>, painted and query-ready
| Error e -> printfn "%s" e
```

A layered document builds through `DocFlow.buildLayers` or `DocFlow.buildLayersXml`, which return one `BuiltLayer` per layer. `build` and `buildXml` keep their signatures: a document that resolves to two or more layers fails and names them.

Emitter failures name the container, the child, and the channel: a gap mismatch, a bad slot, an unknown area, a mixed pack.

## Keep the landmarks

`DocFlow.build` returns the grid alone. To keep the landmarks, resolve and paint the document yourself. Each step is a small function.

```fsharp
open System.Collections.Immutable

// Parse the text and resolve it against the game surface.
let parseAndResolve
  (parse: string -> Result<ImmutableArray<Node>, string>)
  (src: string)
  =
  parse src
  |> Result.bind (fun roots ->
    Doc.resolve surface src roots
    |> Result.map (fun items -> struct (roots, items)))

// The map node states the grid size.
let mapSize (src: string) (roots: ImmutableArray<Node>) =
  match Doc.findMapNode roots with
  | ValueSome node -> Doc.dimsOf(src, node)
  | ValueNone -> Error "the document holds no map node"

// A document resolves to exactly one root item.
let onlyRoot (items: Doc.Item<Tile>[]) =
  match items with
  | [| root |] -> Ok root
  | many -> Error $"the document resolved to {many.Length} roots"

// Paint the root onto a grid, keeping the landmarks.
let paint (size: CellSize) (root: Doc.Item<Tile>) =
  let grid = CellGrid2D.create size.W size.H cellSize Vector2.Zero
  grid |> Flow.run (DocFlow.emit root)

// KDL: buildWithLandmarks Kdl.parse src
// XML: buildWithLandmarks Xml.parse src
let buildWithLandmarks
  (parse: string -> Result<ImmutableArray<Node>, string>)
  (src: string)
  =
  parseAndResolve parse src
  |> Result.bind (fun struct (roots, items) ->
    mapSize src roots
    |> Result.bind (fun size -> onlyRoot items |> Result.map (paint size)))
```

The landmarks answer the queries:

```fsharp
match buildWithLandmarks Kdl.parse src with
| Ok struct (_, marks) ->
  Flow.taggedRects "plaza" marks        // every plaza, newest first
  Flow.isTag "plaza" { X = 3; Y = 4 } marks
| Error e -> printfn "%s" e
```

Every element reports its rectangle through the tag channel, under its own name. `plaza` twice is two rectangles. The anonymous `plot` container reports under `plot`, so a document without named elements still answers what painted a cell. Nothing reports under `map`; its rectangle is the whole grid.

Element names ride the tag channel because a document may use one name many times. The registry allocates one bit grid per name per build. A document with thousands of elements is a memory decision, not a correctness one.

## Live reload

Watch the document, debounce about 250 ms, re-run `DocFlow.build`, and swap the level on success. On failure, show the error and keep the last good level. Error builds nothing, so a broken document never half-paints a running game.
