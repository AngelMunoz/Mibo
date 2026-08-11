module Defli.World.Systems.Enemies

open System.Collections.Generic
open System.Numerics
open AdaptiveSlop.Core
open Mibo.Elmish
open Defli.World
open Defli

// ─────────────────────────────────────────────────────────────
// Enemies sub-system — owns ALL enemy component maps (lifecycle
// consistency: it alone spawns/despawns enemies, atomically across
// its maps) and its own projections (derived from own maps only).
//
//   Healths   — damage writes touch ONLY this map (row-level delta)
//   Motions   — speed/slow/progress/pathIndex
//   Positions — movement; separate so damage never invalidates it
//   Defs      — static per enemy (sprite, reward); written once
//
// Projections:
//   Views      = Positions × Healths × Motions join (3-way AMap.joinOn
//                composition — per-key subgraphs, in-place input swap)
//   Alive      = Views |> filter Hp > 0 (targeting/render query)
// ─────────────────────────────────────────────────────────────

[<Struct>]
type EnemyMsg =
  | Spawn of def: EnemyDef
  /// Spawn mid-path at an explicit position/progress (Phase 6 split
  /// children appear at the corpse — Spawn would teleport them to the
  /// path origin).
  | SpawnAt of spawnAt: struct (EnemyDef * Vector2 * float32 * int)
  | ApplyDamage of applyDamage: struct (int<EnemyId> * int)
  | ApplySlow of slow: SlowApply
  | Despawn of enemy: int<EnemyId>

[<Struct>]
type EnemyEvent =
  | Killed of killed: struct (int<EnemyId> * int)
  | ReachedBase of enemy: int<EnemyId>

type EnemiesModel() =
  member val Healths = CMap.empty<int<EnemyId>, Health> with get, set
  member val Motions = CMap.empty<int<EnemyId>, Motion> with get, set
  member val Positions = CMap.empty<int<EnemyId>, Vector2> with get, set
  member val Defs = CMap.empty<int<EnemyId>, EnemyDef> with get, set
  /// Tagged from the start — ids never pass through a plain int.
  member val NextId = 0<EnemyId> with get, set
  /// Slow expiry timers (sim-only, plain — not adaptive).
  member val SlowTimers = Dictionary<int<EnemyId>, float32>() with get, set
  // Own projections (own maps only) — built in Enemies.init.
  member val Views: amap<int<EnemyId>, EnemyView> =
    Unchecked.defaultof<_> with get, set

  member val Alive: amap<int<EnemyId>, EnemyView> =
    Unchecked.defaultof<_> with get, set

  /// Live boss positions (Positions × Defs, archetype-filtered) — the
  /// world-owned Suppression projection joins on this (Phase 6).
  member val BossPositions: amap<int<EnemyId>, Vector2> =
    Unchecked.defaultof<_> with get, set

module Enemies =
  open System

  // ── Projections (the AdaptiveSlop showcase: join, filter, aggregate) ──

  let inline private buildViews
    (m: EnemiesModel)
    : amap<int<EnemyId>, EnemyView> =
    // The 3-way joinOn composition (Positions × Healths × Motions): each
    // join builds its per-key subgraph once and swaps the left input in
    // place — no rebuild on update. Rows are written atomically in
    // transactions, so post-commit all three always exist; the defensive
    // zero row only guards transient mid-transaction reads (Alive filters
    // it out).
    let positionsHealths =
      AMap.joinOn
        m.Positions
        m.Healths
        (fun eid _ -> eid)
        (fun _ posV healthV ->
          AVal.map2 (fun pos h -> ValueSome(struct (pos, h))) posV healthV)

    AMap.joinOn
      positionsHealths
      m.Motions
      (fun eid _ -> eid)
      (fun
           _
           (structV: aval<struct (Vector2 * Health voption)>)
           (motionV: aval<Motion voption>) ->
        AVal.map2
          (fun
               (struct (pos, h): struct (Vector2 * Health voption))
               (mv: Motion voption) ->
            Telemetry.viewsJoin <- Telemetry.viewsJoin + 1

            match struct (h, mv) with
            | ValueSome h, ValueSome mv ->
              ValueSome {
                Pos = pos
                Hp = h.Hp
                MaxHp = h.MaxHp
                Progress = mv.Progress
                Slow = mv.Slow
                PathIndex = mv.PathIndex
              }
            | _ ->
              ValueSome {
                Pos = pos
                Hp = 0
                MaxHp = 0
                Progress = 0f
                Slow = 1f
                PathIndex = 0
              })
          structV
          motionV)

  let inline private buildAlive
    (m: EnemiesModel)
    : amap<int<EnemyId>, EnemyView> =
    m.Views
    |> AMap.filter(fun _ v ->
      Telemetry.aliveFilter <- Telemetry.aliveFilter + 1
      v.Hp > 0)

  /// Boss positions: a same-key AMap.joinOn into Defs (the Views-join
  /// shape), kept only when the archetype is Boss — the join's
  /// ValueNone output drops the entry (choose semantics). Written by
  /// the movement tick like Positions; read by the world's Suppression
  /// projection.
  let inline private buildBossPositions
    (m: EnemiesModel)
    : amap<int<EnemyId>, Vector2> =
    AMap.joinOn m.Positions m.Defs (fun eid _ -> eid) (fun _ posV defV ->
      AVal.map2
        (fun pos def ->
          Telemetry.bossPositions <- Telemetry.bossPositions + 1

          def
          |> ValueOption.bind(fun d ->
            if d.Archetype = EnemyArchetype.Boss then
              ValueSome pos
            else
              ValueNone))
        posV
        defV)

  let init() : EnemiesModel =
    let m = EnemiesModel()
    m.Views <- buildViews m
    m.Alive <- buildAlive m
    m.BossPositions <- buildBossPositions m
    m

  // ── Cold-path mutations (unit — these never emit) ──
  // The router calls these directly: in-place mutations with no return
  // to discard. The host-facing union dispatch (update) delegates here.

  /// Spawn at the path origin (the wave director's entry point).
  let spawn (def: EnemyDef) (model: EnemiesModel) (path: Vector2[]) : unit =
    let eid = model.NextId
    model.NextId <- model.NextId + 1<EnemyId>

    Transaction.run(fun () ->
      model.Healths |> CMap.addOrUpdate eid { Hp = def.Hp; MaxHp = def.Hp }

      model.Motions
      |> CMap.addOrUpdate eid {
        Speed = def.Speed
        Slow = 1f
        Progress = 0f
        PathIndex = 0
      }

      model.Positions |> CMap.addOrUpdate eid path[0]
      model.Defs |> CMap.addOrUpdate eid def)

  /// Split-child spawn: the same atomic four-row write, but at the
  /// corpse's position and path state (not the path origin).
  let spawnAt
    (def: EnemyDef)
    (pos: Vector2)
    (progress: float32)
    (pathIndex: int)
    (model: EnemiesModel)
    (path: Vector2[])
    : unit =
    let eid = model.NextId
    model.NextId <- model.NextId + 1<EnemyId>

    Transaction.run(fun () ->
      model.Healths |> CMap.addOrUpdate eid { Hp = def.Hp; MaxHp = def.Hp }

      model.Motions
      |> CMap.addOrUpdate eid {
        Speed = def.Speed
        Slow = 1f
        Progress = progress
        PathIndex = pathIndex
      }

      model.Positions |> CMap.addOrUpdate eid pos
      model.Defs |> CMap.addOrUpdate eid def)

  /// Removes the enemy's four rows atomically.
  let despawn
    (eid: int<EnemyId>)
    (model: EnemiesModel)
    (path: Vector2[])
    : unit =
    Transaction.run(fun () ->
      model.Healths |> CMap.remove eid
      model.Motions |> CMap.remove eid
      model.Positions |> CMap.remove eid
      model.Defs |> CMap.remove eid)

  /// Applies the slow factor and arms the slow timer.
  let applySlow
    (slow: SlowApply)
    (model: EnemiesModel)
    (path: Vector2[])
    : unit =
    match model.Motions |> CMap.tryGetValue slow.Enemy with
    | ValueSome mv ->
      model.Motions
      |> CMap.addOrUpdate slow.Enemy { mv with Slow = slow.Factor }

      model.SlowTimers[slow.Enemy] <- slow.Seconds
    | ValueNone -> ()

  /// The one message that emits: applies damage; Killed on a zero
  /// crossing (the router earns gold and despawns from that event).
  let applyDamage
    (eid: int<EnemyId>)
    (amount: int)
    (model: EnemiesModel)
    (path: Vector2[])
    : EnemyEvent[] =
    match model.Healths |> CMap.tryGetValue eid with
    | ValueSome h when h.Hp > 0 ->
      let hp = max 0 (h.Hp - amount)
      model.Healths |> CMap.addOrUpdate eid { h with Hp = hp }

      if hp = 0 then
        match model.Defs |> CMap.tryGetValue eid with
        | ValueSome def -> [| Killed(eid, def.GoldReward) |]
        | ValueNone -> Array.empty
      else
        Array.empty
    | _ -> Array.empty

  /// Host-facing dispatch over the union (tests, debug hosts) —
  /// delegates to the mutations above; returns what was emitted.
  let update
    (msg: EnemyMsg)
    (model: EnemiesModel)
    (path: Vector2[])
    : EnemyEvent[] =
    match msg with
    | Spawn def ->
      spawn def model path
      Array.empty
    | SpawnAt(def, pos, progress, pathIndex) ->
      spawnAt def pos progress pathIndex model path
      Array.empty
    | ApplyDamage(eid, amount) -> applyDamage eid amount model path
    | ApplySlow slow ->
      applySlow slow model path
      Array.empty
    | Despawn eid ->
      despawn eid model path
      Array.empty

  // ── Hot path (movement / "physics" phase) — direct values, no closures ──

  // ── Per-enemy movement, staged into inline helpers (the JIT fuses
  // them back together — no closures, no per-frame allocations) ──

  /// Stage 1 — resolve the archetype (defs are written once at spawn;
  /// a miss is a transient row → Grunt).
  let inline archetypeOf defs eid =
    defs
    |> CMap.tryGetValue eid
    |> ValueOption.map _.Archetype
    |> ValueOption.defaultValue Grunt

  /// Stage 2 — fliers: interpolate the straight line spawn → base.
  /// Returns (pos, progress, arrived); PathIndex is meaningless (0).
  let inline flyStep
    (dt: float32)
    (mv: Motion)
    (flyDist: float32)
    (spawn: Vector2)
    (basePos: Vector2)
    : struct (Vector2 * float32 * bool) =
    let step =
      if flyDist <= 0f then
        1f
      else
        mv.Speed * mv.Slow * dt / flyDist

    let progress = min 1f (mv.Progress + step)
    struct (Vector2.Lerp(spawn, basePos, progress), progress, progress >= 1f)

  /// Stage 3 — road walkers (Grunt/Runner/Tank/Boss): consume the
  /// `Speed * Slow * dt` step along the waypoint segments, advancing
  /// PathIndex. Returns (pos, pathIndex, progress, arrived).
  let inline walkStep
    (dt: float32)
    (mv: Motion)
    (pos: Vector2)
    (path: Vector2[])
    : struct (Vector2 * int * float32 * bool) =
    let mutable p = pos
    let mutable idx = mv.PathIndex
    let mutable remaining = mv.Speed * mv.Slow * dt

    while remaining > 0f && idx < path.Length - 1 do
      let target = path[idx + 1]
      let d = target - p
      let dist = d.Length()

      if dist <= remaining then
        p <- target
        remaining <- remaining - dist
        idx <- idx + 1
      else
        p <- p + (d / dist) * remaining
        remaining <- 0f

    let arrived = idx >= path.Length - 1
    let total = float32(path.Length - 1)

    let progress =
      if arrived then
        1f
      else
        let segLen = Vector2.Distance(path[idx], path[idx + 1])

        if segLen <= 0f then
          float32 idx / total
        else
          (Vector2.Distance(path[idx], p) / segLen + float32 idx) / total

    p, idx, progress, arrived

  let tick
    (dt: float32)
    (model: EnemiesModel)
    (path: Vector2[])
    : EnemyEvent seq =
    // Expire slow timers (collect first — mutating during iteration is unsafe).
    let mutable expired: ResizeArray<int<EnemyId>> = null

    for KeyValueV(eid, remaining) in model.SlowTimers do
      let remaining = remaining - dt

      if remaining <= 0f then
        if isNull expired then
          expired <- ResizeArray()

        expired.Add eid
      else
        model.SlowTimers[eid] <- remaining

    if not(isNull expired) then
      for eid in expired do
        match model.Motions |> CMap.tryGetValue eid with
        | ValueSome mv ->
          model.Motions |> CMap.addOrUpdate eid { mv with Slow = 1f }
        | ValueNone -> ()

    // Movement along waypoints. Fliers ignore the road: they interpolate
    // the straight line spawn → base (world-space, not waypoint walking).
    let flyDist = Vector2.Distance(path[0], path[path.Length - 1])

    let mutable events: ResizeArray<EnemyEvent> = null
    let mutable arrivals: ResizeArray<int<EnemyId>> = null

    for KeyValueV(eid, pos) in model.Positions |> AMap.getValue do
      model.Motions
      |> CMap.tryGetValue eid
      |> ValueOption.iter(fun mv ->
        // The archetype picks the locomotion: fliers fly the straight
        // line spawn → base, everyone else walks the waypoints.
        let archetype = archetypeOf model.Defs eid

        let struct (p, idx, progress, arrived) =
          if archetype = EnemyArchetype.Flier then
            let struct (p, progress, arrived) =
              flyStep dt mv flyDist path[0] path[path.Length - 1]

            struct (p, 0, progress, arrived)
          else
            walkStep dt mv pos path

        if arrived then
          if isNull arrivals then
            arrivals <- ResizeArray()

          arrivals.Add eid

          if isNull events then
            events <- ResizeArray()

          events.Add(ReachedBase eid)
        else
          model.Positions |> CMap.addOrUpdate eid p

          model.Motions
          |> CMap.addOrUpdate eid {
            mv with
                Progress = progress
                PathIndex = idx
          })

    // Arrivals are removed atomically (the router also gets ReachedBase).
    if not(isNull arrivals) then
      Transaction.run(fun () ->
        for eid in arrivals do
          model.Healths |> CMap.remove eid
          model.Motions |> CMap.remove eid
          model.Positions |> CMap.remove eid
          model.Defs |> CMap.remove eid)

    (if isNull events then Array.empty else events)
