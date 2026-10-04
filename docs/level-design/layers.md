---
title: Layers
category: Level Design
categoryindex: 8
index: 5
---

# Layers

A layer is one grid. Author the ground, the decor, and the traffic in one document, or build a stamp array in F#. The build returns one `CellGrid2D<'T>` per layer, bottom first.

What a layer means at draw time (collision, offset, opacity, parallax) is game data. The framework returns grids in order.

## Syntax

KDL: a layer is a node whose first argument is its name.

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

XML names the layer with the `name` property and carries the scalars as attributes.

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

## Rules

1. `layer` is legal only as a direct child of `map`. Elsewhere it fails with its position.
2. A layer carries exactly one name: a word argument in KDL, the `name` property in XML. Any other argument or property fails.
3. Layer names are unique. A duplicate fails at the second node.
4. An empty layer fails. `element` and `style` declarations do not count as content.
5. Paint outside a layer — the map body and its non-layer children — forms an implicit bottom layer named `main`.
6. Stated layers follow `main` in document order of first appearance. Order is draw order.
7. Every layer spans the full map. A layer's stamp stretches over the whole grid, so a layer whose statements cover one band still paints that band at its own cells.
8. One surface per document: every layer shares the cell type, the words, the kernels, and the elements.
9. Inside one layer, paint order resolves conflicts: the body first, children after, later children over earlier ones. Between layers nothing overwrites; each layer paints its own grid.
10. Every element reports its rectangle through the tag channel. Each layer owns its landmarks registry, so an element name is unique per layer and may repeat across layers. The layer container reports nothing; read `BuiltLayer.Name` instead.
11. A document without layer nodes resolves to one layer named `main`, and builds as before.
12. A one-layer document builds through `DocFlow.build` and `DocFlow.buildXml`. Two or more layers fail there and name the plural entry point.

## Building a stack

`DocFlow.buildLayers` (KDL) and `DocFlow.buildLayersXml` (XML) parse, resolve, emit, and paint every layer.

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
  Occupancy: Occupancy      // who owns each cell, and what each instance covers
}
```

A document without layers returns one entry named `main`, so one code path handles both shapes. `buildLayers` scans every painted layer into its `Occupancy`. A layer that breaks a span rule fails the build with the layer's name. See [Instances and Occupancy](instances.html).

`DocFlow.emitLayers` stops before the paint and returns the layer stamps, for a caller that owns its grids:

```fsharp
val emitLayers : Doc.Item<'T> -> struct (string * Stamp<'T>)[]
```

In F#, build the same stack from stamps you wrote. You create the grids. `Flow.runLayers` paints stamp i into grid i, bottom first.

```fsharp
open Mibo.Layout

let grids =
  [|
    CellGrid2D.create 40 24 cellSize Vector2.Zero
    CellGrid2D.create 40 24 cellSize Vector2.Zero
  |]

let layers = grids |> Flow.runLayers [| groundStamp; decorStamp |]

let struct (groundGrid, groundMarks) = layers[0]
let struct (decorGrid, decorMarks) = layers[1]
```

`Flow.runLayers` throws before it paints when the two arrays differ in length, when the grids differ in width or height, or when a stamp asks for `Expand`. Each layer gets its own landmarks registry.

`Flow.buildLayers` is `Flow.runLayers` plus `Landmarks.scanTiles` per layer.

Hex grids work unchanged: create them with `CellGrid2D.createHex` and run the same stamps. Painting is cell-space; only world positions and spatial queries see the geometry.

## Spatial queries across layers

Every blocking decision in `Spatial2D` enters through a predicate. The grid argument is read for its bounds only. Compose the layers in the predicate.

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

When the predicate is read per cell (A* and flood fill visit each cell many times), fold the stack into one array at load.

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

The fold runs once, at load. A rule that only some layers state ("decor never blocks", "the top layer is a trigger volume") belongs in the predicate.

Landmarks compose the same way: `Flow.isTag` and `Flow.tryTagGrid` read one layer's registry. `Array.exists` over the layers answers across them.

## Draw one grid per layer

Keep the `BuiltLayer[]` and issue one `CellGrid2D.iterVisible` per layer, each at its own `RenderLayer`. The buffer sorts the layers. An empty cell in an upper grid draws nothing, so the layer under it shows through.

- The Defli sample draws one pass per layer: `Defli/Raylib/MapView.fs` walks the terrain grid, then the road grid, then the decorations.
- The Platformer sample draws the terrain layer plus the entity rects (`Platformer/Raylib/View.fs`), because its layers differ in kind rather than in depth.

Both shapes consume the same array.

When the simulation wants one value per cell (a physics tile, a buildability flag), project the authored grids into one structure at load. The Defli sample's `splitLayers` (`Defli/Shared/State/Systems/Map.fs`) projects into its `LayeredMap`. The Platformer sample keeps one `Tile` per cell and states the layer with `tileLayer` (`Platformer/Shared/Types.fs`).

A block map needs one more step: a cell holds one column, so the topmost layer that painted it is the column that stands there. The LiveMap sample lifts instead: every layer above the ground rises by the height the layers below reach at that cell. `Stack.feet` derives that lift for the whole stack. [Instances and Occupancy](instances.html) covers one instance stretched over several cells of a layer.

## Limits

- An upper layer cannot read what a lower one painted. `Flow.runLayers` owns the paint order, so an upper layer can close over painted lower grids later.
- One placement paints one layer.
- Every layer shares the cell type. Two cell types are two documents, composed in game code.
- A layer is an array position. Roles, opacity, and parallax are draw-time game data.
