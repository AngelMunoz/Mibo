---
title: Camera
category: v1
categoryindex: 200
index: 4
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# Camera

Cameras control what part of the world you see and how it maps to the screen. Mibo.Raylib provides `Camera2D` for 2D games and `Camera3D` for 3D games. Both support single-camera, split-screen, and overlay patterns.

## What and Why

- **Scroll and zoom** — A 2D camera lets your game world be larger than the screen. Pan, zoom, and follow a player.
- **Perspective** — A 3D camera defines where you look from and where you look at.
- **Coordinate conversion** — Convert between screen pixels and world positions for mouse picking, UI placement, and debug tools.
- **Multi-camera** — Split-screen multiplayer, picture-in-picture minimaps, and HUD overlays on top of the game world.

## When to use

| Situation | Use |
|-----------|-----|
| 2D game with scrolling world | `Camera2D.create` + `Draw.beginCamera` |
| 2D game with split-screen or HUD | `Camera2DConfig` + `Draw.beginCameraWith` |
| 3D game | `Camera3D` struct + `Draw3D.beginCamera` |
| 3D split-screen or overlay | `Camera3DConfig` + `Draw3D.beginCameraWith` |
| Mouse picking in 3D | `Camera3D.screenPointToRay` |
| Culling off-screen objects | `Camera2D.viewportBounds` |

---

## 2D cameras

### Creating a camera

`Camera2D.create` centers the camera on a world position:

```fsharp
let camera = Camera2D.create (Vector2(400f, 300f)) 1.0f viewportSize
```

- `position` — world position to center on
- `zoom` — zoom factor (`1.0f` = no zoom)
- `viewportSize` — screen size in pixels (used to compute the offset)

### Using in a view

Wrap your world-space draw commands between `beginCamera` and `endCamera`. The `layer` parameter controls draw order — camera and content must share the same layer range.

```fsharp
buffer
|> Draw.beginCamera 0<RenderLayer> camera
|> Draw.fillRect (0<RenderLayer>, Color.Green) groundRect
|> Draw.fillCircle (0<RenderLayer>, Color.Red) (playerPos, 16f)
|> Draw.endCamera 999<RenderLayer>
|> Draw.text (1000<RenderLayer>, Color.White) hudTextState
```

> _**TIP**_: Put UI draws *after* `endCamera` on a higher layer so they render in screen space, not world space.

### Camera movement

Use `smoothFollow` to lerp the camera toward a target. Use `clampTarget` to keep the camera within world bounds. Both take the camera by reference.

```fsharp
let mutable cam = Camera2D.create startPos 1.0f viewportSize

// In your update function, each frame:
Camera2D.smoothFollow &cam playerPos 0.1f
Camera2D.clampTarget &cam 0f 0f worldWidth worldHeight
```

### Coordinate conversion

Convert between screen pixels and world positions:

```fsharp
// Mouse click in world space
let worldPos = Camera2D.screenToWorld camera mousePos

// Where does a world object appear on screen?
let screenPos = Camera2D.worldToScreen camera enemyPos
```

Use `viewportBounds` to get the visible world rectangle — useful for culling off-screen objects:

```fsharp
let visible = Camera2D.viewportBounds camera screenWidth screenHeight
```

---

## 2D multi-camera

`Camera2DConfig` lets you control viewport, clear color, and rendering behavior per camera. Build one with `Camera2D.render` and chain `with*` modifiers.

### Config modifiers

| Modifier | Description |
|----------|-------------|
| `Camera2D.withViewport rect` | Viewport in normalized screen coordinates (0–1) |
| `Camera2D.withClear color` | Clear with this color before rendering |

### Using a config in a view

```fsharp
let config =
    Camera2D.render worldCamera
    |> Camera2D.withClear Color.CornflowerBlue

buffer
|> Draw.beginCameraWith 0<RenderLayer> config
|> // ... world content ...
|> Draw.endCamera 999<RenderLayer>
```

### Split-screen

Pre-built helpers for two-player split-screen. Each clears with the given color.

```fsharp
let left = Camera2D.splitScreenLeft player1Camera Color.CornflowerBlue
let right = Camera2D.splitScreenRight player2Camera Color.DarkGreen

buffer
|> Draw.beginCameraWith 0<RenderLayer> left
|> // ... player 1 content ...
|> Draw.endCamera 99<RenderLayer>
|> Draw.beginCameraWith 100<RenderLayer> right
|> // ... player 2 content ...
|> Draw.endCamera 199<RenderLayer>
|> Draw.text (200<RenderLayer>, Color.White) hudState
```

Available split-screen helpers:

| Helper | Viewport |
|--------|----------|
| `Camera2D.splitScreenLeft` | Left half (0, 0, 0.5, 1) |
| `Camera2D.splitScreenRight` | Right half (0.5, 0, 0.5, 1) |
| `Camera2D.splitScreenTop` | Top half (0, 0, 1, 0.5) |
| `Camera2D.splitScreenBottom` | Bottom half (0, 0.5, 1, 0.5) |

### Overlay

Picture-in-picture overlay. Clears with black by default.

```fsharp
let minimapRect = Rectangle(0.75f, 0.0f, 0.25f, 0.25f)
let minimap = Camera2D.overlay topDownCamera minimapRect

buffer
|> Draw.beginCameraWith 0<RenderLayer> worldConfig
|> // ... main game ...
|> Draw.endCamera 99<RenderLayer>
|> Draw.beginCameraWith 100<RenderLayer> minimap
|> // ... minimap content ...
|> Draw.endCamera 199<RenderLayer>
```

---

## 3D cameras

### Creating a camera

For 3D rendering, create a `Camera3D` (raylib struct) directly. This is what `Draw3D.beginCamera` and `Camera3D.render` expect:

```fsharp
let camera = Camera3D(
    Vector3(0f, 10f, 20f),      // position
    Vector3.Zero,                // target
    Vector3.UnitY,               // up
    45.0f,                       // FOV in degrees
    CameraProjection.Perspective
)
```

For third-person or inspection cameras, use `Camera3D.orbit` which returns a `Camera3D` via spherical coordinates:

```fsharp
let orbitCam = Camera3D.orbit target yaw pitch radius fov aspect near far
```

> _**NOTE**_: `Camera3D.lookAt` and `Camera3D.orbit` return a `Camera` struct (view + projection matrices). This is useful for ray casting (`Camera3D.screenPointToRay`) but not for rendering. For rendering, create `Camera3D` directly or use `Camera3D.render` to build a config.

### Using in a view

```fsharp
buffer
|> Draw3D.beginCamera camera
|> Draw3D.drawModel playerModel playerTransform
|> Draw3D.addPointLight { Position = torchPos; Color = Color.White; Intensity = 1f; Radius = 10f; CastsShadows = false; ShadowBias = ValueNone }
|> Draw3D.endCamera
|> Draw3D.drop
```

### 3D config modifiers

`Camera3DConfig` controls viewport, clear color, and post-processing. Build with `Camera3D.render` and chain modifiers:

| Modifier | Description |
|----------|-------------|
| `Camera3D.withViewport rect` | Viewport in normalized screen coordinates (0–1) |
| `Camera3D.withClear color` | Clear with this color before rendering |
| `Camera3D.withPostProcess passes` | Use only specific post-process pass indices |
| `Camera3D.withoutPostProcess` | Disable post-processing for this camera |

```fsharp
let config =
    Camera3D.render mainCamera
    |> Camera3D.withClear Color.SkyBlue
    |> Camera3D.withoutPostProcess

buffer
|> Draw3D.beginCameraWith config
|> Draw3D.drawModel sceneModel sceneTransform
|> Draw3D.endCamera
|> Draw3D.drop
```

### Split-screen (3D)

```fsharp
let left = Camera3D.splitScreenLeft player1Camera Color.SkyBlue
let right = Camera3D.splitScreenRight player2Camera Color.SkyBlue

buffer
|> Draw3D.beginCameraWith left
|> // ... player 1 scene ...
|> Draw3D.endCamera
|> Draw3D.beginCameraWith right
|> // ... player 2 scene ...
|> Draw3D.endCamera
|> Draw3D.drop
```

### Overlay (3D)

```fsharp
let minimapRect = Rectangle(0.75f, 0.0f, 0.25f, 0.25f)
let minimap = Camera3D.overlay topDownCamera minimapRect

buffer
|> Draw3D.beginCameraWith mainConfig
|> // ... main scene ...
|> Draw3D.endCamera
|> Draw3D.beginCameraWith minimap
|> // ... minimap scene ...
|> Draw3D.endCamera
|> Draw3D.drop
```

> _**TIP**_: `Camera3D.overlay` disables post-processing by default. Re-enable it with `Camera3D.withPostProcess` if needed.

### Mouse picking

Cast a ray from a screen position into the 3D scene:

```fsharp
let ray = Camera3D.screenPointToRay camera mousePos viewportWidth viewportHeight
// ray.Position  — origin point
// ray.Direction — normalized direction into the scene
```

---

See also: [2D Rendering Overview](graphics2d/overview.html), [3D Rendering](graphics3d/overview.html), [Lighting & Shadows](graphics2d/lighting.html)
