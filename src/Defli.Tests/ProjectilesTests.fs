module Defli.Tests.ProjectilesTests

open System.Collections.Generic
open System.Numerics
open Expecto
open AdaptiveSlop.Core
open Defli
open Defli.World
open Defli.World.Systems
open TestData
open Defli.World.Systems.Projectiles
open Defli.World.Systems.Enemies

let private cfg = TestData.Fixtures.cfg
let private map = MapModel.create cfg
let private model() = Projectiles.init()

let private target = 0<EnemyId>

/// A transient Positions-shaped dict with one enemy at pos.
let private positionsAt(pos: Vector2) =
  let d = Dictionary<int<EnemyId>, Vector2>()
  d[target] <- pos
  d

let private spawnAt (m: ProjectilesModel) (pos: Vector2) =
  Projectiles.update
    (ProjectileMsg.Spawn {
      Pos = pos
      TargetEnemy = target
      LastTargetPos = pos
      Damage = 5
      Speed = 100f
      SlowFactor = 1f
      SlowSeconds = 0f
      SplashRadius = 0f
      ProjectileSprite = "rocket_small"
    })
    m

  m

let tests =
  testList "Projectiles" [
    testCase "spawn adds a row" (fun () ->
      let m = model()

      Projectiles.update
        (ProjectileMsg.Spawn {
          Pos = Vector2.Zero
          TargetEnemy = target
          LastTargetPos = Vector2.Zero
          Damage = 5
          Speed = 100f
          SlowFactor = 1f
          SlowSeconds = 0f
          SplashRadius = 0f
          ProjectileSprite = "rocket_small"
        })
        m

      Expect.equal ((m.Rows |> AMap.getValue).Count) 1 "one row"

      match m.Rows |> CMap.tryGetValue(0<ProjectileId>) with
      | ValueSome row ->
        Expect.equal row.TargetEnemy target "target"
        Expect.equal row.Damage 5 "damage"
      | ValueNone -> failtest "row must exist")

    testCase "homing: seeks the target's live position and impacts" (fun () ->
      let mutable m = model()
      m <- spawnAt m (Vector2(0f, 0f))

      // Enemy stands 50 px away; speed 100 px/s.
      let _ = Projectiles.tick 0.1f m (positionsAt(Vector2(50f, 0f)))
      let m2 = m

      match m2.Rows |> CMap.tryGetValue(0<ProjectileId>) with
      | ValueSome row -> Expect.equal row.Pos.X 10f "moved toward target"
      | ValueNone -> failtest "row must exist"

      // Enough time to cover the remaining 40 px.
      let events = Projectiles.tick 1.0f m2 (positionsAt(Vector2(50f, 0f)))
      let m3 = m2

      match events |> Seq.toArray with
      | [| Impact impact |] ->
        Expect.equal impact.Projectile (0<ProjectileId>) "projectile id"
        Expect.equal impact.Enemy target "enemy id"
        Expect.equal impact.Damage 5 "damage"
      | _ -> failtest "expected exactly one Impact"

      Expect.equal ((m3.Rows |> AMap.getValue).Count) 0 "removed on impact")

    testCase "spawn carries the slow payload to Impact" (fun () ->
      // Frost-style shot: slowFactor 0.5 for 2 s.
      let mutable m = model()

      Projectiles.update
        (ProjectileMsg.Spawn {
          Pos = Vector2(10f, 0f)
          TargetEnemy = target
          LastTargetPos = Vector2(10f, 0f)
          Damage = 4
          Speed = 200f
          SlowFactor = 0.5f
          SlowSeconds = 2f
          SplashRadius = 0f
          ProjectileSprite = "rocket_small"
        })
        m

      let m' = m

      let _ = Projectiles.tick 0.01f m (positionsAt(Vector2(50f, 0f)))
      let m2 = m

      // Enough time to cover the remaining 40 px.
      let events = Projectiles.tick 1.0f m2 (positionsAt(Vector2(50f, 0f)))
      let m3 = m2

      match events |> Seq.toArray with
      | [| Impact impact |] ->
        Expect.equal impact.Damage 4 "damage"
        Expect.equal impact.SlowFactor 0.5f "slow factor"
        Expect.equal impact.SlowSeconds 2f "slow seconds"
      | _ -> failtest "expected exactly one Impact"

      Expect.equal ((m3.Rows |> AMap.getValue).Count) 0 "removed on impact")

    testCase
      "target despawned mid-flight → detonates at the last recorded position"
      (fun () ->
        let mutable m = model()
        m <- spawnAt m (Vector2(0f, 0f))

        // One tick with the target alive at (50,0): the shot moves 10 px
        // and records the live position.
        let _ = Projectiles.tick 0.1f m (positionsAt(Vector2(50f, 0f)))
        let m2 = m

        match m2.Rows |> CMap.tryGetValue(0<ProjectileId>) with
        | ValueSome row ->
          Expect.equal row.LastTargetPos (Vector2(50f, 0f)) "live pos recorded"
        | ValueNone -> failtest "row must exist"

        // Target despawns: the shot keeps flying (no mid-air pop).
        let events =
          Projectiles.tick 0.1f m2 (Dictionary<int<EnemyId>, Vector2>())

        let m3 = m2

        Expect.isEmpty events "no impact yet"
        Expect.equal ((m3.Rows |> AMap.getValue).Count) 1 "still flying"

        match m3.Rows |> CMap.tryGetValue(0<ProjectileId>) with
        | ValueSome row ->
          Expect.equal row.Pos.X 20f "advanced toward the last pos"
          Expect.equal row.LastTargetPos (Vector2(50f, 0f)) "last pos kept"
        | ValueNone -> failtest "row must exist"

        // Enough time to cover the remaining 30 px → detonation, with the
        // DEAD target's id on the impact (the router no-ops its damage;
        // a splash payload would blast the point).
        let events2 =
          Projectiles.tick 1.0f m3 (Dictionary<int<EnemyId>, Vector2>())

        let m4 = m3

        match events2 |> Seq.toArray with
        | [| Impact impact |] ->
          Expect.equal impact.Enemy target "dead target id"
          Expect.equal impact.Pos.X 20f "detonated on arrival"
        | _ -> failtest "expected exactly one Impact"

        Expect.equal
          ((m4.Rows |> AMap.getValue).Count)
          0
          "removed on detonation")

    testCase "spawn carries the splash payload to Impact" (fun () ->
      let mutable m = model()

      Projectiles.update
        (ProjectileMsg.Spawn {
          Pos = Vector2(10f, 0f)
          TargetEnemy = target
          LastTargetPos = Vector2(10f, 0f)
          Damage = 25
          Speed = 200f
          SlowFactor = 1f
          SlowSeconds = 0f
          SplashRadius = 96f
          ProjectileSprite = "rocket_large"
        })
        m

      let m' = m

      match m.Rows |> CMap.tryGetValue(0<ProjectileId>) with
      | ValueSome row ->
        Expect.equal row.SplashRadius 96f "row splash"
        Expect.equal row.ProjectileSprite "rocket_large" "row sprite"
      | ValueNone -> failtest "row must exist"

      let _ = Projectiles.tick 0.01f m (positionsAt(Vector2(50f, 0f)))
      let m2 = m

      let events = Projectiles.tick 1.0f m2 (positionsAt(Vector2(50f, 0f)))

      match events |> Seq.toArray with
      | [| Impact impact |] ->
        Expect.equal impact.SplashRadius 96f "impact splash"
      | _ -> failtest "expected exactly one Impact")

    testCase "lifetime expiry removes the row" (fun () ->
      let mutable m = model()
      m <- spawnAt m (Vector2(0f, 0f))

      // Enemy is far; lifetime is 2.5 s → expires without impacting.
      let positions = positionsAt(Vector2(9999f, 9999f))

      for _ in 1..30 do
        let events = Projectiles.tick 0.1f m positions
        let m' = m
        m <- m'

        if not(Seq.isEmpty events) then
          failtest "must not impact at that distance"

      Expect.equal ((m.Rows |> AMap.getValue).Count) 0 "expired")

    testCase
      "Homing projection: render row tracks the target's live position"
      (fun () ->
        // World-owned projection — build the pieces it joins.
        let enemies = Enemies.Enemies.init()

        let _ =
          Enemies.Enemies.update
            (EnemyMsg.Spawn Fixtures.runner)
            enemies
            map.Path

        let enemies' = enemies
        let eid = 0<EnemyId>
        let projectiles = model()
        let projectiles' = spawnAt projectiles (Vector2(0f, 0f))
        let towers = Towers.Towers.init()
        let economy = Economy.Economy.init cfg
        let hover = CVal.create ValueNone

        let projections =
          Projections(
            enemies',
            towers,
            projectiles',
            economy,
            MapModel.buildableGrid map,
            hover,
            CVal.create TowerDefs.arrow
          )

        let rows = projections.Homing |> AMap.getValue
        Expect.equal rows.Count 1 "one homing row"

        for KeyValueV(pid, v) in rows do
          Expect.equal v.Pos Vector2.Zero "projectile pos"
          Expect.equal v.TargetPos map.Path[0] "target pos from Positions"

        // Move the enemy; the homing row follows.
        let _ = Enemies.Enemies.tick 1.0f enemies' map.Path
        let enemies2 = enemies'

        let rows2 = projections.Homing |> AMap.getValue

        for KeyValueV(pid, v) in rows2 do
          Expect.equal
            v.TargetPos
            (map.Path[0].X + 90f |> fun x -> Vector2(x, map.Path[0].Y))
            "tracked movement"

        // Kill the enemy (despawn): the homing entry STAYS — the render
        // row falls back to the projectile's LastTargetPos (the sim
        // flies the shot to the detonation point; no render-side pop).
        let _ = Enemies.Enemies.update (EnemyMsg.Despawn eid) enemies2 map.Path

        let enemies3 = enemies2
        let rows3 = projections.Homing |> AMap.getValue
        Expect.equal rows3.Count 1 "entry kept with the last recorded pos"

        for KeyValueV(pid, v) in rows3 do
          Expect.equal
            v.TargetPos
            Vector2.Zero
            "falls back to the row's LastTargetPos")
  ]
