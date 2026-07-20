---
title: 2D Particles
category: 2D Rendering
categoryindex: 4
index: 19
---

# 2D Particles

The particle system renders textured quads in bulk. It is independent of lighting — particles can be lit (`.litSprite(...)`) or unlit (`.particles(...)`).

## Separation of concerns

Particle rendering is split into two layers:

1. **Simulation** — your update function handles velocities, lifetimes, physics
2. **Render** — writes `Particle2D` snapshots, `.particles(...)` adds them to the buffer

## Particle2D

```fsharp
[<Struct>]
type Particle2D = {
    Position: Vector2      // center position (world or screen)
    Size: Vector2           // width and height
    Rotation: float32       // degrees
    SourceRect: Rectangle   // source rect on the texture
    Color: Color            // tint + alpha
}
```

## Rendering

```fsharp
// Pre-allocate a pool in your model
let particles = Array.zeroCreate<Particle2D> 1024
let mutable particleCount = 0

// Each frame, write active particles into the array, then draw:
buffer
  .particles(particleTexture, particles, particleCount, layer = 10<RenderLayer>)
  .drop()
```

`.particles(...)` adds a single command to the buffer that draws all particles in a loop. All particles share one texture.

## Simulation helpers

`ParticleSimulation.fadeAndCompact` fades particles by reducing alpha and compacts dead ones out of the array:

```fsharp
open Mibo.Elmish.Graphics2D.Lighting

// In your update (e.g., Tick handler):
ParticleSimulation.fadeAndCompact
    particles            // Particle2D array
    &particleCount       // byref — updated after compaction
    60f                  // fade speed (alpha/sec — 60 = fully faded in 1s)
    dt                   // delta time
```

Particles with alpha ≤ 0 are removed in a single pass. The array is compacted in-place (no allocation).

## Performance

- All particles share one texture and one draw call — efficient for hundreds of particles.
- Pre-allocate your array to max expected count. `fadeAndCompact` reuses slots.
- Particles don't interact with the lighting system by default. For lit particles, draw them individually via `.litSprite(...)` (see [Lighting](lighting.html)).

## See Also

- [Lighting & Shadows](lighting.html) — Lit particles via `.litSprite(...)`
- [Buffer & Commands](buffer-and-commands.html) — General drawing reference
