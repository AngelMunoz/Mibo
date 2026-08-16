---
title: Custom Commands & Escape Hatches
category: 2D Rendering
categoryindex: 8
index: 3
---

# Custom Commands & Escape Hatches

The 2D rendering system is built on a discriminated union (`Command2D`). The fluent Draw DSL covers standard shapes, sprites, text, and render state. When you need to go outside those primitives, `.drawImmediate(...)` is the escape hatch.

## What and Why

`DrawImmediate` lets you run arbitrary rendering code inside the command pipeline. The renderer flushes the backend's internal batch, temporarily exits any active camera and shader modes, runs your action, then restores the previous state. Your code executes outside the batch — direct backend calls (raylib rlgl/custom meshes, or MonoGame device access), custom GPU operations.

You give up batching. You gain full control.

## When to use

Use `.drawImmediate(...)` when:

- You need direct `Rlgl.*` calls (custom vertices, instancing, compute dispatches).
- You're integrating a third-party renderer that writes to the GL context directly.
- The fluent DSL can't express what you need.

Otherwise, use the fluent members. They batch automatically and are faster.

## When to use which

| Scenario | Approach |
|----------|----------|
| Standard sprites, text, shapes | Fluent Draw DSL |
| Direct rlgl / instancing / compute | `.drawImmediate(...)` |

## DrawImmediate

There are two ways to create a `DrawImmediate` command:

### Via the fluent DSL

```fsharp
buffer
  .drawImmediate(fun () ->
    Rlgl.Begin(DrawMode.Quads)
    Rlgl.Color4f(1f, 0f, 0f, 1f)
    Rlgl.Vertex2f(0f, 0f)
    Rlgl.Vertex2f(100f, 0f)
    Rlgl.Vertex2f(100f, 100f)
    Rlgl.Vertex2f(0f, 100f)
    Rlgl.End()
  )
  .drop()
```

### As a Command2D factory

```fsharp
let cmd =
  Command2D.drawImmediate 0<RenderLayer> (fun () ->
    Rlgl.Begin(DrawMode.Quads)
    Rlgl.Color4f(0f, 1f, 0f, 1f)
    Rlgl.Vertex2f(0f, 0f)
    Rlgl.Vertex2f(50f, 0f)
    Rlgl.Vertex2f(50f, 50f)
    Rlgl.Vertex2f(0f, 50f)
    Rlgl.End()
  )

buffer.Add(cmd)
```

### What happens internally

When the renderer encounters a `Command2D.DrawImmediate` case:

1. `Rlgl.DrawRenderBatchActive()` — flushes pending geometry.
2. Active shader mode is ended (if any).
3. Active camera mode is ended (if any).
4. Your `action` runs.
5. Previous camera and shader modes are restored.

This is implemented in `Renderer2D.fs` at the `drawImmediate` helper (line 142). The `try`/`finally` block guarantees state restoration even if your action throws.

### Example: custom textured quad with rlgl

```fsharp
let drawCustomQuad (texture: Texture2D) (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
  buffer
    .drawImmediate(
      (fun () ->
        Rlgl.SetTexture(int texture.Id)
        Rlgl.Begin(DrawMode.Quads)
        Rlgl.Color4ub(255uy, 255uy, 255uy, 255uy)
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex2f(0f, 0f)
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex2f(200f, 0f)
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex2f(200f, 200f)
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex2f(0f, 200f)
        Rlgl.End()
        Rlgl.SetTexture(0u)),
      layer = layer
    )
    .drop()
```

> _**IMPORTANT**_: Each `DrawImmediate` call forces a batch flush before and after. If you call it in a loop (e.g., once per entity), you pay the flush cost every time. Batch your custom work into a single `DrawImmediate` call where possible.

## Full pipeline example

Mix fluent members and `.drawImmediate(...)` in the same buffer. Commands execute in layer order.

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let layer10 = 10<RenderLayer>

  buffer
    .fillRect(0f, 0f, 800f, 600f, Color.DarkGray)
    .sprite(SpriteState.create(model.Tex, model.Dest, model.Src))
    .drawImmediate(
      (fun () ->
        Rlgl.Begin(DrawMode.Quads)
        Rlgl.Color4f(1f, 1f, 0f, 0.5f)
        Rlgl.Vertex2f(300f, 300f)
        Rlgl.Vertex2f(400f, 300f)
        Rlgl.Vertex2f(400f, 400f)
        Rlgl.Vertex2f(300f, 400f)
        Rlgl.End()),
      layer = layer10
    )
    .text(model.Font, "Overlay", Vector2(10f, 10f), 20f, layer = layer10)
    .drop()
```

> _**TIP**_: Use `.drop()` at the end of your view function to discard the buffer reference and silence unused-value warnings.

## See also:

- [Buffer & Commands](buffer-and-commands.html) — the fluent DSL and command reference.
- [Overview](overview.html) — 2D rendering pipeline architecture.
