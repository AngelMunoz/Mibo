---
title: Pre-computed Derived State
category: MVU
categoryindex: 2
index: 11
---

# Pre-computed Derived State

## What and Why

Many game values depend on other values that change every frame: sky color depends on time, visibility depends on positions, health bars depend on hit points. The naive approach computes these in the view function. This couples logic to rendering, duplicates computation across systems, and makes testing impossible.

The pattern: compute derived values once per frame in a dedicated system. Store the results in model fields. Every other system (rendering, AI, UI) reads the pre-computed values without recalculating.

## Use Cases

### Day/night cycle
Time drives sky color, light direction, ambient intensity, and shadow parameters. A lighting system computes all of these from the time-of-day. The renderer reads them directly.

### Animation state
Time drives bone matrices, sprite frames, and blend weights. An animation system computes poses from time. The renderer applies them to meshes.

### AI perception
Positions drive visibility, threat level, and awareness. A perception system computes which enemies can see the player, which are flanking, which are distracted. The <abbr title="a decision structure made of conditions and actions">behavior tree</abbr> reads these results.

### Physics queries
Positions and velocities drive nearest enemy, line of sight, and predicted intercept points. A query system computes these. The AI and combat systems read them.

### UI state
Game state drives health bar widths, cooldown timers, and resource counters. A UI state system computes display values from raw data. The HUD reads them without touching game logic.

### Weather effects
Time and position drive wind direction, precipitation intensity, and fog density. A weather system computes these from game state. The renderer and physics system read them.

## The Technique

Compute derived values in a dedicated system. The lighting model below is a class with mutable fields, so this system writes the values in place instead of rebuilding the model; that is the point of the pattern, keeping the per-frame hot path allocation-free:

```fsharp
let lightingSystem (dt: float32) (model: GameModel) : struct (GameModel * Cmd<Msg>) =
  let time = model.TimeOfDay
  model.Lighting.SkyColor <- getSkyColor time
  model.Lighting.LightDirection <- getSunDirection time
  model.Lighting.AmbientIntensity <- getAmbientIntensity time
  struct (model, Cmd.none)
```

Store results in a model with mutable fields (`member val ... with get, set` declares a read-write property; this is the F# class syntax for a mutable slot):

```fsharp
type LightingModel() =
  member val SkyColor = Color.Black with get, set
  member val LightDirection = Vector3.Zero with get, set
  member val AmbientIntensity = 0.0f with get, set
```

The view reads pre-computed values, with zero computation:

```fsharp
let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer3D) =
  let l = model.Lighting
  buffer
    .beginCameraWith(Camera3D.render camera |> Camera3D.withClear l.SkyColor)
    .setAmbientLight { Color = l.SkyColor; Intensity = l.AmbientIntensity }
    .addDirectionalLight { Direction = l.LightDirection; ... }
    .drop()
```

Systems run in order, so derived systems run after their inputs:

```fsharp
System.start model
|> System.pipeMutable (dayNightSystem dt)    // clock first
|> System.pipeMutable (lightingSystem dt)    // compute from clock
|> System.finish id
```

## Key Insight

Moving computation from the view to a system means:
- The view stays simple: it only reads state.
- Systems can be tested independently, with no renderer needed.
- Derived values are available to all systems, not just rendering.
- The render path does minimal work.

The same derived value can feed multiple consumers. Lighting affects rendering, but also AI (visibility in dark areas) and gameplay (torch necessity). Pre-computing once means every consumer reads the same consistent value.

## When to use

- Any value that depends on multiple inputs and changes every frame.
- Values needed by multiple systems: rendering, AI, UI, gameplay.
- Expensive computations that would slow down the render path.
- You want to test logic without running the renderer.

## See also

- [Platformer3D day/night cycle](https://github.com/AngelMunoz/Mibo.Samples/blob/master/Platformer3D/Shared/DayNight.fs) and [lighting state](https://github.com/AngelMunoz/Mibo.Samples/blob/master/Platformer3D/Shared/Lighting.fs): day/night cycle as pre-computed state.
- [Composable Systems](composable-systems.html): how pre-computed state fits into the system pipeline.
- [Mibo.Adaptive](../mibo-adaptive/overview.html): on the adaptive runtime this pattern is built in: derived values recompute on change instead of once per frame.
