module Defli.Tests.RouterTests

open Expecto
open System.Collections.Generic
open AdaptiveSlop.Core
open Defli
open Defli.World
open Defli.World.Systems
open Defli.World.Systems.Waves
open Defli.World.Systems.Economy

// ─────────────────────────────────────────────────────────────
// End-to-end through the AdaptiveHeadless harness: the router (the
// world's step) drives the sub-systems, and the cold paths are the
// host-facing handlers — no Msg, no Cmd, no Dispatch. Assertions
// read the model (component maps + projections) after virtual-time
// stepping.
// ─────────────────────────────────────────────────────────────

let private cfg = TestData.Fixtures.cfg

let private goldOf(m: World) = AVal.getValue m.Economy.Gold
let private livesOf(m: World) = AVal.getValue m.Economy.Lives

let private aliveOf(m: World) =
  m.Enemies.Alive |> AMap.count |> AVal.getValue

// ── Phase 6: boss-wave helpers ──

/// Jump the director to a boss wave and start it.
let private startBossWave(h: TestData.Harness) =
  h.World.Waves.WaveNumber.Set 4 // next StartNextWave → wave 5
  Router.startNextWave h.World

let private bossIdOf(m: World) =
  m.Enemies.Defs
  |> AMap.getValue
  |> Seq.tryPick(fun (KeyValueV(eid, d)) ->
    if d.Archetype = EnemyArchetype.Boss then Some eid else None)

let tests =
  testList "Router (e2e)" [
    testCase "wave 1 runs to completion: spawn → walk → leak → clear" (fun () ->
      let h = TestData.mkHarness cfg
      let livesStart = livesOf h.World

      Router.startNextWave h.World

      // StepUntil: wave starts, spawns drain, enemies walk, all leak,
      // the wave clears.
      let cleared =
        h.StepUntil(
          (fun m ->
            not(AVal.getValue m.Waves.WaveActive)
            && AVal.getValue m.Waves.WaveNumber >= 1),
          TestData.dt,
          4000
        )

      Expect.isTrue cleared "wave cleared within budget"

      let model = h.World
      Expect.equal (AVal.getValue model.Waves.WaveNumber) 1 "wave number"
      Expect.equal (aliveOf model) 0 "no enemies alive"
      Expect.equal model.Spawning.Queue.Count 0 "queue drained"

      // No towers in Phase 1: every wave-1 enemy leaks.
      let wave1 = Waves.composeWave 1

      Expect.equal
        (livesOf model)
        (livesStart - wave1.Count)
        "lives lost to leaks"

      // Gold: starting + wave-clear bonus (no kills yet).
      Expect.equal
        (goldOf model)
        (cfg.StartingGold + cfg.WaveClearBonus)
        "gold after clear")

    testCase "game over blocks new waves" (fun () ->
      let h = TestData.mkHarness cfg

      // Drain all lives through the economy (handled synchronously).
      for _ in 1 .. cfg.StartingLives do
        Router.applyEconomyMsg h.World (EconomyMsg.LoseLife)

      h.StepN(2, TestData.dt)

      Expect.isTrue (AVal.getValue h.World.Economy.GameOver) "game over"

      Router.startNextWave h.World
      h.StepN(20, TestData.dt)

      let model = h.World
      Expect.equal (AVal.getValue model.Waves.WaveNumber) 0 "no wave started"
      Expect.isFalse (AVal.getValue model.Waves.WaveActive) "not active")

    testCase "deterministic run: same seed, same outcome" (fun () ->
      let run() =
        let h = TestData.mkHarness cfg
        Router.startNextWave h.World

        h.StepUntil(
          (fun m ->
            not(AVal.getValue m.Waves.WaveActive)
            && AVal.getValue m.Waves.WaveNumber >= 1),
          TestData.dt,
          4000
        )
        |> ignore

        // Fingerprint: gold, lives, wave number.
        let model = h.World

        struct (goldOf model,
                livesOf model,
                AVal.getValue model.Waves.WaveNumber)

      Expect.equal (run()) (run()) "same seed, same fingerprint")

    // ── Phase 2: towers & projectiles through the router ──

    testCase "PlaceTower on buildable cell spends gold and places" (fun () ->
      let h = TestData.mkHarness cfg
      let cell = struct (1, 1) // grass, not path, not occupied

      Router.placeTower h.World cell |> ignore
      h.StepN(2, TestData.dt)

      let model = h.World

      Expect.equal
        (goldOf model)
        (cfg.StartingGold - TowerDefs.arrow.Cost)
        "gold spent"

      match model.Towers.CellIndex |> CMap.tryGetValue cell with
      | ValueSome _ -> ()
      | ValueNone -> failtest "tower must be placed"

      Expect.equal
        ((model.Towers.Statics |> AMap.getValue).Count)
        1
        "one tower")

    testCase "PlaceTower on path cell is rejected" (fun () ->
      let h = TestData.mkHarness cfg
      let cell = struct (1, 4) // the road (spawn row)

      Router.placeTower h.World cell |> ignore
      h.StepN(2, TestData.dt)

      let model = h.World
      let statistics = model.Towers.Statics
      Expect.equal (goldOf model) cfg.StartingGold "no gold spent"
      Expect.equal (statistics |> AMap.count |> AVal.getValue) 0 "no tower")

    testCase "PlaceTower on an occupied cell is rejected" (fun () ->
      let h = TestData.mkHarness cfg
      let cell = struct (1, 1)

      Router.placeTower h.World cell |> ignore
      h.StepN(2, TestData.dt)

      Router.placeTower h.World cell |> ignore
      h.StepN(2, TestData.dt)

      let model = h.World

      Expect.equal
        ((model.Towers.Statics |> AMap.getValue).Count)
        1
        "still one tower"

      Expect.equal
        (goldOf model)
        (cfg.StartingGold - TowerDefs.arrow.Cost)
        "gold spent once")

    testCase "PlaceTower without enough gold is rejected" (fun () ->
      let h = TestData.mkHarness cfg

      // Drain gold below the cost.
      Router.applyEconomyMsg
        h.World
        (EconomyMsg.SpendGold(cfg.StartingGold - 1))

      h.StepN(2, TestData.dt)

      Router.placeTower h.World struct (1, 1) |> ignore
      h.StepN(2, TestData.dt)

      let model = h.World
      Expect.equal ((model.Towers.Statics |> AMap.getValue).Count) 0 "no tower"
      Expect.equal (goldOf model) 1 "gold untouched")

    testCase
      "tower fires → projectile homes → impact damages the enemy"
      (fun () ->
        let h = TestData.mkHarness cfg

        // Place a tower next to the path (the road runs along row 4).
        Router.placeTower h.World struct (2, 3) |> ignore
        h.StepN(2, TestData.dt)

        // Spawn one grunt on the path in range of the tower.
        Router.applyEnemyMsg
          h.World
          (Enemies.EnemyMsg.Spawn TestData.Fixtures.grunt)

        h.StepN(2, TestData.dt)

        // Step until the tower fires (cooldown 0.5 s) and the projectile
        // reaches the enemy (240 px/s, enemy ~64 px away).
        let fired =
          h.StepUntil(
            (fun m -> m.Projectiles.Rows |> AMap.count |> AVal.getValue > 0),
            TestData.dt,
            120
          )

        Expect.isTrue fired "tower fired within budget"

        let impacted =
          h.StepUntil(
            (fun m ->
              (m.Projectiles.Rows |> AMap.getValue).Count = 0
              && m.Enemies.Alive |> AMap.count |> AVal.getValue = 0),
            TestData.dt,
            120
          )

        Expect.isTrue impacted "enemy died to tower fire within budget"

        let model = h.World

        // Grunt died (despawned by the router): gold includes the reward.
        Expect.equal
          (goldOf model)
          (cfg.StartingGold - TowerDefs.arrow.Cost
           + TestData.Fixtures.grunt.GoldReward)
          "kill rewarded")

    testCase
      "splash fan-out survives a mid-loop boss kill (queued-pump ordering)"
      (fun () ->
        let h = TestData.mkHarness cfg

        // Cannon next to the path (the road runs along row 4). The
        // fixture gold (100) cannot afford a cannon (120) — top up.
        Router.applyEconomyMsg h.World (EconomyMsg.EarnGold 200)
        Router.selectTower h.World TowerDefs.cannon
        Router.placeTower h.World struct (2, 3) |> ignore
        h.StepN(2, TestData.dt)

        // A boss next to a runner on the path — ONE cannon blast
        // kills both. The boss's death used to despawn AND split
        // (spawnAt) DURING the splash fan-out's enumeration of
        // Positions; the transaction commits mutate the live
        // dictionary mid-loop and the enumerator throws (the wave-13
        // crash). The original queued the ApplyDamage messages and
        // the pump ran them after the fan-out — the direct handler
        // must defer the event handling the same way.
        Router.applyEnemyMsg
          h.World
          (Enemies.EnemyMsg.Spawn TestData.Fixtures.boss)

        Router.applyEnemyMsg
          h.World
          (Enemies.EnemyMsg.Spawn TestData.Fixtures.runner)

        h.StepN(2, TestData.dt)

        // Pre-damage the boss so the first blast kills it.
        let bossId =
          h.World.Enemies.Defs
          |> AMap.getValue
          |> Seq.pick(fun (KeyValueV(eid, d)) ->
            if d.Key = TestData.Fixtures.boss.Key then
              Some eid
            else
              None)

        Router.applyEnemyMsg
          h.World
          (Enemies.EnemyMsg.ApplyDamage(bossId, 190))

        let cleared =
          h.StepUntil(
            (fun m ->
              (m.Projectiles.Rows |> AMap.getValue).Count = 0
              && m.Enemies.Alive |> AMap.count |> AVal.getValue = 0),
            TestData.dt,
            300
          )

        Expect.isTrue
          cleared
          "boss and pack died to splash fire (no mid-loop throw)")

    testCase "upgrade through the router: gold spent, scaled damage" (fun () ->
      let h = TestData.mkHarness cfg

      Router.placeTower h.World struct (2, 3) |> ignore
      h.StepN(2, TestData.dt)

      // Upgrade the tower (arrow: UpgradeCost 40).
      Router.upgradeTower h.World struct (2, 3) |> ignore
      h.StepN(1, TestData.dt)

      let model = h.World

      Expect.equal
        (goldOf model)
        (cfg.StartingGold - TowerDefs.arrow.Cost - TowerDefs.arrow.UpgradeCost)
        "gold spent on upgrade"

      match model.Towers.Levels |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome lvl -> Expect.equal lvl 2 "level 2"
      | ValueNone -> failtest "level must exist"

      // The tower fires with the EFFECTIVE (scaled) damage.
      Router.applyEnemyMsg
        h.World
        (Enemies.EnemyMsg.Spawn TestData.Fixtures.grunt)

      let fired =
        h.StepUntil(
          (fun m -> (m.Projectiles.Rows |> AMap.getValue).Count > 0),
          TestData.dt,
          60
        )

      Expect.isTrue fired "tower fired after upgrade"

      match (h.World.Projectiles.Rows |> AMap.getValue) |> Seq.tryHead with
      | Some(KeyValueV(_, row)) ->
        Expect.equal
          row.Damage
          (int(float TowerDefs.arrow.Damage * 1.25))
          "scaled damage"
      | None -> failtest "projectile row must exist")

    testCase "upgrade is capped at MaxLevel" (fun () ->
      let h = TestData.mkHarness cfg
      Router.placeTower h.World struct (2, 3) |> ignore
      h.StepN(1, TestData.dt)

      // Top up so the full ladder is affordable. (The original MVU
      // pump checked all queued upgrades before any SpendGold ran —
      // four upgrades "fit" in 50 gold. The direct handlers validate
      // each spend against live gold: the ladder costs 4 × 40.)
      Router.applyEconomyMsg h.World (EconomyMsg.EarnGold 110)

      // Upgrade to the cap.
      for _ in 1 .. TowerDefs.arrow.MaxLevel - 1 do
        Router.upgradeTower h.World struct (2, 3) |> ignore

      h.StepN(2, TestData.dt)
      let goldBefore = goldOf h.World

      // Past the cap: nothing happens, no gold spent.
      Router.upgradeTower h.World struct (2, 3) |> ignore
      h.StepN(1, TestData.dt)

      Expect.equal (goldOf h.World) goldBefore "no gold spent at cap"

      match h.World.Towers.Levels |> CMap.tryGetValue(0<TowerId>) with
      | ValueSome lvl -> Expect.equal lvl TowerDefs.arrow.MaxLevel "capped"
      | ValueNone -> failtest "level must exist")

    testCase "frost tower through the router slows the enemy" (fun () ->
      let h = TestData.mkHarness cfg

      // Frost fires slower but applies the Slow factor on impact.
      Router.selectTower h.World TowerDefs.frost
      Router.placeTower h.World struct (1, 3) |> ignore
      h.StepN(2, TestData.dt)

      Router.applyEnemyMsg
        h.World
        (Enemies.EnemyMsg.Spawn TestData.Fixtures.grunt)

      // 1 s: first shot lands ~0.5 s in; the slow (2 s) must be live.
      h.StepN(10, TestData.dt)

      let model = h.World

      match model.Enemies.Motions |> CMap.tryGetValue(0<EnemyId>) with
      | ValueSome mv ->
        Expect.equal mv.Slow 0.5f "enemy slowed"

        let slowed =
          model.Enemies.SlowTimers |> Dictionary.tryGetValue(0<EnemyId>)

        Expect.isTrue slowed.IsSome "slow timer running"
      | ValueNone -> failtest "enemy must exist")

    // ── Phase 5: cannon splash through the router ──

    testCase "cannon splash kills a stacked pack, gold per victim" (fun () ->
      let h = TestData.mkHarness cfg

      // Cannon costs 120 > StartingGold 100 — top up first.
      Router.applyEconomyMsg h.World (EconomyMsg.EarnGold 60)
      Router.selectTower h.World TowerDefs.cannon
      Router.placeTower h.World struct (1, 3) |> ignore
      h.StepN(2, TestData.dt)

      // Two runners stacked on the same path cell (identical motion).
      Router.applyEnemyMsg
        h.World
        (Enemies.EnemyMsg.Spawn TestData.Fixtures.runner)

      Router.applyEnemyMsg
        h.World
        (Enemies.EnemyMsg.Spawn TestData.Fixtures.runner)

      h.StepN(2, TestData.dt)

      // One shell (25 dmg > 10 hp): the blast kills BOTH.
      let cleared =
        h.StepUntil(
          (fun m -> m.Enemies.Alive |> AMap.count |> AVal.getValue = 0),
          TestData.dt,
          120
        )

      Expect.isTrue cleared "pack died to the splash within budget"

      Expect.equal
        (goldOf h.World)
        (cfg.StartingGold + 60 - TowerDefs.cannon.Cost
         + 2 * TestData.Fixtures.runner.GoldReward)
        "both kills rewarded")

    testCase
      "target dies mid-flight: the shell detonates and splashes the pack"
      (fun () ->
        let h = TestData.mkHarness cfg

        Router.applyEconomyMsg h.World (EconomyMsg.EarnGold 60)
        Router.selectTower h.World TowerDefs.cannon
        Router.placeTower h.World struct (1, 3) |> ignore
        h.StepN(2, TestData.dt)

        Router.applyEnemyMsg
          h.World
          (Enemies.EnemyMsg.Spawn TestData.Fixtures.runner)

        Router.applyEnemyMsg
          h.World
          (Enemies.EnemyMsg.Spawn TestData.Fixtures.runner)

        // Wait for the cannon's shell to be in flight.
        let fired =
          h.StepUntil(
            (fun m -> (m.Projectiles.Rows |> AMap.getValue).Count > 0),
            TestData.dt,
            120
          )

        Expect.isTrue fired "cannon fired within budget"

        // Kill the shell's target mid-flight (another tower's kill, say).
        let target =
          (h.World.Projectiles.Rows |> AMap.getValue)
          |> Seq.head
          |> fun (KeyValueV(_, row)) -> row.TargetEnemy

        Router.applyEnemyMsg
          h.World
          (Enemies.EnemyMsg.ApplyDamage(target, 999))

        // The shell must NOT vanish: it flies to the corpse's last
        // position and the blast takes out the stacked survivor.
        let cleared =
          h.StepUntil(
            (fun m -> m.Enemies.Alive |> AMap.count |> AVal.getValue = 0),
            TestData.dt,
            120
          )

        Expect.isTrue cleared "survivor died to the detonation splash"

        Expect.equal
          (goldOf h.World)
          (cfg.StartingGold + 60 - TowerDefs.cannon.Cost
           + 2 * TestData.Fixtures.runner.GoldReward)
          "manual kill + splash kill both rewarded")

    // ── Phase 6: boss waves through the router ──

    testCase
      "boss wave: the boss spawns and suppresses a road-side tower"
      (fun () ->
        let h = TestData.mkHarness cfg

        Router.placeTower h.World struct (2, 3) |> ignore
        h.StepN(2, TestData.dt)

        startBossWave h

        // The boss leads (1.5 s delay) — it must appear among the defs.
        let bossUp =
          h.StepUntil((fun m -> (bossIdOf m).IsSome), TestData.dt, 60)

        Expect.isTrue bossUp "boss spawned"

        // The boss walks the road (row 4, y = 288); it enters the tower's
        // aura radius (128 px of (160, 224)) after ~5 s. Tower dps is far
        // too low to kill it first (arrow 22.5 dps vs 800 hp).
        let suppressed =
          h.StepUntil(
            (fun m ->
              m.Projections.Suppression
              |> AMap.getValue
              |> ReadOnlyDict.tryGetValue(0<TowerId>)
              |> ValueOption.exists(fun f -> f = BossAura.Factor)),
            TestData.dt,
            200
          )

        Expect.isTrue suppressed "tower suppressed while the boss is near")

    testCase
      "boss killed → split children spawn, wave does NOT clear early"
      (fun () ->
        let h = TestData.mkHarness cfg

        // No towers: nothing else kills the children; they will leak.
        startBossWave h

        let bossUp =
          h.StepUntil((fun m -> (bossIdOf m).IsSome), TestData.dt, 60)

        Expect.isTrue bossUp "boss spawned"

        match bossIdOf h.World with
        | Some bossId ->
          let aliveBefore = aliveOf h.World

          Router.applyEnemyMsg
            h.World
            (Enemies.EnemyMsg.ApplyDamage(bossId, 99999))

          h.StepN(2, TestData.dt)

          let model = h.World

          // The split is synchronous: children exist the same call.
          // alive = before − 1 (boss) + SplitCount (children).
          Expect.equal
            (aliveOf model)
            (aliveBefore - 1 + BossAura.SplitCount)
            "split children spawned"

          Expect.isTrue
            (AVal.getValue model.Waves.WaveActive)
            "the split frame must not clear the wave"

          // The boss paid its reward (kill) — children pay theirs on
          // death. Wave 5 is tier 1: the reward is scaled ×1.2.
          Expect.equal
            (goldOf model)
            (cfg.StartingGold + int(float EnemyDefs.boss.GoldReward * 1.2))
            "boss reward paid"

          // The wave eventually clears (children + pack leak; lives 20 ≥
          // wave-5 count 15 + 3 children + boss... boss was killed, so
          // 15 + 3 = 18 leaks ≤ 20 lives).
          let cleared =
            h.StepUntil(
              (fun m -> not(AVal.getValue m.Waves.WaveActive)),
              TestData.dt,
              4000
            )

          Expect.isTrue cleared "wave cleared after children leaked"
          Expect.isFalse (AVal.getValue model.Economy.GameOver) "survived"
        | None -> failtest "boss must exist")
  ]
