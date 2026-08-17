---
title: 2D Layout Engine
category: Level Design
categoryindex: 8
index: 2
---

# 2D Layout Engine

The Layout engine provides a tile-based level design system for 2D games. It lives in `Mibo.Layout`.

> **`Vector2` namespace (MonoGame).** The Core layout APIs (`CellGrid2D`, `LayeredGrid2D`, and the 3D variants) always take `System.Numerics.Vector2`. MonoGame projects `open Microsoft.Xna.Framework`, so a bare `Vector2(...)` resolves to XNA's vector type and the Core layout calls fail to compile (`FS0193`). Qualify those calls explicitly:
> ```fsharp
> let grid =
>     CellGrid2D.create 100 50 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
> ```
> Backend-specific APIs (each backend's `Camera2D.create`/`Camera3D`, `SpriteState`, `TextState`) use that backend's native vector type: raylib uses `System.Numerics`, MonoGame uses `Microsoft.Xna.Framework`, so bare `Vector2(...)` is fine there as long as the matching namespace is open. See [MonoGame type quirks](../../monogame-types.html) for the full backend-type reference.

## Core Concepts

The system is built on three primitives:

- `CellGrid2D<'T>` - Storage for tile data
- `GridSection2D<'T>` - A cursor/view into the grid for relative positioning
- Stamps - Functions that transform sections (`GridSection2D<'T> -> GridSection2D<'T>`)

## CellGrid2D - The Storage

A dense 2D array that stores your tile content:

```fsharp
open Mibo.Layout
open System.Numerics

// Create a 100x50 grid with 32x32 pixel cells
let grid = CellGrid2D.create 100 50 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
```

Each cell holds `'T voption` - either `ValueSome content` or `ValueNone` (empty). This struct-based option type has zero heap allocation per cell.

### Basic Operations

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

### Iteration

```fsharp
// Iterate all populated cells
grid
|> CellGrid2D.iter (fun x y tile ->
    printfn "Tile at (%d, %d)" x y
)

// Iterate only visible cells (culled to viewport). Pass the viewport bounds as
// left/top/right/bottom pixel coordinates. This is critical for performance in
// large levels, as it avoids processing tiles that aren't on screen.
grid
|> CellGrid2D.iterVisible cameraX cameraY (cameraX + viewportWidth) (cameraY + viewportHeight) (fun x y tile ->
    // render tile at (x, y)
)
```

## GridSection2D - The Cursor

A section is a lightweight view into a grid. It provides:

- **Relative coordinates** - (0, 0) is the section's top-left, not the grid's
- **Bounds clipping** - Drawing outside the section is safely ignored
- **Zero-copy nesting** - Sub-sections reference the same backing grid

You rarely create sections directly - the `Layout.run` function creates the root section for you.

## Layout DSL - Composing Content

The `Layout` module provides a fluent DSL for placing content. All functions return the section, enabling pipeline composition.

### Basic Usage

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

### Scoping with Sections

Create sub-sections for relative positioning:

```fsharp
section
|> Layout.section 5 3 (fun inner ->
    // (0, 0) here maps to (5, 3) in the parent
    inner
    |> Layout.fill 0 0 4 4 FloorTile
)
// Returns to parent section, can continue chaining
|> Layout.section 12 3 (fun inner ->
    inner |> Layout.fill 0 0 4 4 FloorTile
)
```

### Structural Helpers

```fsharp
// Padding - shrink section by N cells on all sides
section |> Layout.padding 2 (fun inner -> ...)

// PaddingEx - explicit padding for each side: left, top, right, bottom
section |> Layout.paddingEx 1 2 1 2 (fun inner -> ...)

// Center - position a fixed-size block in the center
section |> Layout.center 4 4 (fun inner -> ...)

// Flow - place stamps horizontally or vertically with spacing
section |> Layout.flowX 5 stamps
section |> Layout.flowY 5 stamps
```

### Primitives

```fsharp
Layout.set x y content section         // Single cell
Layout.fill x y w h content section    // Rectangle
Layout.border x y w h content section  // Hollow rectangle
Layout.rect x y w h bContent fContent section // Filled rectangle with border
Layout.corners x y w h content section // Only the four corners
Layout.repeatX x y count content section // Horizontal line
Layout.repeatY x y count content section // Vertical line
Layout.clear x y w h section           // Clear cells to empty
```

### Geometry

```fsharp
Layout.line x1 y1 x2 y2 content section        // Bresenham line
Layout.circle cx cy radius filled content      // Midpoint circle
Layout.polygon points filled content           // Arbitrary polygon
```

### Patterns

```fsharp
Layout.checker oddContent evenContent section  // checkerboard pattern
Layout.checkerBorder x y w h odd even section  // Only on perimeter
Layout.scatter count seed content section      // Random placement
Layout.scatterBorder x y w h count seed content section // On perimeter
Layout.scatterLine x1 y1 x2 y2 count seed content section // Along line
Layout.generate x y w h (fun x y -> ...) section  // Procedural
```

### Iteration / Transformation

In-place operations for modifying existing content:

```fsharp
Layout.iter x y w h action section    // Read access to volume
Layout.map x y w h mapping section    // Transform existing content
Layout.replace oldContent newContent section  // Find and replace
Layout.replaceScatter old new prob seed section // Probabilistic replace
Layout.scatterStamp count seed stamp section  // Place complex components
Layout.setIfEmpty x y content section  // Conditional set
```

## Layered Composition

For multi-layer content (background, foreground, decorations), use `LayeredGrid2D`. This manages a collection of grids sharing the same dimensions, keyed by an integer index (usually representing depth).

```fsharp
let level =
    LayeredGrid2D.create 100 50 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
    |> LayeredLayout.layer 0 (fun section ->
        // Layer 0: Ground/Collision
        section |> Layout.fill 0 45 100 5 GroundTile
    )
    |> LayeredLayout.layer 1 (fun section ->
        // Layer 1: Foliage
        section |> Layout.scatter 50 42 GrassDecoration
    )
```

### Rendering Layers

When rendering a layered grid, you don't need to manually sort the layers. Instead, you can map the grid's layer index to Mibo's `RenderLayer` measure. The engine's deferred rendering system will handle the sorting for you:

```fsharp
// Render each layer into the buffer
for KeyValue(layerIndex, layerGrid) in level.Layers do
    let isVisible x y tile =
        let pos = CellGrid2D.getWorldPos x y layerGrid
        true  // your visibility test against viewBounds

    layerGrid
    |> CellGrid2D.iterVisible 0 0 100 100 isVisible
    )
```

This approach is efficient because Mibo's `RenderBuffer` performs a single, optimized CPU-side sort of all collected draw commands before sending them to the GPU. This ensures your layout layers are drawn in the correct back-to-front order and allows them to interact correctly with other game entities (like players or particles) that are also tagged with `RenderLayer` values.

Layers are created on-demand, so only layers you've painted into will consume memory.

## Creating Your Own Stamps

A **stamp** is a function `GridSection2D<'T> -> GridSection2D<'T>`: it takes a section and returns a modified one. You can create reusable stamps, the way UI frameworks let you define reusable components:

### Simple Stamp

```fsharp
/// A treasure chest on a pedestal
let treasureChest (section: GridSection2D<Tile>) =
    section
    |> Layout.fill 0 1 3 1 PedestalTile   // Base
    |> Layout.set 1 0 ChestTile           // Chest on top
```

### Parameterized Stamp

```fsharp
/// A configurable room with walls and floor
let room width height floor wall (section: GridSection2D<Tile>) =
    section
    |> Layout.fill 0 0 width height floor
    |> Layout.border 0 0 width height wall
```

### Composing Stamps

Stamps compose with `>>` (function composition):

```fsharp
let guardPost =
    room 8 6 FloorTile WallTile
    >> Layout.center 2 1 (treasureChest)
    >> Layout.section 6 2 torchStand   // torchStand: your stamp placing a torch prop
```

### Building a Component Library

Organize stamps into domain modules:

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

Use them:

```fsharp
level
|> LayeredLayout.layer 0 (fun section ->
    section
    |> Layout.section 0 0 Dungeon.cell
    |> Layout.section 5 1 (Dungeon.corridor 10)
    |> Layout.section 15 0 Dungeon.intersection
)
```

### The Stamp Pattern

Think about stamps like Lego pieces, you can use a few blocks to build a bigger thing.

The key insight: **stamps are functions**. You can store them, pass them around, compose them, and build complex structures from simple pieces.

## Domain Modules

Mibo includes pre-built stamps for common game types:

- **[Platformer](platformer.html)** - Boxes, platforms, ledges, walls, pillars, stairs, slopes, pits
- **[TopDown](topdown.html)** - Rooms, corridors, wall segments, doorways

These serve as examples and starting points. Copy and modify them for your game's needs.
