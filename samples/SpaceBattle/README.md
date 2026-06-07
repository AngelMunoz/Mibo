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
```

## Systems

| System          | File                     | Owns                                       | Messages                 | Purpose                                      |
| --------------- | ------------------------ | ------------------------------------------ | ------------------------ | -------------------------------------------- |
| **Input**       | `Input.fs`               | `InputModel` (selection, hover, held keys) | `InputMsg`               | Mouse/keyboard input, selection state        |
| **Camera**      | `Camera.fs`              | `CameraModel` (Camera2D)                   | `CameraMsg`              | Zoom, movement, map clamping                 |
| **Map**         | `Map.fs`                 | `MapModel` (grid, reachable, path)         | `MapMsg`                 | Hex grid, pathfinding, reachable cells       |
| **Units**       | `Units.fs`               | `Map<cell, SBUnit>`                        | `UnitsMsg`               | Unit data, move/damage/direction             |
| **Phase**       | `Phase.fs`               | `Turn`, `TurnOrder`                        | `PhaseMsg` → `Intent`    | Turn management, action resolution           |
| **AnimState**   | `AnimState.fs`           | `AnimationState`                           | `AnimationMsg` → `Event` | Movement/attack tween, banners               |
| **Selection**   | `Selection.fs`           | (pure functions)                           | —                        | Move range, path computation, simplification |
| **Shaders**     | `Shaders.fs`             | `SkyboxModel`                              | —                        | Skybox rendering                             |
| **Decorations** | `AnimatedDecorations.fs` | `Map<cell, AnimatedSprite>`                | —                        | Animated background sprites                  |
| **Effects**     | `Effects.fs`             | `EffectState` (particles, lights, flashes) | —                        | Laser trail/impact particles, point lights   |

## Cross-System Communication

Systems never call each other directly. Instead, they communicate through **Intents** and **Events** that `Program.fs` intercepts and translates into messages for other systems.

### Intents (Phase → Program → Other Systems)

`Phase.System.update` returns an `Intent` — a declarative description of what should happen. `Program.fs` translates each intent into commands for the relevant systems:

```
Phase.Intent.PerformMove     ──▶  AnimationMsg.StartMove  +  InputMsg.ClearSelection
Phase.Intent.PerformAttack   ──▶  AnimationMsg.StartAttack + UnitsMsg.UpdateDirection + InputMsg.ClearSelection
Phase.Intent.MoveResolved    ──▶  UnitsMsg.MoveUnit
Phase.Intent.AttackResolved  ──▶  UnitsMsg.AttackUnit
Phase.Intent.SwitchSelection ──▶  InputMsg.SelectCell
Phase.Intent.ClearSelection  ──▶  InputMsg.ClearSelection
```

The Phase system **never knows** about animations or input — it just declares intent.

### Events (AnimState → Program → Other Systems)

`AnimState.update` returns an `AnimationEvent` when something significant happens. `Program.fs` translates events into commands:

```
AnimationEvent.MoveComplete     ──▶  PhaseMsg.Resolution
AnimationEvent.AttackComplete   ──▶  PhaseMsg.Resolution
AnimationEvent.SegmentChanged   ──▶  UnitsMsg.UpdateDirection
AnimationEvent.BannerComplete   ──▶  (currently unused)
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
```

## Key Patterns

### Systems return pure data, Program.fs orchestrates

Phase returns `Intent`, AnimState returns `AnimationEvent`. Neither knows about the other. `Program.fs` is the only place where cross-system wiring exists.

### Query objects for read-only access

Phase needs to read input state, unit positions, and reachable cells — but it doesn't own any of them. Instead, `Program.fs` builds a `PhaseQuery` record with closures that read from the model:

```fsharp
let query: Phase.PhaseQuery = {
  Selection = model.Input.Selection
  UnitAt = fun cell -> model.Units |> Map.tryFind cell
  IsReachable = fun cell -> model.Map.Reachable.Contains cell || model.Map.AttackTargets.Contains cell
  CurrentFaction = model.Turn.CurrentFaction
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
├── Program.fs           ← Router: init, update, view, subscriptions
├── Types.fs             ← Tile type (Asteroid, DeepSpace, etc.)
├── Input.fs             ← Mouse/keyboard input, selection state
├── Camera.fs            ← Camera movement and zoom
├── Map.fs               ← Hex grid, pathfinding overlay, reachable cells
├── Units.fs             ← Unit data, movement, damage, direction, rendering
├── Phase.fs             ← Turn phases, action intents, resolution
├── Selection.fs         ← Move range computation, path simplification
├── AnimState.fs         ← Movement tween animation, banners
├── AnimatedDecorations.fs ← Animated background sprites
├── Effects.fs           ← Laser trail/impact particles, point lights
├── Shaders.fs           ← Skybox shader
├── Assets.fs            ← Sprite sheet loading
├── Constants.fs         ← Game constants (cell size, zoom, etc.)
└── DebugUtils.fs        ← Debug overlay utilities
```
