---
title: Draw DSL
category: Rendering
categoryindex: 3
index: 11
---

# Draw DSL

The **fluent Draw DSL** is the recommended way to write view code in Mibo. It is a single API for **2D and 3D** that works identically on **both backends** (raylib and MonoGame): you chain calls on the render buffer, and the chain compiles down to the same direct buffer commands you would write by hand — there is no runtime cost.

> [!IMPORTANT]
> The fluent DSL is the blessed path going forward. The function-based (piped) modules — `Draw`, `Draw3D`, `LightDraw`, `ParticleDraw` — still work in this release but **will be removed in a future version**. New code should use the fluent DSL; see [Migrating from the piped DSL](#migrating-from-the-piped-dsl).

```fsharp
open Mibo.Elmish.Graphics   // the Draw extensions
open Mibo.Elmish.Graphics2D // buffer, SpriteState, RenderLayer

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  buffer
    .beginCamera(model.Camera)
    .fillCircle(Vector2(400f, 300f), 48f, Color.Blue)
    .sprite(model.PlayerSprite)
    .lineThick(Vector2.Zero, Vector2(800f, 600f), Color.White, thickness = 2f)
    .endCamera()
    .text(model.Font, "HP 100", Vector2(10f, 10f), 20f, layer = 1001<RenderLayer>)
  .drop()
```

Every member returns the buffer, so calls chain. End a chain with `.drop()` to discard the buffer (or pipe it into another view function when composing views).

## Parameters and defaults

Parameters are ordered by how often you set them; anything with a sensible default is an **optional parameter** you pass by name:

| Parameter | Default | Notes |
| --- | --- | --- |
| `layer` | `0<RenderLayer>` | 2D draw ordering, as today |
| `tint` | `Color.White` | sprite/text tint |
| `thickness` | `1.0f` | outlines and thick lines |
| `roundness` / `segments` | `0.5f` / `8` | rounded rects (`16` for sectors/rings) |
| `intensity` / `castsShadows` | `1.0f` / `false` | 2D lights from parts |
| `spacing` | `1.0f` | text — used by raylib, ignored by MonoGame (see [Sprites and text](#sprites-and-text)) |

```fsharp
buffer
  .fillRect(10f, 10f, 220f, 36f, Color.Red)                       // all defaults
  .fillRect(10f, 56f, 220f, 36f, Color.Blue, layer = 5<RenderLayer>)
  .rectOutline(10f, 10f, 220f, 36f, Color.White, thickness = 2f)
  .drop()
```

Optional parameters are struct optionals — they allocate nothing whether you pass them or not.

## Shared vocabulary

The DSL speaks a small set of backend-neutral types. Each backend converts them at the buffer boundary, inline:

- **Colors** — `Mibo.Color` (byte RGBA, `Color.rgb`/`Color.create` plus named values). Convert to/from a backend color with the existing `op_Implicit`/`.ToMiboColor()` extensions when you need one.
- **Vectors and matrices** — `System.Numerics` (`Vector2`, `Vector3`, `Matrix4x4`). raylib uses these natively; the MonoGame witnesses convert through the same `op_Implicit`/`.ToNumerics()` conversions MonoGame already ships.
- **Rectangles** — passed as plain `x, y, w, h` floats (rounded to ints on MonoGame, which is pixel-based). A few members keep int pixel coordinates to match the underlying APIs (`rectGradientV`/`rectGradientH`, `circleGradient`, ellipses).

```fsharp
// The same call, both backends:
buffer
  .fillCircle(Vector2(400f, 300f), 48f, Color.rgb 255uy 200uy 100uy)
  .drop()
```

## Backend handles

Anything that *is* a backend type simply passes through as that type — there is no wrapper and no conversion. This is what lets one DSL cover both backends:

- GPU handles: `Texture2D`, `Font`/`SpriteFont`, `Shader`/`Effect`, render targets, models, meshes, materials, sampler states.
- Backend state records: `SpriteState`, `TextState`, `AnimatedSprite`, the 2D light/occluder records, `AnimatedModel`/animation states.
- Cameras and camera configs (`Camera2D`/`Camera3D` and their `*Config` records).

```fsharp
// raylib view
buffer.beginCamera(model.Camera)          // Raylib_cs.Camera2D
buffer.sprite(tileSprite)                 // raylib SpriteState
buffer.drop()

// MonoGame view — identical shape
buffer.beginCamera(model.Camera)          // Mibo MonoGame Camera2D record
buffer.sprite(tileSprite)                 // MonoGame SpriteState
buffer.drop()
```

Because handles are backend-typed, a view file written against one backend compiles unchanged against the other as long as it only builds values both sides have (this is how the samples share code across clients).

## 2D tour

### Shapes

Rectangles, rounded rects, gradients, circles, sectors, rings, ellipses, lines, beziers, triangles, polygons — all available with the optional-parameter defaults from the table above.

```fsharp
buffer
  .fillRect(0f, 0f, 64f, 64f, Color.Green)
  .circleSector(Vector2(120f, 120f), 40f, 0f, 180f, Color.Red, segments = 24)
  .triangle(Vector2(0f, 0f), Vector2(10f, 20f), Vector2(20f, 0f), Color.Blue)
  .drop()
```

`lineStrip`/`triangleFan`/`triangleStrip` take the backend's vector-array type directly (System.Numerics arrays on raylib, XNA arrays on MonoGame) — no per-frame conversion.

### Sprites and text

Sprites use the backend `SpriteState` record as today; text has both the record form and a parts form that skips the builder chain:

```fsharp
// record form (unchanged from today)
buffer.sprite(SpriteState.create(tex, dest, src) |> SpriteState.withLayer 10<RenderLayer>)

// text, parts form — size maps to each backend's sizing model:
// raylib: font size in pixels; MonoGame: uniform scale
buffer
  .text(font, "HP 100", Vector2(10f, 10f), 20f, layer = 1001<RenderLayer>)
  .text(state)                  // or the backend TextState record
  .drop()
```

### Cameras, shaders, targets, render state

```fsharp
buffer
  .beginCamera(worldCamera)
  // ...world draws...
  .endCamera(layer = 1000<RenderLayer>)
  .setBlend(BlendMode.Additive)           // backend blend handle
  .setScissor(0, 0, 320, 240)
  .clearScissor()
  .setViewport(0, 0, 640, 480)
  .clear(Color.Black)
  .drop()
```

`beginCameraWith` takes the backend camera-config record (viewport/clear), `beginShader`/`beginTarget` take the backend shader/effect and render-target handles.

### Lighting

The 2D lighting API takes the light context plus the usual records — or plain parts for one-off directional lights:

```fsharp
buffer
  .setAmbient(lighting, ambientColor, layer = 5<RenderLayer>)
  .addDirectionalLight(lighting, sunDir, Color.rgb 255uy 245uy 220uy,
                      intensity = 1.5f, castsShadows = true, layer = 6<RenderLayer>)
  .addPointLight(lighting, torchLight, layer = 7<RenderLayer>)
  .addOccluder(lighting, wallSegment, layer = 8<RenderLayer>)
  .litSprite(lighting, tileSprite)
  .litAnimatedSprite(lighting, playerDest, model.PlayerSprite, layer = 20<RenderLayer>)
  .endLighting(lighting, layer = 999<RenderLayer>)
  .drop()
```

### Particles and post-processing

```fsharp
buffer
  .particles(model.ParticleTexture, model.ParticleBuffer, count, layer = 3<RenderLayer>)
  .postProcess(fun pp ->
    // runs once after the scene renders; `pp` is the backend's
    // PostProcessContext2D — draw a fullscreen quad of pp.Source
    ())
  .drawImmediate(fun () ->
    // flush the batch and run raw backend draw calls
    ())
  .drop()
```

## 3D tour

The same buffer-chaining style; 3D members ignore `layer` (3D commands are unsorted).

```fsharp
buffer
  .setShadowOrigin(shadowFocus)
  .setAmbientLight { Color = Color(30, 30, 40, 255); Intensity = 0.4f }
  .addDirectionalLight sunLight           // Core light types — shared by both backends
  .addPointLight torchLight
  .beginCamera(worldCamera)
  .model(terrainModel, terrainTransform)
  .modelWith(playerModel, playerTransform, highlightMaterial)
  .modelWithPerMesh(ghostModel, ghostTransform, fun i -> ghostMaterials[i])
  .animatedModel(model.PlayerAnim, playerTransform)
  .skinnedMesh(mesh, transform, material, bones)   // explicit palette (raylib)
  .instanced(chunkMesh, chunkTransforms, chunkMaterial, chunkCount)
  .billboard(markerTex, Vector3(0f, 2f, 0f), Vector2(1f, 1f), Color.White)
  // optional: rotation (degrees), atlas sub-rect, blend mode
  .billboard(flameTex, Vector3(2f, 1f, 0f), Vector2(1f, 1f), Color.White,
             rotation = 45f, sourceRect = Rectangle(0, 0, 32, 32), blend = BlendMode.Additive)
  .line3D(Vector3.Zero, Vector3.UnitY, Color.Red)
  .endCamera()
  .postProcess(fun pp -> (* color-only pass *))
  .postProcessWithDepth(fun pp -> (* fog, DOF, SSAO *))
  .drop()
```

- **Models and materials** — `model`, `modelWith` (whole-model override), `modelWithPerMesh` (resolver by flat mesh-part index). Materials are the backend `Material3D`.
- **Meshes and instancing** — `mesh` (raylib `Mesh` / MonoGame `PrimitiveMesh`) and `instanced` for bulk draws. `instanced` takes an optional `colors` array for per-instance tinting (**MonoGame only**).
- **Billboards** — `billboard`/`billboardBatch` take optional `rotation` (degrees around the view axis), `sourceRect` (pixel-space atlas frame; all-zero = full texture), and `blend` (MonoGame: the `BlendMode` DU; raylib: `Raylib_cs.BlendMode`). Blended billboards draw in buffer order with no depth sorting. A MonoGame batch draws every item with the first texture — use an atlas plus `sourceRects`; raylib honors per-item textures.
- **Animated models** — `animatedModel` consumes the backend animation state record; the bone palette is derived for you (MonoGame computes it from the state; raylib applies it to the model — or, with the raylib `AnimatedModel` record, skins on the GPU without mutating the model). `animatedModelWith`/`animatedModelWithPerMesh` add material overrides. All three take an optional `pose` (a caller-evaluated `BonePose`) so one pose evaluation per frame can be shared with bone queries and attachments; on raylib the `AnimatedModel` witnesses honor it, the legacy `Animation3DState` witnesses ignore it. `skinnedMesh` is the explicit-palette form (raylib).
- **Bone attachments** — `attachedMesh(animModel, bone, localTransform, mesh, material, transform, ?pose)` draws a static mesh parented to a bone of an animated model (`BoneRef.ByName "Hand_R"` or `BoneRef.ByIndex i`). The attachment's world transform is `localTransform * boneWorld * transform`; an unknown bone is a no-op (no command emitted). Pass the same `pose` given to `animatedModel` to avoid a second pose evaluation. See [Animation 3D](animation3d.html#bone-poses-queries-and-attachments).
- **Effect scopes** — `beginEffect`/`endEffect` shade a group with your own shader, inheriting the scene's camera/lights/shadows; see `docs/shader-uniforms.md`.
- **Lights** — `AmbientLight3D`, `DirectionalLight3D`, `PointLight3D`, `SpotLight3D` are backend-neutral Core types already shared by both backends.

## Backend-specific members

Some features exist on one backend only. The DSL keeps them in the same fluent surface — they simply have no counterpart on the other backend, so **calling them there is a compile error** naming the missing operation:

```fsharp
// MonoGame only — stops tile-atlas bleeding:
buffer.setSamplerState(SamplerState.PointClamp, layer = 9<RenderLayer>)

// raylib only — explicit-palette skinned draw:
buffer.skinnedMesh(mesh, transform, material, bones)
```

This is how asymmetry is meant to look: write the call where the feature exists; the compiler stops you from accidentally porting it.

## Migrating from the piped DSL

The piped modules keep working in this release, but they will be removed. The fluent equivalent of each pattern:

| Piped (old) | Fluent (new) |
| --- | --- |
| `buffer \|> Draw.fillRect (layer, color) rect` | `buffer.fillRect(x, y, w, h, color, layer = layer)` |
| `buffer \|> Draw.sprite state` | `buffer.sprite state` |
| `buffer \|> Draw.text state` | `buffer.text state` or `buffer.text(font, s, pos, size, ...)` |
| `buffer \|> Draw.beginCamera 0<RenderLayer> cam` | `buffer.beginCamera cam` |
| `buffer \|> LightDraw.litSprite ctx state` | `buffer.litSprite(ctx, state)` |
| `buffer \|> LightDraw.litAnimatedSprite ctx layer dest anim` | `buffer.litAnimatedSprite(ctx, dest, anim, layer = layer)` |
| `buffer \|> LightDraw.addPointLight ctx layer light` | `buffer.addPointLight(ctx, light, layer = layer)` |
| `buffer \|> ParticleDraw.particles tex data count layer` | `buffer.particles(tex, data, count, layer = layer)` |
| `buffer \|> Draw3D.drawModel model transform` | `buffer.model(model, transform)` |
| `buffer \|> Draw3D.drawAnimatedModel anim transform` | `buffer.animatedModel(anim, transform)` |
| `buffer \|> Draw3D.addPointLight light` | `buffer.addPointLight light` |
| `buffer \|> Draw3D.beginCamera cam \|> ... \|> Draw3D.endCamera` | `buffer.beginCamera(cam) .... .endCamera()` |
| `... \|> Draw.drop` | `.... .drop()` (or chain into the next view) |

Notes for migrating:

- **Colors** move from backend colors to `Mibo.Color` — drop the `toRaylibColor`/`toMonoGameColor` calls at draw sites (the witnesses convert for you), and use `.ToMiboColor()` where you still need to go the other way.
- **Rectangles** in shape calls become `x, y, w, h` floats. Backend `Rectangle` values still appear in `SpriteState`/lit-sprite destinations, which are pass-through records.
- **`Draw.drop`** becomes `.drop()` at the end of the chain — or pipe the buffer into the next view function (`|> MinimapView.view ctx model`).
- **MonoGame vectors** at draw sites become `System.Numerics`; drop the `Vector2.op_Implicit` conversions you only added to satisfy the old API (the witnesses convert). Values that feed backend records (e.g. `PointLight2D.Position`) still need the backend vector type.

## How it works (for the curious)

Each fluent member is an inline extension resolved at compile time against a small set of `member inline` plug points on the render buffer. The entire chain — optional parameters included — erases at the call site, leaving the same direct `buffer.Add(Command...)` sequence you would write by hand: no dispatch, no closure allocation, no wrapper types. That is also why a member with no plug point on the current backend is a compile error rather than a runtime surprise.
