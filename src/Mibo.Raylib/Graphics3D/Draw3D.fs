namespace Mibo.Elmish.Graphics3D

open System.Numerics
open Raylib_cs
open Mibo.Elmish

/// <summary>
/// Pipe-friendly drawing DSL for 3D rendering. Each function takes a
/// <see cref="T:Mibo.Elmish.Graphics3D.RenderBuffer3D"/> as its last argument,
/// adds the corresponding command, and returns the buffer for chaining.
/// </summary>
/// <remarks>
/// <para>
/// Commands are built via <see cref="T:Mibo.Elmish.Graphics3D.Command3D"/> and added to the buffer.
/// </para>
/// <para>
/// Usage:
/// <code lang="fsharp">
/// buffer
/// |> Draw3D.beginCamera worldCamera
/// |> Draw3D.drawModel model transform
/// |> Draw3D.addPointLight { Position = pos; Color = Color.White; Intensity = 1f; Radius = 10f; CastsShadows = false; ShadowBias = ValueNone }
/// |> Draw3D.endCamera
/// |> Draw3D.drop
/// </code>
/// </para>
/// </remarks>
module Draw3D =

  // ──────────────────────────────────────────────
  // Geometry
  // ──────────────────────────────────────────────

  /// <summary>Draws a mesh with a world transform and material.</summary>
  let inline drawMesh
    (mesh: Mesh)
    (transform: Matrix4x4)
    (material: Material3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawMesh mesh transform material)
    buffer

  /// <summary>
  /// Draws a raylib model with a world transform.
  /// Each sub-mesh is drawn with its corresponding raylib material,
  /// converted to <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/> automatically.
  /// </summary>
  let inline drawModel
    (model: Model)
    (transform: Matrix4x4)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawModel model transform)
    buffer

  /// <summary>
  /// Draws a raylib model with a whole-model <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/>
  /// override — every sub-mesh uses <paramref name="material"/> instead of its authored material.
  /// </summary>
  let inline modelWith
    (model: Model)
    (transform: Matrix4x4)
    (material: Material3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.drawModelWith model transform (MaterialOverride.All material)
    )

    buffer

  /// <summary>
  /// Draws a raylib model with a per-sub-mesh material resolver. <paramref name="resolver"/>
  /// is indexed by mesh index (<c>0..model.MeshCount-1</c>) in pipeline iteration order.
  /// </summary>
  let inline modelWithPerMesh
    (model: Model)
    (transform: Matrix4x4)
    ([<InlineIfLambda>] resolver: int -> Material3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.drawModelWith
        model
        transform
        (MaterialOverride.PerMesh resolver)
    )

    buffer

  /// <summary>Draws a billboard (camera-facing quad) with a texture.</summary>
  let inline drawBillboard
    (texture: Texture2D)
    (position: Vector3)
    (size: Vector2)
    (color: Color)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawBillboard texture position size color)
    buffer

  /// <summary>Draws a 3D line between two points.</summary>
  let inline drawLine3D
    (start: Vector3)
    (finish: Vector3)
    (color: Color)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawLine3D start finish color)
    buffer

  /// <summary>Draws a skinned mesh with bone matrix data.</summary>
  let inline drawSkinnedMesh
    (mesh: Mesh)
    (transform: Matrix4x4)
    (material: Material3D)
    (bones: Matrix4x4[])
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawSkinnedMesh mesh transform material bones)
    buffer

  /// <summary>
  /// Draws multiple instances of the same mesh with different transforms.
  /// Prefer this over individual <c>drawMesh</c> calls for many copies of the same mesh.
  /// </summary>
  let inline drawMeshInstanced
    (mesh: Mesh)
    (transforms: Matrix4x4[])
    (material: Material3D)
    (instanceCount: int)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.drawMeshInstanced mesh transforms material instanceCount
    )

    buffer

  /// <summary>
  /// Draws multiple billboards in a single batch.
  /// Prefer this over individual <c>drawBillboard</c> calls for many sprites at once.
  /// </summary>
  let inline drawBillboardBatch
    (textures: Texture2D[])
    (positions: Vector3[])
    (sizes: Vector2[])
    (colors: Color[])
    (count: int)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.drawBillboardBatch textures positions sizes colors count
    )

    buffer

  // ──────────────────────────────────────────────
  // Camera
  // ──────────────────────────────────────────────

  /// <summary>Begins a 3D camera transform.</summary>
  let inline beginCamera (camera: Camera3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.beginCamera camera)
    buffer

  /// <summary>Begins a 3D camera with explicit rendering config (viewport, clear, post-process).</summary>
  let inline beginCameraWith (config: Camera3DConfig) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.beginCameraConfig config)
    buffer

  /// <summary>Ends the current 3D camera transform.</summary>
  let inline endCamera(buffer: RenderBuffer3D) =
    buffer.Add(Command3D.endCamera())
    buffer

  /// <summary>Sets the shadow origin for this frame's shadow pass.</summary>
  let inline setShadowOrigin (origin: Vector3) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.setShadowOrigin origin)
    buffer

  /// <summary>Enables shadow casting for subsequent geometry until disabled.</summary>
  let inline enableShadows(buffer: RenderBuffer3D) =
    buffer.Add(Command3D.enableShadows())
    buffer

  /// <summary>Disables shadow casting for subsequent geometry until re-enabled.</summary>
  let inline disableShadows(buffer: RenderBuffer3D) =
    buffer.Add(Command3D.disableShadows())
    buffer

  // ──────────────────────────────────────────────
  // Per-group shading scopes
  // ──────────────────────────────────────────────

  /// <summary>
  /// Opens a per-group shading scope: draws between this and <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endEffect"/>
  /// are shaded by <paramref name="shader"/> instead of the default PBR shader. The shader inherits
  /// the gathered scene data (camera matrices, lights, material, bones, time) — <b>not</b> the PBR
  /// shader itself: it need only declare the uniform subset it consumes, and absent uniforms
  /// are skipped. This lets a toon/cel/wireframe shader reuse the scene's camera + lighting without
  /// re-implementing the gather. The scope closes at <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endEffect"/>
  /// or automatically at the next <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endCamera"/> (scopes do not
  /// persist across cameras).
  /// </summary>
  /// <remarks>
  /// <b>Shadows + lights + animation are inherited by declaration.</b> The scene gather (camera,
  /// lights, the shadow pass output, material, bones, and the frame's elapsed <c>time</c>) is uploaded
  /// to the user shader by name via <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload"/>: a
  /// shader that declares the matching uniforms (e.g. <c>dirLightDir</c>, <c>boneMatrices</c>,
  /// <c>shadowViewProjs</c>, <c>time</c>) inherits and samples them; one that declares none of them
  /// is unaffected. So a toon/water scope can opt into shadows, skinned animation, and a shader
  /// animation clock simply by declaring those uniforms.
  /// <para>
  /// <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.drawMeshInstanced"/> inside a scope is shaded by the
  /// user shader when it declares <c>in mat4 instanceTransform;</c> (the instancing opt-in); a shader
  /// that doesn't declare it falls back to the PBR instanced path. Skinned + instanced draws are not
  /// supported. See <c>docs/graphics3d/instancing.md</c>.
  /// </para>
  /// </remarks>
  let inline beginEffect (shader: Shader) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.beginEffect shader)
    buffer

  /// <summary>
  /// Closes the shading scope opened by <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.beginEffect"/>;
  /// subsequent draws revert to the default PBR path. No-op if no scope is open.
  /// </summary>
  let inline endEffect(buffer: RenderBuffer3D) =
    buffer.Add(Command3D.endEffect())
    buffer

  // ──────────────────────────────────────────────
  // Lighting
  // ──────────────────────────────────────────────

  /// <summary>Sets the ambient light for the scene.</summary>
  let inline setAmbientLight (light: AmbientLight3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.setAmbientLight light)
    buffer

  /// <summary>Adds a directional light to the scene.</summary>
  let inline addDirectionalLight
    (light: DirectionalLight3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.addDirectionalLight light)
    buffer

  /// <summary>Adds a point light to the scene.</summary>
  let inline addPointLight (light: PointLight3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.addPointLight light)
    buffer

  /// <summary>Adds a spot light to the scene.</summary>
  let inline addSpotLight (light: SpotLight3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.addSpotLight light)
    buffer

  // ──────────────────────────────────────────────
  // Escape Hatches
  // ──────────────────────────────────────────────

  /// <summary>
  /// Runs a fully-custom draw with the scene data the pipeline gathered this frame. The callback
  /// receives a <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SceneContext"/> — the active camera
  /// (view/projection/config), the accumulated lights, the shadow pass output, and the elapsed time —
  /// so a custom shader (water/refraction, screen-space, multi-pass) can read the scene without
  /// re-implementing the gather. The pipeline restores the camera scope around the callback; any
  /// other raylib device state you mutate is your responsibility.
  /// </summary>
  /// <param name="action">A callback invoked once with the frame's <c>SceneContext</c>.</param>
  let inline drawImmediate
    ([<InlineIfLambda>] action: Pipelines.SceneContext -> unit)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawImmediate action)
    buffer

  /// <summary>Terminal function that discards the buffer, silencing the unused-value warning. Does nothing.</summary>
  let inline drop(_buffer: RenderBuffer3D) = ()
