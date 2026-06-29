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
  | DrawBillboard of
    texture: Texture2D *
    position: Vector3 *
    size: Vector2 *
    color: Color
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
  | AddDirectionalLight of AddDlight: DirectionalLight3D
  | AddPointLight of AddPlight: PointLight3D
  | AddSpotLight of AddSlight: SpotLight3D
  | EnableShadows
  | DisableShadows
  | BeginEffect of shader: Shader
  | EndEffect
  | DrawImmediate of action: (Pipelines.SceneContext -> unit)

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
    Command3D.DrawBillboard(texture, position, size, color)

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
    Command3D.DrawBillboardBatch(textures, positions, sizes, colors, count)

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
