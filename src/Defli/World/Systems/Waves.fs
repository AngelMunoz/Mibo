module Defli.World.Systems.Waves

open AdaptiveSlop.Core
open Defli
open Defli.World

// ─────────────────────────────────────────────────────────────
// Waves sub-system — the wave DIRECTOR: pure composition + state.
// No queue, no timing, no RNG (randomness lives in Spawning's
// picks — Kimo's rule: RNG streams are owned, never shared).
// Clear detection runs on direct values passed by the router
// (hot path, no closures).
// ─────────────────────────────────────────────────────────────

[<Struct>]
type WaveMsg = | StartNextWave

[<Struct>]
type WaveEvent =
  | WaveStarted of wave: WaveDef
  | WaveCleared

type WavesModel() =
  member val WaveNumber = CVal.create 0 with get, set
  member val WaveActive = CVal.create false with get, set
  member val Events = ResizeArray<WaveEvent>() with get, set
  /// Difficulty scale derived from WaveNumber (Phase 5: enemies get
  /// harder every 5 waves) — an aval projection over the wave state.
  member val Scale: aval<WaveScale> = Unchecked.defaultof<_> with get, set
  // Own HUD projection (showcase #9): wave banner text.
  member val Banner: aval<string> = Unchecked.defaultof<_> with get, set

module Waves =

  let inline private buildBanner(m: WavesModel) : aval<string> =
    m.WaveNumber
    |> AVal.map3
      (fun active scale number ->
        Telemetry.banner <- Telemetry.banner + 1

        if active then
          if scale.Hp > 1f then
            $"Wave %d{number}  ×%.2f{scale.Hp}"
          else
            $"Wave %d{number}"
        else
          $"Press Enter — Wave %d{number + 1}")
      m.WaveActive
      m.Scale

  let init() : WavesModel =
    let m = WavesModel()
    m.Scale <- AVal.map WaveScale.ofWave m.WaveNumber
    m.Banner <- buildBanner m
    m

  /// Deterministic composition per wave number — no RNG here; the
  /// weighted table is executed (picked) by Spawning. Escalation:
  /// tanks from wave 3, fliers from wave 4, boss waves (every 5th)
  /// mix all four archetypes AND lead with a boss (ExtraSpawns — a
  /// table entry would make the boss a dice roll). The difficulty
  /// scale (WaveScale — every 5 waves) is applied to the defs HERE,
  /// so the spawned defs carry the tier's stats.
  let composeWave(number: int) : WaveDef =
    let count = 5 + number * 2
    let interval = max 0.3f (1.2f - float32 number * 0.05f)
    let scale = WaveScale.ofWave number

    let inline scaleTable(table: struct (EnemyDef * int)[]) = [|
      for struct (def, w) in table -> struct (WaveScale.apply scale def, w)
    |]

    let table =
      if number % 5 = 0 then
        scaleTable [|
          struct (EnemyDefs.grunt, 3)
          struct (EnemyDefs.runner, 2)
          struct (EnemyDefs.tank, 2)
          struct (EnemyDefs.flier, 1)
        |]
      elif number % 4 = 0 then
        scaleTable [|
          struct (EnemyDefs.grunt, 2)
          struct (EnemyDefs.runner, 3)
          struct (EnemyDefs.flier, 2)
        |]
      elif number % 3 = 0 then
        scaleTable [|
          struct (EnemyDefs.grunt, 3)
          struct (EnemyDefs.runner, 4)
          struct (EnemyDefs.tank, 1)
        |]
      else
        scaleTable [|
          struct (EnemyDefs.grunt, 4)
          struct (EnemyDefs.runner, 2)
        |]

    // Boss waves: the boss leads the pack (spawns with the initial
    // delay, ahead of the weighted picks), tier-scaled like the rest.
    let extraSpawns =
      if number % 5 = 0 then
        [| struct (WaveScale.apply scale EnemyDefs.boss, 1.5f) |]
      else
        Array.empty

    {
      Table = table
      Count = count
      Interval = interval
      InitialDelay = 1.5f
      ExtraSpawns = extraSpawns
    }

  /// Cold path: start the next wave (no-op while one is active or the
  /// game is over — the router guards game-over).
  let update (msg: WaveMsg) (model: WavesModel) : WaveEvent[] =
    match msg with
    | StartNextWave ->
      let waveActive = model.WaveActive |> AVal.getValue

      if waveActive then
        Array.empty
      else
        let waveNumber = model.WaveNumber |> AVal.getValue
        let number = waveNumber + 1
        let wave = composeWave number

        Transaction.run(fun () ->
          model.WaveNumber.Set number
          model.WaveActive.Set true)

        [| WaveStarted wave |]

  /// Hot path — waves are MANUALLY gated: nothing runs while idle; the
  /// player presses Enter to start the next wave. `aliveCount` and
  /// `queueEmpty` are direct values from the router (Enemies.AliveCount
  /// aval + Spawning queue, respectively).
  let tick
    (dt: float32)
    (model: WavesModel)
    (aliveCount: aval<int>)
    (queueEmpty: bool)
    : WaveEvent seq =
    if model.WaveActive |> AVal.getValue then
      if aliveCount |> AVal.getValue = 0 && queueEmpty then
        model.WaveActive.Set false
        [| WaveCleared |]
      else
        Array.empty
    else
      Array.empty
