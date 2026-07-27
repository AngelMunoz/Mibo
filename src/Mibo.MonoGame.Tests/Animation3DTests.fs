module Mibo.MonoGame.Tests.Animation3DTests

open System.Collections.Generic
open Expecto
open Microsoft.Xna.Framework
open Mibo.Animation
open Mibo.Elmish.Graphics3D

// Synthetic 3-bone rig (root -> child -> grandchild) authored directly in
// MonoGame's row-vector convention — no fake "Assimp" column-vector inputs to
// transpose. The child bone is channelless, so its pose comes from the
// BindLocalPoses fallback. Translations are exact integers so float32
// composition is lossless and matrices compare exactly.

let private matrixApproxEqual (a: Matrix) (b: Matrix) =
  let eps = 0.0001f

  abs(a.M11 - b.M11) < eps
  && abs(a.M12 - b.M12) < eps
  && abs(a.M13 - b.M13) < eps
  && abs(a.M14 - b.M14) < eps
  && abs(a.M21 - b.M21) < eps
  && abs(a.M22 - b.M22) < eps
  && abs(a.M23 - b.M23) < eps
  && abs(a.M24 - b.M24) < eps
  && abs(a.M31 - b.M31) < eps
  && abs(a.M32 - b.M32) < eps
  && abs(a.M33 - b.M33) < eps
  && abs(a.M34 - b.M34) < eps
  && abs(a.M41 - b.M41) < eps
  && abs(a.M42 - b.M42) < eps
  && abs(a.M43 - b.M43) < eps
  && abs(a.M44 - b.M44) < eps

let private boneNames = [| "root"; "child"; "grandchild" |]
let private boneParents = [| -1; 0; 1 |]

let private bindLocalPoses = [|
  Matrix.CreateTranslation(1.0f, 0.0f, 0.0f)
  Matrix.CreateTranslation(0.0f, 2.0f, 0.0f)
  Matrix.CreateTranslation(0.0f, 0.0f, 3.0f)
|]

// Inverse-bind of the composed bind world poses (row-vector):
// bindWorld[0] = T(1,0,0); bindWorld[1] = T(1,2,0); bindWorld[2] = T(1,2,3).
let private inverseBindPose = [|
  Matrix.CreateTranslation(-1.0f, 0.0f, 0.0f)
  Matrix.CreateTranslation(-1.0f, -2.0f, 0.0f)
  Matrix.CreateTranslation(-1.0f, -2.0f, -3.0f)
|]

let private testMesh: AnimatedMesh = {
  BoneCount = 3
  BoneNames = boneNames
  BoneParents = boneParents
  InverseBindPose = inverseBindPose
  BindLocalPoses = bindLocalPoses
  BoneOrder = [| 0; 1; 2 |]
  BoneLookup =
    Dictionary<string, int>(dict [ "root", 0; "child", 1; "grandchild", 2 ])
}

// One clip, KeyframeCount 1: "root" animates to T(10,0,0), "grandchild" to
// T(0,0,-1); "child" has no channel and holds its bind local pose T(0,2,0).
let private testClips: Animation3DClips =
  let channel name x y z : Animation3DChannel = {
    BoneName = name
    Keyframes = [|
      {
        TimeTicks = 0.0f
        Transform = Matrix.CreateTranslation(x, y, z)
      }
    |]
  }

  let channels =
    Dictionary<string, Animation3DChannel>(
      dict [
        "root", channel "root" 10.0f 0.0f 0.0f
        "grandchild", channel "grandchild" 0.0f 0.0f -1.0f
      ]
    )

  {
    Clips = [|
      {
        Name = "wave"
        DurationSeconds = 1.0f
        Channels = channels
        KeyframeCount = 1
      }
    |]
    ClipNames = Dictionary<string, int>(dict [ "wave", 0 ])
    ClipsInfo = Animation3DClipsInfo.create [| "wave", 1 |]
  }

let private testState = Animation3DState.create testClips "wave" 30.0f

// Expected world poses: local * worldParent, row-vector, local on the left.
// world[0] = T(10,0,0); world[1] = T(0,2,0) * T(10,0,0) = T(10,2,0);
// world[2] = T(0,0,-1) * T(10,2,0) = T(10,2,-1).
let private expectedWorldPoses = [|
  Matrix.CreateTranslation(10.0f, 0.0f, 0.0f)
  Matrix.CreateTranslation(10.0f, 2.0f, 0.0f)
  Matrix.CreateTranslation(10.0f, 2.0f, -1.0f)
|]

let private dummyPrimitive: PrimitiveMesh = {
  Vertices = null
  Indices = null
  PrimitiveCount = 0
  Bounds = BoundingSphere()
}

let private testModel: AnimatedModel = {
  Model = null
  Mesh = ValueSome testMesh
  State = testState
}

[<Tests>]
let tests =
  testList "Animation3D bone poses & attachments (MonoGame)" [
    testList "Animation3DState.computePose" [
      test "composes parent translations into world poses" {
        let pose = Animation3DState.computePose testMesh testState

        for i = 0 to 2 do
          Expect.isTrue
            (matrixApproxEqual pose.WorldPoses[i] expectedWorldPoses[i])
            $"WorldPoses[{i}] translation mismatch: %A{pose.WorldPoses[i].Translation}"
      }

      test "channelless bone holds its bind local pose" {
        let pose = Animation3DState.computePose testMesh testState

        // child = bindLocal T(0,2,0) composed under animated root T(10,0,0).
        Expect.isTrue
          (matrixApproxEqual
            pose.WorldPoses[1]
            (Matrix.CreateTranslation(10.0f, 2.0f, 0.0f)))
          "channelless child did not follow the BindLocalPoses fallback"
      }

      test "palette is InverseBindPose * WorldPoses per bone" {
        let pose = Animation3DState.computePose testMesh testState

        for i = 0 to 2 do
          let expected = inverseBindPose[i] * pose.WorldPoses[i]

          Expect.isTrue
            (matrixApproxEqual pose.Palette[i] expected)
            $"Palette[{i}] != InverseBindPose[{i}] * WorldPoses[{i}]"
      }

      test "computeBonePalette returns the computePose palette" {
        let palette = Animation3DState.computeBonePalette testMesh testState

        let pose = Animation3DState.computePose testMesh testState

        for i = 0 to 2 do
          Expect.isTrue
            (matrixApproxEqual palette[i] pose.Palette[i])
            $"computeBonePalette[{i}] diverged from computePose"
      }
    ]

    testList "AnimatedMesh.tryFindBoneIndex" [
      test "hit returns the bone index" {
        Expect.equal
          (AnimatedMesh.tryFindBoneIndex "grandchild" testMesh)
          (ValueSome 2)
          "grandchild should resolve to index 2"
      }

      test "miss returns ValueNone" {
        Expect.equal
          (AnimatedMesh.tryFindBoneIndex "nope" testMesh)
          ValueNone
          "unknown bone name should be ValueNone"
      }
    ]

    testList "BonePose.worldAt" [
      test "in-bounds index returns the world pose" {
        let pose = Animation3DState.computePose testMesh testState

        Expect.equal
          (BonePose.worldAt 2 pose)
          (ValueSome expectedWorldPoses[2])
          "index 2 should return the grandchild world pose"
      }

      test "out-of-bounds indices return ValueNone" {
        let pose = Animation3DState.computePose testMesh testState

        Expect.equal (BonePose.worldAt 3 pose) ValueNone "index 3"
        Expect.equal (BonePose.worldAt -1 pose) ValueNone "index -1"
      }
    ]

    testList "AddAttachedMesh witness" [
      test "records one DrawPrimitive with local * boneWorld * transform" {
        use buffer = new RenderBuffer3D()
        let local = Matrix.CreateTranslation(0.0f, 1.0f, 0.0f)
        let transform = Matrix.CreateTranslation(100.0f, 0.0f, 0.0f)

        buffer.AddAttachedMesh(
          testModel,
          BoneRef.ByName "grandchild",
          local,
          dummyPrimitive,
          Material3D.defaults,
          transform,
          ValueNone
        )

        Expect.equal buffer.Count 1 "expected exactly one command"

        match buffer[0] with
        | Command3D.DrawPrimitive(_, actual, _) ->
          let expected = local * expectedWorldPoses[2] * transform

          Expect.isTrue
            (matrixApproxEqual actual expected)
            $"attachment world mismatch: %A{actual.Translation}"
        | other -> failtest $"expected DrawPrimitive, got %A{other}"
      }

      test "BoneRef.ByIndex resolves through the same pose" {
        use buffer = new RenderBuffer3D()

        buffer.AddAttachedMesh(
          testModel,
          BoneRef.ByIndex 1,
          Matrix.Identity,
          dummyPrimitive,
          Material3D.defaults,
          Matrix.Identity,
          ValueNone
        )

        Expect.equal buffer.Count 1 "expected exactly one command"

        match buffer[0] with
        | Command3D.DrawPrimitive(_, actual, _) ->
          Expect.isTrue
            (matrixApproxEqual actual expectedWorldPoses[1])
            "ByIndex attachment should land on the child bone world pose"
        | other -> failtest $"expected DrawPrimitive, got %A{other}"
      }

      test "unknown bone name records nothing" {
        use buffer = new RenderBuffer3D()

        buffer.AddAttachedMesh(
          testModel,
          BoneRef.ByName "nope",
          Matrix.Identity,
          dummyPrimitive,
          Material3D.defaults,
          Matrix.Identity,
          ValueNone
        )

        Expect.equal buffer.Count 0 "unknown bone must be a no-op"
      }

      test "out-of-range bone index records nothing" {
        use buffer = new RenderBuffer3D()

        buffer.AddAttachedMesh(
          testModel,
          BoneRef.ByIndex 99,
          Matrix.Identity,
          dummyPrimitive,
          Material3D.defaults,
          Matrix.Identity,
          ValueNone
        )

        Expect.equal buffer.Count 0 "out-of-range index must be a no-op"
      }

      test "a shared pose value is honored instead of recomputing" {
        use buffer = new RenderBuffer3D()

        // A hand-built pose whose world transforms differ from what
        // computePose would produce for testState — if the witness honors
        // the passed pose, the recorded transform reflects T(50,60,70).
        let sharedPose: BonePose = {
          WorldPoses = [|
            Matrix.Identity
            Matrix.Identity
            Matrix.CreateTranslation(50.0f, 60.0f, 70.0f)
          |]
          Palette = [||]
        }

        buffer.AddAttachedMesh(
          testModel,
          BoneRef.ByName "grandchild",
          Matrix.Identity,
          dummyPrimitive,
          Material3D.defaults,
          Matrix.Identity,
          ValueSome sharedPose
        )

        Expect.equal buffer.Count 1 "expected exactly one command"

        match buffer[0] with
        | Command3D.DrawPrimitive(_, actual, _) ->
          Expect.isTrue
            (matrixApproxEqual
              actual
              (Matrix.CreateTranslation(50.0f, 60.0f, 70.0f)))
            "the witness must use the passed pose, not a recomputed one"
        | other -> failtest $"expected DrawPrimitive, got %A{other}"
      }
    ]

    testList "AnimatedModel pose helpers" [
      test "computePose returns ValueNone for a boneless model" {
        let boneless: AnimatedModel = {
          Model = null
          Mesh = ValueNone
          State = testState
        }

        Expect.equal
          (AnimatedModel.computePose boneless)
          ValueNone
          "boneless model should yield ValueNone"
      }

      test "tryGetBoneWorld resolves by name and index" {
        Expect.equal
          (AnimatedModel.tryGetBoneWorld (BoneRef.ByName "grandchild") testModel)
          (ValueSome expectedWorldPoses[2])
          "by name"

        Expect.equal
          (AnimatedModel.tryGetBoneWorld (BoneRef.ByIndex 0) testModel)
          (ValueSome expectedWorldPoses[0])
          "by index"

        Expect.equal
          (AnimatedModel.tryGetBoneWorld (BoneRef.ByName "nope") testModel)
          ValueNone
          "missing bone"
      }
    ]
  ]
