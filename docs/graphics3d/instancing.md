---
title: GPU Instancing
category: 3D Rendering
categoryindex: 11
index: 5
---

# GPU Instancing

GPU instancing draws many copies of the same mesh in a single draw call. Use it when you have thousands of identical objects: blocks, trees, grass, rocks.

## What and Why

Without instancing, drawing 10,000 cubes means 10,000 draw calls. With instancing, it's **one draw call per mesh type**. The GPU receives an array of transforms and renders all copies in a single pass.

This is the key to rendering voxel worlds, forests, or any scene with high object counts.

## When to use

| Situation | Approach |
|-----------|----------|
| < 50 identical objects | `.mesh(...)` per object (simpler) |
| 50–1,000+ identical objects | `.instanced(...)` (one draw call) |
| dozens of *animated* characters | `.animatedModelInstanced(...)`: see [Skinned + Instanced Draws](../animation3d.html#Skinned-Instanced-Draws) |
| Footprint grid (2D map + column heights) | `buffer.renderFootprintInstanced(...)` (automatic grouping); voxel grids are retired |

## Instanced draws

The low-level instanced draw member. You provide the mesh, an array of transforms, material, and count:

```fsharp
let transforms =
    [| for i in 0 .. 99 ->
        Matrix4x4.CreateTranslation(float32 i * 2f, 0f, 0f)
    |]

buffer
  .instanced(Primitive3D.cube, transforms, material, 100)
  .drop()
```

One draw call renders all 100 cubes. (On MonoGame, pass `prims.Cube` and `Matrix[]` transforms; the member takes your backend's mesh and matrix types.)

### Opacity

The material's `Opacity` follows the same three tiers as regular meshes: `>= 1` renders
inline and casts shadows, `0 < Opacity < 1` defers to the sorted transparent pass and
casts no shadows, `<= 0` draws nothing. Classification is per instance and per part, not
per batch:

- A transparent **material** is a property of the whole batch, so it defers the batch as
  one unit.
- A **per-instance tint alpha** below 255 (MonoGame) defers only that instance: the
  opaque instances stay in the inline pass and keep casting shadows, while the
  transparent ones defer and blend. One faded instance does not make the whole batch
  transparent.
- A skinned + instanced model with **mixed part opacities** (a `PerMesh` resolver or
  authored materials) draws its opaque parts inline and defers only its transparent
  parts.

A deferred unit sorts by the distance to the average position of the instances it
carries, so ordering between those instances stays submission order. For a handful of
large transparent surfaces that must blend in perfect order, draw them as regular
transparent meshes instead of instances.

Instanced draws inside a `beginEffect`/`endEffect` scope are the exception — custom
effects own their transparency (see
[materials → Transparency](materials.md#transparency) for the `drawImmediate` escape).

## Per-instance color (MonoGame only)

Pass an optional `colors` array to tint each instance individually. The albedo is multiplied by `color.rgb` and the final alpha by `color.a`:

```fsharp
let colors =
    [| Color.Red; Color.White; Color(80uy, 160uy, 255uy, 255uy) |]

buffer
  .instanced(Primitive3D.cube, transforms, material, 100, colors = colors)
  .drop()
```

The array may be shorter than `count`: instances beyond `colors.Length` render white. A custom effect that opts into instancing can receive the per-instance color by declaring `float4 InstanceColor : TEXCOORD5` in its vertex input; effects that don't declare it still work (the built-in fallback shades colored draws). See [Shader Uniform Reference](../shader-uniforms.html#Instancing-opt-in).

A color alpha below `255` makes that instance semi-transparent; the framework scans the
array once per command and, when any alpha is below `255`, defers the whole batch to the
sorted transparent pass (even with an opaque material).

> _**NOTE**_: Per-instance color is **MonoGame only**. Passing `colors` on raylib raises `NotSupportedException`; its instanced draw has a fixed instance attribute layout.

> _**NOTE**_: On MonoGame, use `.instancedSlice(...)` when the mesh wraps one part of a shared content-pipeline buffer: pass the part's `vertexOffset`/`startIndex` (`0`/`0` for self-contained meshes), and give the mesh record the part's `PrimitiveCount` and `Bounds`. `ModelParts.ofModel` builds those wraps and offsets for you; see [Instancing content-pipeline models (MonoGame)](#Instancing-content-pipeline-models-MonoGame) below, and [3D Buffer & Commands](buffer-and-commands.html#Slices-of-shared-buffers-MonoGame) for the buffer rules.

## Instancing content-pipeline models (MonoGame)

A content-pipeline `Model` packs all of its parts into shared vertex/index buffers and stores vertices bone-local, so its parts cannot go straight into `.instanced(...)`; they need slice offsets and a bone fold. `ModelParts.ofModel` resolves a model into per-part records that carry everything an instanced draw needs:

> _**IMPORTANT**_: `ModelParts` is for **static** models. The instanced draw path carries no bone palette, so a skinned model (parts baked with `SkinnedEffect`) renders in its <abbr title="the neutral pose a model's skeleton starts in; skinning deforms vertices away from it">bind pose</abbr>, with no error. Use [`.animatedModelInstanced(...)`](../animation3d.html#Skinned-Instanced-Draws) for skinned models.

> _**IMPORTANT**_: Treat the `ModelPart[]` from `ofModel` as **read-only**: it is the cached result shared by every caller, and mutating an element (for example swapping `Material`) corrupts it for the model's lifetime. Copy the array (`Array.map`) when you need adjusted parts.

```fsharp
let parts = ModelParts.ofModel(model)   // cached per model instance

let foldBone (t: Matrix) = part.Bone * t

for part in parts do
    // Fold the part's absolute bone in front of each instance transform:
    // content vertices are bone-local. (Skip the copy when part.Bone
    // is Matrix.Identity.)
    let folded = Array.map foldBone transforms

    buffer
      .instancedSlice(part.Mesh, folded, part.Material, count,
                      vertexOffset = part.VertexOffset,
                      startIndex = part.StartIndex)
      .drop()
```

For footprint grids, `InstancedRenderContext` has a parts constructor that does the folding and the offsets for you: return `ModelPart[]` instead of `(mesh, material)` pairs, and pass the **raw** column matrix as the transform (do not fold bones into `getTransform`; the context folds each part's own bone and passes the part's real offsets):

```fsharp
let modelKey (cell: BlockType) = cell.ModelName
let partsFor (cell: BlockType) = ModelParts.ofModel(loadedModels[cell.ModelName])
let translateCell (pos: Vector3) (_cell: BlockType) = Matrix.CreateTranslation(pos)

let instancedCtx =
    InstancedRenderContext<BlockType, string>(
        getKey = modelKey,
        getParts = partsFor,
        getTransform = translateCell)
```

## InstancedRenderContext for footprint grids

For grid-based worlds, `InstancedRenderContext<'T, 'K>` handles grouping and batching automatically. It groups cells by a key function, then emits one instanced draw per group per sub-mesh. Grids are 2D footprints (`CellGrid2D`, square or hex) with the vertical extent carried in the tile — the heightmap model; the voxel grids are retired.

The transform function receives each column's **base** world position (the footprint position lifted to `y = 0`); scale the unit block by the column's height there.

### Create the context

```fsharp
open Mibo.Layout3D

let blockKey (block: BlockType) = block.ModelPath

let blockMeshes (block: BlockType) =
    // Return array of (mesh, material) pairs for this block type
    let m = loadModel block.ModelPath
    [| for i in 0 .. m.MeshCount - 1 ->
        let mesh = NativePtr.get m.Meshes i
        let matIdx = NativePtr.get m.MeshMaterial i
        let mat = Material3D.fromRaylibMaterial (NativePtr.get m.Materials matIdx)
        struct (mesh, mat)
    |]

let blockTransform (worldPos: Vector3) (_block: BlockType) =
    Raymath.MatrixTranslate(worldPos.X, worldPos.Y, worldPos.Z)

let instancedCtx =
    InstancedRenderContext<BlockType, string>(
        getKey = blockKey,
        getMeshesAndMaterial = blockMeshes,
        getTransform = blockTransform
    )
```

Three function parameters:

| Parameter | Purpose |
|-----------|---------|
| `getKey` | Groups cells by this key. Cells with the same key share a draw call. |
| `getMeshesAndMaterial` | Returns mesh + material pairs for a cell type. Called once per unique key. |
| `getTransform` | Converts grid position to a world transform matrix. |

### Render each frame

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
    // Reset pooled buffers before rendering
    instancedCtx.ResetFrameBuffers()

    buffer
      .beginCamera(camera)
      .setAmbientLight(AmbientLight3D.create (Color(40, 40, 40, 255)))
      // ... lights ...

      // Render the full footprint grid
      .renderFootprintInstanced(instancedCtx, model.World)

      // Or render only within a world-space window
      // .renderFootprintWindowInstanced(instancedCtx, left, top, right, bottom, model.World)

      // ... other geometry ...
      .endCamera()
      .drop()
```

> _**IMPORTANT**_: Call `instancedCtx.ResetFrameBuffers()` once per frame **before** rendering. This returns pooled arrays to `ArrayPool` and prevents memory leaks.

### Window-culled rendering

`renderFootprintWindowInstanced` only processes cells whose footprint
intersects a world-space window (`left`/`top`/`right`/`bottom`, in world
units; hex grids cull with an orientation-aware window). Use it for large
worlds where you only render nearby chunks:

```fsharp
buffer
  .renderFootprintWindowInstanced(instancedCtx, cx - 50, cz - 50, cx + 50, cz + 50, model.World)
  .drop()
```

## How it works internally

1. `renderFootprintInstanced` iterates all populated cells of the footprint grid.
2. Each cell's key is computed via `getKey`.
3. Transforms are accumulated into per-key `ResizeArray<Matrix4x4>`.
4. After iteration, each group emits one instanced draw command per sub-mesh.
5. Arrays are rented from `ArrayPool<Matrix4x4>.Shared` to avoid GC pressure.

The pipeline renders all instances of a mesh type in a single GPU draw call using the instanced shader.

## Shading instances with a custom effect

Instanced draws normally use the built-in PBR instanced shader. To shade them
with your own effect (for a toon, water, fog, or other stylized look), wrap
the instanced draw in a `.beginEffect(...)` / `.endEffect()` scope and have your
shader opt into instancing.

The opt-in is by declaration, and the declaration differs by backend because
each engine feeds per-instance data differently:

- **raylib:** declare `in mat4 instanceTransform;` (raylib streams the rows at
  a per-instance rate). `viewProj` is view-projection only; `matModel` is not
  set for instanced draws.
- **MonoGame:** expose a technique named **`Instanced`** whose vertex shader
  reads the per-instance world matrix as four `float4` rows on `TEXCOORD1..4`
  (matching `ForwardPbr.fx`'s instanced input, or the minimal `Instanced.fx`).

A shader that doesn't declare the opt-in is unaffected; its instanced draws
fall back to the PBR instanced path. Skinned + instanced draws are supported
on all backends: raylib uses a palette texture indexed by `gl_InstanceID`;
MonoGame DX11/Vulkan use vertex texture fetch (VTF); MonoGame DX12 uses a
grouped-uniform constant array (the DX12 mgfx reflection parser drops the
params from the main effect, so an isolated `ForwardPbrGrouped.fx` is loaded);
MonoGame OpenGL falls back to per-instance skinned draws, because the OpenGL
shader profile has no vertex texture fetch.

See [Shader Uniform Reference](../shader-uniforms.html#Instancing-opt-in) for
the full per-backend input contract and minimal example shaders.

## Shading a whole grid with effects

Grid instancing can apply a custom effect per sub-mesh, per cell type, or across
the whole grid. Provide an effect where you want one; cells or sub-meshes
without one keep the default PBR look. The effect must still declare the
instancing opt-in described above, or those draws fall back to the PBR
instanced path.

**Per sub-mesh**: build the context with a `(mesh, material, shader)` triple
for each cell type. Each sub-mesh carrying an effect is shaded by it:

```fsharp
let tileKey (c: Cell) = c.TileType

let tileMeshes (c: Cell) =
    [| struct (baseMesh, baseMat, ValueSome toonShader)
       struct (decoMesh,  decoMat,  ValueNone) |]   // deco keeps PBR

let tileTransform (pos: Vector3) (_c: Cell) =
    Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)

// raylib: Shader voption; MonoGame: Effect voption
let ctx =
    InstancedRenderContext(
        getKey = tileKey,
        getMeshesMaterialAndShader = tileMeshes,
        getTransform = tileTransform)

buffer.renderFootprintInstanced(ctx, grid).drop()
```

**Per cell type**: pass a resolver that returns an effect per grid key:

```fsharp
let tileKey (c: Cell) = c.TileType

let tileMeshes (c: Cell) = ...

let tileTransform (pos: Vector3) (_c: Cell) = ...

let effectFor (tileType: TileType) =
    match tileType with
    | Water -> ValueSome waterShader
    | Lava  -> ValueSome lavaShader
    | _     -> ValueNone

let ctx =
    InstancedRenderContext(
        getKey = tileKey,
        getMeshesAndMaterial = tileMeshes,
        getTransform = tileTransform)

buffer
    .renderFootprintInstanced(ctx, grid, effectFor)
    .drop()
```

**Whole grid**: a special case of per-cell-type: pass `effectFor` with a body
that always returns `ValueSome effect` to shade every cell with one effect.

## Performance tips

- **Key function**: Keep `getKey` cheap. It's called per cell per frame.
- **Transform function**: Avoid allocations. `Raymath.MatrixTranslate` returns a struct.
- **ResetFrameBuffers**: Always call it. Skipping it leaks pooled arrays.
- **Window culling**: Use `renderFootprintWindowInstanced` for large worlds to skip distant cells.
- **Material sharing**: Cells with the same key share materials. Don't create new materials per cell.

## Example: footprint world

```fsharp
type BlockKind =
    | Stone
    | Dirt
    | Grass

type Column = { Kind: BlockKind; Height: int }

let blockKey (col: Column) =
    match col.Kind with
    | Stone -> "stone"
    | Dirt -> "dirt"
    | Grass -> "grass"

let blockMeshes (col: Column) =
    match col.Kind with
    | Stone -> [| struct (cubeMesh, stoneMat) |]
    | Dirt -> [| struct (cubeMesh, dirtMat) |]
    | Grass -> [| struct (cubeMesh, grassMat) |]

let blockTransform (basePos: Vector3) (col: Column) =
    // scale the unit block by the column's height, then place it
    Raymath.MatrixMultiply(
        Raymath.MatrixScale(1f, float32 col.Height, 1f),
        Raymath.MatrixTranslate(basePos.X, basePos.Y, basePos.Z)
    )

let instancedCtx =
    InstancedRenderContext<Column, string>(
        getKey = blockKey,
        getMeshesAndMaterial = blockMeshes,
        getTransform = blockTransform
    )
```

Empty cells produce no draw calls. Each block kind batches into one
instanced draw. Give the tile a `Height` and scale the unit block in
`getTransform` — one grounded column per footprint cell, not one instance
per voxel.

## See also

- [Overview](overview.html): Architecture and pipeline setup
- [Draw DSL](../draw-dsl.html): The fluent draw surface
- [Materials](materials.html): PBR material system
- [Animation 3D: Skinned + Instanced Draws](../animation3d.html#Skinned-Instanced-Draws): instancing animated characters (`animatedModelInstanced`)
