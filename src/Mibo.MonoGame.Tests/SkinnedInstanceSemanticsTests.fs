module Mibo.MonoGame.Tests.SkinnedInstanceSemantics

open Expecto
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D.Pipelines

let private el (usage: VertexElementUsage) (usageIndex: int) =
  VertexElement(0, VertexElementFormat.Vector3, usage, usageIndex)

let private texCoord usageIndex =
  el VertexElementUsage.TextureCoordinate usageIndex

[<Tests>]
let skinnedInstanceSemanticsTests =
  testList "SkinnedInstanceSemantics.elementsCollide (MonoGame)" [
    test "an empty declaration does not collide" {
      Expect.isFalse
        (SkinnedInstanceSemantics.elementsCollide [||])
        "no elements, no collision"
    }

    test "TEXCOORD0 never collides" {
      let elements = [| texCoord 0 |]

      Expect.isFalse
        (SkinnedInstanceSemantics.elementsCollide elements)
        "usage index 0 is the mesh's own UV"
    }

    test "TEXCOORD1 through TEXCOORD6 collide" {
      for i = 1 to 6 do
        Expect.isTrue
          (SkinnedInstanceSemantics.elementsCollide [| texCoord i |])
          $"usage index {i} carries instance data"
    }

    test "TEXCOORD7 and beyond do not collide" {
      let elements = [| texCoord 7; texCoord 8 |]

      Expect.isFalse
        (SkinnedInstanceSemantics.elementsCollide elements)
        "usage indices past 6 are outside the instance stream's range"
    }

    test "a non-TextureCoordinate usage on index 1 does not collide" {
      let elements = [| el VertexElementUsage.Normal 1 |]

      Expect.isFalse
        (SkinnedInstanceSemantics.elementsCollide elements)
        "only TextureCoordinate usage can collide"
    }

    test "a colliding channel is found anywhere in the declaration" {
      let elements = [|
        el VertexElementUsage.Position 0
        el VertexElementUsage.Normal 0
        texCoord 0
        el VertexElementUsage.BlendIndices 0
        texCoord 2
      |]

      Expect.isTrue
        (SkinnedInstanceSemantics.elementsCollide elements)
        "TEXCOORD2 rides the instance stream"
    }

    test "a skinned layout without extra UV channels does not collide" {
      let elements = [|
        el VertexElementUsage.Position 0
        el VertexElementUsage.Normal 0
        texCoord 0
        el VertexElementUsage.BlendIndices 0
        el VertexElementUsage.BlendWeight 0
      |]

      Expect.isFalse
        (SkinnedInstanceSemantics.elementsCollide elements)
        "the standard skinned layout is clean"
    }
  ]
