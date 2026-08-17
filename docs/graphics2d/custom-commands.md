---
title: Custom Commands & Escape Hatches
category: 2D Rendering
categoryindex: 10
index: 3
---

# Custom Commands & Escape Hatches

The 2D rendering system is built on a discriminated union (`Command2D`: one type with a fixed set of cases). The fluent Draw DSL covers standard shapes, sprites, text, and render state. When you need to go outside those primitives, `.drawImmediate(...)` is the escape hatch.

## What and Why

`DrawImmediate` lets you run any rendering code inside the command pipeline. The renderer flushes the backend's internal batch, temporarily exits any active camera and shader modes, runs your action, then restores the previous state. Your code executes outside the batch: direct backend calls (raylib rlgl, its raw graphics layer, or MonoGame device access), custom GPU operations.

You give up batching. You gain full control.

## When to use

Use `.drawImmediate(...)` when:

- You need direct `Rlgl.*` calls (custom vertices, instancing, compute dispatches).
- You're integrating a third-party renderer that writes to the GL context directly.
- The fluent DSL can't express what you need.

Otherwise, use the fluent members. They batch automatically and are faster.

## DrawImmediate

There are two ways to create a `DrawImmediate` command.

### Via the fluent DSL

```fsharp
let drawRedQuad () =
    Rlgl.Begin(DrawMode.Quads)
    Rlgl.Color4f(1f, 0f, 0f, 1f)
    Rlgl.Vertex2f(0f, 0f)
    Rlgl.Vertex2f(100f, 0f)
    Rlgl.Vertex2f(100f, 100f)
    Rlgl.Vertex2f(0f, 100f)
    Rlgl.End()

buffer
  .drawImmediate(drawRedQuad)
  .drop()
```

### As a Command2D factory

```fsharp
let drawGreenQuad () =
    Rlgl.Begin(DrawMode.Quads)
    Rlgl.Color4f(0f, 1f, 0f, 1f)
    Rlgl.Vertex2f(0f, 0f)
    Rlgl.Vertex2f(50f, 0f)
    Rlgl.Vertex2f(50f, 50f)
    Rlgl.Vertex2f(0f, 50f)
    Rlgl.End()

let cmd = Command2D.drawImmediate 0<RenderLayer> drawGreenQuad

buffer.Add(cmd)
```

> On MonoGame the same escape hatch runs raw device calls through `SceneContext`; the `Rlgl.*` examples here are raylib-specific.

### What happens internally

When the renderer encounters a `Command2D.DrawImmediate` case:

1. Pending geometry is flushed.
2. Active shader mode is ended (if any).
3. Active camera mode is ended (if any).
4. Your action runs.
5. Previous camera and shader modes are restored.

This is implemented in the backend's `Renderer2D.fs` at the `drawImmediate` helper. The `try`/`finally` block guarantees state restoration even if your action throws.

### Example: custom textured quad with rlgl

Semicolons let you put two calls on one line; each `TexCoord2f`/`Vertex2f` pair sends a texture coordinate and its corner:

```fsharp
let drawTexturedQuad (texture: Texture2D) (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    let drawQuad () =
        Rlgl.SetTexture(int texture.Id)
        Rlgl.Begin(DrawMode.Quads)
        Rlgl.Color4ub(255uy, 255uy, 255uy, 255uy)
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex2f(0f, 0f)
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex2f(200f, 0f)
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex2f(200f, 200f)
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex2f(0f, 200f)
        Rlgl.End()
        Rlgl.SetTexture(0u)

    buffer
      .drawImmediate(drawQuad, layer = layer)
      .drop()
```

> _**IMPORTANT**_: Each `DrawImmediate` call forces a batch flush before and after. If you call it in a loop (e.g., once per entity), you pay the flush cost every time. Batch your custom work into a single `DrawImmediate` call where possible.

> _**TIP**_: Use `.drop()` at the end of your view function to discard the buffer reference and silence unused-value warnings.

## See also

- [Buffer & Commands](buffer-and-commands.html): the fluent DSL and command reference.
- [Overview](overview.html): 2D rendering pipeline architecture.
