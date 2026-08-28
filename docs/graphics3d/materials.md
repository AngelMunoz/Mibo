---
title: 3D Materials
category: 3D Rendering
categoryindex: 11
index: 4
---

# 3D Materials

`Material3D` is a struct that defines surface appearance for <abbr title="physically based rendering">PBR</abbr> rendering. It carries color, texture maps, and scalar properties, but never a shader handle. The pipeline binds the appropriate shader.

## What and Why

Materials describe *what a surface looks like*. The pipeline's shader reads material properties to compute lighting. You set materials on meshes; the pipeline handles the rest.

Key properties:

- **Albedo**: Base color and optional texture (diffuse look)
- **Roughness**: How rough vs mirror-smooth (0 = mirror, 1 = fully diffuse)
- **Metallic**: Dielectric vs metallic surface (0 = plastic/wood, 1 = metal)
- **Normal map**: Surface detail without extra geometry (<abbr title="a texture that stores per-pixel surface direction, used to fake detail in lighting">normal map</abbr>)
- **Emission**: Self-illumination (glowing surfaces)
- **Opacity**: Transparency

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

// In your view (MonoGame shown; raylib passes its own Mesh;
// prims comes from Primitive3D.create gd, see Primitive meshes below):
buffer
  .mesh(prims.Cube, transform, metalMat)
  .drop()
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
| `Opacity` | `float32` | `1.0` | Alpha (>= 1 = opaque, 0 = invisible, in between = transparent) |
| `Tiling` | `Vector2` | `(1, 1)` | UV tiling multiplier |

## Builder pattern

All materials start from `Material3D.defaults` or a convenience constructor. Use record update syntax for scalar properties and `with*` functions for texture maps (as in the Quick start above). Shorthand constructors cover the common cases:

```fsharp
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

When you load a `.obj`, `.gltf`, or `.fbx` via the asset system, the backend's native material is
extracted into a `Material3D` automatically. If you don't need per-mesh control, `.model(...)`
does this for you; the loops below show what it does internally (the conversion helper differs
by backend because the native material type differs):

```fsharp
// raylib: read a raylib Material
let m = assets.Model("assets/mymodel.obj")
for i = 0 to m.MeshCount - 1 do
    let mesh = NativePtr.get m.Meshes i
    let matIdx = NativePtr.get m.MeshMaterial i
    let raylibMat = NativePtr.get m.Materials matIdx
    let mat = Material3D.fromRaylibMaterial raylibMat

// MonoGame: read a ModelMeshPart's native Effect (BasicEffect/SkinnedEffect)
let model = assets.Model("assets/mymodel")
for mesh in model.Meshes do
    for part in mesh.MeshParts do
        let mat = Material3D.fromModelMeshPart part
```

In both cases the `.model(...)` member does this conversion automatically for all
sub-meshes; use it when you don't need per-mesh control. On MonoGame only the albedo color,
albedo map, and opacity are extracted from native effects (normal/roughness/metallic maps are
not carried by MonoGame's standard effects); assign the remaining PBR maps explicitly if needed.

## Unlit materials

`Material3D.unlit` creates an emissive material that ignores lighting:

```fsharp
let glow = Material3D.unlit Color.Cyan
```

Use for UI elements, debug markers, or anything that should appear at full brightness regardless of scene lighting.

## Transparency

`Opacity` controls how the surface mixes with what is behind it:

- `Opacity >= 1.0`: opaque. Renders in the forward pass, writes depth, casts shadows.
- `0 < Opacity < 1.0`: transparent. Renders after all opaque geometry, sorted far-to-near
  by camera distance, and alpha-blends with the scene. Depth testing stays on but depth
  writes are off for the sorted pass, so a transparent surface never occludes the geometry
  behind it in the depth buffer.
- `Opacity <= 0.0`: not rendered at all (no draw, no shadow).

```fsharp
let glassMat = {
    Material3D.defaults with
        AlbedoColor = Color(200, 220, 255, 120) // alpha 120/255
        Opacity = 120.0f / 255.0f
        Roughness = 0.05f
        Metallic = 0.9f
}
buffer.mesh(prims.Cube, transform, glassMat).drop()
```

Rules:

- **Transparent geometry does not cast shadows and does not write depth.** The shadow and
  scene-depth passes are binary, so they cannot represent partial occlusion; transparent
  surfaces are excluded from both, and the sorted pass writes with depth off. Consequence:
  `PostProcessWithDepth` effects (fog, depth-of-field) sample opaque-only depth on both
  backends. `EnableShadows`/`DisableShadows` scopes do not change this.
- **Ordering is per-camera, far-to-near.** Transparent draws sort by distance to the camera
  that captured them and render at camera boundaries and end of frame. Intersecting
  transparent surfaces can still sort incorrectly; there is no per-pixel ordering.
- **Where opacity comes from.** On raylib, `Opacity` maps from the albedo texture/color
  alpha channel (`Color.A`); on MonoGame, from the effect's alpha (`BasicEffect.Alpha` /
  `SkinnedEffect.Alpha`). On both backends the sorted transparent pass uses alpha blending
  with depth writes off (depth test on) for its duration.
- **Instanced draws follow the same tiers, per instance and per part.** An instanced
  command with a transparent material defers to the sorted pass as one unit and stops
  casting shadows; `Opacity <= 0` draws nothing. On MonoGame, per-instance tint colors
  participate per instance: the instances whose alpha is below 255 defer and blend while
  the opaque instances stay in the inline pass and keep their shadows and depth writes —
  one faded instance does not make the whole batch transparent. Skinned + instanced
  models with mixed part opacities keep their opaque parts inline the same way. Each
  deferred unit sorts by the distance to the average position of the instances it
  carries, so ordering *between* those instances stays submission order — for a few
  large surfaces that must order perfectly, draw them as regular (non-instanced)
  transparent meshes.
- **Custom effects own their transparency.** Draws inside a `beginEffect`/`endEffect`
  scope are not deferred or sorted by the framework: they render immediately with the
  scope's effect. On MonoGame the frame's `BlendState.Opaque` stays on, so a transparent
  material under a custom effect renders solid. For custom transparency use
  `drawImmediate`: its callback runs at render time with the graphics device and the
  gathered scene data, so you can set your own blend and depth states. The callback must
  restore blend/depth state before returning — the framework restores only viewport and
  camera state around it.

## Primitive meshes

The backend provides primitive meshes for basic shapes. The mesh type differs by
backend (raylib `Mesh` / MonoGame `PrimitiveMesh`), and `.mesh(...)` takes whichever yours has:

| Shape | raylib | MonoGame |
|-------|--------|----------|
| Unit sphere | `Primitive3D.sphere` | `prims.Sphere` (from `Primitive3D.create gd`) |
| Unit cube | `Primitive3D.cube` | `prims.Cube` |
| Unit cylinder | `Primitive3D.cylinder` | `prims.Cylinder` |
| Unit plane | `Primitive3D.plane` | `prims.Plane` |
| Torus | `Primitive3D.torus` | `prims.Torus` |
| Unit cone | `Primitive3D.cone` | `prims.Cone` |

```fsharp
// raylib:
let transform = Matrix4x4.CreateScale(2f, 1f, 3f) * Matrix4x4.CreateTranslation(pos)
buffer.mesh(Primitive3D.cube, transform, mat).drop()

// MonoGame: build the primitive set once (needs the GraphicsDevice), then draw
let prims = Primitive3D.create gd
let transform = Matrix.CreateScale(2f, 1f, 3f) * Matrix.CreateTranslation(pos)
buffer.mesh(prims.Cube, transform, mat).drop()
```

> _**IMPORTANT**_: Use `.mesh(...)` with these primitives instead of backend-native immediate
> draws (e.g. `Raylib.DrawCube`). Direct native draws bypass the pipeline's shader and won't
> receive PBR lighting or shadows.

## Overriding a model's material

`.model(...)` always renders with the material baked into the file (auto-extracted per
sub-mesh, as described above). When you want a different material (the authored values look
wrong, you want to reuse one mesh for several looks, gold/silver/bronze variants of the same
model, or you need a flat/debug material), use the override members instead of iterating the
model's meshes by hand:

```fsharp
// Whole-model override: every sub-mesh uses the supplied material
buffer.modelWith(model, transform, Material3D.colored Color.Gold).drop()

// Per-sub-mesh override: a resolver returns the material for each sub-mesh
let goldResolver (i: int) =
    if i = 0 then Material3D.colored Color.Gold else Material3D.defaults

buffer
  .modelWithPerMesh(model, transform, goldResolver)
  .drop()
```

The override goes through the normal PBR and shadow path, so it is lit and shadowed like
an authored material. The default `.model(...)` path is unchanged; overriding is opt-in
and costs nothing when you don't use it.

**Resolver index.** The `int -> Material3D` resolver is indexed by the pipeline's sub-mesh
iteration order, which differs by backend because the native model types differ:

- **raylib**: mesh index `0..model.MeshCount-1`.
- **MonoGame**: a flat counter over `model.Meshes × MeshParts`.

Write the resolver against your specific model's structure; it is not portable across backends.

**Animated models.** MonoGame's animated draw carries no material (it auto-extracts per part,
like `.model(...)`), so the same override pair exists for it:

```fsharp
buffer.animatedModelWith(animatedModel, transform, material).drop()
buffer.animatedModelWithPerMesh(animatedModel, transform, resolver).drop()
```

On raylib, explicit-palette skinned draws go through `.skinnedMesh(...)`, which already takes a
`Material3D` directly; supply the material you want there; no separate override helper is
needed.

## See also

- [Overview](overview.html): Architecture and pipeline setup
- [Draw DSL](../draw-dsl.html): The fluent draw surface
- [Lighting](lighting.html): Light types and shadow configuration
