---
title: Building Interior Spaces
category: Level Design
categoryindex: 2
index: 25
---

# Building Interior Spaces

Interior spaces (dungeons, buildings, FPS levels) are defined by enclosed areas connected by passages. The `Interior` module provides stamps for designing these efficiently.

## Importing

```fsharp
open Mibo.Layout3D
open Mibo.Layout3D.Interior
```

## Understanding Directions

The `DoorSide` type specifies which face to operate on:

```fsharp
type DoorSide =
  | North  // Front face (max Z)
  | South  // Back face (min Z)
  | East   // Right face (max X)
  | West   // Left face (min X)
```

## Core Interior Patterns

### Basic Room

Every interior level starts with rooms:

```fsharp
let singleRoom =
    CellGrid3D.create 20 5 20 (Vector3(2f, 2f, 2f)) Vector3.Zero
        |> Layout3D.run (fun section ->
            section
            |> Interior.room 12 4 12 FloorCell WallCell CeilingCell
            |> Layout3D.section 6 2 (Layout3D.set 0 0 0 ChestCell)
        )
```

### Connecting Rooms with Corridors

Corridors connect spaces. Use X-aligned for east-west, Z-aligned for north-south:

```fsharp
let connectedRooms =
    section
    // Room 1 (spawn)
    |> Layout3D.section 0 0 0 (Interior.room 10 4 10 FloorCell WallCell CeilingCell)
    |> Layout3D.section 5 2 0 (Layout3D.set 0 0 0 SpawnCell)
    
    // Corridor to Room 2 (east)
    |> Interior.doorway East 2 3
    |> Layout3D.section 10 0 3 (Interior.corridorX 8 2 3 FloorCell WallCell CeilingCell)
    
    // Room 2 (encounter)
    |> Interior.doorway West 2 3
    |> Layout3D.section 18 0 0 (Interior.room 8 4 8 FloorCell WallCell CeilingCell)
    |> Layout3D.section 22 2 0 (Layout3D.set 0 0 0 EnemyCell)
```

**Door placement tip:** When using `doorway`, the corridor and room Y positions must align. A doorway at Y=0 connects to a corridor starting at Y=1 (door height is 3, so doorway spans Y=1,2,3).

### Multi-Exit Hubs

Central rooms connecting multiple areas:

```fsharp
let hubRoom =
    section
    |> Interior.room 14 4 14 FloorCell WallCell CeilingCell
    
    // Four exits
    |> Interior.doorway North 2 3  // Exit to north area
    |> Interior.doorway South 2 3  // Exit to south area
    |> Interior.doorway East 2 3   // Exit to east area
    |> Interior.doorway West 2 3   // Exit to west area
    
    // Central marker
    |> Layout3D.center 1 1 1 (Layout3D.set 0 0 0 HubIconCell)
```

### Enclosed vs Open-Top Rooms

Use fully enclosed rooms for most interiors, open-top for exterior or rooftop areas:

```fsharp
let mixedRooms =
    section
    // Fully enclosed indoor room
    |> Layout3D.section 0 0 0 (Interior.room 10 4 10 FloorCell WallCell CeilingCell)
    
    // Open-top area (patio, rooftop, exterior room)
    |> Layout3D.section 15 0 0 (Interior.openRoom 8 2 8 FloorCell WallCell)
```

## Adding Architectural Details

### Windows

Windows break up wall surfaces and provide visual interest:

```fsharp
let roomWithWindows =
    section
    |> Interior.room 12 5 12 FloorCell WallCell CeilingCell
    
    // North wall windows
    |> Interior.window North 4 3 2  // Width=4, Height=3, Sill=2
    |> Interior.window North 4 3 2  // Second window (same pattern)
    
    // East wall window
    |> Interior.window East 3 3 2
```

**Window design:**
- `windowWidth`: 2-4 cells for standard windows
- `windowHeight`: 2-3 cells for vertical space
- `sillHeight`: 1-2 cells from floor (eye level)

### Pillars and Columns

Pillars add vertical interest and cover for gameplay:

```fsharp
let pillaredRoom =
    section
    |> Interior.room 14 5 14 FloorCell WallCell CeilingCell
    
    // Four corner pillars
    |> Layout3D.section 2 0 2 (Interior.pillar 4 PillarBase PillarMid PillarTop)
    |> Layout3D.section 10 0 2 (Interior.pillar 4 PillarBase PillarMid PillarTop)
    |> Layout3D.section 2 0 10 (Interior.pillar 4 PillarBase PillarMid PillarTop)
    |> Layout3D.section 10 0 10 (Interior.pillar 4 PillarBase PillarMid PillarTop)
```

**Pillar usage:**
- 2-4 tiles high for visual pillars
- 4-6 tiles high for climbable columns
- Base/middle/top tiles for visual variety

### Vertical Shafts

Shafts provide vertical transport (elevators, ladders):

```fsharp
let elevatorShaft =
    section
    |> Interior.shaft 2 2 12 WallCell  // 2x2 shaft, 12 tiles tall
```

Use shafts between floors with doors opening into them:

```fsharp
let floorWithShaft =
    section
    |> Interior.room 10 5 10 FloorCell WallCell CeilingCell
    
    // Shaft in corner
    |> Layout3D.section 8 0 8 (Interior.shaft 2 2 12 WallCell)
    
    // Door to shaft
    |> Interior.doorway South 2 3
```

## Vertical Traversal

### Staircases

Stairs connect floors smoothly:

```fsharp
let stairSection =
    section
    // Lower room
    |> Layout3D.section 0 0 0 (Interior.room 10 4 10 FloorCell WallCell CeilingCell)
    
    // Stairs going up
    |> Interior.doorway East 2 3
    |> Layout3D.section 10 0 3 (Interior.stairs 2 4 4 StairCell)
    
    // Upper room
    |> Interior.doorway West 2 3
    |> Layout3D.section 18 0 0 (Interior.room 8 4 8 FloorCell WallCell CeilingCell)
```

**Stair design:**
- `width`: 2-3 cells for player comfort
- `rise`: 4-6 cells for one floor
- `run`: 2-3 cells per step for comfortable climbing

### Multi-Level Layouts

Combine rooms, stairs, and balconies:

```fsharp
let multiLevel =
    section
    // Ground floor lobby
    |> Layout3D.section 0 0 0 (Interior.room 12 5 12 FloorCell WallCell CeilingCell)
    |> Interior.doorway East 2 3
    
    // Staircase
    |> Layout3D.section 12 0 4 (Interior.stairs 2 5 5 StairCell)
    
    // Second floor balcony (open-top)
    |> Interior.doorway West 2 3
    |> Layout3D.section 0 5 0 (Interior.openRoom 10 3 12 FloorCell WallCell)
    
    // Windows for exterior view
    |> Interior.window North 4 2 1
    |> Interior.window South 4 2 1
```

## Building Custom Interior Stamps

Encapsulate common room patterns:

```fsharp
module MyInterior =
    /// A room with a door and torches on each wall
    let torchRoom width height depth floor wall ceiling =
        Interior.room width height depth floor wall ceiling
        >> Interior.doorway North 2 3
        >> Interior.doorway South 2 3
        >> Layout3D.set 1 2 0 TorchCell
        >> Layout3D.set (width - 2) 2 0 TorchCell
    
    /// A corridor with torches at both ends
    let litCorridor length width height floor wall ceiling =
        Interior.corridorX length width height floor wall ceiling
        >> Layout3D.set 2 2 0 TorchCell
        >> Layout3D.set (length - 3) 2 0 TorchCell
    
    /// A guard room with weapon racks
    let armory width height depth floor wall ceiling =
        Interior.room width height depth floor wall ceiling
        >> Layout3D.section 1 2 1 (Layout3D.fill 0 0 0 3 1 WeaponRackCell)
        >> Layout3D.section (width - 4) 2 (depth - 2) (Layout3D.fill 0 0 0 3 1 WeaponRackCell)
    
    /// An L-shaped room combination
    let lRoom room1W room1D room2W room2D height floor wall ceiling =
        Layout3D.section 0 0 0 (Interior.room room1W height room1D floor wall ceiling)
        >> Layout3D.section (room1W - 1) 0 0 (Interior.room room2W height room2D floor wall ceiling)
        >> Layout3D.clear (room1W - 1) 0 1 1 height 1
```

### Using Custom Stamps

```fsharp
let customInterior =
    section
    |> Layout3D.section 0 0 0 (MyInterior.torchRoom 12 4 12 FloorCell WallCell CeilingCell)
    |> Layout3D.section 12 0 3 (MyInterior.litCorridor 10 2 3 FloorCell WallCell CeilingCell)
    |> Layout3D.section 22 0 0 (MyInterior.armory 10 5 10 FloorCell WallCell CeilingCell)
```

## Complete FPS Level Example

```fsharp
let levelSection section =
    section
    // === AREA 1: Spawn ===
    // Starting room
    |> Layout3D.section 0 0 0 (Interior.room 10 4 10 FloorCell WallCell CeilingCell)
    |> Interior.doorway East 2 3
    |> Layout3D.section 5 2 0 (Layout3D.set 0 0 0 SpawnCell)
    
    // === AREA 2: Corridor Network ===
    // Main corridor with turns
    |> Layout3D.section 10 0 3 (Interior.corridorX 6 2 3 FloorCell WallCell CeilingCell)
    
    // Side room (ammo pickup)
    |> Layout3D.section 10 0 0 (Interior.room 4 3 4 FloorCell WallCell CeilingCell)
    |> Layout3D.set 12 1 1 AmmoCell
    
    // Continue corridor
    |> Layout3D.section 14 0 3 (Interior.corridorX 6 2 3 FloorCell WallCell CeilingCell)
    
    // === AREA 3: Combat Hall ===
    // Large hall with pillars
    |> Interior.doorway West 2 3
    |> Layout3D.section 20 0 0 (Interior.room 16 5 16 FloorCell WallCell CeilingCell)
    
    // Pillars for cover
    |> Layout3D.section 4 0 4 (Interior.pillar 4 PillarBase PillarMid PillarTop)
    |> Layout3D.section 12 0 4 (Interior.pillar 4 PillarBase PillarMid PillarTop)
    |> Layout3D.section 4 0 12 (Interior.pillar 4 PillarBase PillarMid PillarTop)
    |> Layout3D.section 12 0 12 (Interior.pillar 4 PillarBase PillarMid PillarTop)
    
    // Enemy spawns
    |> Layout3D.section 6 2 4 (Layout3D.set 0 0 0 EnemyCell)
    |> Layout3D.section 14 2 10 (Layout3D.set 0 0 0 EnemyCell)
    
    // Exits to other areas
    |> Interior.doorway East 2 4
    |> Interior.doorway South 3 4
    
    // === AREA 4: Upper Balcony ===
    // Stairs up
    |> Layout3D.section 20 0 2 (Interior.stairs 2 5 5 StairCell)
    
    // Balcony (open-top for exterior feel)
    |> Layout3D.section 20 5 0 (Interior.openRoom 12 3 16 FloorCell WallCell)
    
    // Windows looking down into hall
    |> Interior.window North 4 2 1
    |> Interior.window South 4 2 1
    
    // Treasure
    |> Layout3D.section 28 6 8 (Layout3D.set 0 0 0 ChestCell)
    
    // === AREA 5: Exit ===
    // Shaft to next level
    |> Layout3D.section 0 0 0 (Interior.shaft 2 2 10 WallCell)
    |> Interior.doorway South 2 3

let level =
    CellGrid3D.create 40 10 20 (Vector3(2f, 2f, 2f)) Vector3.Zero
        |> Layout3D.run levelSection
```

## Common Interior Patterns

### The "Tunnel Run"

Narrow corridor with no rooms, long linear passage:

```fsharp
let tunnelRun =
    section
    |> Interior.corridorX 20 2 3 FloorCell WallCell CeilingCell
```

Use for:
- Introductions
- Tension-building sections
- Connecting distant areas

### The "Hub and Spokes"

Central room with 4 radiating corridors:

```fsharp
let hubAndSpokes =
    section
    // Central hub
    |> Interior.room 10 4 10 FloorCell WallCell CeilingCell
    |> Interior.doorway North 2 3
    |> Interior.doorway South 2 3
    |> Interior.doorway East 2 3
    |> Interior.doorway West 2 3
    
    // North spoke (treasure)
    |> Layout3D.section 4 0 4 (Interior.corridorZ 6 2 3 FloorCell WallCell CeilingCell)
    |> Layout3D.section 4 0 0 (Interior.room 6 4 6 FloorCell WallCell CeilingCell)
    
    // South spoke (enemies)
    |> Layout3D.section 4 0 10 (Interior.corridorZ 6 2 3 FloorCell WallCell CeilingCell)
    |> Layout3D.section 4 0 10 (Interior.room 6 4 6 FloorCell WallCell CeilingCell)
```

### The "Multi-Floor Building"

Three floors connected by stairs and shaft:

```fsharp
let multiFloor =
    section
    // Floor 1: Lobby
    |> Layout3D.section 0 0 0 (Interior.room 10 4 10 FloorCell WallCell CeilingCell)
    
    // Stairs to floor 2
    |> Layout3D.section 10 0 3 (Interior.stairs 2 5 5 StairCell)
    
    // Floor 2: Offices
    |> Interior.doorway West 2 3
    |> Layout3D.section 0 5 0 (Interior.room 10 4 10 FloorCell WallCell CeilingCell)
    
    // Shaft to floor 3
    |> Layout3D.section 8 0 8 (Interior.shaft 2 2 5 WallCell)
    
    // Floor 3: Rooftop
    |> Layout3D.section 0 10 0 (Interior.openRoom 10 2 10 FloorCell WallCell)
```

## Design Tips

### Spatial Awareness

- **Room size matters:** Large rooms feel grand but reduce tension. Small rooms feel claustrophobic but increase intensity.
- **Corridor width:** 2 cells for tight passages, 3-4 for comfortable movement.
- **Ceiling height:** 3-4 cells is standard for human-scale. 2 cells feels cramped, 5+ feels cavernous.

### Flow and Navigation

- **Doors should be obvious:** Place doors on wall centers, mark with different tiles.
- **Avoid dead ends:** Every area should lead somewhere (unless it's a deliberate trap).
- **Provide shortcuts:** Players appreciate backtracking prevention (stairs between floors, hidden passages).

### Visual Clarity

- **Distinguish surfaces:** Different tiles for floors, walls, ceilings helps orientation.
- **Mark exits:** Doors, stairs, and shafts should be visually distinct.
- **Light sources:** Torches, lamps, or windows break up monotony.

### Playtesting

- **Enemy placement test:** Can enemies reach players? Are there safe spots?
- **Line of sight:** Check that corners provide actual cover.
- **Door usability:** Do doors block movement awkwardly? Are they wide enough?

> **See also:** [3D Layout Engine](core.html) for complete Interior module documentation
