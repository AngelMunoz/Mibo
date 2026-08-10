module Defli.Tests.TowersTests

open System.Collections.Generic
open System.Numerics
open Expecto
open AdaptiveSlop.Core
open Defli
open Defli.World
open Defli.World.Systems
open TestData
open Defli.World.Systems.Towers

let private cfg = TestData.Fixtures.cfg
let private map = MapModel.create cfg
let private cellSize = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)
let private model() = Towers.init()

/// Test-owned tower def — distinct values catch production mix-ups.
let private def = {
  Key = "test_arrow"
  Name = "Test Arrow"
  Cost = 30
  Range = 2
  Damage = 5
  FireRate = 4f
  ProjectileSpeed = 200f
  Sprite = "rocket_pod_single"
  ProjectileSprite = "rocket_small"
  TargetPolicy = TargetPolicy.First
  SlowFactor = 1f
  SlowSeconds = 0f
  SplashRadius = 0f
  UpgradeCost = 20
  MaxLevel = 5
}

/// The fixture def with a specific targeting policy.
let private defWith(policy: TargetPolicy) = { def with TargetPolicy = policy }

/// A single enemy standing at a position (transient Alive-shaped dict).
let private enemyAt (pos: Vector2) (progress: float32) =
  let d = Dictionary<int<EnemyId>, EnemyView>()

  d[0<EnemyId>] <- {
    Pos = pos
    Hp = 100
    MaxHp = 100
    Progress = progress
    Slow = 1f
    PathIndex = 1
  }

  d

let private cellCenter(struct (x, y)) =
  Vector2(
    float32 x * cellSize.X + cellSize.X / 2f,
    float32 y * cellSize.Y + cellSize.Y / 2f
  )

/// No boss aura in scope — an empty suppression map (factor 1 = free).
let private noSuppression = Dictionary<int<TowerId>, float32>()

let tests =
  testList "Towers" [
    testCase "place writes Statics + Runtimes + CellIndex atomically" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.update (TowerMsg.Place(cell, def)) m
      let m' = m

      Expect.equal (m'.Statics |> AMap.getValue).Count 1 "statics"
      Expect.equal (m'.Runtimes |> AMap.getValue).Count 1 "runtimes"

      match m'.CellIndex |> CMap.tryGetValue cell with
      | ValueSome tid -> Expect.equal tid (0<TowerId>) "indexed"
      | ValueNone -> failtest "cell must be indexed"

      match m'.Statics |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome s -> Expect.equal s.Def def "def stored"
      | ValueNone -> failtest "tower must exist")

    testCase "no target in range → no fire, cooldown stays ready" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.update (TowerMsg.Place(cell, def)) m
      let m' = m

      // Range 2 cells ≈ 128 px; enemy is far away.
      let alive = AMap.constant(fun () -> enemyAt (Vector2(900f, 900f)) 0.5f)

      let events = Towers.tick 0.1f m' alive noSuppression cellSize
      let m2 = m'

      Expect.isEmpty events "no fire"
      Expect.equal (Seq.length events) 0 "no events"

      match m2.Runtimes |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome r -> Expect.equal r.Cooldown 0f "ready"
      | ValueNone -> failtest "runtime must exist")

    testCase "enemy in range → Fired with damage; cooldown set" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.update (TowerMsg.Place(cell, def)) m
      let m' = m

      // Tower center (3,3) = (224, 224); enemy one cell east = in range 2.
      let alive =
        AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

      let events = Towers.tick 0.1f m' alive noSuppression cellSize
      let m2 = m'

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.Tower (0<TowerId>) "tower id"
        Expect.equal shot.Enemy (0<EnemyId>) "enemy id"
        Expect.equal shot.Damage def.Damage "damage"
      | _ -> failtest "expected exactly one Fired"

      match m2.Runtimes |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome r ->
        Expect.equal r.Cooldown (1f / def.FireRate) "cooldown set"
        Expect.equal r.Target (ValueSome(0<EnemyId>)) "target stored"
      | ValueNone -> failtest "runtime must exist")

    testCase "first policy: picks the enemy closest to the base" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.update (TowerMsg.Place(cell, def)) m
      let m' = m

      // Two enemies both in range; the one with higher progress wins.
      let alive = Dictionary<int<EnemyId>, EnemyView>()

      alive[1<EnemyId>] <- {
        Pos = cellCenter struct (4, 3)
        Hp = 100
        MaxHp = 100
        Progress = 0.2f
        Slow = 1f
        PathIndex = 1
      }

      alive[2<EnemyId>] <- {
        Pos = cellCenter struct (4, 2)
        Hp = 100
        MaxHp = 100
        Progress = 0.8f
        Slow = 1f
        PathIndex = 1
      }

      let alive = AMap.constant(fun () -> alive)

      let events = Towers.tick 0.1f m' alive noSuppression cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.Enemy (2<EnemyId>) "first = highest progress"
      | _ -> failtest "expected exactly one Fired")

    // ── Phase 3: targeting policies ──

    /// Two in-range enemies: id 1 = progress 0.2, 100 hp / 100 max;
    /// id 2 = progress 0.8, 60 hp / 40 max (both at (4,3)).
    let twoInRange =
      let alive = Dictionary<int<EnemyId>, EnemyView>()

      alive[1<EnemyId>] <- {
        Pos = cellCenter struct (4, 3)
        Hp = 100
        MaxHp = 100
        Progress = 0.2f
        Slow = 1f
        PathIndex = 1
      }

      alive[2<EnemyId>] <- {
        Pos = cellCenter struct (4, 3)
        Hp = 60
        MaxHp = 40
        Progress = 0.8f
        Slow = 1f
        PathIndex = 1
      }

      alive

    /// The enemy id the policy picks from `twoInRange`.
    let picked(policy: TargetPolicy) : int<EnemyId> =
      let m = model()

      Towers.update (TowerMsg.Place(struct (3, 3), defWith policy)) m
      let m' = m

      let events =
        Towers.tick
          0.1f
          m'
          (AMap.constant(fun () -> twoInRange))
          noSuppression
          cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] -> shot.Enemy
      | _ -> failtest "expected exactly one Fired"

    testCase "policy Last: lowest progress wins" (fun () ->
      Expect.equal (picked TargetPolicy.Last) (1<EnemyId>) "last")

    testCase "policy Strongest: highest max HP wins" (fun () ->
      Expect.equal (picked TargetPolicy.Strongest) (1<EnemyId>) "strongest")

    testCase "policy Weakest: lowest current HP wins" (fun () ->
      Expect.equal (picked TargetPolicy.Weakest) (2<EnemyId>) "weakest")

    testCase "policy Closest: nearest enemy wins" (fun () ->
      let alive = Dictionary<int<EnemyId>, EnemyView>()

      alive[1<EnemyId>] <- {
        Pos = cellCenter struct (3, 3) + Vector2(40f, 0f)
        Hp = 100
        MaxHp = 100
        Progress = 0.5f
        Slow = 1f
        PathIndex = 1
      }

      alive[2<EnemyId>] <- {
        Pos = cellCenter struct (3, 3) + Vector2(100f, 0f)
        Hp = 100
        MaxHp = 100
        Progress = 0.5f
        Slow = 1f
        PathIndex = 1
      }

      let m = model()

      Towers.update
        (TowerMsg.Place(struct (3, 3), defWith TargetPolicy.Closest))
        m

      let m' = m

      let events =
        Towers.tick
          0.1f
          m'
          (AMap.constant(fun () -> alive))
          noSuppression
          cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] -> Expect.equal shot.Enemy (1<EnemyId>) "closest"
      | _ -> failtest "expected exactly one Fired")

    testCase "frost def → Fired carries the slow payload" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.update (TowerMsg.Place(cell, TowerDefs.frost)) m
      let m' = m

      let alive =
        AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

      let events = Towers.tick 0.1f m' alive noSuppression cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.SlowFactor TowerDefs.frost.SlowFactor "slow factor"

        Expect.equal
          shot.SlowSeconds
          TowerDefs.frost.SlowSeconds
          "slow seconds"
      | _ -> failtest "expected exactly one Fired")

    testCase "cannon def → Fired carries the splash + shell payload" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.update (TowerMsg.Place(cell, TowerDefs.cannon)) m
      let m' = m

      let alive =
        AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

      let events = Towers.tick 0.1f m' alive noSuppression cellSize

      match events |> Seq.toArray with
      | [| Fired shot |] ->
        Expect.equal shot.SplashRadius TowerDefs.cannon.SplashRadius "splash"

        Expect.equal
          shot.ProjectileSprite
          TowerDefs.cannon.ProjectileSprite
          "shell sprite"
      | _ -> failtest "expected exactly one Fired")

    testCase
      "cooldown comes from the EFFECTIVE def (upgraded fire rate)"
      (fun () ->
        let m = model()
        let cell = struct (3, 3)

        Towers.update (TowerMsg.Place(cell, def)) m
        let m' = m

        // Level 2: +10 % fire rate → the cooldown must shrink.
        Towers.update (TowerMsg.Upgrade(0<TowerId>)) m'
        let m2 = m'

        let alive =
          AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

        let events = Towers.tick 0.1f m2 alive noSuppression cellSize
        let m3 = m2

        Expect.equal (Seq.length events) 1 "fired"

        match m3.Runtimes |> CMap.tryGetValue(0<TowerId>) with
        | ValueSome r ->
          Expect.equal
            r.Cooldown
            (1f / (def.FireRate * 1.1f))
            "cooldown uses the upgraded fire rate"
        | ValueNone -> failtest "runtime must exist")

    testCase
      "boss aura suppression halves the fire rate (double cooldown)"
      (fun () ->
        let m = model()
        let cell = struct (3, 3)

        Towers.update (TowerMsg.Place(cell, def)) m
        let m' = m

        let suppression = Dictionary<int<TowerId>, float32>()
        suppression[0<TowerId>] <- BossAura.Factor

        let alive =
          AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

        let events = Towers.tick 0.1f m' alive suppression cellSize
        let m2 = m'

        Expect.equal (Seq.length events) 1 "fired"

        match m2.Runtimes |> CMap.tryGetValue(0<TowerId>) with
        | ValueSome r ->
          Expect.equal
            r.Cooldown
            (1f / (def.FireRate * BossAura.Factor))
            "suppressed cooldown"
        | ValueNone -> failtest "runtime must exist")

    testCase
      "Upgrade bumps the level; EffectiveDef composes scaled stats"
      (fun () ->
        let m = model()
        let cell = struct (3, 3)

        Towers.update (TowerMsg.Place(cell, def)) m
        let m' = m
        let tid = 0<TowerId>

        // Level 1 → the base def.
        let eff1 =
          m'.EffectiveDef |> AMap.getValue |> ReadOnlyDict.tryGetValue tid

        match eff1 with
        | ValueSome e -> Expect.equal e.Damage def.Damage "base damage"
        | ValueNone -> failtest "effective def must exist"

        // Level 2 → +25 % damage, +10 % fire rate, +0.5 range.
        Towers.update (TowerMsg.Upgrade tid) m'
        let m2 = m'

        match m2.Levels |> CMap.tryGetValue tid with
        | ValueSome lvl -> Expect.equal lvl 2 "level stored"
        | ValueNone -> failtest "level must exist"

        let eff2 =
          m2.EffectiveDef |> AMap.getValue |> ReadOnlyDict.tryGetValue tid

        match eff2 with
        | ValueSome e ->
          Expect.equal e.Damage (int(float def.Damage * 1.25)) "scaled damage"
          Expect.equal e.Range (def.Range + 0) "range round-half-down"
        | ValueNone -> failtest "effective def must exist")

    testCase "cooldown gates firing" (fun () ->
      let m = model()
      let cell = struct (3, 3)

      Towers.update (TowerMsg.Place(cell, def)) m
      let m' = m

      let alive =
        AMap.constant(fun () -> enemyAt (cellCenter struct (4, 3)) 0.5f)

      // Fire (cooldown = 0.25 at FireRate 4).
      let events = Towers.tick 0.1f m' alive noSuppression cellSize
      let m2 = m'

      Expect.equal (Seq.length events) 1 "fired once"

      // 0.1 s later: still cooling down (0.25 - 0.1 = 0.15).
      let events2 = Towers.tick 0.1f m2 alive noSuppression cellSize
      let m3 = m2

      Expect.isEmpty events2 "not ready yet"

      // 0.2 s more: 0.15 - 0.2 ≤ 0 → fires again.
      let events3 = Towers.tick 0.2f m3 alive noSuppression cellSize

      Expect.equal (Seq.length events3) 1 "fired again")
  ]
