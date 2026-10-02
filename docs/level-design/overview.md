---
title: Level Design Overview
category: Level Design
categoryindex: 8
index: 1
---

# Level Design

Mibo provides grid-based layout engines for designing game levels programmatically. The system is content-agnostic and works with any tile/entity type you define.

**Author with [Flow](2d/flow.html).** Flow is the level-authoring DSL: elements compose like HTML, containers distribute space like CSS flexbox and grid, and landmarks report the semantic regions that gameplay reads. The rest of this overview covers the grid storage underneath and the low-level `Layout` pipelines for pixel-perfect control.

## Core Philosophy

- **Code-first design** - Define levels as pure F# functions, not designer tools
- **Grid-based positioning** - Cells define spatial positions for your content
- **Composable primitives** - Build complex structures from reusable "stamps"
- **Position, not rendering** - Grid handles spatial placement; you handle models and collision

## Grid Storage

All 2D maps — square or hex — live in one storage type. Geometry is a configuration, not an API family:

| | Square | Hex |
|---|---|---|
| **Storage** | `CellGrid2D<'T>` | `CellGrid2D<'T>` (hex geometry) |
| **Build** | `CellGrid2D.create` | `CellGrid2D.createHex` |
| **Cursor** | `GridSection2D<'T>` | `GridSection2D<'T>` |
| **World Space** | `Vector2` | `Vector2` (staggered rows or columns) |
| **Spatial queries** | `Grid2DSpatial` | `Hex2DSpatial` |

The 3D grid family is obsolete (`CellGrid3D`, `HexGrid3D`, `Layout3D`, the hex-3D modules, the layered grids, and the grid renderers). Author 3D levels as a 2D grid with per-column height — the heightmap approach: walls become height-2 columns, ramps become stepped heights. `BoundingBox`, the general culling type from `Mibo.Layout3D`, stays.

## Common Patterns

Both engines share the same design patterns:

### Stamps

A **stamp** is a function that transforms a section:

```fsharp
type Stamp2D<'T> = GridSection2D<'T> -> GridSection2D<'T>
```

(In the examples below, `room`, `center`, `section`, `fill` and friends are the sample's own named stamps and layout functions.)

Stamps compose with `>>` (function composition: the output of the left stamp feeds into the right one):

```fsharp
let myStructure =
    room 10 8 floor wall
    >> center 2 2 (treasureChest)
    >> section 8 4 (torchStand)
```

### Scoping

Create nested sections for relative positioning:

```fsharp
section |> Layout.section 5 3 (fun inner ->
    // (0, 0) maps to (5, 3) in parent
    inner |> fill 0 0 4 4 content
)
```

### DSL Pipeline

Operations return the section for fluent chaining:

```fsharp
let grid =
    CellGrid2D.create 100 50 cellSize origin
    |> Layout.run (fun section ->
        section
        |> fill 0 0 100 50 floor
        |> border 0 0 100 50 wall
        |> set 50 25 chest
    )
```

## Content Types

Grids are generic - you define what each cell contains:

```fsharp
type Tile =
    | Floor of TileType
    | Wall of WallType
    | Prop of PropType
    | Spawn of EntityType

let myGrid = CellGrid2D.create 100 50 cellSize origin

// 3D levels: same 2D storage, height carried per column
type Column =
    { Kind: BlockKind
      Height: int }

let myLevel = CellGrid2D.create 100 50 cellSize origin
```

## World Position Conversion

Convert grid coordinates to world space for rendering:

```fsharp
let worldPos = CellGrid2D.getWorldPos x y grid  // Vector2
```

## Performance

Both engines use zero-cost abstractions:

- **Flat array storage**: O(1) access via index calculation
- **Struct voption**: No heap allocation for empty cells
- **Inline lambdas**: Zero closure allocation for DSL functions
- **In-place mutation**: operations write into the shared backing array; sections are zero-copy views into it
- **Zero-copy sections**: Sections are lightweight views into the backing grid

## Iteration

Iterate over populated cells for rendering:

```fsharp
grid |> CellGrid2D.iter (fun x y tile ->
    let worldPos = CellGrid2D.getWorldPos x y grid
    renderTile worldPos tile
)
```

## Domain Modules

The pre-built stamp libraries are obsolete — their vocabulary is subsumed by Flow styles, and the pages remain as pattern references:

- **[Platformer](2d/platformer.html)** - Boxes, platforms, ledges, walls, pillars, stairs, slopes, pits (obsolete: use Flow styles)
- **[TopDown](2d/topdown.html)** - Rooms, corridors, wall segments, doorways (obsolete: use Flow styles)
- **[Hex Grid](2d/hex.html)** - Hexagonal tile layouts for strategy and tactics games (rewritten for unified hex storage)

The 3D stamp pages ([Interior](3d/interior.html), [Terrain](3d/terrain.html), [3D Hex](3d/hex.html)) cover obsolete modules; author 3D as 2D plus column height.

## Rendering Integration

### 3D Grids

The `CellGridRenderer3D` / `HexGrid3DRenderer` modules and their instanced-draw buffer members are obsolete with the rest of the 3D grid family. `InstancedRenderContext` renders footprint grids directly — `RenderInstanced` / `RenderWindowInstanced`, or `buffer.renderFootprintInstanced(...)` in the Draw DSL — scaling each column by its height; see the [v6 migration guide](../../migration-to-v6.html).

### 2D Grids

2D grids don't need dedicated renderer modules; use `iterVisible` directly (square and hex — the window is orientation-aware):

```fsharp
grid |> CellGrid2D.iterVisible left top right bottom (fun x y tile ->
    let pos = CellGrid2D.getWorldPos x y grid
    // render at pos
)
```

## Choosing Between 2D and 3D

Use **2D Layout** for:
- Side-scrolling platformers
- Top-down RPGs and roguelikes
- Isometric games (3D projection, 2D layout)
- Tile-based puzzle games

Author **3D as 2D plus column height** for:
- First-person shooters (2D footprint, per-column height and content kind)
- Dungeon crawlers
- Outdoor exploration games

Use **Hex geometry** for:
- Strategy and tactics games
- Wargames and board game adaptations
- Games where 6-directional adjacency matters

## Getting Started

- **[Flow - Level Authoring](2d/flow.html)** - The authoring DSL; start here
- **[Migrating to Mibo v6](../../migration-to-v6.html)** - Old-to-new mapping for the retired hex, 3D, layered, and stamp APIs
- **[2D Layout Engine](2d/core.html)** - Grid storage and the pixel-perfect `Layout` pipelines
- **[Hex Grid Layout (2D)](2d/hex.html)** - Hexagonal 2D layouts with adjacency, pathfinding, and strategy game patterns
- **[Platformer Stamps](2d/platformer.html)** - 2D platformer patterns (obsolete surface)
- **[TopDown Stamps](2d/topdown.html)** - 2D top-down patterns (obsolete surface)
- **[3D Layout Engine](3d/core.html)** - Obsolete 3D concepts and DSL
- **[Interior Stamps](3d/interior.html)** - Obsolete 3D interior examples
- **[Terrain Stamps](3d/terrain.html)** - Obsolete 3D terrain examples
- **[Hex Grid Layout (3D)](3d/hex.html)** - Obsolete hex column 3D layouts
