---
title: Pooled Particles
category: Patterns
categoryindex: 13
index: 4
---

# Pooled Particles

## What and Why

Particles add juice — explosions, dust, sparks, smoke, confetti. You need hundreds of them at 60+ FPS without triggering garbage collection. The naive approach (allocate a list, add particles, remove dead ones) creates GC pressure that causes frame hitches.

The pattern: pre-allocate parallel arrays for every particle attribute. Spawn by writing directly into arrays. Kill by fading alpha. Remove dead particles with an in-place compact pass. Zero allocations after init.

## Use Cases

### Combat effects
Sparks on sword hits, blood on arrows, fire on spell impacts. Hundreds of short-lived particles per fight, spawned and killed rapidly.

### Environmental effects
Dust clouds on movement, leaves in wind, rain, snow. Continuous spawning with varying density based on location or weather.

### UI and feedback
Confetti on level-up, screen shake debris, combo counter particles. Particle effects tied to game events, not world objects.

### Destruction
Debris on building collapse, fragments on enemy death, shrapnel on explosions. Particles that need per-particle color and size variation.

### Ambient effects
Torch fire, candle flicker, magical auras. Small pools of particles with long lifetimes and slow fade.

## The Technique

Pre-allocate parallel arrays — one per attribute:

```fsharp
type ParticlePool = {
  Positions: Vector3[]
  Velocities: Vector3[]
  Sizes: Vector2[]
  Colors: Color[]
  mutable Count: int
}
```

Spawn by writing directly into arrays:

```fsharp
pool.Positions[pool.Count] <- position
pool.Velocities[pool.Count] <- velocity
pool.Colors[pool.Count] <- color
pool.Sizes[pool.Count] <- size
pool.Count <- pool.Count + 1
```

Update: apply physics, reduce alpha:

```fsharp
for i = 0 to pool.Count - 1 do
  pool.Velocities[i] <- pool.Velocities[i] + gravity * dt
  pool.Positions[i] <- pool.Positions[i] + pool.Velocities[i] * dt
  let c = pool.Colors[i]
  pool.Colors[i] <- Color(c.R, c.G, c.B, byte (max 0 (float32 c.A - fadeRate * dt)))
```

Compact: in-place filter, no allocation:

```fsharp
let mutable writeIdx = 0
for readIdx = 0 to pool.Count - 1 do
  if pool.Colors[readIdx].A > 0uy then
    pool.Positions[writeIdx] <- pool.Positions[readIdx]
    pool.Velocities[writeIdx] <- pool.Velocities[readIdx]
    pool.Colors[writeIdx] <- pool.Colors[readIdx]
    pool.Sizes[writeIdx] <- pool.Sizes[readIdx]
    writeIdx <- writeIdx + 1
pool.Count <- writeIdx
```

## Key Insight

Parallel arrays (not structs of arrays) give better cache locality for the physics pass — you iterate positions and velocities without touching colors or sizes. The compact pass is O(n) with zero allocation: dead particles vanish, live ones shift to the front. No lists, no `ResizeArray`, no GC pressure.

For thousands of particles, switch from per-particle `drawBillboard` to `drawBillboardBatch` which sends all particles in a single draw call.

## When to use

- Hundreds of short-lived visual effects.
- GC-sensitive contexts — VR, console, competitive games.
- Effects that need per-particle color, size, or alpha variation.
- You want the particle system to be callable from any other system (input, physics, combat, AI).

## See also

- [Platformer3D particles](https://github.com/AngelMunoz/Mibo.Samples/blob/master/Platformer3D/Shared/Particles.fs) — full implementation with burst spawning and billboard rendering.
- [Composable Systems](../mvu/composable-systems.html) — integrating particles into the system pipeline.
