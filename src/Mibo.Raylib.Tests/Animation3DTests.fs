module Mibo.Raylib.Tests.Animation3D

#nowarn "9"

open System
open System.Numerics
open System.Runtime.InteropServices
open Expecto
open Raylib_cs
open Mibo.Animation

// ──────────────────────────────────────────────
// Helpers — construct ModelAnimation for testing
// ──────────────────────────────────────────────

let private makeModelAnimation
  (name: string)
  (boneCount: int)
  (keyframeCount: int)
  (poses: Transform[][])
  : ModelAnimation =
  let structSize = Marshal.SizeOf<ModelAnimation>()
  let mem = Marshal.AllocHGlobal(structSize)

  // Zero the struct
  for i = 0 to structSize - 1 do
    Marshal.WriteByte(mem + nativeint i, 0uy)

  // Write name bytes at offset 0 (fixed sbyte Name[32])
  let nameBytes = System.Text.Encoding.UTF8.GetBytes(name)
  Marshal.Copy(nameBytes, 0, mem, nameBytes.Length)
  Marshal.WriteByte(mem + nativeint nameBytes.Length, 0uy)

  // Write boneCount at offset 32
  Marshal.WriteInt32(mem + 32n, boneCount)

  // Write keyframeCount at offset 36
  Marshal.WriteInt32(mem + 36n, keyframeCount)

  // Allocate keyframePoses pointer array at offset 40
  if keyframeCount > 0 && poses.Length > 0 then
    let ptrSize = IntPtr.Size
    let posesPtrArray = Marshal.AllocHGlobal(nativeint(ptrSize * keyframeCount))

    for kf = 0 to keyframeCount - 1 do
      let frameData = if kf < poses.Length then poses.[kf] else poses.[0]
      let transformSize = Marshal.SizeOf<Transform>()
      let framePtr = Marshal.AllocHGlobal(nativeint(transformSize * boneCount))

      for b = 0 to boneCount - 1 do
        let t = if b < frameData.Length then frameData.[b] else Transform()

        Marshal.StructureToPtr(
          t,
          framePtr + nativeint(b * transformSize),
          false
        )

      Marshal.WriteIntPtr(posesPtrArray + nativeint(kf * ptrSize), framePtr)

    Marshal.WriteIntPtr(mem + 40n, posesPtrArray)
  else
    Marshal.WriteIntPtr(mem + 40n, IntPtr.Zero)

  Marshal.PtrToStructure<ModelAnimation>(mem)

let private makeTestClips() =
  let idlePoses = [|
    for _ in 0..10 -> [| Transform(Translation = Vector3(0.0f, 0.1f, 0.0f)) |]
  |]

  let walkPoses = [|
    for _ in 0..20 -> [| Transform(Translation = Vector3(0.0f, 0.0f, 1.0f)) |]
  |]

  let jumpPoses = [|
    for _ in 0..5 -> [| Transform(Translation = Vector3(0.0f, 2.0f, 0.0f)) |]
  |]

  [|
    makeModelAnimation "idle" 1 11 idlePoses
    makeModelAnimation "walk" 1 21 walkPoses
    makeModelAnimation "jump" 1 6 jumpPoses
  |]

// ──────────────────────────────────────────────
// Animation3DClips Tests
// ──────────────────────────────────────────────

let clipsTests =
  testList "Animation3DClips" [
    testList "fromModelAnimations" [
      test "creates clips with correct count" {
        let anims = makeTestClips()
        let clips = Animation3DClips.fromModelAnimations anims
        Expect.equal (Animation3DClips.count clips) 3 "Should have 3 clips"
      }

      test "creates name dictionary" {
        let anims = makeTestClips()
        let clips = Animation3DClips.fromModelAnimations anims
        let names = Animation3DClips.names clips |> Array.sort
        Expect.equal names [| "idle"; "jump"; "walk" |] "Should have all names"
      }

      test "isEmpty returns false for non-empty" {
        let anims = makeTestClips()
        let clips = Animation3DClips.fromModelAnimations anims
        Expect.isFalse (Animation3DClips.isEmpty clips) "Should not be empty"
      }

      test "isEmpty returns true for empty" {
        let clips = Animation3DClips.fromModelAnimations [||]
        Expect.isTrue (Animation3DClips.isEmpty clips) "Should be empty"
      }
    ]

    testList "tryGetClipIndex" [
      test "returns index for existing clip" {
        let anims = makeTestClips()
        let clips = Animation3DClips.fromModelAnimations anims
        let idx = Animation3DClips.tryGetClipIndex "walk" clips
        Expect.equal idx (ValueSome 1) "walk should be at index 1"
      }

      test "returns ValueNone for missing clip" {
        let anims = makeTestClips()
        let clips = Animation3DClips.fromModelAnimations anims
        let idx = Animation3DClips.tryGetClipIndex "nonexistent" clips
        Expect.equal idx ValueNone "Missing should return ValueNone"
      }
    ]
  ]

// ──────────────────────────────────────────────
// Animation3DState Tests (pure logic, no GPU)
// ──────────────────────────────────────────────

let stateTests =
  let clips = Animation3DClips.fromModelAnimations(makeTestClips())

  // We need a Model struct for the state, but can't load one without GPU.
  // Use a default Model — applyToModel tests are skipped.
  let dummyModel = Unchecked.defaultof<Model>

  testList "Animation3DState" [
    testList "create" [
      test "starts on specified clip" {
        let state = Animation3DState.create dummyModel clips "walk" 60.0f
        Expect.equal state.CurrentClipIndex 1 "Should be on walk (index 1)"
      }

      test "defaults to index 0 for unknown name" {
        let state = Animation3DState.create dummyModel clips "nonexistent" 60.0f
        Expect.equal state.CurrentClipIndex 0 "Should default to 0"
      }

      test "starts at frame 0" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f

        Expect.floatClose
          Accuracy.medium
          (float state.CurrentFrame)
          0.0
          "Should start at frame 0"
      }

      test "speed is fps/60" {
        let state = Animation3DState.create dummyModel clips "idle" 30.0f

        Expect.floatClose
          Accuracy.medium
          (float state.Speed)
          0.5
          "30fps / 60 = 0.5"
      }

      test "not blending initially" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f

        Expect.isFalse
          (Animation3DState.isBlending state)
          "Should not be blending"
      }
    ]

    testList "createByIndex" [
      test "starts on specified index" {
        let state = Animation3DState.createByIndex dummyModel clips 2 60.0f
        Expect.equal state.CurrentClipIndex 2 "Should be on index 2"
      }

      test "out of range defaults to 0" {
        let state = Animation3DState.createByIndex dummyModel clips 99 60.0f
        Expect.equal state.CurrentClipIndex 0 "Should default to 0"
      }
    ]

    testList "play" [
      test "switches to named clip" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let walked = Animation3DState.play "walk" state
        Expect.equal walked.CurrentClipIndex 1 "Should be on walk"
      }

      test "resets frame to 0" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let updated = Animation3DState.update 0.5f state
        let walked = Animation3DState.play "walk" updated

        Expect.floatClose
          Accuracy.medium
          (float walked.CurrentFrame)
          0.0
          "Frame should reset to 0"
      }

      test "no-op when already playing same clip" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let same = Animation3DState.play "idle" state
        Expect.equal same.CurrentClipIndex state.CurrentClipIndex "Index same"
        Expect.equal same.CurrentFrame state.CurrentFrame "Frame same"
      }

      test "ignores unknown name" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let unchanged = Animation3DState.play "nonexistent" state

        Expect.equal
          unchanged.CurrentClipIndex
          state.CurrentClipIndex
          "Index same"
      }

      test "cancels active blend" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let blending = Animation3DState.blendTo "walk" 0.5f state

        Expect.isTrue
          (Animation3DState.isBlending blending)
          "Precondition: blending"

        let played = Animation3DState.play "jump" blending
        Expect.isFalse (Animation3DState.isBlending played) "Blend cancelled"
        Expect.equal played.CurrentClipIndex 2 "Should be on jump"
      }
    ]

    testList "playByIndex" [
      test "switches by index" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let walked = Animation3DState.playByIndex 1 state
        Expect.equal walked.CurrentClipIndex 1 "Should be on index 1"
      }

      test "out of range is no-op" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let unchanged = Animation3DState.playByIndex 99 state
        Expect.equal unchanged.CurrentClipIndex 0 "Should stay at 0"
      }

      test "negative is no-op" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let unchanged = Animation3DState.playByIndex -1 state
        Expect.equal unchanged.CurrentClipIndex 0 "Should stay at 0"
      }
    ]

    testList "playIfNot" [
      test "switches when different" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let walked = Animation3DState.playIfNot "walk" state
        Expect.equal walked.CurrentClipIndex 1 "Should switch to walk"
      }

      test "no-op when same" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let same = Animation3DState.playIfNot "idle" state
        Expect.equal same.CurrentClipIndex 0 "Should stay at idle"
      }
    ]

    testList "update" [
      test "advances frame" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let updated = Animation3DState.update (1.0f / 60.0f) state
        // speed = 60/60 = 1.0, framesToAdvance = (1/60) * 1.0 * 60 = 1.0
        Expect.floatClose
          Accuracy.medium
          (float updated.CurrentFrame)
          1.0
          "Frame ~1.0"
      }

      test "looping wraps around" {
        let state = Animation3DState.create dummyModel clips "jump" 60.0f // 6 keyframes
        // Advance past end: 6 frames / 60fps speed = 0.1s per full cycle
        let updated = Animation3DState.update 0.12f state // ~7.2 frames
        Expect.isFalse updated.Finished "Should not be finished (looping)"

        Expect.floatClose
          Accuracy.medium
          (float updated.CurrentFrame)
          1.2
          "Should wrap to ~1.2"
      }

      test "non-looping finishes" {
        // Create a non-looping state by using a clip and setting Loop = false
        let state = {
          Animation3DState.create dummyModel clips "jump" 60.0f with
              Loop = false
        }

        // jump has 6 keyframes, speed=1.0, advance past end
        let updated = Animation3DState.update 0.12f state
        Expect.isTrue updated.Finished "Should be finished"

        Expect.floatClose
          Accuracy.medium
          (float updated.CurrentFrame)
          5.0
          "Should be on last frame (5)"
      }

      test "finished non-looping does not advance" {
        let state = {
          Animation3DState.create dummyModel clips "jump" 60.0f with
              Loop = false
        }

        let finished = Animation3DState.update 0.12f state
        let doubleUpdate = Animation3DState.update 0.016f finished

        Expect.equal
          doubleUpdate.CurrentFrame
          finished.CurrentFrame
          "Frame same"

        Expect.isTrue doubleUpdate.Finished "Still finished"
      }

      test "zero dt does not advance" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let updated = Animation3DState.update 0.0f state

        Expect.floatClose
          Accuracy.medium
          (float updated.CurrentFrame)
          0.0
          "Frame stays 0"
      }
    ]

    testList "blendTo" [
      test "starts blend to target" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let blending = Animation3DState.blendTo "walk" 0.3f state

        Expect.isTrue
          (Animation3DState.isBlending blending)
          "Should be blending"

        Expect.equal blending.BlendTargetIndex 1 "Target should be walk"
      }

      test "no-op when target is current and not blending" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let same = Animation3DState.blendTo "idle" 0.3f state

        Expect.isFalse
          (Animation3DState.isBlending same)
          "Should not be blending"
      }

      test "does not restart blend to same target" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let b1 = Animation3DState.blendTo "walk" 0.3f state
        let b1updated = Animation3DState.update 0.05f b1
        let b2 = Animation3DState.blendTo "walk" 0.3f b1updated
        // Should NOT restart — BlendTargetIndex already = walk
        Expect.floatClose
          Accuracy.medium
          (float b2.BlendProgress)
          (float b1updated.BlendProgress)
          "Progress should not reset"
      }

      test "blend completes and switches clip" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let blending = Animation3DState.blendTo "walk" 0.1f state
        // 0.1s duration, advance by 0.12s → blend should complete
        let updated = Animation3DState.update 0.12f blending

        Expect.isFalse
          (Animation3DState.isBlending updated)
          "Blend should be done"

        Expect.equal updated.CurrentClipIndex 1 "Should be on walk"
      }

      test "switching target mid-blend works" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let b1 = Animation3DState.blendTo "walk" 0.5f state
        let b2 = Animation3DState.blendTo "jump" 0.3f b1
        Expect.equal b2.BlendTargetIndex 2 "Target should be jump"
      }
    ]

    testList "blendToByIndex" [
      test "starts blend by index" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let blending = Animation3DState.blendToByIndex 2 0.3f state

        Expect.isTrue
          (Animation3DState.isBlending blending)
          "Should be blending"

        Expect.equal blending.BlendTargetIndex 2 "Target should be jump"
      }

      test "out of range is no-op" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let unchanged = Animation3DState.blendToByIndex 99 0.3f state

        Expect.isFalse
          (Animation3DState.isBlending unchanged)
          "Should not blend"
      }
    ]

    testList "isFinished" [
      test "false for fresh state" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f

        Expect.isFalse
          (Animation3DState.isFinished state)
          "Should not be finished"
      }
    ]

    testList "isPlaying" [
      test "true for current clip" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f

        Expect.isTrue
          (Animation3DState.isPlaying "idle" state)
          "Should be playing idle"
      }

      test "false for different clip" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f

        Expect.isFalse
          (Animation3DState.isPlaying "walk" state)
          "Should not be walk"
      }
    ]

    testList "currentClipName" [
      test "returns correct name" {
        let state = Animation3DState.create dummyModel clips "walk" 60.0f

        Expect.equal
          (Animation3DState.currentClipName state)
          "walk"
          "Should be walk"
      }
    ]

    testList "restart" [
      test "resets frame and clears finished" {
        let state = {
          Animation3DState.create dummyModel clips "jump" 60.0f with
              Loop = false
        }

        let finished = Animation3DState.update 0.12f state

        Expect.isTrue
          (Animation3DState.isFinished finished)
          "Precondition: finished"

        let restarted = Animation3DState.restart finished

        Expect.floatClose
          Accuracy.medium
          (float restarted.CurrentFrame)
          0.0
          "Frame should be 0"

        Expect.isFalse
          (Animation3DState.isFinished restarted)
          "Should not be finished"
      }
    ]

    testList "withSpeed / withLoop" [
      test "withSpeed sets speed" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let slowed = Animation3DState.withSpeed 0.5f state

        Expect.floatClose
          Accuracy.medium
          (float slowed.Speed)
          0.5
          "Speed should be 0.5"
      }

      test "withLoop sets loop" {
        let state = Animation3DState.create dummyModel clips "idle" 60.0f
        let noLoop = Animation3DState.withLoop false state
        Expect.isFalse noLoop.Loop "Loop should be false"
      }
    ]
  ]

// ──────────────────────────────────────────────
// Main test list
// ──────────────────────────────────────────────

[<Tests>]
let tests = testList "Animation3D" [ clipsTests; stateTests ]
