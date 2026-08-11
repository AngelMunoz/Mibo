namespace Defli.Raylib

open System.Collections.Generic
open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics2D.Lighting
open Raylib_cs
open Defli.World
open Defli.World.Systems
open Defli.World.Systems.Vfx

// ─────────────────────────────────────────────────────────────
// VfxView — the raylib EDGE of the VFX pools. The sim stores a LOCAL
// particle struct (backend-free); the view maps it into the raylib
// Particle2D once per frame through a persistent buffer OWNED BY THE
// VIEW (instance state, like Renderer2D's own buffer — no module
// mutables, no per-frame allocation). The full-texture source rect is
// patched here, not into the sim's pool (no sim mutation).
// ─────────────────────────────────────────────────────────────

[<Sealed>]
type VfxView() =

  /// Conversion buffer PER KIND. The draw commands are deferred — the
  /// renderer executes them after the view returns — and each command
  /// holds the array it was recorded with. One shared buffer would
  /// make every command read the LAST kind's particles with its own
  /// texture (the "square behind the puff" artifact). One buffer per
  /// kind keeps each command's data isolated; all six are grown once
  /// and reused, so steady state allocates nothing.
  let scratchByKind = Array.init 6 (fun _ -> Array.empty<Lighting.Particle2D>)

  /// Resolved texture handle per kind, cached on the view (presentation
  /// state — asset handles, not adaptive reads). Resolves once through
  /// IAssets, then reuses the stored Texture2D so steady state does no
  /// per-frame string work.
  let textures = Dictionary<string, Texture2D>()

  let kindIndex(kind: VfxKind) =
    match kind with
    | Impact -> 0
    | Explosion -> 1
    | DeathPoof -> 2
    | Muzzle -> 3
    | Placement -> 4
    | BaseHit -> 5

  [<Literal>]
  let ImpactPath = "kenney_particle_pack/spark_01.png"

  [<Literal>]
  let ExplosionPath = "kenney_smoke_particles/Explosion/explosion03.png"

  [<Literal>]
  let DeathPoofPath = "kenney_smoke_particles/Black smoke/blackSmoke05.png"

  [<Literal>]
  let MuzzlePath = "kenney_smoke_particles/Flash/flash00.png"

  [<Literal>]
  let PlacementPath = "kenney_particle_pack/dirt_01.png"

  [<Literal>]
  let BaseHitPath = "kenney_smoke_particles/Black smoke/blackSmoke05.png"

  /// Texture per kind (kenney_particle_pack).
  let textureOf(kind: VfxKind) =
    match kind with
    | Impact -> ImpactPath
    | Explosion -> ExplosionPath
    | DeathPoof -> DeathPoofPath
    | Muzzle -> MuzzlePath
    | Placement -> PlacementPath
    | BaseHit -> BaseHitPath

  /// Cached handle per kind: resolves through IAssets once, then reuses
  /// the stored Texture2D from the view's own cache (no per-frame string
  /// work).
  let textureOfCached (kind: VfxKind) (assets: IAssets) =
    let key = textureOf kind
    assets.Texture key

  let drawPool
    (kind: VfxKind)
    (pool: VfxPool)
    (assets: IAssets)
    (buffer: RenderBuffer2D)
    =
    if pool.Count > 0 then
      let tex = textureOfCached kind assets
      let full = Rectangle(0f, 0f, float32 tex.Width, float32 tex.Height)
      let idx = kindIndex kind

      if scratchByKind[idx].Length < pool.Particles.Length then
        scratchByKind[idx] <- Array.zeroCreate pool.Particles.Length

      let scratch = scratchByKind[idx]

      for i in 0 .. pool.Count - 1 do
        let p = pool.Particles[i]

        scratch[i] <- {
          Position = p.Position
          Size = p.Size
          Rotation = p.Rotation
          SourceRect = full
          Color = RaylibColor.toRaylibColor p.Color
        }

      buffer.particles(tex, scratch, pool.Count, layer = Layers.Effects).drop()

  /// The view: one .particles draw call per kind/texture.
  member _.View (ctx: GameContext) (model: VfxModel) (buffer: RenderBuffer2D) =
    let assets = GameContext.getService<IAssets> ctx
    drawPool VfxKind.Impact model.Impact assets buffer
    drawPool VfxKind.Explosion model.Explosion assets buffer
    drawPool VfxKind.DeathPoof model.DeathPoof assets buffer
    drawPool VfxKind.Muzzle model.Muzzle assets buffer
    drawPool VfxKind.Placement model.Placement assets buffer
    drawPool VfxKind.BaseHit model.BaseHit assets buffer
