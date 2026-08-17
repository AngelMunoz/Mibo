---
title: Hex Grid Layout (3D)
category: Level Design
categoryindex: 8
index: 9
---

# Hex Grid Layout (3D)

3D hex grids extend hexagonal positioning with a vertical axis, creating hex columns instead of flat hexagons. This is the grid for **strategy games with elevation**: Civilization-style maps, wargames with height advantage, or any game where hex adjacency meets terrain height.

## When to Use 3D Hex vs Rect

| Use Case | Rect 3D | Hex 3D |
|----------|---------|--------|
| Voxel worlds (Minecraft-like) | ✅ Axis-aligned | ❌ Gaps between columns |
| FPS dungeon crawlers | ✅ Box rooms | ❌ Awkward walls |
| Strategy with elevation | ❌ No hex adjacency | ✅ Natural fit |
| Wargames with terrain | ❌ Wrong feel | ✅ Authentic |
| Board game ports | ❌ Doesn't match | ✅ Matches rules |
| Civilization-style maps | ❌ Square tiles | ✅ Hex tiles |

**The trade-off:** Hex 3D grids are hexagonal in XZ but rectangular in Y. Each "column" is a stack of hex cells. This gives you hex adjacency for ground movement while keeping vertical stacking simple.

## Core Concepts

The system is built on three primitives:

- `HexGrid3D<'T>`: Storage for hex column data (hex grid + vertical layers)
- `HexGrid3DSection<'T>`: A cursor/view into the grid for relative positioning
- Stamps: Functions that transform sections (`HexGrid3DSection<'T> -> HexGrid3DSection<'T>`)

## Orientation and Axes

Like 2D hex grids, 3D hex supports both orientations via `HexOrientation`:

| Orientation | XZ Shape | Best for |
|---|---|---|
| `PointyTop` | Pointed at top/bottom | Strategy maps, tactical RPGs |
| `FlatTop` | Flat at top/bottom | Isometric-style games, board games |

**Axis Convention:**
- X = width (left/right)
- Z = depth (forward/back): this is the hex grid plane
- Y = height / layers (up/down): this is the vertical stacking axis

```fsharp
open Mibo.Layout3D
open Mibo.Layout
open System.Numerics

// Strategy map with pointy-top hex columns
let grid = HexGrid3D.create 20 10 15 32f 2f Vector3.Zero PointyTop

// Board game with flat-top hex columns
let board = HexGrid3D.create 12 5 10 48f 1.5f Vector3.Zero FlatTop
```

**Parameters:**
- `width`: Number of columns (X axis)
- `height`: Number of vertical layers (Y axis)
- `depth`: Number of rows (Z axis)
- `hexSize`: Radius of hex (center to corner)
- `layerHeight`: World-space height of each vertical layer
- `origin`: World-space origin point
- `orientation`: PointyTop or FlatTop

## Basic Operations

```fsharp
open Mibo.Layout3D
open Mibo.Layout

let grid = HexGrid3D.create 20 10 15 32f 2f Vector3.Zero PointyTop

// Place content at column 5, row 3, layer 2
HexGrid3D.set 5 3 2 myCell grid

// Read content (returns voption: no heap allocation)
match HexGrid3D.get 5 3 2 grid with
| ValueSome cell -> // handle cell
| ValueNone -> // empty

// Remove content
HexGrid3D.clear 5 3 2 grid

// Get world position for rendering
let worldPos = HexGrid3D.getWorldPos 5 3 2 grid  // Vector3
```

## Iteration

### All Cells

```fsharp
grid |> HexGrid3D.iter (fun col row layer cell ->
    let pos = HexGrid3D.getWorldPos col row layer grid
    spawnModel pos cell
)
```

### Volume-Culled (Frustum Culling)

For large worlds, only process cells within a bounding volume:

```fsharp
let bounds = {
    Min = Vector3(cameraX - viewDist, cameraY - viewHeight, cameraZ - viewDist)
    Max = Vector3(cameraX + viewDist, cameraY + viewHeight, cameraZ + viewDist)
}

grid |> HexGrid3D.iterVolume bounds (fun col row layer cell ->
    let pos = HexGrid3D.getWorldPos col row layer grid
    spawnModel pos cell
)
```

**Performance note:** For a 50×20×50 hex grid (50,000 cells), `iterVolume` typically processes only 500-2000 cells. Always use it for gameplay rendering.

## The DSL: Building Levels with Stamps

The `HexLayout3D` module provides a fluent DSL for placing content. All functions return the section for pipeline composition.

### Running the DSL

```fsharp
let grid =
    HexGrid3D.create 30 10 20 32f 2f Vector3.Zero PointyTop
    |> HexLayout3D.run (fun section ->
        section
        |> HexLayout3D.fill 0 0 0 30 10 20 AirCell
        |> HexLayout3D.floorHex 0 0 0 30 20 GrassCell
        |> HexLayout3D.shell 0 0 0 30 10 20 StoneCell
    )
```

### Sections: Relative Positioning

```fsharp
section
|> HexLayout3D.section 5 0 3 (fun inner ->
    // (0, 0, 0) here = (5, 0, 3) in parent
    inner |> HexLayout3D.fill 0 0 0 4 3 4 WoodCell
)
|> HexLayout3D.section 15 0 8 (fun inner ->
    // (0, 0, 0) here = (15, 0, 8) in parent
    inner |> HexLayout3D.fill 0 0 0 3 2 3 StoneCell
)
```

### Structural Helpers

```fsharp
// Shrink by N on all sides
section |> HexLayout3D.padding 2 (fun inner -> ...)

// Explicit padding: left, bottom, back, right, top, front
section |> HexLayout3D.paddingEx 1 1 1 1 1 1 (fun inner -> ...)

// Center a block within the section
section |> HexLayout3D.center 4 3 4 (fun inner -> ...)

// Place stamps along axes with spacing
section |> HexLayout3D.flowX 5 [tower; tower; tower]   // Horizontal row
section |> HexLayout3D.flowY 3 [floor; floor; floor]   // Vertical stack
section |> HexLayout3D.flowZ 4 [house; house; house]   // Depth row
```

## Primitives: Placing Content

### Single Cells and Lines

```fsharp
// Single cell
HexLayout3D.set col row layer content section

// Lines along axes
HexLayout3D.repeatX col row layer count content section  // Along X
HexLayout3D.repeatY col row layer count content section  // Along Y (vertical)
HexLayout3D.repeatZ col row layer count content section  // Along Z

// Alias for vertical column
HexLayout3D.column col row layer height content section
```

### Volumes

```fsharp
// Fill a box volume
HexLayout3D.fill col row layer w h d content section

// Clear a volume
HexLayout3D.clear col row layer w h d section
```

### Planes (Single-Cell Thickness)

```fsharp
// Horizontal floor (XZ plane)
HexLayout3D.floorHex col row layer width depth content section

// Vertical wall (XY plane): faces forward/back
HexLayout3D.wallXY col row layer width height content section

// Vertical wall (YZ plane): faces left/right
HexLayout3D.wallYZ col row layer height depth content section
```

### 3D Shapes

```fsharp
// Hollow box (6 faces)
HexLayout3D.shell col row layer w h d content section

// 12 edges only
HexLayout3D.edges col row layer w h d content section

// Sphere
HexLayout3D.sphere cx cy cz radius true content section   // Filled
HexLayout3D.sphere cx cy cz radius false content section  // Shell only

// Cylinder (Y-aligned)
HexLayout3D.cylinder cx cz layer radius height true content section   // Filled
HexLayout3D.cylinder cx cz layer radius height false content section  // Shell only
```

### Combined Shapes

```fsharp
// Filled box with different border
HexLayout3D.border col row layer w h d borderContent section

// Filled box with border content
HexLayout3D.rect col row layer w h d borderContent fillContent section

// Only the 8 corners
HexLayout3D.corners col row layer w h d content section
```

## Geometry: 3D Lines

```fsharp
// 3D line between two points
HexLayout3D.line x1 y1 z1 x2 y2 z2 content section
```

**When to use:** Roads, tunnels, rivers, laser beams, flight paths: anything that connects two points in 3D space.

## Patterns: Decorative and Procedural

### Checkerboard Patterns

```fsharp
// Full 3D checkerboard
HexLayout3D.checker3D odd even section

// Single hex layer checkerboard
HexLayout3D.checkerHexLayer layer odd even section

// Planar checkers
HexLayout3D.checkerXY row odd even section    // Vertical plane (XY)
HexLayout3D.checkerYZ col odd even section    // Vertical plane (YZ)

// Box skin checker
HexLayout3D.checkerShell col row layer w h d odd even section
```

### Random Scatter

```fsharp
// Scatter in 3D volume
HexLayout3D.scatter3D count seed content section

// Scatter on single hex layer
HexLayout3D.scatterHexLayer layer count seed content section

// Scatter on vertical planes
HexLayout3D.scatterXY row count seed content section
HexLayout3D.scatterYZ col count seed content section

// Scatter on box surface
HexLayout3D.scatterShell col row layer w h d count seed content section

// Scatter on edges
HexLayout3D.scatterEdges col row layer w h d count seed content section

// Scatter on border
HexLayout3D.scatterBorder col row layer w h d count seed content section

// Scatter an entire stamp at random positions
HexLayout3D.scatterStamp count seed stamp section
```

**Seed usage:** Same seed = same pattern (deterministic). Different seeds for variety. Combine multiple scatter calls for layered randomness.

### Procedural Generation

```fsharp
// Generate from 3D function
HexLayout3D.generate col row layer w h d (fun c r l ->
    if l < 3 then StoneCell else AirCell
) section

// Generate on single hex layer
HexLayout3D.generateHexLayer layer (fun c r ->
    if (c + r) % 2 = 0 then GrassTile else DirtTile
) section

// Generate on vertical planes
HexLayout3D.generateXY row (fun c l -> ...) section
HexLayout3D.generateYZ col (fun r l -> ...) section
```

### Find and Replace

```fsharp
// Replace all instances
HexLayout3D.replace oldContent newContent section

// Probabilistic replace
HexLayout3D.replaceScatter oldContent newContent 0.3f seed section
```

### Iteration and Transformation

```fsharp
// Read existing content
HexLayout3D.iter col row layer w h d (fun c r l cell ->
    match cell with
    | ValueSome content -> printfn "Found %A at (%d,%d,%d)" content c r l
    | ValueNone -> ()
) section

// Transform existing content
HexLayout3D.map col row layer w h d (fun cell ->
    match cell with
    | Block age -> Block(age + 1)
    | other -> other
) section

// Conditional set (only if empty)
HexLayout3D.setIfEmpty col row layer content section
```

## Building Stamps: Reusable Components

### Simple Stamp

```fsharp
/// A hex column with grass on top and dirt below
let grassColumn height (section: HexGrid3DSection<Cell>) =
    section
    |> HexLayout3D.column 0 0 0 (height - 1) DirtCell
    |> HexLayout3D.set 0 (height - 1) 0 GrassCell
```

### Parameterized Stamp

```fsharp
/// A configurable house
let house width height depth floor wall roof (section: HexGrid3DSection<Cell>) =
    section
    |> HexLayout3D.floorHex 0 0 0 width depth floor
    |> HexLayout3D.shell 0 0 0 width height depth wall
    |> HexLayout3D.floorHex 0 0 (height - 1) width depth roof
    |> HexLayout3D.clear 1 0 1 (width - 2) (height - 1) (depth - 2)  // Interior
```

### Composing Stamps

```fsharp
let village =
    house 4 3 4 GrassFloor WoodWall ThatchRoof
    >> HexLayout3D.section 6 0 0 (house 3 2 3 GrassFloor StoneWall TileRoof)
    >> HexLayout3D.section 0 0 6 campfire
    >> HexLayout3D.scatterHexLayer 0 8 42 FlowerCell
```

### Domain Modules

```fsharp
module StrategyGame =
    module Terrain =
        let mountain radius height = ...
        let river width = ...
        let forest density = ...
    
    module Structures =
        let castle size = ...
        let barracks = ...
        let watchtower height = ...
    
    module Units =
        let army unitType count = ...
        let formation shape = ...
```

## Elevation: The Key Feature

3D hex grids shine when elevation matters. The vertical axis lets you model terrain height, multi-level structures, and height-based gameplay.

### Heightmap Terrain

```fsharp
/// Create terrain from a height function
let terrainFromHeightmap maxHeight heightFn surfaceContent subsurfaceContent subsurfaceDepth =
    HexLayout3D.generateHexLayer 0 (fun col row ->
        let height = heightFn col row
        // Surface tile at the top
        surfaceContent
    )
    >> fun section ->
        // Fill subsurface layers
        for layer in 0 .. maxHeight - 1 do
            section |> HexLayout3D.generateHexLayer layer (fun col row ->
                let surfaceHeight = heightFn col row
                if layer < surfaceHeight - subsurfaceDepth then
                    subsurfaceContent
                else
                    surfaceContent
            ) |> ignore
        section
```

### Plateaus and Depressions

```fsharp
/// A raised plateau
let plateau width depth height surface side (section: HexGrid3DSection<Cell>) =
    section
    |> HexLayout3D.fill 0 0 0 width height depth side      // Sides
    |> HexLayout3D.floorHex 0 0 (height - 1) width depth surface  // Top

/// A pit/crater
let pit width depth drop (section: HexGrid3DSection<Cell>) =
    section
    |> HexLayout3D.clear 0 0 0 width drop depth  // Remove cells
```

### Ramps and Slopes

```fsharp
/// A ramp going up along X
let rampX length width rise (section: HexGrid3DSection<Cell>) =
    for i in 0 .. length - 1 do
        let layerHeight = int (float rise * float i / float length)
        section |> HexLayout3D.floorHex i 0 layerHeight 1 width RampCell
    section
```

## Adjacency and Pathfinding

Hex adjacency in 3D works in the XZ plane (6 neighbors at each layer), plus vertical connections (up/down).

### Ground Movement (Same Layer)

```fsharp
/// Get 6 hex neighbors at the same layer
let hexNeighbors col row =
    let isOddRow = row % 2 = 1
    if isOddRow then
        [| (col, row - 1); (col + 1, row - 1)
           (col - 1, row); (col + 1, row)
           (col, row + 1); (col + 1, row + 1) |]
    else
        [| (col - 1, row - 1); (col, row - 1)
           (col - 1, row); (col + 1, row)
           (col - 1, row + 1); (col, row + 1) |]

/// Get vertical neighbors (up/down)
let verticalNeighbors col row layer =
    [| (col, row, layer - 1); (col, row, layer + 1) |]
```

### Height-Based Movement Cost

```fsharp
/// Movement cost considering elevation
let movementCost fromCol fromRow fromLayer toCol toRow toLayer grid =
    let fromHeight = getTerrainHeight fromCol fromRow fromLayer grid
    let toHeight = getTerrainHeight toCol toRow toLayer grid
    let heightDiff = abs (toHeight - fromHeight)
    
    // Upward movement costs more
    if heightDiff > 0 then
        1 + heightDiff  // 2 for 1 height, 3 for 2 height, etc.
    else
        1  // Flat or downhill
```

## Layered Composition

For complex maps with multiple data layers:

```fsharp
let level =
    LayeredHexGrid3D.create 30 10 20 32f 2f Vector3.Zero PointyTop
    |> LayeredHexLayout3D.layer 0 (fun section ->
        // Layer 0: Terrain (ground, water, mountains)
        section
        |> HexLayout3D.floorHex 0 0 0 30 20 GrassCell
        |> HexLayout3D.section 10 0 5 (Terrain.mountain 6 8)
        |> HexLayout3D.section 0 0 15 (Terrain.river 3)
    )
    |> LayeredHexLayout3D.layer 1 (fun section ->
        // Layer 1: Structures
        section
        |> HexLayout3D.section 5 0 3 (Structures.castle 8)
        |> HexLayout3D.section 20 0 10 (Structures.barracks)
    )
    |> LayeredHexLayout3D.layer 2 (fun section ->
        // Layer 2: Units
        section
        |> HexLayout3D.section 7 0 5 (Units.army Infantry 10)
        |> HexLayout3D.section 22 0 12 (Units.army Cavalry 5)
    )
```

## Rendering Integration

### Basic Rendering

```fsharp
grid |> HexGrid3D.iter (fun col row layer cell ->
    let pos = HexGrid3D.getWorldPos col row layer grid
    spawnModel pos cell
)
```

### Volume-Culled Rendering

```fsharp
let frustumBounds = {
    Min = Vector3(minX, minY, minZ)
    Max = Vector3(maxX, maxY, maxZ)
}

grid |> HexGrid3D.iterVolume frustumBounds (fun col row layer cell ->
    let pos = HexGrid3D.getWorldPos col row layer grid
    spawnModel pos cell
)
```

### Using the Renderer Helper

```fsharp
open Mibo.Layout3D

// Basic render
HexGrid3DRenderer.render grid (fun worldPos cell ->
    spawnModel worldPos cell
)

// Volume-culled render
HexGrid3DRenderer.renderVolume frustumBounds grid (fun worldPos cell ->
    spawnModel worldPos cell
)

// With indices (for debug or special logic)
HexGrid3DRenderer.renderWithIndices grid (fun col row layer worldPos cell ->
    spawnModel worldPos cell
)
```

### GPU Instanced Rendering

For large maps with many copies of the same mesh (trees, rocks, buildings):

```fsharp
// Create instanced context
let instancedCtx = InstancedRenderContext(
    getKey = fun cell -> cell.MeshKey,           // Group by mesh type
    getMeshesAndMaterial = fun cell -> cell.Meshes,  // Get GPU resources
    getTransform = fun worldPos cell ->            // Compute transform
        Matrix4x4.CreateTranslation(worldPos)
)

// Render with instancing (one draw call per mesh type)
renderBuffer.renderHexGridInstanced(instancedCtx, grid).drop()

// Volume-culled instancing
renderBuffer.renderHexGridVolumeInstanced(instancedCtx, frustumBounds, grid).drop()
```

**When to use instancing:** If you have 100+ copies of the same mesh (trees, rocks, identical buildings), instancing reduces draw calls from 100+ to 1-2. For unique meshes, regular rendering is fine.

## Complete Example: Civilization-Style Map

```fsharp
type Terrain =
    | Grass | Plains | Desert | Tundra | Forest | Mountain | Water

type Structure =
    | City | Fort | Farm | Mine | Road

// Named stamps: each region is a reusable function
let mountainRange (s: HexGrid3DSection<Terrain, Structure>) =
    s |> HexLayout3D.fill 0 0 0 8 5 3 MountainCell
      |> HexLayout3D.floorHex 0 0 4 8 3 SnowCell

let forestPatch seed (s: HexGrid3DSection<Terrain, Structure>) =
    s |> HexLayout3D.scatterHexLayer 0 30 seed ForestCell

let desertRegion (s: HexGrid3DSection<Terrain, Structure>) =
    s |> HexLayout3D.floorHex 0 0 0 8 6 DesertCell
      |> HexLayout3D.scatterHexLayer 0 5 301 OasisCell

let road a b c d e f = HexLayout3D.line a b c d e f RoadCell

let city x z size =
    HexLayout3D.section x 0 z (
        HexLayout3D.fill 0 0 0 size 2 size CityCell
        >> HexLayout3D.border 0 0 0 (size + 1) 3 (size + 1) WallCell  // defensive wall
    )

let civMap =
    HexGrid3D.create 40 8 30 32f 2f Vector3.Zero FlatTop
    |> HexLayout3D.run (fun section ->
        section
        // Base terrain
        |> HexLayout3D.floorHex 0 0 0 40 30 GrassCell

        // Mountain range (barrier)
        |> HexLayout3D.section 15 0 10 mountainRange

        // River (winding)
        |> HexLayout3D.line 0 0 5 39 0 25 WaterCell

        // Forests
        |> HexLayout3D.section 2 0 2 (forestPatch 101)
        |> HexLayout3D.section 28 0 18 (forestPatch 202)

        // Desert region
        |> HexLayout3D.section 30 0 2 desertRegion

        // Tundra (north)
        |> HexLayout3D.floorHex 0 0 0 40 3 TundraCell

        // Roads connecting cities
        |> road 5 0 5 20 0 15
        |> road 20 0 15 35 0 25

        // Cities (small, medium, small)
        |> city 5 5 2
        |> city 20 15 3
        |> city 35 25 2

        // Farms near cities
        |> HexLayout3D.scatterHexLayer 0 10 401 FarmCell

        // Resources
        |> HexLayout3D.scatter3D 15 501 IronCell  // scattered iron deposits
    )
```

## Performance Considerations

- **Flat array storage**: O(1) cell access, cache-friendly iteration
- **Struct voption**: No heap allocation per cell
- **Zero-copy sections**: Sections don't duplicate the grid
- **Inline lambdas**: DSL functions compile away closures
- **Use `iterVolume`**: Always cull for gameplay rendering
- **Use `generate`**: For procedural content, faster than individual `set` calls
- **Use instancing**: For rendering many identical meshes

## API Reference

For the `HexLayout3D`/`HexGrid3D` signatures, see the [3D Layout Engine](core.html) (layout primitives) and the API reference.
