---
title: Rendering in Mibo
category: Rendering
index: 3
---

# 2D and 3D Rendering

Mibo provides efficient, deferred rendering systems for both 2D and 3D graphics.

## The RenderBuffer Pattern

Unlike standard MonoGame where you might call `SpriteBatch.Draw` inside your update or draw loop, Mibo uses a **Deferred Rendering** pattern:

1. Your `view` function submits drawing commands to a `RenderBuffer`.
2. Commands are stored and sorted (e.g., by layer or depth).
3. The `BatchRenderer` executes these commands in batches to minimize draw calls.

## 2D Rendering

Use the `Batch2DRenderer` and `Draw2D` module for high-performance sprite batching.

```fsharp
let view ctx model buffer =
    Draw2D.sprite texture model.Pos
    |> Draw2D.withColor Color.White
    |> Draw2D.submit buffer
```

### Features

- **Layer Sorting**: Automatically sorts sprites by `int<RenderLayer>`.
- **Camera Support**: Easily apply 2D camera transforms.
- **Fluent API**: Build complex draw commands with a readable builder pattern.

## 3D Rendering

Mibo's 3D system handles complex scenes with multiple materials, skinned models, and batching.

```fsharp
let view ctx model buffer =
    Draw3D.mesh myModel worldMatrix
    |> Draw3D.withColor Color.Green
    |> Draw3D.submit buffer
```

### Features

- **Opaque/Transparent Passes**: Handles transparency sorting automatically.
- **Skinned Animation**: Built-in support for MonoGame's `SkinnedEffect`.
- **Custom Effects**: Easily apply custom shaders to specific draw commands.
- **Primitive Batching**: High-performance batchers for Quads and Billboards.

## Camera System

Mibo provides a unified `Camera` type used by both 2D and 3D renderers.

- `Camera2D`: Helpers for orthographic views, zoom, and world-to-screen conversion.
- `Camera3D`: Helpers for perspective views, orbiting, and raycasting for picking.
