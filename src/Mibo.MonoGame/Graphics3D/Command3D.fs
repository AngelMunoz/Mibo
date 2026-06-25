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
  | DrawModel of model: Model * transform: Matrix
  | DrawAnimatedModel of model: Model * transform: Matrix * bones: Matrix[]
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
