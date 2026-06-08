namespace SpaceBattle

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish.Graphics2D.Lighting

type ImpactFlash() =
  member val Position: Vector2 = Unchecked.defaultof<_> with get, set
  member val Radius: float32 = 0.0f with get, set
  member val Intensity: float32 = 0.0f with get, set
  member val Timer: float32 = 0.0f with get, set
  member val Duration: float32 = 0.0f with get, set

[<RequireQualifiedAccess>]
module Effects =

  [<Literal>]
  let MaxParticles = 256

  let private laser1TrailColors = [|
    Color(255uy, 80uy, 60uy, 255uy)
    Color(255uy, 120uy, 50uy, 255uy)
    Color(255uy, 60uy, 40uy, 255uy)
    Color(255uy, 160uy, 80uy, 255uy)
  |]

  let private laser2TrailColors = [|
    Color(80uy, 255uy, 100uy, 255uy)
    Color(50uy, 200uy, 80uy, 255uy)
    Color(120uy, 255uy, 60uy, 255uy)
    Color(40uy, 180uy, 120uy, 255uy)
  |]

  let private laser1ImpactColors = [|
    Color(255uy, 200uy, 150uy, 255uy)
    Color(255uy, 140uy, 80uy, 255uy)
    Color(255uy, 100uy, 50uy, 255uy)
    Color(255uy, 60uy, 30uy, 255uy)
  |]

  let private laser2ImpactColors = [|
    Color(200uy, 255uy, 180uy, 255uy)
    Color(120uy, 255uy, 100uy, 255uy)
    Color(80uy, 200uy, 60uy, 255uy)
    Color(50uy, 160uy, 40uy, 255uy)
  |]

  type EffectState() =
    let lighting = new LightContext2D()

    member val Lighting: LightContext2D = lighting with get

    member val Particles: Particle2D[] =
      Array.zeroCreate MaxParticles with get, set

    member val ParticleVelocities: Vector2[] =
      Array.zeroCreate MaxParticles with get, set

    member val ParticleCount = 0 with get, set
    member val ParticleTexture: Texture2D = Unchecked.defaultof<_> with get, set
    member val ImpactFlashes: ResizeArray<ImpactFlash> = ResizeArray() with get

    interface IDisposable with
      member this.Dispose() =
        (lighting :> IDisposable).Dispose()
        Raylib.UnloadTexture(this.ParticleTexture)

  let private generateParticleTexture() =
    let size = 32

    let mutable img =
      Raylib.GenImageColor(size, size, Color(0uy, 0uy, 0uy, 0uy))

    let center = size / 2
    let radius = center - 1

    for y in 0 .. size - 1 do
      for x in 0 .. size - 1 do
        let dx = float32(x - center)
        let dy = float32(y - center)
        let dist = sqrt(dx * dx + dy * dy) / float32 radius

        if dist <= 1.0f then
          let alpha =
            255.0f * (1.0f - dist * dist) |> max 0.0f |> min 255.0f |> byte

          Raylib.ImageDrawPixel(&img, x, y, Color(255uy, 255uy, 255uy, alpha))

    let tex = Raylib.LoadTextureFromImage(img)
    Raylib.UnloadImage(img)
    tex

  let init() =
    let state = new EffectState()
    state.ParticleTexture <- generateParticleTexture()
    state

  let hasActiveEffects(state: EffectState) =
    state.ParticleCount > 0 || state.ImpactFlashes.Count > 0

  let spawnTrail
    (state: EffectState)
    (laserPos: Vector2)
    (laserDir: Vector2)
    (isLaser1: bool)
    =
    let rng = Random.Shared
    let mutable pc = state.ParticleCount
    let particles = state.Particles
    let velocities = state.ParticleVelocities

    let colors = if isLaser1 then laser1TrailColors else laser2TrailColors

    let perp = Vector2(-laserDir.Y, laserDir.X)

    for _ in 0..3 do
      if pc < particles.Length then
        let spread = float32(rng.NextDouble() * 16.0 - 8.0)
        let backDrift = float32(rng.NextDouble() * 40.0 + 20.0)

        let spawnPos = laserPos + perp * spread - laserDir * backDrift

        let vel =
          -laserDir * float32(rng.NextDouble() * 60.0 + 30.0)
          + perp * float32(rng.NextDouble() * 40.0 - 20.0)

        let size = float32(rng.NextDouble() * 4.0 + 3.0)

        particles[pc] <- {
          Particle2D.create(spawnPos, Vector2(size, size)) with
              Rotation = float32(rng.NextDouble() * 360.0)
              Color = colors[rng.Next(colors.Length)]
        }

        velocities[pc] <- vel
        pc <- pc + 1

    state.ParticleCount <- pc

  let spawnImpact (state: EffectState) (pos: Vector2) (isLaser1: bool) =
    let rng = Random.Shared
    let mutable pc = state.ParticleCount
    let particles = state.Particles
    let velocities = state.ParticleVelocities

    let colors = if isLaser1 then laser1ImpactColors else laser2ImpactColors

    for _ in 0..24 do
      if pc < particles.Length then
        let angle = float32(rng.NextDouble() * Math.PI * 2.0)
        let speed = float32(rng.NextDouble() * 250.0 + 100.0)
        let vel = Vector2(cos angle * speed, sin angle * speed)

        let size = float32(rng.NextDouble() * 6.0 + 4.0)

        particles[pc] <- {
          Particle2D.create(pos, Vector2(size, size)) with
              Rotation = float32(rng.NextDouble() * 360.0)
              Color = colors[rng.Next(colors.Length)]
        }

        velocities[pc] <- vel
        pc <- pc + 1

    state.ParticleCount <- pc

    let flash = ImpactFlash()
    flash.Position <- pos
    flash.Radius <- 180.0f
    flash.Intensity <- 3.5f
    flash.Timer <- 0.35f
    flash.Duration <- 0.35f
    state.ImpactFlashes.Add flash

  let update (state: EffectState) (dt: float32) =
    let particles = state.Particles
    let velocities = state.ParticleVelocities

    for i = 0 to state.ParticleCount - 1 do
      let vel = velocities[i]
      let p = particles[i]

      particles[i] <- {
        p with
            Position = p.Position + vel * dt
      }

    let mutable count = state.ParticleCount
    ParticleSimulation.fadeAndCompact particles &count 300.0f dt
    state.ParticleCount <- count

    let flashes = state.ImpactFlashes
    let mutable i = flashes.Count - 1

    while i >= 0 do
      let flash = flashes[i]
      flash.Timer <- flash.Timer - dt

      if flash.Timer <= 0.0f then
        flashes.RemoveAt(i)
      else
        let t = flash.Timer / flash.Duration
        flash.Intensity <- 3.5f * t
        flash.Radius <- 180.0f * t

      i <- i - 1

  let ambientColor = Color(180uy, 180uy, 200uy)
