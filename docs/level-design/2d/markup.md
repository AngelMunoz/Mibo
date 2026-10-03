---
title: Markup - Authored Level Documents
category: Level Design
categoryindex: 8
index: 11
---

# Markup: authored level documents

Flow authors levels as F# values. **Mibo.Markup** adds a text front-end
to the same model: level documents you can edit while the game runs,
keep in version control, and hand to a level designer who does not read
F#. Build-time only — a document parses and lays out once, through the
same `Flow.run` your code uses, and the game never sees the text again.

Two rules keep the format honest:

1. **Content and layout are two concerns.** A node's body paints its own
   box in local coordinates; layout lives in node properties (`x=`, `y=`,
   `w=`, `h=`, `pack=`, ...). Layout edits touch properties only.
2. **The engine owns statements; the game owns vocabulary.** Every
   statement (`fill`, `set`, `generate`, ...) means the same thing in
   every game. Cell words, kernels, and element libraries are the
   game's say — a document stays portable at the statement level.

## The node tree is the contract

Every front-end produces the same `Node` tree: a kind, positional args,
named properties, and children. The resolver (`Doc.resolve`) never sees
the text format. The two front-ends differ in one channel: KDL spells
scalars as positional arguments (`map 36 20`), XML spells them as
attributes (`map w="36" h="20"`), and the resolver reads every scalar by
name so both resolve identically. A document holds exactly one map:
a stray root node or a second map fails the build instead of being
dropped. The map's contents can be split into layers, one grid each —
see [Layers in authored maps](layers.html). The map takes its two
dimensions from either channel, or one of each — `map 36 20`,
`map w="36" h="20"`, and `map 36 h="20"` all read 36 across and 20 down:

```fsharp
open Mibo.Markup

match Kdl.parse src with
| Error e -> printfn "%s" e          // "unknown element 'plaza' (12:5)"
| Ok roots -> ...
```

KDL documents carry node positions, so resolution errors point at the
authoring line. XML does not track node positions (the BCL line-info
surface is not reachable from F# without fragile reference tricks), so
XML resolution errors name the element; XML parse errors carry the
parser's own line and position.

## The XML front-end

XML the way XML means it: elements are nodes and children, attributes
are the scalar channel. Comments and whitespace are free, and the BCL
parser does all the parsing. Text is not markup: a non-whitespace text
node inside an element fails the parse — CDATA counts as text and fails
the same way — so a forgotten statement never disappears silently:

```xml
<map w="36" h="20">
  <!-- the meadow -->
  <field><fill cell="grass" /></field>
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13">
    <boulder /><boulder /><boulder />
  </plot>
</map>
```

An attribute value types as int, then finite float, then word (`x="1"`
is a number, `cell="grass"` is a word, `f="NaN"` stays a word — the
resolver never computes with non-finite floats). An `element`
definition names itself with the `name` attribute.

## The KDL front-end

`Kdl.parse` reads KDL 2.0 (via KdlSharp — the package's only external
dependency, confined to this one file). Bare values are positional
args, `name=value` pairs are properties, `/-` comments out a whole
node. Two restrictions the parser enforces: `#null`, `#inf` and `#nan`
values fail the parse, and `/-` works before whole nodes only. Node
positions come from the reader's own line and column — no text
scanning — so resolution errors point at the authoring line even when
a property spells a later node's kind or a comment spells a kind. An
integer past the int32 range becomes a decimal in both front-ends:

```kdl
map 36 20 {
    field { fill grass }
    plot x=1 y=1 w=5 h=5 pack=scatter seed=13 { boulder; boulder; boulder }
}
```

Both front-ends resolve the same document to the same level — the
resolver reads every scalar by name, so KDL's positional args and XML's
attributes land identically, and the emitter's tests build the same
document in both syntaxes and compare the grids cell for cell. KDL's
typed literals (hex, underscores, quoted numbers) have no XML
equivalent; XML attributes type by content. Pick the syntax your team
prefers; a document can migrate between them without touching the game.

## The resolver

`Doc.resolve` turns a `Node` tree into an `Item` tree: templates expand,
words resolve against the game's surface, and layout properties merge
through the style cascade — solver defaults, then document `style name`
rules in order, then inline properties:

```fsharp
let surface: Doc.Surface<Tile> = ...          // words, kernels, elements

match Kdl.parse src with
| Error e -> printfn "%s" e                   // parse errors, positioned
| Ok roots ->
    match Doc.resolve surface src roots with
    | Error e -> printfn "%s" e               // resolution errors, positioned
    | Ok items -> ...                         // the Item tree, ready to emit
```

**The surface is the game's whole say.** Cell *words* (`fill grass`),
per-cell *kernels* (`generate forest`), and the game's *element library*
(`grove`, declared once in F# or as an `element` template in the
document) — three frozen tables, built once at startup, read per build.
A document stays portable at the statement level; only the words differ
per game. Two container kinds exist beside the game's own elements:
`plot` (a plain container) and `grid` (a container that carries the
`cols`/`rows`/`areas` template).

**Paint is data.** A body resolves to `Op` values — `Fill`, `FillRect`,
`Set`, `Border`, `Rect`, `Generate` — interpreted at render time through
the framework's `Layout` ops. The union is closed by design: statements
mean the same thing in every game; games extend through elements and
words, not new cases.

A `style` rule names itself with a word argument in KDL (`style thicket w=6 h=5`) and with the `name` property in XML (`<style name="thicket" w="6" h="5" />`). The name is the rule's key, not a style: it is read first and never applied as a property.

**Styles carry layout only.** `w=`/`h=` size, `x=`/`y=` exact placement
(stack pack only — a flow or scatter child with `x=`/`y=` fails the
build instead of silently dropping the offsets, and negative values
clamp to zero, the framework's own `Flow.at` rule), `hplace=`/`vplace=`/
`place=` alignment, `pack=` (stack, flow, scatter), `pad=` (negative
values clamp to zero), `gapx=`/`gapy=` (the Flow grid takes one gap
today, so the two must match), `seed=`, and flow placement (`area=`,
`col=`, `row=`, `colspan=`, `rowspan=` — col, row, and spans below one
fail the build). Declared `cols`/`rows` (ratios, `fixed n`, `auto`)
imply flow packing at emit time — the emitter derives the pack from the
declared tracks.

## The emitter

`DocFlow.build` (KDL) and `DocFlow.buildXml` (XML) are the whole
pipeline in one call: parse, resolve, emit to Flow stamps, one
`Flow.run`. Every layout channel rides the framework's own primitives —
exact placement is `Flow.at`, stack alignment is `Dock` flags, flow
packing is `Flow.grid` with named areas and explicit slots, `auto`
tracks size from the children's footprints inside the grid, and scatter
is `Flow.scatter`'s seeded rule:

```fsharp
match DocFlow.build (surface, src) with        // or DocFlow.buildXml
| Ok grid -> ...          // a CellGrid2D<Tile>, painted and query-ready
| Error e -> printfn "%s" e
```

A document that declares layers builds through
`DocFlow.buildLayers`/`DocFlow.buildLayersXml`, which return one grid per
layer. `build` and `buildXml` keep their signatures: a document that
resolves to two or more layers fails naming them, instead of silently
painting one. See [Layers in authored maps](layers.html).

The golden tests hand-lay each layout channel with the raw `Layout` ops
and compare cell for cell — the emitter is checked against the
framework's own painting, not against itself — and the same document
built in KDL and in XML produces the identical grid. Parse and
resolution errors carry their document positions (KDL); emitter-stage
failures name the container, the offending child, and the channel (a
gap mismatch, a bad slot, an unknown area, a mixed pack).

## Landmarks: the document's structure

Every element of the document reports its resolved rectangle through the
tag channel, under its own name — `plaza` twice is two rectangles, one
per use. The anonymous `plot` container reports under `plot`, so a
document written without named elements still answers "what painted this
cell". Nothing reports under `map`: its rectangle is the whole grid.

`DocFlow.build` returns the grid alone. A caller that needs the
structure — a hover that names the region under the cursor, a walkability
walk over a tagged area, spawn points derived from the document — runs
the same steps itself and keeps the landmarks. Each step already returns
a `Result` or a `ValueOption`, so the pipeline binds instead of nesting:

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

The landmarks are what the queries read:

```fsharp
match buildWithLandmarks src with
| Ok struct (_, marks) ->
    Flow.taggedRects "plaza" marks        // every plaza, newest first
    Flow.isTag "plaza" { X = 3; Y = 4 } marks
| Error e -> printfn "%s" e
```

Element names ride the tag channel rather than the named-stamp channel
because one document may use the same element name many times, and the
named channel requires unique names. Each name carries a per-cell bit
grid, so a hover or a walk query is one dictionary lookup and one array
read.
The registry allocates one bit grid per name per build — build-time
memory, released with the build — so a document with thousands of
elements is a memory decision, not a correctness one.

## Live reload

The point of authored text is editing while the game runs. The loop is
yours to own (watching APIs differ per platform), and it is small: watch
the document, debounce ~250 ms so the editor's several save events and
mid-write locks settle, re-run `DocFlow.build`, and swap the level on
success — on failure, show the error and keep the last good
level. Error builds nothing, so a broken document never half-paints a
running game.
