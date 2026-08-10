module Defli.Tests.EnemiesTests

open System.Numerics
open Expecto
open AdaptiveSlop.Core
open Defli.World
open Defli.World.Systems
open TestData
open Defli.World.Systems.Enemies

let private cfg = TestData.Fixtures.cfg
let private map = MapModel.create cfg
let private model() = Enemies.init()

let private aliveCount(m: EnemiesModel) = m.Alive |> AMap.count |> AVal.getValue

let private viewsCount(m: EnemiesModel) = m.Views |> AMap.count |> AVal.getValue

let private hpOf (m: EnemiesModel) (eid: int<EnemyId>) =
  m.Healths |> CMap.tryGetValue eid

let tests =
  testList "Enemies" [
    testCase "spawn adds rows to all maps + projections" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.grunt) m map.Path
      let m' = m

      Expect.equal ((m'.Healths |> AMap.getValue).Count) 1 "healths"
      Expect.equal ((m'.Motions |> AMap.getValue).Count) 1 "motions"
      Expect.equal ((m'.Positions |> AMap.getValue).Count) 1 "positions"
      Expect.equal ((m'.Defs |> AMap.getValue).Count) 1 "defs"
      Expect.equal (aliveCount m') 1 "alive"
      Expect.equal (viewsCount m') 1 "views"

      Expect.equal
        ((m'.Positions |> AMap.getValue)[0<EnemyId>])
        map.Path[0]
        "starts at spawn")

    testCase "spawn is atomic across maps" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.tank) m map.Path
      let m' = m

      // All four rows share the same key.
      let eid = 0<EnemyId>
      Expect.isTrue ((m'.Healths |> CMap.tryGetValue eid).IsSome) "health"
      Expect.isTrue ((m'.Motions |> CMap.tryGetValue eid).IsSome) "motion"
      Expect.isTrue ((m'.Positions |> CMap.tryGetValue eid).IsSome) "position"
      Expect.isTrue ((m'.Defs |> CMap.tryGetValue eid).IsSome) "def")

    testCase
      "SpawnAt writes rows at the given position and path state"
      (fun () ->
        let m = model()
        let pos = Vector2(100f, 200f)

        let _ =
          Enemies.update
            (EnemyMsg.SpawnAt(Fixtures.grunt, pos, 0.5f, 2))
            m
            map.Path

        let m' = m
        let eid = 0<EnemyId>

        match m'.Positions |> CMap.tryGetValue eid with
        | ValueSome p -> Expect.equal p pos "explicit position"
        | ValueNone -> failtest "position must exist"

        match m'.Motions |> CMap.tryGetValue eid with
        | ValueSome mv ->
          Expect.equal mv.Progress 0.5f "explicit progress"
          Expect.equal mv.PathIndex 2 "explicit path index"
        | ValueNone -> failtest "motion must exist"

        // Healths/Defs written like a regular spawn; projections agree.
        Expect.equal (aliveCount m') 1 "alive")

    testCase "damage reduces HP; death emits Killed with reward" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.grunt) m map.Path
      let m' = m

      let eid = 0<EnemyId>

      let events = Enemies.update (EnemyMsg.ApplyDamage(eid, 10)) m' map.Path
      let m2 = m'

      Expect.equal events.Length 0 "not dead yet"

      match hpOf m2 eid with
      | ValueSome h ->
        Expect.equal h.Hp (Fixtures.grunt.Hp - 10) "hp after 10 damage"
      | ValueNone -> failtest "enemy must exist"

      let events = Enemies.update (EnemyMsg.ApplyDamage(eid, 100)) m2 map.Path
      let m3 = m2

      match events with
      | [| Killed(dead, reward) |] ->
        Expect.equal dead eid "killed id"
        Expect.equal reward Fixtures.grunt.GoldReward "reward"
      | _ -> failtest "expected exactly one Killed"

      // Alive excludes the corpse; Views still joins it (Hp = 0).
      Expect.equal (aliveCount m3) 0 "alive excludes dead"
      Expect.equal (viewsCount m3) 1 "views keeps corpse until despawn")

    testCase "despawn removes rows everywhere" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.runner) m map.Path
      let m' = m

      let _ = Enemies.update (EnemyMsg.Despawn(0<EnemyId>)) m' map.Path
      let m2 = m'

      Expect.equal ((m2.Healths |> AMap.getValue).Count) 0 "healths"
      Expect.equal ((m2.Motions |> AMap.getValue).Count) 0 "motions"
      Expect.equal ((m2.Positions |> AMap.getValue).Count) 0 "positions"
      Expect.equal ((m2.Defs |> AMap.getValue).Count) 0 "defs"
      Expect.equal (aliveCount m2) 0 "alive"
      Expect.equal (viewsCount m2) 0 "views")

    testCase "movement advances along waypoints" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.runner) m map.Path
      let m' = m

      let eid = 0<EnemyId>

      // Runner: 90 px/s; 1 second moves ~90px (waypoint 0→1 is 448px).
      let _ = Enemies.tick 1.0f m' map.Path
      let m2 = m'

      match m2.Positions |> CMap.tryGetValue eid with
      | ValueSome pos ->
        Expect.equal pos.X (map.Path[0].X + 90f) "moved along segment"
      | ValueNone -> failtest "enemy must exist")

    testCase "arrival at base emits ReachedBase and removes rows" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.runner) m map.Path
      let m' = m

      let eid = 0<EnemyId>

      // Path is 29 cells = 1856 px; runner at 90 px/s needs ~21s.
      let mutable m2 = m'
      let mutable events: EnemyEvent seq = Array.empty

      for _ in 1..260 do
        let ev = Enemies.tick 0.1f m2 map.Path
        let m3 = m2
        m2 <- m3

        if ev |> Seq.length > 0 then
          events <- ev

      match events |> Seq.tryHead with
      | Some(ReachedBase eid) ->
        Expect.equal
          ((m2.Healths |> AMap.getValue).Count)
          0
          "removed on arrival"

        Expect.equal (aliveCount m2) 0 "alive empty"
      | _ -> failtest "expected ReachedBase")

    testCase "slow modifies speed and expires" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.grunt) m map.Path
      let m' = m

      let eid = 0<EnemyId>

      let _ =
        Enemies.update
          (EnemyMsg.ApplySlow {
            Enemy = eid
            Factor = 0.5f
            Seconds = 1.0f
          })
          m'
          map.Path

      let m2 = m'

      match m2.Motions |> CMap.tryGetValue eid with
      | ValueSome mv -> Expect.equal mv.Slow 0.5f "slowed"
      | ValueNone -> failtest "enemy must exist"

      let _ = Enemies.tick 0.5f m2 map.Path
      let m3 = m2
      let _ = Enemies.tick 0.5f m3 map.Path
      let m4 = m3

      match m4.Motions |> CMap.tryGetValue eid with
      | ValueSome mv -> Expect.equal mv.Slow 1f "slow expired"
      | ValueNone -> failtest "enemy must exist")

    // ── Phase 3: archetypes ──

    testCase "flier flies the straight line spawn → base" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.flier) m map.Path
      let m' = m

      let eid = 0<EnemyId>
      let spawn = map.Path[0]
      let basePos = map.Path[map.Path.Length - 1]
      let flyDist = Vector2.Distance(spawn, basePos)

      // 1 second at 60 px/s (fixture speed) → 60 px along the line.
      let _ = Enemies.tick 1.0f m' map.Path
      let m2 = m'

      match m2.Positions |> CMap.tryGetValue eid with
      | ValueSome pos ->
        let expected = Vector2.Lerp(spawn, basePos, 60f / flyDist)
        Expect.equal pos expected "on the straight line"

        // The road's second waypoint is NOT on that line (the road
        // bends) — the flier must not be near it.
        Expect.isGreaterThan
          (Vector2.Distance(pos, map.Path[1]))
          100f
          "off the road"
      | ValueNone -> failtest "flier must exist"

      match m2.Motions |> CMap.tryGetValue eid with
      | ValueSome mv -> Expect.equal mv.PathIndex 0 "no waypoint walking"
      | ValueNone -> failtest "motion must exist")

    testCase "flier arrives at the base and emits ReachedBase" (fun () ->
      let m = model()

      let _ = Enemies.update (EnemyMsg.Spawn Fixtures.flier) m map.Path
      let m' = m

      let eid = 0<EnemyId>
      let spawn = map.Path[0]
      let basePos = map.Path[map.Path.Length - 1]
      let flyDist = Vector2.Distance(spawn, basePos)
      let seconds = flyDist / Fixtures.flier.Speed + 5f

      let mutable m2 = m'
      let mutable events: EnemyEvent seq = Array.empty

      for _ in 1 .. int(seconds / 0.5f) do
        let ev = Enemies.tick 0.5f m2 map.Path
        let m3 = m2
        m2 <- m3

        if ev |> Seq.length > 0 then
          events <- ev

      match events |> Seq.tryHead with
      | Some(ReachedBase arrived) ->
        Expect.equal arrived eid "arrived id"
        Expect.equal ((m2.Positions |> AMap.getValue).Count) 0 "removed"
      | _ -> failtest "expected ReachedBase")
  ]
