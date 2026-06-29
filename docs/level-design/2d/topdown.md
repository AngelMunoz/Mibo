---
title: Building Top-Down Levels
category: Level Design
categoryindex: 2
index: 23
---

# Building Top-Down Levels

Top-down games (RPGs, roguelikes, dungeon crawlers) are defined by connected spaces: rooms, corridors, and the flow between them. The `TopDown` module provides stamps for designing these efficiently.

## Importing

```fsharp
open Mibo.Layout
```

## Core Top-Down Patterns

### The Basic Room

Every top-down level starts with rooms:

```fsharp
let singleRoom =
    CellGrid2D.create 20 15 (Vector2(32f, 32f)) Vector2.Zero
    |> Layout.run (fun section ->
        section
            |> TopDown.room 12 8 FloorTile WallTile
            |> Layout.section 6 3 (Layout.set 0 0 ChestTile)
    )
```

### Connecting Rooms with Corridors

Most levels are sequences of rooms connected by corridors:

```fsharp
let connectedRooms =
    section
    // Room 1 (spawn)
    |> Layout.section 0 0 (TopDown.room 8 8 FloorTile WallTile)
    |> Layout.section 4 4 (Layout.set 0 0 SpawnTile)
    
    // Corridor to Room 2
    |> Layout.section 8 3 (TopDown.corridor 6 3 TopDown.Horizontal FloorTile WallTile)
    
    // Room 2 (small encounter)
    |> Layout.section 14 0 (TopDown.room 8 6 FloorTile WallTile)
    |> Layout.section 16 3 (Layout.set 0 0 EnemyTile)
    
    // Corridor to Room 3
    |> Layout.section 22 3 (TopDown.corridor 6 3 TopDown.Horizontal FloorTile WallTile)
    
    // Room 3 (treasure)
    |> Layout.section 28 0 (TopDown.room 8 8 FloorTile WallTile)
    |> Layout.section 32 4 (Layout.set 0 0 ChestTile)
```

### Multi-Exit Rooms

Hubs connect to multiple areas:

```fsharp
let hubRoom =
    section
    |> TopDown.room 10 10 FloorTile WallTile
    // North exit
    |> Layout.clear 4 0 2 1
    // South exit
    |> Layout.clear 4 9 2 1
    // West exit
    |> Layout.clear 0 4 1 2
    // East exit
    |> Layout.clear 9 4 1 2
    // Central marker
    |> Layout.section 4 4 (Layout.set 1 1 HubIconTile)
```

### Using Doorways

The `doorway` stamp creates walls with gaps, perfect for room entrances:

```fsharp
let roomWithDoors =
    section
    |> TopDown.room 12 10 FloorTile WallTile
    // Add doorways instead of manually clearing
    |> Layout.section 5 0 (TopDown.doorway 3 WallTile)  // North entrance
    |> Layout.section 5 9 (TopDown.doorway 3 WallTile)  // South entrance
```

**When to use doorway vs manual clearing:**
- `doorway`: Quick, symmetrical entrances (3-tile gap, centered)
- Manual clearing: Asymmetric or multiple exits of varying widths

## Building Dungeon Layouts

### Linear Dungeon

Rooms in a straight line:

```fsharp
let linearDungeon =
    section
    |> Layout.section 0 0 (TopDown.room 8 8 FloorTile WallTile)
    |> Layout.section 8 3 (TopDown.corridor 6 3 TopDown.Horizontal FloorTile WallTile)
    |> Layout.section 14 0 (TopDown.room 8 8 FloorTile WallTile)
    |> Layout.section 22 3 (TopDown.corridor 6 3 TopDown.Horizontal FloorTile WallTile)
    |> Layout.section 28 0 (TopDown.room 8 8 FloorTile WallTile)
```

### Branching Dungeon

Rooms that branch into multiple paths:

```fsharp
let branchingDungeon =
    section
    // Central hub
    |> Layout.section 10 10 (TopDown.room 10 10 FloorTile WallTile)
    |> Layout.clear 4 0 2 1  // North
    |> Layout.clear 4 9 2 1  // South
    |> Layout.clear 0 4 1 2  // West
    |> Layout.clear 9 4 1 2  // East
    
    // North branch (treasure)
    |> Layout.section 12 0 (TopDown.corridor 4 3 TopDown.Vertical FloorTile WallTile)
    |> Layout.section 12 0 (TopDown.room 6 6 FloorTile WallTile)
    |> Layout.section 14 3 (Layout.set 0 0 ChestTile)
    
    // South branch (enemies)
    |> Layout.section 12 14 (TopDown.corridor 4 3 TopDown.Vertical FloorTile WallTile)
    |> Layout.section 12 16 (TopDown.room 6 6 FloorTile WallTile)
    |> Layout.section 13 18 (Layout.set 0 0 EnemyTile)
    |> Layout.section 15 18 (Layout.set 0 0 EnemyTile)
    
    // West branch (dead end)
    |> Layout.section 0 12 (TopDown.corridor 4 3 TopDown.Horizontal FloorTile WallTile)
    |> Layout.section 0 12 (TopDown.room 6 6 FloorTile WallTile)
    
    // East branch (boss)
    |> Layout.section 24 12 (TopDown.corridor 4 3 TopDown.Horizontal FloorTile WallTile)
    |> Layout.section 24 12 (TopDown.room 8 8 FloorTile WallTile)
    |> Layout.section 28 16 (Layout.set 0 0 BossTile)
```

### Floor Transitions

Stairs at room edges indicate floor changes:

```fsharp
let floorTransition =
    section
    // Floor 1 room
    |> Layout.section 0 0 (TopDown.room 10 10 FloorTile WallTile)
    
    // Stairs down (clear wall for visual)
    |> Layout.section 4 9 (Layout.clear 2 1 1 1)
    |> Layout.section 4 9 (Layout.set 2 1 StairsDownTile)
    
    // Corridor (floor 2)
    |> Layout.section 0 14 (TopDown.corridor 4 3 TopDown.Vertical FloorTile WallTile)
    
    // Floor 2 room
    |> Layout.section 0 14 (TopDown.room 10 10 FloorTile WallTile)
    
    // Stairs up
    |> Layout.section 4 14 (Layout.clear 2 1 1 1)
    |> Layout.section 4 14 (Layout.set 2 1 StairsUpTile)
```

## Decorative Elements

### Adding Furniture

Place interactive or decorative objects in rooms:

```fsharp
let furnishedRoom =
    section
    |> TopDown.room 12 10 FloorTile WallTile
    
    // Table
    |> Layout.section 4 3 (Layout.fill 1 0 4 2 TableTile)
    
    // Chairs
    |> Layout.section 3 5 (Layout.set 0 0 ChairTile)
    |> Layout.section 8 5 (Layout.set 0 0 ChairTile)
    
    // Rug
    |> Layout.section 5 6 (Layout.fill 0 0 4 3 RugTile)
    
    // Bookshelf
    |> Layout.section 2 1 (Layout.fill 0 0 2 1 BookshelfTile)
```

### Torch and Light Sources

Visual markers for important areas:

```fsharp
let litCorridor =
    section
    |> TopDown.corridor 12 3 TopDown.Horizontal FloorTile WallTile
    
    // Torches at regular intervals
    |> Layout.section 3 0 (Layout.set 0 0 TorchTile)
    |> Layout.section 7 0 (Layout.set 0 0 TorchTile)
    |> Layout.section 11 0 (Layout.set 0 0 TorchTile)
```

## Building Custom Stamps

Encapsulate common room patterns:

```fsharp
module MyDungeon =
    /// A room with a treasure in center
    let treasureRoom width height =
        TopDown.room width height FloorTile WallTile
        >> Layout.center 1 1 (Layout.set 0 0 ChestTile)
    
    /// A corridor with torches on both walls
    let litCorridor length =
        TopDown.corridor length 3 TopDown.Horizontal FloorTile WallTile
        >> Layout.set 2 0 TorchTile
        >> Layout.set (length - 3) 0 TorchTile
    
    /// A guard room with enemies
    let guardRoom width height enemyCount =
        TopDown.room width height FloorTile WallTile
        >> Layout.scatter enemyCount 42 EnemySpawnTile
    
    /// A L-shaped room
    let lRoom w1 w2 height =
        TopDown.room w1 height FloorTile WallTile
        >> Layout.section (w1 - 1) (height / 2) (
            TopDown.room w2 (height / 2) FloorTile WallTile
        )
```

### Using Custom Stamps

```fsharp
let customDungeon =
    section
    |> Layout.section 0 0 (MyDungeon.treasureRoom 10 10)
    |> Layout.section 12 3 (MyDungeon.litCorridor 6)
    |> Layout.section 18 0 (MyDungeon.guardRoom 8 8 3)
```

## Complete Roguelike Floor Example

```fsharp
let roguelikeFloor section =
    section
    // === FLOOR 1: Spawn Area ===
    // Spawn room
    |> Layout.section 2 2 (TopDown.room 8 8 FloorTile WallTile)
    |> Layout.section 6 6 (Layout.set 0 0 SpawnTile)
    
    // Corridor with traps
    |> Layout.section 10 5 (TopDown.corridor 8 3 TopDown.Horizontal FloorTile WallTile)
    |> Layout.section 12 5 (Layout.set 0 0 TrapTile)
    |> Layout.section 16 5 (Layout.set 0 0 TrapTile)
    
    // Combat room
    |> Layout.section 18 2 (TopDown.room 10 8 FloorTile WallTile)
    |> Layout.section 23 6 (Layout.set 0 0 EnemyTile)
    |> Layout.section 24 5 (Layout.set 0 0 EnemyTile)
    
    // === FLOOR 2: Puzzle Area ===
    // Corridor south
    |> Layout.section 22 10 (TopDown.corridor 6 3 TopDown.Vertical FloorTile WallTile)
    
    // Puzzle room (key door, switch, etc.)
    |> Layout.section 20 14 (TopDown.room 12 10 FloorTile WallTile)
    
    // Key (blocked by door)
    |> Layout.section 30 16 (Layout.set 0 0 KeyTile)
    |> Layout.section 25 15 (Layout.fill 0 0 2 1 DoorTile)
    
    // Switch
    |> Layout.section 21 18 (Layout.set 0 0 SwitchTile)
    
    // === FLOOR 3: Boss Area ===
    // Long approach corridor
    |> Layout.section 22 24 (TopDown.corridor 8 3 TopDown.Horizontal FloorTile WallTile)
    |> Layout.section 24 24 (Layout.set 0 0 EliteEnemyTile)
    
    // Boss arena
    |> Layout.section 30 24 (TopDown.room 14 12 FloorTile WallTile)
    |> Layout.section 36 30 (Layout.set 0 0 BossTile)
    
    // === EXITS ===
    // Stairs to next floor
    |> Layout.section 36 28 (Layout.set 0 0 StairsDownTile)

let floor =
    CellGrid2D.create 50 40 (Vector2(32f, 32f)) Vector2.Zero
    |> Layout.run roguelikeFloor
```

## Multi-Layer Top-Down

Use `LayeredGrid2D` for separate structure, decoration, and entity layers:

```fsharp
let layeredDungeon =
    LayeredGrid2D.create 50 50 (Vector2(32f, 32f)) Vector2.Zero
    |> LayeredLayout.layer 0 (fun section ->
        // Layer 0: Structure (collision)
        section
        |> Layout.section 5 5 (TopDown.room 10 10 FloorTile WallTile)
        |> Layout.section 15 7 (TopDown.corridor 10 3 TopDown.Horizontal FloorTile WallTile)
        |> Layout.section 25 5 (TopDown.room 8 10 FloorTile WallTile)
    )
    |> LayeredLayout.layer 1 (fun section ->
        // Layer 1: Decorations
        section
        |> Layout.section 7 7 (Layout.set 0 0 RugTile)
        |> Layout.section 12 8 (Layout.set 0 0 TorchTile)
        |> Layout.set 27 7 TableTile
        |> Layout.set 29 7 ChairTile
    )
    |> LayeredLayout.layer 2 (fun section ->
        // Layer 2: Entities (different type)
        section
        |> Layout.set 10 10 PlayerSpawn
        |> Layout.set 28 10 EnemySpawn
    )
```

**When to use layers:**
- Structure (walls, floors) on layer 0
- Decorations (torches, furniture) on layer 1
- Entities (spawns, interactables) on layer 2
- Foreground elements on layer 3

## Common Top-Down Patterns

### The "Combat Arena"

Central room with enemies and loot:

```fsharp
let combatArena =
    section
    |> TopDown.room 14 12 FloorTile WallTile
    
    // Cover pillars
    |> Layout.section 4 3 (Layout.fill 0 0 2 2 PillarTile)
    |> Layout.section 10 7 (Layout.fill 0 0 2 2 PillarTile)
    
    // Enemy spawns
    |> Layout.section 3 5 (Layout.set 0 0 EnemyTile)
    |> Layout.section 12 5 (Layout.set 0 0 EnemyTile)
    |> Layout.section 7 10 (Layout.set 0 0 EnemyTile)
    
    // Treasure
    |> Layout.section 6 6 (Layout.set 0 0 ChestTile)
```

### The "Trap Corridor"

Long corridor with periodic hazards:

```fsharp
let trapCorridor =
    section
    |> TopDown.corridor 16 3 TopDown.Horizontal FloorTile WallTile
    
    // Traps every 4 tiles
    |> Layout.section 4 1 (Layout.set 0 0 TrapTile)
    |> Layout.section 8 1 (Layout.set 0 0 TrapTile)
    |> Layout.section 12 1 (Layout.set 0 0 TrapTile)
```

### The "Treasure Vault"

Room with multiple loot containers:

```fsharp
let treasureVault =
    section
    |> TopDown.room 10 10 FloorTile WallTile
    
    // Central chest
    |> Layout.section 4 4 (Layout.set 0 0 ChestTile)
    
    // Wall chests
    |> Layout.section 1 4 (Layout.set 0 0 SmallChestTile)
    |> Layout.section 8 4 (Layout.set 0 0 SmallChestTile)
    |> Layout.section 4 1 (Layout.set 0 0 SmallChestTile)
    |> Layout.section 4 8 (Layout.set 0 0 SmallChestTile)
```

## Design Tips

### Flow and Connectivity

- **Linear**: Good for tutorials, boss fights, story progression
- **Branching**: Good for exploration, multiple paths, replayability
- **Loops**: Good for shortcuts, backtracking prevention

### Room Variation

Mix room sizes and shapes:
- **Small (6x6)**: Fights, loot rooms, transitions
- **Medium (10x10)**: Combat arenas, puzzle rooms
- **Large (15x15)**: Boss rooms, hub areas, markets

### Visibility and Line of Sight

If your game uses line of sight:
- Place walls to create choke points
- Use pillars for partial cover
- Position enemies at corners for tactical advantage

### Balance

Test your spawns:
- Are enemies visible from spawn points?
- Can players reach all loot safely?
- Are there unfair choke points?

> **See also:** [2D Layout Engine](core.html) for complete TopDown module documentation
