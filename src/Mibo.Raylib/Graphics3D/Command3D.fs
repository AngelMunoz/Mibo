#nowarn "9"

namespace Mibo.Elmish.Graphics3D

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish

/// <summary>
/// Optional material override for <see cref="M:Mibo.Elmish.Graphics3D.Command3D.DrawModelWith"/>.
/// <para><c>All</c> applies one <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/> to every sub-mesh
/// (allocation-free struct field). <c>PerMesh</c> takes a resolver indexed by the pipeline's sub-mesh
/// iteration order — mesh index <c>0..model.MeshCount-1</c> on the raylib backend.</para>
/// </summary>
[<Struct>]
type MaterialOverride =
  | All of material: Material3D
  | PerMesh of resolver: (int -> Material3D)

/// <summary>
/// Camera-facing quad draw payload. Rotation in degrees around the view axis;
/// all-zero <c>SourceRect</c> = full texture.
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

/// <summary>Factory functions for <see cref="T:Mibo.Elmish.Graphics3D.Billboard3D"/>.</summary>
module Billboard3D =

  /// <summary>
  /// Creates a <see cref="T:Mibo.Elmish.Graphics3D.Billboard3D"/> with defaults:
  /// <c>Rotation = 0</c>, all-zero <c>SourceRect</c> (full texture), <c>Blend = BlendMode.Alpha</c>.
  /// </summary>
  let inline create
    (texture: Texture2D)
    (position: Vector3)
    (size: Vector2)
    (color: Color)
    =
    {
      Texture = texture
      Position = position
      Size = size
      Color = color
      Rotation = 0f
      SourceRect = Rectangle(0f, 0f, 0f, 0f)
      Blend = BlendMode.Alpha
    }

/// <summary>
/// SoA billboard batch payload. <c>Rotations</c>/<c>SourceRects</c> may be null (= all defaults)
/// and are indexed defensively.
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

/// <summary>
/// Closed set of 3D render commands. Stored in <see cref="T:Mibo.Elmish.Graphics3D.RenderBuffer3D"/>
/// and dispatched via pattern matching — no interface boxing.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type Command3D =
  | DrawMesh of mesh: Mesh * transform: Matrix4x4 * material: Material3D
  | DrawModel of model: Model * transform: Matrix4x4
  | DrawModelWith of
    model: Model *
    transform: Matrix4x4 *
    matOverride: MaterialOverride
  | DrawBillboard of billboard: Billboard3D
  | DrawLine3D of start: Vector3 * finish: Vector3 * color: Color
  | DrawSkinnedMesh of
    mesh: Mesh *
    transform: Matrix4x4 *
    material: Material3D *
    bones: Matrix4x4[]
  | DrawMeshInstanced of
    mesh: Mesh *
    transforms: Matrix4x4[] *
    material: Material3D *
    instanceCount: int
  /// <summary>
  /// Skinned + instanced: one draw call for <paramref name="instanceCount"/> instances
  /// of the same skinned mesh, each with its own pose. <paramref name="palettes"/> is
  /// the flat per-instance bone palettes (<c>instanceCount * boneCount</c>,
  /// instance-major); the shader indexes it by <c>gl_InstanceID</c> on the palette
  /// texture.
  /// </summary>
  | DrawSkinnedMeshInstanced of
    mesh: Mesh *
    transforms: Matrix4x4[] *
    palettes: Matrix4x4[] *
    material: Material3D *
    instanceCount: int
  | DrawBillboardBatch of batch: BillboardBatch3D
  | BeginCamera of camera: Camera3D
  | BeginCameraConfig of config: Camera3DConfig
  | EndCamera
  | SetShadowOrigin of origin: Vector3
  | SetAmbientLight of aLight: AmbientLight3D
  | AddDirectionalLight of AddDlight: DirectionalLight3D
  | AddPointLight of AddPlight: PointLight3D
  | AddSpotLight of AddSlight: SpotLight3D
  | EnableShadows
  | DisableShadows
  | BeginEffect of shader: Shader
  | EndEffect
  | DrawImmediate of action: (Pipelines.SceneContext -> unit)
  /// <summary>
  /// A post-process action that reads only color (<c>PostProcessContext3D.Source</c>). Emits no
  /// scene-depth production — use for color-only effects (desaturation, vignette, blur). Cheap:
  /// costs only the scene render target + ping-pong.
  /// </summary>
  | PostProcess of ppAction: (PostProcessContext3D -> unit)
  /// <summary>
  /// A post-process action that needs camera-POV scene depth (<c>PostProcessContext3D.Depth</c>) in
  /// addition to color — fog, depth-of-field, SSAO. The pipeline exposes the scene render target's
  /// depth attachment (OpenGL's depth buffer is directly sampleable, so no extra geometry pass is
  /// needed). Use plain <see cref="F:Mibo.Elmish.Graphics3D.Command3D.PostProcess"/> when an effect
  /// doesn't sample depth.
  /// </summary>
  | PostProcessWithDepth of ppAction: (PostProcessContext3D -> unit)

/// <summary>
/// Factory functions that create <see cref="T:Mibo.Elmish.Graphics3D.Command3D"/> values
/// for all 3D drawing operations.
/// </summary>
/// <remarks>
/// Each function returns a command that can be added to a <see cref="T:Mibo.Elmish.Graphics3D.RenderBuffer3D"/>.
/// Commands are stored as a closed DU for zero-allocation use in the hot path.
/// </remarks>
module Command3D =

  let inline drawMesh
    (mesh: Mesh)
    (transform: Matrix4x4)
    (material: Material3D)
    =
    Command3D.DrawMesh(mesh, transform, material)

  let inline drawModel (model: Model) (transform: Matrix4x4) =
    Command3D.DrawModel(model, transform)

  let inline drawModelWith
    (model: Model)
    (transform: Matrix4x4)
    (matOverride: MaterialOverride)
    =
    Command3D.DrawModelWith(model, transform, matOverride)

  let inline drawBillboard
    (texture: Texture2D)
    (position: Vector3)
    (size: Vector2)
    (color: Color)
    =
    Command3D.DrawBillboard(Billboard3D.create texture position size color)

  let inline drawLine3D (start: Vector3) (finish: Vector3) (color: Color) =
    Command3D.DrawLine3D(start, finish, color)

  let inline drawSkinnedMesh
    (mesh: Mesh)
    (transform: Matrix4x4)
    (material: Material3D)
    (bones: Matrix4x4[])
    =
    Command3D.DrawSkinnedMesh(mesh, transform, material, bones)

  let inline drawMeshInstanced
    (mesh: Mesh)
    (transforms: Matrix4x4[])
    (material: Material3D)
    (instanceCount: int)
    =
    Command3D.DrawMeshInstanced(mesh, transforms, material, instanceCount)

  let inline drawBillboardBatch
    (textures: Texture2D[])
    (positions: Vector3[])
    (sizes: Vector2[])
    (colors: Color[])
    (count: int)
    =
    Command3D.DrawBillboardBatch {
      Textures = textures
      Positions = positions
      Sizes = sizes
      Colors = colors
      Rotations = null
      SourceRects = null
      Blend = BlendMode.Alpha
      Count = count
    }

  let inline beginCamera(camera: Camera3D) = Command3D.BeginCamera(camera)

  let inline beginCameraConfig(config: Camera3DConfig) =
    Command3D.BeginCameraConfig(config)

  let inline endCamera() = Command3D.EndCamera

  let inline setShadowOrigin(origin: Vector3) =
    Command3D.SetShadowOrigin(origin)

  let inline setAmbientLight(light: AmbientLight3D) =
    Command3D.SetAmbientLight(light)

  let inline addDirectionalLight(light: DirectionalLight3D) =
    Command3D.AddDirectionalLight(light)

  let inline addPointLight(light: PointLight3D) = Command3D.AddPointLight(light)
  let inline addSpotLight(light: SpotLight3D) = Command3D.AddSpotLight(light)

  let inline enableShadows() = Command3D.EnableShadows
  let inline disableShadows() = Command3D.DisableShadows

  let inline beginEffect(shader: Shader) = Command3D.BeginEffect(shader)
  let inline endEffect() = Command3D.EndEffect

  let inline drawImmediate(action: Pipelines.SceneContext -> unit) =
    Command3D.DrawImmediate(action)

  /// <summary>
  /// Creates a model-aware post-process pass. The action runs once per frame, after the
  /// whole scene renders to an offscreen target; it receives a <see cref="T:Mibo.Elmish.Graphics3D.PostProcessContext3D"/>
  /// with the scene texture (+ optional depth) and must draw a fullscreen quad of it.
  /// </summary>
  let inline postProcess(action: PostProcessContext3D -> unit) =
    Command3D.PostProcess(action)
