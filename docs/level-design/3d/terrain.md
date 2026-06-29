---
title: Building Outdoor Terrain
category: Level Design
categoryindex: 2
index: 26
---

# Building Outdoor Terrain

Outdoor terrain (landscapes, wilderness, open worlds) is defined by natural elevation, paths, and landmarks. The `Terrain` module provides stamps for designing these efficiently.

## Importing

```fsharp
open Mibo.Layout3D
open Mibo.Layout3D.Terrain
```

## Core Terrain Patterns

### Basic Ground Plane

Every outdoor level starts with a ground:

```fsharp
let basicGround =
    CellGrid3D.create 30 10 30 (Vector3(2f, 2f, 2f)) Vector3.Zero
        |> Layout3D.run (fun section ->
            section
            |> Terrain.ground 30 30 GrassCell
        )
```

### Scattered Decorations

Add trees, rocks, and landmarks:

```fsharp
let forestedGround =
    section
    |> Terrain.ground 30 30 GrassCell
    
    // Scattered trees on the ground (Y=0)
    |> Terrain.scatter 15 42 TreeCell
    
    // Scatters trees on an elevated plateau at Y=5
    |> Terrain.scatterAt 5 10 156 TreeCell
    
    // Scatters flowers across a varying hill surface
    |> Terrain.scatterSurface hillHeight 20 789 FlowerCell
```

**Scatter design:**
- `scatter`: Random X, Z at Y=0.
- `scatterAt`: Random X, Z at a specific Y level (ideal for plateaus).
- `scatterSurface`: Random X, Z with Y determined by a height function (ideal for hills).
- `seed`: Any integer (same seed = same pattern every time).
- Use multiple scatter calls with different seeds for varied placement.

### Elevation Changes

Create plateaus (hills) and pits (depressions):

```fsharp
let variedTerrain =
    section
    // Base ground
    |> Terrain.ground 40 40 GrassCell
    
    // Hill/plateau
    |> Layout3D.section 10 0 10 (Terrain.plateau 12 12 5 HillTopCell HillSideCell)
    
    // Depression/crater
    |> Layout3D.section 25 0 25 (Terrain.pit 8 8 3)
    
    // Water in crater
    |> Layout3D.section 27 0 27 (Terrain.ground 4 4 WaterCell)
```

**Elevation tips:**
- `plateau`: Use height of 3-6 cells for hills. Higher plateaus = bigger landmark.
- `pit`: Use depth of 2-4 cells for shallow depressions, 5+ for craters.

### Ramps and Inclines

Gradual height changes for natural-looking terrain:

```fsharp
let rampedTerrain =
    section
    // Base ground
    |> Terrain.ground 20 20 GrassCell
    
    // Ramp up to plateau
    |> Layout3D.section 0 0 10 (Terrain.rampX 10 10 4 RampCell)
    
    // Plateau on hill
    |> Layout3D.section 10 4 0 (Terrain.ground 8 8 GrassCell)
```

**Ramp design:**
- `rise`: 4-6 cells for one story height
- Ramp width/depth should match path width
- Place ramps where terrain changes (road up hill, path to cave)

## Pathways and Navigation

### Simple Paths

Create roads or trails between landmarks:

```fsharp
let simplePath =
    section
    |> Terrain.ground 30 30 GrassCell
    
    // Winding path through terrain
    |> Layout3D.section 0 0 5 (Terrain.path [
        (0, 0, 0)
        (10, 0, 5)
        (20, 0, 10)
        (25, 0, 15)
        (30, 0, 25)
    ] 2 PathCell)
```

### Multi-Path Network

Branching roads between multiple areas:

```fsharp
let pathNetwork =
    section
    |> Terrain.ground 40 40 GrassCell
    
    // Main path (east-west)
    |> Layout3D.section 5 0 20 (Terrain.path [
        (0, 0, 0)
        (30, 0, 0)
    ] 3 PathCell)
    
    // North branch to hill
    |> Layout3D.section 15 0 20 (Terrain.path [
        (0, 0, 0)
        (0, 0, -10)
    ] 2 PathCell)
    
    // South branch to water
    |> Layout3D.section 15 0 20 (Terrain.path [
        (0, 0, 0)
        (0, 0, 10)
    ] 2 PathCell)
```

### Path with Elevation

Ramps integrated with paths:

```fsharp
let elevatedPath =
    section
    |> Terrain.ground 30 30 GrassCell
    
    // Path on ground
    |> Layout3D.section 5 0 10 (Terrain.path [
        (0, 0, 0)
        (15, 0, 0)
    ] 3 PathCell)
    
    // Ramp up to plateau
    |> Layout3D.section 20 0 5 (Terrain.rampZ 5 10 4 RampCell)
    
    // Path on plateau
    |> Layout3D.section 20 4 5 (Terrain.path [
        (0, 0, 0)
        (10, 0, 10)
    ] 3 PathCell)
```

## Procedural Terrain

### Heightmap Functions

Generate terrain from functions:

```fsharp
let hillTerrain =
    section
    // Generate a single hill
    |> Layout3D.section 0 0 0 (fun inner ->
        let hillHeight x z =
            // Distance from hill center at (12, 12)
            let dist = float (sqrt ((x-12)*(x-12) + (z-12)*(z-12)))
            int (6.0 * exp (-dist/30.0))  // 6 cells tall
        inner |> Terrain.heightmap hillHeight GrassCell
    )
```

### Multiple Features

Combine multiple height functions:

```fsharp
let complexTerrain =
    section
    |> Layout3D.section 0 0 0 (fun inner ->
        let terrainHeight x z =
            // Hill 1 at (12, 12)
            let h1 = 6.0 * exp (-((x-12)*(x-12) + (z-12)*(z-12))/30.0)
            
            // Hill 2 at (30, 20)
            let h2 = 4.0 * exp (-((x-30)*(x-30) + (z-20)*(z-20))/40.0)
            
            // Small valley between hills
            let valley = -1.0 * exp (-((x-21)*(x-21) + (z-16)*(z-16))/50.0)
            
            int (h1 + h2 + valley)
        inner |> Terrain.heightmap terrainHeight GrassCell
    )
```

### Layered Terrain

Show different materials at depths (grass on surface, dirt below, stone deep):

```fsharp
let layeredTerrain =
    section
    |> Layout3D.section 0 0 0 (fun inner ->
        let mountainHeight x z =
            let dist = float (sqrt ((x-15)*(x-15) + (z-15)*(z-15)))
            int (10.0 * exp (-dist/20.0))  // 10 cells tall
        inner |> Terrain.layeredHeightmap mountainHeight GrassCell DirtCell 3 StoneCell
    )
```

### Surface Patterns

Apply patterns that follow the terrain's height function:

```fsharp
// Tiled/Chessboard valley
section |> Terrain.checkerSurface valleyHeight StoneCell GrassCell

// Procedural surface detail (e.g., moisture-driven foliage)
section |> Terrain.generateSurface hillHeight (fun x y z ->
    if moisture x z > 0.8 then FlowerCell else GrassCell
)
```

**Layered terrain design:**
- `topLayer`: 1-3 cells of surface material
- `midLayer`: 3-6 cells of subsurface material
- `bottomLayer`: Remaining cells fill with bedrock

## Building Custom Terrain Stamps

Encapsulate common terrain patterns:

```fsharp
module MyTerrain =
    /// A crater with raised rim
    let crater radius rimHeight =
        let craterHeight x z =
            let dist = float (sqrt ((x-radius)*(x-radius) + (z-radius)*(z-radius)))
            if dist < float radius then
                int (-float rimHeight * (1.0 - dist/float radius))  // Depression
            elif dist < float radius + 2.0 then
                rimHeight  // Raised rim
            else
                0
        fun section ->
            let (w, d) = (radius * 2 + 4, radius * 2 + 4)
            section
            |> Layout3D.section 0 0 0 (fun inner ->
                inner |> Terrain.layeredHeightmap craterHeight CraterDirt CraterDirt 2 CraterRock
            )
    
    /// A road with barriers on both sides
    let roadWithBarriers length width roadCell barrierCell =
        fun section ->
            section
            |> Terrain.path [(0, 0, 0); (length-1, 0, 0)] width roadCell
            |> Layout3D.repeatX 0 0 1 length BarrierCell  // Left barrier
            |> Layout3D.repeatX (width-1) 0 1 length BarrierCell  // Right barrier
    
    /// A forest clearing with trees around edges
    let forestClearing width depth treeCount =
        fun section ->
            section
            |> Terrain.ground width depth GrassCell
            |> Terrain.scatter treeCount 42 TreeCell
    
    /// A mountain peak with snow on top
    let mountain width depth maxHeight =
        let mountainHeight x z =
            let dist = float (sqrt ((x-float width/2.0)*(x-float width/2.0) + (z-float depth/2.0)*(z-float depth/2.0)))
            let maxDist = float (min width depth) / 2.0
            int (float maxHeight * (1.0 - dist/maxDist))
        fun section ->
            section
            |> Layout3D.section 0 0 0 (fun inner ->
                inner
                |> Terrain.layeredHeightmap mountainHeight SnowCell RockCell 2 StoneCell
            )
```

### Using Custom Stamps

```fsharp
let customTerrain =
    section
    |> Layout3D.section 5 0 5 (MyTerrain.crater 6 3)
    |> Layout3D.section 20 0 10 (MyTerrain.mountain 20 20 12)
    |> Layout3D.section 5 0 20 (MyTerrain.roadWithBarriers 15 3 RoadCell BarrierCell)
    |> Layout3D.section 35 0 5 (MyTerrain.forestClearing 15 15 20)
```

## Complete Outdoor Area Example

```fsharp
let outdoorArea =
    CellGrid3D.create 50 15 50 (Vector3(2f, 2f, 2f)) Vector3.Zero
        |> Layout3D.run (fun section ->
            section
            // Base terrain with gentle hills
            |> Layout3D.section 0 0 0 (fun inner ->
                let gentleHills x z =
                    let h1 = 5.0 * exp (-((x-15)*(x-15) + (z-15)*(z-15))/60.0)
                    let h2 = 4.0 * exp (-((x-35)*(x-35) + (z-35)*(z-35))/50.0)
                    int (h1 + h2)
                inner |> Terrain.layeredHeightmap gentleHills GrassCell DirtCell 3 StoneCell
            )
            
            // Main road through terrain
            |> Layout3D.section 0 0 20 (Terrain.path [
                (0, 0, 0)
                (49, 0, 25)
                (49, 0, 49)
            ] 4 RoadCell)
            
            // Side path to village
            |> Layout3D.section 15 0 20 (Terrain.path [
                (0, 0, 0)
                (15, 0, 15)
            ] 2 PathCell)
            
            // Hill with village
            |> Layout3D.section 0 4 0 (fun inner ->
                let hillHeight x z =
                    let dist = float (sqrt ((x-8)*(x-8) + (z-8)*(z-8)))
                    int (8.0 * exp (-dist/20.0))
                inner
                |> Terrain.layeredHeightmap hillHeight GrassCell DirtCell 3 StoneCell
            )
            
            // Village buildings (houses)
            |> Layout3D.section 4 8 4 (Layout3D.fill 0 0 0 3 2 3 HouseFloorCell)
            |> Layout3D.section 4 8 8 (Layout3D.fill 0 0 0 3 2 3 HouseFloorCell)
            |> Layout3D.section 12 8 4 (Layout3D.fill 0 0 0 4 2 4 HouseFloorCell)
            
            // Forest clearing
            |> Layout3D.section 30 0 30 (MyTerrain.forestClearing 15 15 40)
            
            // Water/lake area
            |> Layout3D.section 35 0 35 (Terrain.pit 10 10 2)
            |> Layout3D.section 37 0 37 (Terrain.ground 6 6 WaterCell)
            
            // Scattered decor
            |> Terrain.scatter 30 123 TreeCell
            |> Terrain.scatter 50 456 RockCell
        )
```

## Common Terrain Patterns

### The "Rolling Hills"

Gentle, varied elevation without sharp changes:

```fsharp
let rollingHills =
    section
    |> Layout3D.section 0 0 0 (fun inner ->
        let hillHeight x z =
            // Overlapping hills create natural variation
            let h1 = 4.0 * exp (-((x-10)*(x-10) + (z-10)*(z-10))/40.0)
            let h2 = 3.0 * exp (-((x-25)*(x-25) + (z-20)*(z-20))/35.0)
            let h3 = 5.0 * exp (-((x-15)*(x-15) + (z-35)*(z-35))/50.0)
            int (h1 + h2 + h3)
        inner |> Terrain.heightmap hillHeight GrassCell
    )
```

### The "Valley Pass"

Low path between high areas:

```fsharp
let valleyPass =
    section
    // High terrain on both sides
    |> Layout3D.section 0 0 0 (fun inner ->
        let valleyHeight x z =
            // High ridges at edges, low in center
            let distFromCenter = abs (x - 25)
            int (8.0 * exp (-distFromCenter*distFromCenter/200.0))
        inner |> Terrain.heightmap valleyHeight GrassCell
    )
    
    // Path through valley
    |> Layout3D.section 0 0 25 (Terrain.path [
        (0, 0, 0)
        (49, 0, 0)
    ] 3 PathCell)
```

### The "Island"

Land surrounded by water:

```fsharp
let island =
    section
    // Water base
    |> Terrain.ground 40 40 WaterCell
    
    // Island landmass
    |> Layout3D.section 10 0 10 (fun inner ->
        let islandHeight x z =
            let dist = float (sqrt ((x-10)*(x-10) + (z-10)*(z-10)))
            if dist < 15.0 then
                int (4.0 * (1.0 - dist/15.0))  // Slopes up to center
            else
                0
        inner |> Terrain.layeredHeightmap islandHeight GrassCell DirtCell 2 SandCell
    )
```

## Design Tips

### Terrain Naturalness

- **Combine features:** Don't just use one function type. Mix plates, pits, ramps for realism.
- **Smooth transitions:** Use ramps between elevation levels, avoid sudden cliffs.
- **Layer depth:** Use `layeredHeightmap` so hills have proper interior (not hollow).

### Navigation Design

- **Path width:** 2-3 cells for walking, 4-5 for roads.
- **Slope angle:** Rise/run of 1:2 or 1:3 is comfortable. 1:1 is steep climb.
- **Avoid dead ends:** Paths should loop or connect to areas of interest.

### Visual Variety

- **Material changes:** Use different tiles for different elevations (grass low, rock high).
- **Scatter patterns:** Multiple scatter calls with different seeds prevent uniform distribution.
- **Landmark visibility:** Make key areas (hills, islands) visually distinct.

### Performance Considerations

- **Large heightmaps:** For grids 100x100+, consider generating in sections.
- **Complex functions:** Expensive math in height functions can slow generation.
- **Culling:** Use `iterVolume` when rendering to only process visible cells.

> **See also:** [3D Layout Engine](core.html) for complete Terrain module documentation
