---
title: Rendering 2D
category: Rendering
index: 31
---

# Rendering 2D

2D rendering in Mibo lives in `Mibo.Elmish.Graphics2D`.

The core building blocks are:

- `RenderBuffer<int<RenderLayer>, RenderCmd2D>` (submission + sorting)
- `Batch2DRenderer` (executes commands with `SpriteBatch`)
- `Draw2D` (small fluent builder for common sprite draws)

## Minimal 2D renderer

```fsharp
open Mibo.Elmish.Graphics2D

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  Draw2D.sprite model.PlayerTex model.PlayerRect
  |> Draw2D.atLayer 10<RenderLayer>
  |> Draw2D.submit buffer

let program =
  Program.mkProgram init update
  |> Program.withRenderer (Batch2DRenderer.create view)
```

## Render layers

`RenderLayer` is an `int` unit-of-measure.

- lower layers draw first
- higher layers draw last

If you enable sorting (default), the renderer sorts the buffer by layer each frame.

## Cameras

The 2D renderer responds to `SetCamera` commands.

```fsharp
let cam = Camera2D.create model.CamPos model.Zoom viewportSize

Draw2D.camera cam 0<RenderLayer> buffer
```

### Multi-camera in 2D

You can switch cameras multiple times in a single frame by emitting multiple camera/view commands and grouping your draws under each camera.

The built-in 2D renderer supports the same multi-camera “amenities” as 3D:

- `SetViewport` (split-screen / minimaps)
- `ClearTarget` between cameras

Example (two viewports, two cameras):

```fsharp
let leftVp = Viewport(0, 0, viewportSize.X / 2, viewportSize.Y)
let rightVp = Viewport(viewportSize.X / 2, 0, viewportSize.X / 2, viewportSize.Y)

Draw2D.viewport leftVp 0<RenderLayer> buffer
Draw2D.clear (ValueSome Color.CornflowerBlue) false 1<RenderLayer> buffer
Draw2D.camera leftCam 2<RenderLayer> buffer
// left-side sprites...

Draw2D.viewport rightVp 10<RenderLayer> buffer
Draw2D.clear (ValueSome Color.DarkSlateGray) false 11<RenderLayer> buffer
Draw2D.camera rightCam 12<RenderLayer> buffer
// right-side sprites...
```

Note on ordering: if `Batch2DConfig.SortCommands = true` (the default), only _layer_ ordering is guaranteed (commands within the same layer may reorder). For stateful sequences (viewport/clear/camera/effect), either:

- put each state change on its own layer (as above), or
- set `SortCommands = false` and rely on submission order.

## Configuration

If you need custom SpriteBatch settings (blend, sampler, clear color, etc):

```fsharp
Batch2DRenderer.createWithConfig
  { Batch2DConfig.defaults with
      ClearColor = ValueSome Color.Black
      SamplerState = SamplerState.PointClamp }
  view
```

You can also change key SpriteBatch state _within_ a frame via commands:

- `Draw2D.effect` (per-segment effect; `ValueNone` restores default)
- `Draw2D.blendState`
- `Draw2D.samplerState`
- `Draw2D.depthStencilState`
- `Draw2D.rasterizerState`

## Custom GPU work (escape hatch)

The built-in 2D SpriteBatch renderer exposes `DrawCustom` for “do whatever you want” rendering.

```fsharp
Draw2D.custom
  (fun ctx ->
     // Raw GPU work (primitives, render targets, post-effects, etc.)
     // Note: SpriteBatch is ended before this runs, and restarted after.
     ())
  50<RenderLayer>
  buffer
```

If you need a fully bespoke pipeline, you can still implement your own `IRenderer<'Model>` and add it with `Program.withRenderer`.

See also: [Rendering overview](rendering.html) (overall renderer composition) and [Camera](camera.html).
