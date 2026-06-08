# SpaceBattle — Architecture Guide

This document explains how the SpaceBattle sample is structured as an **Elmish application** where `Program.fs` acts as a **message router** that coordinates independent sub-systems.

## The Elmish Loop as a Router

The core Elmish loop is `init → update → view`, driven by messages. In SpaceBattle, `Program.fs` does **not** contain game logic — it routes messages to the appropriate sub-system and translates cross-system events into new messages.

```
  ┌──────────────────────────────────────────────────────────────┐
  │                        Program.fs                            │
  │                                                              │
  │   Msg ──┬──▶ Input.update     ──▶ model.Input  + Cmd<Input>  │
  │         ├──▶ Map.update       ──▶ model.Map                  │
  │         ├──▶ Units.update     ──▶ model.Units  + Cmd<Units>  │
  │         ├──▶ Camera.update    ──▶ model.Cam                  │
  │         ├──▶ Phase.System     ──▶ model.Turn   + Intent      │
  │         ├──▶ AnimState.update ──▶ model.Anim   + Event       │
  │         ├──▶ Effects.update  ──▶ model.Effects               │
  │         └──▶ Tick (per frame) ──▶ camera, anim, decorations  │
  │                                                              │
  │   Intent ──▶ translate to Cmd<Msg> for other systems         │
  │   Event  ──▶ translate to Cmd<Msg> for other systems         │
  └──────────────────────────────────────────────────────────────┘
```

Each system owns its **model**, **message type**, **update function**, and **view function**. The main `Msg` type wraps all sub-messages:

```fsharp
type Msg =
  | InputMsg      of InputMsg
  | MapMsg        of MapMsg
  | UnitsMsg      of UnitsMsg
  | CameraMsg     of CameraMsg
  | PhaseMsg      of Phase.PhaseMsg
  | AnimationMsg  of AnimationMsg
  | Tick          of GameTime
  | PreStartMsg   of PreStartMsg
  | RestartGame
  | EvaluateAI
```

## Systems

| System          | File                     | Owns                                        | Messages                 | Purpose                                                |
| --------------- | ------------------------ | ------------------------------------------- | ------------------------ | ------------------------------------------------------ |
| **Input**       | `Input.fs`               | `InputModel` (selection, hover, held keys)  | `InputMsg`               | Mouse/keyboard input, selection state                  |
| **Camera**      | `Camera.fs`              | `CameraModel` (Camera2D)                    | `CameraMsg`              | Zoom, movement, map clamping                           |
| **Map**         | `Map.fs`                 | `MapModel` (grid, reachable, visible, path) | `MapMsg`                 | Hex grid, pathfinding, reachable cells, fog visibility |
| **Units**       | `Units.fs`               | `Map<cell, SBUnit>`                         | `UnitsMsg`               | Unit data, move/damage/direction                       |
| **Phase**       | `Phase.fs`               | `Turn`, `TurnOrder`                         | `PhaseMsg` → `Intent`    | Turn management, action resolution                     |
| **AI**          | `AI.fs`                  | (pure functions)                            | —                        | AI decision tree, class-weighted scoring               |
| **AnimState**   | `AnimState.fs`           | `AnimationState`                            | `AnimationMsg` → `Event` | Movement/attack tween, banners                         |
| **Selection**   | `Selection.fs`           | (pure functions)                            | —                        | Move range, path computation, simplification           |
| **PreStart**    | `PreStart.fs`            | `PreStartState` (player slots)              | `PreStartMsg`            | Pre-game player configuration screen                   |
| **UI**          | `UI.fs`                  | (pure functions)                            | —                        | HP bars, action indicators, info overlays              |
| **Shaders**     | `Shaders.fs`             | `SkyboxModel`                               | —                        | Skybox rendering                                       |
| **Decorations** | `AnimatedDecorations.fs` | `Map<cell, AnimatedSprite>`                 | —                        | Animated background sprites                            |
| **Effects**     | `Effects.fs`             | `EffectState` (particles, lights, flashes)  | —                        | Laser trail/impact particles, point lights             |
| **FogOfWar**    | `FogOfWar.fs`            | `FogState` (shader)                         | —                        | Shader-based fog of war with space dust                |

## Cross-System Communication

Systems never call each other directly. Instead, they communicate through **Intents** and **Events** that `Program.fs` intercepts and translates into messages for other systems.

### Intents (Phase → Program → Other Systems)

`Phase.System.update` returns an `Intent` — a declarative description of what should happen. `Program.fs` translates each intent into commands for the relevant systems:

```
Phase.Intent.PerformMove     ──▶  AnimationMsg.StartMove  +  InputMsg.ClearSelection
Phase.Intent.PerformAttack   ──▶  AnimationMsg.StartAttack + UnitsMsg.UpdateDirection + InputMsg.ClearSelection
Phase.Intent.MoveResolved    ──▶  UnitsMsg.MoveUnit
Phase.Intent.AttackResolved  ──▶  UnitsMsg.AttackUnit
Phase.Intent.StartTransition ──▶  AnimationMsg.ShowBanner (turn transition)
Phase.Intent.SwitchSelection ──▶  InputMsg.SelectCell
Phase.Intent.ClearSelection  ──▶  InputMsg.ClearSelection
```

The Phase system **never knows** about animations or input — it just declares intent.

### Events (AnimState → Program → Other Systems)

`AnimState.update` returns an `AnimationEvent` when something significant happens. `Program.fs` translates events into commands:

```
AnimationEvent.MoveComplete       ──▶  PhaseMsg.Resolution
AnimationEvent.AttackComplete     ──▶  PhaseMsg.Resolution
AnimationEvent.SegmentChanged     ──▶  UnitsMsg.UpdateDirection
AnimationEvent.TransitionComplete ──▶  PhaseMsg.TransitionDone
AnimationEvent.BannerComplete     ──▶  (currently unused)
```

The animation system **never knows** about units or phases — it just emits events.

### Input → Phase

Input events are intercepted at `Program.fs` level and forwarded:

```
InputMsg.CellClicked  ──▶  PhaseMsg.CellClicked   (forwarded to Phase)
InputMsg.CalculateRange ──▶ MapMsg.RecalculateRange (forwarded to Map)
```

## Message Flow: A Complete Move

Here's the full lifecycle of a unit move, showing how messages flow through the system:

```
1. User clicks a reachable hex cell
   │
   ▼
2. InputMsg(MouseAction(Select cell))
   │  Program.fs forwards:
   ▼
3. PhaseMsg(CellClicked cell)
   │  Phase determines this is a valid move, returns Intent.PerformMove
   │  Program.fs translates intent:
   │
   ├─▶ UnitsMsg(UpdateDirection(from, dir))    ← set initial facing
   ├─▶ AnimationMsg(StartMove(...))             ← begin tween
   └─▶ InputMsg(ClearSelection)                 ← deselect unit
   │
   ▼
4. Tick (every frame)
   │  AnimState.update advances Progress
   │  If segment boundary crossed:
   │    ──▶ AnimationEvent.SegmentChanged(dir)
   │    ──▶ Program.fs emits UnitsMsg(UpdateDirection(from, dir))
   │  When Progress >= 1.0:
   │    ──▶ AnimationEvent.MoveComplete
   │    ──▶ Program.fs emits PhaseMsg(Resolution)
   │
   ▼
5. PhaseMsg(Resolution)
   │  Phase resolves pending move, returns Intent.MoveResolved
   │  Program.fs translates:
   │
   └─▶ UnitsMsg(MoveUnit(src, dest))           ← move unit data
   │
   ▼
6. UnitsMsg(MoveUnit)
   │  Unit moved in model.Units
   │  Program.fs emits:
   │
    └─▶ MapMsg(RecalculateRange)                ← refresh reachable cells
```

## Message Flow: A Complete Attack

The attack lifecycle mirrors the move flow — Phase declares intent, animation plays, then resolution applies damage:

```
1. User clicks an enemy unit in attack range
   │
   ▼
2. InputMsg(MouseAction(Select cell))
   │  Program.fs forwards:
   ▼
3. PhaseMsg(CellClicked cell)
   │  Phase determines this is a valid attack, returns Intent.PerformAttack
   │  Program.fs translates intent:
   │
   ├─▶ UnitsMsg(UpdateDirection(cell, dir))     ← face the target
   ├─▶ AnimationMsg(StartAttack(...))            ← begin laser tween
   └─▶ InputMsg(ClearSelection)                  ← deselect unit
   │
   ▼
4. Tick (every frame)
   │  AnimState.update advances Progress
   │  Effects.update fades particles and impact flashes
   │  If attacking: Effects.spawnTrail at laser position
   │  When Progress >= 1.0:
   │    ──▶ AnimationEvent.AttackComplete
   │    ──▶ Effects.spawnImpact at target position
   │    ──▶ Program.fs emits PhaseMsg(Resolution)
   │
   ▼
5. PhaseMsg(Resolution)
   │  Phase resolves pending attack, returns Intent.AttackResolved
   │  Program.fs translates:
   │
   └─▶ UnitsMsg(AttackUnit(attacker, target))   ← apply damage
   │
   ▼
6. UnitsMsg(AttackUnit)
   │  Damage calculated (base class damage − target defense)
   │  Target HP reduced, or unit removed if HP ≤ 0
   │  Program.fs checks win conditions via Units.checkGameOver
   │  If one faction remains: sets GameOver, shows victory banner
```

## Message Flow: Turn Transition

When a player ends their turn, a 2-second transition plays before the next faction takes over:

```
1. User presses Enter (EndTurn key) or AI sends EndTurn
   │
   ▼
2. PhaseMsg(EndTurn)
   │  Phase returns Intent.StartTransition(nextFaction)
   │  Program.fs clears visibility (multi-human only) and starts transition:
   │
   └─▶ AnimationMsg(StartTransition(nextFaction, 2.0f))
   │
   ▼
3. Tick (every frame)
   │  AnimState.update advances transition timer
   │  View renders full-screen overlay with faction name
   │  When timer expires:
   │    ──▶ AnimationEvent.TransitionComplete(newFaction)
   │    ──▶ Program.fs emits PhaseMsg(TransitionDone)
   │
   ▼
4. PhaseMsg(TransitionDone)
   │  Phase calls advanceTurn → cycles to next faction
   │  Program.fs recomputes fog visibility via resolveVisibility
   │  If next player is AI: Program.fs emits EvaluateAI
   │  If next player is Human: waits for input
```

## Fog of War

The fog of war system uses a **shader-based approach** with procedural space dust:

### Visibility

Each unit has a `VisualRange` (hex steps). `Map.computeVisibleUnits` unions all cells visible to a player's units using `Hex2DSpatial.inRange`. The result is stored in `MapModel.Visible`.

### Rendering

`FogOfWar.fs` contains a GLSL shader that:

1. For each hex cell in the viewport, checks if it's in the `Visible` set
2. If not visible: draws a filled hex polygon with a procedural nebula shader
3. The shader uses 3-layer FBM noise for swirling purple/blue nebulae
4. If visible: the hex is not drawn (transparent)

### Integration

- `PhaseQuery.IsVisible` — Phase uses this to prevent human attacks on fogged targets (AI bypasses this check)
- `Units.view` — Enemy units in fog are not rendered
- `UI.drawHpBars` — HP bars in fog are not rendered
- Fog is rendered after tiles but before units inside the camera transform

## Win Conditions

Simple elimination: when a faction has no units remaining, the game ends.

- `Units.checkGameOver(units, factions)` — returns `ValueSome winner` if only one unique faction has units (deduplicates duplicate faction slots)
- Called after every `AttackUnit` message and at the start of every `EvaluateAI` call
- Sets `model.GameOver` which blocks all input except restart
- Renders a victory overlay with the winning faction name
- Press **R** to restart

## Key Patterns

### Systems return pure data, Program.fs orchestrates

Phase returns `Intent`, AnimState returns `AnimationEvent`. Neither knows about the other. `Program.fs` is the only place where cross-system wiring exists.

### Query objects for read-only access

Phase needs to read input state, unit positions, and reachable cells — but it doesn't own any of them. Instead, `Program.fs` builds a `PhaseQuery` record with closures that read from the model:

```fsharp
let query: Phase.PhaseQuery = {
  Selection = model.Input.Selection
  UnitAt = fun cell -> model.Units |> Map.tryFind cell
  IsReachable = fun cell -> model.Map.Reachable.Contains cell
  IsAttackable = fun cell -> model.Map.AttackTargets.Contains cell
  IsVisible = fun cell -> model.Map.Visible.Contains cell
  CurrentFaction = model.Turn.CurrentFaction
  CurrentPlayerIndex = model.Turn.CurrentPlayerIndex
  PlayerControl = model.Turn.PlayerControl
}
```

### Cmd.map for message translation

Sub-system commands are lifted into the main `Msg` type using `Cmd.map`:

```fsharp
phaseCmd |> Cmd.map PhaseMsg
inputCmd |> Cmd.map(fun msg -> match msg with CalculateRange -> MapMsg(...) | other -> InputMsg other)
```

### Mutable model, immutable messages

The `Model` class uses mutable properties for performance (avoiding large immutable copies), but all messages and sub-system data types are immutable structs/records.

## AI System

The AI module (`AI.fs`) is a pure-function decision tree. It has no state — it reads the game state and returns `AIAction` values that `Program.fs` translates into `PhaseMsg` messages.

### Decision Flow

```
EvaluateAI (Program.fs)
  │
  ├─▶ checkGameOver → if game over, show banner and stop
  │
  ├─▶ AI.evaluateNextAction → iterate AI player's units
  │     │
  │     ├─▶ For each unit: AI.evaluate → score actions
  │     │     ├─ Compute visible enemies (AI.computeVisible)
  │     │     ├─ Check attack range, movement range
  │     │     ├─ Score: health + threat + target + support (weighted by class)
  │     │     └─ Return: AttackOnly | MoveAndAttack | MoveOnly | NoAction
  │     │
  │     └─▶ Return first actionable (unitCell, PhaseMsg, PhaseMsg)
  │
  ├─▶ Set model.Map.Reachable + AttackTargets directly
  ├─▶ Set model.Input.Selection
  └─▶ Dispatch PhaseMsg(actionMsg) → flows through same Phase pipeline as human
```

### Class-Weighted Scoring

Each unit class has different behavioral weights that shape decision-making:

| Weight  | Fighter | Cruiser | Battleship | Meaning                           |
| ------- | ------- | ------- | ---------- | --------------------------------- |
| Health  | 0.2     | 0.3     | 0.5        | Caution when wounded              |
| Threat  | 0.6     | 0.3     | -0.4       | Drawn to (or repelled by) enemies |
| Target  | 0.8     | 0.5     | 0.3        | Wants to attack                   |
| Support | -0.1    | 0.3     | 0.6        | Stays near allies                 |

- **Fighters**: Aggressive — drawn to enemies, want to attack, don't care about damage
- **Cruisers**: Balanced — moderate concern for health and allies
- **Battleships**: Cautious — repelled by enemies, stay near allies, prioritize survival

### Patrol Behavior

When no enemies are visible, AI units patrol: center of map → corners (cycling each turn via `turnNumber + playerIndex`).

### AI Turn Automation

`Program.fs` automates AI turns via the `aiCmd` logic:

- After `TransitionDone` or `Resolution` → send `EvaluateAI`
- On `NoIntent` or `ClearSelection` → send `EndTurn`
- AI evaluates one unit per `EvaluateAI` call; `Program.fs` loops until all units act or turn ends
- Mouse/keyboard gameplay input is blocked during AI turns

## Unit Classes and Combat

### Unit Stats

| Stat        | Fighter | Cruiser | Battleship |
| ----------- | ------- | ------- | ---------- |
| HP          | 12      | 20      | 35         |
| Defense     | 5       | 12      | 25         |
| MoveRange   | 8       | 5       | 3          |
| AttackRange | 2       | 3       | 4          |
| VisualRange | 2       | 3       | 7          |
| Base Damage | 25      | 18      | 12         |

### Damage Formula

```
damage = max 1 (baseDamage * 10 / (10 + defense))
```

Defense provides **diminishing returns** — each additional point of defense matters less. This prevents stalemates where high-defense units take 1 damage.

| Attacker → Target   | Fighter (5def) | Cruiser (12def) | Battleship (25def) |
| ------------------- | -------------- | --------------- | ------------------ |
| **Fighter** (25)    | 17             | 15              | 8                  |
| **Cruiser** (18)    | 12             | 7               | 6                  |
| **Battleship** (12) | 8              | 5               | 4                  |

### Movement and Attack Order

Humans can move and attack in **any order** per unit:

- Attack from original position (no move required)
- Move then attack
- Attack then move
- Just move or just attack

`RecalculateRange` computes both move range AND attack targets when a unit can do both (`CanMove && CanAct`).

## Fog of War Modes

The fog system adapts based on how many human players are in the game:

| Mode         | HumanCount | Fog    | Visibility Source      | During Transitions    |
| ------------ | :--------: | ------ | ---------------------- | --------------------- |
| Spectator    |     0      | None   | All cells (set once)   | No fog                |
| Single Human |     1      | Active | Human's units          | Never cleared         |
| Hot-Seat     |     2+     | Active | Current player's units | Cleared between turns |

### Visibility Resolution

All visibility decisions are centralized in `resolveVisibility(model, trigger)`:

```fsharp
[<Struct>]
type VisibilityTrigger =
  | GameStart | TurnStart | TransitionStart | UnitMoved | UnitChanged

[<Struct>]
type VisibilityAction =
  | RefreshForSingleHuman | RefreshForCurrentPlayer | ClearVisibility | NoVisibilityChange
```

This single function replaces the scattered `HumanCount`/`PlayerControl` checks that were previously in 6 different handler contexts.

### All-AI Spectator Mode

When all players are AI, `model.Map.Visible` is set to all grid cells at game start. No fog renders, no visibility restrictions — the human watches the AI fight.

## Pre-Start Configuration

`PreStart.fs` provides a configuration screen where players can:

- Toggle up to 4 player slots on/off
- Cycle faction (Federation, Empire, Pirates)
- Cycle control type (Human, AI)

Map size scales with player count: 10×10 (2 players) to 16×16 (4 players). Each player spawns 3 units (Fighter, Cruiser, Battleship) at their corner of the map.

## Performance Considerations

### Why a mutable class instead of an immutable record?

The `Model` type is a **class with mutable properties**, not an immutable F# record:

```fsharp
type Model() =
  member val Time: GameTime = Unchecked.defaultof<_> with get, set
  member val Units: Map<struct (int * int), SBUnit> = Unchecked.defaultof<_> with get, set
  // ...
```

This is a deliberate choice at **Level 2.5** of the [Scaling Mibo](../../docs/scaling.md) architecture. The Model has 13 properties — copying the entire object every frame via immutable updates would create significant GC pressure. With mutable properties, the `update` function returns the **same Model instance** with fields mutated in place. Zero allocation.

Messages (`Msg`, `InputMsg`, `UnitsMsg`, etc.) are all `[<Struct>]` — value types that live on the stack. This means message dispatch is allocation-free, even at 60fps with dozens of messages per frame.

### Mibo's adaptability: from turn-based to 60fps action

Mibo.Raylib is built on the same Elmish foundation (`Program.mkProgram`) regardless of game type. What changes is how you use it. The framework gives you the **same building blocks** — `init`, `update`, `view`, `Tick`, `Cmd`, `Sub` — and lets you decide how much optimization and decomposition you need.

**For high-performance games** (platformers, shooters, 3D explorers), the framework supports:

- Mutable `Model` classes to avoid GC pressure from large immutable copies
- `[<Struct>]` messages for zero-allocation dispatch
- Pre-allocated arrays and `ResizeArray` buffers that are reused each frame
- `System.pipeMutable` pipelines for sequencing physics, particles, and collision in a single pass
- `ArrayPool` integration for temporary per-frame buffers
- `Span`/`byref` for passing large structs without copying

These patterns exist at Level 2.5+ of the scaling ladder. The `update` function can call multiple system functions in sequence, each mutating shared state in place. Performance-critical paths stay allocation-free.

**For lower-intensity games** (turn-based strategy, card games, puzzle games), the same framework works with simpler patterns:

- Immutable records for game state — correctness and clarity over throughput
- A single `update` function with pattern matching — no need for system decomposition
- `Cmd.batch` and `Cmd.map` for coordinating between sub-systems
- Intent/Event patterns where systems return declarative data and `Program.fs` translates into cross-system messages
- Fewer concerns about GC pressure since the game doesn't process thousands of entities at 60fps

**SpaceBattle** demonstrates this simpler end of the spectrum. It prioritizes **architectural clarity** — sub-systems are fully independent, communicate only through the router, and don't know about each other. The tradeoff is more allocations per frame (immutable `Map`, `Set`, `Array` operations), which is perfectly fine for a turn-based game.

The key insight is that **you scale the architecture, not the framework**. The same `Program.withTick`, `Program.withRenderer`, `Program.withSubscription` pipeline powers both a 60fps platformer with pre-allocated particle buffers and a turn-based hex strategy game with routed sub-systems. You apply performance patterns where profiling shows need, and keep everything else simple.

For the full scaling ladder and when to apply each pattern, see [Scaling Mibo.Raylib](../../docs/scaling.md). For implementation details on the performance patterns, see [F# For Perf](../../docs/performance.md).

## File Map

```
SpaceBattle/
├── Program.fs           ← Router: init, update, view, subscriptions, visibility resolution
├── Types.fs             ← Tile type (Asteroid, DeepSpace, etc.)
├── Constants.fs         ← Game constants (cell size, zoom, viewport)
├── Input.fs             ← Mouse/keyboard input, selection state
├── Camera.fs            ← Camera movement and zoom
├── Map.fs               ← Hex grid, pathfinding overlay, reachable cells, fog visibility
├── Units.fs             ← Unit data, movement, damage, direction, rendering
├── Phase.fs             ← Turn phases, action intents, resolution
├── Selection.fs         ← Move range computation, path simplification
├── AI.fs                ← AI decision tree, class-weighted scoring, patrol behavior
├── AnimState.fs         ← Movement tween animation, banners, turn transitions
├── AnimatedDecorations.fs ← Animated background sprites
├── Effects.fs           ← Laser trail/impact particles, point lights
├── FogOfWar.fs          ← Shader-based fog of war with space dust
├── Shaders.fs           ← Skybox shader
├── Assets.fs            ← Sprite sheet loading
├── UI.fs                ← HP bars, action indicators, info overlays, turn indicator
├── PreStart.fs          ← Pre-game player configuration screen
├── DebugUtils.fs        ← Debug overlay utilities
└── SpaceBattle.fsproj   ← Compilation order
```
