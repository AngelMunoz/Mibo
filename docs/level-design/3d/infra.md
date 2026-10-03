---
title: The Map Contract
category: Level Design
categoryindex: 8
index: 14
---

# The map contract

A map game implements two halves.

- **The domain** is what the map *is*: words, kernels, elements, layers, and
  spans. It is authored — in a document, or in Flow code.
- **The infrastructure** is what your game implements to consume a built map:
  the cell type, the surface, the build call, the queries, the render context,
  and the stack.

This page is the second half: what to declare, what to build, what to query,
and what to draw. Every name here is a shipped signature.

## What a map is made of

**A word** is a cell value the game names. `Surface.Words` maps a string to a
`'T`, and a statement writes that value into a cell: `fill grass`,
`set 4 6 wall`. One word paints one cell.

**A kernel** is a rule for one cell, referenced by name through `generate`:

```fsharp
[<Struct>]
type Kernel<'T> = Gen2 of gen2: (int -> int -> 'T)
```

A kernel is a pure function of *local* cell coordinates; `Layout.generate`
clamps the requested area to the section and calls it once per cell. A rule
over the coordinates joins two generated areas with no seam; a Flow `Stamp`
baked once into a kernel repeats a fixed picture over the area.

**An element** is a named body of statements with an optional intrinsic size:

```fsharp
type ElementDecl<'T> = {
  Name: string
  Extent: CellSize voption
  Body: Op<'T>[]
}
```

**A statement** is the closed union the resolver produces and the interpreter
runs — `Fill`, `FillRect`, `Set`, `Border`, `Rect`, `Generate`. Geometry is
box-local, and games extend through words, kernels, and element bodies, never
through a new case.

**A layer** is a document container holding one grid. The build returns one
grid per layer, bottom first.

**A span** is how many cells one drawn instance covers —
[instances larger than a cell](spans.html).

## The cell type

The framework reads two things, and only through projections. Everything else
is your game's.

| Field | Purpose | Who writes it | Read by |
| --- | --- | --- | --- |
| A model identity, or a `ModelInfo` that carries the authored size | Keys the draw and gives the transform its divisor | The word | `getKey`, `getMeshesAndMaterial`, `getTransform` |
| `Height: float32` | Vertical stretch, in cells | The word | `Stack.feet` |
| `Span: InstanceSpan` | Grid-plane occupancy | The word, or the statement | `Occupancy.scan` |
| `Lift: float32` | Where the column stands | The build | `getTransform` |
| `Solid: bool` | Gameplay meaning | The word | Collision, hover |

A 2D flat map needs no `Span` and no `Lift`: a sprite covers its cell. The
three projections a 3D map needs are one line each:

```fsharp
let spanOf (cell: BlockCell) = cell.Span
let heightOf (cell: BlockCell) = cell.Height
let withSpan (cell: BlockCell) (span: InstanceSpan) = { cell with Span = span }
```

## The surface

The surface is the game's whole say, built once and read per build. Every
table is frozen, so a read costs one hash:

```fsharp
let surface: Doc.Surface<BlockCell> = {
  Words = frozen [ "grass", grass; "slab", slab; "pillar", pillar ]
  Kernels = frozen [ "field", Gen2 field; "wood", Bake.kernel 6 6 grass wood ]
  Elements = frozen [ element "hut" ...; element "rampart" ... ]
  Span = ValueSome spanOf        // absent: every cell covers one cell
  WithSpan = ValueSome withSpan  // absent: a statement cannot size one
}
```

`Span` and `WithSpan` are optional. A surface that states neither builds
exactly as it did before, and a statement that states a span fails with
`the surface cannot state a span`.

## Build

One call builds the whole map: one `BuiltLayer` per layer, bottom first.

```fsharp
type BuiltLayer<'T> = {
  Name: string
  Grid: CellGrid2D<'T>
  Landmarks: Landmarks      // resolved element rectangles, by tag
  Occupancy: Occupancy      // who owns each cell, and what each instance covers
}
```

```fsharp
match DocFlow.buildLayersXml(surface, source) with
| Ok layers -> layers
| Error reason -> failwith reason
```

`DocFlow.build` and `buildXml` return one grid and no occupancy, so they refuse
a document that places a spanning word and name `buildLayers`. A document
without layers builds as one layer named `main`.

A code-first game skips Markup entirely:

```fsharp
let struct (grid, marks) = Flow.run map myGrid

let occupancy =
  Occupancy.scan spanOf grid
  |> Result.defaultWith failwith
```

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

A covered cell answers with the instance that covers it, not with empty ground.
`Occupancy.rectOf` gives that instance's rectangle, for an outline;
`Landmarks` still answers which element of the document painted the cell.

## Render

Persist one context per map, and give it a rectangle transform:

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

The transform receives the rectangle the instance covers, so the game divides
by the mesh's authored size instead of re-deriving the size from the cell. The
plain constructor still takes `Vector3 -> 'T -> Matrix4x4`, and its rectangle
is the cell itself.

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
each with a `shaderForKey` overload.

## Stack

A layer that draws above another needs the height the layers below reach at
each cell, or its columns replace them:

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

- [Instances larger than a cell](spans.html) — the span vocabulary, the rules,
  and what fails.
- [Layers in authored maps](../2d/layers.html) — the document construct this
  one builds on.
- [Markup](../2d/markup.html) — the document syntax and the resolver.
- [Flow](../2d/flow.html) — the code-first authoring DSL.
- [Instancing](../../graphics3d/instancing.html) — the draw path in full.
