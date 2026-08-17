---
title: 3D Lighting
category: 3D Rendering
categoryindex: 11
index: 3
---


# 3D Lighting

The built-in forward pipelines support four light types with Cook-Torrance <abbr title="physically based rendering">PBR</abbr> shading (surface shading that models how light reflects off real materials). Lights are added per-frame via fluent members inside your view function. (On raylib the pipeline is `ForwardPbrPipeline`; on MonoGame it is `ForwardPipeline`.)

## What and Why

- **Ambient light**: Base illumination for the entire scene. One per frame.
- **Directional light**: Parallel rays (sun, moon). Supports shadow casting.
- **Point light**: Radial light with position, radius, and falloff. Supports shadow casting.
- **Spot light**: Cone-shaped light with inner/outer cutoff angles.

All lights are struct types with builder functions. The pipeline uploads them as shader uniforms each frame.

## Quick start

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
    buffer
      .beginCamera(camera)
      // Ambient
      .setAmbientLight(AmbientLight3D.create (Color(30, 30, 30, 255)))
      // Directional (sun)
      .addDirectionalLight(
        DirectionalLight3D.create (Vector3(0.3f, -0.7f, -0.5f))
        |> DirectionalLight3D.withIntensity 0.8f
      )
      // Point light (torch)
      .addPointLight(
        PointLight3D.create (torchPos, 10f)
        |> PointLight3D.withColor Color.Orange
        |> PointLight3D.withIntensity 1.5f
        |> PointLight3D.withCastsShadows true
      )
      // Spot light (flashlight)
      .addSpotLight(
        SpotLight3D.create (camPos, camDir, 20f)
        |> SpotLight3D.withIntensity 2.0f
      )
      .model(model.PlayerModel, model.PlayerTransform)
      .endCamera()
      .drop()
```

## Light types

### AmbientLight3D

Uniform base illumination applied to all surfaces.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Color` | `Color` | (required) | Base color |
| `Intensity` | `float32` | `1.0` | Brightness multiplier |

```fsharp
AmbientLight3D.create (Color(30, 30, 30, 255))
|> AmbientLight3D.withIntensity 0.5f
```

### DirectionalLight3D

Parallel light rays. Use for sun or moon.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Direction` | `Vector3` | (required) | Direction rays travel (should be normalized) |
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
| `Position` | `Vector3` | (required) | World-space position |
| `Color` | `Color` | `White` | Light color |
| `Intensity` | `float32` | `1.0` | Brightness multiplier |
| `Radius` | `float32` | (required) | Maximum distance of influence |
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

> _**TIP**_: Set `CastsShadows = true` sparingly. Each shadow-casting point light renders a cubemap shadow pass (a 6-sided shadow capture around the light). Two or three is a good target for performance.

### SpotLight3D

Cone-shaped light with inner and outer cutoff angles.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Position` | `Vector3` | (required) | World-space position |
| `Direction` | `Vector3` | (required) | Direction the cone points (should be normalized) |
| `Color` | `Color` | `White` | Light color |
| `Intensity` | `float32` | `1.0` | Brightness multiplier |
| `Radius` | `float32` | (required) | Maximum distance of influence |
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

**raylib**: the budgets are runtime-configurable via the `ForwardPbrPipeline` constructor:

```fsharp
let pipeline = ForwardPbrPipeline(
    maxPointLights = 16,
    maxSpotLights = 8
)
```

**MonoGame**: the budgets are **baked into the compiled PBR shader** (`MAX_POINT_LIGHTS` /
`MAX_SPOT_LIGHTS` constants in `ForwardPbr.fx`; note the file is named after the raylib
pipeline, but this is the MonoGame shader). The `ForwardPipeline` constructor takes no
light-count argument; to change them you recompile the `.fx` with different `#define`s.

Budgets apply **per camera block**: in buffers with more than one camera block, each block
gets its own ambient slot and light arrays (see
[Lights across camera blocks](#Lights-across-camera-blocks)); in single-camera buffers they
apply to the whole frame.

Exceeding the limit silently drops extra lights on both backends. For directional lights
specifically, only the **first** directional light in the active set is shaded, and only it
can cast shadows; later directional lights in the same set are ignored entirely.

## Lights across camera blocks

In single-camera buffers, lights are frame-global. In buffers with more than one camera
block, lights are scoped per camera block: a block that issues its own light commands starts
from the frame defaults (lights emitted before the first camera block or between blocks) and
applies its own commands in order; a block that issues none inherits the previous block's set
plus any lights emitted in between. A block can add lights but cannot remove inherited ones,
and lights emitted after the last block affect nothing. See
[Buffers & Commands → Light scoping](buffer-and-commands.html#Light-scoping-across-camera-blocks)
for the full placement rules.

Shadows follow the same scoping. Each camera block with shadow-casting lights renders its own
shadow map, so a multi-block buffer costs one shadow pass per block. `.setShadowOrigin(...)`
applies only to the block it appears in; the `.enableShadows()`/`.disableShadows()` toggle
carries into later blocks' initial state. Single-camera buffers behave exactly as single-pass
frames: one shadow map for the whole frame, no per-block cost.

## Shadow configuration

### Global bias

Shadow bias values control the tradeoff between two shadow artifacts: too low a bias and
surfaces speckle with self-shadowing noise (<abbr title="shadow acne: speckled self-shadowing from depth precision errors">acne</abbr>); too high and shadows detach from their
objects (<abbr title="peter-panning: shadows floating detached from the object casting them">peter-panning</abbr>). Set via `ShadowBiasConfig`:

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
> to `RasterizerState.DepthBias`; MonoGame can't use the GLSL `dFdx`/`dFdy` slope math the
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

The shadow atlas controls resolution and caster capacity. The single directional light is
the dominant shadow in most scenes, so by default it gets a dedicated region of the atlas
(`DirectionalAtlasRatio`, default `0.5`) rather than sharing one tile of the caster grid;
this keeps directional shadows high-resolution without tuning `MaxCasters` to your light
count. Point/spot casters subdivide the remaining atlas area into a square grid. Set
`DirectionalAtlasRatio` to `1.0` for directional-only scenes (the directional light gets
the whole atlas) or `0.0` to restore the uniform grid (every caster shares a
`1/MaxCasters` tile). `MaxCasters` must be a perfect square (4, 9, 16, 25, 36); a
constraint of the uniform grid, but enforced at atlas construction either way.

```fsharp
// raylib:
let pipeline = ForwardPbrPipeline(
    shadowAtlasConfig = {
        ShadowAtlasConfig.defaults with
            Resolution = 4096
            DirectionalLightSize = ValueSome 30.f
    }
)

// MonoGame (note the extra DirectionalOriginY field):
let pipeline = ForwardPipeline(
    shadowAtlas = {
        ShadowAtlasConfig.defaults with
            Resolution = 4096
            DirectionalOriginY = 0.0f       // MonoGame-only: lock shadow frustum Y
    }
)
```

> Higher `Resolution` produces sharper shadows but costs proportionally more: the shadow
> pass re-renders shadow-casting geometry into the directional region every frame, so
> doubling the resolution roughly doubles the shadow-pass fragment work. 4096 is a good
> high-quality default; 8192 is expensive.

> _**NOTE**: directional far-plane coverage._ The directional shadow camera's far plane
> is the light distance plus the full ortho size (`DirectionalLightSize`) plus a one-unit
> margin. Geometry
> farther than that from the light (measured along the light direction) is clipped and
> stops casting shadows; receivers there render fully lit. Deep scenes that slope away
> from the light (terrain, tall structures at the frustum edge) may need a larger
> `DirectionalLightSize` (at the cost of texel density) or a `DirectionalOriginY` /
> `OriginStrategy` that centers the frustum on the action.

| Field | Default | raylib | MonoGame | Description |
|-------|---------|--------|----------|-------------|
| `Resolution` | 2048 | ✓ | ✓ | Atlas texture resolution (square) |
| `MaxCasters` | 16 | ✓ | ✓ | Maximum point/spot shadow casters (perfect square). Unused for the directional caster when `DirectionalAtlasRatio > 0`. |
| `DirectionalAtlasRatio` | 0.5 | ✓ | ✓ | Fraction of the atlas the directional light occupies. `0.0` = uniform grid (every caster shares a `1/MaxCasters` tile). |
| `OriginStrategy` | `CameraTarget` | ✓ | ✓ | Where directional shadows are centered (`CameraTarget`/`SceneCenter`/`Custom`) |
| `DirectionalLightDistance` | auto | ✓ | ✓ | Distance to place the directional light camera behind the origin |
| `DirectionalLightSize` | auto | ✓ | ✓ | Full height of the directional shadow ortho window, in world units |
| `DirectionalOriginY` | 0.0 | (none) | ✓ | Lock the shadow frustum's vertical origin (prevents vertical sliding) |
| `GridSnapSize` | 2.0 | ✓ | ✓ | Snap shadow origin to a grid to reduce shimmer |

> _**NOTE**: shadow technique differs._ MonoGame cannot create a sampleable depth-only
> render target, so it writes shadow depth into an R32F color attachment (`DepthShadow.fx`)
> and samples it with a manual 3×3 <abbr title="percentage-closer filtering: sampling the shadow map several times around the pixel to soften shadow edges">PCF</abbr> over point-sampled depth; the same kernel the
> raylib backend runs against its real depth texture. The user-facing config and behavior
> (shadow quality, bias tuning) are equivalent; only the internal path differs.

## See also

- [Overview](overview.html): Architecture and pipeline setup
- [Buffer & Commands](buffer-and-commands.html): The buffer pipeline pattern
- [Materials](materials.html): PBR material system
