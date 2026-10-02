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
   statement (`fill`, `set`, `generate`, `plot`, ...) means the same
   thing in every game. Cell words, kernels, and element libraries are
   the game's say — a document stays portable at the statement level.

## The node tree is the contract

Every front-end produces the same `Node` tree: a kind, positional args,
named properties, and children. The resolver (`Doc.resolve`) never sees
the text format. The two front-ends differ in one channel: KDL spells
scalars as positional arguments (`map 36 20`), XML spells them as
attributes (`map w="36" h="20"`), and the resolver reads every scalar by
name so both resolve identically:

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
node inside an element fails the parse, so a forgotten statement never
disappears silently:

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
let struct (grid, landmarks) = ...            // the Flow build, next PR

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
per game.

**Paint is data.** A body resolves to `Op` values — `Fill`, `FillRect`,
`Set`, `Border`, `Rect`, `Generate` — interpreted at render time through
the framework's `Layout` ops. The union is closed by design: statements
mean the same thing in every game; games extend through elements and
words, not new cases.

**Styles carry layout only.** `w=`/`h=` size, `x=`/`y=` exact placement,
`hplace=`/`vplace=`/`place=` alignment, `pack=` (stack, flow, scatter),
`pad=`, `gapx=`/`gapy=`, `seed=`, and flow placement (`area=`, `col=`,
`row=`, `colspan=`, `rowspan=`). Declared `cols`/`rows` (ratios,
`fixed n`, `auto`) imply flow packing.

*The Flow emitter (`DocFlow`) lands with the next PR of this stack —
this page grows with it.*
