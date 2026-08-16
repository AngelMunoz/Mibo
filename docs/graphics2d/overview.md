---
title: 2D Rendering Overview
category: 2D Rendering
categoryindex: 8
index: 1
---

# 2D Rendering

The 2D rendering pipeline is a **deferred command system**: each frame, your view function populates a `RenderBuffer2D` with `Command2D` values, and the `Renderer2D<'Model>` sorts them by layer and executes them in order.

## What and Why

A deferred renderer means you describe *what to draw* without worrying about *when to draw it*. The renderer handles:

- **Layer ordering** — Commands are sorted by `int<RenderLayer>` so backgrounds draw before foregrounds.
- **Camera transforms** — `.beginCamera()` / `.endCamera()` bracket world-space content.
- **Shader modes** — `.beginShader()` / `.endShader()` enable per-section effects.
- **Post-processing** — Screen-space shader passes applied after the scene renders.
- **GPU batching** — the backend auto-batches standard draw calls; the renderer never interferes.

This is especially useful for:

- **2D lighting** — Light commands and sprites are interleaved in the same buffer and processed together.
- **UI overlays** — Draw UI on a higher layer (and optionally a separate camera) above your game world.
- **Debug visualization** — Toggle debug shapes on/off by adding or removing commands.
- **Portability** — The same view function works with any renderer configuration.

## When to use deferred vs immediate

| Situation | Approach |
|-----------|----------|
| Sprites, text, shapes, tiles | Use the fluent Draw members (deferred) |
| Custom GPU work (e.g. raw rlgl meshes, instancing) | Use `.drawImmediate(...)` (escape hatch) |
| One-off GPU operations | Prefer deferred; use immediate only when the backend lacks the API |

## How it works

```
Program.mkProgram init update
|> Program.withRenderer (fun () -> Renderer2D.create myView)
```

Each frame, the runtime calls `myView ctx model buffer`. Your view adds commands, the renderer sorts and executes:

1. `buffer.Clear()` — wipe previous frame's commands
2. `myView ctx model buffer` — populate with this frame's draw commands
3. `buffer.Sort()` — sort by layer (ascending)
4. Execute in order, managing camera/shader state transitions

## Adding commands

Everyday view code chains members of the fluent Draw DSL on the buffer — see [Draw DSL](../draw-dsl.html) for the full surface.

## Lighting

The 2D lighting system (`Mibo.Elmish.Graphics2D.Lighting`) provides point lights, directional lights, ambient light, and SDF soft shadows — all GPU-driven with no extra render passes.

```fsharp
buffer
  .setAmbient(lightingCtx, gray, layer = 5<RenderLayer>)
  .addDirectionalLight(lightingCtx, sunDir, sunColor, intensity = 1.5f, layer = 6<RenderLayer>)
  .addPointLight(lightingCtx, torchLight, layer = 7<RenderLayer>)
  .litSprite(lightingCtx, playerSprite)
  .endLighting(lightingCtx, layer = 999<RenderLayer>)
  .drop()
```

See [Lighting & Shadows](lighting.html) for details.

## Multi-camera rendering

Use `Camera2DConfig` for viewport-based rendering, split-screen, or picture-in-picture cameras:

```fsharp
// Split-screen left/right
let left = Camera2D.splitScreenLeft cam1 Color.CornflowerBlue
let right = Camera2D.splitScreenRight cam2 Color.DarkGreen

buffer
  .beginCameraWith(left)
  // ... left viewport ...
  .endCamera(layer = 100<RenderLayer>)
  .beginCameraWith(right, layer = 200<RenderLayer>)
  // ... right viewport ...
  .endCamera(layer = 300<RenderLayer>)
  .drop()
```

`Camera2DConfig` controls viewport (normalized 0–1 coordinates) and clear color. See [Camera](../camera.html) for the full API.

## Next steps

- [Draw DSL](../draw-dsl.html) — The fluent, backend-neutral draw surface (2D and 3D)
- [Buffer & Commands](buffer-and-commands.html) — How to build every type of draw command
- [Lighting & Shadows](lighting.html) — Point, directional, ambient lights + SDF shadows
- [Particles](particles.html) — Batched textured quads
- [Custom Commands](custom-commands.html) — Custom rendering with DrawImmediate
- [Performance](performance.html) — Writing efficient rendering code
- [Camera](../camera.html) — Cameras and coordinate transforms
