namespace Defli.World

open System.Numerics
open AdaptiveSlop.Core
open Defli
open Mibo.Layout
open Defli.World.Systems

// ─────────────────────────────────────────────────────────────
// World-owned CROSS-subsystem projections — joins/filters that
// touch two systems' maps. Sub-systems own projections derived
// purely from their own maps (see each system file).
//
//   Homing (#3)          — Projectiles.Rows × Enemies.Positions
//                          (the AMap.joinOn showcase: per-projectile
//                          computed join key on the target's row)
//   Suppression (#12)    — Towers.Statics × Enemies.BossPositions
//                          (the SPATIAL join: per-tower filter over
//                          boss positions — Phase 6 boss aura)
//   RangeRing (#10)      — hover cell × Towers.CellIndex/Statics
//                          (AVal.bind UI-state join)
//   PlacementPreview (#5)— hover cell × Towers.CellIndex ×
//                          Economy.Gold × SelectedTower (per-hover
//                          map3 fan-in; the full-tile filterA variant
//                          is exactly the wide fan-out the join
//                          assessment flagged — the per-hover fan-in
//                          gives the same UX with a shallow graph)
// ─────────────────────────────────────────────────────────────

[<Sealed>]
type Projections
  (
    enemies: Enemies.EnemiesModel,
    towers: Towers.TowersModel,
    projectiles: Projectiles.ProjectilesModel,
    economy: Economy.EconomyModel,
    buildable: CellGrid2D<MapTile>,
    hover: aval<struct (int * int) voption>,
    selected: aval<TowerDef>
  ) =

  /// #3 Homing — one aval per projectile tracking its target's live
  /// position row through the graph, now as an AMap.joinOn: the join
  /// key is the target enemy, the per-projectile subgraph is built once
  /// and the position input swaps in place (no rebuild on update). A
  /// dead target (row removed from Enemies.Positions) yields ValueNone
  /// in the lookup and falls back to the projectile row's LastTargetPos:
  /// the render side keeps drawing the shot flying to the detonation
  /// point (the sim no longer removes it mid-flight).
  member val Homing: amap<int<ProjectileId>, HomingView> =
    AMap.joinOn
      projectiles.Rows
      enemies.Positions
      (fun _ (row: ProjectileRow) -> row.TargetEnemy)
      (fun _ (rowV: aval<ProjectileRow>) (posV: aval<Vector2 voption>) ->
        AVal.map2
          (fun (row: ProjectileRow) (pos: Vector2 voption) ->
            Telemetry.homingJoin <- Telemetry.homingJoin + 1

            ValueSome {
              Pos = row.Pos
              TargetPos = pos |> ValueOption.defaultValue row.LastTargetPos
              Sprite = row.ProjectileSprite
            })
          rowV
          posV)

  /// #12 Suppression (Phase 6) — per tower, is a live boss within
  /// BossAura.Radius of its cell? → the fire-rate factor (1 = free,
  /// Factor = suppressed). The SPATIAL-join stress case: boss
  /// positions change every frame, so every tower's filter/count node
  /// re-scans the boss map per frame — O(towers × bosses) of graph
  /// work per frame. Consumed as a DIRECT VALUE by Towers.tick (the
  /// router passes the transient view; nothing is written back into
  /// a changeable map).
  member val Suppression: amap<int<TowerId>, float32> =
    towers.Statics
    |> AMap.mapA(fun _ s ->
      let center =
        Cells.center
          s.Cell
          (Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize))

      enemies.BossPositions
      |> AMap.filter(fun _ bossPos ->
        Vector2.Distance(bossPos, center) <= BossAura.Radius)
      |> AMap.count
      |> AVal.map(fun n ->
        Telemetry.suppression <- Telemetry.suppression + 1
        if n > 0 then BossAura.Factor else 1f))

  /// #10 RangeRing — hovered own tower → its EFFECTIVE def (the view
  /// draws the range circle). The chain composes derived-on-derived:
  /// hover × CellIndex × (Statics × Levels) — the upgrade showcase.
  member val RangeRing: aval<TowerDef voption> =
    hover
    |> AVal.bind(fun cell ->
      match cell with
      | ValueNone -> AVal.constant ValueNone
      | ValueSome c ->
        Telemetry.rangeRing <- Telemetry.rangeRing + 1
        towers.CellIndex |> AMap.tryFind c)
    |> AVal.bind(fun tid ->
      match tid with
      | ValueNone -> AVal.constant ValueNone
      | ValueSome tid -> towers.EffectiveDef |> AMap.tryFind tid)

  /// #5 PlacementPreview — the hovered cell's build status: blocked
  /// (path/occupied/out of grid), affordable, or too expensive.
  /// map2 fan-in over Gold; re-derives only when hover or gold moves.
  member val PlacementPreview: aval<PlacementStatus> =
    hover
    |> AVal.bind(fun cell ->
      match cell with
      | ValueNone -> AVal.constant PlacementStatus.Hidden
      | ValueSome struct (x, y) ->
        // The Buildable layer row decides (road cells were stamped
        // over with the non-buildable path tile; out of grid = absent).
        let buildableOk =
          buildable |> CellGrid2D.get x y |> ValueOption.exists _.Buildable

        if not buildableOk then
          AVal.constant PlacementStatus.Blocked
        else
          let cellKey = struct (x, y)

          towers.CellIndex
          |> AMap.tryFind cellKey
          |> AVal.map3
            (fun gold def occupied ->
              Telemetry.placementPreview <- Telemetry.placementPreview + 1

              if ValueOption.isSome occupied then PlacementStatus.Blocked
              elif gold >= def.Cost then PlacementStatus.Affordable
              else PlacementStatus.TooExpensive)
            economy.Gold
            selected)
