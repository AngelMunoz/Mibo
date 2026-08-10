namespace Defli.World

open System.Numerics
open AdaptiveSlop.Core
open Defli
open Defli.World.Systems

// ─────────────────────────────────────────────────────────────
// World — the composition root (State). The World record retains
// every feature's roots and projections, and init wires them.
//
// The port replaced Defli's MVU shell (WorldMsg + Cmd + the
// Immediate pump) with the State · Projection · Update · Force
// shape, split across three files:
//   World.fs    — the record + init (this file)
//   Router.fs   — the per-frame router, event translation and the
//                 host-facing cold paths (Update)
//   Frame.fs    — RenderFrame + the frame builder (Force)
// There is no 'Msg, no Cmd, no Sub — handlers write roots and run
// effects directly.
// ─────────────────────────────────────────────────────────────

/// The composition root: every feature's roots and projections are
/// retained here, as fields of this record.
type World = {
  Config: WorldConfig
  Map: MapModel
  Enemies: Enemies.EnemiesModel
  Spawning: Spawning.SpawningModel
  Waves: Waves.WavesModel
  Towers: Towers.TowersModel
  Projectiles: Projectiles.ProjectilesModel
  Vfx: Vfx.VfxModel
  Economy: Economy.EconomyModel
  Camera: Camera.CameraModel

  /// Tower kind the next placement uses — a CVal because the
  /// PlacementPreview projection joins on it (cold path writes).
  SelectedTower: cval<TowerDef>

  /// Hover cell CVal — UI state written by the host; the world
  /// projections (PlacementPreview/RangeRing) join on it.
  HoverCell: cval<struct (int * int) voption>

  /// Frozen by the host; the router writes nothing while paused.
  Paused: cval<bool>

  Projections: Projections

  /// Live enemy count — one count node, created at init and forced by
  /// Waves.tick (Defli created a fresh node per frame; one node is
  /// enough and allocates nothing per frame).
  AliveCount: aval<int>

  /// World-sim diagnostics (sampled inside the router — Kimo WorldDiag).
  Diag: WorldDiag
}

module World =

  let init(cfg: WorldConfig) : World =
    let map = MapModel.create cfg
    let enemies = Enemies.Enemies.init()
    let spawning = Spawning.Spawning.init cfg.Seed
    let waves = Waves.Waves.init()
    let towers = Towers.Towers.init()
    let projectiles = Projectiles.Projectiles.init()
    let vfx = Vfx.Vfx.init()
    let economy = Economy.Economy.init cfg

    let camera =
      Camera.Camera.init(
        Vector2(
          float32(cfg.GridCols * Tiles.TileSize),
          float32(cfg.GridRows * Tiles.TileSize)
        )
      )

    let selectedTower = CVal.create TowerDefs.arrow
    let hoverCell = CVal.create ValueNone
    let paused = CVal.create false

    let projections =
      Projections(
        enemies,
        towers,
        projectiles,
        economy,
        MapModel.buildableGrid map,
        hoverCell,
        selectedTower
      )

    {
      Config = cfg
      Map = map
      Enemies = enemies
      Spawning = spawning
      Waves = waves
      Towers = towers
      Projectiles = projectiles
      Vfx = vfx
      Economy = economy
      Camera = camera
      SelectedTower = selectedTower
      HoverCell = hoverCell
      Paused = paused
      Projections = projections
      AliveCount = enemies.Alive |> AMap.count
      Diag = WorldDiag()
    }

  /// The grid's uniform cell size (world geometry — the router and
  /// the frame builder both use it).
  let inline cellSize(world: World) =
    Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)
