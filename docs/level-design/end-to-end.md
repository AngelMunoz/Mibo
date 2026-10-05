---
title: Build a Map End to End
category: Level Design
categoryindex: 8
index: 2
---

# Build a Map End to End

This page builds one map from an empty project: the cell type, the domain, a KDL document, the build, the queries, and the instanced draw. Each step is one short block. The other pages add options; this one shows the whole path in order.

The example is a 3D map. A 2D map is the same path without `Height`, `Lift`, and `Span`, and it draws sprites instead of instances. Step 12 states the difference.

## 1. Define the cell type

The cell holds what the renderer and the simulation read.

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

## 2. Create the grid

The grid is the same `CellGrid2D` used by 2D maps. A cell holds `ValueSome cell` or `ValueNone`.

```fsharp
open Mibo.Layout
open System.Numerics

let cellSize = 16f

let newGrid (width: int) (height: int) =
  CellGrid2D.create width height (Vector2(cellSize, cellSize)) Vector2.Zero
```

## 3. Define the domain values

A **word** names one cell value. A **kernel** is a per-cell rule for `generate`. An **element** is a named body of statements.

```fsharp
let model name sx sy sz = {
  Name = name
  SizeX = sx
  SizeY = sy
  SizeZ = sz
}

let grass = { Model = model "grass" 1f 1f 1f; Height = 1f; Span = One; Lift = 0f; Solid = false }
let wall = { Model = model "wall" 1f 1f 1f; Height = 2f; Span = One; Lift = 0f; Solid = true }
let tree = { Model = model "tree" 1f 1f 1f; Height = 3f; Span = One; Lift = 0f; Solid = true }
let slab = { Model = model "platform" 1f 1f 1f; Height = 0.2f; Span = Span(3, 2); Lift = 0f; Solid = false }
let chest = { Model = model "chest" 1f 1f 1f; Height = 1f; Span = One; Lift = 0f; Solid = false }
```

`Height` scales the mesh on Y. `Span` states how many cells the instance covers. `Solid` is gameplay data; the framework never reads it.

```fsharp
let field =
  Doc.Gen2(fun x y -> if (x + y) % 7 = 0 then tree else grass)

let hut: Doc.ElementDecl<BlockCell> = {
  Name = "hut"
  Extent = ValueSome { W = 3; H = 3 }
  Body = [|
    Doc.Op.Fill grass
    Doc.Op.Border(ValueSome { X = 0; Y = 0; W = 3; H = 3 }, wall)
    Doc.Op.Set(ValueSome { X = 1; Y = 1 }, Start, Start, chest)
  |]
}
```

## 4. Build the surface

The surface maps document names to the domain. Build it once.

```fsharp
open System.Collections.Frozen
open System.Collections.Generic
open Mibo.Markup

let frozen (pairs: (string * 'T)[]) =
  let table = Dictionary<string, 'T>()

  for name, value in pairs do
    table[name] <- value

  table.ToFrozenDictionary()

let spanOf (cell: BlockCell) = cell.Span
let heightOf (cell: BlockCell) = cell.Height
let withSpan (cell: BlockCell) (span: InstanceSpan) = { cell with Span = span }

let surface: Doc.Surface<BlockCell> = {
  Words =
    frozen [|
      "grass", grass
      "wall", wall
      "tree", tree
      "slab", slab
    |]
  Kernels = frozen [| "field", field |]
  Elements = frozen [| "hut", hut |]
  Span = ValueSome spanOf
  WithSpan = ValueSome withSpan
}
```

The three tables are the game's. The framework reads them and the two span projections. Your names and cell type are yours.

## 5. Write the document

```kdl
map 24 16 {
  layer ground {
    fill grass
    generate 0 0 24 4 field
    set 4 6 slab spanX=3 spanZ=2
  }

  layer decor {
    hut x=10 y=8
    set 20 12 tree
  }
}
```

`fill` paints the whole layer. `generate` runs the kernel over the top four rows. `set` places one cell, and `spanX`/`spanZ` size the slab to 3x2. `hut` places the element from the surface. XML spells the same document; see [Authored Maps](authored.html).

## 6. Build the layers

One call parses, resolves, emits, paints, and scans every layer.

```fsharp
let layers =
  match DocFlow.buildLayers (surface, src) with
  | Ok layers -> layers
  | Error reason -> failwith reason
```

Each layer is a `BuiltLayer<'T>`: `Name`, `Grid`, `Landmarks`, `Occupancy`.

```fsharp
let ground = layers[0]
let decor = layers[1]
```

## 7. Lift the layers

A decoration above the ground needs the height below it, or it replaces the ground. `Stack.feet` derives the lift for the whole stack.

```fsharp
let grids = layers |> Array.map(fun layer -> layer.Grid)
let occupancies = layers |> Array.map(fun layer -> layer.Occupancy)
let feet = Stack.feet occupancies grids heightOf

let drawn =
  Array.init layers.Length (fun i ->
    let source = layers[i].Grid
    let target = newGrid source.Width source.Height

    CellGrid2D.iter
      (fun x y cell ->
        CellGrid2D.set x y { cell with Lift = feet[i][x + y * source.Width] } target)
      source

    target)
```

Skip this step for a flat, single-layer map.

## 8. Query

One query serves a hover, a collision test, and a spawn: which instance owns the cell.

```fsharp
let ownerAt (layer: BuiltLayer<BlockCell>) (x: int) (y: int) =
  Occupancy.owner x y layer.Occupancy
  |> ValueOption.bind(fun at ->
    CellGrid2D.get at.X at.Y layer.Grid
    |> ValueOption.map(fun cell -> struct (at, cell)))

let rectAt (layer: BuiltLayer<BlockCell>) (x: int) (y: int) =
  Occupancy.owner x y layer.Occupancy
  |> ValueOption.bind(fun at -> Occupancy.rectOf at layer.Occupancy)
```

A covered cell answers with the instance that covers it. Element names ride the tag channel in an authored map:

```fsharp
Flow.taggedRects "hut" decor.Landmarks   // every hut, newest first
Flow.isTag "hut" { X = 10; Y = 8 } decor.Landmarks
```

## 9. Draw

One context per map, with a rectangle transform. The transform receives the rectangle the instance covers and the anchor position.

```fsharp
let context =
  InstancedRenderContext<BlockCell, string>.Rect(
    getKey = (fun cell -> cell.Model.Name),
    getMeshesAndMaterial = meshesOf,
    getTransform =
      fun (rect: CellRect) (basePos: Vector3) (cell: BlockCell) ->
        let boxW = float32 rect.W * cellSize
        let boxD = float32 rect.H * cellSize
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

Draw each layer through its own occupancy:

```fsharp
let view (_ctx: GameContext) (_model: Model) (buffer: RenderBuffer3D) =
  context.ResetFrameBuffers()
  buffer.beginCamera(camera).drop()

  for i in 0 .. drawn.Length - 1 do
    context.RenderInstanced(buffer, drawn[i], layers[i].Occupancy)

  buffer.endCamera().drop()
```

`meshesOf`, `camera`, and `buffer` are game values. For a large map, replace `RenderInstanced` with `RenderWindowInstanced` and pass the camera window. See [3D from 2D](three-d.html) and [GPU Instancing](../graphics3d/instancing.html).

## 10. Wire it into the program

Steps 1 to 7 run once, in `init`. Cache `layers`, `drawn`, and `context`. Step 9 runs in `view` each frame. Step 8 runs on demand. The frame order is in [3D from 2D](three-d.html).

## 11. The same map in F#

A code-first map skips the surface. Compose `Stamp` values and run them.

```fsharp
let hutStamp =
  Stamp.named "hut" (Stamp.box 3 3 [ Flow.fill grass; Flow.border wall; Flow.cell { X = 1; Y = 1 } chest ])

let map =
  Flow.overlay [
    Flow.canvas [ Flow.fill grass; Flow.cell { X = 4; Y = 6 } slab ]
    Flow.at 10 8 hutStamp
  ]

let struct (grid, marks) = newGrid 24 16 |> Flow.run map

let occupancy =
  Occupancy.scan spanOf grid
  |> Result.defaultWith failwith
```

A named stamp answers `Flow.tryPosition`, not `Flow.taggedRects`:

```fsharp
Flow.tryPosition "hut" marks
```

Steps 7 to 10 are unchanged. See [Code-First Maps](code-first.html) for the container and style vocabulary.

## 12. 2D maps

Drop `Height`, `Span`, and `Lift` from the cell type. The document drops `spanX=`/`spanZ=`. The grid, the surface, the build, the landmarks, and the queries stay the same. Draw with the sprite loop in [Code-First Maps](code-first.html).
