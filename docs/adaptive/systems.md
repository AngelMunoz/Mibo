---
title: Adaptive Systems
category: Adaptive
categoryindex: 3
index: 3
---

# Sub-Systems Without Cmd

The [routed sub-system rules](../patterns/composable-systems.html) are runtime-agnostic: a sub-system owns its model, emits events as data, and never touches its siblings. The adaptive translation replaces the router's `Cmd` plumbing with direct handlers and posted intents.

## The MVU → Adaptive translation

| MVU concept | Adaptive equivalent |
|---|---|
| root `update` router | one `Application.update` that ticks systems in order and translates events |
| sub-system `Msg` | a `handle` function per system, called by cold paths |
| `Cmd` / `Cmd.batch` | `ctx.Intents.post` — same drain moment, same order |
| sub-system event → `Cmd.ofMsg` | event → direct call to the consumer's handler |
| query objects | function parameters — see below |

```fsharp
// The per-step sim: systems tick in a fixed order and return events.
let update getState (ctx: AdaptiveContext) (gameTime: GameTime) =
    let state = getState()
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    let enemyEvents = Enemies.tick dt state.Enemies state.Map.Path
    let spawnEvents = Spawning.tick dt state.Spawning
    // ... remaining systems in dependency order

    // Reactions post as intents: drained after Update, before the
    // frame is forced — post order = the old Cmd batch order.
    ctx.Intents.post(fun () -> handleEnemyEvents state enemyEvents)
    ctx.Intents.post(fun () -> handleSpawnEvents state spawnEvents)
```

Event translation is one-directional by construction: a handler writes other systems' roots, and only the tick emits. Keep the flow list short and written down — "only `ApplyDamage` (→ `Killed`), `FillWave` (→ `SpawnEnemy`), `StartNextWave` (→ `WaveStarted`) and the ticks emit."

## Hot reads: direct values, not closures

The hot-path rule from [Composable Systems](../patterns/composable-systems.html) §4 applies unchanged, with one refinement: cross-system reads in the tick pull **transient views** of adaptive maps and pass them as plain parameters:

```fsharp
// Towers reads Enemies.Alive and a suppression projection — direct
// values, no closures, no query records in the tick.
let towerEvents =
    Towers.tick
        dt
        state.Towers
        state.Enemies.Alive
        state.Enemies.Velocities
        (state.Projections.Suppression |> AMap.getValue)
        (State.cellSize state)
```

`AMap.getValue` returns a transient read-only dictionary valid until the next write. The tick consumes it immediately; nothing retains it. Incremental work still happens — inside the node, once per change — the tick just refuses to pay closure costs.

## Projection ownership

Two homes for derived state, decided by who owns the inputs:

* **Own maps only** → the system builds its projections in its own `init` (e.g. `Enemies` derives its `Views` join and `Alive` filter from its own maps).
* **Cross-system joins** → the composition root owns them, in a single `Projections` object (e.g. `Suppression` = `Towers.Statics × Enemies.BossPositions`). Sub-systems never import each other to build joins.

```fsharp
/// State-owned CROSS-subsystem projections — joins/filters that touch
/// two systems' maps. Sub-systems own projections derived purely from
/// their own maps.
type Projections(enemies, towers, ...) =
    member val Suppression: amap<int<TowerId>, float32> =
        towers.Statics
        |> AMap.mapA(fun _ s ->
            enemies.BossPositions
            |> AMap.filter(fun _ bossPos -> inRadius s bossPos)
            |> AMap.count
            |> AVal.map(fun n -> if n > 0 then factor else 1f))
```

**Cost rule:** per-element joins (`mapA` over one map, filtering another) re-scan the inner map per element per change — fine for small inner maps, quadratic for large ones. When a join gets expensive, drop to a plain row map over one map (the projectile `Homing` projection did exactly this when the live target join stopped paying for itself). Measure before you nest.

## Cold paths: decisions are data, the router only translates

Commands like *place tower* span several systems (map tile, occupancy, gold). Keep the decision out of the update loop: a small module of pure functions over read-only inputs returns an accepted **plan as data**, and update translates the plan into system calls:

```fsharp
// Placement owns the build rules; Application only translates.
match Placement.place map towers gold def cell with
| ValueSome plan ->
    Towers.handle (Towers.Place(plan.Cell, plan.Def)) state.Towers
    Economy.handle (Economy.SpendGold plan.Cost) state.Economy
    true
| ValueNone -> false
```

This is the cold-path analog of what `Projections` is for the render side — cross-system knowledge lives at the composition root, as pure functions, never inside a sub-system.

## What may live outside the graph

State classification is one question: **does anything derive from it?**

| Home | Use for | Examples |
|---|---|---|
| Graph containers (`cval`, `cmap`) | state the render side or other systems derive from | health, positions, gold, levels |
| Plain containers (arrays, dictionaries) | sim-private state nobody derives from | velocity scratch, slow timers, id counters, RNG |
| By-ref payload on the frame | mutable presentation models | particle pools, camera pose |

The graph sees writes to containers. It does **not** see writes inside stored values — mutating a `ResizeArray` field of a row stored in a `cmap` is invisible to every projection. That is correct exactly when nothing derives from that field; keep such mutations sim-private.
