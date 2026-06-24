namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Animation
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
    buffer.Add(Command3D.drawModel model transform)
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

    buffer.Add(Command3D.drawAnimatedModel am.Model transform bones)
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
    buffer.Add(Command3D.drawPrimitive mesh transform material)
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
    buffer.Add(Command3D.drawInstanced mesh transforms material instanceCount)
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
    buffer.Add(Command3D.drawMeshEffect meshPart transform effect)
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
  /// are shaded by <paramref name="effect"/> instead of the default PBR effect. The effect inherits
  /// the gathered scene data (camera matrices, lights, material, bones) — <b>not</b> the PBR shader
  /// itself (v2 spec §3): it need only declare the uniform subset it consumes, and absent uniforms
  /// are skipped. This lets a toon/cel/wireframe effect reuse the scene's camera + lighting without
  /// re-implementing the gather. The scope closes at <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endEffect"/>
  /// or automatically at the next <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.endCamera"/> (scopes do not
  /// persist across cameras).
  /// </summary>
  /// <remarks>
  /// <b>Shadows + lights + animation are inherited by declaration.</b> The scene gather (camera,
  /// lights, the shadow pass output, material, bones) is uploaded to the user effect by name via
  /// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload"/>: an effect that declares the
  /// matching uniforms (e.g. <c>dirLightDir</c>, <c>boneMatrices</c>, <c>shadowViewProjs</c>,
  /// <c>texture5</c>) inherits and samples them; one that declares none of them is unaffected. So a
  /// toon/water scope can opt into shadows and skinned animation simply by declaring those uniforms.
  /// <para>
  /// <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.drawInstanced"/> inside a scope falls back to the PBR
  /// path — hardware instancing needs a per-instance vertex stream a generic inherited effect won't declare.
  /// </para>
  /// </remarks>
  let inline beginEffect (effect: Effect) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.beginEffect effect)
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
  /// Runs a custom immediate draw action.
  /// The pipeline is responsible for ensuring correct state (e.g., exiting any
  /// active camera or shader before invoking the action).
  /// </summary>
  let inline drawImmediate (action: unit -> unit) (buffer: RenderBuffer3D) =
    buffer.Add(Command3D.drawImmediate action)
    buffer

  /// <summary>Terminal function that discards the buffer, silencing the unused-value warning. Does nothing.</summary>
  let inline drop(_buffer: RenderBuffer3D) = ()
