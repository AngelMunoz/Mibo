module Mibo.MonoGame.Tests.Opacity

open Microsoft.Xna.Framework
open Expecto
open Mibo.Elmish.Graphics3D.Pipelines

let private at(x: float32, y: float32, z: float32) =
  Matrix.CreateTranslation(x, y, z)

[<Tests>]
let opacityTests =
  testList "Opacity classification helpers" [
    testList "instanceCentroidDistanceSq" [
      test "is the distance to the average instance translation" {
        let transforms = [| at(0.0f, 0.0f, 0.0f); at(0.0f, 0.0f, 10.0f) |]

        let d = Opacity.instanceCentroidDistanceSq(Vector3.Zero, transforms, 2)

        Expect.isTrue
          (abs(d - 25.0f) < 0.0001f)
          "centroid at z=5, distance squared 25"
      }

      test "ignores rows past the count" {
        let transforms = [|
          at(0.0f, 0.0f, 4.0f)
          at(0.0f, 0.0f, 4.0f)
          at(100.0f, 0.0f, 100.0f)
        |]

        let d = Opacity.instanceCentroidDistanceSq(Vector3.Zero, transforms, 2)

        Expect.isTrue
          (abs(d - 16.0f) < 0.0001f)
          "only the first two instances count"
      }
    ]

    testList "anyTransparentInstanceColor" [
      test "no colors is opaque" {
        Expect.isFalse
          (Opacity.anyTransparentInstanceColor ValueNone)
          "ValueNone colors"
      }

      test "null colors is opaque" {
        let cs: Color[] = null

        Expect.isFalse
          (Opacity.anyTransparentInstanceColor(ValueSome cs))
          "null colors array"
      }

      test "all-opaque colors stay opaque" {
        let cs = [| Color.White; Color(10, 20, 30, 255) |]

        Expect.isFalse
          (Opacity.anyTransparentInstanceColor(ValueSome cs))
          "every alpha is 255"
      }

      test "one alpha below 255 marks the batch transparent" {
        let cs = [| Color.White; Color(255, 255, 255, 128) |]

        Expect.isTrue
          (Opacity.anyTransparentInstanceColor(ValueSome cs))
          "second instance alpha 128"
      }
    ]
  ]
