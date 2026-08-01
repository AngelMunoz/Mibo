module Mibo.MonoGame.Tests.MergedModelParts

open Expecto
open Mibo.Elmish.Graphics3D.Pipelines

let private planMerge = MergedModelParts.planMerge

let private desc
  (transform, declaration, texture, isSkinned, vertexCount, indexCount, needs32)
  : MergePartDesc =
  {
    TransformId = transform
    DeclarationId = declaration
    TextureId = texture
    IsSkinned = isSkinned
    VertexCount = vertexCount
    IndexCount = indexCount
    SourceNeeds32Bit = needs32
  }

/// Six parts like the sample mannequin: one parent-bone world, one declaration,
/// one texture.
let private mannequin() =
  Array.init 6 (fun _ -> desc(0, 0, 1, true, 1100, 4800, false))

[<Tests>]
let mergedModelPartsTests =
  testList "MergedModelParts.planMerge (MonoGame)" [
    test "parts sharing transform, declaration, texture, and skinned flag merge" {
      let groups = planMerge(mannequin())

      Expect.equal groups.Length 1 "one merged group"

      Expect.sequenceEqual
        groups[0].PartIndices
        [ 0; 1; 2; 3; 4; 5 ]
        "all six parts, pipeline order"

      Expect.isFalse groups[0].Needs32Bit "6600 verts fit in 16 bits"
    }

    test "different parent-bone world does not merge" {
      let parts = mannequin()

      for i = 3 to 5 do
        parts[i] <- desc(1, 0, 1, true, 1100, 4800, false)

      let groups = planMerge parts

      Expect.sequenceEqual
        (groups |> Array.map(fun g -> g.PartIndices))
        [ [| 0; 1; 2 |]; [| 3; 4; 5 |] ]
        "two groups, split by parent-bone world"
    }

    test "different declaration does not merge" {
      let parts = mannequin()
      parts[1] <- desc(0, 1, 1, true, 1100, 4800, false)

      let groups = planMerge parts

      Expect.sequenceEqual
        (groups |> Array.map(fun g -> g.PartIndices))
        [ [| 0; 2; 3; 4; 5 |] ]
        "part 1 left out (singletons are not returned)"
    }

    test "different texture does not merge" {
      let parts = mannequin()
      parts[2] <- desc(0, 0, 2, true, 1100, 4800, false)

      let groups = planMerge parts

      Expect.sequenceEqual
        (groups |> Array.map(fun g -> g.PartIndices))
        [ [| 0; 1; 3; 4; 5 |] ]
        "part 2 left out"
    }

    test "non-skinned parts do not merge with skinned ones" {
      let parts = mannequin()
      parts[0] <- desc(0, 0, 1, false, 1100, 4800, false)

      let groups = planMerge parts

      Expect.sequenceEqual
        (groups |> Array.map(fun g -> g.PartIndices))
        [ [| 1; 2; 3; 4; 5 |] ]
        "part 0 left out"
    }

    test "all-distinct parts return no groups" {
      let parts =
        Array.init 4 (fun i -> desc(i, i, i + 1, true, 100, 300, false))

      Expect.isEmpty (planMerge parts) "nothing to merge"
    }

    test "groups keep stable first-appearance order" {
      let parts = [|
        desc(0, 0, 1, true, 100, 300, false) // group B (second appearance)
        desc(1, 0, 1, true, 100, 300, false) // group A (first appearance)
        desc(0, 0, 1, true, 100, 300, false)
        desc(1, 0, 1, true, 100, 300, false)
      |]

      let groups = planMerge parts

      Expect.sequenceEqual
        (groups |> Array.map(fun g -> g.PartIndices))
        [ [| 0; 2 |]; [| 1; 3 |] ]
        "group order follows first appearance"
    }

    test "combined vertex count past 65535 promotes to 32-bit" {
      let parts =
        Array.init 3 (fun _ -> desc(0, 0, 1, true, 30000, 60000, false))

      let groups = planMerge parts

      Expect.equal groups.Length 1 "one group"
      Expect.isTrue groups[0].Needs32Bit "90000 verts need 32-bit indices"
    }

    test "a 32-bit source promotes the whole group" {
      let parts = [|
        desc(0, 0, 1, true, 100, 300, true)
        desc(0, 0, 1, true, 100, 300, false)
      |]

      let groups = planMerge parts

      Expect.isTrue groups[0].Needs32Bit "one 32-bit source is enough"
    }

    test "16-bit sources under the vertex cap stay 16-bit" {
      let parts = Array.init 2 (fun _ -> desc(0, 0, 1, true, 1000, 3000, false))

      let groups = planMerge parts

      Expect.isFalse groups[0].Needs32Bit "no promotion needed"
    }
  ]
