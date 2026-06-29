---
title: 3D Rendering Overview
category: v1
categoryindex: 200
index: 16
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# 3D Rendering

The 3D rendering pipeline is a **deferred command system** with a pluggable `IRenderPipeline3D`. Each frame, your view function populates a `RenderBuffer3D` with `Command3D` values, and the pipeline executes them.

## What and Why

The 3D renderer provides:

- **Deferred commands** — Describe what to draw without worrying about GPU state. The pipeline handles shader binding, pass order, and lighting.
- **Pluggable pipelines** — Swap the rendering pipeline without changing view code. The built-in `ForwardPbrPipeline` supports PBR materials, shadows, and post-processing.
- **3D lighting** — Ambient, directional, point, and spot lights with shadow mapping.
- **Instanced rendering** — `drawMeshInstanced` and `drawBillboardBatch` for many copies of the same geometry.
- **Camera configs** — `Camera3DConfig` with viewport, clear color, and post-process control.

## Quick start

```fsharp
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

let pipeline = ForwardPbrPipeline()

Program.mkProgram init update
|> Program.withRenderer (fun () -> Renderer3D.create pipeline view)
```

Your view function receives a `RenderBuffer3D`:

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
    buffer
    |> Draw3D.beginCamera worldCamera
    |> Draw3D.setAmbientLight { Color = Color(40, 40, 40); Intensity = 1f }
    |> Draw3D.addDirectionalLight {
        Direction = Vector3(0.3f, -0.7f, 0.2f)
        Color = Color.White
        Intensity = 0.8f
        CastsShadows = true
        ShadowBias = ValueNone
    }
    |> Draw3D.drawModel playerModel playerTransform
    |> Draw3D.endCamera
    |> Draw3D.drop
```

## Command API

Two ways to add commands to the buffer:

| Layer | When to use |
|-------|-------------|
| `Draw3D.*` DSL | Everyday use — pipe-friendly, supports partial application |
| `Command3D.*` factories | When you need to store or reuse commands without a buffer |

## Geometry commands

- `Draw3D.drawMesh mesh transform material` — Draw a single mesh
- `Draw3D.drawModel model transform` — Draw a raylib model (auto-converts materials)
- `Draw3D.drawBillboard texture position size color` — Camera-facing quad
- `Draw3D.drawLine3D start finish color` — Debug line
- `Draw3D.drawSkinnedMesh mesh transform material bones` — Skeletal animation
- `Draw3D.drawMeshInstanced mesh transforms material count` — Instanced mesh
- `Draw3D.drawBillboardBatch textures positions sizes colors count` — Batched billboards

## Lighting

3D lighting supports four light types:

```fsharp
buffer
|> Draw3D.setAmbientLight { Color = Color(30, 30, 30); Intensity = 1f }
|> Draw3D.addDirectionalLight {
    Direction = Vector3(0f, -1f, 0f)
    Color = Color.White; Intensity = 0.8f
    CastsShadows = true; ShadowBias = ValueNone
}
|> Draw3D.addPointLight {
    Position = Vector3(5f, 3f, 0f)
    Color = Color.Orange; Intensity = 1f
    Radius = 10f; CastsShadows = false; ShadowBias = ValueNone
}
|> Draw3D.addSpotLight {
    Position = Vector3(0f, 5f, 0f)
    Direction = Vector3(0f, -1f, 0f)
    Color = Color.White; Intensity = 1f
    Cutoff = 0.5f; Radius = 15f
    CastsShadows = true; ShadowBias = ValueNone
}
```

## Shadow control

Enable or disable shadow casting per-section:

```fsharp
buffer
|> Draw3D.enableShadows
|> Draw3D.drawModel groundModel groundTransform   // casts shadows
|> Draw3D.disableShadows
|> Draw3D.drawModel skyboxModel skyboxTransform   // no shadows
```

## Multi-camera rendering

Use `Camera3DConfig` for split-screen, minimaps, or layered rendering:

```fsharp
let mainConfig = Camera3D.render mainCamera |> Camera3D.withClear Color.SkyBlue
let minimapConfig = Camera3D.overlay topDownCamera (Rectangle(0.75f, 0f, 0.25f, 0.25f))

buffer
|> Draw3D.beginCameraWith mainConfig
|> // ... main scene ...
|> Draw3D.endCamera
|> Draw3D.beginCameraWith minimapConfig
|> // ... minimap ...
|> Draw3D.endCamera
```

See [Camera](../camera.html) for the full `Camera3DConfig` API.

## 2D overlay on 3D

Combine 3D and 2D renderers for HUD overlays:

```fsharp
Program.mkProgram init update
|> Program.withRenderer (fun () ->
    Renderer3D.createWith { ClearColor = ValueSome Color.Black } pipeline view3D)
|> Program.withRenderer (fun () ->
    Renderer2D.createWith { ClearColor = ValueNone } view2D)
```

The 2D renderer clears with `ValueNone` to preserve the 3D scene underneath.

## Escape hatches

For custom rlgl calls or raw raylib API usage:

```fsharp
buffer
|> Draw3D.drawImmediate (fun () ->
    Raylib.DrawCube(Vector3.Zero, 1f, 1f, 1f, Color.Red))
```

## See also

- [Camera](../camera.html) — Camera3D helpers, Camera3DConfig, multi-camera patterns
- [Shaders](../shaders.html) — Custom shader loading and parameters
- [Rendering Overview](../rendering.html) — 2D + 3D pipeline architecture
