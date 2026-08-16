---
title: GPU Instancing
index: 20
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# GPU Instancing

GPU instancing draws many copies of the same mesh in a single draw call. Use it when you have thousands of identical objects — blocks, trees, grass, rocks.

## What and Why

Without instancing, drawing 10,000 cubes means 10,000 draw calls. With instancing, it's **one draw call per mesh type**. The GPU receives an array of transforms and renders all copies in a single pass.

This is the key to rendering voxel worlds, forests, or any scene with high object counts.

## When to use

| Situation | Approach |
|-----------|----------|
| < 50 identical objects | `Draw3D.drawMesh` per object (simpler) |
| 50–10,000+ identical objects | `Draw3D.drawMeshInstanced` (one draw call) |
| Cell grid (voxels, tiles) | `CellGridRenderer3D.renderInstanced` (automatic grouping) |

## Draw3D.drawMeshInstanced

The low-level instanced draw command. You provide the mesh, an array of transforms, material, and count:

```fsharp
let transforms =
    [| for i in 0 .. 99 ->
        Matrix4x4.CreateTranslation(float32 i * 2f, 0f, 0f)
    |]

buffer
|> Draw3D.drawMeshInstanced Primitive3D.cube transforms material 100
```

One draw call renders all 100 cubes.

## InstancedRenderContext for cell grids

For grid-based worlds (voxels, tile maps), `InstancedRenderContext<'T, 'K>` handles grouping and batching automatically. It groups cells by a key function, then emits one `DrawMeshInstanced` per group per sub-mesh.

### Create the context

```fsharp
open Mibo.Layout3D

let instancedCtx =
    InstancedRenderContext<BlockType, string>(
        getKey = fun block -> block.ModelPath,
        getMeshesAndMaterial = fun block ->
            // Return array of (mesh, material) pairs for this block type
            let m = loadModel block.ModelPath
            [| for i in 0 .. m.MeshCount - 1 ->
                let mesh = NativePtr.get m.Meshes i
                let matIdx = NativePtr.get m.MeshMaterial i
                let mat = Material3D.fromRaylibMaterial (NativePtr.get m.Materials matIdx)
                struct (mesh, mat)
            |],
        getTransform = fun worldPos block ->
            Raymath.MatrixTranslate(worldPos.X, worldPos.Y, worldPos.Z)
    )
```

Three lambda parameters:

| Parameter | Purpose |
|-----------|---------|
| `getKey` | Groups cells by this key. Cells with the same key share a draw call. |
| `getMeshesAndMaterial` | Returns mesh + material pairs for a cell type. Called once per unique key. |
| `getTransform` | Converts grid position to a world transform matrix. |

### Render each frame

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
    buffer
    |> Draw3D.beginCamera camera
    |> Draw3D.setAmbientLight (AmbientLight3D.create (Color(40, 40, 40, 255)))
    // ... lights ...

    // Reset pooled buffers before rendering
    instancedCtx.ResetFrameBuffers()

    // Render full grid
    CellGridRenderer3D.renderInstanced instancedCtx model.World buffer

    // Or render only within a bounding volume
    CellGridRenderer3D.renderVolumeInstanced instancedCtx viewBounds model.World buffer

    // ... other geometry ...
    |> Draw3D.endCamera
    |> Draw3D.drop
```

> _**IMPORTANT**_: Call `instancedCtx.ResetFrameBuffers()` once per frame **before** rendering. This returns pooled arrays to `ArrayPool` and prevents memory leaks.

### Volume-culled rendering

`renderVolumeInstanced` only processes cells within a bounding box. Use it for chunk-based worlds where you only render nearby chunks:

```fsharp
let bounds = {
    Mibo.Layout3D.BoundingBox.Min = Vector3(cx - 50f, 0f, cz - 50f)
    Max = Vector3(cx + 50f, 64f, cz + 50f)
}

CellGridRenderer3D.renderVolumeInstanced instancedCtx bounds model.World buffer
```

## How it works internally

1. `renderInstanced` iterates all cells in the grid.
2. Each cell's key is computed via `getKey`.
3. Transforms are accumulated into per-key `ResizeArray<Matrix4x4>`.
4. After iteration, each group emits one `Command3D.DrawMeshInstanced` per sub-mesh.
5. Arrays are rented from `ArrayPool<Matrix4x4>.Shared` to avoid GC pressure.

The pipeline renders all instances of a mesh type in a single GPU draw call using the instanced shader.

## Performance tips

- **Key function** — Keep `getKey` cheap. It's called per cell per frame.
- **Transform function** — Avoid allocations. `Raymath.MatrixTranslate` returns a struct.
- **ResetFrameBuffers** — Always call it. Skipping it leaks pooled arrays.
- **Volume culling** — Use `renderVolumeInstanced` for large worlds to skip distant cells.
- **Material sharing** — Cells with the same key share materials. Don't create new materials per cell.

## Example: voxel world

```fsharp
type BlockType = Air | Stone | Dirt | Grass

let instancedCtx =
    InstancedRenderContext<BlockType, string>(
        getKey = function
            | Stone -> "stone"
            | Dirt -> "dirt"
            | Grass -> "grass"
            | Air -> "air",
        getMeshesAndMaterial = function
            | Stone -> [| struct (cubeMesh, stoneMat) |]
            | Dirt -> [| struct (cubeMesh, dirtMat) |]
            | Grass -> [| struct (cubeMesh, grassMat) |]
            | Air -> Array.empty,
        getTransform = fun pos _ ->
            Raymath.MatrixTranslate(pos.X, pos.Y, pos.Z)
    )
```

Air cells produce no draw calls. Stone, dirt, and grass each batch into one instanced draw.

## See also

- [Overview](overview.html) — Architecture and pipeline setup
- [Buffer & Commands](buffer-and-commands.html) — All `Draw3D.*` functions
- [Materials](materials.html) — PBR material system
