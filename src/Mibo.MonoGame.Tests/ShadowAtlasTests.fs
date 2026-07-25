module Mibo.MonoGame.Tests.ShadowAtlas

open Expecto
open Mibo.Elmish.Graphics3D.Pipelines

[<Tests>]
let shadowAtlasTests =
  testList "ShadowAtlas (MonoGame)" [
    test "ctor rejects out-of-range DirectionalAtlasRatio" {
      let tooBig = {
        ShadowAtlasConfig.defaults with
            DirectionalAtlasRatio = 1.5f
      }

      Expect.throws
        (fun () -> ShadowAtlas(tooBig, ShadowBiasConfig.defaults) |> ignore)
        "Ratio above 1.0 should throw"

      let negative = {
        ShadowAtlasConfig.defaults with
            DirectionalAtlasRatio = -0.5f
      }

      Expect.throws
        (fun () -> ShadowAtlas(negative, ShadowBiasConfig.defaults) |> ignore)
        "Negative ratio should throw"
    }
  ]
