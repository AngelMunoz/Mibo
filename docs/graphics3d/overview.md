---
title: 3D Rendering Overview
category: 3D Rendering
categoryindex: 5
index: 12
---

# 3D Rendering

The 3D rendering pipeline is a **deferred command system** with a pluggable `IRenderPipeline3D`. Each frame, your view function populates a `RenderBuffer3D` with commands, and the pipeline executes them. The architecture is identical on both backends; only the pipeline class name and the shader language differ.

## What and Why

The 3D renderer provides:

- **Deferred commands** — Describe what to draw without worrying about GPU state. The pipeline handles shader binding, pass order, and lighting.
- **Pluggable pipelines** — Swap the rendering pipeline without changing view code. Each backend ships a built-in forward pipeline with Cook-Torrance **PBR** materials, a shadow **atlas**, and post-processing:
  - **raylib:** `ForwardPbrPipeline` (GLSL shaders)
  - **MonoGame:** `ForwardPipeline` (HLSL `.fx` → `.mgfx`, compiled for DirectX 11 and OpenGL)
- **3D lighting** — Ambient, directional, point, and spot lights with shadow mapping.
- **Instanced rendering** — One draw call for many copies of the same geometry (`.instanced(...)`), plus batched billboards.
- **Custom shading opt-in** — `.beginEffect(...)`/`.endEffect()` scopes (a user shader/effect that inherits the gathered scene data), `.drawImmediate(...)` for raw access, and MonoGame's per-mesh-part effect draw.
- **Camera configs** — `Camera3DConfig` with viewport, clear color, and post-process control.

## Quick start

````fsharp
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

// raylib backend:                    // MonoGame backend:
let pipeline = ForwardPbrPipeline()   // let pipeline = ForwardPipeline()

Program.mkProgram init update
|> Program.withRenderer (fun () -> Renderer3D.create pipeline view)
````

Your view function receives a `RenderBuffer3D` and chains members of the fluent Draw DSL (see [Draw DSL](../draw-dsl.html)):

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
    buffer
      .beginCamera(worldCamera)
      .setAmbientLight { Color = Color(40, 40, 40); Intensity = 1f }
      .addDirectionalLight {
        Direction = Vector3(0.3f, -0.7f, 0.2f)
        Color = Color.White
        Intensity = 0.8f
        CastsShadows = true
        ShadowBias = ValueNone
      }
      .model(playerModel, playerTransform)
      .endCamera()
      .drop()
```

## Geometry commands

One member set covers both backends — the buffer takes your backend's own mesh/model/material types, and the transform type (`System.Numerics.Matrix4x4` on raylib, `Microsoft.Xna.Framework.Matrix` on MonoGame):

| Member | What it draws |
|--------|---------------|
| `.model(model, transform)` | A loaded model with its authored materials |
| `.modelWith(model, transform, material)` | Model with a whole-model material override |
| `.modelWithPerMesh(model, transform, resolver)` | Model with a per-mesh-part material resolver |
| `.mesh(mesh, transform, material)` | Single primitive mesh (raylib `Mesh` / MonoGame `PrimitiveMesh`) |
| `.instanced(mesh, transforms, material, count)` | Many copies of one mesh in one draw call |
| `.animatedModel(animModel, transform)` | Skeletal animation (bone palette derived for you) |
| `.skinnedMesh(mesh, transform, material, bones)` | Explicit bone palette (**raylib only**) |
| `.billboard(tex, position, size, color)` | Camera-facing quad |
| `.billboardBatch(textures, positions, sizes, colors, count)` | Batched billboards |
| `.line3D(start, finish, color)` | Debug line |

## Lighting

3D lighting supports four light types, identical across backends (the structs live in
`Mibo.Elmish.Graphics3D`). Add them before geometry inside a camera scope:

```fsharp
buffer
  .setAmbientLight { Color = Color(30, 30, 30); Intensity = 1f }
  .addDirectionalLight {
    Direction = Vector3(0f, -1f, 0f)
    Color = Color.White; Intensity = 0.8f
    CastsShadows = true
  }
  .addPointLight {
    Position = Vector3(5f, 3f, 0f)
    Color = Color.Orange; Intensity = 1f
    Radius = 10f; Falloff = 2f
    CastsShadows = false; ShadowBias = ValueNone
  }
  .addSpotLight {
    Position = Vector3(0f, 5f, 0f)
    Direction = Vector3(0f, -1f, 0f)
    Color = Color.White; Intensity = 1f
    Radius = 15f; InnerCutoff = 0.5f; OuterCutoff = 0.7f
    CastsShadows = true; ShadowBias = ValueNone
  }
  .drop()
```

See [Lighting](lighting.html) for the light-type fields and shadow configuration.

## Shadow control

Enable or disable shadow casting per-section:

```fsharp
buffer
  .enableShadows()
  .model(groundModel, groundTransform)   // casts shadows
  .disableShadows()
  .model(skyboxModel, skyboxTransform)   // no shadows
  .drop()
```

## Post-processing

After the scene renders to an offscreen target, screen-space shader passes run in
buffer order — each receives the previous pass's output as its source texture and
draws a fullscreen quad. The last pass writes to the back-buffer; intermediate
passes ping-pong through pooled render targets.

Two entry points:

| Member | Depth available? | When to use |
|--------|-----------------|-------------|
| `.postProcess(action)` | No (`Context.Depth = ValueNone`) | Color-only effects: desaturation, vignette, tone mapping, blur |
| `.postProcessWithDepth(action)` | Yes (`Context.Depth = ValueSome`) | Distance effects: fog, depth-of-field, SSAO |

Use plain `postProcess` when you don't sample depth — the pipeline skips the
depth-production cost entirely. Emit passes conditionally from the view (e.g. only
while a hit-flash is active).

```fsharp
// Color-only: desaturate the scene while a hit-flash is active
if isHitFlash model then
    buffer
      .postProcess(fun ctx ->
        // ctx.Source is the scene render target (or the previous pass's output)
        // Draw a fullscreen quad of it with your shader...
        drawFullscreenQuad ctx.Source myShader)
      .drop()
```

### Depth-aware post-processing

`.postProcessWithDepth(...)` gives the action a `PostProcessContext3D` whose `Depth`
field is a camera-POV depth texture. Sample it and linearize with the camera's
near/far planes to get view-space distance:

```fsharp
buffer
  .postProcessWithDepth(fun ctx ->
    // ctx.Source — the scene color (Texture2D / RenderTarget2D)
    // ctx.Depth — camera-POV depth (ValueSome Texture2D, NDC z in [0,1])
    // ctx.Width, ctx.Height — dimensions
    // Always handle the ValueNone case: it means depth wasn't produced this frame.
    match ctx.Depth with
    | ValueSome depthTex -> // bind depthTex, apply distance effect
    | ValueNone ->          // no depth — draw the scene through unchanged
    ())
  .drop()
```

### Depth texture contract

The depth texture follows the same convention on both backends:

| Property | Value |
|----------|-------|
| **Format** | Single-channel depth (NDC z) |
| **Range** | `[0.0, 1.0]` — `0.0` = near plane, `1.0` = far plane |
| **Distribution** | Non-linear (hyperbolic) — perspective-projected NDC z |
| **Skybox / uncovered pixels** | `1.0` (far) — cleared before geometry renders |
| **Linearization** | The effect's responsibility (see formula below) |

To convert NDC z back to view-space distance, invert the perspective projection:

```glsl
// GLSL — linearize depth to positive view-space distance
float z = depth * 2.0 - 1.0;   // remap [0,1] → NDC [-1,1]
float dist = (2.0 * near * far) / (far + near - z * (far - near));
```

```hlsl
// HLSL — equivalent for MonoGame
float ndcZ = tex2D(DepthSampler, texCoord).r;
float dist = (far * near) / (far - ndcZ * (far - near));
```

> _**NOTE — near/far source differs by backend.**_ raylib renders 3D with global
> clip planes (`Rlgl.GetCullDistanceNear()` / `GetCullDistanceFar()`, defaults
> `0.05`/`4000`), not per-camera near/far — query them at runtime and pass to your
> shader. MonoGame uses the camera's `NearPlane`/`FarPlane`. The linearization
> formula is the same; only the near/far *source* differs.

### How the backends produce depth

The depth texture is produced differently under the hood, but the contract above is
identical:

- **raylib:** the scene renders into a custom framebuffer whose depth attachment is
  a sampleable texture (not a renderbuffer). OpenGL's depth buffer is directly
  sampleable, so no extra geometry pass is needed — the depth you get is the same
  depth buffer the forward pass wrote.
- **MonoGame:** DirectX/OpenGL depth-stencil buffers are not directly sampleable as
  textures in MonoGame's API, so the pipeline re-renders all opaque geometry into a
  dedicated R32F color render target (cleared to white = far) via a depth-only
  shader. This is an extra geometry pass, but produces the same NDC z values.

### Post-process shader requirements

For the contract your shader must satisfy (sampler names, texture binding, the
`SetShaderValueTexture` caveat on raylib), see
[Shaders → Post-process shaders](../shaders.html#post-process-shaders).

## Multi-camera rendering

Use `Camera3DConfig` for split-screen, minimaps, or layered rendering:

```fsharp
let mainConfig = Camera3D.render mainCamera |> Camera3D.withClear Color.SkyBlue
let minimapConfig =
    Camera3D.render topDownCamera
    |> Camera3D.withViewport(Rectangle(0.75f, 0f, 0.25f, 0.25f))
    |> Camera3D.withClear Color.Black

buffer
  .beginCameraWith(mainConfig)
  // ... main scene ...
  .endCamera()
  .beginCameraWith(minimapConfig)
  // ... minimap ...
  .endCamera()
  .drop()
```

> _**NOTE — viewport coordinates differ by backend.**_ On raylib, `Camera3DConfig.Viewport`
> is in **normalized** screen coordinates (0–1, as above). On MonoGame it is a **pixel**
> `Rectangle` (matching `GraphicsDevice.Viewport`). The `Camera3D.splitScreen*` helpers
> produce backend-appropriate rectangles; for a picture-in-picture view, compose
> `render` + `withViewport` + `withClear` yourself and emit that camera after the main one.

> _**NOTE — lights and shadows are scoped per camera block.**_ A view that sets no
> lights (like the minimap above) inherits the running set, so same-world multi-view
> works with no extra setup. A view that sets its own lights starts from the frame
> defaults instead — useful when the views show different worlds. See
> [Buffers & Commands → Light scoping](buffer-and-commands.html#light-scoping-across-camera-blocks).

See [Camera](../camera.html) for the full `Camera3DConfig` API.

## 2D overlay on 3D

Combine 3D and 2D renderers for HUD overlays:

```fsharp
Program.mkProgram init update
|> Program.withRenderer (fun () ->
    Renderer3D.createWith { ClearColor = ValueSome Color.Black } pipeline view3D)
|> Program.withRenderer (fun () ->
    Renderer2D.createWith { ClearColor = ValueNone } view2D)
```

The 2D renderer clears with `ValueNone` to preserve the 3D scene underneath.

## Escape hatches

Each backend exposes a way to run custom GPU work outside the deferred command buffer:

**raylib** — `.drawImmediate(...)` runs raw rlgl/raylib calls (the batch is flushed and state restored):

```fsharp
buffer
  .drawImmediate(fun () ->
    Raylib.DrawCube(Vector3.Zero, 1f, 1f, 1f, Color.Red))
  .drop()
```

**MonoGame** — two options:
- `.beginEffect(...)` / `.endEffect()` open a **shading scope**: draws inside are shaded by a user
  `Effect` that *inherits* the gathered scene data (camera matrices, lights, the shadow pass
  output, material, bones, frame time) — you only declare the uniforms your effect consumes
  (e.g. `dirLightDir`, `boneMatrices`, `shadowViewProjs`, `time`). Ideal for toon/cel/wireframe
  without re-implementing the scene gather. The scope closes at `.endEffect()` or the next
  `.endCamera()`.

  ```fsharp
  buffer
    .beginCamera(camera)
    .beginEffect(toonEffect)
    .model(model, transform)
    .endEffect()
    .endCamera()
    .drop()
  ```

- A per-mesh-part effect draw (the pipeline only sets World/View/Projection; the caller owns
  all lighting/material parameters), and `.drawImmediate(...)` (the callback receives a
  `SceneContext` with the graphics device + gathered scene data) for raw device access.

See [Shaders](../shaders.html) for loading custom shaders/effects per backend, and the
[Shader Uniform Reference](../shader-uniforms.html) for the exact uniform names the
`beginEffect` scope uploads (declare only what your shader consumes).

## See also

- [Draw DSL](../draw-dsl.html) — the full fluent draw surface (2D and 3D)
- [Camera](../camera.html) — Camera3D helpers, Camera3DConfig, multi-camera patterns
- [Shaders](../shaders.html) — Custom shader loading and parameters
- [Rendering Overview](../rendering.html) — 2D + 3D pipeline architecture
