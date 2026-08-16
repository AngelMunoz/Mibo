---
title: 3D Lighting
index: 18
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# 3D Lighting

The `ForwardPbrPipeline` supports four light types with Cook-Torrance PBR shading. Lights are added per-frame via `Draw3D.*` commands inside your view function.

## What and Why

- **Ambient light** — Base illumination for the entire scene. One per frame.
- **Directional light** — Parallel rays (sun, moon). Supports shadow casting.
- **Point light** — Radial light with position, radius, and falloff. Supports shadow casting.
- **Spot light** — Cone-shaped light with inner/outer cutoff angles.

All lights are struct types with builder functions. The pipeline uploads them as shader uniforms each frame.

## Quick start

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
    buffer
    |> Draw3D.beginCamera camera
    // Ambient
    |> Draw3D.setAmbientLight (AmbientLight3D.create (Color(30, 30, 30, 255)))
    // Directional (sun)
    |> Draw3D.addDirectionalLight (
        DirectionalLight3D.create (Vector3(0.3f, -0.7f, -0.5f))
        |> DirectionalLight3D.withIntensity 0.8f
    )
    // Point light (torch)
    |> Draw3D.addPointLight (
        PointLight3D.create (torchPos, 10f)
        |> PointLight3D.withColor Color.Orange
        |> PointLight3D.withIntensity 1.5f
        |> PointLight3D.withCastsShadows true
    )
    // Spot light (flashlight)
    |> Draw3D.addSpotLight (
        SpotLight3D.create (camPos, camDir, 20f)
        |> SpotLight3D.withIntensity 2.0f
    )
    |> Draw3D.drawModel model.PlayerModel model.PlayerTransform
    |> Draw3D.endCamera
    |> Draw3D.drop
```

## Light types

### AmbientLight3D

Uniform base illumination applied to all surfaces.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Color` | `Color` | — | Base color |
| `Intensity` | `float32` | `1.0` | Brightness multiplier |

```fsharp
AmbientLight3D.create (Color(30, 30, 30, 255))
|> AmbientLight3D.withIntensity 0.5f
```

### DirectionalLight3D

Parallel light rays. Use for sun or moon.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Direction` | `Vector3` | — | Direction rays travel (should be normalized) |
| `Color` | `Color` | `White` | Light color |
| `Intensity` | `float32` | `1.0` | Brightness multiplier |
| `CastsShadows` | `bool` | `true` | Whether to cast shadows |

```fsharp
DirectionalLight3D.create (Vector3(0.3f, -0.7f, -0.5f))
|> DirectionalLight3D.withColor Color.White
|> DirectionalLight3D.withIntensity 0.8f
|> DirectionalLight3D.withCastsShadows true
```

### PointLight3D

Radial light that emits in all directions from a position.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Position` | `Vector3` | — | World-space position |
| `Color` | `Color` | `White` | Light color |
| `Intensity` | `float32` | `1.0` | Brightness multiplier |
| `Radius` | `float32` | — | Maximum distance of influence |
| `Falloff` | `float32` | `2.0` | Decay exponent (1 = linear, 2 = quadratic) |
| `CastsShadows` | `bool` | `false` | Whether to cast shadows |
| `ShadowBias` | `float32 voption` | `ValueNone` | Per-light bias override (uses pipeline default when `ValueNone`) |

```fsharp
PointLight3D.create (Vector3(10f, 5f, 0f), 15f)
|> PointLight3D.withColor Color.Orange
|> PointLight3D.withIntensity 1.5f
|> PointLight3D.withFalloff 2.0f
|> PointLight3D.withCastsShadows true
|> PointLight3D.withShadowBias 0.005f
```

> _**TIP**_: Set `CastsShadows = true` sparingly. Each shadow-casting point light renders a cubemap shadow pass. Two or three is a good target for performance.

### SpotLight3D

Cone-shaped light with inner and outer cutoff angles.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Position` | `Vector3` | — | World-space position |
| `Direction` | `Vector3` | — | Direction the cone points (should be normalized) |
| `Color` | `Color` | `White` | Light color |
| `Intensity` | `float32` | `1.0` | Brightness multiplier |
| `Radius` | `float32` | — | Maximum distance of influence |
| `InnerCutoff` | `float32` | `0.5` | Cosine of inner cone half-angle (full brightness) |
| `OuterCutoff` | `float32` | `0.7` | Cosine of outer cone half-angle (fade to zero) |
| `CastsShadows` | `bool` | `false` | Whether to cast shadows |
| `ShadowBias` | `float32 voption` | `ValueNone` | Per-light bias override |

```fsharp
SpotLight3D.create (camPos, camDir, 25f)
|> SpotLight3D.withIntensity 2.0f
|> SpotLight3D.withCutoff 0.9f 0.95f   // tight beam
|> SpotLight3D.withCastsShadows true
```

## Light limits

The `ForwardPbrPipeline` defaults to:

| Type | Default max |
|------|-------------|
| Point lights | 8 |
| Spot lights | 4 |

Override at pipeline creation:

```fsharp
let pipeline = ForwardPbrPipeline(
    maxPointLights = 16,
    maxSpotLights = 8
)
```

Exceeding the limit silently drops extra lights.

## Shadow configuration

### Global bias

Shadow bias values control the tradeoff between shadow acne (too low) and peter-panning (too high). Set via `ShadowBiasConfig`:

```fsharp
let pipeline = ForwardPbrPipeline(
    shadowBiasConfig = {
        DirectionalBias = 0.002f
        PointBias = 0.01f
        SpotBias = 0.001f
        SlopeScaleBias = 0.001f
    }
)
```

### Per-light bias

Point and spot lights can override the global bias:

```fsharp
PointLight3D.create (pos, radius)
|> PointLight3D.withCastsShadows true
|> PointLight3D.withShadowBias 0.005f   // per-light override
```

### Atlas configuration

The shadow atlas controls resolution and caster capacity:

```fsharp
let pipeline = ForwardPbrPipeline(
    shadowAtlasConfig = {
        ShadowAtlasConfig.defaults with
            Resolution = 4096           // higher = sharper shadows
            MaxCasters = 9              // must be perfect square (4, 9, 16, 25, 36)
            DirectionalLightSize = ValueSome 30.f  // ortho projection half-size
    }
)
```

| Field | Default | Description |
|-------|---------|-------------|
| `Resolution` | 2048 | Atlas texture resolution (square) |
| `MaxCasters` | 16 | Maximum shadow casters (must be perfect square) |
| `OriginStrategy` | `CameraTarget` | Where directional shadows are centered |
| `DirectionalLightSize` | auto | Ortho projection half-size for directional shadows |
| `GridSnapSize` | 2.0 | Snap shadow origin to grid to reduce flickering |

## See also

- [Overview](overview.html) — Architecture and pipeline setup
- [Buffer & Commands](buffer-and-commands.html) — All `Draw3D.*` functions
- [Materials](materials.html) — PBR material system
