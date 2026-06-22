namespace Mibo.Elmish.Graphics3D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

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
  | DrawMesh of
    meshPart: ModelMeshPart *
    transform: Matrix *
    material: Material3D
  | DrawModel of model: Model * transform: Matrix
  | DrawBillboard of
    texture: Texture2D *
    position: Vector3 *
    size: Vector2 *
    color: Color
  | DrawLine3D of start: Vector3 * finish: Vector3 * color: Color
  | DrawSkinnedMesh of
    meshPart: ModelMeshPart *
    transform: Matrix *
    material: Material3D *
    bones: Matrix[]
  | DrawMeshInstanced of
    meshPart: ModelMeshPart *
    transforms: Matrix[] *
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
  | AddDirectionalLight of dLight: DirectionalLight3D
  | AddPointLight of pLight: PointLight3D
  | AddSpotLight of sLight: SpotLight3D
  | EnableShadows
  | DisableShadows
  | DrawImmediate of action: (unit -> unit)

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
    (meshPart: ModelMeshPart)
    (transform: Matrix)
    (material: Material3D)
    =
    Command3D.DrawMesh(meshPart, transform, material)

  let inline drawModel (model: Model) (transform: Matrix) =
    Command3D.DrawModel(model, transform)

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
    (meshPart: ModelMeshPart)
    (transform: Matrix)
    (material: Material3D)
    (bones: Matrix[])
    =
    Command3D.DrawSkinnedMesh(meshPart, transform, material, bones)

  let inline drawMeshInstanced
    (meshPart: ModelMeshPart)
    (transforms: Matrix[])
    (material: Material3D)
    (instanceCount: int)
    =
    Command3D.DrawMeshInstanced(meshPart, transforms, material, instanceCount)

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

  let inline drawImmediate(action: unit -> unit) =
    Command3D.DrawImmediate(action)
