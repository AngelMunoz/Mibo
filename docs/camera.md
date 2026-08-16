---
title: Camera
category: Rendering
categoryindex: 9
index: 3
---

# Camera

Cameras control what part of the world you see and how it maps to the screen. Mibo provides `Camera2D` for 2D games and `Camera3D` for 3D games. Both support single-camera and split-screen patterns. The fluent `.beginCamera(...)`/`.beginCameraWith(...)` members and the `Camera2DConfig`/`Camera3DConfig` modifiers share the same shape across backends; only the underlying camera struct's field layout is backend-specific.

## What and Why

- **Scroll and zoom** — A 2D camera lets your game world be larger than the screen. Pan, zoom, and follow a player.
- **Perspective** — A 3D camera defines where you look from and where you look at.
- **Coordinate conversion** — Convert between screen pixels and world positions for mouse picking, UI placement, and debug tools.
- **Multi-camera** — Split-screen multiplayer, picture-in-picture minimaps, and HUD overlays on top of the game world.

## When to use

| Situation | Use |
|-----------|-----|
| 2D game with scrolling world | `Camera2D.create` + `.beginCamera(...)` |
| 2D game with split-screen or HUD | `Camera2DConfig` + `.beginCameraWith(...)` |
| 3D game | `Camera3D` struct + `.beginCamera(...)` |
| 3D split-screen or picture-in-picture | `Camera3DConfig` + `.beginCameraWith(...)` |
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

> _**NOTE — vector types.**_ Each backend's `Camera2D.create`/`Camera3D` takes that backend's native vector type — raylib uses `System.Numerics`, MonoGame uses `Microsoft.Xna.Framework` — so make sure the matching namespace is `open`. (The `Vector3(...)` used by `Camera3D` follows the same rule.) Note that the Core layout APIs (`CellGrid2D`, `LayeredGrid2D`) always take `System.Numerics.Vector2` and must be explicitly qualified in MonoGame projects; see the note on the [2D Layout Engine](level-design/2d/core.html) page.

### Using in a view

Wrap your world-space draw commands between `.beginCamera(...)` and `.endCamera(...)`. The `layer` parameter controls draw order — camera and content must share the same layer range.

```fsharp
buffer
  .beginCamera(camera)
  .fillRect(0f, 0f, 800f, 600f, Color.Green)
  .fillCircle(playerPos, 16f, Color.Red)
  .endCamera(layer = 999<RenderLayer>)
  .text(font, "HUD", Vector2(10f, 10f), 20f, layer = 1000<RenderLayer>)
  .drop()
```

> _**TIP**_: Put UI draws *after* `.endCamera(...)` on a higher layer so they render in screen space, not world space.

### Camera movement

Use `smoothFollow` to lerp the camera toward a target, and `clampTarget` to keep it within world bounds. The call shape differs per backend: the raylib camera is a native mutable struct (mutated by reference), while the MonoGame camera has immutable fields (the helpers return a new camera).

```fsharp
// raylib — mutates the camera in place (note the &)
let mutable cam = Camera2D.create startPos 1.0f viewportSize

// In your update function, each frame:
Camera2D.smoothFollow &cam playerPos 0.1f
Camera2D.clampTarget &cam 0f 0f worldWidth worldHeight

// MonoGame — returns a new camera (no &)
let cam = Camera2D.create startPos 1.0f viewportSize
let cam = Camera2D.smoothFollow cam playerPos 0.1f
let cam = Camera2D.clampTarget cam 0f 0f worldWidth worldHeight
```

### Coordinate conversion

Convert between screen pixels and world positions. `screenToWorld` / `worldToScreen` / `viewportBounds` are available on both backends — on raylib you pass the camera by reference (`&`) to avoid copying the native struct, on MonoGame the camera is an immutable value (no `&`):

```fsharp
// raylib
let worldPos = Camera2D.screenToWorld &camera mousePos
let screenPos = Camera2D.worldToScreen &camera enemyPos
let visible = Camera2D.viewportBounds &camera screenWidth screenHeight

// MonoGame
let worldPos = Camera2D.screenToWorld camera mousePos
let visible = Camera2D.viewportBounds camera screenWidth screenHeight
```

Use `viewportBounds` to get the visible world rectangle — useful for culling off-screen objects (it pairs with `Culling.isVisible2D`).

---

## 2D multi-camera

`Camera2DConfig` lets you control viewport, clear color, and rendering behavior per camera. Build one with `Camera2D.render` and chain `with*` modifiers.

### Config modifiers

| Modifier | Description |
|----------|-------------|
| `Camera2D.withViewport rect` | raylib: normalized screen coordinates (0–1); MonoGame: pixel `Rectangle` |
| `Camera2D.withClear color` | Clear with this color before rendering |

### Using a config in a view

```fsharp
let config =
    Camera2D.render worldCamera
    |> Camera2D.withClear Color.CornflowerBlue

buffer
  .beginCameraWith(config)
  // ... world content ...
  .endCamera(layer = 999<RenderLayer>)
  .drop()
```

### Split-screen

Pre-built helpers for two-player split-screen. Each clears with the given color. On raylib the split-screen halves the full screen (normalized 0–1); on MonoGame you pass the parent viewport bounds in pixels (typically the window size) as the last argument.

```fsharp
let left = Camera2D.splitScreenLeft player1Camera Color.CornflowerBlue
let right = Camera2D.splitScreenRight player2Camera Color.DarkGreen

buffer
  .beginCameraWith(left)
  // ... player 1 content ...
  .endCamera(layer = 99<RenderLayer>)
  .beginCameraWith(right, layer = 100<RenderLayer>)
  // ... player 2 content ...
  .endCamera(layer = 199<RenderLayer>)
  .text(font, "HUD", Vector2(10f, 10f), 20f, layer = 200<RenderLayer>)
  .drop()
```

Available split-screen helpers:

| Helper | Viewport |
|--------|----------|
| `Camera2D.splitScreenLeft` | Left half (0, 0, 0.5, 1) |
| `Camera2D.splitScreenRight` | Right half (0.5, 0, 0.5, 1) |
| `Camera2D.splitScreenTop` | Top half (0, 0, 1, 0.5) |
| `Camera2D.splitScreenBottom` | Bottom half (0, 0.5, 1, 0.5) |

For a picture-in-picture view (e.g. a minimap), compose one yourself with
`Camera2D.render` + `withViewport` + `withClear`, and emit that camera after the
main one so it draws on top — there is no built-in `overlay` helper, and
layering is purely draw order.

---

## 3D cameras

### Creating a camera

For 3D rendering, use `Camera3D.create`. It takes just three parameters — position, target, and field of view — with sensible defaults for everything else (up = `Vector3.Up`; MonoGame also defaults near = `0.1f`, far = `1000f` and computes aspect from the viewport at render time):

```fsharp
// raylib (FOV in degrees)
let camera = Camera3D.create (Vector3(0f, 10f, 20f)) Vector3.Zero 45.0f

// MonoGame (FOV in radians)
let camera = Camera3D.create (Vector3(0f, 10f, 20f)) Vector3.Zero (MathF.PI / 4f)
```

For third-person or inspection cameras, use `Camera3D.orbit` (both backends):

```fsharp
// raylib (FOV in degrees)
let camera = Camera3D.orbit Vector3.Zero yaw pitch radius 55.0f

// MonoGame (FOV in radians)
let camera = Camera3D.orbit Vector3.Zero yaw pitch radius (MathF.PI / 4f)
```

#### Camera modifiers

Chain `with*` modifiers to override the defaults:

```fsharp
// Custom up vector (both backends)
let camera = Camera3D.create pos target fov |> Camera3D.withUp customUp

// Orthographic projection (both backends; FovY is reinterpreted as view height)
let camera = Camera3D.create pos target 10f |> Camera3D.asOrthographic

// Custom near/far planes (MonoGame only — raylib manages these internally)
let camera = Camera3D.create pos target fov |> Camera3D.withNearFar 0.01f 5000f
```

> _**NOTE — backend difference.**_ Both backends share the same constructor
> surface (`create` / `orbit`) and modifiers (`withUp` / `asOrthographic`).
> MonoGame adds `withNearFar` (raylib manages near/far internally via
> `BeginMode3D`). The FOV unit differs: raylib uses **degrees**, MonoGame
> uses **radians**.

### Using in a view

```fsharp
buffer
  .beginCamera(camera)
  .model(playerModel, playerTransform)
  .addPointLight { Position = torchPos; Color = Color.White; Intensity = 1f; Radius = 10f; CastsShadows = false; ShadowBias = ValueNone }
  .endCamera()
  .drop()
```

### 3D config modifiers

`Camera3DConfig` controls viewport and clear color. Build with `Camera3D.render` and chain modifiers:

| Modifier | Description |
|----------|-------------|
| `Camera3D.withViewport rect` | Viewport in normalized screen coordinates (0–1) |
| `Camera3D.withClear color` | Clear with this color before rendering |

```fsharp
let config =
    Camera3D.render mainCamera
    |> Camera3D.withClear Color.SkyBlue

buffer
  .beginCameraWith(config)
  .model(sceneModel, sceneTransform)
  .endCamera()
  .drop()
```

### Split-screen (3D)

```fsharp
let left = Camera3D.splitScreenLeft player1Camera Color.SkyBlue
let right = Camera3D.splitScreenRight player2Camera Color.SkyBlue

buffer
  .beginCameraWith(left)
  // ... player 1 scene ...
  .endCamera()
  .beginCameraWith(right)
  // ... player 2 scene ...
  .endCamera()
  .drop()
```

Each split-screen half is its own camera block, so lights and shadows can differ per
view: a block that sets no lights inherits the scene's running set, while a block that
sets its own lights (an indoor and an outdoor world, day and night sides) starts from
the frame defaults and renders its own shadow map. See
[Buffers & Commands → Light scoping](graphics3d/buffer-and-commands.html#light-scoping-across-camera-blocks)
and [3D Lighting → Lights across camera blocks](graphics3d/lighting.html#lights-across-camera-blocks).

### Mouse picking

Cast a ray from a screen position into the 3D scene with `Camera3D.screenPointToRay` (both backends):

```fsharp
// raylib — returns the native Raylib_cs.Ray (note the & on the camera)
let ray = Camera3D.screenPointToRay &camera mousePos
// ray.Position  — origin point
// ray.Direction — normalized direction into the scene

// MonoGame — takes the Camera3D and viewport size, returns Mibo's Ray
let ray = Camera3D.screenPointToRay camera mousePos viewportWidth viewportHeight
```

---

See also: [2D Rendering Overview](graphics2d/overview.html), [3D Rendering](graphics3d/overview.html), [Lighting & Shadows](graphics2d/lighting.html)
