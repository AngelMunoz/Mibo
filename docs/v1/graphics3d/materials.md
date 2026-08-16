---
title: 3D Materials
index: 19
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# 3D Materials

`Material3D` is a struct that defines surface appearance for PBR rendering. It carries color, texture maps, and scalar properties — but never a shader handle. The pipeline binds the appropriate shader.

## What and Why

Materials describe *what a surface looks like*. The pipeline's shader reads material properties to compute lighting. You set materials on meshes; the pipeline handles the rest.

Key properties:

- **Albedo** — Base color and optional texture (diffuse look)
- **Roughness** — How rough vs mirror-smooth (0 = mirror, 1 = fully diffuse)
- **Metallic** — Dielectric vs metallic surface (0 = plastic/wood, 1 = metal)
- **Normal map** — Surface detail without extra geometry
- **Emission** — Self-illumination (glowing surfaces)
- **Opacity** — Transparency

## Quick start

```fsharp
// Simple red material
let redMat = Material3D.colored Color.Red

// PBR metal with roughness (record update for scalar properties)
let metalMat = {
    Material3D.defaults with
        AlbedoColor = Color(180, 180, 180, 255)
        Roughness = 0.2f
        Metallic = 1.0f
}

// Textured material
let woodMat =
    { Material3D.defaults with Roughness = 0.8f }
    |> Material3D.withAlbedoMap woodTexture

// In your view:
buffer
|> Draw3D.drawMesh Primitive3D.cube transform metalMat
```

## Material3D fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `AlbedoColor` | `Color` | `White` | Base color, multiplied with albedo map |
| `AlbedoMap` | `Texture2D voption` | `ValueNone` | Albedo/diffuse texture |
| `Roughness` | `float32` | `0.5` | Perceptual roughness (0 = mirror, 1 = diffuse) |
| `RoughnessMap` | `Texture2D voption` | `ValueNone` | Roughness texture (green channel) |
| `Metallic` | `float32` | `0.0` | Metallic factor (0 = dielectric, 1 = metal) |
| `MetallicMap` | `Texture2D voption` | `ValueNone` | Metallic texture (blue channel) |
| `NormalMap` | `Texture2D voption` | `ValueNone` | Normal map for surface detail |
| `EmissionColor` | `Color` | `Black` | Self-illumination color |
| `EmissionMap` | `Texture2D voption` | `ValueNone` | Emission texture |
| `Opacity` | `float32` | `1.0` | Alpha (1 = opaque, 0 = transparent) |
| `Tiling` | `Vector2` | `(1, 1)` | UV tiling multiplier |

## Builder pattern

All materials start from `Material3D.defaults` or a convenience constructor. Use record update syntax for scalar properties and `with*` functions for texture maps:

```fsharp
// Scalar properties via record update
let mat = {
    Material3D.defaults with
        AlbedoColor = Color.Blue
        Roughness = 0.3f
        Metallic = 0.8f
}

// Texture maps via builder functions
let texturedMat =
    Material3D.defaults
    |> Material3D.withAlbedoMap albedoTex
    |> Material3D.withNormalMap normalTex

// Shorthand constructors
let red = Material3D.colored Color.Red          // albedo = red, rest default
let glow = Material3D.unlit Color.Yellow        // emissive, no lighting
```

## Texture maps

Textures are optional. When absent, the scalar/color value applies. When present, the texture is multiplied with the scalar.

| Map | Purpose | Typical source |
|-----|---------|----------------|
| `AlbedoMap` | Base color / diffuse | PNG/JPG color texture |
| `RoughnessMap` | Per-pixel roughness | Grayscale, green channel |
| `MetallicMap` | Per-pixel metallic | Grayscale, blue channel |
| `NormalMap` | Surface normals | Tangent-space normal map |
| `EmissionMap` | Self-illumination | Color texture |

```fsharp
let mat =
    Material3D.defaults
    |> Material3D.withAlbedoMap albedoTexture
    |> Material3D.withNormalMap normalTexture
    |> Material3D.withRoughnessMap roughnessTexture
    |> Material3D.withMetallicMap metallicTexture
```

## Loading from model files

When you load a `.obj`, `.gltf`, or `.fbx` via the asset system, raylib materials are extracted automatically. Use `Material3D.fromRaylibMaterial` to convert:

```fsharp
let m = assets.Model("assets/mymodel.obj")

for i = 0 to m.MeshCount - 1 do
    let mesh = NativePtr.get m.Meshes i
    let matIdx = NativePtr.get m.MeshMaterial i
    let raylibMat = NativePtr.get m.Materials matIdx
    let mat = Material3D.fromRaylibMaterial raylibMat
    // mat now has textures and values extracted from the file
```

The `Draw3D.drawModel` function does this conversion automatically for all sub-meshes. Use it when you don't need per-mesh control.

## Unlit materials

`Material3D.unlit` creates an emissive material that ignores lighting:

```fsharp
let glow = Material3D.unlit Color.Cyan
```

Use for UI elements, debug markers, or anything that should appear at full brightness regardless of scene lighting.

## Primitive meshes

Use `Primitive3D.*` meshes with `Draw3D.drawMesh` for basic shapes:

| Mesh | Description |
|------|-------------|
| `Primitive3D.sphere` | Unit sphere (radius 1, 32x32 segments) |
| `Primitive3D.cube` | Unit cube (1x1x1) |
| `Primitive3D.cylinder` | Unit cylinder (radius 1, height 1) |
| `Primitive3D.plane` | Unit plane (1x1) |
| `Primitive3D.torus` | Torus (inner 0.5, outer 1) |
| `Primitive3D.cone` | Unit cone (radius 1, height 1) |

Scale via the transform matrix:

```fsharp
let transform =
    Matrix4x4.CreateScale(2f, 1f, 3f)
    * Matrix4x4.CreateTranslation(pos)

buffer
|> Draw3D.drawMesh Primitive3D.cube transform mat
```

> _**IMPORTANT**_: Use `Draw3D.drawMesh` with `Primitive3D.*` instead of `Raylib.DrawCube` etc. Direct raylib draws bypass the pipeline's shader and won't receive PBR lighting or shadows.

## See also

- [Overview](overview.html) — Architecture and pipeline setup
- [Buffer & Commands](buffer-and-commands.html) — All `Draw3D.*` functions
- [Lighting](lighting.html) — Light types and shadow configuration
