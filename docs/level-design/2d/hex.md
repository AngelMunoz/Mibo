---
title: Hex Grid Layout (2D)
category: Level Design
categoryindex: 6
index: 5
---

# Hex Grid Layout (2D)

Hex grids trade the simplicity of rectangles for **equal-distance neighbors** and more natural-looking terrain. If your game needs 6-directional movement, strategy-map aesthetics, or organic-looking levels, hexes are the right tool.

## When to Use Hex vs Rect

| Use Case | Rect Grid | Hex Grid |
|----------|-----------|----------|
| Platformers, top-down RPGs | ✅ Natural fit | ❌ Awkward edges |
| Strategy / tactics games | ❌ Diagonal advantage | ✅ Equal neighbors |
| Wargames, board game ports | ❌ Looks wrong | ✅ Authentic |
| Procedural terrain | ❌ Grid lines show | ✅ Organic feel |
| 4/8-directional movement | ✅ Built-in | ❌ Needs adaptation |
| 6-directional movement | ❌ Impossible | ✅ Natural |

**The trade-off:** Hex grids are harder to align with screen edges and rectangular sprites. If your game is tile-based with axis-aligned art, stick with rect. If adjacency or aesthetics matter more, hex wins.

## Orientation: Pointy vs Flat Top

Hexes come in two rotations. The choice affects both visuals and coordinate math:

```
    Pointy Top              Flat Top
      /\                    ______
     /  \                  /      \
    /    \                /        \
    \    /                \        /
     \  /                  \      /
      \/                    ------
```

**When to pick which:**
- **Pointy top** — Strategy maps, tactical RPGs. Rows align horizontally, natural for "marching" armies left-to-right.
- **Flat top** — Isometric-style games, board game adaptations. Columns align vertically, natural for scrolling maps.

```fsharp
open Mibo.Layout

// Strategy map with pointy-top hexes
let grid = HexGrid.create 20 15 32f Vector2.Zero PointyTop

// Isometric board with flat-top hexes
let board = HexGrid.create 12 10 48f Vector2.Zero FlatTop
```

The `size` parameter is the radius of the hex (center to corner). A `size` of 32f gives you hexes roughly 56px wide (pointy) or 64px wide (flat).

## Coordinate System

Hex grids use **offset coordinates** — a standard column/row pair, but with a visual offset to make hexes tessellate:

```
Pointy-top:      Flat-top:
  0 1 2 3          0 1 2
0 . . . .        0 . . .
1  . . . .       1 . . .
2 . . . .        2 . . .
3  . . . .       3 . . .
```

Odd rows (pointy-top) or odd columns (flat-top) are shifted by half a hex width. This is handled automatically by `getWorldPos` — you don't need to think about it when placing content.

**What this means for you:** Use `(col, row)` to address cells. The visual offset is a rendering concern, not a logical one.

## Basic Operations

```fsharp
open Mibo.Layout
open System.Numerics

let grid = HexGrid.create 20 15 32f Vector2.Zero PointyTop

// Place content
HexGrid.set 5 3 myTile grid

// Read content (returns voption — no heap allocation)
match HexGrid.get 5 3 grid with
| ValueSome tile -> // handle tile
| ValueNone -> // empty cell

// Remove content
HexGrid.clear 5 3 grid

// Get world position for rendering
let worldPos = HexGrid.getWorldPos 5 3 grid  // Vector2
```

## Iteration

### All Cells

```fsharp
// Process every populated cell
grid |> HexGrid.iter (fun col row tile ->
    let pos = HexGrid.getWorldPos col row grid
    renderTile pos tile
)
```

### Visible Only (Frustum Culling)

For large maps, you only want to process cells on screen. `iterVisible` takes screen-space bounds and skips off-screen hexes:

```fsharp
// Screen bounds in world coordinates
let screenLeft = cameraX - viewportWidth / 2f
let screenTop = cameraY - viewportHeight / 2f
let screenRight = cameraX + viewportWidth / 2f
let screenBottom = cameraY + viewportHeight / 2f

grid |> HexGrid.iterVisible screenLeft screenTop screenRight screenBottom
    (fun col row tile ->
        let pos = HexGrid.getWorldPos col row grid
        renderTile pos tile
    )
```

**Performance note:** For a 100×100 hex grid, `iterVisible` typically processes only 50-200 cells instead of 10,000. Always use it for gameplay rendering.

## The DSL: Building Levels with Stamps

The `HexLayout` module lets you build levels declaratively. Think of it as painting content onto the grid using composable functions.

### Running the DSL

```fsharp
let grid =
    HexGrid.create 30 20 32f Vector2.Zero PointyTop
    |> HexLayout.run (fun section ->
        section
        |> HexLayout.fill 0 0 30 20 GrassTile
        |> HexLayout.border 0 0 30 20 WaterTile
        |> HexLayout.set 15 10 CastleTile
    )
```

### Sections: Relative Positioning

Sections let you work in local coordinates. A section at `(5, 3)` means "start from column 5, row 3 in the parent":

```fsharp
section
|> HexLayout.section 5 3 (fun inner ->
    // (0, 0) here = (5, 3) in parent
    inner |> HexLayout.fill 0 0 4 3 ForestTile
)
|> HexLayout.section 12 8 (fun inner ->
    // (0, 0) here = (12, 8) in parent
    inner |> HexLayout.fill 0 0 3 3 MountainTile
)
```

Sections are zero-copy — they don't allocate new grids. They're just windows into the same backing data.

### Structural Helpers

```fsharp
// Shrink section by N on all sides (useful for borders)
section |> HexLayout.padding 2 (fun inner ->
    inner |> HexLayout.fill 0 0 8 6 GrassTile  // 8×6 inside a 12×10 section
)

// Explicit padding per side: left, top, right, bottom
section |> HexLayout.paddingEx 1 2 1 2 (fun inner -> ...)

// Center a fixed-size block within the section
section |> HexLayout.center 4 4 (fun inner ->
    inner |> HexLayout.set 2 2 ThroneTile  // Centered in parent
)

// Place stamps in a row with spacing
section |> HexLayout.flowX 5 [
    tower 3 5
    tower 3 5
    tower 3 5
]

// Place stamps in a column with spacing
section |> HexLayout.flowY 4 [
    platform 6
    platform 6
    platform 6
]
```

## Primitives: Placing Content

### Single Cells and Lines

```fsharp
// Single cell
HexLayout.set col row content section

// Horizontal line
HexLayout.repeatX col row count content section

// Vertical line
HexLayout.repeatY col row count content section
```

### Filled Regions

```fsharp
// Fill a rectangle
HexLayout.fill col row width height content section

// Hollow rectangle (border only)
HexLayout.border col row width height content section

// Filled rectangle with different border
HexLayout.rect col row width height borderContent fillContent section

// Only the four corners
HexLayout.corners col row width height content section

// Clear a region
HexLayout.clear col row width height section
```

### Conditional Placement

```fsharp
// Only set if cell is empty (won't overwrite existing content)
HexLayout.setIfEmpty col row content section
```

## Geometry: Lines, Circles, Polygons

These work in grid coordinates, not world space. They're useful for marking paths, areas of effect, or procedural shapes.

```fsharp
// Line between two hexes (Bresenham's algorithm)
HexLayout.line 2 3 18 12 PathTile section

// Circle (midpoint algorithm)
HexLayout.circle 10 8 5 true AreaTile section   // Filled circle
HexLayout.circle 10 8 5 false BorderTile section // Ring only

// Arbitrary polygon (vertex list in grid coordinates)
let vertices = [| struct(5, 2); struct(12, 2); struct(15, 8); struct(8, 10); struct(2, 6) |]
HexLayout.polygon vertices true ZoneTile section   // Filled
HexLayout.polygon vertices false EdgeTile section  // Outline only
```

**When to use geometry:**
- **Lines** — Paths, roads, rivers, laser beams
- **Circles** — Area-of-effect markers, tower ranges, blast radii
- **Polygons** — Territory boundaries, influence zones, procedural regions

## Patterns: Decorative and Procedural

### Checkerboard

```fsharp
// Full checkerboard pattern
HexLayout.checker LightGrass DarkGrass section

// Checkerboard on border only
HexLayout.checkerBorder col row width height LightStone DarkStone section
```

### Random Scatter

```fsharp
// Scatter N random tiles (same seed = same pattern every time)
HexLayout.scatter 50 42 FlowerTile section

// Scatter on border only
HexLayout.scatterBorder col row width height 10 42 RockTile section

// Scatter along a line
HexLayout.scatterLine 2 3 18 12 8 42 TreeTile section

// Scatter an entire stamp at random positions
HexLayout.scatterStamp 5 42 (fun s ->
    s |> HexLayout.fill 0 0 2 2 BushTile
) section
```

**Seed usage:** Use the same seed for deterministic results (replay, testing). Use different seeds for variety. Combine multiple scatter calls with different seeds for layered randomness.

### Procedural Generation

```fsharp
// Generate content from a function
HexLayout.generate col row width height (fun c r ->
    if (c + r) % 3 = 0 then DenseForest else SparseForest
) section
```

### Find and Replace

```fsharp
// Replace all instances of one content type
HexLayout.replace GrassTile SnowTile section

// Probabilistic replace (30% chance per cell)
HexLayout.replaceScatter GrassTile SnowTile 0.3f 42 section
```

### Iteration and Transformation

```fsharp
// Read existing content without modifying
HexLayout.iter col row width height (fun c r tile ->
    match tile with
    | ValueSome t -> printfn "Found %A at (%d, %d)" t c r
    | ValueNone -> ()
) section

// Transform existing content
HexLayout.map col row width height (fun tile ->
    match tile with
    | Tree age -> Tree(age + 1)
    | other -> other
) section
```

## Building Stamps: Reusable Components

A **stamp** is a function `HexGridSection<'T> -> HexGridSection<'T>`. Build your level design vocabulary by creating stamps for common patterns.

### Simple Stamp

```fsharp
/// A small campfire clearing
let campfire (section: HexGridSection<Tile>) =
    section
    |> HexLayout.fill 0 0 3 3 GrassTile
    |> HexLayout.set 1 1 CampfireTile
    |> HexLayout.set 0 1 LogTile
    |> HexLayout.set 2 1 LogTile
```

### Parameterized Stamp

```fsharp
/// A configurable watchtower
let watchtower height baseTile midTile topTile (section: HexGridSection<Tile>) =
    section
    |> HexLayout.set 1 0 baseTile
    |> List.init (height - 2) (fun i -> i + 1)
    |> List.fold (fun s i -> s |> HexLayout.set 1 i midTile) section
    |> HexLayout.set 1 (height - 1) topTile
```

### Composing Stamps

Stamps compose with `>>` (function composition):

```fsharp
let outpost =
    watchtower 6 StoneBase StoneMid TorchTop
    >> HexLayout.section 0 7 campfire
    >> HexLayout.border -1 -1 4 8 FenceTile
```

### Domain Modules

Organize stamps into namespaces for your game:

```fsharp
module Fantasy =
    module Structures =
        let house w h = ...
        let tavern = house 8 6 >> interior ...
        let castle w h = ...
    
    module Nature =
        let forestCluster count = ...
        let river width = ...
        let mountain radius = ...
    
    module Combat =
        let coverWall length = ...
        let trench width depth = ...
        let barricade = ...
```

## Adjacency and Pathfinding

Hex grids shine when adjacency matters. Each hex has exactly 6 neighbors (vs 4 or 8 for rects). This makes pathfinding and movement ranges more natural.

### Getting Neighbors

```fsharp
/// Get the 6 neighbor coordinates for a hex
let neighbors col row =
    // For pointy-top hexes
    let isOddRow = row % 2 = 1
    if isOddRow then
        [| (col, row - 1); (col + 1, row - 1)
           (col - 1, row); (col + 1, row)
           (col, row + 1); (col + 1, row + 1) |]
    else
        [| (col - 1, row - 1); (col, row - 1)
           (col - 1, row); (col + 1, row)
           (col - 1, row + 1); (col, row + 1) |]

/// Check if a hex is walkable
let isWalkable col row (grid: HexGrid<Tile>) =
    match HexGrid.get col row grid with
    | ValueSome tile -> tile.IsWalkable
    | ValueNone -> false

/// Get walkable neighbors
let walkableNeighbors col row grid =
    neighbors col row
    |> Array.filter (fun (c, r) -> isWalkable c r grid)
```

### Movement Range

```fsharp
open System.Collections.Generic

/// Find all hexes within N movement steps
let movementRange startCol startRow steps (grid: HexGrid<Tile>) =
    let visited = HashSet<int * int>()
    let current = Queue<int * int * int>()  // col, row, remaining steps
    current.Enqueue(startCol, startRow, steps)
    
    while current.Count > 0 do
        let col, row, remaining = current.Dequeue()
        if remaining >= 0 && not (visited.Contains(col, row)) then
            visited.Add(col, row)
            for nc, nr in neighbors col row do
                if isWalkable nc nr grid then
                    current.Enqueue(nc, nr, remaining - 1)
    
    visited
```

## Layered Composition

For games with multiple visual layers (terrain, objects, fog of war), use `LayeredHexGrid`:

```fsharp
let level =
    LayeredHexGrid.create 20 15 32f Vector2.Zero PointyTop
    |> LayeredHexLayout.layer 0 (fun section ->
        // Layer 0: Base terrain
        section
        |> HexLayout.fill 0 0 20 15 GrassTile
        |> HexLayout.scatter 30 42 ForestTile
    )
    |> LayeredHexLayout.layer 1 (fun section ->
        // Layer 1: Structures and units
        section
        |> HexLayout.set 5 3 CastleTile
        |> HexLayout.set 10 8 BarracksTile
        |> HexLayout.set 15 12 FarmTile
    )
    |> LayeredHexLayout.layer 2 (fun section ->
        // Layer 2: Fog of war (initially all hidden)
        section
        |> HexLayout.fill 0 0 20 15 FogTile
    )
```

**Layer usage patterns:**
- Layer 0: Terrain (grass, water, mountains)
- Layer 1: Structures, resources, units
- Layer 2: Overlays (fog, highlights, movement range)

Layers are created on-demand — empty layers cost nothing.

## Rendering

Hex grids don't have a dedicated 2D renderer module because the iteration pattern is simple enough to use directly:

```fsharp
// Basic rendering
grid |> HexGrid.iter (fun col row tile ->
    let pos = HexGrid.getWorldPos col row grid
    // Draw your sprite/tile at pos
    buffer.Sprite(sprite {
        texture (getTexture tile)
        at pos.X pos.Y
    })
)

// Performance rendering (only visible hexes)
grid |> HexGrid.iterVisible screenLeft screenTop screenRight screenBottom
    (fun col row tile ->
        let pos = HexGrid.getWorldPos col row grid
        buffer.Sprite(sprite {
            texture (getTexture tile)
            at pos.X pos.Y
        })
    )
```

## Complete Example: Strategy Map

```fsharp
type TerrainType =
    | Grass | Forest | Mountain | Water | Sand | Road

let strategyMap =
    HexGrid.create 30 20 32f Vector2.Zero FlatTop
    |> HexLayout.run (fun section ->
        section
        // Base terrain
        |> HexLayout.fill 0 0 30 20 GrassTile
        
        // Mountain range (natural barrier)
        |> HexLayout.section 10 5 (fun s ->
            s |> HexLayout.fill 0 0 8 3 MountainTile
              |> HexLayout.scatter 5 42 ForestTile
        )
        
        // River (winding path)
        |> HexLayout.line 0 10 29 15 WaterTile
        
        // Forest clusters
        |> HexLayout.section 2 2 (fun s -> s |> HexLayout.scatter 15 101 ForestTile)
        |> HexLayout.section 20 12 (fun s -> s |> HexLayout.scatter 20 202 ForestTile)
        
        // Roads connecting settlements
        |> HexLayout.line 5 3 15 10 RoadTile
        |> HexLayout.line 15 10 25 17 RoadTile
        
        // Settlements
        |> HexLayout.section 5 3 (fun s ->
            s |> HexLayout.fill 0 0 2 2 CastleTile
              |> HexLayout.border -1 -1 4 4 WallTile
        )
        |> HexLayout.section 15 10 (fun s ->
            s |> HexLayout.fill 0 0 3 2 TownTile
        )
        |> HexLayout.section 25 17 (fun s ->
            s |> HexLayout.fill 0 0 2 2 VillageTile
        )
        
        // Desert region
        |> HexLayout.section 22 2 (fun s ->
            s |> HexLayout.fill 0 0 6 4 SandTile
              |> HexLayout.scatter 3 301 CactusTile
        )
    )
```

## Performance Considerations

- **Flat array storage** — O(1) cell access, cache-friendly iteration
- **Struct voption** — No heap allocation per cell
- **Zero-copy sections** — Sections don't duplicate the grid
- **Inline lambdas** — DSL functions compile away closures
- **Use `iterVisible`** — Always cull for gameplay rendering
- **Use `generate`** — For procedural content, it's faster than individual `set` calls

## API Reference

For complete function signatures, see the [2D Layout Engine](core.html).
