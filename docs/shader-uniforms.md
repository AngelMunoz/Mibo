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
| `Draw3D.beginEffect` / `endEffect` | ✓ (`Shader`) | ✓ (`Effect`) | Scene data **by name** — declare only what you use; absent ones are skipped |
| `Draw3D.beginEffectWithInstanced` | ✓ | — | As above; routes `DrawMeshInstanced` through a second shader |
| `Draw3D.drawMeshEffect` | — | ✓ | `World`/`View`/`Projection` only (via `IEffectMatrices`); you own lighting/material |
| `Draw3D.drawImmediate` | ✓ | ✓ | None — the pipeline shader is bypassed; you get a `SceneContext` with raw device + gathered scene fields |

## `beginEffect` / `endEffect` — the inherited uniform contract

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

Only uploaded when the frame produced a shadow atlas. A shader that declares
none of these renders unshadowed at no cost.

| Uniform | Type | Source |
|---|---|---|
| `dirLightCastsShadows` | `int` | `1` if the directional light casts shadows, else `0` |
| `shadowViewProjs[i]` | `mat4[]` / `float4x4[]` | Per-caster view-projection (default max 16 casters) |
| `shadowUVOffsets[i]` | `vec4[]` / `float4[]` | Per-caster atlas region (xy=offset, zw=scale) |
| `shadowTexelSize` | `float` (raylib) / `vec2`/`float2` (MonoGame) | `1.0 / atlasResolution` for PCF spread |
| `pointLightShadowIdx[i]` | `int` | Per-point-light caster slot, `-1` = none |
| `spotLightShadowIdx[i]` | `int` | Per-spot-light caster slot, `-1` = none |
| `shadowAtlas` (raylib) / `texture5` (MonoGame) | `sampler2D` | The depth atlas |

> **Shadow sampler slot differs by backend.** raylib binds the atlas to
> **slot 15** (and sets the `shadowAtlas` sampler uniform to 15). MonoGame binds
> it to **slot 5** (PointClamp) and exposes it as the `texture5` sampler
> parameter. Declare the matching sampler name for your backend.

### Skinning (only for skinned draws)

| Uniform | Type | Source |
|---|---|---|
| `boneMatrices[128]` | `mat4[]` / `float4x4[]` | Bone palette (uploaded only when bones are supplied) |

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
| `Lights` | `LightBuffers` | ambient + directional + point + spot accumulators |
| `Shadows` | `ShadowResult voption` | `ValueNone` when no shadow-casting light |
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
|> Draw3D.beginCamera camera
|> Draw3D.beginEffect toonEffect
|> Draw3D.drawModel model transform
|> Draw3D.endEffect
|> Draw3D.endCamera
|> Draw3D.drop
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
|> Draw3D.beginCamera camera
|> Draw3D.beginEffect toonShader
|> Draw3D.drawModel model transform
|> Draw3D.endEffect
|> Draw3D.endCamera
|> Draw3D.drop
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

## See also

- [Shaders](shaders.html) — Loading custom shaders, setting parameters, the
  `DisableRuntimeMarshalling` caveat
- [3D Rendering Overview](graphics3d/overview.html#escape-hatches) — When to use
  each escape hatch
- [3D Lighting](graphics3d/lighting.html) — Light types, limits, shadow config
- [2D Lighting & Shadows](graphics2d/lighting.html) — The 2D lit-sprite pipeline
