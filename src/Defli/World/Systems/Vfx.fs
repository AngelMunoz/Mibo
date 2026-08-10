module Defli.World.Systems.Vfx

open System
open System.Collections.Generic
open System.Numerics
open Raylib_cs
open Defli.World

// ─────────────────────────────────────────────────────────────
// VFX sub-system — the ONE deliberately non-adaptive system in the
// world (plan §4.10): per-particle adaptive cells would be node
// churn for fire-and-forget presentation. Pooled particles pattern
// (Mibo pooled-particles), one pool per effect kind — each kind
// draws with its own texture in a single .particles call.
//
// Kinds and their look (kenney_smoke_particles / particle_pack):
//   Impact   → spark_01      — tight, fast-fading sparks at the hit
//   Explosion→ explosion03   — a clustered fireball: quick fade
//   DeathPoof→ blackSmoke05  — slow puffs that EXPAND, rise, linger
//   Muzzle   → flash00       — a single stationary flash at the barrel
//   Placement→ dirt_01       — low dust clumps that settle fast
//   BaseHit  → blackSmoke05  — a bigger smoke cloud at the base
//
// The motion model is per-kind (paramsOf): velocity DAMPING stalls
// the spray near the origin (no more fly-away balls), RISE drifts
// smoke upward, and GROWTH expands puffs as they fade — the classic
// smoke look. Deterministic bursts — no RNG stream (index-based
// angles/speed tiers; golden-angle rotation spread).
//
// Texture handles are resolved ONCE and cached on the model: the
// per-frame `assets.Texture(string)` calls were flagged by the trace
// (string allocation per call — resolvePath). The cache is
// presentation state (asset handles, not adaptive reads).
// ─────────────────────────────────────────────────────────────

[<Struct>]
type VfxKind =
  | Impact
  | Explosion
  | DeathPoof
  | Muzzle
  | Placement
  | BaseHit

/// Headless port: the original stored the renderer's Particle2D
/// (Mibo.Elmish.Graphics2D, raylib-only). The sim only integrates
/// positions, sizes and alphas — a local struct keeps the pool shape
/// identical; the raylib frontend maps it back (milestone 2).
[<Struct>]
type Particle2D = {
  Position: Vector2
  Size: Vector2
  Rotation: float32
  SourceRect: Rectangle
  Color: Color
}

/// One pooled particle store (SoA-ish: particles + parallel velocities).
[<Sealed>]
type VfxPool(capacity: int) =
  member val Particles = Array.zeroCreate<Particle2D> capacity with get, set
  member val Velocities = Array.zeroCreate<Vector2> capacity with get, set
  member val Count = 0 with get, set

type VfxModel() =
  member val Impact = VfxPool 256 with get, set
  member val Explosion = VfxPool 256 with get, set
  member val DeathPoof = VfxPool 256 with get, set
  member val Muzzle = VfxPool 128 with get, set
  member val Placement = VfxPool 128 with get, set
  member val BaseHit = VfxPool 128 with get, set
  /// Resolved texture handle per kind (view-time, cached once).
  member val Textures = Dictionary<string, Texture2D>() with get, set

[<Struct>]
type VfxMsg = Burst of kind: VfxKind * pos: Vector2

module Vfx =

  let init() = VfxModel()

  /// Per-kind parameters:
  ///   count  — particles per burst
  ///   speed  — base spray speed (px/s; tier-multiplied at spawn)
  ///   size   — initial quad size (px at zoom 1; tiles are 64)
  ///   fade   — alpha per second (255/fade = seconds of life)
  ///   growth — size delta per second (smoke expands, sparks shrink)
  ///   rise   — upward drift (px/s) added to the spray velocity
  ///   damp   — velocity decay per second (stalls the spray near the
  ///            origin — the anti "fly-away balls" term)
  let inline private paramsOf(kind: VfxKind) =
    match kind with
    | Impact -> struct (6, 90f, 9f, 700f, -6f, 0f, 6f)
    | Explosion -> struct (7, 45f, 44f, 420f, 26f, 10f, 5f)
    | DeathPoof -> struct (5, 25f, 36f, 110f, 14f, 18f, 3f)
    | Muzzle -> struct (1, 0f, 32f, 640f, 20f, 0f, 0f)
    | Placement -> struct (5, 26f, 18f, 260f, 10f, 6f, 4f)
    | BaseHit -> struct (6, 30f, 40f, 130f, 16f, 12f, 3f)

  /// Cold path: spawn a burst into the kind's pool (deterministic
  /// spread — index-based angles, three speed tiers, golden-angle
  /// rotation so overlapping puffs don't read as copies).
  let update (msg: VfxMsg) (model: VfxModel) : unit =
    match msg with
    | Burst(kind, pos) ->
      let struct (count, speed, size, _, _, _, _) = paramsOf kind

      let pool =
        match kind with
        | Impact -> model.Impact
        | Explosion -> model.Explosion
        | DeathPoof -> model.DeathPoof
        | Muzzle -> model.Muzzle
        | Placement -> model.Placement
        | BaseHit -> model.BaseHit

      let mutable i = 0

      while i < count && pool.Count < pool.Particles.Length do
        let angle = float32 i / float32 count * 2f * MathF.PI
        let tier = float32(i % 3 + 1)
        let dir = Vector2(MathF.Cos angle, MathF.Sin angle)
        let velocity = dir * (speed * tier)

        pool.Particles[pool.Count] <-
          {
            Position = pos
            Size = Vector2(size, size)
            Rotation = float32((i * 137) % 360)
            SourceRect = Rectangle(0f, 0f, 0f, 0f) // full texture — patched in the view
            Color = Color(255uy, 255uy, 255uy, 255uy)
          }

        pool.Velocities[pool.Count] <- velocity
        pool.Count <- pool.Count + 1
        i <- i + 1

  let inline private stepPool dt (kind: VfxKind) (pool: VfxPool) =
    let struct (_, _, _, fadeSpeed, growth, rise, damp) = paramsOf kind
    let riseVec = Vector2(0f, -rise)
    let dampMul = max 0f (1f - damp * dt)
    let growVec = Vector2(growth * dt, growth * dt)

    for i in 0 .. pool.Count - 1 do
      let p = pool.Particles[i]
      pool.Velocities[i] <- pool.Velocities[i] * dampMul

      pool.Particles[i] <-
        {
          p with
              Position = p.Position + (pool.Velocities[i] + riseVec) * dt
              Size = Vector2.Max(p.Size + growVec, Vector2.One)
        }

    let fadeAmount = fadeSpeed * dt
    let mutable write = 0

    for read in 0 .. pool.Count - 1 do
      let p = pool.Particles[read]
      // Clamp in FLOAT before the byte conversion: `byte` of a
      // negative float wraps (conv.u1 takes the low byte — -5.67
      // becomes 251), resurrecting nearly-dead particles at full
      // alpha — the "muzzle keeps flashing" ghost.
      let newAlpha = byte(max 0f (float32 p.Color.A - fadeAmount))

      if newAlpha > 0uy then
        let c = Color(p.Color.R, p.Color.G, p.Color.B, newAlpha)
        pool.Particles[write] <- { p with Color = c }
        pool.Velocities[write] <- pool.Velocities[read]
        write <- write + 1

    pool.Count <- write

  /// Hot path: damp/integrate velocities, drift + grow, fade, compact
  /// (in place, zero alloc). Velocities are compacted in parallel.
  let tick (dt: float32) (model: VfxModel) : unit =
    stepPool dt VfxKind.Impact model.Impact
    stepPool dt VfxKind.Explosion model.Explosion
    stepPool dt VfxKind.DeathPoof model.DeathPoof
    stepPool dt VfxKind.Muzzle model.Muzzle
    stepPool dt VfxKind.Placement model.Placement
    stepPool dt VfxKind.BaseHit model.BaseHit
