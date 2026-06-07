# SpaceBattle — Architecture Guide

This document explains how the SpaceBattle sample is structured as an **Elmish application** where `Program.fs` acts as a **message router** that coordinates independent sub-systems.

## The Elmish Loop as a Router

The core Elmish loop is `init → update → view`, driven by messages. In SpaceBattle, `Program.fs` does **not** contain game logic — it routes messages to the appropriate sub-system and translates cross-system events into new messages.

```
  ┌──────────────────────────────────────────────────────────────┐
  │                        Program.fs                            │
  │                                                              │
  │   Msg ──┬──▶ Input.update     ──▶ model.Input  + Cmd<Input> │
  │         ├──▶ Map.update       ──▶ model.Map                 │
  │         ├──▶ Units.update     ──▶ model.Units  + Cmd<Units> │
  │         ├──▶ Camera.update    ──▶ model.Cam                 │
  │         ├──▶ Phase.System     ──▶ model.Turn   + Intent     │
  │         ├──▶ AnimState.update ──▶ model.Anim   + Event      │
  │         └──▶ Tick (per frame) ──▶ camera, anim, decorations │
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

| System | File | Owns | Messages | Purpose |
|---|---|---|---|---|
| **Input** | `Input.fs` | `InputModel` (selection, hover, held keys) | `InputMsg` | Mouse/keyboard input, selection state |
| **Camera** | `Camera.fs` | `CameraModel` (Camera2D) | `CameraMsg` | Zoom, movement, map clamping |
| **Map** | `Map.fs` | `MapModel` (grid, reachable, path) | `MapMsg` | Hex grid, pathfinding, reachable cells |
| **Units** | `Units.fs` | `Map<cell, SBUnit>` | `UnitsMsg` | Unit data, move/damage/direction |
| **Phase** | `Phase.fs` | `Turn`, `TurnOrder` | `PhaseMsg` → `Intent` | Turn management, action resolution |
| **AnimState** | `AnimState.fs` | `AnimationState` | `AnimationMsg` → `Event` | Movement tween, banners |
| **Selection** | `Selection.fs` | (pure functions) | — | Move range, path computation, simplification |
| **Shaders** | `Shaders.fs` | `SkyboxModel` | — | Skybox rendering |
| **Decorations** | `AnimatedDecorations.fs` | `Map<cell, AnimatedSprite>` | — | Animated background sprites |

## Cross-System Communication

Systems never call each other directly. Instead, they communicate through **Intents** and **Events** that `Program.fs` intercepts and translates into messages for other systems.

### Intents (Phase → Program → Other Systems)

`Phase.System.update` returns an `Intent` — a declarative description of what should happen. `Program.fs` translates each intent into commands for the relevant systems:

```
Phase.Intent.PerformMove  ──▶  AnimationMsg.StartMove  +  InputMsg.ClearSelection
Phase.Intent.MoveResolved ──▶  UnitsMsg.MoveUnit
Phase.Intent.SwitchSelection ──▶ InputMsg.SelectCell
Phase.Intent.ClearSelection  ──▶ InputMsg.ClearSelection
```

The Phase system **never knows** about animations or input — it just declares intent.

### Events (AnimState → Program → Other Systems)

`AnimState.update` returns an `AnimationEvent` when something significant happens. `Program.fs` translates events into commands:

```
AnimationEvent.MoveComplete     ──▶  PhaseMsg.Resolution
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
├── Shaders.fs           ← Skybox shader
├── Assets.fs            ← Sprite sheet loading
├── Constants.fs         ← Game constants (cell size, zoom, etc.)
└── DebugUtils.fs        ← Debug overlay utilities
```
