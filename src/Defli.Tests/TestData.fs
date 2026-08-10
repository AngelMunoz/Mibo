module Defli.Tests.TestData

open System
open Mibo.Adaptive
open Defli.World

// ─────────────────────────────────────────────────────────────
// Test-owned fixtures — never production data (Kimo convention:
// tests build their own `test_*` stores/configs with distinct
// values, so a mix-up fails loudly and production tuning is
// never frozen by a test).
// ─────────────────────────────────────────────────────────────

module Fixtures =

  /// Test world config — distinct from WorldConfig.defaults.
  let cfg = {
    Seed = 7
    StartingGold = 100
    StartingLives = 20
    WaveClearBonus = 10
    GridCols = 20
    GridRows = 12
    // Tests rely on the fixed road (cell (1,1) buildable, row 4 is
    // the road) — the procedural variant is covered by its own tests.
    MapVariant = MapVariant.HandAuthored
  }

  /// Test enemy definitions — distinct values catch mix-ups.
  let grunt = {
    Key = "test_grunt"
    Archetype = EnemyArchetype.Grunt
    Hp = 30
    Speed = 40f
    GoldReward = 2
    Sprite = "tank_hull_green"
    Turret = ValueSome "tank_turret_green"
    TurretAngle = 0f
  }

  let runner = {
    Key = "test_runner"
    Archetype = EnemyArchetype.Runner
    Hp = 10
    Speed = 90f
    GoldReward = 3
    Sprite = "tank_hull_beige"
    Turret = ValueSome "turret_missiles_dual"
    TurretAngle = 90f
  }

  let tank = {
    Key = "test_tank"
    Archetype = EnemyArchetype.Tank
    Hp = 100
    Speed = 20f
    GoldReward = 5
    Sprite = "tank_hull_beige"
    Turret = ValueSome "tank_turret_beige"
    TurretAngle = 0f
  }

  /// Test flier — distinct values catch production mix-ups.
  let flier = {
    Key = "test_flier"
    Archetype = EnemyArchetype.Flier
    Hp = 15
    Speed = 60f
    GoldReward = 4
    Sprite = "plane_gray"
    Turret = ValueNone
    TurretAngle = 0f
  }

  /// Test boss — distinct values catch production mix-ups.
  let boss = {
    Key = "test_boss"
    Archetype = EnemyArchetype.Boss
    Hp = 200
    Speed = 30f
    GoldReward = 20
    Sprite = "tank_hull_beige"
    Turret = ValueSome "tank_turret_green"
    TurretAngle = 0f
  }

  let all = [| grunt; runner; tank; flier; boss |]

// ─────────────────────────────────────────────────────────────
// Headless harness over AdaptiveHeadless — the MVU shell is gone:
// the world is a composition root (State · Projection · Update ·
// Force). Tests drive the world's handlers directly and step
// virtual time; StepUntil polls the world's roots/projections.
// ─────────────────────────────────────────────────────────────

/// A world + runner pair. The runner forces the frame once per
/// Step; the tests read the world's projections and roots between
/// steps (same objects the frame packs).
type Harness(world: World, runner: AdaptiveHeadless<Frame.RenderFrame>) =
  member _.World = world

  member _.Step(dt: TimeSpan) : unit = runner.Step(dt) |> ignore

  member _.StepN(n: int, dt: TimeSpan) : unit =
    for _ in 1..n do
      runner.Step(dt) |> ignore

  /// Steps until the predicate holds or the budget runs out.
  /// Returns whether the predicate held.
  member _.StepUntil(pred: World -> bool, dt: TimeSpan, maxSteps: int) : bool =
    let mutable i = 0

    while not(pred world) && i < maxSteps do
      runner.Step(dt) |> ignore
      i <- i + 1

    pred world

let mkHarness(cfg: WorldConfig) =
  let world = World.init cfg

  let runner =
    new AdaptiveHeadless<Frame.RenderFrame>(Frame.adaptiveWorld world)

  Harness(world, runner)

/// Coarse step for e2e timing tests (the sim is dt-agnostic — the
/// movement/spawn math consumes dt directly).
let dt = TimeSpan.FromSeconds 0.1

/// Fine step for frame-accurate tests.
let frameDt = TimeSpan.FromSeconds(1.0 / 60.0)
