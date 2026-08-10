namespace Defli.World

open System.Collections.Generic
open AdaptiveSlop.Core
open Mibo.Adaptive
open Mibo.Elmish
open Raylib_cs
open Defli.World.Systems

// ─────────────────────────────────────────────────────────────
// Frame — the Force phase. Everything the renderer needs, resolved
// and packed once per Step into the RenderFrame struct; the
// renderer reads the struct — O(1), no graph access at draw time.
// ─────────────────────────────────────────────────────────────

module Frame =

  /// Everything the renderer needs, resolved and packed once per Step.
  /// The dictionaries are transient views — valid until the next
  /// Step's writes — so the renderer must read the frame immediately
  /// after Step, before the world is stepped again.
  [<Struct>]
  type RenderFrame = {
    /// Alive enemies (the Alive projection). Draw-side.
    Alive: IReadOnlyDictionary<int<EnemyId>, EnemyView>
    /// Enemy defs (names, archetypes, hull sprites). Draw-side.
    Defs: IReadOnlyDictionary<int<EnemyId>, EnemyDef>
    /// Tower statics (cells, defs) and levels. Draw-side.
    TowerStatics: IReadOnlyDictionary<int<TowerId>, TowerStatic>
    TowerLevels: IReadOnlyDictionary<int<TowerId>, int>
    /// In-flight projectiles (the Homing projection). Draw-side.
    Projectiles: IReadOnlyDictionary<int<ProjectileId>, HomingView>
    /// HUD scalars.
    Gold: int
    Lives: int
    Banner: string
    GameOver: bool
    /// Sim narrative.
    WaveNumber: int
    WaveActive: bool
    SpawnQueueLength: int
    TowerCount: int
    EnemyCount: int
    ProjectileCount: int
    /// The camera (the subsystem mutates the struct; the frame carries
    /// a copy at force time).
    Camera: Camera2D
  }

  /// Forcing the frame: resolve every output projection once, pack the
  /// struct. After this, drawing is plain struct reads — O(1), no
  /// graph access. The count nodes are created ONCE (the AliveCount
  /// precedent): `AMap.count` builds a node, so per-call creation in
  /// the frame body would allocate every Step.
  let buildFrame(world: World) : unit -> RenderFrame =
    let towerCount = world.Towers.Statics |> AMap.count
    let enemyCount = world.Enemies.Alive |> AMap.count
    let projectileCount = world.Projections.Homing |> AMap.count

    fun () -> {
      Alive = world.Enemies.Alive |> AMap.getValue
      Defs = world.Enemies.Defs |> AMap.getValue
      TowerStatics = world.Towers.Statics |> AMap.getValue
      TowerLevels = world.Towers.Levels |> AMap.getValue
      Projectiles = world.Projections.Homing |> AMap.getValue
      Gold = AVal.getValue world.Economy.Gold
      Lives = AVal.getValue world.Economy.Lives
      Banner = AVal.getValue world.Waves.Banner
      GameOver = AVal.getValue world.Economy.GameOver
      WaveNumber = AVal.getValue world.Waves.WaveNumber
      WaveActive = AVal.getValue world.Waves.WaveActive
      SpawnQueueLength = world.Spawning.Queue.Count
      TowerCount = AVal.getValue towerCount
      EnemyCount = AVal.getValue enemyCount
      ProjectileCount = AVal.getValue projectileCount
      Camera = world.Camera.Camera
    }

  /// The adaptive program: the frame builder forces the world's
  /// projections at the end of every Step; Update runs the router.
  let adaptiveWorld(world: World) : AdaptiveWorld<RenderFrame> =
    AdaptiveWorld.mk(fun _ctx -> {
      FrameBuilder = buildFrame world
      Disposables = []
    })
    |> AdaptiveWorld.withUpdate(fun _ctx gameTime -> Router.step world gameTime)
