namespace Mibo.Elmish.Next.Graphics3D

open Mibo.Elmish.Next.Graphics2D.Base

open System.Numerics
open Mibo.Elmish.Next.Graphics2D

// ─────────────────────────────────────────────────────────────────
// Core Command3D
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Closed set of backend-neutral 3D render commands.
/// Stored in a <see cref="T:Mibo.Elmish.Next.Graphics3D.RenderBuffer3DBase"/> and
/// dispatched via pattern matching — no interface boxing.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type Command3D =
  | DrawMesh of mesh: int<Mesh> * transform: Matrix4x4 * material: MaterialData
  | DrawModel of model: int<ModelAsset> * transform: Matrix4x4
  | DrawBillboard of
    texture: int<Texture> *
    position: Vector3 *
    size: Vector2 *
    color: Color
  | DrawLine3D of start: Vector3 * finish: Vector3 * color: Color
  | DrawSkinnedMesh of
    mesh: int<Mesh> *
    transform: Matrix4x4 *
    material: MaterialData *
    bones: Matrix4x4[]
  | DrawMeshInstanced of
    mesh: int<Mesh> *
    transforms: Matrix4x4[] *
    material: MaterialData *
    instanceCount: int
  | DrawBillboardBatch of
    textures: int<Texture>[] *
    positions: Vector3[] *
    sizes: Vector2[] *
    colors: Color[] *
    count: int
  | BeginCamera of camera: Camera
  | BeginCameraConfig of config: Camera3DConfig
  | EndCamera
  | SetShadowOrigin of origin: Vector3
  | SetAmbientLight of aLight: AmbientLight3DData
  | AddDirectionalLight of AddDlight: DirectionalLight3DData
  | AddPointLight of AddPlight: PointLight3DData
  | AddSpotLight of AddSlight: SpotLight3DData
  | EnableShadows
  | DisableShadows
  | DrawImmediate of action: (unit -> unit)
