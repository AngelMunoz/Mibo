---
title: 3D Rendering Overview
category: 3D Rendering
categoryindex: 5
index: 12
---

# 3D Rendering

The 3D rendering pipeline is a **deferred command system** with a pluggable `IRenderPipeline3D`. Each frame, your view function populates a `RenderBuffer3D` with `Command3D` values, and the pipeline executes them. The architecture is identical on both backends; only the pipeline class name, the shader language, and some geometry-command names differ (see below).

## What and Why

The 3D renderer provides:

- **Deferred commands** — Describe what to draw without worrying about GPU state. The pipeline handles shader binding, pass order, and lighting.
- **Pluggable pipelines** — Swap the rendering pipeline without changing view code. Each backend ships a built-in forward pipeline with Cook-Torrance **PBR** materials, a shadow **atlas**, and post-processing:
  - **raylib:** `ForwardPbrPipeline` (GLSL shaders)
  - **MonoGame:** `ForwardPipeline` (HLSL `.fx` → `.mgfx`, compiled for DirectX 11 and OpenGL)
- **3D lighting** — Ambient, directional, point, and spot lights with shadow mapping.
- **Instanced rendering** — One draw call for many copies of the same geometry (`drawMeshInstanced` on raylib; `drawInstanced` on MonoGame), plus batched billboards.
- **Custom shading opt-in** — raylib via `Draw3D.drawImmediate` (raw rlgl/raylib); MonoGame via `Draw3D.beginEffect`/`endEffect` (a user `Effect` that inherits the gathered scene data) or `Draw3D.drawMeshEffect`.
- **Camera configs** — `Camera3DConfig` with viewport, clear color, and post-process control.

## Quick start

````fsharp
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

// raylib backend:                    // MonoGame backend:
let pipeline = ForwardPbrPipeline()   // let pipeline = ForwardPipeline()

Program.mkProgram init update
|> Program.withRenderer (fun () -> Renderer3D.create pipeline view)
````

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

The lighting/camera/shadow commands are identical across backends. The **geometry** commands share most names but differ where the underlying mesh type differs:

| Command | raylib | MonoGame |
|---------|--------|----------|
| Draw a loaded model | `drawModel model transform` | `drawModel model transform` |
| Single primitive mesh | `drawMesh mesh transform material` (`Mesh`) | `drawPrimitive mesh transform material` (`PrimitiveMesh`) |
| Instanced mesh | `drawMeshInstanced mesh transforms material count` | `drawInstanced mesh transforms material count` |
| Skeletal/animated | `drawSkinnedMesh mesh transform material bones` | `drawAnimatedModel animatedModel transform` (bones derived internally) |
| Billboard | `drawBillboard texture position size color` | `drawBillboard texture position size color` |
| Batched billboards | `drawBillboardBatch ...` | `drawBillboardBatch ...` |
| Debug line | `drawLine3D start finish color` | `drawLine3D start finish color` |

> _**NOTE**_: On raylib the transform is `System.Numerics.Matrix4x4`; on MonoGame it is
> `Microsoft.Xna.Framework.Matrix`. The `Draw3D.*` DSL takes whichever your backend uses; the
> Core layout geometry converts at the boundary.

## Lighting

3D lighting supports four light types, identical across backends (the structs live in
`Mibo.Elmish.Graphics3D`). Add them before geometry inside a camera scope:

```fsharp
buffer
|> Draw3D.setAmbientLight { Color = Color(30, 30, 30); Intensity = 1f }
|> Draw3D.addDirectionalLight {
    Direction = Vector3(0f, -1f, 0f)
    Color = Color.White; Intensity = 0.8f
    CastsShadows = true
}
|> Draw3D.addPointLight {
    Position = Vector3(5f, 3f, 0f)
    Color = Color.Orange; Intensity = 1f
    Radius = 10f; Falloff = 2f
    CastsShadows = false; ShadowBias = ValueNone
}
|> Draw3D.addSpotLight {
    Position = Vector3(0f, 5f, 0f)
    Direction = Vector3(0f, -1f, 0f)
    Color = Color.White; Intensity = 1f
    Radius = 15f; InnerCutoff = 0.5f; OuterCutoff = 0.7f
    CastsShadows = true; ShadowBias = ValueNone
}
```

See [Lighting](lighting.html) for the light-type fields and shadow configuration.

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

> _**NOTE — viewport coordinates differ by backend.**_ On raylib, `Camera3DConfig.Viewport`
> is in **normalized** screen coordinates (0–1, as above). On MonoGame it is a **pixel**
> `Rectangle` (matching `GraphicsDevice.Viewport`). The `Camera3D.overlay`/`splitScreen*`
> helpers produce backend-appropriate rectangles.

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

Each backend exposes a way to run custom GPU work outside the deferred command buffer:

**raylib** — `drawImmediate` runs raw rlgl/raylib calls (the batch is flushed and state restored):

```fsharp
buffer
|> Draw3D.drawImmediate (fun () ->
    Raylib.DrawCube(Vector3.Zero, 1f, 1f, 1f, Color.Red))
```

**MonoGame** — two options:
- `beginEffect` / `endEffect` open a **shading scope**: draws inside are shaded by a user
  `Effect` that *inherits* the gathered scene data (camera matrices, lights, the shadow pass
  output, material, bones, frame time) — you only declare the uniforms your effect consumes
  (e.g. `dirLightDir`, `boneMatrices`, `shadowViewProjs`, `time`). Ideal for toon/cel/wireframe
  without re-implementing the scene gather. The scope closes at `endEffect` or the next `endCamera`.

  ```fsharp
  buffer
  |> Draw3D.beginCamera camera
  |> Draw3D.beginEffect toonEffect
  |> Draw3D.drawModel model transform
  |> Draw3D.endEffect
  |> Draw3D.endCamera
  ```

- `drawMeshEffect meshPart transform effect` is a fully user-owned effect (the pipeline only
  sets World/View/Projection); the caller owns all lighting/material parameters.
- `drawImmediate` (callback receives a `SceneContext` with the graphics device + gathered scene
  data) for raw device access.

See [Shaders](../shaders.html) for loading custom shaders/effects per backend, and the
[Shader Uniform Reference](../shader-uniforms.html) for the exact uniform names the
`beginEffect` scope uploads (declare only what your shader consumes).

## See also

- [Camera](../camera.html) — Camera3D helpers, Camera3DConfig, multi-camera patterns
- [Shaders](../shaders.html) — Custom shader loading and parameters
- [Rendering Overview](../rendering.html) — 2D + 3D pipeline architecture
