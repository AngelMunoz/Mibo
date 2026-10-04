---
title: 3D from 2D
category: Level Design
categoryindex: 8
index: 8
---

# 3D from 2D

A 3D map is a 2D footprint plus a height per cell. The footprint is a `CellGrid2D`. The vertical axis stays out of the authoring model. This page covers the cell type, the surface, the build, the occupancy, the stack, and the draw.

An **instance span** stretches one model over several cells. The rules are in [Instances and Occupancy](instances.html).

## Cell type

The framework reads the cell through projections. A 3D cell carries these fields:

| Field | Purpose | Read by |
|---|---|---|
| A model identity, or a `ModelInfo` with the authored size | Keys the draw and gives the transform its divisor | `getKey`, `getMeshesAndMaterial`, `getTransform` |
| `Height: float32` | Vertical stretch, in cells | `Stack.feet`, the transform |
| `Span: InstanceSpan` | Grid-plane occupancy | `Occupancy.scan` |
| `Lift: float32` | Where the column stands | The transform |
| `Solid: bool` | Gameplay meaning | Collision, hover |

```fsharp
type ModelInfo = {
  Name: string
  SizeX: float32
  SizeY: float32
  SizeZ: float32
}

type BlockCell = {
  Model: ModelInfo
  Height: float32
  Span: InstanceSpan
  Lift: float32
  Solid: bool
}
```

```fsharp
let spanOf (cell: BlockCell) = cell.Span
let heightOf (cell: BlockCell) = cell.Height
let withSpan (cell: BlockCell) (span: InstanceSpan) = { cell with Span = span }
```

A 2D flat map needs no `Span` and no `Lift`.

## Surface

A document resolves words, kernels, and elements through a `Doc.Surface<'T>`. Build the surface once. `Span` and `WithSpan` are required fields; `ValueNone` means "every cell covers one cell" and "a statement cannot size one". The complete declaration is in [Authored Maps](authored.html).

```fsharp
let surface: Doc.Surface<BlockCell> = {
  Words = frozen [ "grass", grass; "slab", slab; "pillar", pillar ]
  Kernels = frozen [ "field", Gen2 field; "wood", wordKernel 6 6 grass wall ]
  Elements = frozen [ "hut", hut; "rampart", rampart ]
  Span = ValueSome spanOf        // required; ValueNone: every cell covers one cell
  WithSpan = ValueSome withSpan  // required; ValueNone: a statement cannot size one
}
```

## Build

Code-first: `Flow.run` builds the grid. Scan it into an `Occupancy`.

```fsharp
let struct (grid, marks) = Flow.run map myGrid

let occupancy =
  Occupancy.scan spanOf grid
  |> Result.defaultWith failwith
```

Authored: build every layer in one call. Each `BuiltLayer` carries its grid, landmarks, and occupancy.

```fsharp
match DocFlow.buildLayersXml(surface, source) with
| Ok layers -> layers
| Error reason -> failwith reason
```

`DocFlow.build` and `buildXml` return one grid and no occupancy. They refuse a document that places a spanning word. A document without layers builds as one layer named `main`.

## Stack

A layer above another needs the height the layers below reach at each cell. `Stack.feet` derives that lift.

```fsharp
let feet = Stack.feet occupancies grids heightOf

for i in 0 .. grids.Length - 1 do
  let drawn = CellGrid2D.create width height cellSize Vector2.Zero

  CellGrid2D.iter
    (fun x y cell ->
      CellGrid2D.set x y { cell with Lift = feet[i][x + y * width] } drawn)
    grids[i]
```

`feet[i]` is the height under layer `i`, at `x + y * Width`. Layer 0 stands on the plane. Heights add. A spanning anchor lifts its whole rectangle, so a decoration over a plate lands on the plate.

## Query

One query serves a hover, a collision test, and a spawn: which instance owns a cell.

```fsharp
let ownerAt (layer: BuiltLayer<BlockCell>) (x: int) (y: int) =
  Occupancy.owner x y layer.Occupancy
  |> ValueOption.bind(fun at ->
    CellGrid2D.get at.X at.Y layer.Grid
    |> ValueOption.map(fun cell -> struct (at, cell)))
```

A covered cell answers with the instance that covers it. `Occupancy.rectOf` gives the instance rectangle. `Landmarks` answers which document element painted the cell.

## Draw

Create one context per map with a rectangle transform. The transform receives the rectangle and the anchor position. It scales the model over the cells it covers.

```fsharp
let context =
  InstancedRenderContext<BlockCell, string>.Rect(
    getKey = (fun cell -> cell.Model.Name),
    getMeshesAndMaterial = meshesOf,
    getTransform =
      fun (rect: CellRect) (basePos: Vector3) (cell: BlockCell) ->
        let boxW = float32 rect.W * cellSize
        let boxD = float32 rect.H * cellSize   // rect.H is depth
        let boxH = cell.Height * cellSize

        Matrix4x4.CreateScale(
          boxW / cell.Model.SizeX,
          boxH / cell.Model.SizeY,
          boxD / cell.Model.SizeZ)
        * Matrix4x4.CreateTranslation(
          basePos.X + boxW * 0.5f,
          basePos.Y + cell.Lift,
          basePos.Z + boxD * 0.5f)
  )
```

`Height` scales the model on Y. `rect.W` and `rect.H` scale it on X and Z. `Lift` moves it up. On MonoGame, the types are `Microsoft.Xna.Framework.Vector3` and `Matrix`. The plain constructor takes `Vector3 -> 'T -> Matrix4x4`; its rectangle is the cell.

Draw each layer through its occupancy:

```fsharp
context.ResetFrameBuffers()

for layer in layers do
  context.RenderInstanced(buffer, layer.Grid, layer.Occupancy)
```

For a large world, window the draw. The window is a world-space box in `int` coordinates. The member converts it with `CellGrid2D.visibleRange` and visits the anchors whose rectangle it meets.

```fsharp
context.RenderWindowInstanced(
  buffer,
  int camera.Left,
  int camera.Top,
  int camera.Right,
  int camera.Bottom,
  layer.Grid,
  layer.Occupancy
)
```

The `Draw` DSL routes to the same members: `buffer.renderFootprintInstanced(ctx, grid, occupancy)` and `buffer.renderFootprintWindowInstanced(ctx, left, top, right, bottom, grid, occupancy)`. Each has a `shaderForKey` overload. Per-key grouping, effect scopes, and pooled buffers are in [GPU Instancing](../graphics3d/instancing.html).

## Order

1. Read the source, or build the stamp in code.
2. Parse, resolve, emit, and paint: one grid per layer.
3. Scan each layer into an `Occupancy`.
4. Derive `Stack.feet` when more than one layer draws.
5. Cache the map. Steps 1 to 5 run once.
6. Per frame: reset the context buffers, compute the world window, draw each layer.
7. Query on demand against the cached occupancy and grid.

## Next

- [Instances and Occupancy](instances.html): the span vocabulary and rules.
- [Code-First Maps](code-first.html): the F# authoring DSL.
- [Authored Maps](authored.html): documents, the surface, the emitter.
- [Layers](layers.html): the document stack.
- [Hex Grids](hex.html): the same path on hex geometry.
- [GPU Instancing](../graphics3d/instancing.html): the draw path in full.
