module Defli.Tests.ProjectionTests

open System.Numerics
open Expecto
open AdaptiveSlop.Core
open TestData
open Defli.World
open Defli.World.Systems
open Defli
open Defli.World.Systems.Enemies
open Defli.World.Systems.Economy

// ─────────────────────────────────────────────────────────────
// The ECS "query returns what the tables contain" contract: the
// reactive projections must agree with the component maps at
// every step. These are the AdaptiveSlop stress assertions.
// ─────────────────────────────────────────────────────────────

let private cfg = Fixtures.cfg
let private map = MapModel.create cfg

let private aliveView(m: EnemiesModel) = m.Alive |> AMap.getValue

let private viewsView(m: EnemiesModel) = m.Views |> AMap.getValue

let private spawn (m: EnemiesModel) (def: EnemyDef) =
  let _ = Enemies.update (EnemyMsg.Spawn def) m map.Path
  let m' = m
  m'

let tests =
  testList "Projections" [
    testCase "Views joins all three component maps" (fun () ->
      let m = spawn (Enemies.init()) Fixtures.grunt
      let views = viewsView m
      Expect.equal views.Count 1 "one row"

      for KeyValueV(eid, v) in views do
        Expect.equal v.Pos map.Path[0] "pos from Positions"
        Expect.equal v.Hp Fixtures.grunt.Hp "hp from Healths"
        Expect.equal v.MaxHp Fixtures.grunt.Hp "maxHp from Healths"
        Expect.equal v.Progress 0f "progress from Motions"
        Expect.equal v.Slow 1f "slow from Motions")

    testCase "damage delta lands on exactly the damaged enemy" (fun () ->
      let mutable m = Enemies.init()
      m <- spawn m Fixtures.grunt // id 0
      m <- spawn m Fixtures.tank // id 1
      m <- spawn m Fixtures.runner // id 2

      // Damage the tank (id 1).
      let _ = Enemies.update (EnemyMsg.ApplyDamage(1<EnemyId>, 50)) m map.Path
      let m' = m

      let expectedHp(eid: int<EnemyId>) =
        if eid = 1<EnemyId> then Fixtures.tank.Hp - 50
        elif eid = 0<EnemyId> then Fixtures.grunt.Hp
        else Fixtures.runner.Hp

      let views = viewsView m'

      for KeyValueV(eid, v) in views do
        Expect.equal v.Hp (expectedHp eid) $"hp of enemy %d{int eid}"

      // Alive still holds all three (nothing dead).
      Expect.equal (aliveView m').Count 3 "all alive")

    testCase "Alive drops corpses; Views keeps them until despawn" (fun () ->
      let mutable m = spawn (Enemies.init()) Fixtures.grunt
      m <- spawn m Fixtures.runner

      let _ = Enemies.update (EnemyMsg.ApplyDamage(0<EnemyId>, 999)) m map.Path
      let m' = m

      Expect.equal (aliveView m').Count 1 "only runner alive"
      Expect.equal (viewsView m').Count 2 "corpse still joined"

      Expect.equal
        (m'.Alive |> AMap.count |> AVal.getValue)
        1
        "count follows Alive"

      let _ = Enemies.update (EnemyMsg.Despawn(0<EnemyId>)) m' map.Path
      let m2 = m'

      Expect.equal (viewsView m2).Count 1 "corpse removed"

      Expect.equal
        (m2.Alive |> AMap.count |> AVal.getValue)
        1
        "count unchanged")

    testCase "repeated reads at a settled state are stable" (fun () ->
      let mutable m = spawn (Enemies.init()) Fixtures.tank

      let _ = Enemies.update (EnemyMsg.ApplyDamage(0<EnemyId>, 10)) m map.Path
      let m' = m

      let first = viewsView m'
      let second = viewsView m'
      Expect.equal first.Count second.Count "stable count"

      for KeyValueV(eid, v) in first do
        match second |> ReadOnlyDict.tryGetValue eid with
        | ValueSome v2 -> Expect.equal v v2 "stable row"
        | ValueNone -> failtest "row vanished")

    testCase
      "PlacementPreview affordability follows the selected tower"
      (fun () ->
        let economy = Economy.Economy.init cfg // gold 100
        let towers = Towers.Towers.init()
        let projectiles = Projectiles.Projectiles.init()
        let hover = CVal.create(ValueSome(struct (1, 1))) // buildable grass
        let selected = CVal.create TowerDefs.frost

        let projections =
          Projections(
            Enemies.init(),
            towers,
            projectiles,
            economy,
            MapModel.buildableGrid map,
            hover,
            selected
          )

        // Gold 100 ≥ frost 80 → affordable.
        Expect.equal
          (AVal.getValue projections.PlacementPreview)
          PlacementStatus.Affordable
          "affordable at 100"

        // 60: enough for the arrow (50), NOT for the frost (80) — the
        // preview must reflect the SELECTED tower, not the cheapest.
        economy.Gold |> CVal.set 60

        Expect.equal
          (AVal.getValue projections.PlacementPreview)
          PlacementStatus.TooExpensive
          "frost too expensive at 60"

        selected |> CVal.set TowerDefs.arrow

        Expect.equal
          (AVal.getValue projections.PlacementPreview)
          PlacementStatus.Affordable
          "arrow affordable at 60")

    testCase "game over aval follows lives" (fun () ->
      let e = Economy.init cfg
      Expect.isFalse (AVal.getValue e.GameOver) "not over"
      Economy.update EconomyMsg.LoseLife e
      Expect.isFalse (AVal.getValue e.GameOver) "still not over"

      for _ in 2 .. cfg.StartingLives do
        Economy.update EconomyMsg.LoseLife e

      Expect.isTrue (AVal.getValue e.GameOver) "over at zero")

    testCase "Suppression: boss in radius suppresses, others don't" (fun () ->
      let enemies = Enemies.init()
      let towers = Towers.Towers.init()

      // Tower at cell (2,3) — center (160, 224).
      Towers.Towers.update
        (Towers.TowerMsg.Place(struct (2, 3), TowerDefs.arrow))
        towers

      let t' = towers

      let projections =
        Projections(
          enemies,
          t',
          Projectiles.Projectiles.init(),
          Economy.Economy.init cfg,
          MapModel.buildableGrid map,
          CVal.create ValueNone,
          CVal.create TowerDefs.arrow
        )

      let factorOf(tid: int<TowerId>) =
        projections.Suppression
        |> AMap.getValue
        |> ReadOnlyDict.tryGetValue tid

      // No boss in the world → free (factor 1).
      Expect.equal (factorOf(0<TowerId>)) (ValueSome 1f) "free without boss"

      // Boss ON the tower's cell → suppressed.
      let _ =
        Enemies.update
          (EnemyMsg.SpawnAt(Fixtures.boss, Vector2(160f, 224f), 0f, 0))
          enemies
          map.Path

      let e2 = enemies

      Expect.equal
        (factorOf(0<TowerId>))
        (ValueSome BossAura.Factor)
        "suppressed in radius"

      // A non-boss at the same spot does NOT suppress.
      let _ = Enemies.update (EnemyMsg.Despawn(0<EnemyId>)) e2 map.Path
      let e3 = e2

      let _ =
        Enemies.update
          (EnemyMsg.SpawnAt(Fixtures.grunt, Vector2(160f, 224f), 0f, 0))
          e3
          map.Path

      let e4 = e3

      Expect.equal
        (factorOf(0<TowerId>))
        (ValueSome 1f)
        "grunts don't suppress"

      // A boss OUTSIDE the radius (200 px away > Radius 128) doesn't either.
      let _ = Enemies.update (EnemyMsg.Despawn(1<EnemyId>)) e4 map.Path
      let e5 = e4

      let _ =
        Enemies.update
          (EnemyMsg.SpawnAt(Fixtures.boss, Vector2(360f, 224f), 0f, 0))
          e5
          map.Path

      Expect.equal (factorOf(0<TowerId>)) (ValueSome 1f) "out of radius")
  ]
