---
title: 2D Lighting & Shadows
index: 12
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# 2D Lighting & Shadows

Mibo.Raylib includes a GPU-driven 2D lighting system with soft shadows using analytic Signed Distance Field (SDF) raymarching — no shadow atlas, no extra render passes.

## What and Why

- **Point lights** — Radial lights with configurable radius, falloff, intensity, and color (torches, lamps, explosions).
- **Directional lights** — Parallel rays with a direction vector (sun, moon).
- **Ambient light** — Base illumination for the entire scene.
- **Shadows** — Per-light toggle. Soft shadows via SDF sphere tracing in the pixel shader. Penumbra softness is configurable.
- **Occluders** — Line segments that block light, cast from grid-based levels or placed manually.
- **Lit sprites** — Textured sprites that receive lighting. Unlit sprites (`Draw.sprite`) render at full brightness.

Everything runs on the GPU via a custom lit-sprite shader. Light data is uploaded once per frame as shader uniforms.

## Quick start

1. Create `LightContext2D` in `init`, store in your model
2. Each frame: `ctx.Reset()` at the start of your view
3. Set ambient light, add lights and occluders
4. Draw lit sprites via `LightDraw.litSprite`
5. End the lighting pass via `LightDraw.endLighting` (sprites after this are unlit)

## Setup

Create a `LightContext2D` in your `init` and store it in your model:

```fsharp
open Mibo.Elmish.Graphics2D.Lighting

let init (ctx: GameContext) =
    let lighting = new LightContext2D(
        softness = 0.05f,          // shadow penumbra softness
        maxShadowDistance = 2000f  // max raymarch distance
    )
    { Lighting = lighting }, Cmd.none
```

Parameters:

| Param | Default | Description |
|-------|---------|-------------|
| `litShader` | built-in | Custom GLSL shader (must match uniform layout) |
| `maxDirLights` | 4 | Max directional lights per frame |
| `maxPointLights` | 16 | Max point lights per frame |
| `maxOccluders` | 128 | Max occluder segments per frame |
| `softness` | 0.05 | Shadow penumbra softness (0 = hard, 0.2 = very soft) |
| `maxShadowDistance` | 5000 | Max raymarch distance for directional shadows |

## Frame lifecycle

```fsharp
let myView (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
    // 1. Reset at start of every frame
    model.Lighting.Reset()

    buffer
    // 2. Set ambient light
    |> LightDraw.setAmbient model.Lighting (5<RenderLayer>, { Color = Color(30, 30, 30, 255) })

    // 3. Add directional light (sun)
    |> LightDraw.addDirectionalLight model.Lighting 6<RenderLayer> {
        Direction = Vector2(0.3f, -0.7f)
        Color = Color.White
        Intensity = 0.8f
        CastsShadows = true
    }

    // 4. Add point lights
    |> LightDraw.addPointLight model.Lighting 7<RenderLayer> {
        Position = torchPos
        Color = Color.Orange
        Intensity = 1.0f
        Radius = 200f
        Falloff = 2.0f
        CastsShadows = false
    }

    // 5. Add occluders for shadow casting
    for o in model.Occluders do
        buffer |> LightDraw.addOccluder model.Lighting 8<RenderLayer> o

    // 6. Draw lit sprites
    |> LightDraw.litSprite model.Lighting {
        Texture = tex
        Dest = r (int x) (int y) 32 32
        Source = r 0 0 32 32
        Origin = Vector2.Zero; Rotation = 0f
        Color = Color.White; Layer = 10<RenderLayer>
    }

    // 7. End lighting pass (sprites after this are unlit)
    |> LightDraw.endLighting model.Lighting 999<RenderLayer>

    // 8. Unlit HUD
    |> Draw.text { ... with Layer = 1000<RenderLayer> }
```

## Light types

### AmbientLight2D

```fsharp
{ Color = Color(30, 30, 30, 255) }  // dim base illumination
```

Applied uniformly to all lit sprites. Use a low value so directional/point lights add visible contrast.

### PointLight2D

```fsharp
{
    Position = Vector2(400f, 300f)
    Color = Color.Orange
    Intensity = 1.0f
    Radius = 200f       // world units
    Falloff = 2.0f      // 1 = linear, 2 = quadratic
    CastsShadows = true
}
```

The falloff exponent controls brightness decay. Quadratic (2.0) gives a realistic light falloff. Linear (1.0) gives a wider, softer reach.

### DirectionalLight2D

```fsharp
{
    Direction = Vector2(0.3f, -0.7f)   // shines down-right
    Color = Color.White
    Intensity = 0.8f
    CastsShadows = true
}
```

The direction is the **inward** direction of the light rays (toward the scene). `(0, -1)` points straight down. `(0.3, -0.7)` points down-right at ~23° from vertical.

## Shadows

Shadows use **SDF raymarching** in the pixel shader. Each shadow-casting light sends rays from the fragment position toward the light, stepping along the scene's signed distance field built from occluder segments.

### Occluders

Occluders are 2D line segments. Add them individually via `LightDraw.addOccluder` or auto-generate from a grid:

```fsharp
open Mibo.Layout

// Generate occluders for exposed edges of solid cells
let occluders =
    GridOccluders.fromCellGrid
        (fun tile -> tile = Tile.Wall)   // isSolid predicate
        GridOccluders.Edge.All            // which edges
        grid

// In your view:
for o in occluders do
    buffer |> LightDraw.addOccluder model.Lighting 8<RenderLayer> o
```

The `GridOccluders.Edge` flags control which cell edges produce occluders:
- `Edge.All` — top-down games (all four sides)
- `Edge.Bottom ||| Edge.Left ||| Edge.Right` — platformers (skip top edge so player can stand on it without self-shadowing)
- `Edge.Top` — ceilings only

### Shadow quality

| Param | Effect |
|-------|--------|
| `softness` | Penumbra width. 0 = hard pixel-perfect, 0.05 = typical soft, 0.2 = very blurry |
| `maxShadowDistance` | How far directional shadows raymarch. Lower = faster but shadows fade near edges |
| Occluder count | More segments = more accurate shadows but more GPU work. 128 default |

Point light shadows are bounded by the light's radius, so they're cheaper than directional shadows which raymarch up to `maxShadowDistance`.

### Performance

- Occluders are uploaded as a uniform array to the GPU each frame (max 128 by default).
- The shadow raymarch loops up to 64 iterations per lit pixel per shadow-casting light.
- Keep shadow-casting lights few (1–2 directional, 2–4 point) for good performance.

## Unlit rendering

Sprites drawn with `Draw.sprite` (instead of `LightDraw.litSprite`) render at full brightness, ignoring lighting. This is useful for UI, minimaps, or any element that shouldn't be affected by scene lighting.

## Shadow toggle

You can enable or disable shadows globally via `LightContext2D.ShadowsEnabled`:

```fsharp
// Disable shadows (property)
model.Lighting.ShadowsEnabled <- false

// Or use commands in the render buffer
buffer
|> LightDraw.disableShadows model.Lighting 90<RenderLayer>
|> // ... sprites drawn here won't cast/receive shadows ...
|> LightDraw.enableShadows model.Lighting 100<RenderLayer>
```

`Reset()` re-enables shadows automatically each frame.

When to disable shadows:

- **Performance** — Shadows are the most expensive part of the lighting pipeline. Disable on low-end hardware or when you have many shadow-casting lights.
- **Stylized look** — Flat lighting without shadows suits certain art styles (e.g., retro pixel art).
- **Interior scenes** — Disable directional shadows in small rooms where they add little visual value.

> _**TIP**_: Disable shadows per-section rather than globally. Use `disableShadows`/`enableShadows` to skip shadows only for specific layers (e.g., background tiles) while keeping them for foreground objects.

## See Also

- [Particles](particles.html) — Batched particle rendering
- [Buffer & Commands](buffer-and-commands.html) — SpriteState reference
- [Custom Commands](custom-commands.html) — Implementing custom lighting passes
