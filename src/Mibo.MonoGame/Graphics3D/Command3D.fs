namespace Mibo.Elmish.Graphics3D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

/// <summary>
/// Camera-facing quad draw payload. Rotation in degrees around the view axis;
/// all-zero SourceRect = full texture.
/// </summary>
[<Struct>]
type Billboard3D = {
  Texture: Texture2D
  Position: Vector3
  Size: Vector2
  Color: Color
  Rotation: float32
  SourceRect: Rectangle
  Blend: BlendMode
}

/// <summary>
/// SoA billboard batch payload. Rotations/SourceRects may be null (= all defaults)
/// and are indexed defensively. The batch is drawn with Textures[0].
/// </summary>
[<Struct>]
type BillboardBatch3D = {
  Textures: Texture2D[]
  Positions: Vector3[]
  Sizes: Vector2[]
  Colors: Color[]
  Rotations: float32[]
  SourceRects: Rectangle[]
  Blend: BlendMode
  Count: int
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics3D.Billboard3D"/>.</summary>
module Billboard3D =

  /// <summary>
  /// Creates a billboard with default framing: no rotation, full-texture source rect,
  /// <see cref="T:Mibo.Elmish.Graphics2D.BlendMode.AlphaBlend"/> blending.
  /// </summary>
  let create
    (texture: Texture2D)
    (position: Vector3)
    (size: Vector2)
    (color: Color)
    : Billboard3D =
    {
      Texture = texture
      Position = position
      Size = size
      Color = color
      Rotation = 0.0f
      SourceRect = Rectangle.Empty
      Blend = BlendMode.AlphaBlend
    }

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
    colors: Color[] voption *
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
  | DrawBillboard of billboard: Billboard3D
  | DrawLine3D of start: Vector3 * finish: Vector3 * color: Color
  | DrawBillboardBatch of batch: BillboardBatch3D
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
  /// A post-process action that reads only color (<c>PostProcessContext3D.Source</c>). Emits no
  /// scene-depth production — use for color-only effects (desaturation, vignette, blur). Cheap:
  /// costs only the scene render target + ping-pong, never a depth pass.
  /// </summary>
  | PostProcess of ppAction: (PostProcessContext3D -> unit)
  /// <summary>
  /// A post-process action that needs camera-POV scene depth (<c>PostProcessContext3D.Depth</c>) in
  /// addition to color — fog, depth-of-field, SSAO. When at least one is present this frame, the
  /// pipeline renders scene depth to an R32F target and exposes it via
  /// <c>PostProcessContext3D.Depth</c>; use plain <see cref="F:Mibo.Elmish.Graphics3D.Command3D.PostProcess"/>
  /// instead when an effect doesn't sample depth, so the depth pass is skipped entirely.
  /// </summary>
  | PostProcessWithDepth of ppAction: (PostProcessContext3D -> unit)
