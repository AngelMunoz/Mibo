---
title: Animation
category: Amenities
categoryindex: 12
index: 2
---

# Animation (2D Sprite Animation)

Mibo provides a format-agnostic 2D animation system in `Mibo.Animation`. It integrates with the `.sprite(...)` / `.litSprite(...)` rendering pipeline.

## Core Types

| Type | Purpose |
| ---- | ------- |
| `Animation` | A struct holding frame rectangles, duration, and loop flag |
| `GridAnimationDef` | Definition for animations in grid-based spritesheets |
| `SpriteSheet` | Texture + named animations with O(1) index-based access |
| `AnimatedSprite` | Runtime state (current frame, time, visual properties) |

## Quick Start

```fsharp
open Mibo.Animation

// 1. Create a SpriteSheet from a uniform grid
let sheet = SpriteSheet.fromGrid texture 32 32 8 [|
  { Name = "idle"; Row = 0; StartCol = 0; FrameCount = 1; Fps = 1.0f; Loop = false }
  { Name = "walk"; Row = 1; StartCol = 0; FrameCount = 4; Fps = 8.0f; Loop = true }
|]

// 2. Create an AnimatedSprite
let sprite = AnimatedSprite.create sheet "idle"

// 3. Update each frame (in your animation system)
let updatedSprite = AnimatedSprite.update deltaTime sprite

// 4. Draw (in your view); lit path: pass the AnimatedSprite directly
buffer
  .litAnimatedSprite(lighting, Rectangle(position.X, position.Y, 32f, 32f), sprite, layer = 10<RenderLayer>)
  .drop()
```

## SpriteSheet Factory Functions

### `SpriteSheet.fromGrid`: Uniform Grid Layouts

```fsharp
let sheet = SpriteSheet.fromGrid texture 48 48 4 [|
  { Name = "idle";   Row = 0; StartCol = 0; FrameCount = 1; Fps = 1.0f;  Loop = false }
  { Name = "walk";   Row = 1; StartCol = 0; FrameCount = 4; Fps = 8.0f;  Loop = true }
  { Name = "attack"; Row = 2; StartCol = 0; FrameCount = 6; Fps = 12.0f; Loop = false }
|]
```

The `GridAnimationDef` struct:

```fsharp
[<Struct>]
type GridAnimationDef = {
  Name: string
  Row: int
  StartCol: int
  FrameCount: int
  Fps: float32
  Loop: bool
}
```

### `SpriteSheet.single`: Explicit Frame Rectangles

```fsharp
let frames = [|
  Rectangle(0, 0, 64, 64)
  Rectangle(64, 0, 64, 64)
  Rectangle(128, 0, 64, 64)
|]
// 10.0f = frames per second, true = loop
let sheet = SpriteSheet.single texture frames 10.0f true
```

### `SpriteSheet.fromFrames`: Full Control

```fsharp
let idleAnim: Animation = {
  Frames = [| Rectangle(0, 0, 48, 48) |]
  FrameDuration = 1.0f
  Loop = false
}

let walkAnim: Animation = {
  Frames = [| for i in 0..3 -> Rectangle(i * 48, 48, 48, 48) |]
  FrameDuration = 1.0f / 8.0f
  Loop = true
}

let sheet = SpriteSheet.fromFrames texture (Vector2(24.0f, 24.0f)) [|
  "idle", idleAnim
  "walk", walkAnim
|]
```

### `SpriteSheet.static'`: Single Static Frame

```fsharp
let sheet = SpriteSheet.static' texture (Rectangle(0, 0, 32, 32))
let sprite = AnimatedSprite.create sheet "default"
```

### Animation Index Queries

```fsharp
let walkIdx =
  match SpriteSheet.tryGetAnimationIndex "walk" sheet with
  | ValueSome idx -> idx
  | ValueNone -> 0

// oldSprite: the sprite you are switching animations on
let sprite = oldSprite |> AnimatedSprite.playByIndex walkIdx
```

## AnimatedSprite API

### Creation and Animation Control

```fsharp
let sprite = AnimatedSprite.create sheet "idle"
// createWith: sheet, animation name, tint color, scale
let colored = AnimatedSprite.createWith sheet "idle" Color.Red 1.5f
let walkingSprite = sprite |> AnimatedSprite.play "walk"
let resumedSprite = sprite |> AnimatedSprite.playIfNot "walk"
let restartedSprite = sprite |> AnimatedSprite.restart
let isWalking = sprite |> AnimatedSprite.isPlaying "walk"
```

### Update

```fsharp
let updated = AnimatedSprite.update deltaTime sprite
```

### Visual Properties

```fsharp
sprite
|> AnimatedSprite.withScale 2.0f
|> AnimatedSprite.withColor Color.Red
|> AnimatedSprite.withRotation (MathF.PI / 4.0f)
|> AnimatedSprite.flipX true
|> AnimatedSprite.facingLeft
```

### Drawing

**Lit path**: `.litAnimatedSprite(...)` consumes the `AnimatedSprite` directly: it extracts the current frame's source rect, applies `FlipX`/`FlipY`, and picks up the sheet's texture and normal map:

```fsharp
buffer
  .litAnimatedSprite(lighting, playerDest, model.PlayerSprite, layer = 20<RenderLayer>)
  .drop()
```

**Unlit path**: use `AnimatedSprite.currentSource` to get the current frame's source rectangle, then draw a sprite record:

```fsharp
let src = AnimatedSprite.currentSource sprite

buffer
  .sprite(
    SpriteState.create(sprite.Sheet.Texture, Rectangle(position.X, position.Y, 32f, 32f), src)
    |> SpriteState.withLayer 10<RenderLayer>
  )
  .drop()
```

## Animation Type

```fsharp
[<Struct>]
type Animation = {
  Frames: Rectangle[]
  FrameDuration: float32
  Loop: bool
}
```

### Helpers

```fsharp
let totalTime = Animation.duration anim
let spriteTime = AnimatedSprite.duration sprite
let finished = AnimatedSprite.isFinished sprite
```

## Performance Tips

1. **Resolve animation names once**: Use `AnimationIndices` + `playByIndex` to avoid string allocations in update loops
2. **Share SpriteSheets**: create sheets once at init, reuse for all instances

```fsharp
// At init time
let walkIndex = sheet.AnimationIndices["walk"]

// In update (zero allocations)
let updatedSprite = oldSprite |> AnimatedSprite.playByIndex walkIndex
```

## Texture Atlases & Sprite Management

Mibo is format-agnostic: a `SpriteSheet` is a **Texture** plus a set of **Source Rectangles**.

The concrete types are backend-native: `Texture2D`/`Rectangle` from `Raylib_cs` (raylib) or `Microsoft.Xna.Framework` (MonoGame). Obtain them via the service registry (`GameContext.getService<IAssets> ctx`), then call its loaders:

```fsharp
// Example: pseudo-code for a custom loader
let loadHero (ctx: GameContext) =
    let assets = GameContext.getService<IAssets> ctx
    let tex = assets.Texture("hero_atlas")  // raylib: "hero_atlas.png"; MonoGame: "hero_atlas" (content name)
    let frames = MyJsonParser.parse "hero_metadata.json"
    SpriteSheet.fromFrames tex (Vector2(32.f, 32.f)) frames
```

## See Also

- [Rendering 2D](graphics2d/overview.html)
- [Rendering overview](rendering.html)
