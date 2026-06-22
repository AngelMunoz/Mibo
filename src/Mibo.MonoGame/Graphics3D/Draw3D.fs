namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
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

  /// <summary>Draws a mesh part with a world transform, binding the part's own native effect.</summary>
  let inline drawMesh
    (meshPart: ModelMeshPart)
    (transform: Matrix)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawMesh meshPart transform)
    buffer

  /// <summary>
  /// Draws a mesh part with a world transform using a user-supplied <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>.
  /// The pipeline sets <c>World</c> from the transform and <c>View</c>/<c>Projection</c> from
  /// the active camera, applies the effect's current technique pass, and draws the part.
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

  /// <summary>
  /// Draws a MonoGame model with a world transform.
  /// Each sub-mesh is drawn with its own native effect (e.g. <c>BasicEffect</c>),
  /// which the pipeline configures with the active camera and lights.
  /// </summary>
  let inline drawModel
    (model: Model)
    (transform: Matrix)
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawModel model transform)
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

  /// <summary>Draws a skinned mesh part with bone matrix data, binding the part's own native effect.</summary>
  let inline drawSkinnedMesh
    (meshPart: ModelMeshPart)
    (transform: Matrix)
    (bones: Matrix[])
    (buffer: RenderBuffer3D)
    =
    buffer.Add(Command3D.drawSkinnedMesh meshPart transform bones)
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
