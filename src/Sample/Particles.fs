module MiboSample.Particles

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

/// Particles are ephemeral visual effects, not game entities.
/// They don't need IDs - just a flat list that gets updated each frame.
[<Struct>]
type Particle = {
  Position: Vector2
  Velocity: Vector2
  Life: float32
  MaxLife: float32
  Color: Color
}

let private rng = Random.Shared

// ─────────────────────────────────────────────────────────────
// World Module: Direct operations on particle storage
//
// Particles are stored in a ResizeArray for efficient iteration
// and removal. No entity IDs needed - they're just visual effects
// that live for a short time and disappear.
// ─────────────────────────────────────────────────────────────

module World =

  let private createParticle(pos: Vector2) =
    let angle = rng.NextDouble() * Math.PI * 2.0
    let speed = rng.NextDouble() * 100.0 + 50.0

    let velocity =
      Vector2(float32(Math.Cos angle), float32(Math.Sin angle)) * float32 speed

    {
      Position = pos
      Velocity = velocity
      Life = 1.0f
      MaxLife = 1.0f
      Color = Color.Yellow
    }

  /// Spawn particles directly into the list.
  let emit (pos: Vector2) (count: int) (particles: ResizeArray<Particle>) =
    for _ = 0 to count - 1 do
      particles.Add(createParticle pos)

  /// Update all particles: move, age, remove expired.
  /// Iterates backwards to safely remove during iteration.
  let tick (dt: float32) (particles: ResizeArray<Particle>) =
    let mutable i = particles.Count - 1

    while i >= 0 do
      let p = particles[i]
      let newLife = p.Life - dt

      if newLife <= 0.0f then
        // Remove expired: swap with last, then remove last (O(1))
        particles.RemoveAt(i)
      else
        // Update in-place
        particles[i] <- {
          p with
              Position = p.Position + p.Velocity * dt
              Life = newLife
              Color =
                Color.Lerp(Color.Transparent, Color.Yellow, newLife / p.MaxLife)
        }

      i <- i - 1

/// Render all particles.
let view
  (ctx: GameContext)
  (particles: ResizeArray<Particle>)
  (buffer: RenderBuffer<RenderCmd2D>)
  =
  let tex =
    ctx
    |> Assets.getOrCreate<Texture2D> "pixel" (fun gd ->
      let t = new Texture2D(gd, 1, 1)
      t.SetData([| Color.White |])
      t)

  for p in particles do
    let rect = Rectangle(int p.Position.X, int p.Position.Y, 2, 2)

    Draw2D.sprite tex rect
    |> Draw2D.withColor p.Color
    |> Draw2D.atLayer 5<RenderLayer>
    |> Draw2D.submit buffer
