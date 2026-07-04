namespace Mibo.Elmish.Graphics3D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

/// <summary>
/// Optional material override for <see cref="M:Mibo.Elmish.Graphics3D.Command3D.DrawModelWith"/> /
/// <see cref="M:Mibo.Elmish.Graphics3D.Command3D.DrawAnimatedModelWith"/>.
/// <para><c>All</c> applies one <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/> to every mesh part
/// (allocation-free struct field). <c>PerMesh</c> takes a resolver indexed by a flat counter over
/// <c>model.Meshes × MeshParts</c> in pipeline iteration order.</para>
/// </summary>
[<Struct>]
type MaterialOverride =
  | All of material: Material3D
  | PerMesh of resolver: (int -> Material3D)

/// <summary>
/// Closed set of 3D render commands. Stored in <see cref="T:Mibo.Elmish.Graphics3D.RenderBuffer3D"/>
/// and dispatched via pattern matching — no interface boxing.
/// </summary>
/// <remarks>
/// Fields use native MonoGame types (<see cref="T:Microsoft.Xna.Framework.Graphics.Model"/>,
/// <see cref="T:Microsoft.Xna.Framework.Graphics.ModelMeshPart"/>, <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>,
/// <see cref="T:Microsoft.Xna.Framework.Graphics.Texture2D"/>, etc.) for zero-copy interop with the MonoGame pipeline.
/// </remarks>
[<RequireQualifiedAccess; Struct>]
type Command3D =
  | DrawModel of model: Model * transform: Matrix
  | DrawAnimatedModel of model: Model * transform: Matrix * bones: Matrix[]
  | DrawModelWith of
    model: Model *
    transform: Matrix *
    matOverride: MaterialOverride
  | DrawAnimatedModelWith of
    model: Model *
    transform: Matrix *
    bones: Matrix[] *
    matOverride: MaterialOverride
  | DrawInstanced of
    mesh: PrimitiveMesh *
    transforms: Matrix[] *
    material: Material3D *
    instanceCount: int
  | DrawPrimitive of
    mesh: PrimitiveMesh *
    transform: Matrix *
    material: Material3D
  | DrawMeshEffect of
    meshPart: ModelMeshPart *
    transform: Matrix *
    effect: Effect
  | DrawBillboard of
    texture: Texture2D *
    position: Vector3 *
    size: Vector2 *
    color: Color
  | DrawLine3D of start: Vector3 * finish: Vector3 * color: Color
  | DrawBillboardBatch of
    textures: Texture2D[] *
    positions: Vector3[] *
    sizes: Vector2[] *
    colors: Color[] *
    count: int
  | BeginCamera of camera: Camera3D
  | BeginCameraConfig of config: Camera3DConfig
  | EndCamera
  | SetShadowOrigin of origin: Vector3
  | SetAmbientLight of aLight: AmbientLight3D
  | AddDirectionalLight of dLight: DirectionalLight3D
  | AddPointLight of pLight: PointLight3D
  | AddSpotLight of sLight: SpotLight3D
  | EnableShadows
  | DisableShadows
  | BeginEffect of effect: Effect
  | EndEffect
  | DrawImmediate of action: (Pipelines.SceneContext -> unit)
  /// <summary>
  /// Requests a camera-POV linear-depth pre-pass this frame. When present (anywhere in the
  /// buffer), the pipeline renders the opaque scene to an R32F target and exposes it as
  /// <see cref="F:Mibo.Elmish.Graphics3D.PostProcessContext3D.Depth"/> to every
  /// <see cref="F:Mibo.Elmish.Graphics3D.Command3D.PostProcess"/> action that frame. Emit it when a
  /// post-process effect needs distance (fog, depth-of-field, SSAO); omit it for effects that
  /// don't (e.g. a desaturation hit-flash) so the extra geometry pass is skipped. Depth is only
  /// populated when at least one <c>EnableDepthPrePass</c> command is present; otherwise
  /// <c>PostProcessContext3D.Depth</c> is <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueNone"/>.
  /// </summary>
  | EnableDepthPrePass
  | PostProcess of ppAction: (PostProcessContext3D -> unit)
