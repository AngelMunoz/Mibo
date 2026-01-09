---
title: Rendering 3D
category: Rendering
index: 12
---

# Rendering 3D

3D rendering in Mibo lives in `Mibo.Elmish.Graphics3D`.

The core building blocks are:

- `RenderBuffer<unit, RenderCmd3D>` (submission order is preserved)
- `Batch3DRenderer` (executes commands and manages opaque/transparent passes)

## Minimal 3D renderer

```fsharp
open Mibo.Elmish.Graphics3D

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd3D>) =
  buffer.Add((), SetCamera model.Camera)
  buffer.Add((), DrawMesh(Opaque, model.Level, Matrix.Identity, ValueNone, ValueNone, ValueNone))

let program =
  Program.mkProgram init update
  |> Program.withRenderer (Batch3DRenderer.create view)
```

## Passes: opaque vs transparent

Most commands specify a `RenderPass`:

- `Opaque`: depth write on, opaque blending
- `Transparent`: depth read + alpha blending (sorted back-to-front)

The renderer partitions the command stream into opaque/transparent lists and sorts the transparent list correctly.

## Camera and multi-camera rendering

`RenderCmd3D` includes:

- `SetViewport` for split screen / minimaps
- `ClearTarget` to clear between cameras
- `SetCamera` for subsequent draws

Because submission order is preserved, you can do multi-camera rendering by emitting:

1. `SetViewport`
2. `ClearTarget`
3. `SetCamera`
4. draw commands

…and repeating.

Example (world + minimap):

```fsharp
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd3D>) =
  // Main camera (full screen)
  Draw3D.viewport ctx.GraphicsDevice.Viewport buffer
  Draw3D.clear (ValueSome Color.CornflowerBlue) true buffer
  Draw3D.camera model.MainCamera buffer
  Draw3D.mesh model.Level Matrix.Identity
  |> Draw3D.withBasicEffect
  |> Draw3D.submit buffer

  // Minimap camera (top-right)
  let vp = ctx.GraphicsDevice.Viewport
  let mini = Viewport(vp.Width - 256, 0, 256, 256)
  Draw3D.viewport mini buffer
  Draw3D.clear (ValueSome Color.Black) true buffer
  Draw3D.camera model.MiniMapCamera buffer
  Draw3D.mesh model.Level Matrix.Identity
  |> Draw3D.withBasicEffect
  |> Draw3D.submit buffer
```

## Custom draws (escape hatch)

If you need custom GPU work without forking the renderer:

- `DrawCustom (GameContext * View * Projection -> unit)`

This is handy for special effects, custom vertex buffers, debug gizmos, etc.

You can also use the helper `Draw3D.custom`:

```fsharp
Draw3D.custom
  (fun (ctx, view, proj) ->
    // example: set device state, draw debug primitives, etc.
    // ctx.GraphicsDevice.DrawUserPrimitives(...)
    ())
  buffer
```

## Quads and billboards

The 3D renderer includes a built-in **Sprite3D** path for the “90% case”: unlit textured quads and billboards.

This path:

- does **not** require user-managed effects
- supports texture atlases via `UvRect`
- participates in the renderer’s opaque/transparent sorting (transparent is back-to-front)
- works naturally with multi-camera / multi-viewport rendering

### Quads (Sprite3D)

`DrawQuad` is a fast way to draw lots of simple, textured rectangles in 3D without building a full `Model`.

Typical uses:

- ground decals / markers ("target here")
- simple planes (floors/walls) for prototypes
- tile-like world geometry

Quads are represented as **center + basis half-extents** (`center`, `right`, `up`), which is both flexible and fast.

There are helpers for common planes like XZ (ground decals) and XY (in-world UI).

```fsharp
open Microsoft.Xna.Framework
open Mibo.Elmish.Graphics3D

// Draw a 2x2 ground decal centered at (10,0,5)
let q =
  Draw3D.quadOnXZ (Vector3(10f, 0f, 5f)) (Vector2(2f, 2f))
  |> Draw3D.withQuadColor (Color.White)
  |> Draw3D.withQuadUv UvRect.full

Draw3D.quad model.DecalTex q buffer
```

### Billboards (Sprite3D)

`DrawBillboard` draws a quad that always faces the camera. This is ideal for “2D sprites in 3D space”.

Typical uses:

- particles (smoke, fire, sparks)
- floating UI / markers (quest icons)
- impostors / far-distance foliage

```fsharp
open Microsoft.Xna.Framework
open Mibo.Elmish.Graphics3D

let b =
  Draw3D.billboard3D (Vector3(0f, 1.5f, 0f)) (Vector2(0.5f, 0.5f))
  |> Draw3D.withBillboardRotation 0.0f
  |> Draw3D.withBillboardColor Color.White
  |> Draw3D.withBillboardUv UvRect.full

Draw3D.billboard model.ParticleTex b buffer
```

If you want “tree style” billboards that rotate only around an up axis:

```fsharp
let tree =
  Draw3D.billboard3D pos (Vector2(2f, 4f))
  |> Draw3D.cylindrical Vector3.Up

Draw3D.billboard model.TreeTex tree buffer
```

### Using your own effect (advanced)

If you want full control over shader semantics, use the effect-driven commands:

- `Draw3D.quadEffect`
- `Draw3D.billboardEffect`

These take an `Effect` and an optional setup callback (`EffectSetup`) which receives an `EffectContext` containing `View` and `Projection`.

#### Example: quadEffect (custom shader / parameters)

```fsharp
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D

// Cache effects in your model/renderer state; do not reallocate per draw.
let be = new BasicEffect(ctx.GraphicsDevice)
be.TextureEnabled <- true
be.VertexColorEnabled <- true

let setup : EffectSetup =
  fun effect ec ->
    match effect with
    | :? BasicEffect as be ->
        be.View <- ec.View
        be.Projection <- ec.Projection
        // World is baked into the quad's vertices (center/right/up).
        // Any other parameters can be set here.
    | _ -> ()

let q =
  Draw3D.quadOnXZ (Vector3(10f, 0f, 5f)) (Vector2(2f, 2f))
  |> Draw3D.withQuadColor Color.White
  |> Draw3D.withQuadUv UvRect.full

be.Texture <- model.DecalTex
Draw3D.quadEffect Transparent (be :> Effect) (ValueSome setup) q buffer
```

#### Example: billboardEffect (custom shader / parameters)

```fsharp
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D

let be = new BasicEffect(ctx.GraphicsDevice)
be.TextureEnabled <- true
be.VertexColorEnabled <- true

let setup : EffectSetup =
  fun effect ec ->
    match effect with
    | :? BasicEffect as be ->
        be.View <- ec.View
        be.Projection <- ec.Projection
    | _ -> ()

let b =
  Draw3D.billboard3D (Vector3(0f, 1.5f, 0f)) (Vector2(0.5f, 0.5f))
  |> Draw3D.withBillboardColor Color.White
  |> Draw3D.withBillboardRotation 0.25f
  |> Draw3D.withBillboardUv UvRect.full

be.Texture <- model.ParticleTex
Draw3D.billboardEffect Transparent (be :> Effect) (ValueSome setup) b buffer
```

If you need arbitrary GPU work (render targets, post-processing, unusual state), use `DrawCustom` / `Draw3D.custom`.

See also: [Camera](camera.html) and [Culling](culling.html).
