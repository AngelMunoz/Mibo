namespace Mibo.Elmish.Graphics2D.Lighting

open System
open System.Numerics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics2D

/// <summary>Factory functions for particle render commands.</summary>
module ParticleCommands =

  /// <summary>
  /// Creates a batched particle render command. All particles are drawn as textured quads
  /// in a single draw call.
  /// </summary>
  /// <param name="texture">The texture applied to every particle.</param>
  /// <param name="particles">Array of particle render snapshots. Managed by the user.</param>
  /// <param name="count">Number of active particles in the array.</param>
  /// <param name="layer">Render layer for ordering.</param>
  let inline particles
    (texture: Texture2D)
    (particles: Particle2D[])
    (count: int)
    (layer: int<RenderLayer>)
    =
    Command2D.Particle(texture, particles, count, layer)

/// <summary>Pipe-friendly wrappers for particle drawing.</summary>
module ParticleDraw =

  /// <summary>Adds a batched particle render command to the buffer. Returns the buffer for chaining.</summary>
  let inline particles
    (texture: Texture2D)
    (particles: Particle2D[])
    (count: int)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(ParticleCommands.particles texture particles count layer)
    buffer

/// <summary>Helpers for particle simulation. Called in the user's update function.</summary>
/// <remarks>
/// These operate on the simulation state (velocity, lifetime, etc.), not the render snapshot.
/// After simulation, map your sim state to <see cref="T:Mibo.Elmish.Graphics2D.Lighting.Particle2D"/>
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
      let newAlpha = MathF.Max(0.0f, float32 p.Color.A - fadeAmount)

      if newAlpha > 0.0f then
        let newColor = Color(p.Color.R, p.Color.G, p.Color.B, byte newAlpha)

        particles[writeIdx] <- { p with Color = newColor }
        writeIdx <- writeIdx + 1

    count <- writeIdx
