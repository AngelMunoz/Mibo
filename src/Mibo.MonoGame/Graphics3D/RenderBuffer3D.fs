namespace Mibo.Elmish.Graphics3D

open System
open System.Buffers
open System.Collections.Generic

/// <summary>
/// An allocation-friendly buffer for 3D render commands.
/// </summary>
/// <remarks>
/// Commands are accumulated each frame via <see cref="M:Mibo.Elmish.Graphics3D.RenderBuffer3D.Add"/>,
/// then executed in insertion order by the active pipeline.
/// The pipeline may re-sort internally if needed for state efficiency (e.g., front-to-back,
/// material batching), but the buffer itself does not impose an order.
///
/// Uses <see cref="T:System.Buffers.ArrayPool`1"/> for the backing store to avoid per-frame
/// heap allocations.
///
/// The buffer is designed to be cleared and repopulated each frame.
/// <see cref="M:Mibo.Elmish.Graphics3D.RenderBuffer3D.Clear"/> resets the count
/// without deallocating the internal array.
/// </remarks>
type RenderBuffer3D([<Struct>] ?capacity: int) =

  let mutable items =
    ArrayPool<Command3D>.Shared.Rent(defaultValueArg capacity 1024)

  let mutable count = 0
  let mutable clearCounter = 0
  let mutable postProcessCount = 0
  let mutable depthPostProcessCount = 0
  let mutable cameraBlockCount = 0
  let mutable shadowCasterLightCount = 0

  let rentedArrays = ResizeArray<Microsoft.Xna.Framework.Matrix[]>()

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newArr = ArrayPool<Command3D>.Shared.Rent(newSize)

      Array.Copy(items, newArr, count)
      // clearArray = true: a Command3D holds managed refs to Model/Texture2D/Effect,
      // so the pooled array would keep them alive across frames if not cleared.
      // Matches RenderBuffer2D.ensureCapacity.
      ArrayPool<Command3D>.Shared.Return(items, clearArray = true)
      items <- newArr

  /// <summary>The number of commands currently in the buffer.</summary>
  member _.Count = count

  /// <summary>
  /// Number of <c>PostProcess</c>/<c>PostProcessWithDepth</c> commands added since the last
  /// <c>Clear</c>. Lets a pipeline skip the post-process drain (and its per-frame allocation) when
  /// the view emits none.
  /// </summary>
  member _.PostProcessCount = postProcessCount

  /// <summary>
  /// Number of <c>PostProcessWithDepth</c> commands added since the last <c>Clear</c>. When &gt; 0,
  /// the pipeline produces a camera-POV depth target this frame so depth-needing effects (fog,
  /// depth-of-field, SSAO) can sample <c>PostProcessContext3D.Depth</c>. Zero on frames with only
  /// color-only <c>PostProcess</c> actions, so the depth pass is skipped entirely.
  /// </summary>
  member _.DepthPostProcessCount = depthPostProcessCount

  /// <summary>
  /// Number of <c>BeginCamera</c>/<c>BeginCameraConfig</c> commands added since the last
  /// <c>Clear</c>. Lets a pipeline skip the per-camera-block plan (and its allocations) for
  /// single-camera frames.
  /// </summary>
  member _.CameraBlockCount = cameraBlockCount

  /// <summary>
  /// Number of light commands with <c>CastsShadows = true</c> added since the last
  /// <c>Clear</c>. A conservative gate for shadow-geometry collection: zero means no
  /// shadow-casting light exists in the buffer, so collection can be skipped outright.
  /// (Conservative because any casting directional counts, not just the first — only
  /// <c>DirLights[0]</c> can actually cast.)
  /// </summary>
  member _.ShadowCasterLightCount = shadowCasterLightCount

  /// <summary>Gets the command at the specified index.</summary>
  member _.Item(i: int) = items[i]

  /// <summary>Adds a render command to the buffer.</summary>
  member _.Add(cmd: Command3D) =
    ensureCapacity 1
    items[count] <- cmd

    match cmd with
    | Command3D.PostProcess _ -> postProcessCount <- postProcessCount + 1
    | Command3D.PostProcessWithDepth _ ->
      postProcessCount <- postProcessCount + 1
      depthPostProcessCount <- depthPostProcessCount + 1
    | Command3D.BeginCamera _
    | Command3D.BeginCameraConfig _ -> cameraBlockCount <- cameraBlockCount + 1
    | Command3D.AddDirectionalLight light when light.CastsShadows ->
      shadowCasterLightCount <- shadowCasterLightCount + 1
    | Command3D.AddPointLight light when light.CastsShadows ->
      shadowCasterLightCount <- shadowCasterLightCount + 1
    | Command3D.AddSpotLight light when light.CastsShadows ->
      shadowCasterLightCount <- shadowCasterLightCount + 1
    | _ -> ()

    count <- count + 1

  /// <summary>
  /// Rents a <c>Matrix[]</c> from the shared <see cref="T:System.Buffers.ArrayPool`1"/>
  /// and tracks it for automatic return at the end of the frame. Used by the instanced
  /// draw witnesses for the scratch palette and per-command transforms copies consumed
  /// synchronously within the same frame.
  /// </summary>
  member _.RentMatrixArray(needed: int) =
    let arr = ArrayPool<Microsoft.Xna.Framework.Matrix>.Shared.Rent(needed)
    rentedArrays.Add(arr)
    arr

  /// <summary>
  /// Returns all rented arrays to the pool. Called automatically from <c>Clear</c>
  /// and by the renderer after the pipeline has executed for the frame.
  /// </summary>
  member _.ReleaseRentedArrays() =
    for arr in rentedArrays do
      ArrayPool<Microsoft.Xna.Framework.Matrix>.Shared
        .Return(arr, clearArray = false)

    rentedArrays.Clear()

  /// <summary>
  /// Clears all commands from the buffer without deallocating the backing array.
  /// Call this at the start of each frame before populating with new commands.
  /// </summary>
  /// <remarks>
  /// Resets the count every frame (clearing thousands of struct-DU slots per frame
  /// is a hot-path cost we avoid), but periodically zeroes the backing array (~every
  /// 300 frames) so stale managed refs (Model/Texture2D/Effect) in slots above count
  /// can't keep unloaded assets alive indefinitely after a scene shrinks. Dispose also
  /// clears. This matches <c>RenderBuffer2D.Clear</c> and the raylib buffers.
  /// </remarks>
  member this.Clear() =
    this.ReleaseRentedArrays()
    count <- 0
    postProcessCount <- 0
    depthPostProcessCount <- 0
    cameraBlockCount <- 0
    shadowCasterLightCount <- 0
    // Periodically zero the backing array so stale managed refs (Model/Texture2D/Effect)
    // in slots above count don't keep unloaded assets alive indefinitely after a scene
    // shrinks or chunks evict. ~5s at 60fps; Array.Clear on structs is a cheap memset.
    clearCounter <- clearCounter + 1

    if clearCounter >= 300 then
      clearCounter <- 0
      Array.Clear(items, 0, items.Length)

  /// <summary>
  /// Sorts commands using the provided comparer.
  /// Pipelines may call this internally to optimize draw order.
  /// </summary>
  member _.Sort(comparer: IComparer<Command3D>) =
    Array.Sort(items, 0, count, comparer)

  interface System.IDisposable with
    member this.Dispose() =
      this.ReleaseRentedArrays()
      ArrayPool<Command3D>.Shared.Return(items, clearArray = true)
      items <- Array.empty
      count <- 0

// ─────────────────────────────────────────────────────────────────────────────
// Fluent Draw DSL witnesses (backing Mibo.Elmish.Graphics.Draw).
//
// One `member inline` per Core Draw member: forward to the existing Command3D
// builders, converting System.Numerics vectors to XNA at the boundary via the
// existing internal Conversions module. 3D has no layer concept — camera and
// immediate witnesses accept and ignore the layer the shared Draw members
// pass. Augmentations must live in the buffer's own file: the SRTP solver
// only considers extension members in the type's declaration group.
// ─────────────────────────────────────────────────────────────────────────────

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open System.Numerics
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo

/// <summary>SRTP witnesses backing <see cref="T:Mibo.Elmish.Graphics.Draw"/> on the MonoGame 3D buffer.</summary>
type RenderBuffer3D with

  // ── Geometry ──

  /// <summary>
  /// MonoGame's mesh-level draw is the effectless PrimitiveMesh. Draws the whole
  /// buffer from offset 0 — see <c>AddDrawMeshSlice</c> for shared content-pipeline
  /// buffers (multi-part models).
  /// </summary>
  [<Obsolete("AddDrawMesh draws from buffer offset 0. Use AddDrawMeshSlice to draw a part's slice of shared content-pipeline buffers (vertexOffset/startIndex); procedural meshes pass 0, 0.")>]
  member inline b.AddDrawMesh
    (mesh: PrimitiveMesh, transform: Matrix, material: Material3D)
    =
    b.Add(Command3D.DrawPrimitive(mesh, transform, material, 0, 0))

  /// <summary>
  /// Draws a slice of a mesh within shared content-pipeline vertex/index buffers:
  /// <c>vertexOffset</c> is the part's first vertex (<c>baseVertex</c>) and
  /// <c>startIndex</c> the part's first index. Self-contained buffers pass 0, 0.
  /// </summary>
  member inline b.AddDrawMeshSlice
    (
      mesh: PrimitiveMesh,
      transform: Matrix,
      material: Material3D,
      vertexOffset: int,
      startIndex: int
    ) =
    b.Add(
      Command3D.DrawPrimitive(
        mesh,
        transform,
        material,
        vertexOffset,
        startIndex
      )
    )

  /// <summary>
  /// Draws many instances of the same mesh, whole buffers from offset 0 — see
  /// <c>AddDrawInstancedSlice</c> for shared content-pipeline buffers.
  /// </summary>
  [<Obsolete("AddDrawInstanced draws from buffer offset 0. Use AddDrawInstancedSlice to draw a part's slice of shared content-pipeline buffers (vertexOffset/startIndex); procedural meshes pass 0, 0.")>]
  member inline b.AddDrawInstanced
    (
      mesh: PrimitiveMesh,
      transforms: Matrix[],
      material: Material3D,
      instanceCount: int,
      colors: Microsoft.Xna.Framework.Color[] voption
    ) =
    // Copy the transforms into a rented array: the command executes after
    // the whole view has been recorded, so a caller reusing its backing
    // array (across camera blocks or frames) must not be able to overwrite
    // this command's transforms before they execute. Same by-reference
    // fix as AddAnimatedModelInstanced.
    // max 0: ArrayPool.Rent throws on a negative length — a negative
    // instanceCount was (and stays) a recorded no-op.
    let count = max 0 (min instanceCount transforms.Length)
    let transformsCopy = b.RentMatrixArray(count)
    Array.Copy(transforms, 0, transformsCopy, 0, count)

    // Pass count (not instanceCount): the rented array may be longer than
    // count, and the pipeline clamps draws to transforms.Length — with the
    // pooled length that clamp could read past the copied rows.
    b.Add(
      Command3D.DrawInstanced(
        mesh,
        transformsCopy,
        colors,
        material,
        count,
        0,
        0
      )
    )

  /// <summary>
  /// Draws many instances of a slice of a mesh within shared content-pipeline
  /// vertex/index buffers: <c>vertexOffset</c> is the part's first vertex
  /// (<c>baseVertex</c>) and <c>startIndex</c> the part's first index.
  /// Self-contained buffers pass 0, 0.
  /// </summary>
  member inline b.AddDrawInstancedSlice
    (
      mesh: PrimitiveMesh,
      transforms: Matrix[],
      material: Material3D,
      instanceCount: int,
      colors: Microsoft.Xna.Framework.Color[] voption,
      vertexOffset: int,
      startIndex: int
    ) =
    // Copy the transforms into a rented array: the command executes after
    // the whole view has been recorded, so a caller reusing its backing
    // array (across camera blocks or frames) must not be able to overwrite
    // this command's transforms before they execute. Same by-reference
    // fix as AddAnimatedModelInstanced.
    // max 0: ArrayPool.Rent throws on a negative length — a negative
    // instanceCount was (and stays) a recorded no-op.
    let count = max 0 (min instanceCount transforms.Length)
    let transformsCopy = b.RentMatrixArray(count)
    Array.Copy(transforms, 0, transformsCopy, 0, count)

    // Pass count (not instanceCount): the rented array may be longer than
    // count, and the pipeline clamps draws to transforms.Length — with the
    // pooled length that clamp could read past the copied rows.
    b.Add(
      Command3D.DrawInstanced(
        mesh,
        transformsCopy,
        colors,
        material,
        count,
        vertexOffset,
        startIndex
      )
    )

  member inline b.AddDrawModel(model: Model, transform: Matrix) =
    b.Add(Command3D.DrawModel(model, transform))

  member inline b.AddDrawModelWith
    (model: Model, transform: Matrix, material: Material3D)
    =
    b.Add(
      Command3D.DrawModelWith(model, transform, MaterialOverride.All material)
    )

  member inline b.AddDrawModelWithPerMesh
    (
      model: Model,
      transform: Matrix,
      [<InlineIfLambda>] resolver: int -> Material3D
    ) =
    b.Add(
      Command3D.DrawModelWith(
        model,
        transform,
        MaterialOverride.PerMesh resolver
      )
    )

  /// MonoGame animated draw: derives the bone palette from the state, or reuses
  /// a caller-evaluated pose shared with bone queries and attachment draws.
  member inline b.AddAnimatedModel
    (am: AnimatedModel, transform: Matrix, pose: BonePose voption)
    =
    let bones =
      match pose with
      | ValueSome p -> p.Palette
      | ValueNone ->
        match am.Mesh with
        | ValueSome mesh -> Animation3DState.computeBonePalette mesh am.State
        | ValueNone -> [||]

    b.Add(Command3D.DrawAnimatedModel(am.Model, transform, bones))

  member inline b.AddAnimatedModelWith
    (
      am: AnimatedModel,
      transform: Matrix,
      material: Material3D,
      pose: BonePose voption
    ) =
    let bones =
      match pose with
      | ValueSome p -> p.Palette
      | ValueNone ->
        match am.Mesh with
        | ValueSome mesh -> Animation3DState.computeBonePalette mesh am.State
        | ValueNone -> [||]

    b.Add(
      Command3D.DrawAnimatedModelWith(
        am.Model,
        transform,
        bones,
        MaterialOverride.All material
      )
    )

  member inline b.AddAnimatedModelWithPerMesh
    (
      am: AnimatedModel,
      transform: Matrix,
      [<InlineIfLambda>] resolver: int -> Material3D,
      pose: BonePose voption
    ) =
    let bones =
      match pose with
      | ValueSome p -> p.Palette
      | ValueNone ->
        match am.Mesh with
        | ValueSome mesh -> Animation3DState.computeBonePalette mesh am.State
        | ValueNone -> [||]

    b.Add(
      Command3D.DrawAnimatedModelWith(
        am.Model,
        transform,
        bones,
        MaterialOverride.PerMesh resolver
      )
    )

  /// <summary>
  /// Skinned + instanced draw: one draw call for N instances of the same
  /// animated model, each with its own pose. <paramref name="poses"/> carries
  /// one caller-evaluated <c>BonePose</c> per instance (share them with bone
  /// queries / attachment draws); the witness flattens the per-instance
  /// palettes into the command's instance-major array. The instance count is
  /// <c>min(transforms.Length, poses.Length)</c>; 0 (or a boneless model —
  /// use <c>instanced</c> for those) emits nothing. On the OpenGL backend the
  /// pipeline falls back to per-instance skinned draws. Each pose's
  /// <c>Palette</c> must hold at least one matrix per bone: a longer palette
  /// truncates to its first <c>boneCount</c> entries, a shorter one throws
  /// <see cref="T:System.ArgumentException"/>.
  /// Both <paramref name="transforms"/> and the palettes are copied into
  /// pooled arrays at record time, so the caller may freely reuse or refill
  /// its arrays once this call returns.
  /// </summary>
  member inline b.AddAnimatedModelInstanced
    (
      am: AnimatedModel,
      transforms: Matrix[],
      poses: BonePose[],
      material: MaterialOverride voption,
      colors: Microsoft.Xna.Framework.Color[] voption
    ) =
    let count = min transforms.Length poses.Length

    match am.Mesh with
    | ValueSome mesh when count > 0 ->
      let boneCount = mesh.BoneCount
      let palettes = b.RentMatrixArray(count * boneCount)

      for i = 0 to count - 1 do
        let src = poses[i].Palette

        if src.Length < boneCount then
          invalidArg
            "poses"
            $"poses[{i}].Palette has {src.Length} matrices, the model needs {boneCount} (one per bone). Pass poses evaluated for this model (Animation3DState.computePose / computePoseInto)."

        Array.Copy(src, 0, palettes, i * boneCount, boneCount)

      // Copy the instance transforms into a rented array too: the command
      // executes after the whole view has been recorded (and after later
      // camera blocks are recorded), so storing the caller's array by
      // reference would let a refill — e.g. reusing one array across camera
      // blocks — overwrite this command's transforms before they execute.
      let transformsCopy = b.RentMatrixArray(count)
      Array.Copy(transforms, 0, transformsCopy, 0, count)

      b.Add(
        Command3D.DrawAnimatedModelInstanced(
          am.Model,
          transformsCopy,
          palettes,
          material,
          colors,
          count,
          boneCount
        )
      )
    | _ -> ()

  /// Draws a static mesh parented to a bone of an animated model. World =
  /// localTransform * boneWorld * transform (row-vector composition — the
  /// attachment inherits the instance's full world transform). An unknown bone
  /// is a no-op: no command is emitted. Pass the same pose given to
  /// AddAnimatedModel to avoid a second pose evaluation this frame.
  member inline b.AddAttachedMesh
    (
      am: AnimatedModel,
      bone: BoneRef,
      localTransform: Matrix,
      mesh: PrimitiveMesh,
      material: Material3D,
      transform: Matrix,
      pose: BonePose voption
    ) =
    am.Mesh
    |> ValueOption.bind(fun animMesh ->
      let pose' =
        match pose with
        | ValueSome p -> p
        | ValueNone -> Animation3DState.computePose animMesh am.State

      match bone with
      | BoneRef.ByIndex i -> BonePose.worldAt i pose'
      | BoneRef.ByName name ->
        AnimatedMesh.tryFindBoneIndex name animMesh
        |> ValueOption.bind(fun i -> BonePose.worldAt i pose'))
    |> ValueOption.iter(fun boneWorld ->
      b.Add(
        Command3D.DrawPrimitive(
          mesh,
          localTransform * boneWorld * transform,
          material,
          0,
          0
        )
      ))

  // ── Billboards & Lines ──

  member inline b.AddBillboard
    (
      texture: Texture2D,
      position: Vector3,
      size: Vector2,
      color: Color,
      rotation: float32,
      sourceRect: Microsoft.Xna.Framework.Rectangle,
      blend: BlendMode voption
    ) =
    b.Add(
      Command3D.DrawBillboard {
        Texture = texture
        Position = Conversions.fromNumericsVector3 position
        Size = Conversions.fromNumericsVector2 size
        Color = MonoGameColor.toMonoGameColor color
        Rotation = rotation
        SourceRect = sourceRect
        Blend = defaultValueArg blend BlendMode.AlphaBlend
      }
    )

  member inline b.AddBillboardBatch
    (
      textures: Texture2D[],
      positions: Microsoft.Xna.Framework.Vector3[],
      sizes: Microsoft.Xna.Framework.Vector2[],
      colors: Microsoft.Xna.Framework.Color[],
      count: int,
      rotations: float32[],
      sourceRects: Microsoft.Xna.Framework.Rectangle[],
      blend: BlendMode voption
    ) =
    b.Add(
      Command3D.DrawBillboardBatch {
        Textures = textures
        Positions = positions
        Sizes = sizes
        Colors = colors
        Rotations = rotations
        SourceRects = sourceRects
        Blend = defaultValueArg blend BlendMode.AlphaBlend
        Count = count
      }
    )

  member inline b.AddLine3D(start: Vector3, finish: Vector3, color: Color) =
    b.Add(
      Command3D.DrawLine3D(
        Conversions.fromNumericsVector3 start,
        Conversions.fromNumericsVector3 finish,
        MonoGameColor.toMonoGameColor color
      )
    )

  // ── Camera (layer ignored — 3D has no layers) ──

  member inline b.AddBeginCamera(camera: Camera3D, _layer: int<RenderLayer>) =
    b.Add(Command3D.BeginCamera camera)

  member inline b.AddBeginCameraConfig
    (config: Camera3DConfig, _layer: int<RenderLayer>)
    =
    b.Add(Command3D.BeginCameraConfig config)

  member inline b.AddEndCamera(_layer: int<RenderLayer>) =
    b.Add Command3D.EndCamera

  // ── Shadows & Effect Scopes ──

  member inline b.AddSetShadowOrigin(origin: Vector3) =
    b.Add(Command3D.SetShadowOrigin(Conversions.fromNumericsVector3 origin))

  member inline b.AddEnableShadows3D() = b.Add Command3D.EnableShadows

  member inline b.AddDisableShadows3D() = b.Add Command3D.DisableShadows

  member inline b.AddBeginEffect(effect: Effect) =
    b.Add(Command3D.BeginEffect effect)

  member inline b.AddEndEffect() = b.Add Command3D.EndEffect

  // ── Lights (Core types, pass-through) ──

  member inline b.AddSetAmbientLight(light: AmbientLight3D) =
    b.Add(Command3D.SetAmbientLight light)

  member inline b.AddDirectionalLight(light: DirectionalLight3D) =
    b.Add(Command3D.AddDirectionalLight light)

  member inline b.AddPointLight(light: PointLight3D) =
    b.Add(Command3D.AddPointLight light)

  member inline b.AddSpotLight(light: SpotLight3D) =
    b.Add(Command3D.AddSpotLight light)

  // ── Escape Hatches ──

  member inline b.AddDrawImmediate
    ([<InlineIfLambda>] action: SceneContext -> unit, _layer: int<RenderLayer>)
    =
    b.Add(Command3D.DrawImmediate action)

  member inline b.AddPostProcess
    ([<InlineIfLambda>] action: PostProcessContext3D -> unit)
    =
    b.Add(Command3D.PostProcess action)

  member inline b.AddPostProcessWithDepth
    ([<InlineIfLambda>] action: PostProcessContext3D -> unit)
    =
    b.Add(Command3D.PostProcessWithDepth action)
