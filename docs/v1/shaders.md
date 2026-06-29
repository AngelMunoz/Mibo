---
title: Shaders
category: v1
categoryindex: 200
index: 2
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


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

You don't need custom shaders to start. Mibo.Raylib's built-in renderers work without them:

- **2D games**: Use `Graphics2D` with standard raylib drawing (no shaders required)
- **3D games**: Use `Graphics3D` with standard raylib model rendering (works without custom shaders)

Write shaders when:
- You have specific visual requirements
- You need performance optimizations for your target hardware
- You're building advanced rendering features

## Loading Shaders

Shaders are GLSL strings loaded at runtime using `Raylib.LoadShaderFromMemory` or `Raylib.LoadShader`. There is no content pipeline — shaders are plain `.fs`/`.vs` files or embedded strings.

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

## Setting Parameters

Set shader parameters using `Raylib.SetShaderValue`:

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

> _**IMPORTANT**_: The project uses `[<DisableRuntimeMarshalling>]`. This affects how `SetShaderValue` works — see the critical warning below.

## DisableRuntimeMarshalling and `SetShaderValue`

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

Shader binding contracts (what parameters each shader type expects) are documented in the rendering sections:

- **2D rendering shaders**: See [Rendering 2D overview](graphics2d/overview.html) for shader contracts

See also: [raylib shaders documentation](https://www.raylib.com/examples/shaders/loader.html?name=shaders_basic_lighting)
