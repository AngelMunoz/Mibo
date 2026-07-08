---
title: Buffer & Commands
category: 2D Rendering
categoryindex: 4
index: 12
---

# Buffer & Commands

Every frame, your view function receives a `RenderBuffer2D` and populates it with commands. This page covers all command types and how to use them.

## The buffer lifecycle

```
// Your view function signature:
val view : GameContext -> 'Model -> RenderBuffer2D -> unit
```

The buffer is **pre-cleared** by the renderer each frame. Do not call `Clear()` yourself. Just add commands:

```fsharp
let myView (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let bg = Rectangle(0f, 0f, 800f, 600f)
  let player = SpriteState.create(tex, Rectangle(100f, 100f, 32f, 32f), Rectangle(0f, 0f, 32f, 32f))

  buffer
  |> Draw.fillRect (0<RenderLayer>, Color.SkyBlue) bg
  |> Draw.sprite player
  |> Draw.drop
```

The `|> Draw.drop` at the end silences the unused-value warning. It does nothing.

> _**NOTE (backend types)**_: The snippets on this page use the **raylib** `Rectangle` (four `float32` fields). On the **MonoGame** backend, `Rectangle` is `Microsoft.Xna.Framework.Rectangle` with **int** fields — drop the `f` suffixes (`Rectangle(100, 100, 32, 32)`). `Vector2`/`Color` differ too. See [MonoGame type quirks](../monogame-types.html).

## Two ways to build commands

### Draw DSL (pipe-friendly)

Use the `Draw` module for everyday rendering. Every `Draw.*` function takes styling parameters first, geometry parameters second, and the buffer last — enabling partial application:

```fsharp
// Partial application: bind styling once
let redFill = Draw.fillRect (10<RenderLayer>, Color.Red)
let blueOutline = Draw.rectOutline (10<RenderLayer>, Color.Blue, 2f)

buffer
|> redFill groundRect
|> blueOutline groundRect
|> Draw.text (TextState.create(font, "Score: 100", Vector2(10f, 10f)))
```

### Command2D factories (command-first)

Use `Command2D.*` when you need to store a command or build one without a buffer:

```fsharp
let dest = Rectangle(0f, 0f, 64f, 64f)
let source = Rectangle(0f, 0f, 64f, 64f)
let mySprite = Command2D.sprite (SpriteState.create(tex, dest, source))

// Later, add to buffer:
buffer.Add(mySprite)
```

## Command reference

All commands live in two modules in `Mibo.Elmish.Graphics2D`:

| Category | Draw DSL function | What it draws |
|----------|------------------|---------------|
| **Sprite** | `Draw.sprite state` | Textured sprite via `DrawTexturePro` |
| **Text** | `Draw.text state` | Text via `DrawTextEx` |
| **Rect** | `Draw.fillRect (layer, color) rect` | Filled rectangle |
| | `Draw.rectOutline (layer, color, thickness) rect` | Rectangle outline |
| | `Draw.fillRectRounded (layer, color, roundness, segments) rect` | Rounded rectangle |
| | `Draw.rectRoundedOutline (layer, color, roundness, segments, thickness) rect` | Rounded outline |
| | `Draw.rectGradientV layer (x, y, w, h, top, bottom)` | Vertical gradient rect |
| | `Draw.rectGradientH layer (x, y, w, h, left, right)` | Horizontal gradient rect |
| | `Draw.rectGradient layer (rect, tl, bl, tr, br)` | 4-corner gradient rect |
| **Circle** | `Draw.fillCircle (layer, color) (center, radius)` | Filled circle |
| | `Draw.circleOutline (layer, color) (center, radius)` | Circle outline |
| | `Draw.circleSector (layer, color) (center, radius, startAngle, endAngle, segments)` | Pie slice |
| | `Draw.circleGradient layer (cx, cy, radius, inner, outer)` | Gradient circle |
| | `Draw.fillRing (layer, color) (center, innerR, outerR, startAngle, endAngle, segments)` | Ring / arc |
| | `Draw.ringOutline (layer, color) (center, innerR, outerR, startAngle, endAngle, segments)` | Ring outline |
| | `Draw.fillEllipse (layer, color) (cx, cy, radiusH, radiusV)` | Filled ellipse |
| | `Draw.ellipseOutline (layer, color) (cx, cy, radiusH, radiusV)` | Ellipse outline |
| **Line** | `Draw.line (layer, color) (start, finish)` | 1px line |
| | `Draw.lineThick (layer, color, thickness) (start, finish)` | Thick line |
| | `Draw.lineStrip (layer, color) points` | Connected line segments |
| | `Draw.bezier (layer, color, thickness) (start, control, finish)` | Quadratic bezier |
| **Triangle** | `Draw.triangle (layer, color) (v1, v2, v3)` | Filled triangle |
| | `Draw.triangleFan (layer, color) points` | Triangle fan |
| | `Draw.triangleStrip (layer, color) points` | Triangle strip |
| **Polygon** | `Draw.fillPoly (layer, color) (center, sides, radius, rotation)` | Regular polygon |
| | `Draw.polyOutline (layer, color, thickness) (center, sides, radius, rotation)` | Polygon outline |
| **Camera** | `Draw.beginCamera layer camera` | Start camera transform |
| | `Draw.endCamera layer` | End camera transform |
| **Shader** | `Draw.beginShader layer shader` | Start shader mode |
| | `Draw.endShader layer` | End shader mode |
| **Target** | `Draw.beginTarget layer target` | Render to texture |
| | `Draw.endTarget layer` | End render-to-texture |
| **State** | `Draw.setBlend layer mode` | Set blend mode |
| | `Draw.setScissor layer (x, y, w, h)` | Enable scissor rect |
| | `Draw.clearScissor layer` | Disable scissor |
| | `Draw.setLineWidth layer width` | Set line thickness |
| | `Draw.setViewport layer (x, y, w, h)` | Set viewport |
| | `Draw.setSamplerState layer sampler` | Set sprite sampler (MonoGame) |
| **Clear** | `Draw.clear layer color` | Clear background |
| **Immediate** | `Draw.drawImmediate layer action` | Escape hatch (see [Custom Commands](custom-commands.html)) |

## Per-command styling

All `Draw` functions group styling parameters first. This lets you bind them once:

```fsharp
let hudText text = Draw.text (TextState.create(font, text, Vector2(10f, 10f)))

// Reuse with different data:
buffer
|> hudText "HP: 100"
|> hudText "Score: 5000"
```

## State commands (blend, scissor, viewport)

State commands affect subsequent draws within the same layer range:

```fsharp
buffer
|> Draw.setBlend 0<RenderLayer> BlendMode.Additive
|> Draw.fillCircle (10<RenderLayer>, Color.Red) (center, 20f)
|> Draw.setBlend 0<RenderLayer> BlendMode.Alpha
```

Blend mode, scissor rect, line width, and viewport are reset at the start of each frame.

### Sampler state (MonoGame only)

`Draw.setSamplerState layer sampler` sets the sampler used for subsequent sprites, modeled on `setBlend`. It applies to subsequent draws and flushes/restarts the sprite batch. This is **MonoGame-only** — `SamplerState` is `Microsoft.Xna.Framework.Graphics.SamplerState` (e.g. `SamplerState.PointClamp`, `SamplerState.LinearClamp`).

```fsharp
buffer
|> Draw.setSamplerState 0<RenderLayer> SamplerState.PointClamp
|> Draw.sprite (SpriteState.create (atlas, dest, src))
```

The sampler defaults to `SamplerState.LinearClamp` and is reset each frame, alongside blend mode, scissor, line width, and viewport.

> **raylib** has no equivalent command — a texture's filter is a property of the texture, not the batch. Override the load-time default with the `Texture.filter` helper: `assets.Texture "tiles.png" |> Texture.filter TextureFilter.Point` (apply once at load/init, not per frame). See [Assets](../assets.html).

## Text and sprite state types

Sprite and text use state records. Use the `create` builders for quick setup. A `SpriteState` exposes the full field set: `Texture`, `Dest`, `Source`, `Origin`, `Rotation` (radians), `Color`, `Layer`, and `NormalMap` (for 2D lighting).

```fsharp
// Sprite: texture + destination + source rect
let sprite = SpriteState.create(tex, Rectangle(100f, 100f, 32f, 32f), Rectangle(0f, 0f, 32f, 32f))

// Sprite with custom color
let redSprite = { sprite with Color = Color.Red; Layer = 10<RenderLayer> }

// SpriteState also exposes Rotation (radians), Origin (pivot, pixels), and NormalMap (2D lighting).
let spinning = { sprite with Rotation = 0.785f } // ~45 degrees

// Text: font + string + position
let scoreText = TextState.create(font, "Score: 100", Vector2(10f, 10f))

// Text with custom size
let bigText = { scoreText with FontSize = 24f; Color = Color.Yellow; Layer = 100<RenderLayer> }
```

## Cameras

Wrap world-space content between `Draw.beginCamera` and `Draw.endCamera`:

```fsharp
let camera = Camera2D.create (Vector2(400f, 300f)) 1.0f viewportSize
let hudLabel = TextState.create(font, "HUD", Vector2(10f, 10f))

buffer
|> Draw.beginCamera 0<RenderLayer> camera
|> Draw.fillCircle (10<RenderLayer>, Color.Red) (worldPos, 20f)
|> Draw.endCamera 1000<RenderLayer>
// After endCamera, draws are in screen space:
|> Draw.text hudLabel
```

See [Camera](../camera.html) for details.
