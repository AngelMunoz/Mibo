---
title: 3D Buffer & Commands
category: 3D Rendering
categoryindex: 11
index: 2
---

# 3D Buffer & Commands

Your view function receives a `RenderBuffer3D` each frame and populates it with drawing commands via the fluent Draw DSL. The renderer dispatches them in order.

## What and Why

The buffer is a command list. You don't draw to the screen directly; you describe what to draw, and the renderer handles batching, state management, and submission to the backend. This keeps your view function pure and testable.

## When to use

Every 3D game needs this. Your `view` function writes to `RenderBuffer3D`. The framework calls it once per frame.

## The buffer lifecycle

```fsharp
// Your view function signature: three inputs (context, model, buffer),
// and it does its work by adding commands to the buffer
val view : GameContext -> 'Model -> RenderBuffer3D -> unit
```

The buffer is **pre-cleared** each frame. Add commands:

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

One member set covers both backends; the buffer takes your backend's own mesh (`Mesh` / `PrimitiveMesh`), model, material, and transform types:

| Member | What it draws |
|--------|---------------|
| `.mesh(mesh, transform, material)` | Single primitive mesh (deprecated on MonoGame: see [Slices of shared buffers](#Slices-of-shared-buffers-MonoGame)) |
| `.meshSlice(mesh, transform, material, ?vertexOffset, ?startIndex)` | Mesh or mesh slice: **MonoGame**; offsets address a part of a shared buffer |
| `.model(model, transform)` | A loaded model with authored materials |
| `.modelWith(model, transform, material)` | Model with whole-model material override |
| `.modelWithPerMesh(model, transform, resolver)` | Model with per-mesh-part material override |
| `.animatedModel(animModel, transform)` | Skeletal animation: bone palette derived for you |
| `.animatedModelWith(...)` / `.animatedModelWithPerMesh(...)` | Animated model + material override |
| `.skinnedMesh(mesh, transform, material, bones)` | Explicit bone palette (**raylib only**) |
| `.instanced(mesh, transforms, material, count, ?colors)` | Many copies of one mesh in one draw call; optional per-instance `colors` tint (**MonoGame only**; deprecated there: see below) |
| `.instancedSlice(mesh, transforms, material, count, ?colors, ?vertexOffset, ?startIndex)` | Instanced draw of a mesh or mesh slice: **MonoGame** |
| `.billboard(tex, position, size, color, ?rotation, ?sourceRect, ?blend)` | Camera-facing quad; optional rotation (degrees around view axis), atlas sub-rect, blend mode |
| `.billboardBatch(textures, positions, sizes, colors, count, ?rotations, ?sourceRects, ?blend)` | Batched billboards; optional per-item arrays (null or short = defaults for those items) |
| `.line3D(start, finish, color)` | Debug line |

> _**TIP**_: Use the instanced/batched variants when drawing many copies of the same thing. One draw call is faster than many.

Billboard details:

- `sourceRect` is a pixel-space sub-rect of the texture (atlas/flipbook frame); an all-zero rect means the full texture.
- Blended billboards draw in buffer order with **no depth sorting**: non-`Opaque` modes test depth but don't write it, `Opaque` uses full depth. (`Opaque` exists only on the MonoGame `BlendMode` <abbr title="discriminated union: one type with a fixed set of cases">DU</abbr>; raylib's `Raylib_cs.BlendMode` has no opaque member, so **every** raylib billboard blends and writes no depth.)
- On MonoGame, a billboard batch draws every item with `textures[0]` (use an atlas plus `sourceRects`); raylib honors per-item textures.

### Slices of shared buffers (MonoGame)

The MonoGame content pipeline can build many models into **one shared vertex/index buffer pair**; each `ModelMeshPart` is then a *slice* of that buffer, addressed by the part's first vertex (`baseVertex`) and first index. `.mesh(...)`/`.instanced(...)` draw from offset 0; for a mesh wrapping a shared-buffer part, that renders the **first part's** triangles. Use the slice members for those meshes:

```fsharp
buffer.meshSlice(partMesh, transform, material, vertexOffset = baseVertex, startIndex = startIndex)
buffer.instancedSlice(partMesh, transforms, material, count, vertexOffset = baseVertex, startIndex = startIndex)
```

- The offsets default to `0`: self-contained buffers (procedural primitives, raylib meshes) call the slice members unchanged. This is why `Draw.mesh`/`Draw.instanced` are deprecated on MonoGame; raylib meshes are self-contained and keep them.
- The mesh record must describe the **part**, not the whole shared buffer: `PrimitiveCount` is the part's triangle count (the draw is sized by it) and `Bounds` is the part's local-space bounding sphere (the shadow pass frustum-culls by it). Both are read from the record, never from the shared buffer.
- Shared buffers with more than 65,536 vertices need a 32-bit index buffer; `PrimitiveMesh.Indices` holds either element size, and the merged-parts pipeline widens automatically. Only a hand-built shared buffer requires you to pick the element size yourself. (The procedural `Primitive3D` meshes are 16-bit by construction; they are small, so the limit never applies to them.)

Building the part-describing records by hand is the tedious part; `ModelParts.ofModel(model)` does it for you. It resolves every mesh part of a content `Model` into a `ModelPart`: a zero-copy wrap of the model's shared buffers (with the part's `PrimitiveCount` and the mesh's bounding sphere, both already in the part's bone-local space), the part's `VertexOffset`/`StartIndex`, the part's absolute parent-bone transform, and a `Material3D` read from the part's baked effect. Results are cached per model instance, so calling it every frame is a dictionary hit:

```fsharp
let parts = ModelParts.ofModel(model)

for part in parts do
    buffer.meshSlice(part.Mesh, transform, part.Material,
                     vertexOffset = part.VertexOffset, startIndex = part.StartIndex)
```

Content vertices are stored **bone-local**, so fold `part.Bone` in front of every world/instance transform (stock `ModelMesh.Draw` does this internally). `part.Bone` is `Matrix.Identity` for models without bones. Three things to keep in mind:

- Treat the returned array as **read-only**: it is the cached result shared by every caller, and mutating an element corrupts it for the model's lifetime. Copy it (`Array.map`) when you need adjusted parts.
- `ModelParts` is for **static** models only: the instanced path carries no bone palette, so skinned parts render in their <abbr title="the neutral pose a model's skeleton starts in; skinning deforms vertices away from it">bind pose</abbr>.
- For skinned models use `animatedModelInstanced` instead; see [GPU Instancing](instancing.html#Instancing-content-pipeline-models-MonoGame) for the instanced form and the grid-context shortcut.

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

`.drawImmediate(...)` flushes the batch, runs raw backend calls (rlgl/raylib, or MonoGame device access via `SceneContext`), and restores state. On MonoGame, also see `.beginEffect(...)`/`.endEffect()` (custom shading scope that inherits scene data). See [Overview](overview.html#Escape-hatches).

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

Add lights before geometry. Within a camera block, lights affect all subsequent draws in that block:

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

### Light scoping across camera blocks

In a single-camera buffer, lights are frame-global: every light command applies to every draw. In a buffer with more than one camera block, lights are scoped **per camera block**:

- **Frame defaults**: light commands emitted outside any camera block (before the first one, or between two) accumulate into the frame defaults.
- **Reset**: a block that issues its own light commands starts from the frame defaults, then applies its own commands in order (a later ambient overwrites the earlier one; directional, point, and spot lights append).
- **Inherit**: a block that issues no light commands inherits the running set: the previous block's lights plus any light commands emitted between the two blocks.
- **After the last block**: light commands emitted after the final `.endCamera()` affect nothing.

Light state is tracked per light type, and a block can only add to the set it inherits; it cannot remove an inherited light. Shadows follow the same scoping: `.setShadowOrigin(...)` applies only to the block it appears in, and each block with shadow-casting lights renders its own shadow map.

## See also

- [Draw DSL](../draw-dsl.html): the full fluent draw surface (2D and 3D)
- [Overview](overview.html): Architecture and pipeline setup
- [Lighting](lighting.html): Light types and configuration
- [Materials](materials.html): PBR material system
- [Instancing](instancing.html): GPU instanced rendering
