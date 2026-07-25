module Mibo.MonoGame.Tests.ShadowAtlas

open Expecto
open Microsoft.Xna.Framework
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

    test "freed slots are reused when the bump allocator is exhausted" {
      // Regression: the decrement-only allocator "freed" a middle slot by lowering the
      // high-water mark, so the next AddCaster re-allocated a slot a live caster still
      // occupied (collision) while leaving a real hole behind.
      let cfg = {
        ShadowAtlasConfig.defaults with
            MaxCasters = 4
      }

      let atlas = ShadowAtlas(cfg, ShadowBiasConfig.defaults)

      let add() =
        atlas.AddCaster(
          ShadowCasterType.Point,
          Vector3.Zero,
          Vector3.UnitY,
          Vector3.Zero,
          true,
          ValueNone
        )

      let ids = [| add(); add(); add(); add() |]

      // Remove the second caster (a middle slot), then re-add.
      match ids[1] with
      | ValueSome id -> atlas.RemoveCaster(id)
      | ValueNone -> Tests.failtest "Expected caster to be allocated"

      match add() with
      | ValueSome _ ->
        let regions =
          atlas.Casters
          |> Seq.map(fun c -> c.AtlasRegion)
          |> Seq.sort
          |> Seq.toList

        Expect.equal
          regions
          [ 0; 1; 2; 3 ]
          "Regions stay dense: no collisions, no holes"
      | ValueNone -> Tests.failtest "Freed slot should have been reusable"
    }
  ]
