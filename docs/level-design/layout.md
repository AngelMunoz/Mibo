---
title: The Layout Escape Hatch
category: Level Design
categoryindex: 8
index: 3
---

# The Layout escape hatch

`Layout` is the low-level grid API. Use it for exact index math and manual section surgery. [Code-First Maps](code-first.html) cover the common path; every Flow style is a `Layout` pipeline underneath, and `Stamp.sized` wraps a `Layout` pipeline as a Flow element.

The `LayeredGrid2D` helper is obsolete: a layered grid is a dictionary of grids you own. The retired helper is in the [archive](../v5/legacy-grids/layered-2d.html).

> **`Vector2` and MonoGame.** The Core layout API always takes `System.Numerics.Vector2`. MonoGame projects that `open Microsoft.Xna.Framework` must qualify those calls:
> ```fsharp
> let grid =
>     CellGrid2D.create 100 50 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
> ```
> See [MonoGame type quirks](../monogame-types.html).

## Concepts

- `CellGrid2D<'T>` stores the cells.
- `GridSection2D<'T>` is a zero-copy view into the grid. Coordinates are local to the section.
- A stamp is a function `GridSection2D<'T> -> GridSection2D<'T>`.

## The grid

A dense 2D array. Each cell holds `'T voption`: `ValueSome content` or `ValueNone`.

```fsharp
open Mibo.Layout
open System.Numerics

// Create a 100x50 grid with 32x32 pixel cells
let grid = CellGrid2D.create 100 50 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
```

```fsharp
// Set a cell
CellGrid2D.set 5 3 myTile grid

// Get a cell (returns voption)
match CellGrid2D.get 5 3 grid with
| ValueSome tile -> // use tile
| ValueNone -> // empty

// Get world position for a cell
let worldPos = CellGrid2D.getWorldPos 5 3 grid  // System.Numerics.Vector2(160f, 96f)
```

```fsharp
// Iterate all populated cells
grid
|> CellGrid2D.iter (fun x y tile ->
    printfn "Tile at (%d, %d)" x y
)

// Iterate only visible cells. Bounds are left/top/right/bottom int pixels.
grid
|> CellGrid2D.iterVisible
    (int cameraX) (int cameraY)
    (int (cameraX + viewportWidth)) (int (cameraY + viewportHeight))
    (fun x y tile ->
        // render tile at (x, y)
        ()
    )
```

## Sections

A section is a zero-copy view. `(0, 0)` is the section's origin, not the grid's. Drawing outside the section is ignored. `Layout.run` creates the root section.

```fsharp
open Mibo.Layout

let myGrid =
    CellGrid2D.create 20 15 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
    |> Layout.run (fun section ->
        section
        |> Layout.fill 0 0 20 15 FloorTile      // Fill entire area
        |> Layout.border 0 0 20 15 WallTile     // Add border
        |> Layout.set 10 7 ChestTile            // Place item
    )
```

```fsharp
section
|> Layout.section 5 3 (fun inner ->
    // (0, 0) here maps to (5, 3) in the parent
    inner
    |> Layout.fill 0 0 4 4 FloorTile
)
|> Layout.section 12 3 (fun inner ->
    inner |> Layout.fill 0 0 4 4 FloorTile
)
```

## Operations

```fsharp
// Shrink the section by N on all sides
section |> Layout.padding 2 (fun inner -> ...)

// Explicit padding per side: left, top, right, bottom
section |> Layout.paddingEx 1 2 1 2 (fun inner -> ...)

// Center a fixed-size block
section |> Layout.center 4 4 (fun inner -> ...)

// Place stamps in a row or column with spacing
section |> Layout.flowX 5 stamps
section |> Layout.flowY 5 stamps

// Place, fill, and outline
Layout.set x y content section
Layout.fill x y w h content section
Layout.border x y w h content section
Layout.rect x y w h borderContent fillContent section
Layout.corners x y w h content section
Layout.repeatX x y count content section
Layout.repeatY x y count content section
Layout.clear x y w h section
```

## Shapes and patterns

```fsharp
Layout.line x1 y1 x2 y2 content section        // Bresenham line
Layout.circle cx cy radius filled content      // Midpoint circle
Layout.polygon points filled content           // Arbitrary polygon

Layout.checker oddContent evenContent section  // checkerboard pattern
Layout.checkerBorder x y w h odd even section  // perimeter checker
Layout.scatter count seed content section      // random placement
Layout.scatterBorder x y w h count seed content section // random perimeter
Layout.scatterLine x1 y1 x2 y2 count seed content section // random line
Layout.generate x y w h (fun x y -> ...) section  // procedural
```

## Rewrite existing paint

```fsharp
Layout.iter x y w h action section    // read access
Layout.map x y w h mapping section    // transform existing content
Layout.replace oldContent newContent section  // find and replace
Layout.replaceScatter old new prob seed section // probabilistic replace
Layout.scatterStamp count seed stamp section  // place complex components
Layout.setIfEmpty x y content section  // set only when empty
```

## Layers

Keep a `Dictionary<int, CellGrid2D<'T>>` keyed by layer index. Create a layer on demand.

```fsharp
let layers = Dictionary<int, CellGrid2D<Tile>>()

let layer index paint =
    let grid =
        match Dictionary.tryGetValue index layers with
        | ValueSome grid -> grid
        | ValueNone ->
            let grid =
                CellGrid2D.create 100 50 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero

            layers.[index] <- grid
            grid

    Layout.run paint grid

layer 0 (fun section ->
    // Layer 0: Ground/Collision
    section |> Layout.fill 0 45 100 5 GroundTile
) |> ignore

layer 1 (fun section ->
    // Layer 1: Foliage
    section |> Layout.scatter 50 42 GrassDecoration
) |> ignore
```

Render each layer into the buffer and tag it with its `RenderLayer`. The buffer sorts all commands, so layers draw back to front and interleave with entities and particles.

```fsharp
// Render each layer into the buffer
for KeyValueV(layerIndex, layerGrid) in layers do
    let drawTile x y tile =
        let pos = CellGrid2D.getWorldPos x y layerGrid
        // submit the draw command for tile at pos, tagged with layerIndex
        // as its RenderLayer
        ()

    // iterate only the cells inside the viewport (left/top/right/bottom, in pixels)
    layerGrid
    |> CellGrid2D.iterVisible viewLeft viewTop viewRight viewBottom drawTile
```

Layers are created on demand, so only painted layers consume memory.

Text documents split into layers too ([Layers](layers.html)), and `Flow.runLayers` builds one grid per layer from F# stamps. What you keep afterwards is your choice: an array of grids, the dictionary above, or one grid folded at load.

## Stamps

A stamp is a function `GridSection2D<'T> -> GridSection2D<'T>`. Build reusable pieces with `Layout` calls.

```fsharp
/// A treasure chest on a pedestal
let treasureChest (section: GridSection2D<Tile>) =
    section
    |> Layout.fill 0 1 3 1 PedestalTile   // Base
    |> Layout.set 1 0 ChestTile           // Chest on top

/// A configurable room with walls and floor
let room width height floor wall (section: GridSection2D<Tile>) =
    section
    |> Layout.fill 0 0 width height floor
    |> Layout.border 0 0 width height wall

let guardPost =
    room 8 6 FloorTile WallTile
    >> Layout.center 2 1 (treasureChest)
    >> Layout.section 6 2 torchStand   // torchStand: your stamp placing a torch prop
```

Organize stamps into modules.

```fsharp
module Dungeon =
    let cell = room 5 5 StoneFloor StoneWall

    let corridor length =
        Layout.fill 0 0 length 3 StoneFloor
        >> Layout.repeatX 0 0 length StoneWall
        >> Layout.repeatX 0 2 length StoneWall

    let intersection =
        cell >> Layout.clear 2 0 1 1  // North door
            >> Layout.clear 2 4 1 1   // South door
            >> Layout.clear 0 2 1 1   // West door
            >> Layout.clear 4 2 1 1   // East door
```

```fsharp
level
|> Layout.run (fun section ->
    section
    |> Layout.section 0 0 Dungeon.cell
    |> Layout.section 5 1 (Dungeon.corridor 10)
    |> Layout.section 15 0 Dungeon.intersection
)
```

Stamps compose with `>>`. Store them, pass them, and build bigger pieces from smaller ones.

## Retired stamps

The pre-built stamp libraries are retired. Flow styles replace their vocabulary (`Stamp.box` plus `Flow.fill`, `Flow.border`, and friends). The retired pages remain in the archive:

- [Platformer](../v5/legacy-stamps/platformer.html)
- [TopDown](../v5/legacy-stamps/topdown.html)
