---
title: Layered Rendering
category: Patterns
categoryindex: 13
index: 5
---

# Layered Rendering

## What and Why

Most games need multiple visual layers: a 3D world with a 2D HUD, a minimap in the corner, debug wireframes overlaid on collision boxes. Drawing everything in one renderer creates a mess of state management. Drawing in separate renderers requires knowing how they composite.

The pattern: register multiple renderers in order. The first renderer clears and draws the base layer. Subsequent renderers draw on top without clearing. Each renderer has its own camera and command buffer.

## Use Cases

### 3D world + 2D HUD
The classic setup. A 3D renderer draws the game world. A 2D renderer draws health bars, ammo counters, and text on top.

### 3D world + minimap
Same as HUD, but the 2D renderer also draws a texture generated from world data — a top-down view of the map in a corner.

### Game world + debug overlay
3D renderer draws the game. A second renderer draws wireframe collision boxes, pathfinding lines, and AI state text. Toggle the debug layer on/off by adding or removing the renderer.

### Split-screen multiplayer
Two 3D renderers, each with a different camera and viewport. Each renders the full scene from a different perspective.

### Picture-in-picture
A security camera feed, rearview mirror, or spectator view. A small 3D renderer draws into a corner of the screen after the main 3D pass.

### Screen-space effects
Vignette, scanlines, film grain. A 2D renderer draws full-screen quads with post-processing shaders on top of the game world.

## The Technique

Register renderers in order: first registered, first drawn. The scene renderer goes first (it clears), overlays after it (they don't clear):

```fsharp
let createSceneRenderer () =
    Renderer3D.create pipeline sceneView

let createOverlayRenderer () =
    Renderer2D.createWith Renderer2DConfig.noClear overlayView

Program.mkProgram init update
|> Program.withRenderer createSceneRenderer
|> Program.withRenderer createOverlayRenderer
```

The 2D overlay is registered second, so it draws after the 3D scene, on top. The `noClear` config value prevents it from erasing the 3D scene. The example shows the Elmish program builder; the adaptive runtime registers renderers exactly the same way, with `AdaptiveProgram.withRenderer` and a draw function that reads the frame.

Each renderer has its own view function:

```fsharp
let overlayView (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  drawMinimap model buffer
  drawDiagnostics model buffer
  drawHealthBar model buffer
```

### Renderer behavior

| Renderer | Clears screen? | Draws to | Typical content |
|----------|---------------|----------|-----------------|
| `Renderer3D.create` | Yes | 3D buffer | World, models, lights, shadows |
| `Renderer2D.createWith noClear` | No | 2D buffer | HUD, minimap, text, debug |

## Key Insight

Renderer order is execution order. The 3D renderer clears the screen and draws the world. The 2D renderer draws on top without clearing. If you reversed the order, the 3D clear would erase the 2D content. The `noClear` config is the critical detail: without it, overlays don't work.

You can add more than two renderers. Each one composites on top of the previous. Make sure only the base renderer clears the screen.

## When to use

- Any game with a HUD, minimap, or debug overlay.
- Split-screen or multi-viewport games.
- Games with screen-space post-processing.
- You want to separate rendering concerns into independent view functions.

## See also

- [Platformer3D renderer setup](https://github.com/AngelMunoz/Mibo.Samples/blob/master/Platformer3D/Raylib/Program.fs) — renderer setup with 3D scene and 2D overlay.
- [Rendering Overview](../rendering.html) — renderers and render buffers.
