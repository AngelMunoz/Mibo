namespace Mibo.Animation

open System
open System.Collections.Generic

// ─────────────────────────────────────────────────────────────────────────────
// Backend-neutral 3D skeletal animation state machine.
//
// The playback clock (frame advance, blend progress, loop wrap) operates on
// ints and floats only — it never touches bone data or native backend types.
// This module is the single implementation shared by all backends.
//
// Each backend loads its own clip data (raylib's ModelAnimation[], MonoGame's
// Animation3DClip[]) and builds an Animation3DClipsInfo from it at load time.
// The backend then uses this state machine to drive playback, and does its own
// bone-matrix computation / model mutation at render time.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Backend-neutral animation clip metadata. Carries only what the playback clock
/// needs: clip names and their keyframe counts.
/// </summary>
/// <remarks>
/// Build this from your backend's loaded clip data at load time. The backend
/// keeps its own rich clip objects alongside for bone sampling; this struct is
/// the shareable subset the state machine reads.
/// </remarks>
[<Struct>]
type Animation3DClipsInfo = {
  /// <summary>Map of clip name to index (resolved at load time for zero-allocation playback).</summary>
  ClipNames: IReadOnlyDictionary<string, int>

  /// <summary>Clip names indexed by clip index (reverse lookup for currentClipName).</summary>
  ClipNamesByIndex: string[]

  /// <summary>Keyframe count per clip, indexed by clip index. Read by the clock loop each frame.</summary>
  KeyFrameCounts: int[]
}

/// <summary>Functions for building and querying <see cref="T:Mibo.Animation.Animation3DClipsInfo"/>.</summary>
module Animation3DClipsInfo =

  /// <summary>Build clip info from parallel name and keyframe-count arrays.</summary>
  let create(clipNames: (string * int)[]) : Animation3DClipsInfo =
    let dict = Dictionary<string, int>(clipNames.Length)
    let namesByIndex = Array.zeroCreate<string> clipNames.Length
    let counts = Array.zeroCreate<int> clipNames.Length

    for i = 0 to clipNames.Length - 1 do
      let name, count = clipNames[i]
      dict[name] <- i
      namesByIndex[i] <- name
      counts[i] <- count

    {
      ClipNames = dict
      ClipNamesByIndex = namesByIndex
      KeyFrameCounts = counts
    }

  /// <summary>Try to get the index for an animation name.</summary>
  let inline tryGetClipIndex
    (name: string)
    (clips: Animation3DClipsInfo)
    : int voption =
    match clips.ClipNames.TryGetValue(name) with
    | true, idx -> ValueSome idx
    | _ -> ValueNone

  /// <summary>Get the list of animation names.</summary>
  let names(clips: Animation3DClipsInfo) : string[] =
    clips.ClipNames.Keys |> Seq.toArray

  /// <summary>Get the number of animation clips.</summary>
  let inline count(clips: Animation3DClipsInfo) : int =
    clips.KeyFrameCounts.Length

  /// <summary>Check if the clip set is empty.</summary>
  let inline isEmpty(clips: Animation3DClipsInfo) : bool =
    clips.KeyFrameCounts.Length = 0

/// <summary>
/// Addresses a bone of an animated model for pose queries and attachment draws.
/// <c>ByName</c> is the authoring-friendly path (resolved through the mesh's
/// name→index lookup); <c>ByIndex</c> is the fast path (no lookup).
/// A missing bone is never an error: queries return <c>ValueNone</c> and
/// attachment draws emit no command.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type BoneRef =
  /// <summary>Look the bone up by its authored name (e.g. "Hand_R").</summary>
  | ByName of name: string
  /// <summary>Address the bone directly by index (zero lookup cost).</summary>
  | ByIndex of index: int

/// <summary>
/// Runtime state for a playing 3D skeletal animation. Pure value — no backend
/// types. Each entity that needs independent animation must have its own copy.
/// </summary>
[<Struct>]
type Animation3DState = {
  Clips: Animation3DClipsInfo
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

/// <summary>Pure playback functions for <see cref="T:Mibo.Animation.Animation3DState"/>.</summary>
module Animation3DState =

  /// <summary>Create a new animation state starting on the named clip.</summary>
  /// <param name="clips">Clip metadata (names + keyframe counts).</param>
  /// <param name="clipName">The animation to start on (falls back to clip 0 if absent).</param>
  /// <param name="fps">Playback speed in frames per second.</param>
  let create
    (clips: Animation3DClipsInfo)
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

  /// <summary>Create a new animation state starting on the specified clip index.</summary>
  let createByIndex
    (clips: Animation3DClipsInfo)
    (clipIndex: int)
    (fps: float32)
    : Animation3DState =
    let idx =
      if clipIndex >= 0 && clipIndex < clips.KeyFrameCounts.Length then
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
    elif clipIndex < 0 || clipIndex >= state.Clips.KeyFrameCounts.Length then
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
  /// <param name="clipName">The animation to blend towards.</param>
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
    if clipIndex < 0 || clipIndex >= state.Clips.KeyFrameCounts.Length then
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
  /// <remarks>
  /// Call from your update function each frame. This is pure clock logic — it
  /// does not compute bone matrices or mutate any model. The backend does that
  /// at render time using the updated indices and frames.
  /// </remarks>
  let update
    (deltaSeconds: float32)
    (state: Animation3DState)
    : Animation3DState =
    if state.Finished && state.BlendTargetIndex < 0 then
      state
    elif state.Clips.KeyFrameCounts.Length = 0 then
      state
    else
      let keyFrameCount = state.Clips.KeyFrameCounts[state.CurrentClipIndex]

      if keyFrameCount <= 0 then
        state
      else
        let framesToAdvance = deltaSeconds * state.Speed * 60.0f
        let nextFrame = state.CurrentFrame + framesToAdvance

        let mutable s = state

        if nextFrame >= float32 keyFrameCount then
          if state.Loop then
            s <- {
              s with
                  CurrentFrame = nextFrame % float32 keyFrameCount
            }
          else
            s <- {
              s with
                  Finished = true
                  CurrentFrame = float32(keyFrameCount - 1)
            }
        else
          s <- { s with CurrentFrame = nextFrame }

        if s.BlendTargetIndex >= 0 then
          let targetKeyFrameCount = s.Clips.KeyFrameCounts[s.BlendTargetIndex]

          let nextTargetFrame = s.BlendTargetFrame + framesToAdvance

          let targetFrame =
            if targetKeyFrameCount > 0 then
              if nextTargetFrame >= float32 targetKeyFrameCount then
                if s.Loop then
                  nextTargetFrame % float32 targetKeyFrameCount
                else
                  float32(targetKeyFrameCount - 1)
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
    if state.Clips.KeyFrameCounts.Length = 0 then
      0.0f
    else
      let keyFrameCount = state.Clips.KeyFrameCounts[state.CurrentClipIndex]
      float32 keyFrameCount / (state.Speed * 60.0f)

  /// <summary>Get the name of the current animation clip.</summary>
  let currentClipName(state: Animation3DState) : string =
    if state.Clips.KeyFrameCounts.Length = 0 then
      ""
    else
      state.Clips.ClipNamesByIndex[state.CurrentClipIndex]

  /// <summary>Set the playback speed multiplier.</summary>
  let inline withSpeed
    (speed: float32)
    (state: Animation3DState)
    : Animation3DState =
    { state with Speed = speed }

  /// <summary>Set whether the current clip loops.</summary>
  let inline withLoop
    (loop: bool)
    (state: Animation3DState)
    : Animation3DState =
    { state with Loop = loop }
