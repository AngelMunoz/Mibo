---
title: Animation 3D
index: 7
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# Animation 3D (Skeletal Animation)

Mibo provides a three-tier 3D skeletal animation system in `Mibo.Animation`. It supports per-model CPU skinning, shared-mesh GPU skinning, and animation blending via `UpdateModelAnimationEx`.

## Core Types

| Type | Purpose |
| ---- | ------- |
| `Animation3DClips` | Shared clip set loaded from `ModelAnimation[]` — name/index lookup |
| `Animation3DState` | Per-entity playback state (current frame, blend, speed, loop) |
| `AnimatedMesh` | Shared mesh + inverse bind pose for GPU skinning |

## Quick Start

```fsharp
open Mibo.Animation

// 1. Load model and animations (at init time)
let model = assets.Model "character.glb"
let anims = assets.ModelAnimations "character.glb"
let clips = Animation3DClips.fromModelAnimations anims

// 2. Create per-entity animation state
let anim = Animation3DState.create model clips "idle" 60.0f

// 3. Update each frame (in your animation system)
let anim = anim |> Animation3DState.update deltaTime

// 4. Apply and render (in your view)
Animation3DState.applyToModel anim
buffer |> Draw3D.drawModel anim.Model transform
```

## Three API Tiers

### Tier 1 — Data Extraction (`Animation3DClips`)

Load and query animation clips. No GPU, no model mutation.

```fsharp
let anims = assets.ModelAnimations "character.glb"
let clips = Animation3DClips.fromModelAnimations anims

let names = Animation3DClips.names clips    // [|"idle"; "walk"; "jump"|]
let count = Animation3DClips.count clips    // 3
let idx = Animation3DClips.tryGetClipIndex "walk" clips  // ValueSome 1
```

### Tier 2 — GPU Skinning (`AnimatedMesh`)

Share one mesh across many entities. Each entity computes its own bone matrices.

```fsharp
let mesh = AnimatedMesh.fromModel model  // load once, share

// Per-entity (lightweight — just matrix math)
let bones = AnimatedMesh.computeBoneMatrices clip frame mesh

// Render — GPU does the skinning
buffer |> Draw3D.drawSkinnedMesh mesh.Mesh transform material bones
```

### Tier 3 — Per-Model CPU Skinning (`Animation3DState`)

Simplest API. Each entity owns its own `Model` copy. Raylib's `UpdateModelAnimation` handles skinning on the CPU.

```fsharp
let anim = Animation3DState.create model clips "idle" 60.0f
let anim = anim |> Animation3DState.update dt
Animation3DState.applyToModel anim  // mutates model's bone matrices
buffer |> Draw3D.drawModel anim.Model transform
```

### When to Use Which

| Scenario | Tier | Why |
|----------|------|-----|
| 1–5 animated characters | Tier 3 | Simple, no shader changes |
| 10+ animated enemies | Tier 2 | Share mesh, GPU skinning |
| Hundreds of units (RTS) | Tier 2 + instancing | `DrawMeshInstanced` with skinning |

## Animation3DClips API

### Loading

```fsharp
let anims = assets.ModelAnimations "character.glb"
let clips = Animation3DClips.fromModelAnimations anims
```

The `ModelAnimations` asset method loads all skeletal animations from a glb/gltf/iqm file. Returns an empty array if the model has no animations.

### Discovery

```fsharp
let names = Animation3DClips.names clips          // [|"idle"; "walk"; "jump"|]
let count = Animation3DClips.count clips           // 3
let empty = Animation3DClips.isEmpty clips         // false
let idx = Animation3DClips.tryGetClipIndex "walk" clips  // ValueSome 1
```

## Animation3DState API

### Creation

```fsharp
// Start on a named clip
let anim = Animation3DState.create model clips "idle" 60.0f

// Start on a clip index (zero string allocation)
let anim = Animation3DState.createByIndex model clips 0 60.0f

// Default to index 0 if name not found
let anim = Animation3DState.create model clips "nonexistent" 60.0f
```

The `fps` parameter controls playback speed. It is divided by 60 internally (raylib's default keyframe rate) to produce a speed multiplier.

### Playback Control

```fsharp
// Switch animation (resets frame, cancels blend)
let anim = anim |> Animation3DState.play "walk"

// Switch by index (zero string allocation)
let anim = anim |> Animation3DState.playByIndex 1

// Switch only if not already playing
let anim = anim |> Animation3DState.playIfNot "walk"

// Restart current animation
let anim = anim |> Animation3DState.restart
```

### Blending

Crossfade between two animations using `UpdateModelAnimationEx`:

```fsharp
// Blend from current to "walk" over 0.2 seconds
let anim = anim |> Animation3DState.blendTo "walk" 0.2f

// Or by index
let anim = anim |> Animation3DState.blendToByIndex 1 0.2f

// Check blend state
let blending = Animation3DState.isBlending anim  // true during blend
```

`blendTo` is idempotent — calling it repeatedly with the same target does not restart the blend. When the blend completes, the target animation becomes the current animation.

### Update

```fsharp
let anim = anim |> Animation3DState.update deltaTime
```

Advances the current frame (and blend target frame if blending). Respects `Loop` and `Speed` settings. Does not apply to the model — use `applyToModel` after.

### Apply to Model

```fsharp
Animation3DState.applyToModel anim
```

Calls `Raylib.UpdateModelAnimation` (or `UpdateModelAnimationEx` when blending) which mutates the model's bone matrices. Must be called before rendering with `DrawModel`.

### Query

```fsharp
let finished = Animation3DState.isFinished anim
let playing = Animation3DState.isPlaying "walk" anim
let name = Animation3DState.currentClipName anim
let dur = Animation3DState.duration anim
```

### Configuration

```fsharp
let anim = anim |> Animation3DState.withSpeed 0.5f   // half speed
let anim = anim |> Animation3DState.withLoop false    // don't loop
```

## GPU Skinning (AnimatedMesh)

For scenarios with many animated entities sharing the same mesh.

### Loading

```fsharp
let mesh = AnimatedMesh.fromModel model
// Returns ValueNone if model has no bones
```

### Computing Bone Matrices

```fsharp
let clip = clips.Clips[clipIndex]
let bones = AnimatedMesh.computeBoneMatrices clip frame mesh
// Returns Matrix4x4[] — pure math, no model mutation
```

The algorithm matches raylib's `UpdateModelAnimation`:
1. Interpolate keyframes (lerp for translation/scale, slerp for rotation)
2. Build TRS matrices for bind pose and current pose
3. Multiply: `boneMatrices[i] = inverse(bindPose) * currentPose`

### Rendering

```fsharp
buffer |> Draw3D.drawSkinnedMesh mesh.Mesh transform material bones
```

The shader receives bone matrices as a `boneMatrices[128]` uniform and applies skinning on the GPU via `vertexBoneIndices` / `vertexBoneWeights` vertex attributes.

## Integration with MVU

Animation state lives in your Elmish model. Update in a system, apply in the view:

```fsharp
// Types.fs
type GameModel() =
    member val PlayerAnim = Unchecked.defaultof<Animation3DState> with get, set

// Systems.fs
let animationSystem dt model =
    let targetAnim = if not model.IsGrounded then "jump" elif isMoving then "walk" else "idle"
    model.PlayerAnim <- model.PlayerAnim |> Animation3DState.blendTo targetAnim 0.15f |> Animation3DState.update dt
    struct (model, Cmd.none)

// View.fs
Animation3DState.applyToModel model.PlayerAnim
buffer |> Draw3D.drawModel model.PlayerAnim.Model transform
```

## Model Format

Raylib supports **glTF/GLB** and **IQM** for skeletal animation. GLB is recommended — it bundles geometry, textures, and animation data in a single file.

Animations are loaded from the model file via `assets.ModelAnimations`. The animation names come from the file's embedded animation names (e.g., "idle", "walk", "jump" in a Kenney character model).

## Performance Tips

1. **Resolve clip names once at init**: Use `tryGetClipIndex` + `playByIndex` to avoid string lookups in the hot path
2. **Share Animation3DClips**: Create clips once, reuse across all entities using the same model
3. **Tier 2 for many entities**: Use `AnimatedMesh` + `computeBoneMatrices` + `DrawSkinnedMesh` to avoid per-entity model copies
4. **Blend duration**: Keep blend durations short (0.1–0.3s) to minimize double-animation overhead

## See Also

- [Animation (2D)](animation.html)
- [Rendering 3D](graphics3d/overview.html)
- [Materials](graphics3d/materials.html)
