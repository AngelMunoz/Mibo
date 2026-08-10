module Defli.World.Systems.Towers

open System.Collections.Generic
open System.Numerics
open AdaptiveSlop.Core
open Mibo.Elmish
open Raylib_cs
open Defli.World
open Defli

// ─────────────────────────────────────────────────────────────
// Towers sub-system — owns placement, targeting, firing.
//
//   Statics   — { Def, Cell } written once at placement
//   Runtimes  — { Cooldown, Target } written every tick
//   CellIndex — cell → tower id (placement occupancy + the
//               RangeRing projection's hover lookup)
//
// Targeting reads the Enemies.Alive TRANSIENT VIEW passed in as a
// direct value by the router (hot path, no closures). Phase 3 adds
// the TargetPolicy field; Phase 2 always picks "first" (the enemy
// closest to the base — highest progress).
// ─────────────────────────────────────────────────────────────

[<Struct>]
type TowerMsg =
  | Place of struct (struct (int * int) * TowerDef)
  /// Cold path: bump the tower's level (the ROUTER validates gold
  /// and the cap before sending).
  | Upgrade of tower: int<TowerId>

[<Struct>]
type TowerEvent = Fired of shot: TowerShot

type TowersModel() =
  member val Statics = CMap.empty<int<TowerId>, TowerStatic> with get, set
  member val Runtimes = CMap.empty<int<TowerId>, TowerRuntime> with get, set

  member val CellIndex =
    CMap.empty<struct (int * int), int<TowerId>> with get, set

  /// Upgrade level per tower (1 = base def) — a SEPARATE component map
  /// so the EffectiveDef projection composes on top of Statics
  /// (Phase 5 projection-composition showcase).
  member val Levels = CMap.empty<int<TowerId>, int> with get, set

  /// Tagged from the start — ids never pass through a plain int.
  member val NextId = 0<TowerId> with get, set

  /// The EFFECTIVE def per tower: Statics.Def × Levels — derived
  /// projections composing on derived projections (RangeRing joins on
  /// this, the tick reads it transiently once per frame).
  member val EffectiveDef: amap<int<TowerId>, TowerDef> =
    Unchecked.defaultof<_> with get, set

module Towers =

  let inline private buildEffectiveDef
    (m: TowersModel)
    : amap<int<TowerId>, TowerDef> =
    m.Statics
    |> AMap.mapA(fun tid s ->
      m.Levels
      |> AMap.tryFind tid
      |> AVal.map(fun level ->
        Telemetry.effectiveDef <- Telemetry.effectiveDef + 1
        TowerDefs.effectiveDef s.Def (level |> ValueOption.defaultValue 1)))

  let init() : TowersModel =
    let m = TowersModel()
    m.EffectiveDef <- buildEffectiveDef m
    m

  /// Cold path: place a tower. The ROUTER validates (buildable tile,
  /// occupancy, gold) before sending — this only writes the rows.
  let update (msg: TowerMsg) (model: TowersModel) : unit =
    match msg with
    | Place(cell, def) ->
      let tid = model.NextId
      model.NextId <- model.NextId + 1<TowerId>

      Transaction.run(fun () ->
        model.Statics |> CMap.addOrUpdate tid { Def = def; Cell = cell }

        model.Runtimes
        |> CMap.addOrUpdate tid { Cooldown = 0f; Target = ValueNone }

        model.CellIndex |> CMap.addOrUpdate cell tid)
    | Upgrade tid ->
      let level =
        model.Levels |> CMap.tryGetValue tid |> ValueOption.defaultValue 1

      model.Levels |> CMap.addOrUpdate tid (level + 1)

  /// Hot path: cooldown decay + target acquisition + fire.
  /// `alive` is a transient read of Enemies.Alive and `suppression`
  /// one of the world's boss-aura projection (both direct values from
  /// the router — hot path, no closures); `cellSize` is the grid's
  /// uniform cell size.
  let tick
    (dt: float32)
    (model: TowersModel)
    (alive: amap<int<EnemyId>, EnemyView>)
    (suppression: IReadOnlyDictionary<int<TowerId>, float32>)
    (cellSize: Vector2)
    : TowerEvent seq =
    let mutable events: ResizeArray<TowerEvent> = null

    // ONE transient read of the composed projection per frame — the
    // effective def (Statics × Levels) drives range/damage/rate/policy.
    let effective = model.EffectiveDef |> AMap.getValue

    for KeyValueV(tid, s) in model.Statics |> AMap.getValue do
      let def =
        effective
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue s.Def

      // Boss aura (Phase 6): a live boss near this tower multiplies
      // its fire rate by the suppression factor (default 1 = free).
      let suppress =
        suppression
        |> ReadOnlyDict.tryGetValue tid
        |> ValueOption.defaultValue 1f

      let center = Cells.center s.Cell cellSize
      let rangeWorld = float32 def.Range * cellSize.X
      let runtimes = model.Runtimes |> AMap.tryFind tid
      let targetA = runtimes |> AVal.map(ValueOption.bind _.Target)

      let cooldownA =
        runtimes
        |> AVal.map(ValueOption.map _.Cooldown >> ValueOption.defaultValue 0f)

      let cooldown = cooldownA |> AVal.getValue

      let cooldown' = max 0f (cooldown - dt)

      if cooldown' <= 0f then
        // Acquire a target: in range + exact distance, then the def's
        // policy decides among the candidates (Phase 3).
        let mutable best: struct (int<EnemyId> * EnemyView * float32) voption =
          ValueNone

        for KeyValueV(eid, v) in alive |> AMap.getValue do
          let d = Vector2.Distance(center, v.Pos)

          if d <= rangeWorld then
            let better =
              match best with
              | ValueNone -> true
              | ValueSome struct (_, bv, bd) ->
                match def.TargetPolicy with
                | TargetPolicy.First -> v.Progress > bv.Progress
                | TargetPolicy.Last -> v.Progress < bv.Progress
                | TargetPolicy.Strongest -> v.MaxHp > bv.MaxHp
                | TargetPolicy.Weakest -> v.Hp < bv.Hp
                | TargetPolicy.Closest -> d < bd

            if better then
              best <- ValueSome struct (eid, v, d)

        match best with
        | ValueSome struct (eid, _, _) ->
          if isNull events then
            events <- ResizeArray()

          events.Add(
            Fired {
              Tower = tid
              Enemy = eid
              Damage = def.Damage
              SlowFactor = def.SlowFactor
              SlowSeconds = def.SlowSeconds
              SplashRadius = def.SplashRadius
              ProjectileSprite = def.ProjectileSprite
            }
          )

          // Cooldown from the EFFECTIVE def (Statics × Levels): the
          // +10 %/level fire-rate upgrade must actually apply. The
          // boss-aura suppression factor multiplies the rate (0.5 =
          // half speed → double cooldown).
          model.Runtimes
          |> CMap.addOrUpdate tid {
            Cooldown = 1f / max 0.1f (def.FireRate * suppress)
            Target = ValueSome eid
          }
        | ValueNone ->
          model.Runtimes
          |> CMap.addOrUpdate tid { Cooldown = 0f; Target = ValueNone }
      else
        let target = targetA |> AVal.getValue

        model.Runtimes
        |> CMap.addOrUpdate tid {
          Cooldown = cooldown'
          Target = target
        }

    (if isNull events then Array.empty else events)
