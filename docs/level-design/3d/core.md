---
title: 3D Layout Engine
category: Level Design
categoryindex: 8
index: 6
---

# 3D Layout Engine

The Layout3D engine provides a voxel-based level design system for 3D games. It lives in `Mibo.Layout3D`.

## Core Concepts

The system is built on three primitives:

- `CellGrid3D<'T>` - Storage for cell data
- `GridSection3D<'T>` - A cursor/view into the grid for relative positioning
- Stamps - Functions that transform sections (`GridSection3D<'T> -> GridSection3D<'T>`)

## CellGrid3D - The Storage

A dense 3D array that stores your cell content:

```fsharp
open Mibo.Layout3D
open System.Numerics

// Create a 100x50x50 grid with 2x2x2 unit cells
let grid = CellGrid3D.create 100 50 50 (Vector3(2f, 2f, 2f)) Vector3.Zero
```

Each cell holds `'T voption` - either `ValueSome content` or `ValueNone` (empty). This struct-based option type has zero heap allocation per cell.

**Axis Convention:**
- X = width (left/right)
- Y = height (up/down)
- Z = depth (forward/back)

### Basic Operations

```fsharp
// Set a cell
CellGrid3D.set 5 3 10 myCell grid

// Get a cell (returns voption)
match CellGrid3D.get 5 3 10 grid with
| ValueSome cell -> // use cell
| ValueNone -> // empty

// Get world position for a cell
let worldPos = CellGrid3D.getWorldPos 5 3 10 grid  // Vector3(10f, 6f, 20f)
```

### Iteration

```fsharp
// Iterate all populated cells
grid
|> CellGrid3D.iter (fun x y z cell ->
    printfn "Cell at (%d, %d, %d)" x y z
)

// Iterate only visible cells (culled to frustum)
// This is the foundation of efficient rendering in 3D, ensuring
// you only spawn/render models within the camera's view.
// Bounding volume defined by min/max corner positions in world space
let viewBounds = (Vector3(minX, minY, minZ), Vector3(maxX, maxY, maxZ))
grid
|> CellGrid3D.iterVolume viewBounds (fun x y z cell ->
    // render cell at (x, y, z)
)
```

## GridSection3D - The Cursor

A section is a lightweight view into a grid. It provides:

- **Relative coordinates** - (0, 0, 0) is the section's origin, not the grid's
- **Bounds clipping** - Drawing outside the section is safely ignored
- **Zero-copy nesting** - Sub-sections reference the same backing grid

You rarely create sections directly - the `Layout3D.run` function creates the root section for you.

## Layout3D DSL - Composing Content

The `Layout3D` module provides a fluent DSL for placing content. All functions return the section, enabling pipeline composition.

### Basic Usage

```fsharp
open Mibo.Layout3D

let myGrid =
    CellGrid3D.create 20 15 20 (Vector3(2f, 2f, 2f)) Vector3.Zero
    |> Layout3D.run (fun section ->
        section
        |> Layout3D.fill 0 0 0 20 15 20 FloorCell      // Fill entire volume
        |> Layout3D.shell 0 0 0 20 15 20 WallCell     // Add shell
        |> Layout3D.set 10 7 10 ChestCell             // Place item
    )
```

### Scoping with Sections

Create sub-sections for relative positioning:

```fsharp
section
|> Layout3D.section 5 3 0 (fun inner ->
    // (0, 0, 0) here maps to (5, 3, 0) in the parent
    inner
    |> Layout3D.fill 0 0 0 4 4 4 FloorCell
)
// Returns to parent section, can continue chaining
|> Layout3D.section 12 3 0 (fun inner ->
    inner |> Layout3D.fill 0 0 0 4 4 4 FloorCell
)
```

### Structural Helpers

```fsharp
// Padding - shrink section by N cells on all sides
section |> Layout3D.padding 2 (fun inner -> ...)

// PaddingEx - explicit padding for each side: left, bottom, back, right, top, front
section |> Layout3D.paddingEx 1 1 1 1 1 1 (fun inner -> ...)

// Center - position a fixed-size block in the center
section |> Layout3D.center 4 4 4 (fun inner -> ...)

// Flow - place stamps along axis with spacing
section |> Layout3D.flowX 5 stamps
section |> Layout3D.flowY 5 stamps
section |> Layout3D.flowZ 5 stamps
```

### Primitives

```fsharp
Layout3D.set x y z content section                    // Single cell
Layout3D.fill x y z w h d content section             // Box volume
Layout3D.clear x y z w h d section                     // Clear volume
```

### Planes (Single-Cell Thickness)

```fsharp
Layout3D.floorXZ x y z w d content section    // Horizontal floor
Layout3D.wallXY x y z w h content section     // Vertical wall (XY plane)
Layout3D.wallYZ x y z h d content section     // Vertical wall (YZ plane)
```

### 3D Shapes

```fsharp
Layout3D.shell x y z w h d content section    // Hollow box (6 faces)
Layout3D.edges x y z w h d content section    // 12 edges only
```

### Repetition

```fsharp
Layout3D.repeatX x y z count content section  // Line along X
Layout3D.repeatY x y z count content section  // Line along Y (column)
Layout3D.repeatZ x y z count content section  // Line along Z
Layout3D.column x y z height content section   // Alias for repeatY
```

### 3D Geometry

```fsharp
Layout3D.line x1 y1 z1 x2 y2 z2 content section               // 3D Bresenham line
Layout3D.sphere cx cy cz radius filled content section        // Sphere
Layout3D.cylinder cx cz y radius height filled content section // Cylinder (Y-aligned)
```

### Patterns

```fsharp
Layout3D.checker3D odd even section                // 3D checkerboard
Layout3D.checkerXZ y odd even section              // Planar checker (Floor)
Layout3D.checkerXY z odd even section              // Planar checker (Wall)
Layout3D.checkerYZ x odd even section              // Planar checker (Wall)
Layout3D.checkerShell x y z w h d odd even section // Box skin checker

Layout3D.scatter3D count seed content section      // Volumetric scatter
Layout3D.scatterXZ y count seed content section    // Planar scatter
Layout3D.scatterShell x y z w h d count seed content section

Layout3D.generate x y z w h d generator section    // Volumetric generate
Layout3D.generateXZ y generator section            // Planar generate
Layout3D.generateShell x y z w h d generator section
```

### Iteration / Transformation

```fsharp
Layout3D.iter x y z w h d action section    // Read access to volume
Layout3D.map x y z w h d mapping section    // Transform existing content
Layout3D.replace oldContent newContent section  // Find and replace
Layout3D.setIfEmpty x y z content section  // Conditional set
```

## Creating Your Own Stamps

A **stamp** is simply a function `GridSection3D<'T> -> GridSection3D<'T>`. You can create reusable stamps just like HTML custom elements:

### Simple Stamp

```fsharp
/// A treasure chest on a pedestal
let treasureChest (section: GridSection3D<Cell>) =
    section
    |> Layout3D.fill 0 0 1 3 1 3 PedestalCell   // Base
    |> Layout3D.set 1 1 1 ChestCell              // Chest on top
```

### Parameterized Stamp

```fsharp
/// A configurable room with floor, walls, and ceiling
let room width height depth floor wall ceiling (section: GridSection3D<Cell>) =
    section
    |> Layout3D.fill 0 0 0 width 1 depth floor          // Floor
    |> Layout3D.fill 0 height 0 width 1 depth ceiling   // Ceiling
    |> Layout3D.shell 0 0 0 width height depth wall     // Walls
```

### Composing Stamps

Stamps compose with `>>` (function composition):

```fsharp
let guardPost =
    room 8 6 6 FloorCell WallCell CeilingCell
    >> Layout3D.center 2 1 2 (treasureChest)
    >> Layout3D.section 6 2 2 (torchStand)
```

### Building a Component Library

Organize stamps into domain modules:

```fsharp
module Dungeon =
    let cell = room 5 5 5 FloorCell WallCell CeilingCell

    let corridor length =
        Layout3D.fill 0 0 0 length 3 3 FloorCell
        >> Layout3D.shell 0 0 0 length 3 3 WallCell

    let intersection =
        cell
        >> Layout3D.clear 2 0 2 1 5 1  // North door
        >> Layout3D.clear 2 0 0 1 5 1  // South door
        >> Layout3D.clear 0 2 2 5 1 1  // West door
        >> Layout3D.clear 0 2 0 5 1 1  // East door
```

Use them:

```fsharp
level
|> Layout3D.run (fun section ->
    section
    |> Layout3D.section 0 0 0 Dungeon.cell
    |> Layout3D.section 5 1 0 (Dungeon.corridor 10)
    |> Layout3D.section 15 0 0 Dungeon.intersection
)
```

### The Stamp Pattern

Think of stamps as Lego blocks: a few blocks on top of each other build a bigger thing.

The key insight: **stamps are just functions**. You can store them, pass them around, compose them, and build complex structures from simple pieces.

## Domain Modules

Mibo includes pre-built stamps for common 3D game types:

- **[Interior](interior.html)** - Rooms, corridors, doorways, stairs, shafts, pillars, windows
- **[Terrain](terrain.html)** - Ground, plateaus, pits, ramps, paths, heightmaps

These serve as examples and starting points. Copy and modify them for your game's needs.

## Layered Composition

For levels with multiple independent data layers (terrain, structures, items), use `LayeredGrid3D`. It manages a collection of `CellGrid3D` layers sharing the same dimensions, keyed by an integer index:

```fsharp
let level =
    LayeredGrid3D.create 50 20 50 (Vector3(2f, 2f, 2f)) Vector3.Zero
    |> LayeredLayout3D.layer 0 (fun section ->
        // Layer 0: Terrain
        section
        |> Layout3D.fill 0 0 0 50 1 50 FloorCell
        |> Layout3D.shell 10 0 5 8 8 8 WallCell)
    |> LayeredLayout3D.layer 1 (fun section ->
        // Layer 1: Structures
        section
        |> Layout3D.section 5 1 3 (fun inner ->
            inner |> Layout3D.fill 0 0 0 6 4 6 FloorCell)
        |> Layout3D.section 20 1 10 (fun inner ->
            inner |> Layout3D.fill 0 0 0 4 3 4 FloorCell))
    |> LayeredLayout3D.layer 2 (fun section ->
        // Layer 2: Items
        section
        |> Layout3D.set 7 2 5 ChestCell
        |> Layout3D.set 22 2 12 ChestCell)
```

Each `layer` call lazily creates (or reuses) the `CellGrid3D` for that index and runs your stamp against it. Layers are independent; edit one without touching the others.

## Rendering Integration

### Basic Rendering

```fsharp
grid
|> CellGrid3D.iter (fun x y z content ->
    let worldPos = CellGrid3D.getWorldPos x y z grid
    spawnModel worldPos content
)
```

### Volume-Culled Rendering

```fsharp
// Frustum bounds as min/max corner positions in world space
let frustumBounds = (Vector3(minX, minY, minZ), Vector3(maxX, maxY, maxZ))
grid
|> CellGrid3D.iterVolume frustumBounds (fun x y z content ->
    let worldPos = CellGrid3D.getWorldPos x y z grid
    spawnModel worldPos content
)
```

### Using the Renderer Helper

```fsharp
open Mibo.Layout3D

// Basic render
CellGridRenderer3D.render grid (fun worldPos content ->
    spawnModel worldPos content
)

// Volume-culled render
CellGridRenderer3D.renderVolume frustumBounds grid (fun worldPos content ->
    spawnModel worldPos content
)
```

For large grids (1000+ cells), prefer `Layout3D.generate` over setting cells individually - it's a single pass with no intermediate allocations.

## 3D-Specific Considerations

### Multi-Cell Models

1. Use anchor-cell only and handle size at render time
2. Create stamps that fill all occupied cells for blocking/collision

### Rotation and Orientation

The grid stores **position only**. Rotation is:
- Determined at render time by the user
- Derived from context (auto-tiling, neighbor checks)
- Stored in user's cell content type if needed: `type Cell = { Kind: EntityType; Facing: Facing }`
