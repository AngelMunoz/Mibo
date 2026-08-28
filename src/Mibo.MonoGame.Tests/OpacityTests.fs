module Mibo.MonoGame.Tests.Opacity

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Expecto
open Mibo.Elmish.Graphics3D
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
          (Opacity.anyTransparentInstanceColor(ValueNone, 4))
          "ValueNone colors"
      }

      test "null colors is opaque" {
        let cs: Color[] = null

        Expect.isFalse
          (Opacity.anyTransparentInstanceColor(ValueSome cs, 4))
          "null colors array"
      }

      test "all-opaque colors stay opaque" {
        let cs = [| Color.White; Color(10, 20, 30, 255) |]

        Expect.isFalse
          (Opacity.anyTransparentInstanceColor(ValueSome cs, 2))
          "every alpha is 255"
      }

      test "one alpha below 255 marks the batch transparent" {
        let cs = [| Color.White; Color(255, 255, 255, 128) |]

        Expect.isTrue
          (Opacity.anyTransparentInstanceColor(ValueSome cs, 2))
          "second instance alpha 128"
      }

      test "a transparent row past the count stays opaque" {
        let cs = [| Color.White; Color(255, 255, 255, 128) |]

        Expect.isFalse
          (Opacity.anyTransparentInstanceColor(ValueSome cs, 1))
          "the transparent instance is past the clamped count"
      }
    ]

    testList "instanceSubsetStats" [
      test "no colors means nothing to split" {
        let transforms = [| at(1.0f, 0.0f, 0.0f) |]

        let struct (count, _) =
          Opacity.instanceSubsetStats(Vector3.Zero, transforms, ValueNone, 1)

        Expect.equal count 0 "uncolored batches never split"
      }

      test "counts only the transparent instances within the count" {
        let transforms = [|
          at(0.0f, 0.0f, 0.0f)
          at(0.0f, 0.0f, 6.0f)
          at(0.0f, 0.0f, 100.0f)
        |]

        let colors = [|
          Color.White
          Color(255, 255, 255, 100)
          Color(255, 255, 255, 50)
        |]

        // The third instance is transparent but past the clamped count.
        let struct (count, _) =
          Opacity.instanceSubsetStats(
            Vector3.Zero,
            transforms,
            ValueSome colors,
            2
          )

        Expect.equal count 1 "only the second instance is transparent"
      }

      test "distance is to the transparent instances' centroid" {
        let transforms = [|
          at(0.0f, 0.0f, 0.0f)
          at(0.0f, 0.0f, 4.0f)
          at(0.0f, 0.0f, 8.0f)
        |]

        let colors = [|
          Color(255, 255, 255, 128)
          Color.White
          Color(255, 255, 255, 128)
        |]

        let struct (count, dist) =
          Opacity.instanceSubsetStats(
            Vector3.Zero,
            transforms,
            ValueSome colors,
            3
          )

        Expect.equal count 2 "two transparent instances"

        // Centroid of instances 0 and 2: z = 4 → distance squared 16.
        Expect.isTrue
          (abs(dist - 16.0f) < 0.0001f)
          "sort key follows the transparent half, not the whole batch"
      }

      test "instances past the colors array clamp to opaque" {
        let transforms = [| at(0.0f, 0.0f, 0.0f); at(0.0f, 0.0f, 6.0f) |]

        let colors = [| Color(255, 255, 255, 100) |]

        let struct (count, _) =
          Opacity.instanceSubsetStats(
            Vector3.Zero,
            transforms,
            ValueSome colors,
            2
          )

        Expect.equal count 1 "the second instance has no color row, so opaque"
      }

      test "all-transparent instances report the full count" {
        let transforms = [| at(0.0f, 0.0f, 0.0f); at(0.0f, 0.0f, 2.0f) |]

        let colors = [| Color(255, 255, 255, 200); Color(255, 255, 255, 1) |]

        let struct (count, _) =
          Opacity.instanceSubsetStats(
            Vector3.Zero,
            transforms,
            ValueSome colors,
            2
          )

        Expect.equal count 2 "every instance defers"
      }
    ]

    testList "animatedModelPartOpacityMix (whole-model override)" [
      // The whole-model All override short-circuits without touching the
      // model, so a null model is safe here. The per-part resolutions
      // (PerMesh / authored materials) walk Model.Meshes and need a GPU
      // fixture — they mirror drawAnimatedModelInstanced's own resolution.
      let model: Model = null

      test "an opaque All override is all-opaque" {
        let o =
          Opacity.animatedModelPartOpacityMix(
            model,
            ValueSome(
              MaterialOverride.All {
                Material3D.defaults with
                    Opacity = 1.0f
              }
            )
          )

        Expect.equal o (struct (false, true, false)) "no transparent part"
      }

      test "a semi-transparent All override is all-transparent" {
        let o =
          Opacity.animatedModelPartOpacityMix(
            model,
            ValueSome(
              MaterialOverride.All {
                Material3D.defaults with
                    Opacity = 0.5f
              }
            )
          )

        Expect.equal o (struct (true, false, false)) "every part defers"
      }

      test "an invisible All override casts and draws nothing" {
        let o =
          Opacity.animatedModelPartOpacityMix(
            model,
            ValueSome(
              MaterialOverride.All {
                Material3D.defaults with
                    Opacity = 0.0f
              }
            )
          )

        // The invisible flag is what keeps the shadow collector's merged fast
        // path from collecting a fully invisible command as casters.
        Expect.equal
          o
          (struct (false, false, true))
          "invisible parts draw nothing and cast nothing"
      }
    ]
  ]
