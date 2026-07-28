module Mibo.Raylib.Tests.Animation3D

#nowarn "9"

open System
open System.Collections.Generic
open System.Numerics
open System.Runtime.InteropServices
open Expecto
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Animation
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D

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
// Helpers — synthetic AnimatedMesh / skinnable Model (no GPU)
// ──────────────────────────────────────────────

let private makeAnimatedMesh
  (names: string[])
  (invBind: Matrix4x4[])
  : AnimatedMesh =
  let lookup = Dictionary<string, int>(names.Length)
  names |> Array.iteri(fun i name -> lookup[name] <- i)

  let bindPose =
    Array.create
      names.Length
      (Transform(
        Translation = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Scale = Vector3.One
      ))

  {
    Mesh = Unchecked.defaultof<Mesh>
    BoneCount = names.Length
    InverseBindPose = invBind
    BindPose = bindPose
    BoneNames = names
    BoneParents = Array.create names.Length -1
    BoneLookup = lookup
  }

/// Clips with well-formed TRS (identity rotation, unit scale):
/// "slide" moves the single bone +1 on X per frame (3 keyframes);
/// "hover" holds it at Y = 10 (3 keyframes).
let private makePoseClips() =
  let slidePoses = [|
    for i in 0..2 ->
      [|
        Transform(
          Translation = Vector3(float32 i, 0.0f, 0.0f),
          Rotation = Quaternion.Identity,
          Scale = Vector3.One
        )
      |]
  |]

  let hoverPoses = [|
    for _ in 0..2 ->
      [|
        Transform(
          Translation = Vector3(0.0f, 10.0f, 0.0f),
          Rotation = Quaternion.Identity,
          Scale = Vector3.One
        )
      |]
  |]

  [|
    makeModelAnimation "slide" 1 3 slidePoses
    makeModelAnimation "hover" 1 3 hoverPoses
  |]

/// A native Model with <paramref name="meshCount"/> zeroed meshes and one zeroed
/// material (with a valid, zeroed map array so Material3D.fromRaylibMaterial can
/// read it). Skeleton data is absent — pair it with a record-literal AnimatedMesh.
let private makeSkinnableModel(meshCount: int) : Model =
  let zeroAlloc(size: int) =
    let ptr = Marshal.AllocHGlobal(nativeint size)

    for i = 0 to size - 1 do
      Marshal.WriteByte(ptr + nativeint i, 0uy)

    ptr

  let meshSize = Marshal.SizeOf<Mesh>()
  let meshesPtr = zeroAlloc(meshSize * meshCount)

  let mapCount = int MaterialMapIndex.Brdf + 1
  let mapsPtr = zeroAlloc(Marshal.SizeOf<MaterialMap>() * mapCount)

  let mutable material = Unchecked.defaultof<Material>
  material.Maps <- NativePtr.ofVoidPtr<MaterialMap>(mapsPtr.ToPointer())

  let materialsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Material>())
  Marshal.StructureToPtr(material, materialsPtr, false)

  let meshMaterialPtr = zeroAlloc(4 * meshCount)

  let mutable model = Unchecked.defaultof<Model>
  model.MeshCount <- meshCount
  model.MaterialCount <- 1
  model.Meshes <- NativePtr.ofVoidPtr<Mesh>(meshesPtr.ToPointer())
  model.Materials <- NativePtr.ofVoidPtr<Material>(materialsPtr.ToPointer())
  model.MeshMaterial <- NativePtr.ofVoidPtr<int>(meshMaterialPtr.ToPointer())
  model

// ──────────────────────────────────────────────
// Bone query tests (record-literal mesh — no native model needed)
// ──────────────────────────────────────────────

let boneQueryTests =
  let mesh =
    makeAnimatedMesh [| "root"; "hand" |] [|
      Matrix4x4.Identity
      Matrix4x4.Identity
    |]

  let pose = {
    WorldPoses = [|
      Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f)
      Raymath.MatrixTranslate(0.0f, 2.0f, 0.0f)
    |]
    Palette = [| Matrix4x4.Identity; Matrix4x4.Identity |]
  }

  testList "Bone queries" [
    testList "AnimatedMesh.tryFindBoneIndex" [
      test "finds an existing bone" {
        Expect.equal
          (AnimatedMesh.tryFindBoneIndex "hand" mesh)
          (ValueSome 1)
          "hand should be at index 1"
      }

      test "returns ValueNone for an unknown bone" {
        Expect.equal
          (AnimatedMesh.tryFindBoneIndex "nope" mesh)
          ValueNone
          "Unknown bone should return ValueNone"
      }
    ]

    testList "BonePose.worldAt" [
      test "returns the world pose for a valid index" {
        Expect.equal
          (BonePose.worldAt 1 pose)
          (ValueSome(Raymath.MatrixTranslate(0.0f, 2.0f, 0.0f)))
          "Should return the bone's world pose"
      }

      test "returns ValueNone for a negative index" {
        Expect.equal
          (BonePose.worldAt -1 pose)
          ValueNone
          "Negative index should return ValueNone"
      }

      test "returns ValueNone for an out-of-range index" {
        Expect.equal
          (BonePose.worldAt 2 pose)
          ValueNone
          "Out-of-range index should return ValueNone"
      }
    ]

    testList "BonePose.tryGetWorld" [
      test "returns the world pose for a known bone name" {
        Expect.equal
          (BonePose.tryGetWorld "root" mesh pose)
          (ValueSome(Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f)))
          "Should return the root bone's world pose"
      }

      test "returns ValueNone for an unknown bone name" {
        Expect.equal
          (BonePose.tryGetWorld "nope" mesh pose)
          ValueNone
          "Unknown bone should return ValueNone"
      }
    ]
  ]

// ──────────────────────────────────────────────
// Animation3DState.computePose tests
// ──────────────────────────────────────────────

let computePoseTests =
  let clips = Animation3DClips.fromModelAnimations(makePoseClips())
  let dummyModel = Unchecked.defaultof<Model>
  let identityMesh = makeAnimatedMesh [| "root" |] [| Matrix4x4.Identity |]

  testList "Animation3DState.computePose" [
    test "samples an exact keyframe" {
      let state = {
        Animation3DState.create dummyModel clips "slide" 60.0f with
            CurrentFrame = 1.0f
      }

      let pose = Animation3DState.computePose identityMesh state

      Expect.equal
        pose.WorldPoses[0]
        (Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f))
        "World pose should be the frame-1 keyframe pose"
    }

    test "lerps between keyframes at fractional frames" {
      let state = {
        Animation3DState.create dummyModel clips "slide" 60.0f with
            CurrentFrame = 0.5f
      }

      let pose = Animation3DState.computePose identityMesh state

      Expect.equal
        pose.WorldPoses[0]
        (Raymath.MatrixTranslate(0.5f, 0.0f, 0.0f))
        "World pose should be lerped halfway between frames 0 and 1"
    }

    test "palette matches raylib's native inverse-bind-times-pose" {
      // InverseBindPose is System.Numerics layout (see AnimatedMesh.fromModel).
      // A bone sitting at +10 in bind pose has inverse bind T(-10).
      let invBind = Matrix4x4.CreateTranslation(-10.0f, 0.0f, 0.0f)
      let mesh = makeAnimatedMesh [| "root" |] [| invBind |]

      let state = {
        Animation3DState.create dummyModel clips "slide" 60.0f with
            CurrentFrame = 1.0f
      }

      let pose = Animation3DState.computePose mesh state

      // The native-layout equivalent of the palette, computed the way
      // raylib's UpdateModelAnimation does: MatrixMultiply(MatrixInvert(bind), current).
      // Transpose(invBind) is that inverse bind pose in native layout.
      let expected =
        Raymath.MatrixMultiply(Matrix4x4.Transpose invBind, pose.WorldPoses[0])

      Expect.equal
        pose.Palette[0]
        expected
        "Palette[i] should equal raylib's native MatrixMultiply(MatrixInvert(bind), current)"

      // Concrete value: a vertex at the bone in bind (+10) is unbound to 0,
      // then re-posed to the current bone position (+1) → net T(-9).
      Expect.equal
        pose.Palette[0]
        (Raymath.MatrixTranslate(-9.0f, 0.0f, 0.0f))
        "Palette should carry the -9 net skinning translation"
    }

    test "blends current and target clips by BlendProgress" {
      let state = {
        Animation3DState.create dummyModel clips "slide" 60.0f with
            CurrentFrame = 0.0f
            BlendTargetIndex = 1
            BlendTargetFrame = 0.0f
            BlendProgress = 0.5f
      }

      let pose = Animation3DState.computePose identityMesh state

      Expect.equal
        pose.WorldPoses[0]
        (Raymath.MatrixTranslate(0.0f, 5.0f, 0.0f))
        "Should blend slide frame 0 and hover frame 0 halfway"
    }

    test "empty clip set yields zeroed poses" {
      let emptyClips = Animation3DClips.fromModelAnimations [||]
      let state = Animation3DState.create dummyModel emptyClips "x" 60.0f
      let pose = Animation3DState.computePose identityMesh state

      Expect.equal pose.WorldPoses.Length 1 "One world pose entry"
      Expect.equal pose.Palette.Length 1 "One palette entry"

      Expect.equal
        pose.WorldPoses[0]
        Unchecked.defaultof<Matrix4x4>
        "World pose should be zeroed"
    }
  ]

// ──────────────────────────────────────────────
// AnimatedModel tests
// ──────────────────────────────────────────────

let animatedModelTests =
  let clips = Animation3DClips.fromModelAnimations(makePoseClips())
  let dummyModel = Unchecked.defaultof<Model>
  let mesh = makeAnimatedMesh [| "root" |] [| Matrix4x4.Identity |]

  testList "AnimatedModel" [
    test "computePose matches Animation3DState.computePose" {
      let state = {
        Animation3DState.create dummyModel clips "slide" 60.0f with
            CurrentFrame = 1.0f
      }

      let am = AnimatedModel.create mesh state
      let pose = AnimatedModel.computePose am

      Expect.equal
        pose.WorldPoses[0]
        (Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f))
        "Should evaluate the model's current pose"
    }

    testList "tryGetBoneWorld" [
      test "finds a bone by name" {
        let state = {
          Animation3DState.create dummyModel clips "slide" 60.0f with
              CurrentFrame = 1.0f
        }

        let am = AnimatedModel.create mesh state

        Expect.equal
          (AnimatedModel.tryGetBoneWorld (BoneRef.ByName "root") am)
          (ValueSome(Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f)))
          "Should return the root bone's world pose"
      }

      test "finds a bone by index" {
        let state = Animation3DState.create dummyModel clips "slide" 60.0f
        let am = AnimatedModel.create mesh state

        Expect.equal
          (AnimatedModel.tryGetBoneWorld (BoneRef.ByIndex 0) am)
          (ValueSome Matrix4x4.Identity)
          "Frame 0 pose should be identity"
      }

      test "returns ValueNone for an unknown bone" {
        let state = Animation3DState.create dummyModel clips "slide" 60.0f
        let am = AnimatedModel.create mesh state

        Expect.equal
          (AnimatedModel.tryGetBoneWorld (BoneRef.ByName "nope") am)
          ValueNone
          "Unknown bone should return ValueNone"
      }
    ]
  ]

// ──────────────────────────────────────────────
// RenderBuffer3D witness tests (AnimatedModel GPU path)
// ──────────────────────────────────────────────

let animatedWitnessTests =
  let clips = Animation3DClips.fromModelAnimations(makePoseClips())
  let dummyModel = Unchecked.defaultof<Model>

  let makeAnimatedModel(meshCount: int) =
    let model = makeSkinnableModel meshCount
    let state = Animation3DState.create model clips "slide" 60.0f
    let mesh = makeAnimatedMesh [| "root" |] [| Matrix4x4.Identity |]
    AnimatedModel.create mesh state

  let sharedPose = {
    WorldPoses = [| Raymath.MatrixTranslate(0.0f, 5.0f, 0.0f) |]
    Palette = [| Raymath.MatrixTranslate(1.0f, 2.0f, 3.0f) |]
  }

  testList "RenderBuffer3D animated model witnesses" [
    test
      "AddAnimatedModel (AnimatedModel) emits one DrawSkinnedMesh per mesh with the shared palette" {
      let am = makeAnimatedModel 2
      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModel(am, Matrix4x4.Identity, ValueSome sharedPose)

      Expect.equal buffer.Count 2 "Should emit one command per mesh"

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.DrawSkinnedMesh(_, transform, _, bones) ->
          Expect.equal transform Matrix4x4.Identity "Transform should match"

          Expect.isTrue
            (Object.ReferenceEquals(bones, sharedPose.Palette))
            "Palette should be forwarded untouched"
        | _ -> Tests.failtest "Expected DrawSkinnedMesh"
    }

    test "AddAnimatedModel (AnimatedModel) computes the pose when omitted" {
      let am = makeAnimatedModel 1
      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModel(am, Matrix4x4.Identity, ValueNone)

      Expect.equal buffer.Count 1 "Should emit one command"

      match buffer[0] with
      | Command3D.DrawSkinnedMesh(_, _, _, bones) ->
        Expect.equal bones.Length 1 "One bone in the palette"

        Expect.equal
          bones[0]
          Matrix4x4.Identity
          "Frame 0 with identity bind pose should be identity"
      | _ -> Tests.failtest "Expected DrawSkinnedMesh"
    }

    test
      "AddAnimatedModelWith (AnimatedModel) applies the material to every mesh" {
      let am = makeAnimatedModel 2
      let material = Material3D.colored Color.Red
      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModelWith(
        am,
        Matrix4x4.Identity,
        material,
        ValueSome sharedPose
      )

      Expect.equal buffer.Count 2 "Should emit one command per mesh"

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.DrawSkinnedMesh(_, _, mat, bones) ->
          Expect.equal mat.AlbedoColor Color.Red "Material should match"

          Expect.isTrue
            (Object.ReferenceEquals(bones, sharedPose.Palette))
            "Palette should be forwarded untouched"
        | _ -> Tests.failtest "Expected DrawSkinnedMesh"
    }

    test "AddAttachedMesh draws with localTransform * boneWorld * transform" {
      let am = makeAnimatedModel 1
      let local = Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f)
      let world = Raymath.MatrixTranslate(10.0f, 0.0f, 0.0f)
      use buffer = new RenderBuffer3D()

      buffer.AddAttachedMesh(
        am,
        BoneRef.ByName "root",
        local,
        Unchecked.defaultof<Mesh>,
        Material3D.defaults,
        world,
        ValueSome sharedPose
      )

      Expect.equal buffer.Count 1 "Should emit one command"

      match buffer[0] with
      | Command3D.DrawMesh(_, transform, _) ->
        // All inputs are raylib-native layout, applied left to right:
        // translate (1,0,0), then the bone pose (0,5,0), then the world
        // transform (10,0,0) — the attachment lands at (11,5,0).
        Expect.equal
          transform
          (Raymath.MatrixTranslate(11.0f, 5.0f, 0.0f))
          "Attachment world should be local * boneWorld * transform"
      | _ -> Tests.failtest "Expected DrawMesh"
    }

    test "AddAttachedMesh with an unknown bone emits nothing" {
      let am = makeAnimatedModel 1
      use buffer = new RenderBuffer3D()

      buffer.AddAttachedMesh(
        am,
        BoneRef.ByName "nope",
        Matrix4x4.Identity,
        Unchecked.defaultof<Mesh>,
        Material3D.defaults,
        Matrix4x4.Identity,
        ValueSome sharedPose
      )

      buffer.AddAttachedMesh(
        am,
        BoneRef.ByIndex 7,
        Matrix4x4.Identity,
        Unchecked.defaultof<Mesh>,
        Material3D.defaults,
        Matrix4x4.Identity,
        ValueSome sharedPose
      )

      Expect.equal buffer.Count 0 "Unknown bones should be a no-op"
    }

    test "DSL animatedModel resolves the AnimatedModel overload (GPU path)" {
      let am = makeAnimatedModel 2
      use buffer = new RenderBuffer3D()

      buffer.animatedModel(am, Matrix4x4.Identity, pose = sharedPose) |> ignore

      Expect.equal buffer.Count 2 "Should emit one DrawSkinnedMesh per mesh"

      match buffer[0] with
      | Command3D.DrawSkinnedMesh(_, _, _, bones) ->
        Expect.isTrue
          (Object.ReferenceEquals(bones, sharedPose.Palette))
          "Palette should be forwarded untouched"
      | _ -> Tests.failtest "Expected DrawSkinnedMesh"
    }

    test "DSL animatedModel computes the pose when omitted" {
      let am = makeAnimatedModel 1
      use buffer = new RenderBuffer3D()

      buffer.animatedModel(am, Matrix4x4.Identity) |> ignore

      Expect.equal buffer.Count 1 "Should emit one command"

      match buffer[0] with
      | Command3D.DrawSkinnedMesh _ -> ()
      | _ -> Tests.failtest "Expected DrawSkinnedMesh"
    }

    test "DSL animatedModel still resolves the legacy Animation3DState overload" {
      // Empty clip set: applyToModel is a no-op, so no native model is touched.
      let emptyClips = Animation3DClips.fromModelAnimations [||]
      let state = Animation3DState.create dummyModel emptyClips "x" 60.0f
      use buffer = new RenderBuffer3D()

      buffer.animatedModel(state, Matrix4x4.Identity) |> ignore

      Expect.equal buffer.Count 1 "Should emit one command"

      match buffer[0] with
      | Command3D.DrawModel _ -> ()
      | _ -> Tests.failtest "Expected DrawModel"
    }

    test "DSL attachedMesh emits DrawMesh with the composed transform" {
      let am = makeAnimatedModel 1
      let local = Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f)
      let world = Raymath.MatrixTranslate(10.0f, 0.0f, 0.0f)
      use buffer = new RenderBuffer3D()

      buffer.attachedMesh(
        am,
        BoneRef.ByIndex 0,
        local,
        Unchecked.defaultof<Mesh>,
        Material3D.defaults,
        world,
        pose = sharedPose
      )
      |> ignore

      Expect.equal buffer.Count 1 "Should emit one command"

      match buffer[0] with
      | Command3D.DrawMesh(_, transform, _) ->
        Expect.equal
          transform
          (Raymath.MatrixTranslate(11.0f, 5.0f, 0.0f))
          "Attachment world should be local * boneWorld * transform"
      | _ -> Tests.failtest "Expected DrawMesh"
    }
  ]

// ──────────────────────────────────────────────
// RenderBuffer3D instanced witness tests (AnimatedModel GPU path)
// ──────────────────────────────────────────────

let animatedInstancedWitnessTests =
  let clips = Animation3DClips.fromModelAnimations(makePoseClips())

  let makeAnimatedModel(meshCount: int) =
    let model = makeSkinnableModel meshCount
    let state = Animation3DState.create model clips "slide" 60.0f
    let mesh = makeAnimatedMesh [| "root" |] [| Matrix4x4.Identity |]
    AnimatedModel.create mesh state

  let poseA = {
    WorldPoses = [||]
    Palette = [| Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f) |]
  }

  let poseB = {
    WorldPoses = [||]
    Palette = [| Raymath.MatrixTranslate(2.0f, 0.0f, 0.0f) |]
  }

  let transforms = [|
    Matrix4x4.Identity
    Raymath.MatrixTranslate(5.0f, 0.0f, 0.0f)
  |]

  testList "RenderBuffer3D animated model instanced witness" [
    test "emits one DrawSkinnedMeshInstanced per mesh with a flattened palette" {
      let am = makeAnimatedModel 2
      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModelInstanced(
        am,
        transforms,
        [| poseA; poseB |],
        ValueNone,
        ValueNone
      )

      Expect.equal buffer.Count 2 "Should emit one command per mesh"

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.DrawSkinnedMeshInstanced(_, t, palettes, _, count) ->
          Expect.equal count 2 "Two instances"

          Expect.isTrue
            (Object.ReferenceEquals(t, transforms))
            "Transforms array should be forwarded untouched"

          Expect.equal
            palettes.Length
            2
            "One palette matrix per instance (1 bone)"

          Expect.equal palettes[0] poseA.Palette[0] "Instance 0 palette first"
          Expect.equal palettes[1] poseB.Palette[0] "Instance 1 palette second"
        | _ -> Tests.failtest "Expected DrawSkinnedMeshInstanced"
    }

    test "instance count clamps to the shorter array" {
      let am = makeAnimatedModel 1
      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModelInstanced(
        am,
        transforms,
        [| poseA |],
        ValueNone,
        ValueNone
      )

      match buffer[0] with
      | Command3D.DrawSkinnedMeshInstanced(_, _, palettes, _, count) ->
        Expect.equal count 1 "Clamped to the single pose"
        Expect.equal palettes.Length 1 "One palette"
      | _ -> Tests.failtest "Expected DrawSkinnedMeshInstanced"
    }

    test "zero instances emits nothing" {
      let am = makeAnimatedModel 1
      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModelInstanced(am, [||], [||], ValueNone, ValueNone)

      Expect.equal buffer.Count 0 "No commands expected"
    }

    test "MaterialOverride.All applies the material to every mesh" {
      let am = makeAnimatedModel 2
      let material = Material3D.colored Color.Red
      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModelInstanced(
        am,
        transforms,
        [| poseA; poseB |],
        ValueSome(MaterialOverride.All material),
        ValueNone
      )

      Expect.equal buffer.Count 2 "Should emit one command per mesh"

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.DrawSkinnedMeshInstanced(_, _, _, mat, _) ->
          Expect.equal mat.AlbedoColor Color.Red "Material should match"
        | _ -> Tests.failtest "Expected DrawSkinnedMeshInstanced"
    }

    test "MaterialOverride.PerMesh resolves the material by mesh index" {
      let am = makeAnimatedModel 2

      let materials = [|
        Material3D.colored Color.Red
        Material3D.colored Color.Blue
      |]

      use buffer = new RenderBuffer3D()

      buffer.AddAnimatedModelInstanced(
        am,
        transforms,
        [| poseA; poseB |],
        ValueSome(MaterialOverride.PerMesh(fun i -> materials[i])),
        ValueNone
      )

      Expect.equal buffer.Count 2 "Should emit one command per mesh"

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        | Command3D.DrawSkinnedMeshInstanced(_, _, _, mat, _) ->
          Expect.equal
            mat.AlbedoColor
            materials[i].AlbedoColor
            $"Mesh {i} should get its resolver material"
        | _ -> Tests.failtest "Expected DrawSkinnedMeshInstanced"
    }

    test "colors raise NotSupportedException" {
      let am = makeAnimatedModel 1
      use buffer = new RenderBuffer3D()

      Expect.throwsT<System.NotSupportedException>
        (fun () ->
          buffer.AddAnimatedModelInstanced(
            am,
            transforms,
            [| poseA |],
            ValueNone,
            ValueSome [| Color.Red |]
          ))
        "Per-instance colors are MonoGame-only"
    }

    test "DSL animatedModelInstanced emits the instanced command" {
      let am = makeAnimatedModel 1
      use buffer = new RenderBuffer3D()

      buffer.animatedModelInstanced(am, transforms, [| poseA; poseB |])
      |> ignore

      Expect.equal buffer.Count 1 "Should emit one command"

      match buffer[0] with
      | Command3D.DrawSkinnedMeshInstanced(_, _, _, _, count) ->
        Expect.equal count 2 "Two instances"
      | _ -> Tests.failtest "Expected DrawSkinnedMeshInstanced"
    }
  ]

// ──────────────────────────────────────────────
// Bone remap (cross-file clip merge) tests
// ──────────────────────────────────────────────

let remapTests =
  let targetOrder = [| "root"; "hips"; "hand.l"; "hand.r" |]

  testList "Bone remap" [
    testList "Animation3DClips.buildBoneRemap" [
      test "identical orders need no remap" {
        Expect.equal
          (Animation3DClips.buildBoneRemap targetOrder targetOrder)
          ValueNone
          "Same order should be ValueNone"
      }

      test "swapped sides produce the inverse index map" {
        // KayKit case: the source skeleton orders the right side first,
        // the target orders the left side first.
        let sourceOrder = [| "root"; "hips"; "hand.r"; "hand.l" |]

        Expect.equal
          (Animation3DClips.buildBoneRemap sourceOrder targetOrder)
          (ValueSome [| 0; 1; 3; 2 |])
          "Target hand.l should sample source index 3 and vice versa"
      }

      test "bones missing from the source map to -1" {
        let sourceOrder = [| "root"; "hips" |]

        Expect.equal
          (Animation3DClips.buildBoneRemap sourceOrder targetOrder)
          (ValueSome [| 0; 1; -1; -1 |])
          "Unmapped bones should map to -1 (they hold their bind pose)"
      }
    ]

    testList "Animation3DClips.merge" [
      test "concatenates clips and remaps only the mismatched source" {
        let movement = makePoseClips() // 2 clips
        let general = makeTestClips() // 3 clips
        let targetNames = [| "root" |]

        let clips =
          Animation3DClips.merge targetNames [|
            targetNames, movement
            [| "other" |], general
          |]

        Expect.equal clips.Clips.Length 5 "All clips from both sources"
        Expect.equal clips.BoneRemaps.Length 5 "One remap slot per clip"

        Expect.equal
          clips.BoneRemaps[0]
          ValueNone
          "Matching source needs no remap"

        Expect.equal
          clips.BoneRemaps[2]
          (ValueSome [| -1 |])
          "Mismatched source is remapped"

        Expect.equal
          (Animation3DClips.tryGetClipIndex "hover" clips)
          (ValueSome 1)
          "Clip names resolve across the merge"
      }
    ]

    testList "computePose with remap" [
      test "samples each bone from the remapped source index" {
        // Target skeleton: left side first. Clip authored right side first.
        let sourcePoses = [|
          [|
            Transform(
              Translation = Vector3(1.0f, 0.0f, 0.0f),
              Rotation = Quaternion.Identity,
              Scale = Vector3.One
            ) // right
            Transform(
              Translation = Vector3(0.0f, 2.0f, 0.0f),
              Rotation = Quaternion.Identity,
              Scale = Vector3.One
            ) // left
          |]
        |]

        let clips =
          Animation3DClips.merge [| "left"; "right" |] [|
            [| "right"; "left" |],
            [| makeModelAnimation "swap" 2 1 sourcePoses |]
          |]

        let mesh =
          makeAnimatedMesh [| "left"; "right" |] [|
            Matrix4x4.Identity
            Matrix4x4.Identity
          |]

        let state =
          Animation3DState.create
            (Unchecked.defaultof<Model>)
            clips
            "swap"
            60.0f

        let pose = Animation3DState.computePose mesh state

        Expect.equal
          pose.WorldPoses[0]
          (Raymath.MatrixTranslate(0.0f, 2.0f, 0.0f))
          "left should sample the clip's second (left) pose"

        Expect.equal
          pose.WorldPoses[1]
          (Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f))
          "right should sample the clip's first (right) pose"
      }

      test "bones unmapped by the remap hold their bind pose" {
        // The clip animates only "root"; "extra" must hold its rest position
        // instead of collapsing to the skeleton origin.
        let sourcePoses = [|
          [|
            Transform(
              Translation = Vector3(1.0f, 0.0f, 0.0f),
              Rotation = Quaternion.Identity,
              Scale = Vector3.One
            )
          |]
        |]

        let clips =
          Animation3DClips.merge [| "root"; "extra" |] [|
            [| "root" |], [| makeModelAnimation "partial" 1 1 sourcePoses |]
          |]

        let bindExtra =
          Transform(
            Translation = Vector3(0.0f, 7.0f, 0.0f),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
          )

        let mesh = {
          makeAnimatedMesh [| "root"; "extra" |] [|
            Matrix4x4.Identity
            Matrix4x4.Identity
          |] with
              BindPose = [|
                Transform(
                  Translation = Vector3.Zero,
                  Rotation = Quaternion.Identity,
                  Scale = Vector3.One
                )
                bindExtra
              |]
        }

        let state =
          Animation3DState.create
            (Unchecked.defaultof<Model>)
            clips
            "partial"
            60.0f

        let pose = Animation3DState.computePose mesh state

        Expect.equal
          pose.WorldPoses[0]
          (Raymath.MatrixTranslate(1.0f, 0.0f, 0.0f))
          "root should sample the clip pose"

        Expect.equal
          pose.WorldPoses[1]
          (Raymath.MatrixTranslate(0.0f, 7.0f, 0.0f))
          "extra should hold its bind pose"
      }
    ]
  ]

// ──────────────────────────────────────────────
// Main test list
// ──────────────────────────────────────────────

[<Tests>]
let tests =
  testList "Animation3D" [
    clipsTests
    stateTests
    boneQueryTests
    computePoseTests
    animatedModelTests
    animatedWitnessTests
    animatedInstancedWitnessTests
    remapTests
  ]
