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
| Language | GLSL (`#version 330`) | HLSL (`.fx`, compiled to `.mgfx`) |
| Loading | `Raylib.LoadShader` / `LoadShaderFromMemory` (GLSL strings/files) | `ShaderLoader.loadEffect gd name` (embedded compiled `.mgfx`) |
| Content pipeline | None — plain `.fs`/`.vs` files or strings | The MonoGame content pipeline compiles `.fx` → `.mgfx` for DX11 (`.dx.mgfx`) and OpenGL (`.ogl.mgfx`) |
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

**MonoGame** — author an HLSL `.fx`, compile it to `.mgfx` (via the 2MGFX tool / content pipeline for both DX11 and OGL), then load it. For shaders embedded as resources, use `ShaderLoader.loadEffect`; for your own compiled `.mgfx` files, load the bytes and construct an `Effect` from the `GraphicsDevice`:

```fsharp
open Microsoft.Xna.Framework.Graphics

// Load a compiled effect from bytes (embed it or read it from disk)
let effect = new Effect(gd, effectBytes)

// Set a uniform
effect.Parameters.["tint"].SetValue(Microsoft.Xna.Framework.Vector4(1f, 1f, 1f, 1f))
```

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

**raylib (3D)** — `Draw3D.drawImmediate` runs raw rlgl/raylib calls (the pipeline's shader is bypassed for those draws). For a full custom pipeline, implement `IRenderPipeline3D`.

**MonoGame (3D)** — two opt-in paths (no raylib equivalent):
- `Draw3D.beginEffect effect` / `Draw3D.endEffect` — a **shading scope**: draws inside are shaded by your `Effect`, which *inherits* the scene data the pipeline gathered (camera matrices, lights, the shadow pass output, material, bones, frame time). Your effect only needs to declare the uniforms it consumes (e.g. `dirLightDir`, `boneMatrices`, `shadowViewProjs`, `time`, `texture5`); absent uniforms are skipped. Ideal for toon/cel/wireframe without re-implementing the gather.
- `Draw3D.drawMeshEffect meshPart transform effect` — a fully user-owned effect (the pipeline sets only World/View/Projection; you own all lighting/material params).

See [3D Rendering Overview](graphics3d/overview.html#escape-hatches) for examples.

For the **full list of uniform names** the `beginEffect` scope uploads (so you
know exactly what to declare in your shader to inherit the scene), see
[Shader Uniform Reference](shader-uniforms.html).

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

## Where to Learn More

- **2D lighting shaders**: See [2D Lighting & Shadows](graphics2d/lighting.html)
- **3D pipeline & PBR shaders**: See [3D Lighting](graphics3d/lighting.html)
- raylib shaders: [raylib shaders documentation](https://www.raylib.com/examples/shaders/loader.html?name=shaders_basic_lighting)
- MonoGame effects: [MonoGame content pipeline / 2MGFX](https://docs.monogame.net/articles/content_pipeline/)
