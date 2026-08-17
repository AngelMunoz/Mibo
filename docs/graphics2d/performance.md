---
title: 2D Performance
category: 2D Rendering
categoryindex: 10
index: 4
---

# 2D Rendering Performance

## 1. Prefer fluent members over `.drawImmediate(...)`

The fluent DSL compiles to struct commands that the backend batches into GPU draw calls automatically. Every `.drawImmediate(...)` call forces a batch flush (costly):

```fsharp
// Good: batched by the backend
for i = 0 to 999 do
    buffer.fillCircle(positions[i], 5f, Color.Red, layer = 10<RenderLayer>).drop()

// Bad: one batch flush per call
let drawRaw () =
    // raw backend draw (e.g. raylib Raylib.DrawCircleV, or MonoGame device draw)
    ()

for i = 0 to 999 do
    buffer
      .drawImmediate(drawRaw, layer = 10<RenderLayer>)
      .drop()
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

## 3. Bind common arguments for repeated draws

Bind a partially-applied call once rather than passing every argument repeatedly:

```fsharp
// Good: bind texture once, reuse across the batch
let tile (dest: Rectangle) = buffer.sprite(SpriteState.create(atlas, dest, src))
for t in visibleTiles do
    tile t.Dest |> ignore

// Less good: rebuild the record every iteration
for t in visibleTiles do
    buffer.sprite(SpriteState.create(atlas, t.Dest, src)) |> ignore
```

## 4. Struct commands are already zero-allocation

`Command2D` is a `[<Struct>]` discriminated union, so every command lives on the stack instead of the heap. The fluent chain itself also disappears at compile time: optional parameters are struct optionals. The practical upshot: the fluent path costs you nothing per command, so write the readable version and don't worry about it.

## 5. Minimize state-switching commands

Commands like `setBlend`, `setSamplerState`, `setScissor`, `beginCamera`, and `beginShader` flush the draw batch. Group draw calls that share state together:

```fsharp
// Good: one blend switch for all additive particles
buffer
  .setBlend(BlendMode.Additive)
  .fillCircle(p1, 5f, Color.Yellow, layer = 10<RenderLayer>)
  .fillCircle(p2, 5f, Color.Yellow, layer = 10<RenderLayer>)
  .setBlend(BlendMode.AlphaBlend)
  .drop()
```

## 6. Share textures and fonts

The backend's internal batching is most efficient when consecutive draw calls use the same texture. Sort your commands by texture where practical (though the renderer sorts by layer, so consider arranging layers to keep same-texture draws together).

## 7. Tile-atlas bleeding

Tiles sampled from a gutterless spritesheet (no padding between tiles) bleed at the edges under <abbr title="filtering that blends between nearby texels, smoothing scaled textures">linear filtering</abbr>, producing dark seams between abutting tiles.

**MonoGame:** use `.setSamplerState(SamplerState.PointClamp)` for the tile draws; point filtering reads exact texels, so there's no bleed. Note it flushes the batch, so group tile draws together. Alternatively, inset each tile's source rectangle by 1px.

```fsharp
// Point filtering stops adjacent tiles from bleeding into each other.
buffer.setSamplerState(SamplerState.PointClamp).drop()

for tile in visibleTiles do
    buffer.sprite(SpriteState.create(atlas, tile.Dest, tile.Src)).drop()
```

**raylib:** there is no per-draw sampler — a texture's filter is set on the texture itself. Use the `Texture.filter` helper once at load time (e.g. `assets.Texture "tiles.png" |> Texture.filter TextureFilter.Point`), or inset source rectangles by 1px.

## 8. The buffer is allocation-free after warmup

`RenderBuffer2D` uses `ArrayPool<Command2D>` internally. It grows as needed but never allocates per-frame once it reaches capacity. Default initial capacity is 1024 commands.

## 9. Culling

For worlds with many off-screen objects, use `Camera2D.viewportBounds` + `Culling.isVisible2D` to skip out-of-view draws:

```fsharp
let viewBounds = Camera2D.viewportBounds camera viewportWidth viewportHeight

for entity in entities do
    if Culling.isVisible2D viewBounds entity.Bounds then
        buffer.sprite(entity.Sprite).drop()
```

See [Culling](../culling.html).

## 10. Profiling

If you suspect a rendering bottleneck:

- Reduce command count to isolate the issue
- Check for unintended `.drawImmediate(...)` calls
- Verify layer count is reasonable
- Use your backend's built-in profiling or a GPU debugger to check draw-call count
