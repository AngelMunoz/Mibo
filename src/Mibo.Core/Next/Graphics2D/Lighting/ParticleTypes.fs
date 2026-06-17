namespace Mibo.Elmish.Next.Graphics2D.Lighting

open System
open System.Numerics
open Mibo.Elmish.Next.Graphics2D
open Mibo.Elmish.Next.Graphics2D.Base

/// <summary>A single 2D particle rendered as a textured quad with optional sprite-sheet source rect.</summary>
/// <remarks>
/// This is a render snapshot. Simulation state (velocity, lifetime, spin, color rules) lives in the
/// user's model and is written into this struct at the start of the view function.
/// </remarks>
[<Struct>]
type Particle2D = {
  /// <summary>Center position in world/screen space.</summary>
  Position: Vector2

  /// <summary>Width and height of the quad.</summary>
  Size: Vector2

  /// <summary>Rotation in degrees around the center.</summary>
  Rotation: float32

  /// <summary>Source rectangle within the texture in pixels. Use (0, 0, tw, th) for the full texture.</summary>
  SourceRect: Rect

  /// <summary>Tint color. Alpha controls transparency.</summary>
  Color: Color
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Next.Graphics2D.Lighting.Particle2D"/>.</summary>
module Particle2D =

  /// <summary>Creates a particle with required fields. Defaults: Rotation=0, SourceRect=empty, Color=White.</summary>
  let create(position: Vector2, size: Vector2) : Particle2D = {
    Position = position
    Size = size
    Rotation = 0.0f
    SourceRect = Rect.Zero
    Color = {
      R = 255uy
      G = 255uy
      B = 255uy
      A = 255uy
    }
  }

  let inline withRotation (v: float32) (p: Particle2D) = { p with Rotation = v }

  let inline withSourceRect (v: Rect) (p: Particle2D) = {
    p with
        SourceRect = v
  }

  let inline withColor (v: Color) (p: Particle2D) = { p with Color = v }

/// <summary>Helpers for particle simulation. Called in the user's update function.</summary>
/// <remarks>
/// These operate on the simulation state (velocity, lifetime, etc.), not the render snapshot.
/// After simulation, map your sim state to <see cref="T:Mibo.Elmish.Next.Graphics2D.Lighting.Particle2D"/>
/// render snapshots for the view function.
/// </remarks>
module ParticleSimulation =

  /// <summary>
  /// Fades particles by reducing alpha and compacts the dead ones out of the array.
  /// Particles with alpha &lt;= 0 are removed. Returns the new count via the byref parameter.
  /// Call this in your Tick handler after updating positions/velocities.
  /// </summary>
  /// <param name="particles">The particle render snapshot array. Mutated in place.</param>
  /// <param name="count">Current active count. Updated to reflect compacted array.</param>
  /// <param name="fadeSpeed">Alpha reduction per second. 255.0f means a particle fades completely in 1 second.</param>
  /// <param name="dt">Delta time in seconds.</param>
  let inline fadeAndCompact
    (particles: Particle2D[])
    (count: int byref)
    (fadeSpeed: float32)
    (dt: float32)
    =
    let fadeAmount = fadeSpeed * dt
    let mutable writeIdx = 0

    for readIdx = 0 to count - 1 do
      let p = particles[readIdx]

      let clampedAlpha =
        Math.Clamp(float32 p.Color.A - fadeAmount, 0.0f, 255.0f)

      let newAlphaByte = byte clampedAlpha

      if newAlphaByte > 0uy then
        let newColor = { p.Color with A = newAlphaByte }

        particles[writeIdx] <- { p with Color = newColor }
        writeIdx <- writeIdx + 1

    count <- writeIdx
