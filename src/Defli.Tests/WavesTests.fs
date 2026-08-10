module Defli.Tests.WavesTests

open Expecto
open AdaptiveSlop.Core
open Defli.World
open Defli.World.Systems
open Defli.World.Systems.Waves

let tests =
  testList "Waves" [
    testCase "composeWave scales with wave number" (fun () ->
      let w1 = Waves.composeWave 1
      let w5 = Waves.composeWave 5
      Expect.isGreaterThan w5.Count w1.Count "count grows"
      Expect.isLessThan w5.Interval w1.Interval "interval shrinks"
      Expect.isGreaterThanOrEqual w5.Interval 0.3f "interval floors"
      Expect.isGreaterThan w1.Count 0 "non-empty")

    testCase "difficulty tiers scale every 5 waves" (fun () ->
      // Waves 1-4: base stats. Wave 5: ×1.6 hp / ×1.07 speed / ×1.2
      // reward. Wave 10: the same multipliers squared.
      let hpOf(w: WaveDef) =
        let struct (def, _) = w.Table[0]
        def.Hp

      let w1 = Waves.composeWave 1
      let w5 = Waves.composeWave 5
      let w10 = Waves.composeWave 10

      Expect.equal (hpOf w1) EnemyDefs.grunt.Hp "wave 1 unscaled"

      Expect.equal
        (hpOf w5)
        (int(float EnemyDefs.grunt.Hp * 1.6))
        "wave 5 ×1.6"

      Expect.equal
        (hpOf w10)
        (int(float EnemyDefs.grunt.Hp * 1.6 * 1.6))
        "wave 10 ×1.6²"

      // Rewards scale too, and never collapse to zero.
      let rewardOf(w: WaveDef) =
        let struct (def, _) = w.Table[0]
        def.GoldReward

      Expect.equal
        (rewardOf w10)
        (int(float EnemyDefs.grunt.GoldReward * 1.2 * 1.2))
        "reward scaled"

      // The Scale aval follows WaveNumber (the projection contract).
      let m = Waves.init()
      Expect.equal (AVal.getValue m.Scale).Hp 1f "base scale"
      m.WaveNumber.Set 10

      Expect.equal
        (AVal.getValue m.Scale).Hp
        (float32(1.6 ** 2.0))
        "tier 2 scale")

    testCase "fliers enter the tables from wave 4" (fun () ->
      let w4 = Waves.composeWave 4

      Expect.contains
        (w4.Table |> Array.map(fun struct (def, _) -> def.Key))
        EnemyDefs.flier.Key
        "wave 4 has fliers"

      let w5 = Waves.composeWave 5

      Expect.contains
        (w5.Table |> Array.map(fun struct (def, _) -> def.Key))
        EnemyDefs.flier.Key
        "boss wave has fliers"

      let w2 = Waves.composeWave 2

      Expect.isFalse
        (w2.Table
         |> Array.exists(fun struct (def, _) -> def.Key = EnemyDefs.flier.Key))
        "early waves have no fliers")

    testCase "composition is deterministic (no RNG in the director)" (fun () ->
      let tableOf n =
        Waves.composeWave n
        |> fun w -> w.Table |> Array.map(fun struct (def, _) -> def.Key)

      Expect.equal (tableOf 4) (tableOf 4) "wave 4 table stable"
      Expect.equal (tableOf 5) (tableOf 5) "wave 5 table stable"
      Expect.equal (tableOf 12) (tableOf 12) "wave 12 table stable")

    testCase "boss waves (every 5th) lead with a tier-scaled boss" (fun () ->
      let w5 = Waves.composeWave 5
      Expect.hasLength w5.ExtraSpawns 1 "one extra spawn"

      let struct (bossDef, delay) = w5.ExtraSpawns[0]
      Expect.equal bossDef.Archetype EnemyArchetype.Boss "boss leads"
      Expect.equal delay 1.5f "spawns with the initial delay"

      // Wave 5 is tier 1: ×1.6 HP. Expected values mirror the impl's
      // float32 math exactly (1.6² in float32 is 2.5599999).
      let expectedHp n =
        max 1 (int(float EnemyDefs.boss.Hp * float (WaveScale.ofWave n).Hp))

      Expect.equal bossDef.Hp (expectedHp 5) "tier-scaled hp"

      let w10 = Waves.composeWave 10
      let struct (boss10, _) = w10.ExtraSpawns[0]

      Expect.equal boss10.Hp (expectedHp 10) "wave 10 ×1.6²"

      // Regular waves have no extras.
      for n in [ 1; 2; 3; 4; 6; 7 ] do
        Expect.isEmpty
          (Waves.composeWave n).ExtraSpawns
          $"wave %d{n} no extras")

    testCase "StartNextWave composes + activates, then refuses" (fun () ->
      let m = Waves.init()
      let events = Waves.update WaveMsg.StartNextWave m
      let m' = m

      match events with
      | [| WaveStarted wave |] ->
        Expect.equal wave.Count (Waves.composeWave 1).Count "wave 1"
      | _ -> failtest "expected WaveStarted"

      Expect.isTrue m'.WaveActive.Value "active"
      Expect.equal m'.WaveNumber.Value 1 "wave number"

      let events = Waves.update WaveMsg.StartNextWave m'
      let m2 = m'
      Expect.equal events.Length 0 "refuses while active"
      Expect.equal m2.WaveNumber.Value 1 "wave number unchanged")

    testCase "clear detection via direct values" (fun () ->
      let m = Waves.init()
      let _ = Waves.update WaveMsg.StartNextWave m
      let m' = m

      // Still spawning: no clear.
      let events = Waves.tick 0.1f m' (AVal.constant 3) false
      let m2 = m'
      Expect.equal (events |> Seq.length) 0 "not cleared with enemies alive"

      // Queue empty and no enemies: cleared.
      let events = Waves.tick 0.1f m2 (AVal.constant 0) true
      let m3 = m2

      match events |> Seq.tryHead with
      | Some WaveCleared -> ()
      | _ -> failtest "expected WaveCleared"

      Expect.isFalse m3.WaveActive.Value "inactive after clear")

    testCase "banner projection follows state" (fun () ->
      let m = Waves.init()

      Expect.stringContains
        (AVal.getValue m.Banner)
        "Press Enter"
        "idle banner"

      let _ = Waves.update WaveMsg.StartNextWave m
      let m' = m
      Expect.stringContains (AVal.getValue m'.Banner) "Wave 1" "active banner")
  ]
