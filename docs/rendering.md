---
title: Custom Rendering in Mibo
category: Rendering
index: 33
---

# Rendering in Mibo

Mibo’s rendering story is intentionally simple:

- Your Elmish `update` stays pure.
- Your `view` function **submits render commands** to a `RenderBuffer`.
- A renderer (`IRenderer<'Model>`) executes those commands each frame.

This keeps drawing as _data submission_, which composes nicely as projects grow.

## Renderers are just `IRenderer<'Model>`

A renderer is anything that implements:

```fsharp
type IRenderer<'Model> =
    abstract member Draw: GameContext * 'Model * GameTime -> unit
```

You add renderers with:

```fsharp
Program.withRenderer (fun game -> myRenderer :> IRenderer<Model>)
```

Multiple renderers can be added (e.g. 3D world + 2D UI). They run in the order they were added.

## The `RenderBuffer` pattern

Instead of drawing immediately inside `update`, you typically:

1. allocate a buffer (owned by the renderer)
2. clear it each frame
3. call your `view` to fill it
4. optionally sort it
5. execute the commands

The built-in renderers follow this pattern, and custom renderers can too.

## Writing your own renderer

If you have a custom draw pipeline (special post-processing, instancing, debug overlays, etc), implement `IRenderer<'Model>`.

Minimal example:

```fsharp
type MyRenderer(game: Game) =
    interface IRenderer<Model> with
        member _.Draw(ctx, model, gameTime) =
            // use ctx.GraphicsDevice, ctx.Content, etc
            // draw based on model
            ()
```

Then install it:

```fsharp
Program.mkProgram init update
|> Program.withRenderer (fun game -> MyRenderer(game) :> IRenderer<Model>)
```

## Built-in renderers

Mibo includes:

- 2D SpriteBatch renderer: `Mibo.Elmish.Graphics2D.Batch2DRenderer`
- 3D renderer with pass handling and primitive batching: `Mibo.Elmish.Graphics3D.Batch3DRenderer`

See the dedicated pages:

- [Rendering 2D](rendering2d.html)
- [Rendering 3D](rendering3d.html)
- [Camera](camera.html)
- [Culling](culling.html)
