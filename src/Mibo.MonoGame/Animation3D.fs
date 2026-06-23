namespace Mibo.Animation

open System
open System.Collections.Generic
open Assimp
open Microsoft.Xna.Framework

// ─────────────────────────────────────────────────────────────────────────────
// MonoGame 3D skeletal animation — runtime clip/state API.
//
// Ported from Mibo.Raylib/Animation3D.fs. Key adaptations:
//   - Clips are built at runtime from Assimp's Scene (via Animation3DClips.fromScene)
//     instead of raylib's LoadModelAnimations.
//   - Animation3DState drops the Model field — MonoGame doesn't mutate model
//     bones like raylib's UpdateModelAnimation. Instead, computeBonePalette
//     returns a Matrix[] the caller passes to Draw3D.drawSkinnedMesh.
//   - AnimatedMesh + computeBoneMatrices (GPU skinning path) are preserved from
//     the raylib port.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single keyframe: a time offset (in ticks) and a bone transform matrix.
/// </summary>
/// <remarks>
/// Assimp's per-channel TRS keyframes are merged into a single Matrix per bone
/// per keyframe time during clip construction (matching raylib's ModelAnimation
/// frame-poses). This keeps the hot-path interpolation loop simple: one lerp +
/// one matrix multiply per bone.
/// </remarks>
[<Struct>]
type Animation3DKeyframe = {
  TimeTicks: float32
  Transform: Matrix
}

/// <summary>
/// A per-bone animation channel: a sorted array of keyframes for one bone.
/// </summary>
type Animation3DChannel = {
  BoneName: string
  Keyframes: Animation3DKeyframe[]
}

/// <summary>
/// A single animation clip with a name, duration (in seconds), and per-bone channels.
/// </summary>
type Animation3DClip = {
  Name: string
  DurationSeconds: float32
  /// <summary>Per-bone channels, keyed by bone name.</summary>
  Channels: IReadOnlyDictionary<string, Animation3DChannel>
  /// <summary>Max keyframe count across all channels (used by the update loop).</summary>
  KeyframeCount: int
}

/// <summary>
/// A loaded set of 3D animation clips extracted from a model file.
/// </summary>
/// <remarks>
/// This is a shared data container — load once, use across multiple
/// <c>Animation3DState</c> instances. Use <c>Animation3DClips.names</c> to
/// discover available animations at runtime.
/// </remarks>
type Animation3DClips = {
  Clips: Animation3DClip[]
  ClipNames: IReadOnlyDictionary<string, int>
}

/// <summary>
/// Runtime state for a playing 3D skeletal animation.
/// </summary>
/// <remarks>
/// Each entity that needs independent animation must have its own
/// <c>Animation3DState</c>. Call <c>computeBonePalette</c> after <c>update</c>
/// to get the bone matrices for <c>Draw3D.drawSkinnedMesh</c>.
/// </remarks>
[<Struct>]
type Animation3DState = {
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

/// <summary>
/// A mesh with skeleton data for GPU skinning.
/// </summary>
/// <remarks>
/// Load once from an Assimp <c>Scene</c> via <c>AnimatedMesh.fromScene</c>, then
/// share across all entities that use the same mesh. Each entity computes its
/// own bone matrices via <c>AnimatedMesh.computeBoneMatrices</c> and passes
/// them to <c>Draw3D.drawSkinnedMesh</c>.
/// </remarks>
type AnimatedMesh = {
  BoneCount: int
  BoneNames: string[]
  InverseBindPose: Matrix[]
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers (internal)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Internal helpers for Assimp keyframe merging and sampling.</summary>
module private Animation3DHelpers =

  /// <summary>Sample a list of VectorKeys at time t (linear interpolation).</summary>
  let sampleVectorKeys (keys: ResizeArray<VectorKey>) (t: float32) : Vector3 =

    if keys.Count = 0 then
      Vector3.Zero
    elif keys.Count = 1 then
      Vector3.op_Implicit keys[0].Value
    else
      let mutable lo = 0
      let mutable hi = keys.Count - 1

      if float32 keys[lo].Time >= t then
        Vector3.op_Implicit keys[0].Value
      elif float32 keys[hi].Time <= t then
        Vector3.op_Implicit keys[hi].Value
      else
        while hi - lo > 1 do
          let mid = (lo + hi) / 2

          if float32 keys[mid].Time <= t then lo <- mid else hi <- mid

        let k0 = keys[lo]
        let k1 = keys[hi]
        let t0 = float32 k0.Time
        let t1 = float32 k1.Time
        let range = t1 - t0
        let blend = if range <= 0.0f then 0.0f else (t - t0) / range
        Vector3.Lerp(k0.Value, k1.Value, blend)

  /// <summary>Sample a list of QuaternionKeys at time t (spherical interpolation).</summary>
  let sampleQuaternionKeys
    (keys: ResizeArray<QuaternionKey>)
    (t: float32)
    : Quaternion =

    if keys.Count = 0 then
      Quaternion.Identity
    elif keys.Count = 1 then
      Quaternion.op_Implicit keys[0].Value
    else
      let mutable lo = 0
      let mutable hi = keys.Count - 1

      if float32 keys[lo].Time >= t then
        Quaternion.op_Implicit keys[0].Value
      elif float32 keys[hi].Time <= t then
        Quaternion.op_Implicit keys[hi].Value
      else
        while hi - lo > 1 do
          let mid = (lo + hi) / 2

          if float32 keys[mid].Time <= t then lo <- mid else hi <- mid

        let k0 = keys[lo]
        let k1 = keys[hi]
        let t0 = float32 k0.Time
        let t1 = float32 k1.Time
        let range = t1 - t0
        let blend = if range <= 0.0f then 0.0f else (t - t0) / range
        Quaternion.Slerp(k0.Value, k1.Value, blend)

  /// <summary>Build a TRS (Scale * Rotation * Translation) matrix.</summary>
  let buildTrsMatrix
    (scale: Vector3)
    (rotation: Quaternion)
    (translation: Vector3)
    : Matrix =
    let s = Matrix.CreateScale(scale)
    let r = Matrix.CreateFromQuaternion(rotation)
    let t = Matrix.CreateTranslation(translation)
    s * r * t

  /// <summary>
  /// Merge Assimp's split TRS keyframes into a sorted array of Matrix keyframes
  /// for a single bone channel, matching raylib's frame-pose model.
  /// </summary>
  /// <remarks>
  /// Assimp gives separate PositionKeys, RotationKeys, ScalingKeys — possibly at
  /// different time samples. We build a unified timeline at every unique time,
  /// interpolating each component as needed (matching OpenAssetImporter's approach).
  /// </remarks>
  let mergeChannel
    (boneName: string)
    (posKeys: ResizeArray<VectorKey>)
    (rotKeys: ResizeArray<QuaternionKey>)
    (scaleKeys: ResizeArray<VectorKey>)
    : Animation3DChannel =

    // Collect all unique times across the three key types.
    let times = HashSet<float32>()

    if not(isNull posKeys) then
      for k in posKeys do
        times.Add(float32 k.Time) |> ignore

    if not(isNull rotKeys) then
      for k in rotKeys do
        times.Add(float32 k.Time) |> ignore

    if not(isNull scaleKeys) then
      for k in scaleKeys do
        times.Add(float32 k.Time) |> ignore

    let sortedTimes = Array.ofSeq times
    Array.sortInPlace sortedTimes

    let keyframes = Array.zeroCreate<Animation3DKeyframe> sortedTimes.Length

    for i = 0 to sortedTimes.Length - 1 do
      let t = sortedTimes[i]

      let translation =
        if isNull posKeys || posKeys.Count = 0 then
          Vector3.Zero
        else
          sampleVectorKeys posKeys t

      let rotation =
        if isNull rotKeys || rotKeys.Count = 0 then
          Quaternion.Identity
        else
          sampleQuaternionKeys rotKeys t

      let scale =
        if isNull scaleKeys || scaleKeys.Count = 0 then
          Vector3.One
        else
          sampleVectorKeys scaleKeys t

      keyframes.[i] <- {
        TimeTicks = t
        Transform = buildTrsMatrix scale rotation translation
      }

    {
      BoneName = boneName
      Keyframes = keyframes
    }

// ─────────────────────────────────────────────────────────────────────────────
// Animation3DClips
// ─────────────────────────────────────────────────────────────────────────────

module Animation3DClips =

  /// <summary>
  /// Create a clip set from an Assimp <c>Scene</c> loaded via
  /// <c>AssimpContext.ImportFile</c>.
  /// </summary>
  /// <remarks>
  /// This is the MonoGame analog of raylib's <c>LoadModelAnimations</c> +
  /// <c>Animation3DClips.fromModelAnimations</c>. The <c>Scene</c> is parsed once;
  /// the resulting <c>Animation3DClips</c> is shared across all entities.
  /// </remarks>
  let fromScene(scene: Scene) : Animation3DClips =
    if isNull scene || scene.AnimationCount = 0 then
      {
        Clips = [||]
        ClipNames = Dictionary<string, int>()
      }
    else
      let clips = Array.zeroCreate<Animation3DClip> scene.AnimationCount

      for i = 0 to scene.AnimationCount - 1 do
        let anim = scene.Animations[i]

        let tps =
          if anim.TicksPerSecond > 0.0 then
            anim.TicksPerSecond
          else
            25.0

        let durationSeconds = float32(anim.DurationInTicks / tps)

        let channels = Dictionary<string, Animation3DChannel>()
        let mutable maxKeys = 0

        for c = 0 to anim.NodeAnimationChannelCount - 1 do
          let ch = anim.NodeAnimationChannels[c]
          let boneName = ch.NodeName

          let channel =
            Animation3DHelpers.mergeChannel
              boneName
              (if ch.HasPositionKeys then ch.PositionKeys else null)
              (if ch.HasRotationKeys then ch.RotationKeys else null)
              (if ch.HasScalingKeys then ch.ScalingKeys else null)

          if channel.Keyframes.Length > maxKeys then
            maxKeys <- channel.Keyframes.Length

          channels[boneName] <- channel

        clips[i] <- {
          Name = anim.Name
          DurationSeconds = durationSeconds
          Channels = channels
          KeyframeCount = maxKeys
        }

      let nameDict = Dictionary<string, int> clips.Length

      for i = 0 to clips.Length - 1 do
        nameDict[clips[i].Name] <- i

      { Clips = clips; ClipNames = nameDict }

  /// <summary>
  /// Try to get the index for an animation name.
  /// </summary>
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

// ─────────────────────────────────────────────────────────────────────────────
// Animation3DState
// ─────────────────────────────────────────────────────────────────────────────

module Animation3DState =

  /// <summary>Create a new 3D animation state starting on the specified clip.</summary>
  /// <param name="clips">The loaded animation clip set.</param>
  /// <param name="clipName">The name of the animation to start on.</param>
  /// <param name="fps">The animation playback speed in frames per second.</param>
  let create
    (clips: Animation3DClips)
    (clipName: string)
    (fps: float32)
    : Animation3DState =
    let idx =
      match clips.ClipNames.TryGetValue(clipName) with
      | true, i -> i
      | false, _ -> 0

    {
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
  /// <remarks>Call from your Elmish update function each frame. Does not compute
  /// bone matrices — use <c>computeBonePalette</c> after.</remarks>
  let update
    (deltaSeconds: float32)
    (state: Animation3DState)
    : Animation3DState =
    if state.Finished && state.BlendTargetIndex < 0 then
      state
    elif state.Clips.Clips.Length = 0 then
      state
    else
      let clip = state.Clips.Clips.[state.CurrentClipIndex]

      if clip.KeyframeCount <= 0 then
        state
      else
        let framesToAdvance = deltaSeconds * state.Speed * 60.0f
        let nextFrame = state.CurrentFrame + framesToAdvance

        let mutable s = state

        if nextFrame >= float32 clip.KeyframeCount then
          if state.Loop then
            s <- {
              s with
                  CurrentFrame = nextFrame % float32 clip.KeyframeCount
            }
          else
            s <- {
              s with
                  Finished = true
                  CurrentFrame = float32(clip.KeyframeCount - 1)
            }
        else
          s <- { s with CurrentFrame = nextFrame }

        if s.BlendTargetIndex >= 0 then
          let targetClip = s.Clips.Clips.[s.BlendTargetIndex]

          let nextTargetFrame = s.BlendTargetFrame + framesToAdvance

          let targetFrame =
            if targetClip.KeyframeCount > 0 then
              if nextTargetFrame >= float32 targetClip.KeyframeCount then
                nextTargetFrame % float32 targetClip.KeyframeCount
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

  /// <summary>Is the current animation finished? (always false for looping animations).</summary>
  let inline isFinished(state: Animation3DState) = state.Finished

  /// <summary>Is currently playing the specified animation?</summary>
  let isPlaying (clipName: string) (state: Animation3DState) =
    match state.Clips.ClipNames.TryGetValue(clipName) with
    | true, idx -> idx = state.CurrentClipIndex && not state.Finished
    | false, _ -> false

  /// <summary>Get the total duration of the current clip in seconds at the current speed.</summary>
  let inline duration(state: Animation3DState) =
    let clip = state.Clips.Clips[state.CurrentClipIndex]
    float32 clip.KeyframeCount / (state.Speed * 60.0f)

  /// <summary>Get the name of the current animation clip.</summary>
  let currentClipName(state: Animation3DState) : string =
    state.Clips.Clips[state.CurrentClipIndex].Name

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

  // ───────────────────────────────────────────────────────────────────────────
  // Bone palette computation
  // ───────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Sample a single bone channel at a fractional frame index, returning a
  /// bone-local transform matrix (SRT).
  /// </summary>
  let private sampleChannel
    (clip: Animation3DClip)
    (boneName: string)
    (frame: float32)
    : Matrix =
    match clip.Channels.TryGetValue(boneName) with
    | false, _ -> Matrix.Identity
    | true, ch ->
      if ch.Keyframes.Length = 0 then
        Matrix.Identity
      elif ch.Keyframes.Length = 1 then
        ch.Keyframes[0].Transform
      else
        let currentFrame = int frame
        let nextFrame = currentFrame + 1

        let blend: float32 =
          let v = frame - float32 currentFrame
          Math.Clamp(v, 0.0f, 1.0f)

        let cf =
          if currentFrame >= ch.Keyframes.Length then
            currentFrame % ch.Keyframes.Length
          else
            currentFrame

        let nf =
          if nextFrame >= ch.Keyframes.Length then
            nextFrame % ch.Keyframes.Length
          else
            nextFrame

        let t0 = ch.Keyframes[cf].Transform
        let t1 = ch.Keyframes[nf].Transform
        Matrix.Lerp(t0, t1, blend)

  /// <summary>
  /// Compute the bone matrix palette for the current animation frame.
  /// </summary>
  /// <remarks>
  /// This is the MonoGame analog of raylib's <c>UpdateModelAnimation</c>. Instead
  /// of mutating the model, it returns a <c>Matrix[]</c> the caller passes to
  /// <c>Draw3D.drawSkinnedMesh</c>. Each entry is
  /// <c>InverseBindPose[i] * currentPoseMatrix</c> — the standard skinning palette.
  /// When blending, the two clips' bone matrices are linearly interpolated by
  /// <c>BlendProgress</c>.
  /// </remarks>
  let computeBonePalette
    (mesh: AnimatedMesh)
    (state: Animation3DState)
    : Matrix[] =
    let boneCount = mesh.BoneCount
    let matrices = Array.zeroCreate<Matrix> boneCount

    if boneCount <= 0 || state.Clips.Clips.Length = 0 then
      matrices
    else
      let clip = state.Clips.Clips[state.CurrentClipIndex]

      if state.BlendTargetIndex >= 0 then
        let clipB = state.Clips.Clips[state.BlendTargetIndex]
        let blend = state.BlendProgress

        for i = 0 to boneCount - 1 do
          let boneName = mesh.BoneNames[i]
          let poseA = sampleChannel clip boneName state.CurrentFrame
          let poseB = sampleChannel clipB boneName state.BlendTargetFrame
          let blended = Matrix.Lerp(poseA, poseB, blend)
          matrices.[i] <- mesh.InverseBindPose[i] * blended
      else
        for i = 0 to boneCount - 1 do
          let boneName = mesh.BoneNames[i]
          let pose = sampleChannel clip boneName state.CurrentFrame
          matrices.[i] <- mesh.InverseBindPose[i] * pose

      matrices

// ─────────────────────────────────────────────────────────────────────────────
// AnimatedMesh
// ─────────────────────────────────────────────────────────────────────────────

module AnimatedMesh =

  /// <summary>
  /// Extract an <c>AnimatedMesh</c> from an Assimp <c>Scene</c>.
  /// </summary>
  /// <remarks>
  /// Reads bone names and inverse-bind (offset) matrices from the first mesh
  /// that has bones. Returns <c>ValueNone</c> if no mesh has bones.
  /// </remarks>
  let fromScene(scene: Scene) : AnimatedMesh voption =
    if isNull scene || scene.MeshCount = 0 then
      ValueNone
    else
      let mutable meshIdx = 0
      let mutable found = false

      while meshIdx < scene.MeshCount && not found do
        if scene.Meshes[meshIdx].HasBones then
          found <- true
        else
          meshIdx <- meshIdx + 1

      if not found then
        ValueNone
      else
        let mesh = scene.Meshes[meshIdx]
        let boneCount = mesh.BoneCount
        let boneNames = Array.zeroCreate<string> boneCount
        let invBind = Array.zeroCreate<Matrix> boneCount

        for i = 0 to boneCount - 1 do
          let bone = mesh.Bones[i]
          boneNames[i] <- bone.Name
          // Assimp's OffsetMatrix is already the inverse-bind matrix.
          // MonoGame's content pipeline transposes it (Matrix.Transpose);
          // since we bypass the pipeline and read Assimp directly, we use
          // the matrix as-is (Assimp gives row-major, System.Numerics is row-major).
          invBind[i] <- Matrix.op_Implicit bone.OffsetMatrix

        ValueSome {
          BoneCount = boneCount
          BoneNames = boneNames
          InverseBindPose = invBind
        }

  /// <summary>
  /// Compute bone matrices for a given animation clip and frame.
  /// </summary>
  /// <remarks>
  /// This is pure math — does not mutate the model. The result can be passed
  /// directly to <c>Draw3D.drawSkinnedMesh</c> for GPU skinning. The algorithm
  /// matches raylib's <c>UpdateModelAnimation</c>: interpolates keyframes (lerp),
  /// builds TRS matrices (already merged into keyframes), and multiplies by the
  /// inverse bind pose.
  /// </remarks>
  let computeBoneMatrices
    (clip: Animation3DClip)
    (frame: float32)
    (mesh: AnimatedMesh)
    : Matrix[] =
    let boneCount = mesh.BoneCount
    let matrices = Array.zeroCreate<Matrix> boneCount

    if clip.KeyframeCount <= 0 || boneCount <= 0 then
      matrices
    else
      for i = 0 to boneCount - 1 do
        let boneName = mesh.BoneNames.[i]

        match clip.Channels.TryGetValue(boneName) with
        | false, _ -> matrices[i] <- mesh.InverseBindPose[i]
        | true, ch ->
          if ch.Keyframes.Length = 0 then
            matrices[i] <- mesh.InverseBindPose[i]
          elif ch.Keyframes.Length = 1 then
            matrices[i] <- mesh.InverseBindPose[i] * ch.Keyframes[0].Transform
          else
            let currentFrame = int frame
            let nextFrame = currentFrame + 1

            let blend: float32 =
              let v = frame - float32 currentFrame
              Math.Clamp(v, 0.0f, 1.0f)

            let cf =
              if currentFrame >= ch.Keyframes.Length then
                currentFrame % ch.Keyframes.Length
              else
                currentFrame

            let nf =
              if nextFrame >= ch.Keyframes.Length then
                nextFrame % ch.Keyframes.Length
              else
                nextFrame

            let t0 = ch.Keyframes[cf].Transform
            let t1 = ch.Keyframes[nf].Transform
            let pose = Matrix.Lerp(t0, t1, blend)
            matrices[i] <- mesh.InverseBindPose[i] * pose

      matrices
