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
  let names(clips: Animation3DClips) : string[] =
    Animation3DClipsInfo.names clips.ClipsInfo

  /// <summary>Get the number of animation clips.</summary>
  let inline count(clips: Animation3DClips) : int = clips.Clips.Length

  /// <summary>Check if the clip set has no animations.</summary>
  let inline isEmpty(clips: Animation3DClips) : bool = clips.Clips.Length = 0

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
  let create
    (model: Model)
    (clips: Animation3DClips)
    (clipName: string)
    (fps: float32)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.create clips.ClipsInfo clipName fps

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
  let createByIndex
    (model: Model)
    (clips: Animation3DClips)
    (clipIndex: int)
    (fps: float32)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.createByIndex
        clips.ClipsInfo
        clipIndex
        fps

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
  let play (clipName: string) (state: Animation3DState) : Animation3DState =
    let core = Mibo.Animation.Animation3DState.play clipName (toCoreState state)
    fromCoreState state core

  /// <summary>
  /// Play by clip index (zero string allocation).
  /// </summary>
  let playByIndex
    (clipIndex: int)
    (state: Animation3DState)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.playByIndex clipIndex (toCoreState state)

    fromCoreState state core

  /// <summary>Play animation only if not already playing it.</summary>
  let playIfNot
    (clipName: string)
    (state: Animation3DState)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.playIfNot clipName (toCoreState state)

    fromCoreState state core

  /// <summary>
  /// Start blending from the current animation to a target animation.
  /// </summary>
  /// <param name="clipName">The animation to blend towards.</param>
  /// <param name="duration">The blend duration in seconds.</param>
  let blendTo
    (clipName: string)
    (duration: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.blendTo
        clipName
        duration
        (toCoreState state)

    fromCoreState state core

  /// <summary>Start blending to a target animation by clip index.</summary>
  let blendToByIndex
    (clipIndex: int)
    (duration: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.blendToByIndex
        clipIndex
        duration
        (toCoreState state)

    fromCoreState state core

  /// <summary>Is currently blending between two animations?</summary>
  let inline isBlending(state: Animation3DState) =
    Mibo.Animation.Animation3DState.isBlending(toCoreState state)

  /// <summary>Force restart the current animation from the beginning.</summary>
  let restart(state: Animation3DState) : Animation3DState =
    let core = Mibo.Animation.Animation3DState.restart(toCoreState state)
    fromCoreState state core

  /// <summary>
  /// Advance the animation by delta time.
  /// </summary>
  /// <remarks>Call from your Elmish update function each frame. Does not apply to the model — use <c>applyToModel</c> after.</remarks>
  let update
    (deltaSeconds: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.update deltaSeconds (toCoreState state)

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
    Mibo.Animation.Animation3DState.isFinished(toCoreState state)

  /// <summary>Is currently playing the specified animation?</summary>
  let isPlaying (clipName: string) (state: Animation3DState) =
    Mibo.Animation.Animation3DState.isPlaying clipName (toCoreState state)

  /// <summary>Get the total duration of the current clip in seconds at the current speed.</summary>
  let inline duration(state: Animation3DState) =
    Mibo.Animation.Animation3DState.duration(toCoreState state)

  /// <summary>Get the name of the current animation clip.</summary>
  let currentClipName(state: Animation3DState) : string =
    Mibo.Animation.Animation3DState.currentClipName(toCoreState state)

  let inline withSpeed
    (speed: float32)
    (state: Animation3DState)
    : Animation3DState =
    let core =
      Mibo.Animation.Animation3DState.withSpeed speed (toCoreState state)

    fromCoreState state core

  let inline withLoop
    (loop: bool)
    (state: Animation3DState)
    : Animation3DState =
    let core = Mibo.Animation.Animation3DState.withLoop loop (toCoreState state)

    fromCoreState state core

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

          s * r * tr

        matrices[i] <- mesh.InverseBindPose[i] * currentPoseMatrix

      matrices
