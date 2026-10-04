---
title: HexLayout (2D, retired)
index: 6
---

> **⚠ Archived V5-and-below docs.** These pages cover the retired APIs and
> the earlier releases. They are frozen. The current docs live at the
> [site root](../../index.html); the V6 map guides start at
> [Level Design Overview](../../level-design/overview.html).

# HexLayout (2D, retired)

The `HexGrid` and `HexLayout` modules are obsolete compatibility surfaces.
They still compile and delegate to the unified grid, but new code uses
`CellGrid2D` with hex geometry plus [Flow](../../level-design/code-first.html).
This page keeps the retired reference for existing levels.

> **Obsolete surface.** `HexGrid.create` and every `HexLayout.*` call emit
> deprecation warnings. Author new maps with
> [Code-first maps](../../level-design/code-first.html) and the
> [Hex Grids](../../level-design/hex.html) page.

## The retired pipelines

The `HexLayout` module (sections, per-op painting) still compiles and works
exactly as before — the square `Layout` ops also run on hex storage for
pixel-perfect control:

```fsharp
// obsolete surface, kept for reference
let grid =
    CellGrid2D.createHex {
  Orientation = HexOrientation.PointyTop
  Width = 30
  Height = 20
  Radius = 32f
  Origin = Vector2.Zero
}
    |> HexLayout.run (fun section ->
        section
        |> HexLayout.fill 0 0 30 20 GrassTile
        |> HexLayout.border 0 0 30 20 WaterTile
        |> HexLayout.set 15 10 CastleTile
    )
```

## Sections: relative positioning

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

Sections are zero-copy: they don't allocate new grids. They're windows into the same backing data.

## Structural helpers

```fsharp
// Shrink section by N on all sides (useful for borders)
section |> HexLayout.padding 2 (fun inner ->
    inner |> HexLayout.fill 0 0 8 6 GrassTile  // 8x6 inside a 12x10 section
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

## Primitives: placing content

### Single cells and lines

```fsharp
// Single cell
HexLayout.set col row content section

// Horizontal line
HexLayout.repeatX col row count content section

// Vertical line
HexLayout.repeatY col row count content section
```

### Filled regions

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

### Conditional placement

```fsharp
// Only set if cell is empty (won't overwrite existing content)
HexLayout.setIfEmpty col row content section
```

## Geometry: lines, circles, polygons

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

## Patterns: decorative and procedural

### Checkerboard

```fsharp
// Full checkerboard pattern
HexLayout.checker LightGrass DarkGrass section

// Checkerboard on border only
HexLayout.checkerBorder col row width height LightStone DarkStone section
```

### Random scatter

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

### Procedural generation

```fsharp
// Generate content from a function
HexLayout.generate col row width height (fun c r ->
    if (c + r) % 3 = 0 then DenseForest else SparseForest
) section
```

### Find and replace

```fsharp
// Replace all instances of one content type
HexLayout.replace GrassTile SnowTile section

// Probabilistic replace (30% chance per cell)
HexLayout.replaceScatter GrassTile SnowTile 0.3f 42 section
```

### Iteration and transformation

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

## Building stamps: reusable components

A **stamp** is a function `HexGridSection<'T> -> HexGridSection<'T>`. Build your level design vocabulary by creating stamps for common patterns.

### Simple stamp

```fsharp
/// A small campfire clearing
let campfire (section: HexGridSection<Tile>) =
    section
    |> HexLayout.fill 0 0 3 3 GrassTile
    |> HexLayout.set 1 1 CampfireTile
    |> HexLayout.set 0 1 LogTile
    |> HexLayout.set 2 1 LogTile
```

### Parameterized stamp

```fsharp
/// A configurable watchtower
let stackMidTiles height midTile (section: HexGridSection<Tile>) =
    // layers 1..height-2 form the shaft
    (section, [ 1 .. height - 2 ])
    ||> List.fold (fun s i -> s |> HexLayout.set 1 i midTile)

let watchtower height baseTile midTile topTile (section: HexGridSection<Tile>) =
    section
    |> HexLayout.set 1 0 baseTile
    |> stackMidTiles height midTile
    |> HexLayout.set 1 (height - 1) topTile
```

### Composing stamps

Stamps compose with `>>` (function composition):

```fsharp
let outpost =
    watchtower 6 StoneBase StoneMid TorchTop
    >> HexLayout.section 0 7 campfire
    >> HexLayout.border 0 0 4 8 FenceTile

// note: negative coordinates are clipped, so a border starting at -1
// would silently drop its top and left edges
```

### Domain modules

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

## Layered composition

> **Obsolete surface.** `LayeredHexGrid` and `LayeredHexLayout` are
> retired. A layered grid is a dictionary of grids, and game code can
> own the dictionary:

```fsharp
open System.Collections.Generic

let layers = Dictionary<int, CellGrid2D<Tile>>()

let hexLayer index paint =
    let grid =
        match Dictionary.tryGetValue index layers with
        | ValueSome grid -> grid
        | ValueNone ->
            let grid =
                CellGrid2D.createHex {
                    Orientation = HexOrientation.PointyTop
                    Width = 20
                    Height = 15
                    Radius = 32f
                    Origin = Vector2.Zero
                }
            layers.[index] <- grid
            grid

    Layout.run paint grid

hexLayer 0 (fun s -> s |> Layout.fill 0 0 20 15 GrassTile) |> ignore
```

## Complete example: strategy map

> **This example runs on the obsolete compatibility surface.**
> `HexGrid.create` and every `HexLayout.*` call below emit deprecation
> warnings. It remains as a porting reference.

```fsharp
type TerrainType =
    | Grass | Forest | Mountain | Water | Sand | Road

// Named stamps: each region is a reusable function
let baseTerrain = HexLayout.fill 0 0 30 20 GrassTile

let mountainRange (s: HexGridSection<TerrainType>) =
    s |> HexLayout.fill 0 0 8 3 MountainTile
      |> HexLayout.scatter 5 42 ForestTile

let river = HexLayout.line 0 10 29 15 WaterTile

let forestCluster seed (s: HexGridSection<TerrainType>) =
    s |> HexLayout.scatter 15 seed ForestTile

let road a b c d = HexLayout.line a b c d RoadTile

let settlement x y w h tile =
    HexLayout.section x y (HexLayout.fill 0 0 w h tile)

let castle x y =
    HexLayout.section x y (
        HexLayout.fill 0 0 2 2 CastleTile
        >> HexLayout.border 0 0 4 4 WallTile
    )

let desert (s: HexGridSection<TerrainType>) =
    s |> HexLayout.fill 0 0 6 4 SandTile
      |> HexLayout.scatter 3 301 CactusTile

let strategyMap =
    HexGrid.create 30 20 32f Vector2.Zero FlatTop
    |> HexLayout.run (fun section ->
        section
        |> baseTerrain
        |> HexLayout.section 10 5 (mountainRange)
        |> river
        |> HexLayout.section 2 2 (forestCluster 101)
        |> HexLayout.section 20 12 (forestCluster 202)
        |> road 5 3 15 10
        |> road 15 10 25 17
        |> castle 5 3
        |> settlement 15 10 3 2 TownTile
        |> settlement 25 17 2 2 VillageTile
        |> HexLayout.section 22 2 desert
    )
```
