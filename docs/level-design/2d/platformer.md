---
title: Building Platformer Levels
category: Level Design
categoryindex: 2
index: 22
---

# Building Platformer Levels

Platformer games revolve around spatial challenges: jumps, gaps, vertical traversal, and hazards. The `Platformer` module provides stamps that make designing these challenges quick and composable.

## Importing

```fsharp
open Mibo.Layout
```

## Core Platformer Patterns

### The Basic Level Structure

Most platformer levels start with a foundation - ground, walls, and platforms:

```fsharp
let basicLevel =
    CellGrid2D.create 100 20 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
    |> Layout.run (fun section ->
        section
        // Ground floor
        |> Layout.fill 0 18 100 2 GroundTile
        
        // Back wall
        |> Layout.fill 0 0 2 20 WallTile
        
        // Floating platforms to jump between
        |> Layout.section 15 12 (Platformer.platform 8 PlatformTile)
        |> Layout.section 30 10 (Platformer.platform 6 PlatformTile)
        |> Layout.section 42 8 (Platformer.platform 6 PlatformTile)
    )
```

### Creating Jump Challenges

Players need to jump across gaps of varying difficulty. The `pit` and `platform` stamps are your primary tools:

```fsharp
let jumpSection =
    section
    // Start platform
    |> Layout.section 5 12 (Platformer.platform 6 PlatformTile)
    
    // Gap to jump across (clears floor cells)
    |> Layout.section 11 18 (Platformer.pit 4 2)
    
    // Landing platform
    |> Layout.section 15 14 (Platformer.platform 4 PlatformTile)
    
    // Wider, lower gap
    |> Layout.section 19 16 (Platformer.pit 6 4)
    
    // Final landing
    |> Layout.section 25 12 (Platformer.platform 8 PlatformTile)
```

**Design tip:** Wider gaps with lower landing platforms are more challenging. Vary the Y position of platforms to create engaging vertical gameplay.

### Vertical Traversal with Stairs and Ledges

Stairs provide controlled ascent while ledges create one-way platforms:

```fsharp
let verticalSection =
    section
    // Staircase going up and right
    |> Layout.section 0 10 (Platformer.stairs 8 StepTile Platformer.UpRight)
    
    // Ledge players can drop through
    |> Layout.section 12 4 (Platformer.ledge 4 Platformer.Right PlatformTile)
    
    // Platform to catch landing
    |> Layout.section 20 8 (Platformer.platform 6 PlatformTile)
    
    // Stairs going down
    |> Layout.section 30 4 (Platformer.stairs 6 StepTile Platformer.DownRight)
```

**Use cases:**
- **Stairs (`stairs`)**: Use when players need to go up or down multiple tiles in a controlled manner
- **Ledges (`ledge`)**: Create one-way barriers or platforms players can drop through

### Sloped Surfaces

Slopes create smooth, gradual height changes:

```fsharp
let slopeSection =
    section
    // Slope down from elevated platform
    |> Layout.section 0 6 (Platformer.slope 12 6 SlopeTile Platformer.DownRight)
    
    // Flat area after slope
    |> Layout.section 12 12 (Platformer.platform 8 PlatformTile)
    
    // Slope up (ramp to next area)
    |> Layout.section 20 12 (Platformer.slope 10 8 SlopeTile Platformer.UpLeft)
```

**Slope design:**
- Use moderate slopes (rise/run of 1:2 or 1:3) for comfortable play
- Steep slopes (1:1) create slip-down mechanics if you implement them
- Always provide flat landing areas after slopes

## Building Enclosed Areas

### Box Rooms

Fully enclosed rooms with walls, floor, and ceiling:

```fsharp
let boxRoom =
    section
    |> Layout.section 10 5 (Platformer.box 16 12 WallTile FloorTile)
    // Add platforms inside
    |> Layout.section 13 8 (Platformer.platform 4 PlatformTile)
    |> Layout.section 18 9 (Platformer.platform 4 PlatformTile)
```

### Combining Boxes and Pits

Create complex obstacle courses:

```fsharp
let obstacleCourse =
    section
    // Starting area (box with floor)
    |> Layout.section 5 10 (Platformer.box 10 6 WallTile FloorTile)
    
    // Challenging jump section
    |> Layout.section 15 14 (Platformer.pit 6 6)
    
    // Small landing platform
    |> Layout.section 21 14 (Platformer.platform 3 PlatformTile)
    
    // Hazardous pit (spikes at bottom)
    |> Layout.section 24 14 (Platformer.pit 4 8)
    |> Layout.section 25 18 (Layout.fill 0 0 4 2 SpikeTile)
    
    // Safe landing area
    |> Layout.section 28 10 (Platformer.box 8 8 WallTile FloorTile)
```

## Decorative Elements

### Pillars and Columns

Pillars add visual interest and can serve as hiding spots or climbable surfaces:

```fsharp
let decoratedRoom =
    section
    |> Layout.fill 0 10 20 10 FloorTile
    |> Layout.fill 0 0 2 10 WallTile
    
    // Decorative pillars
    |> Layout.section 5 10 (Platformer.pillar 6 PillarBase PillarMid PillarTop)
    |> Layout.section 10 10 (Platformer.pillar 6 PillarBase PillarMid PillarTop)
    |> Layout.section 15 10 (Platformer.pillar 6 PillarBase PillarMid PillarTop)
```

**Pillar usage:**
- 2-3 tiles high for visual pillars
- 4-6 tiles high for climbable columns (if you implement climbing)
- Vary the base/middle/top tiles for variety

## Building Custom Stamps

Encapsulate common patterns in reusable stamps:

```fsharp
module MyPlatformer =
    /// A checkpoint platform with a flag
    let checkpoint width =
        Platformer.platform width PlatformTile
        >> Layout.set (width / 2) 1 FlagTile
    
    /// A pit with hazards at the bottom
    let hazardPit width depth hazardCount =
        Platformer.pit width depth
        >> Layout.section 0 (depth - 1) (Layout.repeatX 0 0 hazardCount SpikeTile)
    
    /// A floating platform with supports
    let floatingPlatform width =
        Platformer.platform width PlatformTile
        >> Layout.set 0 1 SupportTile
        >> Layout.set (width - 1) 1 SupportTile
    
    /// A room with a treasure chest
    let treasureRoom width height =
        Platformer.box width height WallTile FloorTile
        >> Layout.center 1 1 (Layout.set 0 0 ChestTile)
```

### Using Custom Stamps

```fsharp
let customLevel =
    section
    |> Layout.section 5 10 MyPlatformer.checkpoint 4
    |> Layout.section 15 12 (MyPlatformer.hazardPit 6 8 4)
    |> Layout.section 25 10 (MyPlatformer.floatingPlatform 6)
    |> Layout.section 35 8 (MyPlatformer.treasureRoom 10 8)
```

## Multi-Layer Platformer

Use `LayeredGrid2D` for separate collision, decoration, and entity layers:

```fsharp
let layeredPlatformer =
    LayeredGrid2D.create 100 25 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
    |> LayeredLayout.layer 0 (fun section ->
        // Layer 0: Collision (walls, platforms, hazards)
        section
        |> Layout.fill 0 20 100 5 GroundTile
        
        // Platforms
        |> Layout.section 5 12 (Platformer.platform 10 PlatformTile)
        |> Layout.section 20 10 (Platformer.platform 8 PlatformTile)
        |> Layout.section 35 8 (Platformer.platform 6 PlatformTile)
        
        // Hazardous pit
        |> Layout.section 42 14 (Platformer.pit 6 8)
        |> Layout.section 43 18 (Layout.fill 0 0 6 2 SpikeTile)
        
        // Enclosed challenge room
        |> Layout.section 50 8 (Platformer.box 15 12 WallTile FloorTile)
        |> Layout.section 55 10 (Platformer.platform 4 PlatformTile)
        |> Layout.section 62 10 (Platformer.platform 4 PlatformTile)
    )
    |> LayeredLayout.layer 1 (fun section ->
        // Layer 1: Decorations (torches, flags, markers)
        section
        |> Layout.section 8 11 (Layout.set 0 0 TorchTile)
        |> Layout.section 24 9 (Layout.set 0 0 TorchTile)
        |> Layout.section 40 7 (Layout.set 0 0 TorchTile)
        |> Layout.section 55 11 (Layout.set 0 0 FlagTile)
        |> Layout.section 75 15 (Layout.set 0 0 ExitSignTile)
        
        // Rug in challenge room
        |> Layout.section 55 15 (Layout.fill 1 1 10 4 RugTile)
    )
    |> LayeredLayout.layer 2 (fun section ->
        // Layer 2: Entities (spawns, interactables)
        section
        |> Layout.section 8 16 (Layout.set 0 0 SpawnTile)
        |> Layout.section 55 14 (Layout.set 0 0 ChestTile)
        |> Layout.section 63 12 (Layout.set 0 0 EnemyTile)
        |> Layout.section 65 12 (Layout.set 0 0 EnemyTile)
        |> Layout.section 75 18 (Layout.set 0 0 NextLevelTriggerTile)
    )
```

**When to use layers in platformer games:**
- **Layer 0**: Solid collision (walls, floors, platforms, spikes)
- **Layer 1**: Non-colliding decorations (torches, flags, particles)
- **Layer 2**: Entities and triggers (player spawns, enemies, checkpoints)

This separation allows you to:
- Render decorations without affecting collision
- Clear entity layer between level transitions
- Different tile types per layer without conflicts

## Complete Level Example

Here's a full level showcasing multiple patterns:

```fsharp
let levelSection section =
    section
    // === AREA 1: Start ===
    // Spawn room
    |> Layout.fill 0 18 20 2 GroundTile
    |> Layout.fill 0 10 2 10 WallTile
    |> Layout.section 2 12 (Layout.set 0 0 SpawnTile)
    
    // === AREA 2: Basic Jumps ===
    // Series of platforms
    |> Layout.section 20 14 (Platformer.platform 5 PlatformTile)
    |> Layout.section 25 12 (Platformer.platform 4 PlatformTile)
    |> Layout.section 29 10 (Platformer.platform 4 PlatformTile)
    
    // === AREA 3: Vertical Ascent ===
    // Staircase
    |> Layout.section 33 6 (Platformer.stairs 8 StepTile Platformer.UpRight)
    
    // Ledge section
    |> Layout.section 45 4 (Platformer.ledge 6 Platformer.Right PlatformTile)
    |> Layout.section 55 4 (Platformer.platform 6 PlatformTile)
    
    // === AREA 4: Hazardous Descent ===
    // Slope down
    |> Layout.section 63 6 (Platformer.slope 10 6 SlopeTile Platformer.DownRight)
    
    // Pit with spikes
    |> Layout.section 73 12 (Platformer.pit 4 8)
    |> Layout.section 74 18 (Layout.fill 0 0 4 2 SpikeTile)
    
    // === AREA 5: Enclosed Challenge ===
    // Box room with internal platforms
    |> Layout.section 77 2 (Platformer.box 12 10 WallTile FloorTile)
    |> Layout.section 80 4 (Platformer.platform 3 PlatformTile)
    |> Layout.section 85 6 (Platformer.platform 3 PlatformTile)
    
    // Pillar decorations
    |> Layout.section 80 2 (Platformer.pillar 8 PillarBase PillarMid PillarTop)
    |> Layout.section 86 2 (Platformer.pillar 8 PillarBase PillarMid PillarTop)
    
    // Treasure
    |> Layout.section 83 5 (Layout.set 0 0 ChestTile)

let level =
    CellGrid2D.create 100 20 (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
    |> Layout.run levelSection
```

## Common Platformer Level Patterns

### The "Gauntlet" Run

Platforms get smaller and further apart:

```fsharp
let gauntletSection =
    section
    |> Layout.section 5 14 (Platformer.platform 8 PlatformTile)
    |> Layout.section 13 12 (Platformer.platform 6 PlatformTile)
    |> Layout.section 19 10 (Platformer.platform 4 PlatformTile)
    |> Layout.section 23 8 (Platformer.platform 3 PlatformTile)
    |> Layout.section 26 6 (Platformer.platform 2 PlatformTile)
```

### The "Climb and Fall" Sequence

Staircase up followed by a controlled drop:

```fsharp
let climbAndFall =
    section
    |> Layout.section 0 10 (Platformer.stairs 10 StepTile Platformer.UpRight)
    |> Layout.section 12 2 (Platformer.ledge 4 Platformer.Left PlatformTile)
    |> Layout.section 20 6 (Platformer.platform 6 PlatformTile)
    |> Layout.section 26 10 (Platformer.stairs 8 StepTile Platformer.DownRight)
```

### The "Hazard Valley"

Pits with hazards between safe platforms:

```fsharp
let hazardValley =
    section
    |> Layout.section 5 14 (Platformer.platform 6 PlatformTile)
    |> Layout.section 11 14 (Platformer.pit 4 8)
    |> Layout.section 12 18 (Layout.fill 0 0 4 2 SpikeTile)
    |> Layout.section 15 12 (Platformer.platform 5 PlatformTile)
    |> Layout.section 20 12 (Platformer.pit 4 8)
    |> Layout.section 21 18 (Layout.fill 0 0 4 2 SpikeTile)
    |> Layout.section 25 14 (Platformer.platform 6 PlatformTile)
```

## Design Tips

### Difficulty Progression

Start with easy jumps and gradually increase challenge:
- Early levels: Wider platforms, smaller gaps
- Mid levels: Mix of slopes, smaller platforms
- Late levels: Hazardous gaps, vertical sections

### Visual Clarity

Make hazards and playable areas visually distinct:
- Use different tiles for hazardous pits
- Add visual markers for spawn points and checkpoints
- Use consistent wall/floor colors for readability

### Playtesting

Always playtest your sections:
- Are jumps too wide/narrow?
- Are hazard pits fair or frustrating?
- Is the level flow obvious to players?

> **See also:** [2D Layout Engine](core.html) for complete Platformer module documentation
