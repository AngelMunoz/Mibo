---
title: Instances and Occupancy
category: Level Design
categoryindex: 8
index: 6
---

# Instances and occupancy

A cell holds one value, and one value usually draws one instance. A 3D map
often wants the opposite trade: author a model once, then stretch it over the
cells it stands for — one ground plate instead of 640 columns, a 4x4 deck, a
2x8 walkway.

An **instance span** states how many cells one instance covers. The build
derives an **occupancy** from the painted grid, and every consumer — a hover, a
collision test, a spawn query, a draw — reads the cell through it.

```fsharp
/// How many cells one drawn instance covers.
[<Struct>]
type InstanceSpan =
  | Span of across: int * deep: int   // square grids: a rectangle of cells
  | Radius of r: int                  // hex grids: a disc of hex steps
  | One                               // the identity: this cell only
```

`Span(1, 1)` and `Radius 0` are also the identity, so a span a game computes
needs no special case.

## The four numbers

Keep these apart. Most confusion in this area comes from mixing them.

| Number | Means | Lives in |
| --- | --- | --- |
| `spanX` `spanZ` | How many cells one instance covers in the grid plane | The cell value, and optionally the statement |
| `Height` | How tall the column stands, in cells | The cell value, set by the word |
| `w=` `h=` | How many cells an element paints | The element box |
| `SizeX` `SizeY` `SizeZ` | The mesh as authored | The model catalog |

There is no `spanY`. The grid is two-dimensional: no cell sits above another,
so a taller column owns nothing. `spanY` would only stretch the mesh, which is
what `Height` already does.

## A word states a span

The word carries the value, so a span on a word needs no document change:

```fsharp
let slab = {
  Model = model "platform"
  Height = 0.2f
  Lift = 0f
  Span = One          // the document sizes it, or the word states one
  Solid = false
}
```

The surface tells the resolver how to read it, and how to write one a
statement states:

```fsharp
let surface: Doc.Surface<BlockCell> = {
  Words = words
  Kernels = kernels
  Elements = elements
  Span = ValueSome(fun cell -> cell.Span)
  WithSpan = ValueSome(fun cell span -> { cell with Span = span })
}
```

`Span` and `WithSpan` are required by the `Surface` record. `ValueNone` on
both keeps the map exactly as it was before spans existed: every cell covers
one cell.

## A statement states a span

`set` sizes one instance with `spanX` and `spanZ`. They come as a pair, and no
other statement takes them.

```kdl
layer ground {
    fill grass
    set 3 9 slab spanX=16 spanZ=6
}
```

```xml
<layer name="ground">
  <fill cell="grass" />
  <set x="3" y="9" cell="slab" spanX="16" spanZ="6" />
</layer>
```

A word states the default span and a placement overrides it, so one `slab`
serves a 16x6 plaza, a 4x4 deck, and a 2x8 walkway. A stated box does not size
a word whose default is a `Radius`: a hex span takes a radius, not a box.

## The occupancy

`Occupancy.scan` expands every populated cell through the projection, checks
the result, and answers who owns what:

```fsharp
type Occupancy = {
  Width: int
  Height: int
  Cells: CellPoint[]      // anchor index -> the anchor's cell
  Rects: CellRect[]       // anchor index -> the rectangle it covers
  Owner: int[]            // cell -> anchor index + 1, or 0
  Claimed: int            // populated cells a span hides
}
```

A span **claims** the cells it covers: a plain cell under a plate stops drawing
on its own, and `Claimed` counts it. That is the point of the feature — a plate
replaces the ground it covers instead of standing beside it.

Four queries serve every consumer:

```fsharp
Occupancy.owner x y occupancy      // the anchor that owns a cell, ValueNone when empty
Occupancy.rectOf at occupancy      // the rectangle that anchor covers
Occupancy.iterInWindow l t r b f occ   // every anchor whose rectangle meets a window
Occupancy.identity grid            // an occupancy where each populated cell owns itself
```

Layers stack, so a plate in one layer and a decoration in the layer above are
legal. Two spans that meet in the *same* layer are not: the build fails and
names both cells.

## The rules, and what fails

1. `Span` needs both sides at least one; `Radius` needs `r >= 0`. Otherwise:
   `the span at (x,y) spans nothing`.
2. `Span` and `Radius` must match the grid geometry, and `One` is legal on
   both. Otherwise: `Span spans need a square grid; (x,y) is hex`, and the
   twin.
3. Only `set` places a spanning word. An area statement paints every cell of
   its box, so one instance has no single place to stand in it:
   `the word 'slab' spans 6x4, so only set may place it`.
4. A span stays inside the grid: `the span at (x,y) covers past the grid
   edge`.
5. Two spans do not overlap: `the span at (x,y) overlaps the span at (ax,ay)`.
6. An element's extent follows the span it places. A style `w=`/`h=` or a
   declared `element ... w= h=` that disagrees fails naming the element and
   both sizes.

## Where the numbers come from

The rectangle an anchor covers is computed from the span, checked against the
grid, and then expanded — never read back from the walk that expands it, since
the hex range walk clips to the grid.

- A square anchor grows toward +X and +Z from its cell:
  `Span(across, deep)` at `(x, y)` covers `(x, y, across, deep)`.
- A hex anchor is centred on its cell, and its rectangle is the offset-space
  bounding box `(x - r, y - r, 2r + 1, 2r + 1)` — the same convention hex
  landmarks use.

## Drawing one instance over many cells

Rendering reads the occupancy too and hands the transform the rectangle each
instance covers. [3D from 2D](three-d.html) builds the context and the
transform; the draw calls are:

```fsharp
context.RenderInstanced(buffer, grid, occupancy)
context.RenderWindowInstanced(buffer, left, top, right, bottom, grid, occupancy)
```

The whole-map form draws one instance per anchor. The windowed form converts
the world-space window to a cell range and enumerates anchors by rectangle,
so an instance stays drawn while any cell of its rectangle is in view.

[3D from 2D](three-d.html) walks a whole map from an empty project to a
drawn, queryable stack.
