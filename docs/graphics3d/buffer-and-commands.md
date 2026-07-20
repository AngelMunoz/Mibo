---
title: 3D Buffer & Commands
category: 3D Rendering
categoryindex: 5
index: 21
---

# 3D Buffer & Commands

Your view function receives a `RenderBuffer3D` each frame and populates it with drawing commands via the fluent Draw DSL. The renderer dispatches them in order.

## What and Why

The buffer is a command list. You don't draw to the screen directly — you describe what to draw, and the renderer handles batching, state management, and submission to the backend. This keeps your view function pure and testable.

## When to use

Every 3D game needs this. Your `view` function writes to `RenderBuffer3D`. The framework calls it once per frame.

## The buffer lifecycle

```fsharp
val view : GameContext -> 'Model -> RenderBuffer3D -> unit
```

The buffer is **pre-cleared** each frame. Just add commands:

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
    buffer
      .beginCamera(camera)
      .model(model.PlayerModel, model.PlayerTransform)
      .endCamera()
      .drop()
```

`.drop()` at the end silences the unused-value warning. It does nothing.

## Pipeline pattern

Every 3D view follows the same structure:

```fsharp
buffer
  .beginCamera(camera)       // start camera transform
  .setAmbientLight ...       // lighting setup
  .addDirectionalLight ...
  .model ...                 // geometry
  .endCamera()               // end camera transform
  .drop()                    // terminal
```

> _**IMPORTANT**_: Geometry drawn outside `.beginCamera(...)` / `.endCamera()` renders in screen space. This is rarely what you want.

## Geometry commands

One member set covers both backends — the buffer takes your backend's own mesh (`Mesh` / `PrimitiveMesh`), model, material, and transform types:

| Member | What it draws |
|--------|---------------|
| `.mesh(mesh, transform, material)` | Single primitive mesh |
| `.model(model, transform)` | A loaded model with authored materials |
| `.modelWith(model, transform, material)` | Model with whole-model material override |
| `.modelWithPerMesh(model, transform, resolver)` | Model with per-mesh-part material override |
| `.animatedModel(animModel, transform)` | Skeletal animation — bone palette derived for you |
| `.animatedModelWith(...)` / `.animatedModelWithPerMesh(...)` | Animated model + material override |
| `.skinnedMesh(mesh, transform, material, bones)` | Explicit bone palette (**raylib only**) |
| `.instanced(mesh, transforms, material, count)` | Many copies of one mesh in one draw call |
| `.billboard(tex, position, size, color)` | Camera-facing quad |
| `.billboardBatch(...)` | Batched billboards |
| `.line3D(start, finish, color)` | Debug line |

> _**TIP**_: Use the instanced/batched variants when drawing many copies of the same thing. One draw call is faster than many.

## Camera commands

| Member | Description |
|--------|-------------|
| `.beginCamera(camera)` | Start 3D camera transform |
| `.beginCameraWith(config)` | Start camera with explicit viewport/clear/post-process |
| `.endCamera()` | End camera transform |

## Lighting commands

| Member | Description |
|--------|-------------|
| `.setAmbientLight(light)` | Set scene ambient light |
| `.addDirectionalLight(light)` | Add a directional light |
| `.addPointLight(light)` | Add a point light |
| `.addSpotLight(light)` | Add a spot light |

## Shadow commands

| Member | Description |
|--------|-------------|
| `.setShadowOrigin(origin)` | Set shadow map origin for this frame |
| `.enableShadows()` | Enable shadow casting for subsequent geometry |
| `.disableShadows()` | Disable shadow casting for subsequent geometry |

## Escape hatches

`.drawImmediate(...)` flushes the batch, runs raw backend calls (rlgl/raylib, or MonoGame device access via `SceneContext`), and restores state. On MonoGame, also see `.beginEffect(...)`/`.endEffect()` (custom shading scope that inherits scene data). See [Overview](overview.html#escape-hatches).

## Camera config

Use `.beginCameraWith(...)` when you need viewport control, clear color, or post-process pass selection:

```fsharp
buffer
  .beginCameraWith(Camera3D.render camera |> Camera3D.withClear Color.SkyBlue)
  .model(model, transform)
  .endCamera()
  .drop()
```

`Camera3DConfig` fields:

| Field | Type | Description |
|-------|------|-------------|
| `Camera` | `Camera3D` | The 3D camera (backend struct; same field shape on both) |
| `Viewport` | `Rectangle voption` | raylib: normalized screen coords (0-1); MonoGame: pixel coords. `ValueNone` = fullscreen |
| `ClearColor` | `Color voption` | `ValueSome color` to clear, `ValueNone` to skip |

## Lighting setup

Add lights before geometry. Lights affect all subsequent draws:

```fsharp
buffer
  .beginCamera(camera)
  .setAmbientLight { Color = Color.White; Intensity = 0.3f }
  .addDirectionalLight {
    Direction = Vector3(-1f, -1f, -1f)
    Color = Color.White
    Intensity = 0.8f
    CastsShadows = true
  }
  .addPointLight {
    Position = Vector3(5f, 3f, 0f)
    Color = Color.Yellow
    Intensity = 1f
    Radius = 10f
    CastsShadows = false
    ShadowBias = ValueNone
  }
  .model(model, transform)
  .endCamera()
  .drop()
```

> _**TIP**_: You can call `.addPointLight(...)` in a loop for dynamic lights.

## See also

- [Draw DSL](../draw-dsl.html) — the full fluent draw surface (2D and 3D)
- [Overview](overview.html) — Architecture and pipeline setup
- [Lighting](lighting.html) — Light types and configuration
- [Materials](materials.html) — PBR material system
- [Instancing](instancing.html) — GPU instanced rendering
