---
title: 2D Rendering Overview
category: v1
categoryindex: 200
index: 10
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# 2D Rendering

The 2D rendering pipeline is a **deferred command system**: each frame, your view function populates a `RenderBuffer2D` with `Command2D` values, and the `Renderer2D<'Model>` sorts them by layer and executes them in order.

## What and Why

A deferred renderer means you describe *what to draw* without worrying about *when to draw it*. The renderer handles:

- **Layer ordering** — Commands are sorted by `int<RenderLayer>` so backgrounds draw before foregrounds.
- **Camera transforms** — `Draw.beginCamera` / `Draw.endCamera` bracket world-space content.
- **Shader modes** — `Draw.beginShader` / `Draw.endShader` enable per-section effects.
- **Post-processing** — Screen-space shader passes applied after the scene renders.
- **GPU batching** — raylib auto-batches standard draw calls; the renderer never interferes.

This is especially useful for:

- **2D lighting** — Light commands and sprites are interleaved in the same buffer and processed together.
- **UI overlays** — Draw UI on a higher layer (and optionally a separate camera) above your game world.
- **Debug visualization** — Toggle debug shapes on/off by adding or removing commands.
- **Portability** — The same view function works with any renderer configuration.

## When to use deferred vs immediate

| Situation | Approach |
|-----------|----------|
| Sprites, text, shapes, tiles | Use `Draw.*` commands (deferred) |
| Custom rlgl meshes, instancing | Use `Draw.drawImmediate` (escape hatch) |
| One-off GPU operations | Prefer deferred; use immediate only when raylib lacks the API |

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

## Command API layers

Two ways to add commands to the buffer:

| Layer | When to use |
|-------|-------------|
| `Draw.*` DSL | Everyday use — pipe-friendly, supports partial application |
| `Command2D.*` factories | When you need to store or reuse commands without a buffer |

## Lighting

The 2D lighting system (`Mibo.Elmish.Graphics2D.Lighting`) provides point lights, directional lights, ambient light, and SDF soft shadows — all GPU-driven with no extra render passes.

```fsharp
buffer
|> LightDraw.setAmbient lightingCtx (5<RenderLayer>, { Color = gray })
|> LightDraw.addDirectionalLight lightingCtx 6<RenderLayer> { Direction = sunDir; ... }
|> LightDraw.addPointLight lightingCtx 7<RenderLayer> { Position = torchPos; ... }
|> LightDraw.litSprite lightingCtx playerSprite
|> LightDraw.endLighting lightingCtx 999<RenderLayer>
```

See [Lighting & Shadows](lighting.html) for details.

## Multi-camera rendering

Use `Camera2DConfig` for viewport-based rendering, split-screen, or overlay cameras:

```fsharp
// Split-screen left/right
let left = Camera2D.splitScreenLeft cam1 Color.CornflowerBlue
let right = Camera2D.splitScreenRight cam2 Color.DarkGreen

buffer
|> Draw.beginCameraWith 0<RenderLayer> left
|> // ... left viewport ...
|> Draw.endCamera 100<RenderLayer>
|> Draw.beginCameraWith 200<RenderLayer> right
|> // ... right viewport ...
|> Draw.endCamera 300<RenderLayer>
```

`Camera2DConfig` controls viewport (normalized 0–1 coordinates) and clear color. See [Camera](../camera.html) for the full API.

## Next steps

- [Buffer & Commands](buffer-and-commands.html) — How to build every type of draw command
- [Lighting & Shadows](lighting.html) — Point, directional, ambient lights + SDF shadows
- [Particles](particles.html) — Batched textured quads
- [Custom Commands](custom-commands.html) — Custom rendering with DrawImmediate
- [Performance](performance.html) — Writing efficient rendering code
- [Camera](../camera.html) — Cameras and coordinate transforms
