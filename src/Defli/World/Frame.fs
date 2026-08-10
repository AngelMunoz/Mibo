namespace Defli.World

open System.Collections.Generic
open AdaptiveSlop.Core
open Mibo.Adaptive
open Mibo.Elmish
open Defli
open Defli.World.Systems
open Defli.World.Systems.Camera

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
    /// UI state written by the host (hover cell / selected tower) —
    /// the frame carries their current values so draw reads the
    /// struct.
    HoverCell: struct (int * int) voption
    SelectedTower: TowerDef
    /// Hover overlays — the projections the draw side consumes.
    PlacementPreview: PlacementStatus
    RangeRing: TowerDef voption
    /// Vfx pools + the view-memoized texture handles (non-adaptive
    /// state; draw reads pool particles).
    Vfx: Vfx.VfxModel
    /// The map — static world data (terrain/path/decorations/
    /// waypoints).
    Map: MapModel
    /// World-sim diagnostics (the F3 overlay reads the display line).
    Diag: WorldDiag
    /// The camera — a backend-neutral snapshot at force time; the
    /// frontend builds its native camera at the edge.
    Camera: CameraState
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
      HoverCell = world.HoverCell |> AVal.getValue
      SelectedTower = world.SelectedTower |> AVal.getValue
      PlacementPreview = world.Projections.PlacementPreview |> AVal.getValue
      RangeRing = world.Projections.RangeRing |> AVal.getValue
      Vfx = world.Vfx
      Map = world.Map
      Diag = world.Diag
      Camera = world.Camera.State
    }

  /// The adaptive program: the frame builder forces the world's
  /// projections at the end of every Step; Update runs the router.
  let adaptiveWorld(world: World) : AdaptiveWorld<RenderFrame> =
    AdaptiveWorld.mk(fun _ctx -> {
      FrameBuilder = buildFrame world
      Disposables = []
    })
    |> AdaptiveWorld.withUpdate(fun _ctx gameTime -> Router.step world gameTime)
