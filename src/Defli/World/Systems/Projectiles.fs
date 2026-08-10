module Defli.World.Systems.Projectiles

open System
open System.Collections.Generic
open System.Numerics
open AdaptiveSlop.Core
open Mibo.Elmish
open Raylib_cs
open Defli.World

// ─────────────────────────────────────────────────────────────
// Projectiles sub-system — owns in-flight shots. One map is enough
// (projectiles have no cross-component reads). Its render position
// is the world-owned Homing projection (Projectiles.Rows ×
// Enemies.Positions — see Projections.fs).
//
// The homing feel: the projectile seeks the target's LIVE position
// row each tick (passed in as a direct transient read of
// Enemies.Positions — hot path, no closures). A target that despawns
// mid-flight leaves the shot seeking its LastTargetPos instead: it
// detonates there rather than vanishing mid-air.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type ProjectileMsg = Spawn of spawn: ProjectileSpawn

[<Struct>]
type ProjectileEvent = Impact of impact: ProjectileImpact

type ProjectilesModel() =
  member val Rows = CMap.empty<int<ProjectileId>, ProjectileRow> with get, set
  member val NextId = 0<ProjectileId> with get, set

module Projectiles =

  let private lifetime = 2.5f
  let private hitThreshold = 6f

  let init() = ProjectilesModel()

  /// Cold path: spawn one shot (router-translated from TowerEvent.Fired).
  let update (msg: ProjectileMsg) (model: ProjectilesModel) : unit =
    match msg with
    | Spawn spawn ->
      let pid = model.NextId
      model.NextId <- model.NextId + 1<ProjectileId>

      model.Rows
      |> CMap.addOrUpdate pid {
        Pos = spawn.Pos
        TargetEnemy = spawn.TargetEnemy
        LastTargetPos = spawn.LastTargetPos
        Damage = spawn.Damage
        Speed = spawn.Speed
        Lifetime = lifetime
        SlowFactor = spawn.SlowFactor
        SlowSeconds = spawn.SlowSeconds
        SplashRadius = spawn.SplashRadius
        ProjectileSprite = spawn.ProjectileSprite
      }

  /// Hot path: advance toward the target's live position; impact or
  /// expire. `positions` is a transient read of Enemies.Positions
  /// (direct value from the router). A target that despawns mid-flight
  /// no longer removes the shot: it flies on to the target's LAST
  /// RECORDED position and detonates there — no mid-air pop, and a
  /// splash shell still blasts the pack around the corpse (the impact
  /// carries the dead id; the router's splash fan-out hits whatever is
  /// actually near the point). Writes are collected and applied after
  /// iteration (transient views die on the next write).
  let tick
    (dt: float32)
    (model: ProjectilesModel)
    (positions: IReadOnlyDictionary<int<EnemyId>, Vector2>)
    : ProjectileEvent seq =
    let mutable events: ResizeArray<ProjectileEvent> = null

    let mutable updates: ResizeArray<struct (int<ProjectileId> * ProjectileRow)> =
      null

    let mutable removes: ResizeArray<int<ProjectileId>> = null

    for KeyValueV(pid, row) in model.Rows |> AMap.getValue do
      let lifetime = row.Lifetime - dt

      if lifetime <= 0f then
        if isNull removes then
          removes <- ResizeArray()

        removes.Add pid
      else
        // Live position while the target lives, last recorded after.
        let struct (targetPos, live) =
          positions
          |> ReadOnlyDict.tryGetValue row.TargetEnemy
          |> ValueOption.map(fun p -> struct (p, true))
          |> ValueOption.defaultValue struct (row.LastTargetPos, false)

        let d = targetPos - row.Pos
        let dist = d.Length()
        let step = row.Speed * dt

        if dist <= step + hitThreshold then
          if isNull events then
            events <- ResizeArray()

          events.Add(
            Impact {
              Projectile = pid
              Enemy = row.TargetEnemy
              Damage = row.Damage
              Pos = row.Pos
              SlowFactor = row.SlowFactor
              SlowSeconds = row.SlowSeconds
              SplashRadius = row.SplashRadius
            }
          )

          if isNull removes then
            removes <- ResizeArray()

          removes.Add pid
        else
          if isNull updates then
            updates <- ResizeArray()

          updates.Add
            struct (pid,
                    {
                      row with
                          Pos = row.Pos + (d / dist) * step
                          Lifetime = lifetime
                          LastTargetPos =
                            if live then targetPos else row.LastTargetPos
                    })

    if not(isNull updates) then
      for struct (pid, row) in updates do
        model.Rows |> CMap.addOrUpdate pid row

    if not(isNull removes) then
      Transaction.run(fun () ->
        for pid in removes do
          model.Rows |> CMap.remove pid)

    (if isNull events then Array.empty else events)
