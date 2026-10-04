---
title: 3D from 2D
category: Level Design
categoryindex: 8
index: 7
---

# 3D from 2D

A Mibo 3D map is a 2D footprint plus a height per cell. The footprint is a
`CellGrid2D`, exactly like a 2D map; the vertical axis stays out of the
authoring model. This page walks the whole infrastructure: the cell type, the
surface, the build, the occupancy, the stack, and the instanced draw.

An **instance span** stretches one model over several footprint cells, so one
plate replaces 640 columns and one deck covers 4×4 cells. The rules live in
[Instances and Occupancy](instances.html).

## The cell type

The framework reads your cell through projections. Everything else is yours.
A 3D cell carries these fields:

| Field | Purpose | Read by |
|---|---|---|
| A model identity, or a `ModelInfo` that carries the authored size | Keys the draw and gives the transform its divisor | `getKey`, `getMeshesAndMaterial`, `getTransform` |
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

The three projections a 3D map needs are one line each:

```fsharp
let spanOf (cell: BlockCell) = cell.Span
let heightOf (cell: BlockCell) = cell.Height
let withSpan (cell: BlockCell) (span: InstanceSpan) = { cell with Span = span }
```

A 2D flat map needs no `Span` and no `Lift`: a sprite covers its cell.

## The surface

A document resolves cell words, kernels, and elements through a
`Doc.Surface<'T>`. The surface is built once and read per build. The `Span`
and `WithSpan` fields are required by the record; `ValueNone` means "every
cell covers one cell" and "a statement cannot size one". See
[Authored Maps](authored.html) for the complete surface example.

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

A code-first map builds with `Flow.run`; scan the painted grid into an
`Occupancy` beside it:

```fsharp
let struct (grid, marks) = Flow.run map myGrid

let occupancy =
  Occupancy.scan spanOf grid
  |> Result.defaultWith failwith
```

An authored map builds whole layers in one call. Each `BuiltLayer` carries
its grid, its landmarks, and its occupancy:

```fsharp
match DocFlow.buildLayersXml(surface, source) with
| Ok layers -> layers
| Error reason -> failwith reason
```

`DocFlow.build` and `buildXml` return one grid and no occupancy, so they
refuse a document that places a spanning word. A document without layers
builds as one layer named `main`.

## Stack

A layer that draws above another needs the height the layers below reach at
each cell, or its columns replace them. `Stack.feet` derives that lift for
the whole stack:

```fsharp
let feet = Stack.feet occupancies grids heightOf

for i in 0 .. grids.Length - 1 do
  let drawn = CellGrid2D.create width height cellSize Vector2.Zero

  CellGrid2D.iter
    (fun x y cell ->
      CellGrid2D.set x y { cell with Lift = feet[i][x + y * width] } drawn)
    grids[i]
```

`feet[i]` is the height under layer `i`, flat at `x + y * Width`. Layer 0
stands on the plane, heights add, and a spanning anchor lifts its whole
rectangle — so a decoration over a plate lands on the plate.

## Query

One query serves a hover, a collision test, and a spawn: which instance owns
this cell.

```fsharp
let ownerAt (layer: BuiltLayer<BlockCell>) (x: int) (y: int) =
  Occupancy.owner x y layer.Occupancy
  |> ValueOption.bind(fun at ->
    CellGrid2D.get at.X at.Y layer.Grid
    |> ValueOption.map(fun cell -> struct (at, cell)))
```

A covered cell answers with the instance that covers it, not with empty
ground. `Occupancy.rectOf` gives that instance's rectangle, for an outline.
`Landmarks` still answers which element of the document painted the cell.

## Draw

Persist one context per map and give it a rectangle transform. The transform
receives the rectangle each instance covers and the anchor's base world
position, so the model scales over the cells it stands for:

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

`Height` scales the model on Y. `rect.W` and `rect.H` scale it on X and Z, so
a spanning instance fills its rectangle. `Lift` moves it up. On MonoGame, the
types are `Microsoft.Xna.Framework.Vector3` and `Matrix`; the shape is the
same. The plain constructor still takes `Vector3 -> 'T -> Matrix4x4`, and its
rectangle is the cell itself.

Draw each layer through its own occupancy:

```fsharp
context.ResetFrameBuffers()

for layer in layers do
  context.RenderInstanced(buffer, layer.Grid, layer.Occupancy)
```

For a large world, window it. The window is a world-space box in `int`
coordinates; the member converts it with `CellGrid2D.visibleRange` and
enumerates the anchors whose rectangle it meets:

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

The `Draw` DSL routes to the same members:
`buffer.renderFootprintInstanced(ctx, grid, occupancy)` and
`buffer.renderFootprintWindowInstanced(ctx, left, top, right, bottom, grid, occupancy)`,
each with a `shaderForKey` overload. The per-key grouping, the effect scopes,
and the pooled buffers live in [GPU Instancing](../graphics3d/instancing.html).

## Frame order

1. Read the source, or build the stamp in code.
2. Parse, resolve, emit, and paint: one grid per layer.
3. Scan each layer into an `Occupancy`.
4. Derive `Stack.feet` when more than one layer draws.
5. Cache the map. Nothing above runs per frame.
6. Per frame: reset the context buffers, work out the world window from the
   camera, and draw each layer through the occupancy form.
7. Queries run on demand, against the cached occupancy and grid.

## Where to go next

- [Instances and Occupancy](instances.html) — the span vocabulary, the rules,
  and what fails.
- [Code-First Maps](code-first.html) — the code-first authoring DSL.
- [Authored Maps](authored.html) — documents, the surface, and the emitter.
- [Layers](layers.html) — the document construct the stack builds on.
- [Hex Grids](hex.html) — the same path on hex geometry.
- [GPU Instancing](../graphics3d/instancing.html) — the draw path in full.
