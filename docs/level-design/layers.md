---
title: Layers in Authored Maps
category: Level Design
categoryindex: 8
index: 12
---

# Layers in authored maps

A document can declare layers. A layer is one grid: you author the ground,
the decor, and the traffic in one text file, and the build hands back one
`CellGrid2D<'T>` per layer, bottom first.

What a layer means at draw time — collision, offset, opacity, parallax —
is game data. The framework hands over grids in order.

## The syntax

KDL spells a layer as a node whose first argument is its name:

```kdl
map 40 24 {
    layer ground {
        generate meadow
        generate 0 0 40 3 forest
        generate 0 11 40 1 road
    }

    layer decor {
        plot x=4 y=4 w=9 h=6 pack=scatter seed=3 { repeat 6 { treeRound } }
        plot x=17 y=6 w=9 h=4 pack=scatter seed=7 { crate; crateBeveled }
    }
}
```

XML names the layer with the `name` property and carries the scalars as
attributes:

```xml
<map w="40" h="24">
  <layer name="ground">
    <generate kernel="meadow" />
    <generate x="0" y="0" w="40" h="3" kernel="forest" />
    <generate x="0" y="11" w="40" h="1" kernel="road" />
  </layer>
  <layer name="decor">
    <plot x="4" y="4" w="9" h="6" pack="scatter" seed="3">
      <repeat count="6"><treeRound /></repeat>
      <repeat count="4"><treePine /></repeat>
    </plot>
    <plot x="17" y="6" w="9" h="4" pack="scatter" seed="7">
      <repeat count="3"><crate /></repeat>
      <repeat count="2"><crateBeveled /></repeat>
    </plot>
  </layer>
</map>
```

Both documents build the same layers with the same cells.

## The rules

1. `layer` is legal only as a direct child of `map`. Inside a plot, an
   element, or another layer it fails with its position.
2. A layer node carries exactly one name: a word argument in KDL, the
   `name` property in XML. Any other argument or property fails.
3. Layer names are words and unique in the document. A duplicate fails at
   the position of the second node.
4. A layer with no statements and no children fails. `element` and
   `style` declarations do not count as content — they are leaves the
   collectors read first, so on their own they leave the layer painting
   nothing.
5. Paint written outside a layer — the map's bare statements and its
   non-layer children — forms an implicit bottom layer named `main`,
   wherever in the file that paint sits.
6. Stated layers follow `main` in document order of first appearance.
   Order is draw order.
7. Every layer spans the full map. A layer is not a sub-rectangle: its
   stamp is stretched over the whole grid, so a layer whose statements
   cover one band still paints that band at its own cells.
8. One surface per document: every layer shares the cell type, the words,
   the kernels, and the element library.
9. Inside one layer, paint order resolves conflicts as it does today: the
   body first, children after, later children over earlier ones. Between
   layers nothing overwrites, because each layer paints its own grid.
10. Every element of a layer reports its rectangle under its own name
    through the tag channel, as it does in a document without layers.
    Each layer owns its landmarks registry, so an element name is unique
    per layer and may repeat across layers. The layer container itself
    reports nothing: its rectangle is the whole map, so a region query
    would answer "the whole map" for every cell no element covers. The
    layer reaches the caller through `BuiltLayer.Name` instead.
11. A document without `layer` nodes resolves to exactly one layer,
    `main`, and builds exactly as it did before.
12. A document that resolves to one layer — a single `layer` node with no
    bare paint, or no layer node at all — builds through `DocFlow.build`
    and `DocFlow.buildXml`. Two or more layers fail the build there, and
    name the layers and the plural entry point.

## Building a layer stack

`DocFlow.buildLayers` (KDL) and `DocFlow.buildLayersXml` (XML) parse,
resolve, emit, and paint every layer, in order:

```fsharp
match DocFlow.buildLayers (surface, src) with
| Error e -> printfn "%s" e
| Ok layers ->
    for layer in layers do
        printfn "%s: %dx%d" layer.Name layer.Grid.Width layer.Grid.Height
```

Each entry is a `BuiltLayer<'T>`:

```fsharp
type BuiltLayer<'T> = {
    Name: string
    Grid: CellGrid2D<'T>
    Landmarks: Landmarks
}
```

A document without layers returns one entry named `main`, so one code
path handles both shapes.

`DocFlow.emitLayers` stops before the paint and hands back the layer
stamps, for a caller that owns its own grids — a viewer that keeps the
grids it already allocated, or a probe that compares two syntaxes:

```fsharp
val emitLayers : Doc.Item<'T> -> struct (string * Stamp<'T>)[]
```

In F#, the same stack builds from stamps you wrote yourself. You create
the grids; `Flow.runLayers` paints stamp i into grid i, bottom first:

```fsharp
open Mibo.Layout

let grids =
    [| CellGrid2D.create 40 24 cellSize Vector2.Zero
       CellGrid2D.create 40 24 cellSize Vector2.Zero |]

let layers = grids |> Flow.runLayers [| groundStamp; decorStamp |]

let struct (groundGrid, groundMarks) = layers[0]
let struct (decorGrid, decorMarks) = layers[1]
```

`Flow.runLayers` throws before it paints anything when the two arrays
differ in length, when the grids differ in width or height, or when a
stamp asks for `Expand`. Each layer gets its own landmarks registry, so
an element name may repeat across layers.

`Flow.buildLayers` is `Flow.runLayers` plus `Landmarks.scanTiles` per
layer: every layer derives its own per-cell tag bit grids.

Hex grids work unchanged: create them with `CellGrid2D.createHex` and run
the same stamps. Painting is cell-space, so only world positions and
spatial queries see the geometry.

## Spatial queries across layers

Every blocking decision in `Spatial2D` enters through a predicate you
pass, and the grid argument is read for its bounds only. Layers compose
in the predicate:

```fsharp
let blocked (x: int) (y: int) =
    layers
    |> Array.exists (fun layer ->
        match CellGrid2D.get x y layer.Grid with
        | ValueSome tile -> tile.Blocks
        | ValueNone -> false)

Spatial2D.findPath
    startX
    startY
    goalX
    goalY
    (fun x y -> not (blocked x y))
    (fun _ _ _ _ -> 1f)
    layers[0].Grid
```

When the predicate is read per cell rather than per query — A* and flood
fill visit each cell many times — fold the stack into one occupancy array
at load:

```fsharp
let occupancy = Array.zeroCreate (width * height)

for layer in layers do
    for y in 0 .. height - 1 do
        for x in 0 .. width - 1 do
            match CellGrid2D.get x y layer.Grid with
            | ValueSome tile when tile.Blocks -> occupancy[x + y * width] <- true
            | _ -> ()

// one array read per cell, no layer walk
let passable x y = not occupancy[x + y * width]
```

The fold runs once, at load. A walkability rule that only some layers
state — "decor never blocks", "the top layer is a trigger volume" — is
the predicate, not the storage.

Landmarks compose the same way: `Flow.isTag` and `Flow.tryTagGrid` read
one layer's registry, so a rule expressed as a tag answers per layer, and
`Array.exists` over the layers answers across them.

## Consumption patterns

**Draw one grid per layer.** Keep the `BuiltLayer[]` and issue one
`CellGrid2D.iterVisible` per layer, each at its own `RenderLayer`; the
render buffer's deferred sort puts them in order, and an empty cell in an
upper grid draws nothing, so the layer under it shows through. The Defli
sample is the per-layer drawer: `Defli/Raylib/MapView.fs` walks the
terrain grid, then the road grid, then the decorations, one pass each.

The Platformer sample is the other shape: its view draws the terrain
layer plus the entity rects (`Platformer/Raylib/View.fs`), not every
layer, because its layers differ in kind rather than in depth. Both
shapes consume the same array.

**Fold the stack into one composite cell at load.** When the simulation
wants one value per cell — a platformer's physics tile, a tower-defense
buildability flag — project what the document authored into a single
structure once, and let the game read that. The Defli sample's
`splitLayers` (`Defli/Shared/State/Systems/Map.fs`) projects the authored
grid into its `LayeredMap` at load. The Platformer sample keeps one
`Tile` per cell and states which layer a tile belongs to as a plain
function, `tileLayer` (`Platformer/Shared/Types.fs`).

A block map needs one more step: a cell holds one column, so the topmost
layer that painted it is the column that stands there. The LiveMap sample
lifts instead — every layer above the ground rises by the height the
layers below it reach at that cell, so a decoration stands on the terrain
and the terrain stays whole underneath — and draws the flat map one grid
per layer. `Stack.feet` derives that lift for the whole stack, and
[instances larger than a cell](../3d/spans.html) is the 3D counterpart of
this page: one instance stretched over several cells of a layer, with the
occupancy that answers which instance owns each cell.

Neither pattern is the framework's business. The framework stops at
handing over the grids.

## What layers are not

- **Not a cross-layer read.** An upper layer cannot ask what the lower
  one painted. `Flow.runLayers` owns the paint order, so an upper layer
  can close over painted lower grids later, if a real level needs it.
- **Not one element painting several layers.** One placement paints one
  layer today.
- **Not per-layer surfaces or cell types.** Every layer of a document
  shares the cell type. Two cell types are two documents, composed in
  game code.
- **Not a role, an opacity, or a parallax factor.** Those are draw-time
  game data. A layer is an array position.
