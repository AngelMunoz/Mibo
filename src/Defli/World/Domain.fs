namespace Defli.World

open Raylib_cs
open System.Numerics

// ─────────────────────────────────────────────────────────────
// Typed IDs (units of measure — zero-cost, struct-friendly)
// ─────────────────────────────────────────────────────────────

[<Measure>]
type EnemyId

[<Measure>]
type TowerId

[<Measure>]
type ProjectileId

// ─────────────────────────────────────────────────────────────
// Render layers (deferred RenderBuffer2D sorts by layer)
// ─────────────────────────────────────────────────────────────

module Layers =

  [<Literal>]
  let Ground = 0<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Path = 1<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Entities = 2<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Projectiles = 3<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Effects = 4<Mibo.Elmish.Graphics2D.RenderLayer>

  [<Literal>]
  let Hud = 10<Mibo.Elmish.Graphics2D.RenderLayer>

// ─────────────────────────────────────────────────────────────
// Map
// ─────────────────────────────────────────────────────────────

[<Struct>]
type TerrainKind =
  | Grass
  | Dirt
  | Stone
  | Sand

/// One baked atlas tile (position + size), see Tiles.fs.
/// GENERATED data — the dataset is compile-time, no XML at runtime.
[<Struct>]
type TileInfo = {
  Name: string
  X: int
  Y: int
  Width: int
  Height: int
} with

  member this.Rect =
    Rectangle(
      float32 this.X,
      float32 this.Y,
      float32 this.Width,
      float32 this.Height
    )

[<Struct>]
type MapTile = {
  Terrain: TerrainKind
  IsPath: bool
  Buildable: bool
  /// True on the path's waypoint cells (spawn/base — Waypoints layer).
  IsWaypoint: bool
  /// Decorations-layer frame to draw over the terrain (ValueNone =
  /// no decoration on this cell).
  Decoration: TileInfo voption
}

// ─────────────────────────────────────────────────────────────
// World config (assembled outside the world — Kimo Phase 6 seam)
// ─────────────────────────────────────────────────────────────

/// Level-1 hand-authored road (fixed waypoints) vs Level-2 procedural
/// (props scattered, road carved by findPath, floodFill validated).
[<Struct>]
type MapVariant =
  | HandAuthored
  | Procedural

type WorldConfig = {
  Seed: int
  StartingGold: int
  StartingLives: int
  WaveClearBonus: int
  GridCols: int
  GridRows: int
  MapVariant: MapVariant
}

module WorldConfig =

  let defaults = {
    Seed = 42
    StartingGold = 60
    StartingLives = 20
    WaveClearBonus = 25
    GridCols = 20
    GridRows = 12
    MapVariant = MapVariant.Procedural
  }

// ─────────────────────────────────────────────────────────────
// Enemy definitions & components (code-authored def store)
// ─────────────────────────────────────────────────────────────

[<Struct>]
type EnemyArchetype =
  | Grunt
  | Runner
  | Tank
  | Flier
  /// Slow, huge HP, debuffs nearby towers (BossAura), splits on death.
  /// Walks the road — no custom locomotion.
  | Boss

[<Struct>]
type EnemyDef = {
  Key: string
  Archetype: EnemyArchetype
  Hp: int
  Speed: float32
  GoldReward: int
  /// Baked sprite name in the Tiles sheet (tower-defense pack).
  Sprite: string
  /// Baked turret name in the Tiles sheet, drawn centered over the
  /// body and aimed at the heading (ValueNone = no turret).
  Turret: string voption
  /// Built-in orientation correction of the turret sprite, degrees
  /// clockwise (0 = the sprite's barrel points up, like the bodies).
  TurretAngle: float32
}

module EnemyDefs =

  let grunt = {
    Key = "grunt"
    Archetype = EnemyArchetype.Grunt
    Hp = 40
    Speed = 60f
    GoldReward = 3
    Sprite = "tank_hull_green"
    Turret = ValueSome "tank_turret_green"
    TurretAngle = 0f
  }

  let runner = {
    Key = "runner"
    Archetype = EnemyArchetype.Runner
    Hp = 20
    Speed = 110f
    GoldReward = 5
    Sprite = "tank_hull_green"
    Turret = ValueNone
    TurretAngle = 0f
  }

  let tank = {
    Key = "tank"
    Archetype = EnemyArchetype.Tank
    Hp = 120
    Speed = 35f
    GoldReward = 7
    Sprite = "tank_hull_beige"
    Turret = ValueSome "tank_turret_beige"
    TurretAngle = 0f
  }

  /// Flies the straight line spawn → base (ignores the road).
  /// A plane — no turret.
  let flier = {
    Key = "flier"
    Archetype = EnemyArchetype.Flier
    Hp = 30
    Speed = 130f
    GoldReward = 10
    Sprite = "plane_gray"
    Turret = ValueNone
    TurretAngle = 0f
  }

  /// Boss — every 5th wave's leader (Phase 6). Slow, solid HP pool,
  /// suppresses nearby towers (BossAura), splits into grunts on death.
  /// Inverse-of-tank palette, rendered 1.6× (Enemies.view).
  /// 300 base: ~480 on wave 5 (tier 1) — a wall for an early defense,
  /// not a brick; the ×1.6/tier scaling does the late-game lifting.
  let boss = {
    Key = "boss"
    Archetype = EnemyArchetype.Boss
    Hp = 300
    Speed = 25f
    GoldReward = 50
    Sprite = "tank_hull_beige"
    Turret = ValueSome "tank_turret_green"
    TurretAngle = 0f
  }

  let all = [| grunt; runner; tank; flier; boss |]

/// Boss aura + split parameters (Phase 6) — constants keyed off the
/// Boss ARCHETYPE, not EnemyDef fields: a per-def field would ripple
/// through every def literal and fixture for one archetype's sake.
module BossAura =

  /// Towers within this distance (world px — 2 tiles) of a live boss
  /// have their fire rate multiplied by Factor.
  let Radius = 128f

  /// Fire-rate multiplier for suppressed towers (0.5 = halved).
  let Factor = 0.5f

  /// Grunts spawned at the corpse when a boss dies.
  let SplitCount = 3

  /// The child def (the wave's tier scale is applied by the router).
  let SplitInto = EnemyDefs.grunt

/// Per-enemy components (rows in the Enemies sub-system's CMaps).
[<Struct>]
type Health = { Hp: int; MaxHp: int }

[<Struct>]
type Motion = {
  Speed: float32
  Slow: float32
  Progress: float32
  PathIndex: int
}

/// One wave's executable content — composed by Waves (director),
/// executed by Spawning (queue + weighted picks).
[<Struct>]
type WaveDef = {
  Table: struct (EnemyDef * int)[]
  Count: int
  Interval: float32
  InitialDelay: float32
  /// Explicit spawns at fixed delays, queued AHEAD of the weighted
  /// picks (Phase 6: the boss leads its wave deterministically — a
  /// table entry would make it a dice roll). Empty for regular waves.
  ExtraSpawns: struct (EnemyDef * float32)[]
}

/// Join row of the EnemyViews projection (Positions × Healths × Motions).
[<Struct>]
type EnemyView = {
  Pos: Vector2
  Hp: int
  MaxHp: int
  Progress: float32
  Slow: float32
  PathIndex: int
}

// ─────────────────────────────────────────────────────────────
// Tower definitions & components
// ─────────────────────────────────────────────────────────────

/// Which enemy a tower picks first among its in-range candidates.
[<Struct>]
type TargetPolicy =
  /// Closest to the base (highest progress).
  | First
  /// Furthest from the base (lowest progress).
  | Last
  /// Highest max HP.
  | Strongest
  /// Lowest current HP.
  | Weakest
  /// Nearest to the tower.
  | Closest

[<Struct>]
type TowerDef = {
  Key: string
  Name: string
  Cost: int
  /// Range in grid cells (Chebyshev ring narrowed by exact distance).
  Range: int
  Damage: int
  /// Shots per second.
  FireRate: float32
  ProjectileSpeed: float32
  /// Head sprite name in the Tiles sheet (drawn over turretBaseA).
  Sprite: string
  /// Projectile sprite name in the Tiles sheet (the shell/rocket).
  ProjectileSprite: string
  TargetPolicy: TargetPolicy
  /// Slow applied by this tower's projectiles: 1 = no slow, < 1 = the
  /// enemy's speed factor for SlowSeconds (frost tower).
  SlowFactor: float32
  SlowSeconds: float32
  /// Blast radius in world pixels (cannon): every enemy within this
  /// distance of the impact point takes full Damage. 0 = single-target.
  SplashRadius: float32
  /// Gold cost per upgrade level (flat) and the level cap.
  UpgradeCost: int
  MaxLevel: int
}

module TowerDefs =

  let arrow = {
    Key = "arrow"
    Name = "Arrow"
    Cost = 50
    Range = 3
    Damage = 10
    FireRate = 2.25f
    ProjectileSpeed = 240f
    Sprite = "rocket_pod_single"
    ProjectileSprite = "rocket_small"
    TargetPolicy = TargetPolicy.First
    SlowFactor = 1f
    SlowSeconds = 0f
    SplashRadius = 0f
    UpgradeCost = 40
    MaxLevel = 5
  }

  /// Frost — low damage, slows the target's movement (Motions.Slow
  /// factor + expiry timer, consumed by the Enemies movement tick).
  let frost = {
    Key = "frost"
    Name = "Frost"
    Cost = 80
    Range = 2
    Damage = 4
    FireRate = 1.5f
    ProjectileSpeed = 200f
    Sprite = "rocket_pod_dual"
    ProjectileSprite = "rocket_small"
    TargetPolicy = TargetPolicy.Weakest
    SlowFactor = 0.5f
    SlowSeconds = 2f
    SplashRadius = 0f
    UpgradeCost = 60
    MaxLevel = 5
  }

  /// Cannon — slow, expensive, area damage: the shell detonates at the
  /// impact point and every enemy within SplashRadius (1.5 tiles) takes
  /// full damage. The pack counter (and the boss answer, later).
  let cannon = {
    Key = "cannon"
    Name = "Cannon"
    Cost = 120
    Range = 3
    Damage = 25
    FireRate = 0.6f
    ProjectileSpeed = 160f
    Sprite = "turret_red_dual"
    ProjectileSprite = "rocket_large"
    TargetPolicy = TargetPolicy.Strongest
    SlowFactor = 1f
    SlowSeconds = 0f
    SplashRadius = 96f
    UpgradeCost = 100
    MaxLevel = 5
  }

  let all = [| arrow; frost; cannon |]

  /// The upgrade formula (pure): +25 % damage, +10 % fire rate, +0.5
  /// range per level over the base def. Level 1 = the base def.
  let effectiveDef (def: TowerDef) (level: int) : TowerDef =
    if level <= 1 then
      def
    else
      let l = float32(level - 1)

      {
        def with
            Damage = int(float def.Damage * (1.0 + 0.25 * float l))
            FireRate = def.FireRate * (1f + 0.1f * l)
            Range = def.Range + int(l * 0.5f)
      }

/// Per-tower components (rows in the Towers sub-system's CMaps).
/// Static vs runtime is the write-frequency grouping: Statics is written
/// once (placement), Runtimes every tick (cooldown/target).
[<Struct>]
type TowerStatic = {
  Def: TowerDef
  Cell: struct (int * int)
}

[<Struct>]
type TowerRuntime = {
  Cooldown: float32
  Target: int<EnemyId> voption
}

// ─────────────────────────────────────────────────────────────
// Projectiles
// ─────────────────────────────────────────────────────────────

/// One in-flight shot (a row in Projectiles.Rows).
[<Struct>]
type ProjectileRow = {
  Pos: Vector2
  TargetEnemy: int<EnemyId>
  /// The target's last recorded position. Live-tracked while the
  /// target is alive; when the target despawns mid-flight the shot
  /// flies on to THIS point and detonates there (no mid-air pop —
  /// splash shells still blast the pack around the corpse).
  LastTargetPos: Vector2
  Damage: int
  Speed: float32
  Lifetime: float32
  /// Slow applied on impact (1 = none; copied from the TowerDef).
  SlowFactor: float32
  SlowSeconds: float32
  /// Blast radius in world pixels (0 = single-target; from the def).
  SplashRadius: float32
  /// Shell sprite name in the Tiles sheet (from the def).
  ProjectileSprite: string
}

/// Spawn intent for a projectile — the shot's definitional content
/// (the row minus the id and the lifetime, both system-owned: the id
/// is assigned at spawn, the lifetime is a fixed constant).
[<Struct>]
type ProjectileSpawn = {
  Pos: Vector2
  TargetEnemy: int<EnemyId>
  /// Seeded by the router from the target's live position at fire time.
  LastTargetPos: Vector2
  Damage: int
  Speed: float32
  SlowFactor: float32
  SlowSeconds: float32
  SplashRadius: float32
  ProjectileSprite: string
}

/// Difficulty scaling per wave tier (every 5 waves the enemies get
/// harder — Phase 5 difficulty curve data).
[<Struct>]
type WaveScale = {
  Hp: float32
  Speed: float32
  Reward: float32
}

module WaveScale =

  /// Tier = the wave's difficulty bracket: waves 1-4 → tier 0,
  /// waves 5-9 → tier 1, etc. Each tier: +60 % HP, +7 % speed,
  /// +20 % reward.
  let ofWave(number: int) : WaveScale =
    let tier = max 0 (number / 5)
    let hp = float32(1.6 ** float tier)
    let speed = float32(1.07 ** float tier)
    let reward = float32(1.2 ** float tier)

    {
      Hp = hp
      Speed = speed
      Reward = reward
    }

  let inline apply (scale: WaveScale) (def: EnemyDef) : EnemyDef = {
    def with
        Hp = max 1 (int(float def.Hp * float scale.Hp))
        Speed = def.Speed * scale.Speed
        GoldReward = max 1 (int(float def.GoldReward * float scale.Reward))
  }

/// A tower shot (TowerEvent.Fired payload) — what left the barrel.
[<Struct>]
type TowerShot = {
  Tower: int<TowerId>
  Enemy: int<EnemyId>
  Damage: int
  SlowFactor: float32
  SlowSeconds: float32
  SplashRadius: float32
  ProjectileSprite: string
}

/// A projectile impact (ProjectileEvent.Impact payload) — what hit.
/// Pos is the detonation point; on a splash hit the router fans out
/// one ApplyDamage per enemy within SplashRadius of it.
[<Struct>]
type ProjectileImpact = {
  Projectile: int<ProjectileId>
  Enemy: int<EnemyId>
  Damage: int
  Pos: Vector2
  SlowFactor: float32
  SlowSeconds: float32
  SplashRadius: float32
}

/// A slow application (EnemyMsg.ApplySlow payload) — factor + expiry.
[<Struct>]
type SlowApply = {
  Enemy: int<EnemyId>
  Factor: float32
  Seconds: float32
}

/// Render row of the world-owned Homing projection
/// (Projectiles.Rows × Enemies.Positions). TargetPos is the target's
/// live position while it lives, the row's LastTargetPos after.
[<Struct>]
type HomingView = {
  Pos: Vector2
  TargetPos: Vector2
  Sprite: string
}

// ─────────────────────────────────────────────────────────────
// Placement preview (hover highlight state)
// ─────────────────────────────────────────────────────────────

[<Struct>]
type PlacementStatus =
  | Hidden
  | Blocked
  | Affordable
  | TooExpensive

// ─────────────────────────────────────────────────────────────
// Cell helpers
// ─────────────────────────────────────────────────────────────

module Cells =

  /// World-space center of a grid cell (grid origin is Zero,
  /// cell size is uniform — see MapModel.create).
  let center (cell: struct (int * int)) (cellSize: Vector2) =
    let struct (x, y) = cell

    Vector2(
      float32 x * cellSize.X + cellSize.X / 2f,
      float32 y * cellSize.Y + cellSize.Y / 2f
    )
