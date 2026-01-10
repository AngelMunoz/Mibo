---
title: Animation
category: Amenities
index: 22
---

# Animation (2D Sprite Animation)

Mibo provides a format-agnostic 2D animation system in `Mibo.Animation`. It integrates directly with the existing `Graphics2D` rendering primitives.

## Core Types

| Type               | Purpose                                                    |
| ------------------ | ---------------------------------------------------------- |
| `Animation`        | A struct holding frame rectangles, duration, and loop flag |
| `GridAnimationDef` | Definition for animations in grid-based spritesheets       |
| `SpriteSheet`      | Texture + named animations with O(1) index-based access    |
| `AnimatedSprite`   | Runtime state (current frame, time, visual properties)     |

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

// 4. Draw (in your view)
sprite |> AnimatedSprite.draw position layer buffer
```

## SpriteSheet Factory Functions

### `SpriteSheet.fromGrid` – Uniform Grid Layouts

For spritesheets arranged in uniform rows/columns, use `GridAnimationDef`:

```fsharp
// Parameters: texture, frameWidth, frameHeight, columns, animations
let sheet = SpriteSheet.fromGrid texture 48 48 4 [|
  { Name = "idle";   Row = 0; StartCol = 0; FrameCount = 1; Fps = 1.0f;  Loop = false }
  { Name = "walk";   Row = 1; StartCol = 0; FrameCount = 4; Fps = 8.0f;  Loop = true }
  { Name = "attack"; Row = 2; StartCol = 0; FrameCount = 6; Fps = 12.0f; Loop = false }
|]
```

The `GridAnimationDef` struct has named fields for clarity:

```fsharp
[<Struct>]
type GridAnimationDef = {
  Name: string       // Animation name (e.g., "idle", "walk")
  Row: int           // Row in the sprite sheet (0-indexed)
  StartCol: int      // Starting column (0-indexed)
  FrameCount: int    // Number of frames
  Fps: float32       // Frames per second
  Loop: bool         // Does this animation loop?
}
```

### `SpriteSheet.single` – Explicit Frame Rectangles

When you know the exact pixel positions:

```fsharp
// Manually specified rectangles (e.g., from TexturePacker JSON)
let frames = [|
  Rectangle(0, 0, 64, 64)
  Rectangle(64, 0, 64, 64)
  Rectangle(128, 0, 64, 64)
|]
let sheet = SpriteSheet.single texture frames 10.0f true  // 10fps, looping
```

### `SpriteSheet.fromFrames` – Full Control

Most flexible – provide explicit `Animation` records:

```fsharp
let idleAnim: Animation = {
  Frames = [| Rectangle(0, 0, 48, 48) |]
  FrameDuration = 1.0f
  Loop = false
}

let walkAnim: Animation = {
  Frames = [| for i in 0..3 -> Rectangle(i * 48, 48, 48, 48) |]
  FrameDuration = 1.0f / 8.0f   // 8fps
  Loop = true
}

let sheet = SpriteSheet.fromFrames texture (Vector2(24.0f, 24.0f)) [|
  "idle", idleAnim
  "walk", walkAnim
|]
```

### `SpriteSheet.static'` – Single Static Frame

For non-animated sprites that share the same API:

```fsharp
let sheet = SpriteSheet.static' texture (Rectangle(0, 0, 32, 32))
let sprite = AnimatedSprite.create sheet "default"
```

## AnimatedSprite API

### Creation and Animation Control

```fsharp
// Create from sheet with named animation
let sprite = AnimatedSprite.create sheet "idle"

// Switch to a different animation (resets to frame 0)
let walkingSprite = sprite |> AnimatedSprite.play "walk"

// Hot-path alternative: resolve name to index once, use index thereafter
let walkIndex = sheet.AnimationIndices["walk"]
let walkingSprite = sprite |> AnimatedSprite.playByIndex walkIndex
```

### Update (Call Every Frame)

```fsharp
// Pure function – returns updated sprite with advanced frame/time
let updated = AnimatedSprite.update deltaTime sprite
```

### Visual Properties

```fsharp
sprite
|> AnimatedSprite.withScale 2.0f
|> AnimatedSprite.withColor Color.Red
|> AnimatedSprite.withRotation (MathF.PI / 4.0f)
|> AnimatedSprite.flipX true
|> AnimatedSprite.facingLeft   // shorthand for flipX
```

### Drawing

```fsharp
// Built-in draw (uses DrawTexture command)
sprite |> AnimatedSprite.draw position layer buffer

// With custom depth
sprite |> AnimatedSprite.drawWithDepth position 0.5f layer buffer

// Into specific destination rectangle
sprite |> AnimatedSprite.drawRect destRect layer buffer
```

### Manual Frame Access

For custom rendering or debugging:

```fsharp
let sourceRect = AnimatedSprite.currentSource sprite
let texture = sprite.Sheet.Texture

// Use with Draw2D builder for full control
Draw2D.sprite texture destRect
|> Draw2D.withSource sourceRect
|> Draw2D.submit buffer
```

## Animation Type

The `Animation` struct holds the raw data:

```fsharp
[<Struct>]
type Animation = {
  Frames: Rectangle[]       // Source rectangles in texture
  FrameDuration: float32    // Seconds per frame
  Loop: bool                // Restart when finished?
}
```

### Helpers

```fsharp
// Total duration of the animation
let duration = Animation.duration anim

// Check if playback finished (always false for looping)
let finished = AnimatedSprite.isFinished sprite
```

## Integration with Elmish

### In Your Model

```fsharp
type Model = {
  PlayerSprite: AnimatedSprite
  // ...
}
```

### Animation System

```fsharp
module Animation =
  let update (dt: float32) (model: Model) : struct (Model * Cmd<'Msg>) =
    let isMoving =
      model.Actions.Held.Contains MoveLeft ||
      model.Actions.Held.Contains MoveRight

    let playerSprite =
      if isMoving then
        model.PlayerSprite |> AnimatedSprite.play "walk"
      else
        model.PlayerSprite |> AnimatedSprite.play "idle"

    let updated = AnimatedSprite.update dt playerSprite

    { model with PlayerSprite = updated }, Cmd.none
```

### In Pipeline

```fsharp
let struct (model, cmds) =
  System.start model
  ...
  |> System.pipe (Animation.update dt)
  ...
```

> **Note:** since animations are immutable you can use them directly in the update function, or if you're using Systems, you can use them after snapshotting your model.

## Performance Tips

1. **Resolve animation names once**: Use `AnimationIndices` + `playByIndex` to avoid string allocations in update loops
2. **Share SpriteSheets**: create sheets once at init, reuse for all instances

```fsharp
// At init time
let walkIndex = sheet.AnimationIndices["walk"]

// In update (zero allocations)
let sprite = oldSprite |> AnimatedSprite.playByIndex walkIndex
```

## Texture Atlases & Sprite Management

Mibo is format-agnostic: a `SpriteSheet` is simply a **Texture** plus a set of **Source Rectangles**.

### Using Packed Atlases (TexturePacker, etc.)

If you use a tool like TexturePacker or Aseprite to pack your sprites into a single large texture:

1.  Load the atlas texture using `Assets.texture`.
2.  Parse your metadata (JSON, XML, etc.) to get the frame rectangles.
3.  Use `SpriteSheet.fromFrames` to map those rectangles to animation names.

> **Why Atlases?** Using a single texture atlas is highly recommended. It reduces draw calls and texture swaps on the GPU, leading to better performance, especially when drawing many different animated entities.

### What if my animations are in separate files?

If your animations are split across files (e.g., `hero_idle.png` and `hero_walk.png`):

- **Option A: Create separate SpriteSheets**. Since an `AnimatedSprite` can have its `Sheet` swapped at runtime, you can simply change the sheet when the state changes.
- **Option B: Combine them at build time**. It is generally better to use a tool to combine these into one atlas so they share a single `SpriteSheet` and texture.

```fsharp
// Example: Swapping sheets for separate files
let idleSheet = SpriteSheet.single idleTex idleFrames 10.0f true
let walkSheet = SpriteSheet.single walkTex walkFrames 12.0f true

// In your update:
let playerSprite =
    if isMoving then
        AnimatedSprite.create walkSheet "default" // swap to walk sheet
    else
        AnimatedSprite.create idleSheet "default"
```

## Parsing Metadata

Since Mibo doesn't force a specific format, you can easily plug in any parser:

```fsharp
// Example: pseudo-code for a custom loader
let loadHero (ctx: GameContext) =
    let tex = Assets.texture "hero_atlas" ctx
    let frames = MyJsonParser.parse "hero_metadata.json" // returns (string * Animation)[]
    SpriteSheet.fromFrames tex (Vector2(32.f, 32.f)) frames
```

See also: [Rendering 2D](rendering2d.html), [Assets](assets.html)
