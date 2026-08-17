---
title: Culling
category: Rendering
categoryindex: 9
index: 4
---

# Culling (visibility helpers)

`Mibo.Elmish.Culling` is a helper module that keeps _visibility math_ separate from your renderer and your spatial partitioning (the data structure that organizes objects by position).

It operates on geometric primitives:

- A view frustum (built from a camera or light View×Projection matrix; the frustum is the pyramid-shaped volume the camera sees)
- A bounding sphere / bounding box to test against it
- 2D rectangle overlap

## 3D: frustum culling

Build a frustum from a View×Projection matrix and test geometry against it. The frustum type is backend-specific: raylib ships its own `Frustum` (it has no native one), while MonoGame uses its native `BoundingFrustum`:

```fsharp
// raylib: Mibo.Elmish.Frustum over System.Numerics.Matrix4x4
let frustum = Frustum(viewProjection)

// MonoGame: Microsoft.Xna.Framework.BoundingFrustum
let frustum = BoundingFrustum(viewProjection)
```

```fsharp
if Culling.isVisible frustum entitySphere then
    // submit draw commands
    ()
```

Or for axis-aligned bounding boxes:

```fsharp
if Culling.isVisibleBox frustum nodeBounds then
    ()
```

> _**Where does the View×Projection matrix come from?**_ On MonoGame, the camera
> struct carries it directly: `BoundingFrustum(cam.View * cam.Projection)`. On
> raylib, capture it inside `BeginMode3D`
> (`Rlgl.GetMatrixModelview() * Rlgl.GetMatrixProjection()`), or build it from
> `Raylib.GetCameraMatrix3D(camera)` and a perspective matrix.

## 2D: rectangle overlap

Use `Camera2D.viewportBounds` with `Culling.isVisible2D` (the rectangle type is
the backend's native one: float `Rectangle` on raylib, int `Rectangle` on
MonoGame):

```fsharp
// raylib: viewportBounds takes the camera by reference (&)
let viewBounds = Camera2D.viewportBounds &camera viewportWidth viewportHeight

// MonoGame: immutable camera, passed by value
let viewBounds = Camera2D.viewportBounds camera viewportWidth viewportHeight

if Culling.isVisible2D viewBounds spriteBounds then
    ()
```

## What this is _not_

This module doesn't try to be your spatial index.

- If you have many objects: use a grid / quadtree / <abbr title="bounding volume hierarchy: a tree of nested bounding volumes for fast broad-phase tests">BVH</abbr> / octree.
- Use these helpers at the edge: "is this node/object worth considering for rendering?"

See also: [Camera](camera.html) and [Rendering overview](rendering.html).
