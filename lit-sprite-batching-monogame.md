# MonoGame 2D Lit-Sprite Batching

Executive summary of the change, for developers reviewing or upgrading.

## What changed

The MonoGame 2D lit-sprite path no longer issues **one draw call per sprite**.
It now accumulates consecutive lit sprites into a single indexed vertex buffer
and submits them in **one `DrawUserIndexedPrimitives` per `(effect, texture,
normalMap)` group** — the same kind of batching raylib gets for free from `rlgl`,
done here with a private accumulator inside the renderer.

- The `.litSprite(...)` API, the `LitSprite` command, `LightContext2D`, and the
  lit-sprite shaders are **unchanged**. This is a renderer-internal change only.
- Per-sprite work that happened every sprite before — uniform upload,
  `MatrixTransform`/`Texture`/`NormalMap` parameter binding, blend/depth/raster
  save+restore, the `SpriteBatch`/`PrimitiveBatch` flush+restart — now happens
  **once per flush** (i.e. once per texture group or once per lighting block).
- Draw order between lit and unlit sprites is preserved: a lit run flushes the
  pending unlit batches on entry and submits its own geometry on exit / at
  `endLighting`, so the relative ordering you rely on is identical to before.
- Light uniforms are still uploaded once per lighting block (gated by the
  existing `UniformsDirty` flag), to both effect variants.

## What could visually break

Things to look at when validating this:

1. **Draw ordering of lit vs. unlit sprites on the same layer.** This is the
   invariant the change is most careful about, but it's the highest-risk area.
   If you interleave `.litSprite(...)` with `.sprite(...)` and rely on a
   specific paint order, confirm it still layers correctly.
2. **Flipped sprites.** The flip convention (negative source `Width`/`Height`)
   is reproduced exactly, but verify flipped lit sprites and flipped lit
   animations still sample the right texels (no flicker/blink).
3. **Rotated / offset-origin lit sprites.** Corner transform with origin and
   rotation is preserved verbatim; spot-check rotated sprites and sprites with a
   non-zero origin.
4. **Normal-mapped lit sprites.** A strict `(effect, texture, normalMap)` batch
   key prevents one sprite sampling another's normal map, but if you previously
   relied on per-sprite normal-map binding timing, confirm normal-mapped
   sprites look right — especially when several use different normal maps.
5. **Lit sprites under post-processing.** The lit batch is flushed into the
   scene render target before the post-process drain; verify lit + post-process
   scenes (shadows, bloom, etc.) still composite correctly.
6. **Lit sprites near camera/scissor/blend transitions.** Every state-transition
  command flushes the lit batch automatically; if anything looks clipped or
  out-of-order around camera or scissor changes, that's where to look.

## What we expect

- **Fewer draw calls.** A run of N lit sprites sharing one texture now costs
  ~1 draw call instead of N. Interleaving textures or switching between the
  plain and normal-map variants still splits batches (by design, for
  correctness), so grouping lit sprites by texture in your view pays off.
- **Identical visuals.** The vertex data, transform, UVs, lighting math, and
  shader are the same. Output pixels should match the previous per-sprite path.
- **No API or shader changes.** No migration needed. Custom lit-sprite shaders
  that matched the documented uniform contract continue to work.
- **Pure-CPU regression coverage** for the tessellation math and the batch-key
  flush logic lives in `src/Mibo.MonoGame.Tests/LitBatchTests.fs`. The GPU draw
  itself isn't unit-tested headless; validate visually against a lit scene.
