---
title: Shader Uniform Reference
category: Rendering
categoryindex: 3
index: 16
---

# Shader Uniform Reference

The exact uniform names the built-in pipelines upload, so a custom shader or
effect can declare the ones it consumes and inherit the rest of the scene data.

> The **contract is what the pipeline resolves and uploads** (the F# side), not
> what the shipped GLSL/HLSL happens to declare. Sources of truth:
> `Mibo.Raylib/Graphics3D/Pipelines/SceneUpload.fs` + `ForwardPbrPipeline.fs`,
> `Mibo.MonoGame/Graphics3D/Pipelines/SceneUpload.fs` + `ForwardPipeline.fs`,
> and `Mibo.Raylib/Graphics2D/Lighting/LightContext.fs` for 2D.

## Integration points

How custom shading gets into the pipeline, per backend:

| Escape hatch | raylib | MonoGame | Uniforms you receive |
|---|---|---|---|
| `.beginEffect(...)` / `.endEffect()` | ✓ (`Shader`) | ✓ (`Effect`) | Scene data **by name** — declare only what you use; absent ones are skipped. Instanced draws are shaded by your shader when it opts in (see [Instancing](#instancing-opt-in)). |
| Per-mesh-part effect draw | — | ✓ | `World`/`View`/`Projection` only (via `IEffectMatrices`); you own lighting/material |
| `.drawImmediate(...)` | ✓ | ✓ | None — the pipeline shader is bypassed; you get a `SceneContext` with raw device + gathered scene fields |

## `.beginEffect(...)` / `.endEffect()` — the inherited uniform contract

Both backends resolve the **same uniform names** (mirrored `SceneUpload`
modules). Declare a subset in your shader; the pipeline uploads only what's
present. Absent uniforms are a no-op (`-1` location on raylib, `null` parameter
on MonoGame).

### Matrices

| Uniform | Type | Source |
|---|---|---|
| `matModel` | `mat4` / `float4x4` | Per-draw world matrix |
| `viewProj` | `mat4` / `float4x4` | `view * projection` (compose with `matModel` in-shader) |
| `normalMatrix` | `mat4` / `float4x4` | `transpose(inverse(matModel))` |
| `cameraPos` | `vec3` / `float3` | Active camera world position |

> The built-in PBR shaders (and `drawMeshEffect`) use a precomposed `mvp`.
> `beginEffect` does **not** set `mvp` — declare `matModel` + `viewProj` and
> compose the clip-space transform yourself.

### Animation clock

| Uniform | Type | Source |
|---|---|---|
| `time` | `float` | Total elapsed game time, seconds. Absent on the default PBR shaders; declare it for water/flow effects. |

### Material

| Uniform | Type | Source |
|---|---|---|
| `albedoColor` | `vec4` / `float4` | Base color tint |
| `roughness` | `float` | Scalar roughness |
| `metallic` | `float` | Scalar metallic |
| `emissionColor` | `vec4` / `float4` | Emission tint |
| `opacity` | `float` | Alpha multiplier |
| `tiling` | `vec2` / `float2` | UV tiling |
| `useNormalMap` | `int` | `1` if a normal map is bound, else `0` |
| `texture0` | `sampler2D` | Albedo map |
| `texture1` | `sampler2D` | Roughness map (raylib) / Roughness map (MonoGame `s1`) |
| `texture2` | `sampler2D` | Normal map |
| `texture3` | `sampler2D` | Metallic map (raylib) / Metallic map (MonoGame `s3`) |
| `texture4` | `sampler2D` | Emission map |

### Lights

In single-camera frames these reflect every light command in the buffer,
frame-globally. In frames with more than one camera block they describe the
active camera block's light set: a block that issues its own light commands
resets to the frame defaults (lights emitted outside any camera block) and
applies them in order; a block that issues none inherits the running set.
Only the first directional light is shaded, and only it can cast shadows.

| Uniform | Type | Source |
|---|---|---|
| `ambientColor` | `vec3` / `float3` | Ambient color |
| `ambientIntensity` | `float` | Ambient brightness |
| `dirLightDir` | `vec3` / `float3` | Directional light direction (travel direction) |
| `dirLightColor` | `vec3` / `float3` | Directional color |
| `dirLightIntensity` | `float` | Directional brightness |
| `pointLightCount` | `int` | Active point lights |
| `pointLightPos[i]` | `vec3` / `float3` | Per-light position (array, default max 8) |
| `pointLightColor[i]` | `vec3` / `float3` | Per-light color |
| `pointLightIntensity[i]` | `float` | Per-light brightness |
| `pointLightRadius[i]` | `float` | Per-light radius |
| `pointLightFalloff[i]` | `float` | Per-light falloff exponent |
| `spotLightCount` | `int` | Active spot lights |
| `spotLightPos[i]` | `vec3` / `float3` | Per-light position (array, default max 4) |
| `spotLightDir[i]` | `vec3` / `float3` | Per-light direction |
| `spotLightColor[i]` | `vec3` / `float3` | Per-light color |
| `spotLightIntensity[i]` | `float` | Per-light brightness |
| `spotLightRadius[i]` | `float` | Per-light radius |
| `spotLightInnerCutoff[i]` | `float` | Per-light inner cone cosine |
| `spotLightOuterCutoff[i]` | `float` | Per-light outer cone cosine |

### Shadows (opt-in by declaration)

Only uploaded when the active camera block produced a shadow atlas — in buffers with
more than one camera block these are re-uploaded at each block's start and always
describe the block being drawn. A shader that declares
none of these renders unshadowed at no cost.

| Uniform | Type | Source |
|---|---|---|
| `dirLightCastsShadows` | `int` | `1` if the directional light casts shadows, else `0` |
| `shadowViewProjs[i]` | `mat4[]` / `float4x4[]` | Per-caster view-projection (default max 16 casters) |
| `shadowUVOffsets[i]` | `vec4[]` / `float4[]` | Per-caster atlas region (xy=offset, zw=scale) |
| `shadowTexelSize` | `float` (raylib) / `vec2`/`float2` (MonoGame) | `1.0 / atlasResolution` for PCF spread |
| `shadowBiases[i]` | `float[]` | Per-caster receiver-side bias (prevents self-shadow acne; raylib adds an in-shader slope-scale term, MonoGame applies it directly) |
| `pointLightShadowIdx[i]` | `int` | Per-point-light caster slot, `-1` = none |
| `spotLightShadowIdx[i]` | `int` | Per-spot-light caster slot, `-1` = none |
| `shadowAtlas` | `sampler2D` | The depth atlas (MonoGame DX11/Vulkan: `Texture2D shadowAtlas : register(t5); SamplerState shadowAtlasSampler : register(s5);` — MonoGame OpenGL: `sampler2D shadowAtlas : register(s5)`) |

> **Shadow sampler slot differs by backend.** raylib binds the atlas to
> **slot 15** (and sets the `shadowAtlas` sampler uniform to 15). MonoGame binds
> it to **slot 5** (PointClamp) and exposes it through the effect's `shadowAtlas`
> parameter (mgfxc names the parameter after its HLSL declaration, not the
> register slot — so on DX11/Vulkan declare
> `Texture2D shadowAtlas : register(t5); SamplerState shadowAtlasSampler : register(s5);`
> and sample with `shadowAtlas.Sample(shadowAtlasSampler, uv)`; on OpenGL declare
> `sampler2D shadowAtlas : register(s5)`. The built-in `ForwardPbr.fx` switches
> between the two forms with an `#if OPENGL` macro). Both backends use the same
> uniform name: `shadowAtlas`.

### Skinning (only for skinned draws)

| Uniform | Type | Source |
|---|---|---|
| `boneMatrices[128]` | `mat4[]` / `float4x4[]` | Bone palette (uploaded only when bones are supplied) |

### Instancing (opt-in)

An `.instanced(...)` draw inside a `.beginEffect(...)` scope is shaded by your
shader when it declares the instancing input; otherwise it falls back to the
built-in PBR instanced path. The opt-in convention differs by backend because
each engine feeds per-instance data differently — raylib uses a single vertex
attribute and sets the divisor itself, while MonoGame requires two explicit
vertex streams. The data is the same in both cases: a per-instance 4×4 world
matrix.

`matModel` is **not** used for instanced draws (the per-instance transform *is*
the model matrix); the pipeline uploads identity for it, so a shader that still
declares `matModel` sees a benign value.

**raylib (GLSL `#version 330`):** declare the per-instance attribute.

```glsl
in mat4 instanceTransform;   // the opt-in: raylib streams rows at a per-instance rate

uniform mat4 viewProj;       // view * projection (matModel is NOT set for instanced draws)
uniform vec3 cameraPos;      // plus whatever scene uniforms you consume

void main() {
  vec4 world = instanceTransform * vec4(vertexPosition, 1.0);
  gl_Position = viewProj * world;
  // ...
}
```

**MonoGame (HLSL, SM 3.0/5.0):** expose a technique named **`Instanced`** whose
vertex shader reads the per-instance matrix as four `float4` rows on
`TEXCOORD1..4` (usage indices 1-4, to avoid colliding with the mesh's own
`TEXCOORD0` on stream 0). This matches `ForwardPbr.fx`'s `VS_INPUT_INSTANCED`
and the minimal `Instanced.fx`.

```hlsl
float4x4 viewProj;          // plus whatever scene uniforms you consume

struct VS_INPUT_INSTANCED {
  // Stream 0 (per-vertex mesh)
  float3 Position : POSITION0;
  float3 Normal   : NORMAL0;
  float2 TexCoord : TEXCOORD0;
  // Stream 1 (per-instance) — 4 rows composing a 4x4 world matrix
  float4 Row0 : TEXCOORD1;
  float4 Row1 : TEXCOORD2;
  float4 Row2 : TEXCOORD3;
  float4 Row3 : TEXCOORD4;
};

VS_OUTPUT VS_Instanced(VS_INPUT_INSTANCED input) {
  VS_OUTPUT o;
  float4x4 world = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 wp = mul(float4(input.Position, 1.0), world);   // row-vector convention
  o.Position = mul(wp, viewProj);
  // ...
  return o;
}

technique Instanced {   // the opt-in: the pipeline selects this technique for instanced draws
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Instanced();
    PixelShader  = compile PS_SHADERMODEL PS_Main();
  }
}
```

**Per-instance color (optional, MonoGame only).** When the draw supplies a
`colors` array, the pipeline feeds each instance's tint as an additional
`float4` on `TEXCOORD5` (offset 64 in the instance vertex, right after the four
matrix rows). Declare it in your input struct to receive it:

```hlsl
struct VS_INPUT_INSTANCED {
  // ... mesh + Row0..Row3 as above ...
  float4 InstanceColor : TEXCOORD5;   // albedo *= rgb; final alpha *= a
};
```

The declaration is optional — an effect that omits it still works; the built-in
fallback shades colored draws instead. Instances beyond the `colors` array
length receive white.

**Skinned + instanced.** An `animatedModelInstanced` draw inside a
`.beginEffect(...)` scope is shaded by your shader when it opts in; otherwise it
falls back to the built-in PBR skinned-instanced path. Per-instance bone
palettes ride a **palette texture** — RGBA32F, width = `boneCount * 4` texels,
height = instance count, four consecutive texels per bone matrix — instead of
the `boneMatrices` uniform array.

**raylib (GLSL `#version 330`):** declare the instancing attribute, the bone
attributes, and the palette sampler. The instance row is `gl_InstanceID`;
texel `boneIndex*4+c` is column `c` of the bone's matrix (same raw layout the
`boneMatrices` uniform path uploads).

```glsl
in mat4 instanceTransform;
in vec4 vertexBoneIndices;
in vec4 vertexBoneWeights;

uniform sampler2D bonePalette;   // bound on texture unit 14
uniform ivec2 bonePaletteSize;   // (boneCount * 4, instanceCount)

mat4 getBoneMatrix(int boneIndex) {
  mat4 m;
  m[0] = texelFetch(bonePalette, ivec2(boneIndex * 4 + 0, gl_InstanceID), 0);
  m[1] = texelFetch(bonePalette, ivec2(boneIndex * 4 + 1, gl_InstanceID), 0);
  m[2] = texelFetch(bonePalette, ivec2(boneIndex * 4 + 2, gl_InstanceID), 0);
  m[3] = texelFetch(bonePalette, ivec2(boneIndex * 4 + 3, gl_InstanceID), 0);
  return m;
}
```

**MonoGame (HLSL):** expose a technique named **`SkinnedInstanced`** whose
vertex shader combines the skinned input (`BLENDWEIGHT0`/`BLENDINDICES0`), the
instance rows (`TEXCOORD1..4`), and a per-instance palette row index
(`PaletteOffset : TEXCOORD6`), sampling the palette texture at LOD 0. Texel
`boneIndex*4+r` is row `r` of the bone's matrix; the texel-center UV is
`((boneIndex*4+r + 0.5) / paletteTexSize.x, (instance + 0.5) / paletteTexSize.y)`.
The technique ships only where vertex texture fetch exists (DX11/Vulkan — the
OpenGL profile compiles it out, see the note below), so declare the texture
the SM 4+ way: `Texture2D` + `SamplerState` + `.SampleLevel`. (The built-in
`ForwardPbr.fx` hides this split behind `DECLARE_TEX`/`SAMPLE_TEX_LOD` macros;
those macros are not visible to your effect, so spell the pair out.)

```hlsl
Texture2D paletteTex : register(t6);
SamplerState paletteTexSampler : register(s6);
float2 paletteTexSize;   // (boneCount * 4, instanceCount)

struct VS_INPUT_SKINNED_INSTANCED {
  float3 Position    : POSITION0;
  float2 TexCoord    : TEXCOORD0;
  float3 Normal      : NORMAL0;
  float4 BoneWeights : BLENDWEIGHT0;
  int4   BoneIndices : BLENDINDICES0;
  float4 Row0 : TEXCOORD1;
  float4 Row1 : TEXCOORD2;
  float4 Row2 : TEXCOORD3;
  float4 Row3 : TEXCOORD4;
  float  PaletteOffset : TEXCOORD6;   // instance row in the palette texture
};

float4 paletteBoneRow(int boneIndex, int row, float instance) {
  float2 uv = float2(
    (float(boneIndex * 4 + row) + 0.5) / paletteTexSize.x,
    (instance + 0.5) / paletteTexSize.y);
  return paletteTex.SampleLevel(paletteTexSampler, uv, 0);
}
```

> The OpenGL shader profile has no vertex texture fetch, so
> `SkinnedInstanced` does not exist there — the `SkinnedInstanced` technique
> probe is skipped on that backend and the framework draws per-instance
> through the `Skinned` path (your `Skinned` technique, if declared, applies).

> **DX12 note:** the MonoGame DX12 backend does not support vertex texture
> fetch (`NotSupportedException`), so VTF is unavailable. Instead, the
> framework loads an isolated `ForwardPbrGrouped.fx` (DX12-only) whose
> `SkinnedInstancedGrouped` / `SkinnedInstancedGroupedColor` techniques read
> bone palettes from a `bonePaletteGroup[448]` constant array in `$Globals`,
> indexed by the per-instance `PaletteOffset`. The main `ForwardPbr.fx` cannot
> serve these techniques on DX12 because the mgfx reflection parser drops the
> `bonePaletteGroup` / `groupBoneCount` params when all 8 techniques are
> present in one file. User effects that declare a `SkinnedInstanced`
> technique fall back to per-instance `Skinned` draws on DX12 (the grouped
> path is framework-PBR-only). A model with more than 448 bones exceeds the
> group budget and takes the same per-instance fallback.

## `drawMeshEffect` (MonoGame only)

A fully user-owned `Effect`. The pipeline sets only the camera + transform
matrices via the effect's `IEffectMatrices` interface:

| Property | Source |
|---|---|
| `World` | The draw's transform matrix |
| `View` | Active camera view |
| `Projection` | Active camera projection |

> Your effect must implement `IEffectMatrices` (as `BasicEffect`,
> `SkinnedEffect`, etc. do). A raw compiled `Effect` from a `.mgfx` that doesn't
> expose the interface gets nothing set — own all parameters yourself and set
> them before issuing the draw.

raylib has no `drawMeshEffect` equivalent; use `beginEffect` (inherits scene
data) or `drawImmediate` (raw rlgl/raylib calls).

## `drawImmediate` (both backends)

The pipeline shader is **bypassed** — there is no uniform contract. The callback
receives a `SceneContext` record with the raw device plus the gathered scene
fields (as F# values, not shader uniforms):

| Field | Type | Notes |
|---|---|---|
| `Device` (MonoGame only) | `GraphicsDevice` | raylib uses global `Raylib.*`/`Rlgl.*` instead |
| `View` | camera view matrix | |
| `Projection` | camera projection matrix | |
| `Camera` | active `Camera3D` | position, target, up, fov, planes |
| `Lights` | `LightBuffers` | ambient + directional + point + spot accumulators (in multi-block buffers, the current camera block's set) |
| `Shadows` | `ShadowResult voption` | `ValueNone` when no shadow-casting light (in multi-block buffers, the current camera block's shadow pass output) |
| `Time` | `float32` | Total elapsed game time, seconds |

Set whatever uniforms your own shader needs directly from these values.

## 2D lit-sprite uniforms

A custom lit-sprite shader must match the built-in uniform layout. **Names differ
by backend** (raylib camelCase, MonoGame PascalCase) — unlike the 3D
`beginEffect` contract.

| raylib (`LitShader.fs`) | MonoGame (`LitSprite.fx`) | Type | Source |
|---|---|---|---|
| `ambientColor` | `AmbientColor` | `vec3`/`float3` | Ambient color |
| `dirLightCount` | `DirLightCount` | `int` | Active directional lights (max 4) |
| `dirLightDirs[i]` | `DirLightDirs[i]` | `vec2`/`float2` | Per-light direction |
| `dirLightColors[i]` | `DirLightColors[i]` | `vec3`/`float3` | Per-light color |
| `dirLightIntensities[i]` | `DirLightIntensities[i]` | `float` | Per-light brightness |
| `dirLightShadowIdx[i]` | `DirLightShadowIdx[i]` | `int` | `0` if casting shadows, else `-1` |
| `pointLightCount` | `PointLightCount` | `int` | Active point lights (max 16) |
| `pointLightPos[i]` | `PointLightPos[i]` | `vec2`/`float2` | Per-light position |
| `pointLightColors[i]` | `PointLightColors[i]` | `vec3`/`float3` | Per-light color |
| `pointLightIntensities[i]` | `PointLightIntensities[i]` | `float` | Per-light brightness |
| `pointLightRadii[i]` | `PointLightRadii[i]` | `float` | Per-light radius |
| `pointLightFalloffs[i]` | `PointLightFalloffs[i]` | `float` | Per-light falloff exponent |
| `pointLightShadowIdx[i]` | `PointLightShadowIdx[i]` | `int` | `0` if casting shadows, else `-1` |
| `occluders[i]` | `Occluders[i]` | `vec4`/`float4` | Line segment (xy=p1, zw=p2) |
| `occluderCount` | `OccluderCount` | `int` | Active occluder segments |
| `shadowSoftness` | `ShadowSoftness` | `float` | Penumbra softness |
| `shadowMaxDistance` | `ShadowMaxDistance` | `float` | Max raymarch distance |
| `normalMap` (normal-map variant) | `NormalMap` (normal-map variant) | `sampler2D` | Normal-map sampler |
| — | `MatrixTransform` | `float4x4` | View-projection (MonoGame only) |

The `MAX_DIR_LIGHTS` (4), `MAX_POINT_LIGHTS` (16), and `MAX_OCCLUDERS` (128 on
DX, 32 on OpenGL) constants must match between your shader and the
`LightContext2D` constructor args.

## Worked examples

### MonoGame — minimal HLSL for `beginEffect`

A toon shader that consumes camera + the directional light + material:

```hlsl
// Toon.fx — declare only the uniforms you consume; the rest are skipped.
float4x4 matModel;
float4x4 viewProj;
float4x4 normalMatrix;
float3 cameraPos;

float4 albedoColor;
sampler2D texture0 : register(s0);

float3 dirLightDir;
float3 dirLightColor;
float dirLightIntensity;

struct VS_INPUT {
  float3 Position : POSITION0;
  float3 Normal   : NORMAL0;
  float2 TexCoord : TEXCOORD0;
};
struct VS_OUTPUT {
  float4 Position : SV_POSITION;
  float2 TexCoord : TEXCOORD0;
  float3 Normal   : TEXCOORD1;
};

VS_OUTPUT VS_Main(VS_INPUT input) {
  VS_OUTPUT o;
  float4 world = mul(float4(input.Position, 1.0), matModel);
  o.Position = mul(world, viewProj);
  o.TexCoord = input.TexCoord;
  o.Normal = mul(input.Normal, (float3x3)normalMatrix);
  return o;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float3 N = normalize(input.Normal);
  float3 L = normalize(-dirLightDir);
  float ndotl = max(dot(N, L), 0.0);
  // Quantize to 3 bands for a toon look
  float band = step(0.33, ndotl) * 0.5 + step(0.66, ndotl) * 0.5;
  float3 albedo = tex2D(texture0, input.TexCoord).rgb * albedoColor.rgb;
  return float4(albedo * dirLightColor * dirLightIntensity * band, 1.0);
}

technique Toon {
  pass P0 {
    VertexShader = compile vs_5_0 VS_Main();
    PixelShader  = compile ps_5_0 PS_Main();
  }
};
```

```fsharp
buffer
  .beginCamera(camera)
  .beginEffect(toonEffect)
  .model(model, transform)
  .endEffect()
  .endCamera()
  .drop()
```

### raylib — minimal GLSL for `beginEffect`

```glsl
#version 330
// Same uniform names as the MonoGame contract — declare only what you use.

in vec3 vertexPosition;
in vec3 vertexNormal;
in vec2 vertexTexCoord;

uniform mat4 matModel;
uniform mat4 viewProj;
uniform mat4 normalMatrix;
uniform vec3 cameraPos;

uniform vec4 albedoColor;
uniform sampler2D texture0;

uniform vec3 dirLightDir;
uniform vec3 dirLightColor;
uniform float dirLightIntensity;

out vec2 vTexCoord;
out vec3 vNormal;

void main() {
  vec4 world = matModel * vec4(vertexPosition, 1.0);
  gl_Position = viewProj * world;
  vTexCoord = vertexTexCoord;
  vNormal = mat3(normalMatrix) * vertexNormal;
}
```

```fsharp
// Fragment shader declares the same uniforms it consumes; load both, then:
buffer
  .beginCamera(camera)
  .beginEffect(toonShader)
  .model(model, transform)
  .endEffect()
  .endCamera()
  .drop()
```

## Convention notes

- **Matrix multiply direction.** MonoGame HLSL uses row-vector convention
  (`mul(position, matrix)`); raylib GLSL uses column-vector (`matrix * position`).
  Compose clip space accordingly: `mul(mul(position, matModel), viewProj)` (HLSL)
  vs `viewProj * matModel * position` (GLSL). `viewProj` is uploaded as
  `view * projection` on both backends.
- **Setting raylib uniforms.** `[<DisableRuntimeMarshalling>]` requires
  `fixed + NativePtr.toVoidPtr` for scalar/vector `SetShaderValue` calls. See
  [Shaders](shaders.html) for the full caveat; `SetShaderValueMatrix` is exempt.
- **Light/shadow budgets.** Point-light/spot-light array sizes come from the
  pipeline constructor (raylib) or baked `#define`s (MonoGame). See
  [3D Lighting](graphics3d/lighting.html#light-limits) for the limits and how to
  change them.
- **MonoGame/OpenGL: index uniform arrays dynamically.** On the DesktopGL
  backend, a uniform array that is only ever indexed with compile-time constants
  (e.g. `shadowViewProjs[0]`) can be trimmed by the MojoShader/GLSL toolchain
  down to the referenced elements — while the effect's parameter table still
  reports the declared element count. MonoGame's GL constant-buffer upload then
  writes past the end of the shader's constant buffer and crashes in
  `EffectPass.Apply` (an `ArgumentException` from `Buffer.BlockCopy`). DirectX
  and Vulkan keep the declared size, so this surfaces as an OpenGL-only crash.
  To avoid it, index the array with a uniform value somewhere in the shader
  (the way `ForwardPbr.fx` indexes with `pointLightShadowIdx[i]` /
  `spotLightShadowIdx[j]`), or declare exactly the slots the shader reads.
  Declaring fewer slots than the pipeline maximums is safe on the upload side —
  uploads are clamped to the effect's declared element count, and the light count
  uniforms (`pointLightCount`/`spotLightCount`) are clamped to the declared slots
  as well.

## Contract changes

Breaking changes and additions to this contract, by Mibo version. Additions
are opt-in: a shader that declares nothing new renders exactly as before.

### 4.x

- **Breaking: 3.x -> 4.x — raylib skinned palettes.** Palettes handed to
  `skinnedMesh` / `DrawSkinnedMesh` — and returned by
  `AnimatedMesh.computeBoneMatrices` — are now plain System.Numerics
  row-major matrices (`palette[i] = InverseBindPose[i] * pose[i]`); the
  pipeline transposes at upload. In 3.x the same APIs expected pre-transposed
  (raylib-native) matrices. If you build palettes yourself, drop your own
  transpose.
- **Added: 4.x — skinned + instanced opt-in.** The palette texture (MonoGame
  `paletteTex` t6 / `paletteTexSampler` s6; raylib `bonePalette` unit 14), the
  `paletteTexSize` / `bonePaletteSize` size uniforms, the
  `PaletteOffset : TEXCOORD6` instance field, and the `SkinnedInstanced`
  technique name — see [Instancing (opt-in)](#instancing-opt-in). No existing
  uniform changed name, meaning, or slot.
- **Limits: 4.x.** MonoGame DX12: the grouped path holds at most 448 bone
  matrices in the forward effect (`bonePaletteGroup[448]`) and 500 in the
  depth effect; models with more than 448 bones fall back to
  per-instance `Skinned` draws. raylib: the palette texture is `boneCount * 4`
  texels wide — OpenGL only guarantees a 1024-texel texture (256 bones);
  larger skeletons depend on the driver's limit (8192+ texels — 2048+ bones —
  is typical on desktop).

## See also

- [Shaders](shaders.html) — Loading custom shaders, setting parameters, the
  `DisableRuntimeMarshalling` caveat
- [3D Rendering Overview](graphics3d/overview.html#escape-hatches) — When to use
  each escape hatch
- [3D Lighting](graphics3d/lighting.html) — Light types, limits, shadow config
- [2D Lighting & Shadows](graphics2d/lighting.html) — The 2D lit-sprite pipeline
