---
title: Culling
category: Rendering
index: 14
---

# Culling (visibility helpers)

`Mibo.Elmish.Culling` is a tiny helper module that keeps _visibility math_ separate from your renderer and your spatial partitioning.

It operates on MonoGame primitives:

- `BoundingFrustum`
- `BoundingSphere`
- `BoundingBox`
- `Rectangle`

## 3D: frustum culling

```fsharp
let frustum = Camera3D.boundingFrustum camera

if Culling.isVisible frustum entitySphere then
  // submit draw commands
  ()
```

Or for AABBs:

```fsharp
if Culling.isGenericVisible frustum nodeBounds then
  ()
```

## 2D: rectangle overlap

This is typically paired with `Camera2D.viewportBounds`:

```fsharp
let viewBounds = Camera2D.viewportBounds camera viewport

if Culling.isVisible2D viewBounds spriteBounds then
  ()
```

## What this is _not_

This module doesn’t try to be your spatial index.

- If you have many objects: use a grid / quadtree / BVH / octree.
- Use these helpers at the edge: “is this node/object worth considering for rendering?”
