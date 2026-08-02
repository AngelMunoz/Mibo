---
title: Migrating to Mibo v4
category: Migrating
categoryindex: 2
index: 3
---

# Migrating to Mibo v4

This page collects the **breaking changes** between the last v3 release (`3.3.0`)
and v4 (`4.0.0`), with the exact steps to update your code. Work through the
sections that match your code — most games are affected by none of them.

> _**The short answer for most games:** upgrade the package, recompile, done.
> Every source-level break is in 3D skeletal-animation internals or in
> multi-camera-block lighting. If your game draws animated models through
> `buffer.animatedModel(...)` and uses a single camera, v4 is a drop-in
> recompile._

The headline features of v4 — bone pose queries and attachment draws, skinned +
instanced draws, and one shared `BonePose` evaluation per frame — are additive
and need no migration. See [Animation 3D](animation3d.html).

## 1. Recompile against the new assemblies (binary break)

**Who is affected:** everyone with pre-compiled assemblies referencing Mibo.

`buffer.animatedModel` / `animatedModelWith` / `animatedModelWithPerMesh` gained
an optional `pose` parameter, which changes their compiled (IL) signature.
Existing **source** compiles unchanged — but assemblies built against v3 must be
recompiled. A plain `dotnet build` of your game is enough; there is nothing to
change in code.

## 2. Animation types are now struct records

**Who is affected:** code that constructs `AnimatedMesh` literally, or relies on
reference identity of the animation types.

`Animation3DChannel`, `Animation3DClip`, `Animation3DClips`, and `AnimatedMesh`
(MonoGame), and `AnimatedMesh` (raylib) changed from reference records to
`[<Struct>]` records. Consequences:

- Equality and copying are now **value semantics** — two copies compare equal
  when their contents match, and assigning one copies it. Code that relied on
  reference identity needs review.
- **raylib only (source break):** `AnimatedMesh` gains a
  `BindPose: Transform[]` field (the model-space rest pose, used as the fallback
  for bones a clip doesn't animate). Record literals must add the field:

```fsharp
// v3 — no longer compiles
let mesh = { Mesh = m; InverseBindPose = ibp; BoneNames = names; ... }

// v4 — add BindPose, or (better) let the loader build the record
let mesh = { Mesh = m; InverseBindPose = ibp; BoneNames = names
             BindPose = bindPose; ... }

// recommended — populated for you, no literal construction
match AnimatedMesh.fromModel model with
| ValueSome mesh -> ...
| ValueNone -> ...
```

## 3. raylib: bone palettes are plain row-major now

**Who is affected:** raylib code that builds its own bone palettes and feeds
them to `buffer.skinnedMesh(...)` / `DrawSkinnedMesh`.

**Symptom after upgrading:** skinned meshes render distorted or garbled.

`AnimatedMesh.computeBoneMatrices` now returns the palette in plain
System.Numerics row-major layout (`result[i] = InverseBindPose[i] * pose[i]`)
instead of pre-transposed into raylib's native layout. The framework transposes
at upload time where the shader contract needs it.

**What to do:** drop any manual pre-transpose around palettes you pass in.
Palettes produced by `computeBoneMatrices` or a computed `BonePose` are
unchanged and render the same as before.

## 4. Lights and shadows are scoped per camera block

**Who is affected:** frames with **more than one camera block** (split-screen,
minimaps, rear-view mirrors). Single-camera frames are unchanged.

**Symptom after upgrading:** lights "leak" differently between views — a view
that set its own lights no longer also gets the lights emitted before or after
it, and each view renders its own shadow map.

The new rules, on both backends:

- Lights, the shadow origin, and shadow casting are scoped **per camera block**.
- A block that sets its own lights starts from the **frame defaults** (lights
  emitted before the first camera block or between blocks) and applies them in
  order; a block that sets none inherits the running set.
- Each camera block renders **its own shadow map** — a multi-block frame costs
  one shadow pass per block.

**What to do:** emit the lights every view shares **before the first camera
block**, and per-view lights **inside** that view's block. If you relied on
lights accumulating across blocks, move the shared ones to the frame defaults.

## 5. Only the first directional light is shaded

**Who is affected:** scenes with more than one directional light.

**Symptom after upgrading:** the second (and later) directional lights no longer
contribute any light, and a non-casting first light no longer "borrows" a later
casting light's shadow map.

On both backends, only the **first** directional light is shaded, and only it
can cast shadows. Previously a frame whose first directional light didn't cast
could still be shadowed by a later casting light's map, and a casting light
could render a shadow map nothing sampled.

**What to do:** merge your directional lights into one sun (combine color and
intensity), and make sure the shadow-casting directional light is the first one
emitted.

## 6. Deprecation warnings on the piped draw modules (not breaking)

After upgrading, code using the piped draw modules — `Draw`, `Draw3D`,
`LightDraw`, `ParticleDraw` — builds with warning **FS0044** pointing at the
fluent draw DSL. The modules still work and will not be removed before a future
major release, so you can migrate at your own pace, file by file:

```fsharp
// piped (deprecated — still works)
buffer |> Draw3D.drawModel model transform |> Draw3D.drop

// fluent (recommended)
buffer.model(model, transform).drop()
```

The full mapping, including lighting, particles, and grid rendering, is in
[Draw DSL → Migrating from the piped DSL](draw-dsl.html#migrating-from-the-piped-dsl).
To silence the warnings until you migrate, add FS0044 to your project's
`NoWarn` — but prefer migrating, since the modules will be removed in a future
release.

## See also

- [Draw DSL](draw-dsl.html) — the fluent draw surface for 2D and 3D
- [Animation 3D](animation3d.html) — bone poses, queries, attachments, skinned instancing
- [Migrating to Mibo v2](migration-to-v2.html) — if you are coming from 1.x
- [Changelog](https://github.com/AngelMunoz/Mibo/blob/main/CHANGELOG.md) — full release notes
