namespace Mibo.Animation

#nowarn "9"

open System
open System.Collections.Generic
open System.Numerics
open System.Runtime.InteropServices
open FSharp.NativeInterop
open Raylib_cs

/// <summary>
/// A loaded set of 3D animation clips extracted from a model file.
/// </summary>
/// <remarks>
/// This is a shared data container — load once, use across multiple <c>Animation3DState</c> instances.
/// Use <c>Animation3DClips.names</c> to discover available animations at runtime.
/// </remarks>
type Animation3DClips = {
  Clips: ModelAnimation[]
  ClipNames: IReadOnlyDictionary<string, int>
  ClipIndices: ModelAnimation[]
  /// <summary>Backend-neutral clip metadata for the shared Core state machine.</summary>
  ClipsInfo: Animation3DClipsInfo
  /// <summary>
  /// Per-clip bone-order remap into the target skeleton's bone order, parallel
  /// to <c>Clips</c>. <c>ValueNone</c> (or an index past this array) means the
  /// clip already follows the target order. When present, the map is indexed by
  /// target bone index and yields the clip's own bone index (-1 = the clip does
  /// not animate that bone). Built by <c>Animation3DClips.merge</c> when clips
  /// come from files with differently-ordered skeletons; consumed by
  /// <c>Animation3DState.computePose</c>. The legacy path (raylib's native
  /// <c>UpdateModelAnimation</c>) cannot remap — it requires same-file clips.
  /// </summary>
  BoneRemaps: (int[] voption)[]
}

/// <summary>
/// Runtime state for a playing 3D skeletal animation.
/// </summary>
/// <remarks>
/// Each entity that needs independent animation must have its own <c>Animation3DState</c>
/// because <c>UpdateModelAnimation</c> mutates the model's bone matrices.
/// For shared-mesh GPU skinning, use the Phase 2 API (AnimatedMesh + DrawSkinnedMesh) instead.
/// </remarks>
[<Struct>]
type Animation3DState = {
  Model: Model
  Clips: Animation3DClips
  CurrentClipIndex: int
  CurrentFrame: float32
  Speed: float32
  Loop: bool
  Finished: bool
  BlendTargetIndex: int
  BlendTargetFrame: float32
  BlendProgress: float32
  BlendDuration: float32
}

module Animation3DClips =
  /// <summary>
  /// Create a clip set from an array of loaded <c>ModelAnimation</c> values.
  /// </summary>
  /// <remarks>
  /// Use this after loading animations with <c>Raylib.LoadModelAnimations</c>.
  /// The <c>ModelAnimation</c> values must be copied to an array before passing —
  /// <c>LoadModelAnimations</c> returns a <c>Span</c> which cannot be stored directly.
  /// </remarks>
  let fromModelAnimations(anims: ModelAnimation[]) : Animation3DClips =
    let dict = Dictionary<string, int>(anims.Length)

    for i = 0 to anims.Length - 1 do
      dict[anims[i].NameToString()] <- i

    let namesAndCounts =
      anims |> Array.map(fun a -> (a.NameToString(), a.KeyFrameCount))

    {
      Clips = anims
      ClipNames = dict
      ClipIndices = anims
      ClipsInfo = Animation3DClipsInfo.create namesAndCounts
      BoneRemaps = [||]
    }

  /// <summary>
  /// Skeleton bone names of a loaded <c>Model</c>, indexed by bone index.
  /// </summary>
  /// <remarks>
  /// raylib 6 clips carry no bone names — a file's animation keyframe poses
  /// follow the bone order of that file's model skeleton, which is what this
  /// reads. Needed per source file when merging clips from multiple files (see
  /// <c>merge</c>).
  /// </remarks>
  let boneNamesOf(model: Model) : string[] =
    let bones = model.Skeleton.BonesAsSpan()
    let names = Array.zeroCreate bones.Length

    for i = 0 to bones.Length - 1 do
      names[i] <- bones[i].NameToString()

    names

  /// <summary>
  /// Build the remap that lets a clip authored against one skeleton bone order
  /// (<paramref name="sourceBoneNames"/>) be sampled against another
  /// (<paramref name="targetBoneNames"/>), matching bones by name.
  /// </summary>
  /// <remarks>
  /// Returns <c>ValueNone</c> when both orders already match (no remap work at
  /// sample time). Otherwise the map is indexed by target bone index and yields
  /// the source bone index, or -1 when the source skeleton has no bone with
  /// that name (the bone samples as a zeroed pose, like an out-of-range bone).
  /// </remarks>
  let buildBoneRemap
    (sourceBoneNames: string[])
    (targetBoneNames: string[])
    : int[] voption =
    if
      sourceBoneNames.Length = targetBoneNames.Length
      && Array.forall2 (=) sourceBoneNames targetBoneNames
    then
      ValueNone
    else
      let sourceLookup = Dictionary<string, int>(sourceBoneNames.Length)

      for i = 0 to sourceBoneNames.Length - 1 do
        sourceLookup[sourceBoneNames[i]] <- i

      let map =
        targetBoneNames
        |> Array.map(fun name ->
          match sourceLookup.TryGetValue name with
          | true, idx -> idx
          | false, _ -> -1)

      ValueSome map

  /// <summary>
  /// Merge animations loaded from different files into one clip set that
  /// samples correctly against the target skeleton's bone order.
  /// </summary>
  /// <remarks>
  /// Asset packs may split clips across rig files whose skeletons order the
  /// same bones differently (e.g. KayKit's Rig_Medium_MovementBasic.glb orders
  /// right-side joints first, Rig_Medium_General.glb left-side first). raylib
  /// keyframe poses are index-based, so a clip from a differently-ordered file
  /// would drive the wrong bones (mirrored limbs). Each entry in
  /// <paramref name="sources"/> pairs one file's skeleton bone names (see
  /// <c>boneNamesOf</c>) with the animations loaded from that same file;
  /// <paramref name="targetBoneNames"/> is the skeleton of the model being
  /// animated. Clips whose source order already matches get no remap.
  /// </remarks>
  let merge
    (targetBoneNames: string[])
    (sources: (string[] * ModelAnimation[])[])
    : Animation3DClips =
    let clips = sources |> Array.collect snd

    let remaps =
      sources
      |> Array.collect(fun (sourceNames, anims) ->
        Array.create anims.Length (buildBoneRemap sourceNames targetBoneNames))

    let dict = Dictionary<string, int>(clips.Length)

    for i = 0 to clips.Length - 1 do
      dict[clips[i].NameToString()] <- i

    let namesAndCounts =
      clips |> Array.map(fun a -> (a.NameToString(), a.KeyFrameCount))

    {
      Clips = clips
      ClipNames = dict
      ClipIndices = clips
      ClipsInfo = Animation3DClipsInfo.create namesAndCounts
      BoneRemaps = remaps
    }

  /// <summary>
  /// Try to get the index for an animation name.
  /// </summary>
  /// <remarks>Use at load time to resolve animation names to indices for zero-allocation playback.</remarks>
  let inline tryGetClipIndex
    (name: string)
    (clips: Animation3DClips)
    : int voption =
    Animation3DClipsInfo.tryGetClipIndex name clips.ClipsInfo

  /// <summary>Get the list of animation names in this clip set.</summary>
  let inline names(clips: Animation3DClips) : string[] =
    Animation3DClipsInfo.names clips.ClipsInfo

  /// <summary>Get the number of animation clips.</summary>
  let inline count(clips: Animation3DClips) : int = clips.Clips.Length

  /// <summary>Check if the clip set has no animations.</summary>
  let inline isEmpty(clips: Animation3DClips) : bool = clips.Clips.Length = 0

/// <summary>
/// A mesh with skeleton data for GPU skinning.
/// </summary>
/// <remarks>
/// Load once from a <c>Model</c> via <c>AnimatedMesh.fromModel</c>, then share across
/// all entities that use the same mesh. Each entity computes its own bone matrices
/// via <c>AnimatedMesh.computeBoneMatrices</c> and passes them to <c>DrawSkinnedMesh</c>.
/// This avoids per-entity model copies and CPU skinning — the GPU does the vertex transform.
/// </remarks>
[<Struct>]
type AnimatedMesh = {
  Mesh: Mesh
  BoneCount: int
  /// <summary>Inverse bind pose per bone, in <c>System.Numerics</c> layout — internal
  /// input to pose evaluation; the transposed, upload-ready palette lives on <c>BonePose</c>.</summary>
  InverseBindPose: Matrix4x4[]
  /// <summary>Authored bone names, indexed by bone index (for <c>BoneRef.ByName</c> lookups).</summary>
  BoneNames: string[]
  /// <summary>Parent bone index per bone (-1 for roots). Informational — raylib keyframe poses are model-space.</summary>
  BoneParents: int[]
  /// <summary>Bone name → bone index lookup used by <c>AnimatedMesh.tryFindBoneIndex</c>.</summary>
  BoneLookup: IReadOnlyDictionary<string, int>
}

/// <summary>
/// The result of evaluating an animated model's pose once: both the model-space
/// bone transforms for the current frame (<c>WorldPoses</c>) and the shader
/// skinning palette (<c>Palette</c>).
/// </summary>
/// <remarks>
/// Compute once per entity per frame via <c>Animation3DState.computePose</c> (or
/// <c>AnimatedModel.computePose</c>) and share it between the skinned draw and any
/// number of bone queries / attachment draws. Both arrays are in raylib's native
/// matrix layout (a <c>Raymath.*</c> matrix is the transpose of the equivalent
/// <c>System.Numerics.Matrix4x4.*</c> matrix): compose <c>WorldPoses</c> with
/// <c>Raymath.MatrixMultiply</c>, never <c>System.Numerics</c> operators, and
/// hand <c>Palette</c> to <c>DrawSkinnedMesh</c> as-is.
/// </remarks>
[<Struct>]
type BonePose = {
  /// <summary>Model-space bone transforms for the current frame (attachment/query data), raylib-native layout.</summary>
  WorldPoses: Matrix4x4[]
  /// <summary>Skinning palette in raylib-native layout — equals raylib's own
  /// <c>MatrixMultiply(MatrixInvert(bind), current)</c> per bone, upload-ready.</summary>
  Palette: Matrix4x4[]
}

module private BoneAttributeUpload =
  // GPU skinning vertex attribute locations (raylib defaults, rlgl.h).
  let boneIndicesLocation = 7u
  let boneWeightsLocation = 8u

  /// <summary>
  /// Upload the bone index/weight VBOs that raylib's <c>UploadMesh</c> skips.
  /// </summary>
  /// <remarks>
  /// raylib only uploads bone attributes when natively compiled with
  /// <c>SUPPORT_GPU_SKINNING</c> — off by default (raylib 6.x config.h) and off
  /// in the NuGet-shipped native library. Without these VBOs every vertex
  /// falls back to bone 0 and skinned meshes render as a rigid T-pose. This
  /// mirrors the bone-attribute branch of <c>UploadMesh</c> (rmodels.c) from
  /// managed code, so both <c>UpdateModelAnimation</c> (legacy) and
  /// <c>DrawSkinnedMesh</c> work with the stock native library. Requires a live
  /// GL context; safe to call repeatedly — only uploads when the slots are
  /// empty (e.g. it is a no-op against a natively GPU-skinning-enabled build).
  /// </remarks>
  let upload(model: Model) : unit =
    let meshes = model.MeshesAsSpan()

    for i = 0 to meshes.Length - 1 do
      let mesh = meshes[i]

      let needsUpload =
        mesh.VaoId > 0u
        && not(NativePtr.isNullPtr mesh.VboId)
        && not(NativePtr.isNullPtr mesh.BoneIndices)
        && not(NativePtr.isNullPtr mesh.BoneWeights)
        && NativePtr.get mesh.VboId (int boneWeightsLocation) = 0u

      if needsUpload then
        Rlgl.EnableVertexArray mesh.VaoId |> ignore

        let indicesId =
          Rlgl.LoadVertexBuffer(
            NativePtr.toVoidPtr mesh.BoneIndices,
            mesh.VertexCount * 4, // 4 x unsigned byte
            false
          )

        Rlgl.SetVertexAttribute(
          boneIndicesLocation,
          4,
          Rlgl.UNSIGNED_BYTE,
          false,
          0,
          0
        )

        Rlgl.EnableVertexAttribute boneIndicesLocation

        let weightsId =
          Rlgl.LoadVertexBuffer(
            NativePtr.toVoidPtr mesh.BoneWeights,
            mesh.VertexCount * 4 * sizeof<float32>,
            false
          )

        Rlgl.SetVertexAttribute(boneWeightsLocation, 4, Rlgl.FLOAT, false, 0, 0)
        Rlgl.EnableVertexAttribute boneWeightsLocation

        // Write the ids back so UnloadMesh frees the buffers and
        // IsModelAnimationValid sees a fully-uploaded mesh. Mesh is a struct
        // copy, but VboId is a pointer — the writes land in the model's array.
        NativePtr.set mesh.VboId (int boneIndicesLocation) indicesId
        NativePtr.set mesh.VboId (int boneWeightsLocation) weightsId

        Rlgl.DisableVertexArray()

module Animation3DState =
  // ── Helpers: map between the raylib state and the Core state ───────────────
  // The playback fields are identical primitive types. We extract a Core state,
  // delegate the pure clock logic, then write the changed fields back. The Core
  // functions are fully inlinable, so the JIT sees straight struct copies — no
  // virtual dispatch, no boxing.

  let inline private toCoreState
    (s: Animation3DState)
    : Mibo.Animation.Animation3DState =
    {
      Clips = s.Clips.ClipsInfo
      CurrentClipIndex = s.CurrentClipIndex
      CurrentFrame = s.CurrentFrame
      Speed = s.Speed
      Loop = s.Loop
      Finished = s.Finished
      BlendTargetIndex = s.BlendTargetIndex
      BlendTargetFrame = s.BlendTargetFrame
      BlendProgress = s.BlendProgress
      BlendDuration = s.BlendDuration
    }

  let inline private fromCoreState
    (s: Animation3DState)
    (core: Mibo.Animation.Animation3DState)
    : Animation3DState =
    {
      s with
          CurrentClipIndex = core.CurrentClipIndex
          CurrentFrame = core.CurrentFrame
          Speed = core.Speed
          Loop = core.Loop
          Finished = core.Finished
          BlendTargetIndex = core.BlendTargetIndex
          BlendTargetFrame = core.BlendTargetFrame
          BlendProgress = core.BlendProgress
          BlendDuration = core.BlendDuration
    }

  /// <summary>Create a new 3D animation state starting on the specified clip.</summary>
  /// <param name="model">The model to animate. Must be a unique instance per entity.</param>
  /// <param name="clips">The loaded animation clip set.</param>
  /// <param name="clipName">The name of the animation to start on.</param>
  /// <param name="fps">The animation playback speed in frames per second.</param>
  /// <remarks>
  /// Also uploads the mesh's bone attribute VBOs when the native raylib build
  /// skipped them (stock NuGet builds do — see <c>SUPPORT_GPU_SKINNING</c>),
  /// so <c>UpdateModelAnimation</c> skinning works out of the box.
  /// </remarks>
  let create
    (model: Model)
    (clips: Animation3DClips)
    (clipName: string)
    (fps: float32)
    : Animation3DState =
    let core = Animation3DState.create clips.ClipsInfo clipName fps
    BoneAttributeUpload.upload model

    {
      Model = model
      Clips = clips
      CurrentClipIndex = core.CurrentClipIndex
      CurrentFrame = core.CurrentFrame
      Speed = core.Speed
      Loop = core.Loop
      Finished = core.Finished
      BlendTargetIndex = core.BlendTargetIndex
      BlendTargetFrame = core.BlendTargetFrame
      BlendProgress = core.BlendProgress
      BlendDuration = core.BlendDuration
    }

  /// <summary>Create a new 3D animation state starting on the specified clip index.</summary>
  /// <remarks>See <c>create</c> — also ensures bone attribute VBOs are uploaded.</remarks>
  let createByIndex
    (model: Model)
    (clips: Animation3DClips)
    (clipIndex: int)
    (fps: float32)
    : Animation3DState =
    let core = Animation3DState.createByIndex clips.ClipsInfo clipIndex fps
    BoneAttributeUpload.upload model

    {
      Model = model
      Clips = clips
      CurrentClipIndex = core.CurrentClipIndex
      CurrentFrame = core.CurrentFrame
      Speed = core.Speed
      Loop = core.Loop
      Finished = core.Finished
      BlendTargetIndex = core.BlendTargetIndex
      BlendTargetFrame = core.BlendTargetFrame
      BlendProgress = core.BlendProgress
      BlendDuration = core.BlendDuration
    }

  /// <summary>Play an animation by name. Resets the frame if switching clips.</summary>
  let inline play
    (clipName: string)
    (state: Animation3DState)
    : Animation3DState =
    let core = Animation3DState.play clipName (toCoreState state)
    fromCoreState state core

  /// <summary>
  /// Play by clip index (zero string allocation).
  /// </summary>
  let inline playByIndex
    (clipIndex: int)
    (state: Animation3DState)
    : Animation3DState =
    let core = Animation3DState.playByIndex clipIndex (toCoreState state)

    fromCoreState state core

  /// <summary>Play animation only if not already playing it.</summary>
  let inline playIfNot
    (clipName: string)
    (state: Animation3DState)
    : Animation3DState =
    let core = Animation3DState.playIfNot clipName (toCoreState state)

    fromCoreState state core

  /// <summary>
  /// Start blending from the current animation to a target animation.
  /// </summary>
  /// <param name="clipName">The animation to blend towards.</param>
  /// <param name="duration">The blend duration in seconds.</param>
  let inline blendTo
    (clipName: string)
    (duration: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core = Animation3DState.blendTo clipName duration (toCoreState state)

    fromCoreState state core

  /// <summary>Start blending to a target animation by clip index.</summary>
  let inline blendToByIndex
    (clipIndex: int)
    (duration: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core =
      Animation3DState.blendToByIndex clipIndex duration (toCoreState state)

    fromCoreState state core

  /// <summary>Is currently blending between two animations?</summary>
  let inline isBlending(state: Animation3DState) =
    Animation3DState.isBlending(toCoreState state)

  /// <summary>Force restart the current animation from the beginning.</summary>
  let inline restart(state: Animation3DState) : Animation3DState =
    let core = Animation3DState.restart(toCoreState state)
    fromCoreState state core

  /// <summary>
  /// Advance the animation by delta time.
  /// </summary>
  /// <remarks>Call from your Elmish update function each frame. Does not apply to the model — use <c>applyToModel</c> after.</remarks>
  let inline update
    (deltaSeconds: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core = Animation3DState.update deltaSeconds (toCoreState state)

    fromCoreState state core

  /// <summary>
  /// Apply the current animation frame to the model's bone matrices.
  /// </summary>
  /// <remarks>
  /// Calls <c>Raylib.UpdateModelAnimation</c> (or <c>UpdateModelAnimationEx</c> when blending)
  /// which mutates the model's internal bone data.
  /// Must be called before rendering with <c>DrawModel</c>.
  /// </remarks>
  let applyToModel(state: Animation3DState) : unit =
    if state.Clips.Clips.Length = 0 then
      ()
    elif state.BlendTargetIndex >= 0 then
      let clipA = state.Clips.ClipIndices.[state.CurrentClipIndex]
      let clipB = state.Clips.ClipIndices.[state.BlendTargetIndex]

      Raylib.UpdateModelAnimationEx(
        state.Model,
        clipA,
        state.CurrentFrame,
        clipB,
        state.BlendTargetFrame,
        state.BlendProgress
      )
    else
      let clip = state.Clips.ClipIndices.[state.CurrentClipIndex]
      Raylib.UpdateModelAnimation(state.Model, clip, state.CurrentFrame)

  /// <summary>Is the current animation finished? (always false for looping animations).</summary>
  let inline isFinished(state: Animation3DState) =
    Animation3DState.isFinished(toCoreState state)

  /// <summary>Is currently playing the specified animation?</summary>
  let inline isPlaying (clipName: string) (state: Animation3DState) =
    Animation3DState.isPlaying clipName (toCoreState state)

  /// <summary>Get the total duration of the current clip in seconds at the current speed.</summary>
  let inline duration(state: Animation3DState) =
    Animation3DState.duration(toCoreState state)

  /// <summary>Get the name of the current animation clip.</summary>
  let currentClipName(state: Animation3DState) : string =
    Animation3DState.currentClipName(toCoreState state)

  let inline withSpeed
    (speed: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core = Animation3DState.withSpeed speed (toCoreState state)

    fromCoreState state core

  let inline withLoop
    (loop: bool)
    (state: Animation3DState)
    : Animation3DState =
    let core = Animation3DState.withLoop loop (toCoreState state)

    fromCoreState state core

  /// Samples the interpolated TRS pose of one bone from <paramref name="clip"/> at
  /// <paramref name="frame"/>. Same keyframe-lerp math as
  /// <c>AnimatedMesh.computeBoneMatrices</c>: floor/ceil frames with wraparound,
  /// lerp on translation/scale, slerp on rotation. A clip with no keyframes (or a
  /// bone past the keyframe pose span) yields a zeroed <c>Transform</c>, matching
  /// <c>computeBoneMatrices</c>. <paramref name="remap"/> translates the target
  /// skeleton's <paramref name="boneIndex"/> into the clip's own bone order (see
  /// <c>Animation3DClips.buildBoneRemap</c>); -1 samples as a zeroed pose.
  let private sampleBoneTrs
    (remap: int[] voption)
    (clip: ModelAnimation)
    (frame: float32)
    (boneIndex: int)
    : struct (Vector3 * Quaternion * Vector3) =
    let sourceIndex =
      match remap with
      | ValueSome map when boneIndex < map.Length -> map[boneIndex]
      | _ -> boneIndex

    if clip.KeyFrameCount <= 0 || sourceIndex < 0 then
      let t = Unchecked.defaultof<Transform>
      struct (t.Translation, t.Rotation, t.Scale)
    else
      let currentFrame = int frame
      let nextFrame = currentFrame + 1

      let blend: float32 =
        let v = frame - float32 currentFrame
        Math.Clamp(v, 0.0f, 1.0f)

      let cf =
        if currentFrame >= clip.KeyFrameCount then
          currentFrame % clip.KeyFrameCount
        else
          currentFrame

      let nf =
        if nextFrame >= clip.KeyFrameCount then
          nextFrame % clip.KeyFrameCount
        else
          nextFrame

      let cfPtr = NativePtr.get clip.KeyframePoses cf
      let nfPtr = NativePtr.get clip.KeyframePoses nf

      let cfPoses =
        if NativePtr.isNullPtr cfPtr then
          Span<Transform>.Empty
        else
          Span<Transform>(NativePtr.toVoidPtr cfPtr, clip.BoneCount)

      let nfPoses =
        if NativePtr.isNullPtr nfPtr then
          Span<Transform>.Empty
        else
          Span<Transform>(NativePtr.toVoidPtr nfPtr, clip.BoneCount)

      let ct =
        if sourceIndex < cfPoses.Length then
          cfPoses[sourceIndex]
        else
          Unchecked.defaultof<Transform>

      let nt =
        if sourceIndex < nfPoses.Length then
          nfPoses[sourceIndex]
        else
          Unchecked.defaultof<Transform>

      Vector3.Lerp(ct.Translation, nt.Translation, blend),
      Quaternion.Slerp(ct.Rotation, nt.Rotation, blend),
      Vector3.Lerp(ct.Scale, nt.Scale, blend)

  /// <summary>
  /// Evaluate the current pose of <paramref name="state"/> on
  /// <paramref name="mesh"/> once, producing both the model-space bone transforms
  /// for the current frame (<c>WorldPoses</c>) and the shader skinning palette
  /// (<c>Palette</c>).
  /// </summary>
  /// <remarks>
  /// Pure math — does not mutate the model (unlike <c>applyToModel</c>). Uses the
  /// same TRS sampling as <c>AnimatedMesh.computeBoneMatrices</c>; when the state
  /// is blending, both clips are sampled and blended by <c>BlendProgress</c>,
  /// mirroring the <c>applyToModel</c> branch. raylib keyframe poses are
  /// model-space, so the world pose is the sampled pose directly (no parent
  /// walk). Both outputs are transposed into raylib's native matrix layout:
  /// <c>WorldPoses</c> composes with <c>Raymath.*</c> ops and <c>Palette</c> is
  /// upload-ready (<c>Palette[i] = Transpose(InverseBindPose[i] * pose[i])</c>,
  /// which equals raylib's native <c>MatrixMultiply(MatrixInvert(bind), current)</c>).
  /// </remarks>
  let computePose (mesh: AnimatedMesh) (state: Animation3DState) : BonePose =
    let boneCount = mesh.BoneCount
    let worldPoses = Array.zeroCreate<Matrix4x4> boneCount
    let palette = Array.zeroCreate<Matrix4x4> boneCount

    if state.Clips.Clips.Length > 0 && boneCount > 0 then
      let clipA = state.Clips.ClipIndices[state.CurrentClipIndex]
      let blending = state.BlendTargetIndex >= 0

      let clipB =
        if blending then
          state.Clips.ClipIndices[state.BlendTargetIndex]
        else
          clipA

      // Per-clip bone-order remap (clips merged from differently-ordered
      // skeleton files); ValueNone = the clip follows the target order.
      let remapFor(clipIndex: int) : int[] voption =
        if clipIndex >= 0 && clipIndex < state.Clips.BoneRemaps.Length then
          state.Clips.BoneRemaps[clipIndex]
        else
          ValueNone

      let remapA = remapFor state.CurrentClipIndex
      let remapB = remapFor state.BlendTargetIndex

      for i = 0 to boneCount - 1 do
        let struct (ta, ra, sa) =
          sampleBoneTrs remapA clipA state.CurrentFrame i

        let struct (translation, rotation, scale) =
          if blending then
            let struct (tb, rb, sb) =
              sampleBoneTrs remapB clipB state.BlendTargetFrame i

            struct (Vector3.Lerp(ta, tb, state.BlendProgress),
                    Quaternion.Slerp(ra, rb, state.BlendProgress),
                    Vector3.Lerp(sa, sb, state.BlendProgress))
          else
            struct (ta, ra, sa)

        let pose =
          let s = Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
          let r = Matrix4x4.CreateFromQuaternion rotation

          let tr =
            Matrix4x4.CreateTranslation(
              translation.X,
              translation.Y,
              translation.Z
            )

          Matrix4x4.Multiply(Matrix4x4.Multiply(s, r), tr)

        // Transpose the System.Numerics results into raylib's native matrix
        // layout (raylib's Matrix stores fields column-wise — a Raymath.*
        // matrix is the transpose of the equivalent Matrix4x4.* matrix).
        // WorldPoses then composes correctly with Raymath.* ops (attachment
        // draws, DrawMesh), and Palette matches the bone matrices raylib's
        // own UpdateModelAnimation computes for the shader upload.
        worldPoses[i] <- Matrix4x4.Transpose pose

        palette[i] <-
          Matrix4x4.Transpose(Matrix4x4.Multiply(mesh.InverseBindPose[i], pose))

    {
      WorldPoses = worldPoses
      Palette = palette
    }

module AnimatedMesh =
  let private buildMatrix(t: Transform) : Matrix4x4 =
    let s = Matrix4x4.CreateScale(t.Scale.X, t.Scale.Y, t.Scale.Z)

    let r = Matrix4x4.CreateFromQuaternion t.Rotation

    let tr =
      Matrix4x4.CreateTranslation(
        t.Translation.X,
        t.Translation.Y,
        t.Translation.Z
      )

    Matrix4x4.Multiply(Matrix4x4.Multiply(s, r), tr)

  /// <summary>
  /// Extract an <c>AnimatedMesh</c> from a loaded <c>Model</c>.
  /// </summary>
  /// <remarks>
  /// The model must have been loaded from a file that contains skeleton data (glb/gltf/iqm).
  /// Returns <c>ValueNone</c> if the model has no bones.
  /// </remarks>
  let fromModel(model: Model) : AnimatedMesh voption =
    if model.Skeleton.BoneCount <= 0 then
      ValueNone
    else
      let boneCount = model.Skeleton.BoneCount
      let bindPose = model.Skeleton.BindPoseAsSpan()
      let bones = model.Skeleton.BonesAsSpan()
      let invBindPose = Array.zeroCreate<Matrix4x4> boneCount
      let boneNames = Array.zeroCreate<string> boneCount
      let boneParents = Array.zeroCreate<int> boneCount
      let boneLookup = Dictionary<string, int> boneCount

      for i = 0 to boneCount - 1 do
        invBindPose[i] <-
          let _m = buildMatrix bindPose[i]
          let mutable m = Matrix4x4.Identity
          Matrix4x4.Invert(_m, &m) |> ignore
          m

        let boneName = bones[i].NameToString()
        boneNames[i] <- boneName
        boneParents[i] <- bones[i].Parent
        boneLookup[boneName] <- i

      let meshes = model.MeshesAsSpan()

      if meshes.Length = 0 then
        ValueNone
      else
        // Stock native raylib never uploads bone attributes — do it here so
        // skinning works out of the box (see BoneAttributeUpload).
        BoneAttributeUpload.upload model

        ValueSome {
          Mesh = meshes[0]
          BoneCount = boneCount
          InverseBindPose = invBindPose
          BoneNames = boneNames
          BoneParents = boneParents
          BoneLookup = boneLookup
        }

  /// <summary>
  /// Try to find the index of a bone by its authored name.
  /// Returns <c>ValueNone</c> when the mesh has no bone with that name.
  /// </summary>
  let tryFindBoneIndex (name: string) (mesh: AnimatedMesh) : int voption =
    match mesh.BoneLookup.TryGetValue name with
    | true, index -> ValueSome index
    | false, _ -> ValueNone

  /// <summary>
  /// Compute bone matrices for a given animation clip and frame.
  /// </summary>
  /// <remarks>
  /// This is pure math — does not mutate the model. The result can be passed
  /// directly to <c>DrawSkinnedMesh</c> for GPU skinning.
  /// The algorithm matches raylib's <c>UpdateModelAnimation</c>:
  /// interpolates keyframes (lerp/slerp), builds TRS matrices, and multiplies
  /// by the inverse bind pose. The result is transposed into raylib's native
  /// matrix layout — it equals raylib's own
  /// <c>MatrixMultiply(MatrixInvert(bind), current)</c> per bone, ready for the
  /// shader upload.
  /// </remarks>
  let computeBoneMatrices
    (clip: ModelAnimation)
    (frame: float32)
    (mesh: AnimatedMesh)
    : Matrix4x4[] =
    let boneCount = mesh.BoneCount
    let matrices = Array.zeroCreate<Matrix4x4> boneCount

    if clip.KeyFrameCount <= 0 || boneCount <= 0 then
      matrices
    else
      let currentFrame = int frame
      let nextFrame = currentFrame + 1

      let blend: float32 =
        let v = frame - float32 currentFrame
        Math.Clamp(v, 0.0f, 1.0f)

      let cf =
        if currentFrame >= clip.KeyFrameCount then
          currentFrame % clip.KeyFrameCount
        else
          currentFrame

      let nf =
        if nextFrame >= clip.KeyFrameCount then
          nextFrame % clip.KeyFrameCount
        else
          nextFrame

      let cfPtr = NativePtr.get clip.KeyframePoses cf
      let nfPtr = NativePtr.get clip.KeyframePoses nf

      let cfPoses =
        if NativePtr.isNullPtr cfPtr then
          Span<Transform>.Empty
        else
          Span<Transform>(NativePtr.toVoidPtr cfPtr, boneCount)

      let nfPoses =
        if NativePtr.isNullPtr nfPtr then
          Span<Transform>.Empty
        else
          Span<Transform>(NativePtr.toVoidPtr nfPtr, boneCount)

      for i = 0 to boneCount - 1 do
        let ct =
          if i < cfPoses.Length then
            cfPoses[i]
          else
            Unchecked.defaultof<Transform>

        let nt =
          if i < nfPoses.Length then
            nfPoses[i]
          else
            Unchecked.defaultof<Transform>

        let translation = Vector3.Lerp(ct.Translation, nt.Translation, blend)

        let rotation = Quaternion.Slerp(ct.Rotation, nt.Rotation, blend)

        let scale = Vector3.Lerp(ct.Scale, nt.Scale, blend)

        let currentPoseMatrix =
          let s = Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
          let r = Matrix4x4.CreateFromQuaternion(rotation)

          let tr =
            Matrix4x4.CreateTranslation(
              translation.X,
              translation.Y,
              translation.Z
            )

          Matrix4x4.Multiply(Matrix4x4.Multiply(s, r), tr)

        // Transposed into raylib's native matrix layout for the shader upload
        // (see computePose).
        matrices[i] <-
          Matrix4x4.Transpose(
            Matrix4x4.Multiply(mesh.InverseBindPose[i], currentPoseMatrix)
          )

      matrices

/// <summary>Pose query functions for <see cref="T:Mibo.Animation.BonePose"/>.</summary>
module BonePose =

  /// <summary>
  /// Get the model-space world transform of a bone by index.
  /// Bounds-checked — returns <c>ValueNone</c> for out-of-range indices.
  /// </summary>
  let inline worldAt (index: int) (pose: BonePose) : Matrix4x4 voption =
    if index >= 0 && index < pose.WorldPoses.Length then
      ValueSome pose.WorldPoses[index]
    else
      ValueNone

  /// <summary>
  /// Get the model-space world transform of a bone by name.
  /// Returns <c>ValueNone</c> when the mesh has no bone with that name.
  /// </summary>
  let tryGetWorld
    (name: string)
    (mesh: AnimatedMesh)
    (pose: BonePose)
    : Matrix4x4 voption =
    AnimatedMesh.tryFindBoneIndex name mesh
    |> ValueOption.bind(fun i -> worldAt i pose)

/// <summary>
/// An animated model: shared skeleton/mesh data plus a per-entity playback state.
/// </summary>
/// <remarks>
/// This is the opt-in <b>GPU skinning path</b>: drawing an <c>AnimatedModel</c>
/// emits <c>DrawSkinnedMesh</c> commands with a bone palette instead of mutating
/// the model via <c>UpdateModelAnimation</c>, so the same model can be drawn with
/// several different poses in one frame. The legacy bare
/// <see cref="T:Mibo.Animation.Animation3DState"/> path keeps mutating the model
/// and keeps working unchanged.
/// </remarks>
[<Struct>]
type AnimatedModel = {
  /// <summary>The shared skeleton/mesh data (load once per model).</summary>
  Mesh: AnimatedMesh
  /// <summary>The per-entity playback state. Also carries the raylib <c>Model</c> the meshes are drawn from.</summary>
  State: Animation3DState
}

/// <summary>Creation, pose evaluation, and bone queries for <see cref="T:Mibo.Animation.AnimatedModel"/>.</summary>
module AnimatedModel =

  /// <summary>Create an animated model from shared mesh data and a playback state.</summary>
  let inline create
    (mesh: AnimatedMesh)
    (state: Animation3DState)
    : AnimatedModel =
    { Mesh = mesh; State = state }

  /// <summary>
  /// Evaluate the model's current pose once. Share the result between the skinned
  /// draw and any number of bone queries / attachment draws this frame.
  /// </summary>
  let inline computePose(am: AnimatedModel) : BonePose =
    Animation3DState.computePose am.Mesh am.State

  /// <summary>
  /// Get the model-space world transform of a bone in the current frame.
  /// Returns <c>ValueNone</c> for unknown bones.
  /// </summary>
  /// <remarks>
  /// Recomputes the pose on every call — for multiple queries in one frame,
  /// call <c>computePose</c> once and use <c>BonePose.worldAt</c> /
  /// <c>BonePose.tryGetWorld</c> instead.
  /// </remarks>
  let inline tryGetBoneWorld
    (bone: BoneRef)
    (am: AnimatedModel)
    : Matrix4x4 voption =
    let pose = computePose am

    match bone with
    | BoneRef.ByIndex i -> BonePose.worldAt i pose
    | BoneRef.ByName name -> BonePose.tryGetWorld name am.Mesh pose
