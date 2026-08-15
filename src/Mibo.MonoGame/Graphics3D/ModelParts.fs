namespace Mibo.Elmish.Graphics3D

open System.Runtime.CompilerServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// One drawable slice of a content-pipeline <see cref="T:Microsoft.Xna.Framework.Graphics.Model"/>:
/// a zero-copy wrap of the part's shared vertex/index buffers plus everything a
/// slice draw needs — the part's buffer offsets, its absolute parent-bone
/// transform, and its material.
/// </summary>
/// <remarks>
/// The content pipeline packs a whole model into shared buffers and stores
/// vertices bone-local, so a part can only be drawn by slice (<c>Draw.meshSlice</c> /
/// <c>Draw.instancedSlice</c> with <see cref="F:Mibo.Elmish.Graphics3D.ModelPart.VertexOffset"/> and
/// <see cref="F:Mibo.Elmish.Graphics3D.ModelPart.StartIndex"/>) and its bone must be folded in front
/// of every world/instance transform — stock <c>ModelMesh.Draw</c> does both
/// internally. Resolve with <see cref="M:Mibo.Elmish.Graphics3D.ModelParts.ofModel"/>.
/// <para>
/// <b>Static models only.</b> The instanced draw path carries no bone palette,
/// so skinned parts (baked with <c>SkinnedEffect</c>) render in their bind pose.
/// Use <c>Draw.animatedModelInstanced</c> for skinned models.
/// </para>
/// </remarks>
[<Struct>]
type ModelPart = {

  /// <summary>
  /// Zero-copy wrap of the model's shared buffers. <c>PrimitiveCount</c> is the
  /// part's triangle count and <c>Bounds</c> the mesh's bounding sphere — both
  /// live in the same bone-local space as the stored vertices, so the record is
  /// sized for slice draws and shadow-pass culling without re-derivation.
  /// </summary>
  Mesh: PrimitiveMesh

  /// <summary>
  /// The part's first vertex in the shared vertex buffer — pass as the
  /// <c>vertexOffset</c> (baseVertex) of the slice draw.
  /// </summary>
  VertexOffset: int

  /// <summary>
  /// The part's first index in the shared index buffer — pass as the
  /// <c>startIndex</c> of the slice draw.
  /// </summary>
  StartIndex: int

  /// <summary>
  /// The part's absolute parent-bone transform (<c>CopyAbsoluteBoneTransformsTo</c>).
  /// Multiply it in front of every instance world transform; the stored vertices
  /// are bone-local. <see cref="P:Microsoft.Xna.Framework.Matrix.Identity"/> when the model has no bones.
  /// </summary>
  Bone: Matrix

  /// <summary>
  /// Material read from the part's baked pipeline effect
  /// (<see cref="M:Mibo.Elmish.Graphics3D.Material3D.fromModelMeshPart"/>).
  /// </summary>
  Material: Material3D
}

/// <summary>
/// Resolves content-pipeline models into drawable
/// <see cref="T:Mibo.Elmish.Graphics3D.ModelPart"/> slices.
/// </summary>
module ModelParts =

  let private cache = ConditionalWeakTable<Model, ModelPart[]>()

  /// <summary>
  /// Resolves every mesh part of a content-pipeline model into a zero-copy
  /// <see cref="T:Mibo.Elmish.Graphics3D.ModelPart"/> — shared buffers wrapped, per-part absolute
  /// bone captured, buffer offsets ready for <c>meshSlice</c>/<c>instancedSlice</c>.
  /// Cached per model instance: the <c>ContentManager</c> hands back the same
  /// <c>Model</c> per asset name, so the cache follows the model's lifetime and
  /// never keeps one alive.
  /// <para>
  /// Treat the returned array as <b>read-only</b>: it is the cached result shared
  /// by every caller, and mutating an element (for example swapping
  /// <see cref="F:Mibo.Elmish.Graphics3D.ModelPart.Material"/>) corrupts it for the model's lifetime.
  /// Copy the array (<c>Array.map</c>) when you need adjusted parts.
  /// </para>
  /// <para>
  /// Static models only — see the <see cref="T:Mibo.Elmish.Graphics3D.ModelPart"/> remarks about
  /// skinned parts.
  /// </para>
  /// </summary>
  let ofModel(model: Model) : ModelPart[] =
    cache.GetValue(
      model,
      fun model ->
        let absolute =
          if model.Bones.Count > 0 then
            let transforms = Array.zeroCreate<Matrix> model.Bones.Count
            model.CopyAbsoluteBoneTransformsTo transforms
            transforms
          else
            null

        [|
          for mesh in model.Meshes do
            let bone =
              if isNull absolute then
                Matrix.Identity
              else
                absolute[mesh.ParentBone.Index]

            for part in mesh.MeshParts do
              {
                Mesh = {
                  Vertices = part.VertexBuffer
                  Indices = part.IndexBuffer
                  PrimitiveCount = part.PrimitiveCount
                  Bounds = mesh.BoundingSphere
                }
                VertexOffset = part.VertexOffset
                StartIndex = part.StartIndex
                Bone = bone
                Material = Material3D.fromModelMeshPart part
              }
        |]
    )
