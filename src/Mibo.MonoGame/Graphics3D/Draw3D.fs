namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

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
/// |> Draw3D.addPointLight { Position = pos; Color = Color.White; Intensity = 1f; Radius = 10f; Falloff = 2f; CastsShadows = false; ShadowBias = ValueNone }
/// |> Draw3D.endCamera
/// |> Draw3D.drop
/// </code>
/// </para>
/// </remarks>
module Draw3D =

  // ──────────────────────────────────────────────
  // Geometry
  // ──────────────────────────────────────────────

  /// <summary>
  /// Draws a static model with a world transform. Auto-PBR + lights + shadows; the model's
  /// baked native effect is read via <c>Material3D.fromModelMeshPart</c> so the model keeps its
  /// authored look when the pipeline swaps to the PBR effect.
  /// </summary>
  let inline drawModel
    (model: Model)
    (transform: Matrix)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.DrawModel(model, transform))
    buffer

  /// <summary>
  /// Draws a static model with a whole-model <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/>
  /// override — every mesh part uses <paramref name="material"/> instead of its authored material.
  /// Auto-PBR + lights + shadows.
  /// </summary>
  let inline modelWith
    (model: Model)
    (transform: Matrix)
    (material: Material3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.DrawModelWith(model, transform, MaterialOverride.All material)
    )

    buffer

  /// <summary>
  /// Draws a static model with a per-mesh-part material resolver. <paramref name="resolver"/>
  /// is indexed by a flat counter over <c>model.Meshes × MeshParts</c> in pipeline iteration order.
  /// Auto-PBR + lights + shadows.
  /// </summary>
  let inline modelWithPerMesh
    (model: Model)
    (transform: Matrix)
    ([<InlineIfLambda>] resolver: int -> Material3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.DrawModelWith(
        model,
        transform,
        MaterialOverride.PerMesh resolver
      )
    )

    buffer

  /// <summary>
  /// Draws an animated model. The 3D analog of the 2D <c>litAnimatedSprite</c>: takes the
  /// runtime state value (<see cref="T:Mibo.Animation.AnimatedModel"/>) + a transform, derives
  /// the bone palette internally from the state, and emits a <c>DrawAnimatedModel</c> command.
  /// Auto-PBR + lights + shadows; skinned parts use the PBR <c>Skinned</c> technique. The caller
  /// never handles a <c>Matrix[]</c> — bone computation happens here, at draw-recording time.
  /// </summary>
  let drawAnimatedModel
    (am: AnimatedModel)
    (transform: Matrix)
    (buffer: RenderBuffer3D)
    =
    let bones =
      match am.Mesh with
      | ValueSome mesh -> Animation3DState.computeBonePalette mesh am.State
      | ValueNone -> [||]

    buffer.Add(Command3D.DrawAnimatedModel(am.Model, transform, bones))
    buffer

  /// <summary>
  /// Draws an animated model with a whole-model <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/>
  /// override. Bone palette is derived internally from the state, same as <c>drawAnimatedModel</c>.
  /// </summary>
  let inline animatedModelWith
    (am: AnimatedModel)
    (transform: Matrix)
    (material: Material3D)
    (buffer: RenderBuffer3D)
    =
    let bones =
      match am.Mesh with
      | ValueSome mesh -> Animation3DState.computeBonePalette mesh am.State
      | ValueNone -> [||]

    buffer.Add(
      Command3D.DrawAnimatedModelWith(
        am.Model,
        transform,
        bones,
        MaterialOverride.All material
      )
    )

    buffer

  /// <summary>
  /// Draws an animated model with a per-mesh-part material resolver (flat index over
  /// <c>model.Meshes × MeshParts</c>). Bone palette is derived internally from the state.
  /// </summary>
  let inline animatedModelWithPerMesh
    (am: AnimatedModel)
    (transform: Matrix)
    ([<InlineIfLambda>] resolver: int -> Material3D)
    (buffer: RenderBuffer3D)
    =
    let bones =
      match am.Mesh with
      | ValueSome mesh -> Animation3DState.computeBonePalette mesh am.State
      | ValueNone -> [||]

    buffer.Add(
      Command3D.DrawAnimatedModelWith(
        am.Model,
        transform,
        bones,
        MaterialOverride.PerMesh resolver
      )
    )

    buffer

  /// <summary>
  /// Draws an effectless <see cref="T:Mibo.Elmish.Graphics3D.PrimitiveMesh"/> with a PBR material.
  /// Auto-PBR + lights + shadows.
  /// </summary>
  let inline drawPrimitive
    (mesh: PrimitiveMesh)
    (transform: Matrix)
    (material: Material3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.DrawPrimitive(mesh, transform, material))
    buffer

  /// <summary>
  /// Draws static instanced bulk (terrain/props) of an effectless
  /// <see cref="T:Mibo.Elmish.Graphics3D.PrimitiveMesh"/>. Auto-PBR + lights + shadows.
  /// Used by <c>CellGridRenderer3D</c>/<c>HexGrid3DRenderer</c> after camera culling.
  /// </summary>
  let inline drawInstanced
    (mesh: PrimitiveMesh)
    (transforms: Matrix[])
    (material: Material3D)
    (instanceCount: int)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.DrawInstanced(
        mesh,
        transforms,
        ValueNone,
        material,
        instanceCount
      )
    )

    buffer

  /// <summary>
  /// Draws a mesh part with a user-supplied <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>
  /// (escape hatch). The pipeline sets <c>World</c> from the transform and <c>View</c>/<c>Projection</c>
  /// from the active camera, applies the effect's current technique pass, and draws the part.
  /// The caller owns lighting and material parameters on the effect.
  /// </summary>
  let inline drawMeshEffect
    (meshPart: ModelMeshPart)
    (transform: Matrix)
    (effect: Effect)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.DrawMeshEffect(meshPart, transform, effect))
    buffer

  /// <summary>Draws a billboard (camera-facing quad) with a texture.</summary>
  let inline drawBillboard
    (texture: Texture2D)
    (position: Vector3)
    (size: Vector2)
    (color: Color)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(
      Command3D.DrawBillboard(Billboard3D.create texture position size color)
    )

    buffer

  /// <summary>Draws a 3D line between two points.</summary>
  let inline drawLine3D
    (start: Vector3)
    (finish: Vector3)
    (color: Color)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.DrawLine3D(start, finish, color))
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
      Command3D.DrawBillboardBatch {
        Textures = textures
        Positions = positions
        Sizes = sizes
        Colors = colors
        Rotations = null
        SourceRects = null
        Blend = BlendMode.AlphaBlend
        Count = count
      }
    )

    buffer

  // ──────────────────────────────────────────────
  // Camera
  // ──────────────────────────────────────────────

  /// <summary>Begins a 3D camera transform.</summary>
  let inline beginCamera (camera: Camera3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.BeginCamera camera)
    buffer

  /// <summary>Begins a 3D camera with explicit rendering config (viewport, clear, post-process).</summary>
  let inline beginCameraWith (config: Camera3DConfig) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.BeginCameraConfig config)
    buffer

  /// <summary>Ends the current 3D camera transform.</summary>
  let inline endCamera(buffer: RenderBuffer3D) =
    buffer.Add Command3D.EndCamera
    buffer

  /// <summary>Sets the shadow origin for this frame's shadow pass.</summary>
  let inline setShadowOrigin (origin: Vector3) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.SetShadowOrigin origin)
    buffer

  /// <summary>Enables shadow casting for subsequent geometry until disabled.</summary>
  let inline enableShadows(buffer: RenderBuffer3D) =
    buffer.Add Command3D.EnableShadows
    buffer

  /// <summary>Disables shadow casting for subsequent geometry until re-enabled.</summary>
  let inline disableShadows(buffer: RenderBuffer3D) =
    buffer.Add Command3D.DisableShadows
    buffer

  // ──────────────────────────────────────────────
  // Per-group shading scopes
  // ──────────────────────────────────────────────

  /// <summary>
  /// Opens a per-group shading scope: draws between this and <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endEffect"/>
  /// are shaded by <paramref name="effect"/> instead of the default PBR effect. The effect inherits
  /// the gathered scene data (camera matrices, lights, material, bones) — <b>not</b> the PBR shader
  /// itself: it need only declare the uniform subset it consumes, and absent uniforms
  /// are skipped. This lets a toon/cel/wireframe effect reuse the scene's camera + lighting without
  /// re-implementing the gather. The scope closes at <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endEffect"/>
  /// or automatically at the next <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endCamera"/> (scopes do not
  /// persist across cameras).
  /// </summary>
  /// <remarks>
  /// <b>Shadows + lights + animation are inherited by declaration.</b> The scene gather (camera,
  /// lights, the shadow pass output, material, bones, and the frame's elapsed <c>time</c>) is uploaded
  /// to the user effect by name via <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload"/>: an
  /// effect that declares the matching uniforms (e.g. <c>dirLightDir</c>, <c>boneMatrices</c>,
  /// <c>shadowViewProjs</c>, <c>shadowAtlas</c>, <c>time</c>) inherits and samples them; one that declares
  /// none of them is unaffected. So a toon/water scope can opt into shadows, skinned animation, and a
  /// shader animation clock simply by declaring those uniforms.
  /// <para>
  /// <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.drawInstanced"/> inside a scope is shaded by the user
  /// effect when it exposes an <c>Instanced</c> technique (the instancing opt-in); an effect that
  /// doesn't falls back to the PBR instanced path. Skinned + instanced draws are not supported. See
  /// <c>docs/graphics3d/instancing.md</c>.
  /// </para>
  /// </remarks>
  let inline beginEffect (effect: Effect) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.BeginEffect effect)
    buffer

  /// <summary>
  /// Closes the shading scope opened by <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.beginEffect"/>;
  /// subsequent draws revert to the default PBR path. No-op if no scope is open.
  /// </summary>
  let inline endEffect(buffer: RenderBuffer3D) =
    buffer.Add Command3D.EndEffect
    buffer

  // ──────────────────────────────────────────────
  // Lighting
  // ──────────────────────────────────────────────

  /// <summary>Sets the ambient light for the scene.</summary>
  let inline setAmbientLight (light: AmbientLight3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.SetAmbientLight light)
    buffer

  /// <summary>Adds a directional light to the scene.</summary>
  let inline addDirectionalLight
    (light: DirectionalLight3D)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.AddDirectionalLight light)
    buffer

  /// <summary>Adds a point light to the scene.</summary>
  let inline addPointLight (light: PointLight3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.AddPointLight light)
    buffer

  /// <summary>Adds a spot light to the scene.</summary>
  let inline addSpotLight (light: SpotLight3D) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.AddSpotLight light)
    buffer

  // ──────────────────────────────────────────────
  // Escape Hatches
  // ──────────────────────────────────────────────

  /// <summary>
  /// Runs a fully-custom draw with raw device access AND the scene data the pipeline gathered this
  /// frame. The callback receives a <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SceneContext"/> — the
  /// graphics device, the active camera (view/projection/config), the accumulated lights, the shadow
  /// pass output, and the elapsed time — so a custom effect (water/refraction, screen-space, multi-pass)
  /// can read the scene without re-implementing the gather. The pipeline restores the viewport + camera
  /// scope around the callback; any other device state you mutate is your responsibility.
  /// </summary>
  /// <param name="action">A callback invoked once with the frame's <c>SceneContext</c>.</param>
  let inline drawImmediate
    ([<InlineIfLambda>] action: Pipelines.SceneContext -> unit)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.DrawImmediate action)
    buffer

  /// <summary>
  /// Enqueues a color-only post-process pass. The action runs once, after the whole scene renders
  /// to an offscreen target; it receives a <see cref="T:Mibo.Elmish.Graphics3D.PostProcessContext3D"/>
  /// carrying the scene texture, a fullscreen quad, and the device. <c>Context.Depth</c> is
  /// <c>ValueNone</c> — no scene-depth pass runs. The action binds its effect, sets model-derived
  /// parameters, binds <c>Source</c> to the sampler, and calls <c>Quad.Draw(effect)</c>. Emit it
  /// conditionally from the view based on game state. Chain multiple passes — they ping-pong in
  /// buffer order, the last drawing to the back-buffer.
  ///
  /// Use this for effects that don't sample depth (desaturation, vignette, blur) so the framework
  /// skips depth production entirely. For distance effects (fog, depth-of-field, SSAO) use
  /// <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.postProcessWithDepth"/>.
  /// </summary>
  /// <param name="action">Invoked once per frame with the post-process context (Depth = ValueNone).</param>
  let inline postProcess
    ([<InlineIfLambda>] action: PostProcessContext3D -> unit)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.PostProcess action)
    buffer

  /// <summary>
  /// Enqueues a post-process pass that needs camera-POV scene depth. Same lifecycle as
  /// <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.postProcess"/> (runs once after the scene renders,
  /// chains in buffer order), but when at least one such action is present the pipeline also renders
  /// scene depth to an R32F target and exposes it via <c>Context.Depth</c>. Sample it (linearize with
  /// the camera's near/far) for distance effects. Prefer plain <c>postProcess</c> for effects that
  /// don't read depth, so the depth pass is skipped.
  /// </summary>
  /// <param name="action">Invoked once per frame with the post-process context (Depth populated).</param>
  let inline postProcessWithDepth
    ([<InlineIfLambda>] action: PostProcessContext3D -> unit)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.PostProcessWithDepth action)
    buffer

  /// <summary>Terminal function that discards the buffer, silencing the unused-value warning. Does nothing.</summary>
  let inline drop(_buffer: RenderBuffer3D) = ()
