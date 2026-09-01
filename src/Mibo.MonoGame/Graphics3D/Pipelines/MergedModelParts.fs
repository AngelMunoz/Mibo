namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// MergedModelParts — automatic mesh-part merging for skinned + instanced draws.
//
// A skinned + instanced command draws per mesh part per palette chunk/group;
// on DX12 (small uniform groups) that multiplication dominates the frame
// (hundreds of groups × parts × 2 passes). Models whose parts share the same
// render state — same parent-bone world transform, vertex layout, and
// material — can draw as ONE part per chunk instead. This module builds that
// merged geometry lazily per Model and caches it (ConditionalWeakTable —
// models stay collectible).
//
// Correctness for arbitrary games: the static grouping below is only a
// heuristic to decide which merged buffers are worth building. The forward
// pass re-validates per command (all source parts of a merged group must
// resolve to the same MaterialKey) and falls back to per-part draws when a
// MaterialOverride splits a group — a game with all-distinct materials gets
// the unmerged behavior plus one array scan. The depth-only shadow pass binds
// no material state, so it uses merged geometry unconditionally.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>GPU-identity-free description of a mesh part for the pure merge planner
/// (unit-testable without a GraphicsDevice). The *Id fields are caller-assigned
/// ordinals standing in for identity values (parent-bone world transform, vertex
/// declaration, texture).</summary>
[<Struct>]
type internal MergePartDesc = {
  TransformId: int
  DeclarationId: int
  TextureId: int
  IsSkinned: bool
  VertexCount: int
  IndexCount: int
  SourceNeeds32Bit: bool
}

/// <summary>A merge decision: the source part indices (pipeline iteration order) that
/// can share one draw, and whether the merged index buffer needs 32-bit indices
/// (any 32-bit source, or a combined vertex count past the 16-bit range).</summary>
[<Struct>]
type internal MergeGroup = { PartIndices: int[]; Needs32Bit: bool }

/// <summary>One merged mesh part: the concatenated vertex/index buffers of its
/// <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.MergedPart.SourceParts"/>, always
/// drawn with VertexOffset = 0 / StartIndex = 0. <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.MergedPart.SourceParts"/>
/// is retained (in pipeline iteration order) so the forward pass can re-validate
/// per-command material uniformity against the resolved per-part materials.</summary>
[<Struct>]
type internal MergedPart = {
  VertexBuffer: VertexBuffer
  IndexBuffer: IndexBuffer
  PrimitiveCount: int
  ParentBoneIndex: int
  IsSkinned: bool
  SourceParts: ModelMeshPart[]
}

module internal MergedModelParts =

  /// <summary>Pure merge planner: groups part indices that share
  /// (parent-bone world transform, vertex declaration, texture, skinned flag) into
  /// mergeable groups. Only groups with more than one member are returned, in stable
  /// first-appearance order; a model whose parts all differ returns an empty array
  /// (zero overhead).</summary>
  let planMerge(parts: MergePartDesc[]) : MergeGroup[] =
    let groups = Dictionary<struct (int * int * int * bool), ResizeArray<int>>()

    let order = ResizeArray<struct (int * int * int * bool)>()

    for i = 0 to parts.Length - 1 do
      let p = parts[i]

      let key =
        struct (p.TransformId, p.DeclarationId, p.TextureId, p.IsSkinned)

      match Dictionary.tryGetValue key groups with
      | ValueSome bucket -> bucket.Add i
      | ValueNone ->
        let bucket = ResizeArray<int>()
        bucket.Add i
        groups[key] <- bucket
        order.Add key

    let result = ResizeArray<MergeGroup>()

    for key in order do
      let bucket = groups[key]

      if bucket.Count > 1 then
        let mutable totalVerts = 0
        let mutable needs32 = false

        for i in bucket do
          totalVerts <- totalVerts + parts[i].VertexCount
          needs32 <- needs32 || parts[i].SourceNeeds32Bit

        result.Add {
          PartIndices = bucket.ToArray()
          Needs32Bit = needs32 || totalVerts > 65535
        }

    result.ToArray()

  /// <summary>Builds the merged geometry for <paramref name="model"/>, or null when no
  /// parts merge (cached by <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.MergedModelParts.tryGet"/>).
  /// Vertex data is copied per part slice (<c>VertexOffset * stride</c>, <c>NumVertices * stride</c>)
  /// into one static vertex buffer per group; indices are rebased by the accumulated
  /// vertex offset and widened to 32-bit when the group needs it.</summary>
  let build(gd: GraphicsDevice, model: Model) : MergedPart[] =
    // Flatten parts in pipeline iteration order, assigning identity ordinals.
    // Grouping is by parent-bone WORLD TRANSFORM (not bone index): skinned parts
    // hang off different skeleton nodes whose bind transforms are usually equal
    // (typically identity), and equal worlds compose identically with the skinned
    // vertex position — so they can share one matModel and one draw.
    let boneWorlds = Array.zeroCreate<Matrix> model.Bones.Count
    model.CopyAbsoluteBoneTransformsTo boneWorlds

    let transformIds = Dictionary<Matrix, int>()
    let declarations = Dictionary<VertexDeclaration, int>()
    let textures = Dictionary<Texture2D, int>()
    let flatParts = ResizeArray<ModelMeshPart>()
    let flatParentBones = ResizeArray<int>()
    let descs = ResizeArray<MergePartDesc>()

    for mesh in model.Meshes do
      let boneWorld = boneWorlds[mesh.ParentBone.Index]

      let transformId =
        match Dictionary.tryGetValue boneWorld transformIds with
        | ValueSome id -> id
        | ValueNone ->
          let id = transformIds.Count
          transformIds[boneWorld] <- id
          id

      for part in mesh.MeshParts do
        let declaration = part.VertexBuffer.VertexDeclaration

        let declId =
          match Dictionary.tryGetValue declaration declarations with
          | ValueSome id -> id
          | ValueNone ->
            let id = declarations.Count
            declarations[declaration] <- id
            id

        let texture =
          match part.Effect with
          | :? SkinnedEffect as e -> e.Texture
          | :? BasicEffect as e -> e.Texture
          | _ -> null

        let texId =
          if isNull texture then
            0
          else
            match Dictionary.tryGetValue texture textures with
            | ValueSome id -> id
            | ValueNone ->
              let id = textures.Count + 1 // 0 reserved for "no known texture"
              textures[texture] <- id
              id

        flatParts.Add part
        flatParentBones.Add mesh.ParentBone.Index

        descs.Add {
          TransformId = transformId
          DeclarationId = declId
          TextureId = texId
          IsSkinned = part.Effect :? SkinnedEffect
          VertexCount = part.NumVertices
          IndexCount = part.PrimitiveCount * 3
          SourceNeeds32Bit =
            part.IndexBuffer.IndexElementSize = IndexElementSize.ThirtyTwoBits
        }

    let groups = planMerge(descs.ToArray())

    if groups.Length = 0 then
      null
    else
      let merged = ResizeArray<MergedPart>()

      for group in groups do
        let first = flatParts[group.PartIndices[0]]
        let declaration = first.VertexBuffer.VertexDeclaration
        let stride = declaration.VertexStride

        let mutable totalVerts = 0
        let mutable totalIndices = 0

        for i in group.PartIndices do
          totalVerts <- totalVerts + descs[i].VertexCount
          totalIndices <- totalIndices + descs[i].IndexCount

        // Vertices: concatenate each part's slice, in group order.
        let vb = new VertexBuffer(gd, declaration, totalVerts, BufferUsage.None)

        let vertexBytes = Array.zeroCreate<byte>(totalVerts * stride)
        let mutable vertOffset = 0

        for i in group.PartIndices do
          let part = flatParts[i]
          let byteCount = descs[i].VertexCount * stride

          part.VertexBuffer.GetData<byte>(
            part.VertexOffset * stride,
            vertexBytes,
            vertOffset,
            byteCount
          )

          vertOffset <- vertOffset + byteCount

        vb.SetData<byte> vertexBytes

        // Indices: rebase by the accumulated vertex offset, widening to 32-bit
        // when the group needs it.
        let ib =
          new IndexBuffer(
            gd,
            (if group.Needs32Bit then
               IndexElementSize.ThirtyTwoBits
             else
               IndexElementSize.SixteenBits),
            totalIndices,
            BufferUsage.None
          )

        let mutable baseVertex = 0
        let mutable indexOffset = 0

        if group.Needs32Bit then
          let indices = Array.zeroCreate<int> totalIndices

          for i in group.PartIndices do
            let part = flatParts[i]
            let indexCount = descs[i].IndexCount

            if descs[i].SourceNeeds32Bit then
              part.IndexBuffer.GetData<int>(
                part.StartIndex * 4,
                indices,
                indexOffset,
                indexCount
              )

              for j = indexOffset to indexOffset + indexCount - 1 do
                indices[j] <- indices[j] + baseVertex
            else
              let narrow = Array.zeroCreate<uint16> indexCount

              part.IndexBuffer.GetData<uint16>(
                part.StartIndex * 2,
                narrow,
                0,
                indexCount
              )

              for j = 0 to indexCount - 1 do
                indices[indexOffset + j] <- int narrow[j] + baseVertex

            baseVertex <- baseVertex + descs[i].VertexCount
            indexOffset <- indexOffset + indexCount

          ib.SetData<int> indices
        else
          let indices = Array.zeroCreate<uint16> totalIndices

          for i in group.PartIndices do
            let part = flatParts[i]
            let indexCount = descs[i].IndexCount
            let narrow = Array.zeroCreate<uint16> indexCount

            part.IndexBuffer.GetData<uint16>(
              part.StartIndex * 2,
              narrow,
              0,
              indexCount
            )

            for j = 0 to indexCount - 1 do
              indices[indexOffset + j] <- narrow[j] + uint16 baseVertex

            baseVertex <- baseVertex + descs[i].VertexCount
            indexOffset <- indexOffset + indexCount

          ib.SetData<uint16> indices

        merged.Add {
          VertexBuffer = vb
          IndexBuffer = ib
          PrimitiveCount = totalIndices / 3
          ParentBoneIndex = flatParentBones[group.PartIndices[0]]
          IsSkinned = descs[group.PartIndices[0]].IsSkinned
          SourceParts = group.PartIndices |> Array.map(fun i -> flatParts[i])
        }

      merged.ToArray()

  // null value = "nothing merges" (cached too — no per-frame regrouping).
  // GPU resources on the merged parts die with the Model (MonoGame graphics
  // resources are finalizable); the weak table keeps models collectible.
  let private cache = ConditionalWeakTable<Model, MergedPart[]>()

  /// <summary>The cached merged parts for <paramref name="model"/>, built on first use
  /// by whichever pass (shadow or forward) sees the model first. <c>ValueNone</c> means
  /// no parts merge — callers draw per original part, today's behavior.</summary>
  let tryGet(gd: GraphicsDevice, model: Model) : MergedPart[] voption =
    match cache.GetValue(model, fun m -> build(gd, m)) with
    | null -> ValueNone
    | parts -> ValueSome parts

/// <summary>Per-part vertex-stream rebuild for semantically-colliding models.
///
/// The instance vertex stream declares its world-matrix rows and palette offset
/// on TextureCoordinate usage indices 1..6; MonoGame's input-layout builders
/// match shader inputs to the first bound stream carrying the semantic (DX12)
/// or renumber duplicates (DirectX), so a mesh whose vertices carry extra UV
/// channels on those indices silently steals or shifts the instance stream's
/// semantics and corrupts every instance transform (see
/// <c>SkinnedInstanceSemantics</c>).
///
/// The fix keeps real batching: this module rebuilds a colliding part's vertex
/// buffer ONCE — copying only the elements the instanced shaders read
/// (position, texcoord 0, normal, blend indices/weights, everything
/// non-colliding) into a compacted declaration, slicing the part's own
/// vertices, and rebasing its index slice. Draw sites consume the cached
/// (vb, ib, vertexOffset, startIndex) tuple exactly like a merged part; the
/// extra channels were never read by any instanced technique, so output is
/// bit-identical. Lifetime mirrors <c>MergedModelParts</c>: GPU buffers die
/// with the part/Model, the weak table keeps them collectible.</summary>
module internal InstancedPartStreams =

  let private formatSize(fmt: VertexElementFormat) =
    match fmt with
    | VertexElementFormat.Single -> 4
    | VertexElementFormat.Vector2 -> 8
    | VertexElementFormat.Vector3 -> 12
    | VertexElementFormat.Vector4 -> 16
    | VertexElementFormat.Color -> 4
    | VertexElementFormat.Byte4 -> 4
    | VertexElementFormat.Short2 -> 4
    | VertexElementFormat.Short4 -> 8
    | VertexElementFormat.NormalizedShort2 -> 4
    | VertexElementFormat.NormalizedShort4 -> 8
    | VertexElementFormat.HalfVector2 -> 4
    | VertexElementFormat.HalfVector4 -> 8
    | _ -> failwith $"unsupported vertex element format: {fmt}"

  let private collides(el: VertexElement) =
    el.VertexElementUsage = VertexElementUsage.TextureCoordinate
    && el.UsageIndex >= 1
    && el.UsageIndex <= 6

  /// Reference wrapper — ConditionalWeakTable values must be reference types.
  [<Sealed>]
  type private Streams(vb: VertexBuffer, ib: IndexBuffer, vOff: int, sIdx: int)
    =
    member _.Vb = vb
    member _.Ib = ib
    member _.VOff = vOff
    member _.SIdx = sIdx

  let private cache = ConditionalWeakTable<ModelMeshPart, Streams>()

  /// <summary>The streams to draw <paramref name="part"/> with on a two-stream
  /// instanced draw: the original buffers when the declaration doesn't collide,
  /// otherwise a rebuilt (vb, ib, 0, 0) slice. Cached per part.</summary>
  let resolve
    (gd: GraphicsDevice, part: ModelMeshPart)
    : struct (VertexBuffer * IndexBuffer * int * int) =
    match cache.TryGetValue(part) with
    | true, streams ->
      struct (streams.Vb, streams.Ib, streams.VOff, streams.SIdx)
    | false, _ ->
      let struct (vb, ib, vOff, sIdx) =
        if not(SkinnedInstanceSemantics.partCollides part) then
          struct (part.VertexBuffer,
                  part.IndexBuffer,
                  part.VertexOffset,
                  part.StartIndex)
        else
          let srcDecl = part.VertexBuffer.VertexDeclaration
          let srcStride = srcDecl.VertexStride
          let srcElements = srcDecl.GetVertexElements()

          let keep = srcElements |> Array.filter(fun el -> not(collides el))

          // Per-element byte sizes, hoisted out of the per-vertex copy loop.
          let keepSizes =
            keep |> Array.map(fun el -> formatSize el.VertexElementFormat)

          // Compact the kept elements to the front; offsets stay aligned to
          // each format's own size.
          let mutable cursor = 0

          let newElements =
            keep
            |> Array.map(fun el ->
              let offset = cursor

              cursor <- cursor + formatSize el.VertexElementFormat

              VertexElement(
                offset,
                el.VertexElementFormat,
                el.VertexElementUsage,
                el.UsageIndex
              ))

          let newStride = cursor

          // Vertices: read back ONLY this part's slice of the shared buffer —
          // content models pack every part into one buffer, and a whole-buffer
          // readback per colliding part would sync and copy the mesh's every
          // byte once per part. Then drop the colliding channels' bytes.
          let src = Array.zeroCreate<byte>(part.NumVertices * srcStride)

          part.VertexBuffer.GetData<byte>(
            part.VertexOffset * srcStride,
            src,
            0,
            src.Length
          )

          let dst = Array.zeroCreate<byte>(part.NumVertices * newStride)

          for v = 0 to part.NumVertices - 1 do
            let srcBase = v * srcStride
            let dstBase = v * newStride

            for i = 0 to keep.Length - 1 do
              Array.blit
                src
                (srcBase + keep[i].Offset)
                dst
                (dstBase + newElements[i].Offset)
                keepSizes[i]

          let vb =
            new VertexBuffer(
              gd,
              new VertexDeclaration(newStride, newElements),
              part.NumVertices,
              BufferUsage.None
            )

          vb.SetData<byte> dst

          // Indices: this part's index slice, rebased to the sliced vertices.
          let indexCount = part.PrimitiveCount * 3

          let ib =
            if
              part.IndexBuffer.IndexElementSize = IndexElementSize.ThirtyTwoBits
            then
              let indices = Array.zeroCreate<int> indexCount

              part.IndexBuffer.GetData<int>(
                part.StartIndex * 4,
                indices,
                0,
                indexCount
              )

              for j = 0 to indexCount - 1 do
                indices[j] <- indices[j] - part.VertexOffset

              let ib =
                new IndexBuffer(
                  gd,
                  IndexElementSize.ThirtyTwoBits,
                  indexCount,
                  BufferUsage.None
                )

              ib.SetData<int> indices
              ib
            else
              let indices = Array.zeroCreate<uint16> indexCount

              part.IndexBuffer.GetData<uint16>(
                part.StartIndex * 2,
                indices,
                0,
                indexCount
              )

              for j = 0 to indexCount - 1 do
                indices[j] <- indices[j] - uint16 part.VertexOffset

              let ib =
                new IndexBuffer(
                  gd,
                  IndexElementSize.SixteenBits,
                  indexCount,
                  BufferUsage.None
                )

              ib.SetData<uint16> indices
              ib

          struct (vb, ib, 0, 0)

      cache.Add(part, Streams(vb, ib, vOff, sIdx))
      struct (vb, ib, vOff, sIdx)
