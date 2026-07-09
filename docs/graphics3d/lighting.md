---
title: 3D Lighting
category: 3D Rendering
categoryindex: 5
index: 22
---

# 3D Lighting

The built-in forward pipelines support four light types with Cook-Torrance PBR shading. Lights are added per-frame via `Draw3D.*` commands inside your view function. (On raylib the pipeline is `ForwardPbrPipeline`; on MonoGame it is `ForwardPipeline`.)

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

Both built-in pipelines default to the same light budgets:

| Type | Default max |
|------|-------------|
| Ambient | 1 |
| Directional | 1 |
| Point lights | 8 |
| Spot lights | 4 |

How you change them differs by backend:

**raylib** — the budgets are runtime-configurable via the `ForwardPbrPipeline` constructor:

```fsharp
let pipeline = ForwardPbrPipeline(
    maxPointLights = 16,
    maxSpotLights = 8
)
```

**MonoGame** — the budgets are **baked into the compiled PBR shader** (`MAX_POINT_LIGHTS` /
`MAX_SPOT_LIGHTS` constants in `ForwardPbr.fx`). The `ForwardPipeline` constructor takes no
light-count argument; to change them you recompile the `.fx` with different `#define`s.

Exceeding the limit silently drops extra lights on both backends.

## Shadow configuration

### Global bias

Shadow bias values control the tradeoff between shadow acne (too low) and peter-panning (too high). Set via `ShadowBiasConfig`:

```fsharp
// raylib:
let pipeline = ForwardPbrPipeline(
    shadowBiasConfig = {
        DirectionalBias = 0.0005f      // raylib default
        PointBias = 0.01f
        SpotBias = 0.001f
        SlopeScaleBias = 0.0005f
    }
)

// MonoGame:
let pipeline = ForwardPipeline(
    shadowBias = {
        DirectionalBias = 0.002f       // MonoGame default (higher; native depth bias)
        PointBias = 0.01f
        SpotBias = 0.001f
        SlopeScaleBias = 0.0005f
    }
)
```

> _**NOTE**_: On MonoGame, `SlopeScaleBias` maps to the native
> `RasterizerState.SlopeScaleDepthBias` (hardware polygon offset) and the per-type biases map
> to `RasterizerState.DepthBias` — MonoGame can't use the GLSL `dFdx`/`dFdy` slope math the
> raylib shader relies on. On raylib the slope-scale bias is applied in the GLSL depth shader.
> The observable effect (tuning acne vs peter-panning) is the same.

### Per-light bias

Point and spot lights can override the global bias:

```fsharp
PointLight3D.create (pos, radius)
|> PointLight3D.withCastsShadows true
|> PointLight3D.withShadowBias 0.005f   // per-light override
```

### Atlas configuration

The shadow atlas controls resolution and caster capacity. `MaxCasters` must be a perfect square
(4, 9, 16, 25, 36) — it lays out the atlas as a √N × √N grid.

```fsharp
// raylib:
let pipeline = ForwardPbrPipeline(
    shadowAtlasConfig = {
        ShadowAtlasConfig.defaults with
            Resolution = 4096
            MaxCasters = 9
            DirectionalLightSize = ValueSome 30.f
    }
)

// MonoGame (note the extra DirectionalOriginY field):
let pipeline = ForwardPipeline(
    shadowAtlas = {
        ShadowAtlasConfig.defaults with
            Resolution = 4096
            MaxCasters = 9
            DirectionalOriginY = 0.0f       // MonoGame-only: lock shadow frustum Y
    }
)
```

| Field | Default | raylib | MonoGame | Description |
|-------|---------|--------|----------|-------------|
| `Resolution` | 2048 | ✓ | ✓ | Atlas texture resolution (square) |
| `MaxCasters` | 16 | ✓ | ✓ | Maximum shadow casters (perfect square) |
| `OriginStrategy` | `CameraTarget` | ✓ | ✓ | Where directional shadows are centered (`CameraTarget`/`SceneCenter`/`Custom`) |
| `DirectionalLightDistance` | auto | ✓ | ✓ | Distance to place the directional light camera behind the origin |
| `DirectionalLightSize` | auto | ✓ | ✓ | Ortho projection half-size for directional shadows |
| `DirectionalOriginY` | 0.0 | — | ✓ | Lock the shadow frustum's vertical origin (prevents vertical sliding) |
| `GridSnapSize` | 2.0 | ✓ | ✓ | Snap shadow origin to a grid to reduce shimmer |

> _**NOTE — shadow technique differs.**_ MonoGame cannot create a sampleable depth-only
> render target, so it writes shadow depth into an R32F color attachment (`DepthShadow.fx`)
> and samples it with a comparison sampler for hardware PCF. raylib samples depth textures
> directly via GLSL derivatives. The user-facing config and behavior (shadow quality, bias
> tuning) are equivalent; only the internal path differs.

## See also

- [Overview](overview.html) — Architecture and pipeline setup
- [Buffer & Commands](buffer-and-commands.html) — All `Draw3D.*` functions
- [Materials](materials.html) — PBR material system
