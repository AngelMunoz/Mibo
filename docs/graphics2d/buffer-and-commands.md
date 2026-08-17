---
title: Buffer & Commands
category: 2D Rendering
categoryindex: 10
index: 2
---

# Buffer & Commands

Every frame, your view function receives a `RenderBuffer2D` and populates it with commands. This page explains how to think about the buffer; the member-level details live in [Draw DSL](../draw-dsl.html) and the API reference.

## The buffer lifecycle

```fsharp
// Your view function signature: three inputs (context, model, buffer),
// and it does its work by adding commands to the buffer
val view : GameContext -> 'Model -> RenderBuffer2D -> unit
```

The buffer is **pre-cleared** by the renderer each frame. Do not call `Clear()` yourself; add commands:

```fsharp
let myView (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let player = SpriteState.create(tex, Rectangle(100f, 100f, 32f, 32f), Rectangle(0f, 0f, 32f, 32f))

  buffer
    .fillRect(0f, 0f, 800f, 600f, Color.SkyBlue)
    .sprite(player)
    .drop()
```

The `.drop()` at the end silences the unused-value warning. It does nothing.

## Layers, not call order

Commands execute in **layer order**, not insertion order. Every 2D member takes an optional `layer` (default `0<RenderLayer>`; `RenderLayer` is F#'s unit-of-measure syntax, a compile-time tag that keeps layer values distinct from plain numbers); within one layer, insertion order is preserved. This means you can write your view in whatever order reads best: backgrounds low, world in the middle, UI high:

```fsharp
buffer
  .text(font, "HP 100", Vector2(10f, 10f), 20f, layer = 1001<RenderLayer>)
  .fillCircle(worldPos, 20f, Color.Red, layer = 10<RenderLayer>)
  .rectGradientV(0, 0, w, h, skyTop, skyBot, layer = -1000<RenderLayer>)
  .drop()
```

Even though the text was added first, the sky gradient draws first, then the circle, then the UI. (`w`, `h`, `skyTop`, `skyBot` are your values: screen size and gradient colors.)

## Neutral inputs, backend records

The fluent DSL takes `Mibo.Color`, `System.Numerics` vectors, and float rectangle coordinates on **both backends** — the buffer converts for you. Backend state records (`SpriteState`, `TextState`, light records, particle arrays) pass through as the backend's own types: **raylib** `Rectangle` (four `float32` fields) vs **MonoGame** `Microsoft.Xna.Framework.Rectangle` (**int** fields). See [MonoGame type quirks](../monogame-types.html).

Sprites and text have builder-equipped state records when you want to carry them around:

```fsharp
let sprite = SpriteState.create(tex, Rectangle(100f, 100f, 32f, 32f), Rectangle(0f, 0f, 32f, 32f))
let redSprite = { sprite with Color = Color.Red; Layer = 10<RenderLayer> }
// Rotation is in radians; 0.785f is about 45 degrees
let spinning = { sprite with Rotation = 0.785f }

buffer.sprite(sprite).drop()
```

For text, the parts form is usually shortest — `size` maps to each backend's sizing model (raylib: font size in pixels; MonoGame: uniform scale):

```fsharp
buffer
  .text(font, "Score: 100", Vector2(10f, 10f), 20f, tint = Color.Yellow, layer = 100<RenderLayer>)
  .drop()
```

## State commands (blend, scissor, viewport)

State commands affect subsequent draws within the same layer range:

```fsharp
buffer
  .setBlend(BlendMode.Additive)
  .fillCircle(center, 20f, Color.Red, layer = 10<RenderLayer>)
  .setBlend(BlendMode.AlphaBlend)
  .drop()
```

Blend mode, scissor rect, line width, and viewport are reset at the start of each frame.

### Sampler state (MonoGame only)

`.setSamplerState(sampler, ?layer)` sets the sampler used for subsequent sprites and flushes/restarts the sprite batch. It is **MonoGame-only**: calling it on the raylib buffer is a compile error, because the raylib buffer has no corresponding operation.

```fsharp
buffer
  .setSamplerState(SamplerState.PointClamp, layer = 9<RenderLayer>)
  .sprite(SpriteState.create(atlas, dest, src))
  .drop()
```

The sampler defaults to `SamplerState.LinearClamp` and is reset each frame, alongside blend mode, scissor, line width, and viewport.

> **raylib** has no equivalent command — a texture's filter is a property of the texture, not the batch. Override the load-time default with the `Texture.filter` helper: `assets.Texture "tiles.png" |> Texture.filter TextureFilter.Point` (apply once at load/init, not per frame). See [Assets](../assets.html).

## Cameras

Wrap world-space content between `.beginCamera(...)` and `.endCamera(...)`:

```fsharp
let camera = Camera2D.create (Vector2(400f, 300f)) 1.0f viewportSize

buffer
  .beginCamera(camera)
  .fillCircle(worldPos, 20f, Color.Red, layer = 10<RenderLayer>)
  .endCamera(layer = 1000<RenderLayer>)
  // After endCamera, draws are in screen space:
  .text(font, "HUD", Vector2(10f, 10f), 20f, layer = 1001<RenderLayer>)
  .drop()
```

See [Camera](../camera.html) for details.

## Next steps

- [Draw DSL](../draw-dsl.html): the full fluent surface with defaults
- [Custom Commands](custom-commands.html): the `.drawImmediate(...)` escape hatch
- [Performance](performance.html): writing efficient rendering code
