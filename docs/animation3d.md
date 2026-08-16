---
title: Animation 3D
category: Amenities
categoryindex: 12
index: 3
---

# Animation 3D (Skeletal Animation)

Mibo provides a three-tier 3D skeletal animation system in `Mibo.Animation`. It supports per-model CPU skinning, shared-mesh GPU skinning, and animation blending.

> _**NOTE — backend differences.**_ The types (`Animation3DClips`, `Animation3DState`,
> `AnimatedMesh`) mirror across backends, but the skinning path differs:
> - **raylib**: CPU skinning via `Raylib.UpdateModelAnimation` / `UpdateModelAnimationEx`
>   (mutates the model's bone matrices); render with `.model(...)` (per-entity model copy)
>   or `.skinnedMesh(...)` (shared mesh + bone matrices). Wrapping the state in an
>   `AnimatedModel` record and rendering with `.animatedModel(...)` selects the opt-in
>   GPU path instead — no model mutation, several poses per model per frame (see
>   [Tier 3](#tier-3--per-model-cpu-skinning-animation3dstate)).
> - **MonoGame**: clips load from the raw model file via Assimp (`assets.ModelAnimations` →
> `Animation3DClips`); render with `.animatedModel(animModel, transform)` (the bone
> palette is derived internally from an `AnimatedModel` state value — the caller never handles
> a `Matrix[]`).

## Core Types

| Type | Purpose |
| ---- | ------- |
| `Animation3DClips` | Shared clip set loaded from `ModelAnimation[]` — name/index lookup |
| `Animation3DState` | Per-entity playback state (current frame, blend, speed, loop) |
| `AnimatedMesh` | Shared mesh + inverse bind pose for GPU skinning |
| `BoneRef` | Addresses a bone by name (`ByName`) or index (`ByIndex`) for queries/attachments |
| `BonePose` | One evaluated pose: per-bone world transforms + shader skinning palette |

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

// 4. Render (in your view) — the witness applies the pose and draws
buffer
  .animatedModel(anim, transform)
  .drop()
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
// load once, share — ValueNone when the model has no bones
match AnimatedMesh.fromModel model with
| ValueNone -> ()
| ValueSome mesh ->
    // Per-entity (lightweight — just matrix math)
    let bones = AnimatedMesh.computeBoneMatrices clip frame mesh

    // Render — GPU does the skinning (raylib)
    buffer
      .skinnedMesh(mesh.Mesh, transform, material, bones)
      .drop()
```

> **Merged clips need the pose path.** `computeBoneMatrices` samples a raw
> `ModelAnimation` by bone index and does not apply the bone-order remaps from
> `Animation3DClips.merge` — playing a merged cross-file clip through this
> Tier-2 path drives the wrong bones (mirrored limbs). When clips come from
> several files, use `Animation3DState.computePose` / `AnimatedModel` (the
> remap-aware path) instead.

### Tier 3 — Per-Model CPU Skinning (`Animation3DState`)

Simplest API. Each entity owns its own model state. `.animatedModel(...)` applies the pose for you:

```fsharp
let anim = Animation3DState.create model clips "idle" 60.0f
let anim = anim |> Animation3DState.update dt

buffer
  .animatedModel(anim, transform)
  .drop()
```

> _**NOTE — raylib has two Tier-3 paths.**_ Passing a bare `Animation3DState` to
> `.animatedModel(...)` is the **legacy mutating path**: it applies the pose to the
> embedded model via `UpdateModelAnimation`, so the model holds one pose at a time
> (last writer wins), and any `pose` argument is ignored. Wrapping shared mesh data
> plus the state in the `AnimatedModel` record selects the **opt-in GPU path**:
> drawing emits one skinned-mesh command per sub-mesh carrying a per-instance bone
> palette — no model mutation, so the same model can be drawn with several
> different poses in one frame, and the `pose` parameter is honored (see
> [Bone Poses, Queries, and Attachments](#bone-poses-queries-and-attachments)).
>
> ```fsharp
> match AnimatedMesh.fromModel model with
> | ValueSome mesh ->
>     let am = AnimatedModel.create mesh anim   // anim: Animation3DState
>     buffer.animatedModel(am, transform).drop()
> | ValueNone -> ()
> ```
>
> On the GPU path the `transform` argument is the full world transform (raylib's
> internal `model.Transform` is not composed in, matching every other mesh draw).
> The GPU path supports all three material forms — `animatedModel`,
> `animatedModelWith` (whole-model override), and `animatedModelWithPerMesh`
> (per-mesh resolver). MonoGame's `.animatedModel(...)` never mutates — it
> always works like the GPU path.

### When to Use Which

| Scenario | Tier | Why |
|----------|------|-----|
| 1–5 animated characters | Tier 3 | Simple, no shader changes |
| Several poses of the same model per frame | Tier 3, raylib `AnimatedModel` | GPU path — no mutation, per-instance palettes |
| 10+ animated enemies | Tier 2 | Share mesh, GPU skinning |
| Hundreds of units (RTS) | Tier 2 + `animatedModelInstanced` | Skinned + instanced draws: one draw call per sub-mesh for N instances, each with its own pose |

## Skinned + Instanced Draws

`animatedModelInstanced` draws many instances of the same animated model in one
draw call (per sub-mesh), each instance with its own world transform **and its
own pose**. It exists on both backends and mirrors the `instanced` member's
shape, with poses taking the place of a single shared skeleton state:

```fsharp
// Per frame: one transform + one pose per instance
let transforms = units |> Array.map (fun u -> u.Transform)
let poses = units |> Array.map (fun u -> AnimatedModel.computePose u.Model)

buffer
  .animatedModelInstanced(sharedModel, transforms, poses)
  .drop()
```

- `poses` carries one caller-evaluated `BonePose` per instance — compute each
  unit's pose once and share it with bone queries / attachment draws for that
  unit (see above). The instance count is `min(transforms.Length, poses.Length)`.
  Each pose's palette must cover the model's bones — a shorter palette raises
  an `ArgumentException`; extra entries beyond the bone count are ignored.
- `?material` takes a `MaterialOverride` (`All` for a whole-model override,
  `PerMesh` for a per-sub-mesh resolver), like the model draw members.
- `?colors` tints each instance — **MonoGame only**, like `instanced`.
- Per-instance bone palettes ride a texture the vertex shader samples (raylib
  indexes it by `gl_InstanceID`), so draws are chunked at 2048 instances.
  On DX12 (no vertex texture fetch) palettes ride a per-group constant array
  instead, so draws are chunked into groups of `448 / boneCount` instances in
  the forward pass (`500 / boneCount` in the shadow pass — the depth effect
  carries no lighting uniforms, so it affords larger groups) — a model with
  more than 448 bones falls back to per-instance skinned draws
  there (identical output, no batching win).
- **Automatic sub-mesh merging (MonoGame):** mesh parts that share a parent
  bone, vertex layout, and material draw as one merged part per chunk instead
  of one draw each — on DX12, where palette groups are small, this is a large
  draw-count win (a 6-part character merges 6× fewer draws per group). The
  merged geometry is built lazily on a model's first instanced draw and costs
  one extra copy of the model's vertex/index data. A command whose materials
  split a group (e.g. a `PerMesh` resolver returning different materials)
  falls back to per-part draws for that command, so output is never affected.

> **OpenGL backend note:** MonoGame's OpenGL profile cannot sample textures in
> the vertex shader, so there `animatedModelInstanced` falls back to
> per-instance skinned draws — identical output, but no batching win. This
> includes Android, whose MonoGame backend is GL-based. DesktopGL remains fully
> supported for moderate counts; plan crowd-heavy scenes for the DX12/Vulkan or
> raylib backends.

### Reusing pose buffers (high instance counts)

For 500+ instances, evaluate poses into pre-allocated buffers to avoid
per-frame garbage. Use `computePoseInto`, which writes into a caller-owned
`BonePose` (grown on first use, reused every frame after):

```fsharp
// Init once: poses pre-filled with BonePose.empty
let poses = Array.init count (fun _ -> BonePose.empty)

// Per frame: zero allocation after the first frame
for i = 0 to count - 1 do
    poses[i] <- Animation3DState.computePoseInto(animMesh, states[i], poses[i])

buffer
  .animatedModelInstanced(sharedModel, transforms, poses)
  .drop()
```

`computePoseInto` takes the existing `BonePose` by value and **returns** it —
assign the result back: the returned struct carries the (possibly regrown)
buffers, so discarding it loses the reuse the call is meant to give you.

For moderate counts (< 500) the simpler `Array.map (fun u -> computePose ...)`
pattern is fine — the garbage is small and collected cheaply.

> _**Sample.**_ The [AnimatedInstancing sample](https://github.com/AngelMunoz/Mibo.Samples)
> in the Mibo.Samples repo is a working crowd probe built on this API — it runs
> 500–10,000 animated instances with per-instance poses on raylib and MonoGame
> (DX12/DesktopGL), and shows both the plain `computePose` path and the
> `computePoseInto` buffer-reuse path at tier scale.

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

### Loading clips from multiple files (raylib)

Asset packs sometimes split a skeleton's clips across files — for example KayKit ships movement clips in `Rig_Medium_MovementBasic.glb` and general clips in `Rig_Medium_General.glb`. Concatenating two `ModelAnimation[]` arrays by hand is **not safe** on raylib: keyframe poses are index-based and clips carry no bone names, so a clip from a file whose skeleton orders the same bones differently (right-side joints first vs left-side first) drives the wrong bones — typically seen as mirrored limbs.

Use `Animation3DClips.merge`, which pairs each file's clips with that file's skeleton bone order and remaps by bone name:

```fsharp
let movementAnims = assets.ModelAnimations "Rig_Medium_MovementBasic.glb"
let generalAnims = assets.ModelAnimations "Rig_Medium_General.glb"

// Bone orders come from each file's model skeleton
let movementBones = Animation3DClips.boneNamesOf movementModel
let generalBones = Animation3DClips.boneNamesOf generalModel

let clips =
    Animation3DClips.merge movementBones [|
        movementBones, movementAnims   // first entry = the skeleton being animated
        generalBones, generalAnims
    |]
```

Clips whose file already follows the target order are sampled directly; the remap costs nothing at runtime. Bones a clip doesn't animate hold their bind pose (matching the MonoGame backend). The remap is honored only by the pose path (`Animation3DState.computePose` / `AnimatedModel`) — the legacy mutating path (`UpdateModelAnimation` via a bare `Animation3DState`) and the Tier-2 path (`AnimatedMesh.computeBoneMatrices` + `skinnedMesh`) sample by raw bone index and cannot remap; they require clips from the same file as the model.

MonoGame is unaffected: its clip channels are keyed by bone name, so clips from differently-ordered files resolve correctly without a remap.

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

Advances the current frame (and blend target frame if blending). Respects `Loop` and `Speed` settings.

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
buffer
  .skinnedMesh(mesh.Mesh, transform, material, bones)
  .drop()
```

The shader receives bone matrices as a `boneMatrices[128]` uniform and applies skinning on the GPU via `vertexBoneIndices` / `vertexBoneWeights` vertex attributes.

> _**NOTE — works with the stock raylib native library.**_ raylib uploads those
> bone vertex attributes only when natively compiled with `SUPPORT_GPU_SKINNING`,
> which is off by default — including the builds shipped by the raylib-cs NuGet
> package — leaving skinned meshes stuck in bind pose. Mibo detects the missing
> buffers and uploads them from managed code when you call
> `AnimatedMesh.fromModel` or `Animation3DState.create`, so GPU skinning works
> out of the box regardless of how the native library was built.

## Bone Poses, Queries, and Attachments

Both backends can evaluate an animated model's pose **once per instance per frame** and share the result between the skinned draw, bone queries, and attachment draws. The shared value is a `BonePose`:

| Field | Contents |
| ----- | -------- |
| `WorldPoses` | Model-space bone transform for the current frame, per bone — the query/attachment data |
| `Palette` | Shader skinning palette: `InverseBindPose[i] * WorldPoses[i]`, per bone |

### Evaluating a pose

```fsharp
// MonoGame — ValueNone when the model has no skeleton
let pose: BonePose voption = AnimatedModel.computePose animModel

// raylib (AnimatedModel path) — the record's Mesh is non-optional,
// so this returns a plain BonePose
let pose: BonePose = AnimatedModel.computePose animModel

// Lower level, both backends
let pose = Animation3DState.computePose mesh state
```

The caller owns the value — there is no per-frame caching on the animation state. Compute it once and pass it to everything that needs the pose this frame via the optional `pose` parameter:

```fsharp
match AnimatedModel.computePose model.PlayerAnim with
| ValueSome pose ->
    buffer
      .animatedModel(model.PlayerAnim, playerTransform, pose = pose)
      .attachedMesh(
          model.PlayerAnim, BoneRef.ByName "Hand_R", gripOffset,
          swordMesh, swordMaterial, playerTransform, pose = pose)
      .drop()
| ValueNone -> ()
```

When `pose` is omitted, the witness computes the pose internally exactly as before. On raylib, `pose` is honored by the `AnimatedModel` (GPU path) witnesses and ignored by the legacy `Animation3DState` (mutating path) witnesses.

> _**NOTE — MonoGame example above.**_ On raylib the same chain works when
> `model.PlayerAnim` is the `AnimatedModel` record — skip the `match` and bind
> the pose directly, since raylib's `AnimatedModel.computePose` returns a plain
> `BonePose` (the record's mesh is non-optional).

### Bone queries

`BoneRef` addresses a bone — `ByName` is the authoring-friendly path (resolved through the mesh's name→index lookup, retained on `AnimatedMesh` at load time), `ByIndex` is the fast path (no lookup):

```fsharp
// One-off query — recomputes the pose on every call
let hand: Matrix voption =
    AnimatedModel.tryGetBoneWorld (BoneRef.ByName "Hand_R") animModel

// Shared pose — query as many bones as you like from one evaluation
let hand = BonePose.tryGetWorld "Hand_R" mesh pose   // by name
let root = BonePose.worldAt 0 pose                   // by index, bounds-checked
```

For hot loops, resolve the name once with `AnimatedMesh.tryFindBoneIndex` and switch to `ByIndex`.

All query results are **model-space** world transforms — row-vector convention on both backends, consumed as-is (no transpose or inverse-bind recovery anywhere in the query path). Compose with the instance transform yourself when you need world space — on raylib use `Raymath.*` ops (`Raymath.MatrixMultiply`), the same as every other transform on that backend.

### Attachment draws

`buffer.attachedMesh` draws a static mesh parented to a bone of an animated model — swords in hands, hats on heads, muzzle-flash anchors:

```fsharp
buffer.attachedMesh(animModel, bone, localTransform, mesh, material, transform, pose = pose)
```

The attachment's world transform is **`localTransform * boneWorld * transform`** (row-vector composition): it inherits the instance's full world transform including scale, and `localTransform` is your grip offset/rotation/scale relative to the bone. The draw lowers to the existing plain-mesh command (`DrawPrimitive` on MonoGame, `DrawMesh` on raylib) — no new command types, so attachments get the same lighting/shadow treatment as any other mesh.

> _**MonoGame vertex-space caveat.**_ The attachment mesh's vertices must be in model-root space. Mesh parts extracted from a content-pipeline `Model` are **bone-local** — bake the part's absolute bone transform (`model.CopyAbsoluteBoneTransformsTo` / the part's entry in `mesh.AbsoluteBoneTransforms`) into `localTransform`, or the prop renders offset from the bone. The Platformer3D sample shows the pattern.
>
> _**Tip.**_ An unknown bone name is a silent no-op (below), so a typo fails invisibly. Validate attachment bone names once at load time with `AnimatedMesh.tryFindBoneIndex`.

### Missing bones are never an error

- Queries (`AnimatedModel.tryGetBoneWorld`, `BonePose.worldAt`/`tryGetWorld`) return `ValueNone` for an unknown name or out-of-range index.
- Attachment draws emit **no command** (a silent no-op) for a missing bone.

### One evaluation per frame

A pose evaluation allocates the bone-length arrays and walks every bone — cheap, but not free. When a frame needs the pose in more than one place (the skinned draw plus attachments, or several bone queries), compute it once with `computePose` and pass the value around. `AnimatedModel.tryGetBoneWorld` recomputes the pose on every call — fine for a one-off query, the wrong shape for multi-query frames.

### Attachments are per-instance draws

Skinned + instanced draws are not supported (there is no per-instance bone palette), so an animated model with attachments costs one skinned draw plus one plain-mesh draw per attachment, per instance.

## Integration with MVU

Animation state lives in your Elmish model. Update in a system, draw in the view:

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
buffer
  .animatedModel(model.PlayerAnim, playerTransform)
  .drop()
```

## Model Format

Skeletal animation models are typically **glTF/GLB** (recommended — it bundles geometry, textures, and animation data in a single file). raylib also supports **IQM**; MonoGame loads animations from the raw file via Assimp at runtime (the content pipeline does not preserve animation data in `.xnb`).

Animations are loaded from the model file via `assets.ModelAnimations`. The animation names come from the file's embedded animation names (e.g., "idle", "walk", "jump" in a Kenney character model).

## Performance Tips

1. **Resolve clip names once at init**: Use `tryGetClipIndex` + `playByIndex` to avoid string lookups in the hot path
2. **Share Animation3DClips**: Create clips once, reuse across all entities using the same model
3. **Tier 2 for many entities**: Share a single mesh and avoid per-entity model copies — use `AnimatedMesh` + `computeBoneMatrices` + `.skinnedMesh(...)` (raylib), or the shared-mesh path with `.animatedModel(...)` (MonoGame)
4. **Blend duration**: Keep blend durations short (0.1–0.3s) to minimize double-animation overhead
5. **One pose evaluation per frame**: When drawing attachments or querying bones, compute the `BonePose` once and pass it as `pose` to `animatedModel`/`attachedMesh` instead of letting each witness re-evaluate it
6. **`computePoseInto` for crowds**: For 500+ instanced characters, use `computePoseInto` with pre-allocated `BonePose` buffers (see [Reusing pose buffers](#reusing-pose-buffers-high-instance-counts)) — it eliminates per-frame array allocation

## See Also

- [Animation (2D)](animation.html)
- [Rendering 3D](graphics3d/overview.html)
- [Materials](graphics3d/materials.html)
