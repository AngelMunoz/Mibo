---
title: 3D Buffer & Commands
category: 3D Rendering
categoryindex: 5
index: 21
---

# 3D Buffer & Commands

Your view function receives a `RenderBuffer3D` each frame. You populate it with drawing commands using the `Draw3D` module. The renderer dispatches them in order.

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
    |> Draw3D.beginCamera camera
    |> Draw3D.drawModel model.PlayerModel model.PlayerTransform
    |> Draw3D.endCamera
    |> Draw3D.drop
```

`Draw3D.drop` at the end silences the unused-value warning. It does nothing.

## Pipeline pattern

Every 3D view follows the same structure:

```
buffer
|> Draw3D.beginCamera camera       // start camera transform
|> Draw3D.setAmbientLight ...      // lighting setup
|> Draw3D.addDirectionalLight ...
|> Draw3D.drawModel ...            // geometry
|> Draw3D.endCamera                // end camera transform
|> Draw3D.drop                     // terminal
```

> _**IMPORTANT**_: Geometry drawn outside `beginCamera` / `endCamera` renders in screen space. This is rarely what you want.

## Geometry commands

The lighting/camera/shadow commands are identical across backends. The **geometry** commands
differ where the mesh type differs (raylib `Mesh` / MonoGame `PrimitiveMesh`):

| Function | raylib | MonoGame |
|----------|--------|----------|
| `Draw3D.drawMesh` / `drawPrimitive` | `drawMesh mesh transform material` | `drawPrimitive mesh transform material` |
| `Draw3D.drawModel` | `drawModel model transform` | `drawModel model transform` |
| `Draw3D.modelWith` / `modelWithPerMesh` | `modelWith model transform material` · `modelWithPerMesh model transform resolver` | same (overrides a model's baked material, whole or per-sub-mesh) |
| `Draw3D.drawBillboard` | `drawBillboard texture position size color` | `drawBillboard texture position size color` |
| `Draw3D.drawBillboardBatch` | batched billboards | batched billboards |
| `Draw3D.drawLine3D` | `drawLine3D start finish color` | `drawLine3D start finish color` |
| Skinned/animated | `drawSkinnedMesh mesh transform material bones` (material supplied directly) | `drawAnimatedModel animatedModel transform` · `animatedModelWith` / `animatedModelWithPerMesh` to override its material |
| Instanced | `drawMeshInstanced mesh transforms material count` | `drawInstanced mesh transforms material count` |

> _**TIP**_: Use the instanced/batched variants when drawing many copies of the same thing. One draw call is faster than many.

## Camera commands

| Function | Description |
|----------|-------------|
| `Draw3D.beginCamera camera` | Start 3D camera transform |
| `Draw3D.beginCameraWith config` | Start camera with explicit viewport/clear/post-process |
| `Draw3D.endCamera` | End camera transform |

## Lighting commands

| Function | Description |
|----------|-------------|
| `Draw3D.setAmbientLight light` | Set scene ambient light |
| `Draw3D.addDirectionalLight light` | Add a directional light |
| `Draw3D.addPointLight light` | Add a point light |
| `Draw3D.addSpotLight light` | Add a spot light |

## Shadow commands

| Function | Description |
|----------|-------------|
| `Draw3D.setShadowOrigin origin` | Set shadow map origin for this frame |
| `Draw3D.enableShadows` | Enable shadow casting for subsequent geometry |
| `Draw3D.disableShadows` | Disable shadow casting for subsequent geometry |

## Escape hatches

| Function | Description |
|----------|-------------|
| `Draw3D.drawImmediate action` | Flush batch, run raw backend calls (rlgl/raylib, or MonoGame device access via `SceneContext`), restore state |

On MonoGame, also see `Draw3D.beginEffect`/`endEffect` (custom shading scope that inherits scene data) and `Draw3D.drawMeshEffect` (fully user-owned effect). See [Overview](overview.html#escape-hatches).

## Camera config

Use `beginCameraWith` when you need viewport control, clear color, or post-process pass selection:

```fsharp
buffer
|> Draw3D.beginCameraWith(
    Camera3D.render camera |> Camera3D.withClear Color.SkyBlue
)
|> Draw3D.drawModel model transform
|> Draw3D.endCamera
|> Draw3D.drop
```

`Camera3DConfig` fields:

| Field | Type | Description |
|-------|------|-------------|
| `Camera` | `Camera3D` | The 3D camera (backend struct; same field shape on both) |
| `Viewport` | `Rectangle voption` | raylib: normalized screen coords (0-1); MonoGame: pixel coords. `ValueNone` = fullscreen |
| `ClearColor` | `Color voption` | `ValueSome color` to clear, `ValueNone` to skip |
| `PostProcessPasses` | `int[] voption` | Which post-process passes to apply |

## Lighting setup

Add lights before geometry. Lights affect all subsequent draws:

```fsharp
buffer
|> Draw3D.beginCamera camera
|> Draw3D.setAmbientLight { Color = Color.White; Intensity = 0.3f }
|> Draw3D.addDirectionalLight {
    Direction = Vector3(-1f, -1f, -1f)
    Color = Color.White
    Intensity = 0.8f
    CastsShadows = true
}
|> Draw3D.addPointLight {
    Position = Vector3(5f, 3f, 0f)
    Color = Color.Yellow
    Intensity = 1f
    Radius = 10f
    CastsShadows = false
    ShadowBias = ValueNone
}
|> Draw3D.drawModel model transform
|> Draw3D.endCamera
|> Draw3D.drop
```

> _**TIP**_: You can call `addPointLight` in a loop for dynamic lights. The sample does this for visible lights each frame.

## Two ways to build commands

| Layer | When to use |
|-------|-------------|
| `Draw3D.*` DSL | Everyday use — pipe-friendly, buffer-last for chaining |
| `Command3D.*` factories | When you need to store or reuse commands without a buffer |

```fsharp
// DSL — pipe-friendly
buffer
|> Draw3D.drawMesh mesh transform material
|> Draw3D.drawModel model transform

// Factory — store for later
let cmd = Command3D.drawMesh mesh transform material
buffer.Add(cmd)
```

## See also

- [Overview](overview.html) — Architecture and pipeline setup
- [Lighting](lighting.html) — Light types and configuration
- [Materials](materials.html) — PBR material system
- [Instancing](instancing.html) — GPU instanced rendering
