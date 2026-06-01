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

    {
      Clips = anims
      ClipNames = dict
      ClipIndices = anims
    }

  /// <summary>
  /// Try to get the index for an animation name.
  /// </summary>
  /// <remarks>Use at load time to resolve animation names to indices for zero-allocation playback.</remarks>
  let inline tryGetClipIndex
    (name: string)
    (clips: Animation3DClips)
    : int voption =
    match clips.ClipNames.TryGetValue(name) with
    | true, idx -> ValueSome idx
    | false, _ -> ValueNone

  /// <summary>Get the list of animation names in this clip set.</summary>
  let names(clips: Animation3DClips) : string[] =
    clips.ClipNames.Keys |> Seq.toArray

  /// <summary>Get the number of animation clips.</summary>
  let inline count(clips: Animation3DClips) : int = clips.Clips.Length

  /// <summary>Check if the clip set has no animations.</summary>
  let inline isEmpty(clips: Animation3DClips) : bool = clips.Clips.Length = 0

module Animation3DState =
  /// <summary>Create a new 3D animation state starting on the specified clip.</summary>
  /// <param name="model">The model to animate. Must be a unique instance per entity.</param>
  /// <param name="clips">The loaded animation clip set.</param>
  /// <param name="clipName">The name of the animation to start on.</param>
  /// <param name="fps">The animation playback speed in frames per second.</param>
  let create
    (model: Model)
    (clips: Animation3DClips)
    (clipName: string)
    (fps: float32)
    : Animation3DState =
    let idx =
      match clips.ClipNames.TryGetValue(clipName) with
      | true, i -> i
      | false, _ -> 0

    {
      Model = model
      Clips = clips
      CurrentClipIndex = idx
      CurrentFrame = 0.0f
      Speed = fps / 60.0f
      Loop = true
      Finished = false
      BlendTargetIndex = -1
      BlendTargetFrame = 0.0f
      BlendProgress = 0.0f
      BlendDuration = 0.0f
    }

  /// <summary>Create a new 3D animation state starting on the specified clip index.</summary>
  let createByIndex
    (model: Model)
    (clips: Animation3DClips)
    (clipIndex: int)
    (fps: float32)
    : Animation3DState =
    let idx =
      if clipIndex >= 0 && clipIndex < clips.Clips.Length then
        clipIndex
      else
        0

    {
      Model = model
      Clips = clips
      CurrentClipIndex = idx
      CurrentFrame = 0.0f
      Speed = fps / 60.0f
      Loop = true
      Finished = false
      BlendTargetIndex = -1
      BlendTargetFrame = 0.0f
      BlendProgress = 0.0f
      BlendDuration = 0.0f
    }

  /// <summary>Play an animation by name. Resets the frame if switching clips.</summary>
  let play (clipName: string) (state: Animation3DState) : Animation3DState =
    match state.Clips.ClipNames.TryGetValue(clipName) with
    | false, _ -> state
    | true, idx when idx = state.CurrentClipIndex && not state.Finished -> state
    | true, idx ->
        {
          state with
              CurrentClipIndex = idx
              CurrentFrame = 0.0f
              Finished = false
              BlendTargetIndex = -1
              BlendTargetFrame = 0.0f
              BlendProgress = 0.0f
              BlendDuration = 0.0f
        }

  /// <summary>
  /// Play by clip index (zero string allocation).
  /// </summary>
  /// <remarks>For maximum performance, resolve clip names to indices once at load time.</remarks>
  let playByIndex
    (clipIndex: int)
    (state: Animation3DState)
    : Animation3DState =
    if clipIndex = state.CurrentClipIndex && not state.Finished then
      state
    elif clipIndex < 0 || clipIndex >= state.Clips.Clips.Length then
      state
    else
      {
        state with
            CurrentClipIndex = clipIndex
            CurrentFrame = 0.0f
            Finished = false
            BlendTargetIndex = -1
            BlendTargetFrame = 0.0f
            BlendProgress = 0.0f
            BlendDuration = 0.0f
      }

  /// <summary>Play animation only if not already playing it.</summary>
  let playIfNot
    (clipName: string)
    (state: Animation3DState)
    : Animation3DState =
    match state.Clips.ClipNames.TryGetValue(clipName) with
    | true, idx when idx = state.CurrentClipIndex -> state
    | true, _ -> play clipName state
    | false, _ -> state

  /// <summary>
  /// Start blending from the current animation to a target animation.
  /// </summary>
  /// <param name="clipName">The name of the animation to blend towards.</param>
  /// <param name="duration">The blend duration in seconds.</param>
  /// <param name="state">The current animation state.</param>
  /// <remarks>
  /// During the blend, both animations play simultaneously and their bone transforms
  /// are interpolated using <c>UpdateModelAnimationEx</c>. Once the blend completes,
  /// the target animation becomes the current animation.
  /// </remarks>
  let blendTo
    (clipName: string)
    (duration: float32)
    (state: Animation3DState)
    : Animation3DState =
    match state.Clips.ClipNames.TryGetValue(clipName) with
    | false, _ -> state
    | true, idx when idx = state.CurrentClipIndex && state.BlendTargetIndex < 0 ->
      state
    | true, idx when idx = state.BlendTargetIndex -> state
    | true, idx ->
        {
          state with
              BlendTargetIndex = idx
              BlendTargetFrame = 0.0f
              BlendProgress = 0.0f
              BlendDuration = if duration > 0.0f then duration else 0.001f
        }

  /// <summary>Start blending to a target animation by clip index.</summary>
  let blendToByIndex
    (clipIndex: int)
    (duration: float32)
    (state: Animation3DState)
    : Animation3DState =
    if clipIndex < 0 || clipIndex >= state.Clips.Clips.Length then
      state
    elif clipIndex = state.CurrentClipIndex && state.BlendTargetIndex < 0 then
      state
    elif clipIndex = state.BlendTargetIndex then
      state
    else
      {
        state with
            BlendTargetIndex = clipIndex
            BlendTargetFrame = 0.0f
            BlendProgress = 0.0f
            BlendDuration = if duration > 0.0f then duration else 0.001f
      }

  /// <summary>Is currently blending between two animations?</summary>
  let inline isBlending(state: Animation3DState) = state.BlendTargetIndex >= 0

  /// <summary>Force restart the current animation from the beginning.</summary>
  let restart(state: Animation3DState) : Animation3DState = {
    state with
        CurrentFrame = 0.0f
        Finished = false
  }

  /// <summary>
  /// Advance the animation by delta time.
  /// </summary>
  /// <remarks>Call from your Elmish update function each frame. Does not apply to the model — use <c>applyToModel</c> after.</remarks>
  let update
    (deltaSeconds: float32)
    (state: Animation3DState)
    : Animation3DState =
    if state.Finished && state.BlendTargetIndex < 0 then
      state
    else
      let clip = state.Clips.ClipIndices.[state.CurrentClipIndex]

      if clip.KeyFrameCount <= 0 then
        state
      else
        let framesToAdvance = deltaSeconds * state.Speed * 60.0f
        let nextFrame = state.CurrentFrame + framesToAdvance

        let mutable s = state

        if nextFrame >= float32 clip.KeyFrameCount then
          if state.Loop then
            s <- {
              s with
                  CurrentFrame = nextFrame % float32 clip.KeyFrameCount
            }
          else
            s <- {
              s with
                  Finished = true
                  CurrentFrame = float32(clip.KeyFrameCount - 1)
            }
        else
          s <- { s with CurrentFrame = nextFrame }

        if s.BlendTargetIndex >= 0 then
          let targetClip = s.Clips.ClipIndices.[s.BlendTargetIndex]

          let nextTargetFrame = s.BlendTargetFrame + framesToAdvance

          let targetFrame =
            if targetClip.KeyFrameCount > 0 then
              if nextTargetFrame >= float32 targetClip.KeyFrameCount then
                nextTargetFrame % float32 targetClip.KeyFrameCount
              else
                nextTargetFrame
            else
              0.0f

          let newProgress = s.BlendProgress + deltaSeconds / s.BlendDuration

          if newProgress >= 1.0f then
            {
              s with
                  CurrentClipIndex = s.BlendTargetIndex
                  CurrentFrame = targetFrame
                  Finished = false
                  BlendTargetIndex = -1
                  BlendTargetFrame = 0.0f
                  BlendProgress = 0.0f
                  BlendDuration = 0.0f
            }
          else
            {
              s with
                  BlendTargetFrame = targetFrame
                  BlendProgress = newProgress
            }
        else
          s

  /// <summary>
  /// Apply the current animation frame to the model's bone matrices.
  /// </summary>
  /// <remarks>
  /// Calls <c>Raylib.UpdateModelAnimation</c> (or <c>UpdateModelAnimationEx</c> when blending)
  /// which mutates the model's internal bone data.
  /// Must be called before rendering with <c>DrawModel</c>.
  /// </remarks>
  let applyToModel(state: Animation3DState) : unit =
    if state.BlendTargetIndex >= 0 then
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
  let inline isFinished(state: Animation3DState) = state.Finished

  /// <summary>Is currently playing the specified animation?</summary>
  let isPlaying (clipName: string) (state: Animation3DState) =
    match state.Clips.ClipNames.TryGetValue(clipName) with
    | true, idx -> idx = state.CurrentClipIndex && not state.Finished
    | false, _ -> false

  /// <summary>Get the total duration of the current clip in seconds at the current speed.</summary>
  let inline duration(state: Animation3DState) =
    let clip = state.Clips.ClipIndices.[state.CurrentClipIndex]
    float32 clip.KeyFrameCount / (state.Speed * 60.0f)

  /// <summary>Get the name of the current animation clip.</summary>
  let currentClipName(state: Animation3DState) : string =
    state.Clips.ClipIndices.[state.CurrentClipIndex].NameToString()

  let inline withSpeed
    (speed: float32)
    (state: Animation3DState)
    : Animation3DState =
    { state with Speed = speed }

  let inline withLoop
    (loop: bool)
    (state: Animation3DState)
    : Animation3DState =
    { state with Loop = loop }

/// <summary>
/// A mesh with skeleton data for GPU skinning.
/// </summary>
/// <remarks>
/// Load once from a <c>Model</c> via <c>AnimatedMesh.fromModel</c>, then share across
/// all entities that use the same mesh. Each entity computes its own bone matrices
/// via <c>AnimatedMesh.computeBoneMatrices</c> and passes them to <c>DrawSkinnedMesh</c>.
/// This avoids per-entity model copies and CPU skinning — the GPU does the vertex transform.
/// </remarks>
type AnimatedMesh = {
  Mesh: Mesh
  BoneCount: int
  InverseBindPose: Matrix4x4[]
}

module AnimatedMesh =
  let private buildMatrix(t: Transform) : Matrix4x4 =
    let s = Matrix4x4.CreateScale(t.Scale.X, t.Scale.Y, t.Scale.Z)

    let r = Matrix4x4.CreateFromQuaternion(t.Rotation)

    let tr =
      Matrix4x4.CreateTranslation(
        t.Translation.X,
        t.Translation.Y,
        t.Translation.Z
      )

    s * r * tr

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
      let invBindPose = Array.zeroCreate<Matrix4x4> boneCount

      for i = 0 to boneCount - 1 do
        invBindPose[i] <-
          let mutable m = buildMatrix bindPose[i]
          Matrix4x4.Invert(m, &m) |> ignore
          m

      let meshes = model.MeshesAsSpan()

      if meshes.Length = 0 then
        ValueNone
      else
        ValueSome {
          Mesh = meshes[0]
          BoneCount = boneCount
          InverseBindPose = invBindPose
        }

  /// <summary>
  /// Compute bone matrices for a given animation clip and frame.
  /// </summary>
  /// <remarks>
  /// This is pure math — does not mutate the model. The result can be passed
  /// directly to <c>DrawSkinnedMesh</c> for GPU skinning.
  /// The algorithm matches raylib's <c>UpdateModelAnimation</c>:
  /// interpolates keyframes (lerp/slerp), builds TRS matrices, and multiplies
  /// by the inverse bind pose.
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
        let ct = cfPoses[i]
        let nt = nfPoses[i]

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

          s * r * tr

        matrices[i] <- mesh.InverseBindPose[i] * currentPoseMatrix

      matrices
