---
title: Camera
category: Rendering
index: 13
---

# Camera (2D + 3D)

Mibo uses a single renderer-agnostic `Camera` type:

```fsharp
type Camera = { View: Matrix; Projection: Matrix }
```

Both the 2D and 3D renderers consume cameras via render commands (`SetCamera`).

## 2D cameras (`Camera2D`)

Use `Camera2D.create` to build an orthographic camera centered on a world position.

```fsharp
let cam = Camera2D.create playerPos 1.5f (Point(1280, 720))

// In a 2D view:
Draw2D.camera cam 0<RenderLayer> buffer
```

Helpers:

- `Camera2D.screenToWorld` / `worldToScreen`
- `Camera2D.viewportBounds` (useful for 2D culling)

## 3D cameras (`Camera3D`)

`Camera3D.lookAt` is the common default:

```fsharp
let cam =
  Camera3D.lookAt
    (Vector3(0f, 10f, 20f))
    Vector3.Zero
    Vector3.Up
    MathHelper.PiOver4
    (16f/9f)
    0.1f
    1000f
```

`Camera3D.orbit` is handy for third-person cameras, inspection views, or editor cameras:

```fsharp
let cam =
  Camera3D.orbit
    Vector3.Zero      // target
    0.0f              // yaw (radians)
    0.35f             // pitch (radians)
    12.0f             // radius
    MathHelper.PiOver4
    (16f/9f)
    0.1f
    1000f
```

If you need a different style (FPS/free-fly/etc.), you can always construct a `Camera` directly by providing your own `View`/`Projection` matrices.

### Custom camera (simple)

This example builds a minimal “FPS-ish” camera from a position plus yaw/pitch:

```fsharp
let customCamera
  (position: Vector3)
  (yaw: float32)
  (pitch: float32)
  (fov: float32)
  (aspect: float32)
  (nearPlane: float32)
  (farPlane: float32)
  : Camera =

  let rot = Matrix.CreateFromYawPitchRoll(yaw, pitch, 0.0f)
  let forward = Vector3.Transform(Vector3.Forward, rot)
  let up = Vector3.Transform(Vector3.Up, rot)

  {
    View = Matrix.CreateLookAt(position, position + forward, up)
    Projection = Matrix.CreatePerspectiveFieldOfView(fov, aspect, nearPlane, farPlane)
  }
```

Picking helper:

- `Camera3D.screenPointToRay` creates a `Ray` you can intersect with `BoundingBox`/`BoundingSphere`.

Frustum helper:

- `Camera3D.boundingFrustum` returns `BoundingFrustum` for visibility checks.

See also: [Culling](culling.html).
