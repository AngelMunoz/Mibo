---
title: 2D Performance
index: 15
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# 2D Rendering Performance

## 1. Prefer `Draw.*` over `DrawImmediate`

The `Draw.*` DSL compiles to struct commands that raylib batches into GPU draw calls automatically. Every `DrawImmediate` call forces a batch flush (costly):

```fsharp
// Good: batched by raylib
for i = 0 to 999 do
    buffer |> Draw.fillCircle (10<RenderLayer>, Color.Red) (positions[i], 5f)

// Bad: one batch flush per call
for i = 0 to 999 do
    buffer |> Draw.drawImmediate 10<RenderLayer> (fun () ->
        Raylib.DrawCircleV(positions[i], 5f, Color.Red))
```

## 2. Group commands by layer

The buffer sorts by layer. Grouping commands into fewer distinct layers reduces sort cost:

```fsharp
// Prefer this: one layer per visual depth
let worldLayer = 10<RenderLayer>
let uiLayer = 100<RenderLayer>

// Not this: many layers for no reason
let groundLayer = 10<RenderLayer>
let groundLayer2 = 11<RenderLayer>
let groundLayer3 = 12<RenderLayer>
```

## 3. Use partial application for repeated styling

Bind style parameters once rather than passing them repeatedly:

```fsharp
// Good: partial application
let drawHealthBar = Draw.fillRect (10<RenderLayer>, Color.Red)
for hp in healthBars do
    buffer |> drawHealthBar hp.Rect

// Less good: repeated tuples
for hp in healthBars do
    buffer |> Draw.fillRect (10<RenderLayer>, Color.Red) hp.Rect
```

## 4. Struct commands are already zero-allocation

`Command2D` is a `[<Struct>]` discriminated union — every command is stack-allocated with no heap pressure. For custom rendering logic, use `DrawImmediate` which is also zero-allocation:

```fsharp
// Good: DrawImmediate is zero-allocation
buffer |> Draw.drawImmediate 10<RenderLayer> (fun () ->
    Raylib.DrawCircleV(pos, 5f, Color.Red))
```

## 5. Minimize state-switching commands

Commands like `setBlend`, `setScissor`, `beginCamera`, and `beginShader` flush the draw batch. Group draw calls that share state together:

```fsharp
// Good: one blend switch for all additive particles
buffer
|> Draw.setBlend 0<RenderLayer> BlendMode.Additive
|> Draw.fillCircle (10<RenderLayer>, Color.Yellow) (p1, 5f)
|> Draw.fillCircle (10<RenderLayer>, Color.Yellow) (p2, 5f)
|> Draw.setBlend 0<RenderLayer> BlendMode.Alpha
```

## 6. Share textures and fonts

raylib's internal batching is most efficient when consecutive draw calls use the same texture. Sort your commands by texture where practical (though the renderer sorts by layer, so consider arranging layers to keep same-texture draws together).

## 7. The buffer is allocation-free after warmup

`RenderBuffer2D` uses `ArrayPool<Command2D>` internally. It grows as needed but never allocates per-frame once it reaches capacity. Default initial capacity is 1024 commands.

## 8. Culling

For worlds with many off-screen objects, use `Camera2D.viewportBounds` + `Culling.isVisible2D` to skip out-of-view draws:

```fsharp
let viewBounds = Camera2D.viewportBounds camera viewportWidth viewportHeight

for entity in entities do
    if Culling.isVisible2D viewBounds entity.Bounds then
        buffer |> Draw.sprite { ... }
```

See [Culling](../../culling.html).

## 9. Profiling

If you suspect a rendering bottleneck:

- Reduce command count to isolate the issue
- Check for unintended `DrawImmediate` calls
- Verify layer count is reasonable
- Use raylib's built-in profiling or a GPU debugger to check draw-call count
