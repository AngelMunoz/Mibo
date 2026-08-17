---
title: Rendering Overview
category: Rendering
categoryindex: 9
index: 1
---

# Rendering Overview

Mibo uses a **deferred, layer-sorted rendering pipeline**. Instead of calling backend draw functions directly, you build a list of commands each frame, and the renderer sorts and executes them in one pass. This works the same way on every backend.

## The Pipeline

1. Your view function builds `IRenderCommand2D` commands and adds them to a `RenderBuffer2D`
2. The renderer sorts commands by `Layer` (ascending)
3. The renderer executes commands in order
4. The backend auto-batches GPU draw calls; optional post-processing passes run after

## Why Layered Rendering?

- **Separation of concerns**: Your view doesn't have to worry about draw order, batching, or GPU state.
- **Lighting**: Commands can be interleaved with light commands for 2D lighting (see [Lighting](graphics2d/overview.html#Lighting)).
- **Post-processing**: Screen-space shader passes run after the scene is rendered.
- **Predictable ordering**: Every command declares its layer explicitly.

> This command-layering model is not the same as "deferred shading" (a lighting technique). The deferred part here means your view describes what to draw and the renderer executes it later, in sorted order.

## 2D Rendering

The 2D pipeline is built on `Renderer2D<'Model>` in the `Mibo.Elmish.Graphics2D` namespace. See:

- [2D Rendering Overview](graphics2d/overview.html): What, Why, When
- [Buffer & Commands](graphics2d/buffer-and-commands.html): Building and issuing draw commands
- [Lighting & Shadows](graphics2d/lighting.html): 2D lights and soft shadows
- [Particles](graphics2d/particles.html): Batched particle rendering
- [Custom Commands](graphics2d/custom-commands.html): `IRenderCommand2D` and escape hatches
- [Performance](graphics2d/performance.html): Writing performant 2D rendering code
- [Camera](camera.html): Cameras and coordinate systems
- [Culling](culling.html): Visibility testing

## 3D Rendering

The 3D pipeline is built on `Renderer3D<'Model>` in the `Mibo.Elmish.Graphics3D` namespace. It uses a pluggable `IRenderPipeline3D` that interprets `Command3D` values: draw meshes, billboards, lights, shadows, and post-processing passes.

- [3D Rendering Overview](graphics3d/overview.html): What, Why, When
- [Camera](camera.html): `Camera3DConfig`, split-screen, picture-in-picture cameras

## Multi-Renderer Compositing

You can combine 2D and 3D renderers in the same program. The 3D scene renders first, then the 2D renderer composites on top (HUD, menus, debug overlays).

```fsharp
Program.mkProgram init update
|> Program.withRenderer (fun () ->
    Renderer3D.create pipeline view3D)
|> Program.withRenderer (fun () ->
    Renderer2D.create view2D)
```

The 2D renderer uses `Renderer2DConfig.noClear` to skip clearing the background, preserving the 3D scene underneath. Per-camera clear control is also available via `Camera3DConfig` and `Camera2DConfig`.

See also: [Camera](camera.html) for multi-camera patterns.
