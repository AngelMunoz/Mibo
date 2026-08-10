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

  /// Conversion buffer — grown as needed, reused every frame.
  let mutable scratch = Array.empty<Lighting.Particle2D>

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

  /// Cached handle per kind: resolves through IAssets once, then
  /// reuses the stored Texture2D (the cache lives on the sim model —
  /// no per-frame string work).
  let textureOfCached (kind: VfxKind) (model: VfxModel) (assets: IAssets) =
    let key = textureOf kind

    match model.Textures |> Dictionary.tryGetValue key with
    | ValueSome tex -> tex
    | ValueNone ->
      let tex = assets.Texture key
      model.Textures[key] <- tex
      tex

  let drawPool
    (kind: VfxKind)
    (pool: VfxPool)
    (model: VfxModel)
    (assets: IAssets)
    (buffer: RenderBuffer2D)
    =
    if pool.Count > 0 then
      let tex = textureOfCached kind model assets
      let full = Rectangle(0f, 0f, float32 tex.Width, float32 tex.Height)

      if scratch.Length < pool.Particles.Length then
        scratch <- Array.zeroCreate pool.Particles.Length

      for i in 0 .. pool.Count - 1 do
        let p = pool.Particles[i]

        scratch[i] <- {
          Position = p.Position
          Size = p.Size
          Rotation = p.Rotation
          SourceRect = full
          Color = p.Color
        }

      buffer.particles(tex, scratch, pool.Count, layer = Layers.Effects).drop()

  /// The view: one .particles draw call per kind/texture.
  member _.View (ctx: GameContext) (model: VfxModel) (buffer: RenderBuffer2D) =
    let assets = GameContext.getService<IAssets> ctx
    drawPool VfxKind.Impact model.Impact model assets buffer
    drawPool VfxKind.Explosion model.Explosion model assets buffer
    drawPool VfxKind.DeathPoof model.DeathPoof model assets buffer
    drawPool VfxKind.Muzzle model.Muzzle model assets buffer
    drawPool VfxKind.Placement model.Placement model assets buffer
    drawPool VfxKind.BaseHit model.BaseHit model assets buffer
