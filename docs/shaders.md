---
title: Shaders
category: Rendering
categoryindex: 3
index: 15
---

# Shaders

Shaders are GPU programs that transform vertices and determine pixel colors. They run on the graphics card in parallel, making them efficient for complex visual effects.

## What They Are

- **Vertex shaders** transform 3D model vertices into screen space
- **Fragment shaders** (also called pixel shaders) determine the final color of each pixel
- **Shaders** (in raylib terminology) package vertex+fragment pairs with parameters

## Why Use Them

Use shaders when you need visual effects beyond what built-in rendering provides:

- Custom lighting models (toon shading, stylized PBR)
- Post-processing effects (bloom, tone mapping, color grading)
- Special effects (holograms, distortion, pixelation)
- Optimized rendering for specific art styles

## When to Write Them

You don't need custom shaders to start. Mibo's built-in renderers work without them:

- **2D games**: Use `Graphics2D` with standard drawing (no shaders required)
- **3D games**: Use `Graphics3D` with the built-in forward PBR pipeline (works without custom shaders)

Write shaders when:
- You have specific visual requirements
- You need performance optimizations for your target hardware
- You're building advanced rendering features

## Shaders are backend-specific

The shader language depends on your backend — this is the main place the two backends diverge:

| | raylib | MonoGame |
|---|---|---|
| Language | GLSL (`#version 330`) | HLSL (`.fx`, compiled to `.xnb`) |
| Loading | `Raylib.LoadShader` / `LoadShaderFromMemory` (GLSL strings/files) | The content pipeline: add the `.fx` to your `.mgcb`, then `assets.Effect(name)` |
| Content pipeline | None — plain `.fs`/`.vs` files or strings | The MonoGame content pipeline compiles `.fx` → `.xnb` for DX11 and OpenGL |
| Params | `Raylib.SetShaderValue` / `SetShaderValueMatrix` | Set parameters on the `Effect` object directly (`effect.Parameters.[name].SetValue(...)`) |

## Built-in shaders

Both backends ship the shaders their default pipelines need, so PBR, shadows, and 2D lighting work out of the box:

- **raylib** (`src/Mibo.Raylib/`): GLSL sources embedded for the `ForwardPbrPipeline` (PBR + depth/shadow) and the 2D lit-sprite shaders.
- **MonoGame** (`src/Mibo.MonoGame/Shaders/`): `ForwardPbr` (Cook-Torrance PBR), `DepthShadow` (shadow depth → R32F), `Instanced`, `LitSprite`, `LitSpriteNormalMap` — each as a `.fx` source plus `.dx.mgfx` and `.ogl.mgfx` compiled variants. Platform detection picks the right variant at load time.

## Loading a custom shader

**raylib** — load GLSL from a file or memory:

```fsharp
open Raylib_cs

// Load from file
let myShader = Raylib.LoadShader("shaders/vertex.vs", "shaders/fragment.fs")

// Or load from memory (GLSL strings)
let fragCode = """
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform vec4 tint;

void main() {
    vec4 texel = texture(texture0, fragTexCoord);
    finalColor = texel * tint;
}
"""

let myShader = Raylib.LoadShaderFromMemory(null, fragCode)
```

**MonoGame** — author an HLSL `.fx` and build it through the **content pipeline** (the same pipeline that compiles your models/textures). Add the `.fx` to your `.mgcb` with the `EffectImporter` / `EffectProcessor`, which compiles it to a `.xnb` for both DirectX 11 and OpenGL, then load it like any other content asset:

```
#begin Toon.fx
/importer:EffectImporter
/processor:EffectProcessor
/build:Toon.fx;Toon
```

```fsharp
open Microsoft.Xna.Framework.Graphics

// Loaded through the content pipeline, like a model or texture.
let toonEffect = assets.Effect("Toon")
```

> Effects are content: author `.fx`, add them to the `.mgcb`, and load via
> `assets.Effect`. The framework's `ShaderLoader.loadEffect` is an internal path
> for the built-in shaders it embeds as resources — your game effects go through
> the content pipeline.

## Setting parameters

**raylib** — set shader parameters using `Raylib.SetShaderValue`:

```fsharp
open System.Numerics
open System.Runtime.InteropServices
open Raylib_cs

// Set a float uniform
let loc = Raylib.GetShaderLocation(myShader, "tint")
let mutable value = 1.0f
use p = fixed &value
Raylib.SetShaderValue(myShader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Float)

// Set a matrix uniform (no fixed needed)
let world = Matrix4x4.Identity
let matLoc = Raylib.GetShaderLocation(myShader, "world")
Raylib.SetShaderValueMatrix(myShader, matLoc, world)
```

| Uniform Type | `ShaderUniformDataType` |
|---|---|
| `float` | `ShaderUniformDataType.Float` |
| `Vector2` | `ShaderUniformDataType.Vec2` |
| `Vector3` | `ShaderUniformDataType.Vec3` |
| `Vector4` | `ShaderUniformDataType.Vec4` |
| `Matrix4x4` | `ShaderUniformDataType.Mat4` |

**MonoGame** — set parameters directly on the `Effect`:

```fsharp
myEffect.Parameters.["tint"].SetValue(Microsoft.Xna.Framework.Vector4(1f, 1f, 1f, 1f))
myEffect.Parameters.["world"].SetValue(worldMatrix)
```

## Plugging a custom shader into the pipeline

How you opt in with your own shading depends on the backend:

**Shading scopes (both backends)** — `.beginEffect(shader)` / `.endEffect()` opens a scope where draws are shaded by your shader (raylib `Shader` / MonoGame `Effect`) instead of the default PBR shader, *inheriting* the scene data the pipeline gathered (camera matrices, lights, the shadow pass output, material, bones, frame time). Your shader only needs to declare the uniforms it consumes (e.g. `dirLightDir`, `boneMatrices`, `shadowViewProjs`, `shadowAtlas`, `time`); absent uniforms are skipped. Ideal for toon/cel/wireframe without re-implementing the gather.

**MonoGame only** — a per-mesh-part effect draw: the pipeline sets only World/View/Projection; you own all lighting/material params.

**Raw access** — `.drawImmediate(...)` runs raw backend calls (rlgl/raylib, or MonoGame device access via `SceneContext`); the pipeline's shader is bypassed for those draws. For a full custom pipeline, implement `IRenderPipeline3D`.

See [3D Rendering Overview](graphics3d/overview.html#escape-hatches) for examples.

For the **full list of uniform names** the `beginEffect` scope uploads (so you
know exactly what to declare in your shader to inherit the scene), see
[Shader Uniform Reference](shader-uniforms.html).

### MonoGame native DX12: upload ordering and DynamicVertexBuffer (upstream issue)

When custom-shader geometry is re-uploaded more than once per frame, the
MonoGame **native DX12 backend** orders the work differently than the other
backends: `SetData` on a *static* `VertexBuffer` is recorded into a separate
command list that executes immediately, while draw calls execute at end of
frame. Every draw in the frame therefore reads the **last** upload's data —
garbage or flickering geometry. This is an upstream MonoGame behavior, not a
Mibo-specific one.

The workaround (what the built-in forward PBR machinery does — see the
`stageInstanceData` comment in `PbrShading.fs` and the shadow pass): keep any
buffer that is re-uploaded per frame, per instance group, or per draw as a
**`DynamicVertexBuffer`**, and upload with
`SetData(..., SetDataOptions.Discard)`. Dynamic buffers take the discard-rename
path — each upload gets a fresh backing buffer — so each draw's data stays
intact. Per-frame `SetData` on static buffers is only safe when the buffer is
uploaded once per frame and read once per frame.

The same guidance applies to 2D custom effects drawn through raw device calls:
upload their geometry through a `DynamicVertexBuffer` and draw with
`SetVertexBuffer` + `DrawPrimitives`.

## DisableRuntimeMarshalling and `SetShaderValue` (raylib only)

> This caveat applies **only to the raylib backend**, which uses
> `[<DisableRuntimeMarshalling>]`. MonoGame `Effect` parameter setting is unaffected.

Because the project uses `[<DisableRuntimeMarshalling>]`, you **must** use `fixed + NativePtr.toVoidPtr` when passing scalar, vector, or struct values to `SetShaderValue`. Passing raw values directly as `void*` arguments causes the runtime to treat the value itself as a memory address, leading to access violations.

**DO NOT** do this:

```fsharp
// WRONG — runtime treats the int value as a pointer address
Raylib.SetShaderValue(shader, loc, 1, ShaderUniformDataType.Int)

// WRONG — runtime treats the float value as a pointer address
Raylib.SetShaderValue(shader, loc, 0.5f, ShaderUniformDataType.Float)

// WRONG — runtime treats the Vector3 as a pointer address
Raylib.SetShaderValue(shader, loc, Vector3.One, ShaderUniformDataType.Vec3)
```

**ALWAYS** pin the value and pass a pointer:

```fsharp
open System.Runtime.InteropServices

let setShaderInt (shader: Shader) (loc: int) (value: int) =
    use p = fixed &value
    Raylib.SetShaderValue(shader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Int)

let setShaderFloat (shader: Shader) (loc: int) (value: float32) =
    use p = fixed &value
    Raylib.SetShaderValue(shader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Float)

let setShaderVec3 (shader: Shader) (loc: int) (value: Vector3) =
    use p = fixed &value
    Raylib.SetShaderValue(shader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Vec3)

let setShaderVec4 (shader: Shader) (loc: int) (value: Vector4) =
    use p = fixed &value
    Raylib.SetShaderValue(shader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Vec4)
```

**Exceptions:**

- `SetShaderValueMatrix` takes `Matrix4x4` directly (not `void*`) — this works correctly without `fixed`.
- `Rlgl.SetUniform` (raw rlgl) also requires `fixed + NativePtr.toVoidPtr`.

## Post-process shaders

Post-process passes (`.postProcess(...)` / `.postProcessWithDepth(...)`) run after
the scene renders to an offscreen target. Your action receives a
`PostProcessContext3D` and must draw a fullscreen quad of `ctx.Source`. See
[3D Rendering → Post-processing](graphics3d/overview.html#post-processing) for the
pipeline behavior and the depth-texture contract.

### Scene color texture

The scene color (`ctx.Source`) is always available:

| Backend | Type | Binding |
|---------|------|---------|
| raylib | `RenderTexture2D` (use `.Texture` for the color) | Draw via `Raylib.DrawTexturePro` inside `BeginShaderMode` |
| MonoGame | `RenderTarget2D` | Set as a texture parameter on your `Effect`, draw via the context's `Quad.Draw(effect)` |

### Depth texture (depth-aware passes only)

When you use `postProcessWithDepth`, `ctx.Depth` is `ValueSome texture` containing
camera-POV NDC z (`[0,1]`, non-linear). Always handle the `ValueNone` case — it
means depth wasn't produced this frame (bind a valid texture and pass through
unchanged).

**raylib — binding the depth sampler:**

Raylib's 2D batch flush (triggered by `DrawTexturePro`) only re-binds textures
registered through `SetShaderValueTexture`. Raw rlgl calls (`ActiveTextureSlot` +
`EnableTexture`) set GL state but bypass that registry, so the sampler ends up
unbound and reads `0`. **Always use `SetShaderValueTexture`:**

```fsharp
let depthLoc = Raylib.GetShaderLocation(shader, "texture1")  // your depth sampler

Raylib.BeginShaderMode shader
// ... set scalar uniforms ...
Raylib.SetShaderValueTexture(shader, depthLoc, depthTexture)  // batch-safe binding
Raylib.DrawTexturePro(ctx.Source.Texture, srcRect, dstRect, origin, 0f, Color.White)
Raylib.EndShaderMode()
```

> _**NOTE — raylib auto-binds `texture1`.**_ Raylib maps the GLSL uniform name
> `"texture1"` to its internal `SHADER_LOC_MAP_SPECULAR` slot during
> `LoadShaderFromMemory`. Using `texture0` / `texture1` as your sampler names means
> `GetShaderLocation` resolves them automatically — no manual location attribute
> setup needed.

**MonoGame — binding the depth sampler:**

MonoGame has no equivalent batch-clobbering issue. Set the depth render target as a
texture parameter on your `Effect`, just like the scene color:

```fsharp
effect.Parameters.["DepthTexture"].SetValue(ctx.Depth.Value)
effect.Parameters.["SceneTexture"].SetValue(ctx.Source)
ctx.Quad.Draw(effect)
```

### DisableRuntimeMarshalling caveat (raylib)

The [`fixed + NativePtr.toVoidPtr`](#disableruntimemarshalling-and-setshadervalue-raylib-only)
requirement applies to all scalar/vector uniforms in your post-process shader
(`fogColor`, `fogNear`, etc.). One subtle trap: `Rlgl.GetCullDistanceNear` /
`GetCullDistanceFar` return `double` (8 bytes), but `SetShaderValue` with
`ShaderUniformDataType.Float` reads 4 bytes — convert to `float32` before passing:

```fsharp
// WRONG — uploads the first 4 bytes of a double as float32 (garbage)
let mutable camN = Rlgl.GetCullDistanceNear()
use p = fixed &camN
Raylib.SetShaderValue(shader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Float)

// CORRECT — convert double → float32 first
let mutable camN = float32 (Rlgl.GetCullDistanceNear())
use p = fixed &camN
Raylib.SetShaderValue(shader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Float)
```

## Where to Learn More

- **2D lighting shaders**: See [2D Lighting & Shadows](graphics2d/lighting.html)
- **3D pipeline & PBR shaders**: See [3D Lighting](graphics3d/lighting.html)
- raylib shaders: [raylib shaders documentation](https://www.raylib.com/examples/shaders/loader.html?name=shaders_basic_lighting)
- MonoGame effects: [MonoGame content pipeline / 2MGFX](https://docs.monogame.net/articles/content_pipeline/)
