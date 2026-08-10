namespace Defli.World

open System
open System.Collections.Generic
open System.Numerics
open AdaptiveSlop.Core
open Mibo.Elmish
open Defli
open Defli.World.Systems

// ─────────────────────────────────────────────────────────────
// Router — the Update phase. `step` runs the nine systems in Kimo
// order; the handle* functions translate the systems' events
// directly (Defli sent them through the Cmd pump as WorldMsg —
// DispatchMode.Immediate ran them in the same frame; here the same
// logic runs in place, no queue, no Cmd). The cold paths are the
// host-facing handlers (startNextWave / placeTower / upgradeTower /
// selectTower / apply*Msg), validation unchanged from Defli's
// router.
//
// The event flow is one direction: systems emit, the handlers
// translate, and only ApplyDamage (→ Killed), FillWave (→
// SpawnEnemy), StartNextWave (→ WaveStarted) and the ticks emit —
// the cold-path updates return empty event lists, so nothing here
// recurses or re-enters the router.
// ─────────────────────────────────────────────────────────────

module Router =

  // ── Event translation ───────────────────────────────────────────

  let private handleEnemyEvents
    (world: World)
    (events: Enemies.EnemyEvent seq)
    : unit =
    for ev in events do
      match ev with
      | Enemies.Killed(eid, reward) ->
        let pos =
          world.Enemies.Positions
          |> CMap.tryGetValue eid
          |> ValueOption.defaultValue Vector2.Zero

        // The original router read the Defs row BEFORE its queued
        // Despawn Cmd ran (batch order); the direct handler must read
        // it before removing the row too — otherwise the boss split
        // dies with the corpse.
        let isBoss =
          world.Enemies.Defs
          |> CMap.tryGetValue eid
          |> ValueOption.exists(fun d -> d.Archetype = Boss)

        let struct (progress, pathIndex) =
          world.Enemies.Motions
          |> CMap.tryGetValue eid
          |> ValueOption.map(fun mv -> struct (mv.Progress, mv.PathIndex))
          |> ValueOption.defaultValue struct (0f, 0)

        Economy.Economy.update (Economy.EarnGold reward) world.Economy
        Enemies.Enemies.despawn eid world.Enemies world.Map.Path

        Vfx.Vfx.update (Vfx.Burst(Vfx.VfxKind.DeathPoof, pos)) world.Vfx

        // Boss split-on-death (Phase 6): grunts burst from the corpse.
        // Spawned SYNCHRONOUSLY (the FillWave-on-WaveStarted precedent):
        // a deferred round-trip would leave one frame with aliveCount =
        // 0 and the wave would clear before the children exist. Children
        // carry the wave's tier scale.
        if isBoss then
          let scale = AVal.getValue world.Waves.Scale
          let childDef = WaveScale.apply scale BossAura.SplitInto

          for i in 0 .. BossAura.SplitCount - 1 do
            // Small deterministic radial offsets so the children
            // don't stack on one pixel.
            let angle = float32 i / float32 BossAura.SplitCount * 2f * MathF.PI

            let childPos = pos + Vector2(MathF.Cos angle, MathF.Sin angle) * 16f

            Enemies.Enemies.spawnAt
              childDef
              childPos
              progress
              pathIndex
              world.Enemies
              world.Map.Path
      | Enemies.ReachedBase _ ->
        let basePos = Cells.center world.Map.BaseCell (World.cellSize world)

        Economy.Economy.update Economy.LoseLife world.Economy
        Vfx.Vfx.update (Vfx.Burst(Vfx.VfxKind.BaseHit, basePos)) world.Vfx
        Camera.Camera.update (Camera.Shake 8f) world.Camera

  let private handleSpawnEvents
    (world: World)
    (events: Spawning.SpawnEvent seq)
    : unit =
    for ev in events do
      match ev with
      | Spawning.SpawnEnemy def ->
        Enemies.Enemies.spawn def world.Enemies world.Map.Path
      | Spawning.SpawnFailed _ -> ()

  let private handleWaveEvents
    (world: World)
    (events: Waves.WaveEvent seq)
    : unit =
    for ev in events do
      match ev with
      | Waves.WaveStarted wave ->
        // Fill the spawn queue IN THE SAME CALL: a deferred round-trip
        // would leave one frame where the wave is active with an empty
        // queue, and the clear check would fire instantly (the wave
        // starts and clears without spawning anything).
        let spawnEvents =
          Spawning.Spawning.update (Spawning.FillWave wave) world.Spawning

        handleSpawnEvents world spawnEvents
      | Waves.WaveCleared ->
        Economy.Economy.update
          (Economy.EarnGold world.Config.WaveClearBonus)
          world.Economy

  let private handleProjectileEvents
    (world: World)
    (events: Projectiles.ProjectileEvent seq)
    : unit =
    for ev in events do
      match ev with
      | Projectiles.Impact impact ->
        if impact.SplashRadius > 0f then
          // Splash: the blast fans out from the DETONATION POINT to
          // every enemy within radius (flat full damage, no falloff).
          // Each ApplyDamage zero-crossing emits its own Killed →
          // gold + DeathPoof via handleEnemyEvents. A transient
          // read of Positions — cold path, once per impact.
          let positions = world.Enemies.Positions |> AMap.getValue

          for KeyValueV(eid, epos) in positions do
            if Vector2.Distance(epos, impact.Pos) <= impact.SplashRadius then
              let enemyEvents =
                Enemies.Enemies.applyDamage
                  eid
                  impact.Damage
                  world.Enemies
                  world.Map.Path

              handleEnemyEvents world enemyEvents

          Vfx.Vfx.update
            (Vfx.Burst(Vfx.VfxKind.Explosion, impact.Pos))
            world.Vfx
        else
          let enemyEvents =
            Enemies.Enemies.applyDamage
              impact.Enemy
              impact.Damage
              world.Enemies
              world.Map.Path

          handleEnemyEvents world enemyEvents

          if impact.SlowFactor < 1f then
            Enemies.Enemies.applySlow
              {
                Enemy = impact.Enemy
                Factor = impact.SlowFactor
                Seconds = impact.SlowSeconds
              }
              world.Enemies
              world.Map.Path

          Vfx.Vfx.update (Vfx.Burst(Vfx.VfxKind.Impact, impact.Pos)) world.Vfx

  let private handleTowerEvents
    (world: World)
    (events: Towers.TowerEvent seq)
    : unit =
    for ev in events do
      match ev with
      | Towers.Fired shot ->
        // Muzzle pos from the static row; projectile speed from the
        // EFFECTIVE def (the upgrade projection) — the +10 %/level
        // fire-rate/range upgrades must not be dropped here.
        let struct (pos, speed) =
          world.Towers.Statics
          |> CMap.tryGetValue shot.Tower
          |> ValueOption.map(fun s ->
            let eff =
              world.Towers.EffectiveDef
              |> AMap.getValue
              |> ReadOnlyDict.tryGetValue shot.Tower
              |> ValueOption.defaultValue s.Def

            struct (Cells.center s.Cell (World.cellSize world),
                    eff.ProjectileSpeed))
          |> ValueOption.defaultValue struct (Vector2.Zero, 0f)

        // Seed the shot's last-known target position from the live
        // row (fall back to the muzzle): a target that dies
        // mid-flight still gets detonated on.
        let lastTargetPos =
          world.Enemies.Positions
          |> CMap.tryGetValue shot.Enemy
          |> ValueOption.defaultValue pos

        Projectiles.Projectiles.update
          (Projectiles.Spawn {
            Pos = pos
            TargetEnemy = shot.Enemy
            LastTargetPos = lastTargetPos
            Damage = shot.Damage
            Speed = speed
            SlowFactor = shot.SlowFactor
            SlowSeconds = shot.SlowSeconds
            SplashRadius = shot.SplashRadius
            ProjectileSprite = shot.ProjectileSprite
          })
          world.Projectiles

        Vfx.Vfx.update (Vfx.Burst(Vfx.VfxKind.Muzzle, pos)) world.Vfx

  // ── The per-frame router ─────────────────────────────────────────

  /// The per-frame simulation: runs the systems in Kimo order and
  /// handles their events directly. Runs after the time root is
  /// written and before the frame is forced.
  let step (world: World) (gameTime: GameTime) : unit =
    if not(AVal.getValue world.Paused) then
      let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
      let t0 = Diagnostics.tickStart()

      // Kimo's system organization: movement/"physics" first, then the
      // spawn/queue phases; read-only consumers after.
      let enemyEvents = Enemies.Enemies.tick dt world.Enemies world.Map.Path

      let spawnEvents = Spawning.Spawning.tick dt world.Spawning

      let waveEvents =
        Waves.Waves.tick
          dt
          world.Waves
          world.AliveCount
          (world.Spawning.Queue.Count = 0)

      let towerEvents =
        // Suppression's chain (BossPositions chooseA → per-tower
        // filter → count) settles bottom-up on read: the count and
        // filter nodes pull their sources when versions differ
        // (AdaptiveSlop fix #18 — dirty-indicator Version). Reading
        // the tail alone is fresh; no pre-read needed.
        Towers.Towers.tick
          dt
          world.Towers
          world.Enemies.Alive
          (world.Projections.Suppression |> AMap.getValue)
          (World.cellSize world)

      let projectileEvents =
        Projectiles.Projectiles.tick
          dt
          world.Projectiles
          (world.Enemies.Positions |> AMap.getValue)

      Vfx.Vfx.tick dt world.Vfx
      Camera.Camera.tick dt world.Camera

      Diagnostics.tickEnd
        t0
        world.Diag
        world.AliveCount
        world.Spawning.Queue.Count

      // Events, in the same order Defli's Cmd batch translated them.
      handleEnemyEvents world enemyEvents
      handleSpawnEvents world spawnEvents
      handleWaveEvents world waveEvents
      handleTowerEvents world towerEvents
      handleProjectileEvents world projectileEvents

  // ── Cold paths — plain handlers, called by the host ──────────────
  // Validation stays exactly where Defli had it (in the router).

  /// The player starts the next wave. No-op on game over.
  let startNextWave(world: World) : unit =
    if not(AVal.getValue world.Economy.GameOver) then
      let events = Waves.Waves.update Waves.WaveMsg.StartNextWave world.Waves

      handleWaveEvents world events

  /// Places the selected tower at a cell. Validates buildable tile,
  /// occupancy and gold. Returns true when placed.
  let placeTower (world: World) (cell: struct (int * int)) : bool =
    let def = AVal.getValue world.SelectedTower
    let struct (cx, cy) = cell

    let tileOk = MapModel.isBuildable cx cy world.Map

    let occupied =
      ValueOption.isSome(world.Towers.CellIndex |> CMap.tryGetValue cell)

    let affordable = AVal.getValue world.Economy.Gold >= def.Cost

    if tileOk && not occupied && affordable then
      Towers.Towers.update (Towers.Place(cell, def)) world.Towers
      Economy.Economy.update (Economy.SpendGold def.Cost) world.Economy

      Vfx.Vfx.update
        (Vfx.Burst(
          Vfx.VfxKind.Placement,
          Cells.center cell (World.cellSize world)
        ))
        world.Vfx

      true
    else
      false

  /// Upgrades the tower under a cell. Validates gold and the level
  /// cap. Returns true when upgraded.
  let upgradeTower (world: World) (cell: struct (int * int)) : bool =
    match world.Towers.CellIndex |> CMap.tryGetValue cell with
    | ValueNone -> false
    | ValueSome tid ->
      let level =
        world.Towers.Levels
        |> CMap.tryGetValue tid
        |> ValueOption.defaultValue 1

      let def =
        world.Towers.Statics
        |> CMap.tryGetValue tid
        |> ValueOption.map(fun s -> s.Def)
        |> ValueOption.defaultValue TowerDefs.arrow

      let capped = level >= def.MaxLevel
      let affordable = AVal.getValue world.Economy.Gold >= def.UpgradeCost

      if capped || not affordable then
        false
      else
        Towers.Towers.update (Towers.Upgrade tid) world.Towers
        Economy.Economy.update (Economy.SpendGold def.UpgradeCost) world.Economy
        true

  /// Player switched the tower kind to place (cold path).
  let inline selectTower (world: World) (def: TowerDef) : unit =
    world.SelectedTower |> CVal.set def

  /// Host-facing system messages (tests and debug hosts): applies the
  /// message and handles the events it emits, exactly as the router
  /// would when the same message arrives from a tick.
  let applyEnemyMsg (world: World) (msg: Enemies.EnemyMsg) : unit =
    let events = Enemies.Enemies.update msg world.Enemies world.Map.Path

    handleEnemyEvents world events

  /// Host-facing economy messages (tests and debug hosts).
  let inline applyEconomyMsg (world: World) (msg: Economy.EconomyMsg) : unit =
    Economy.Economy.update msg world.Economy
