module Mibo.MonoGame.Tests.ShadowPass

open System.Numerics
open Expecto
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

let private cam = Unchecked.defaultof<Camera3D>

let private resources() =
  ShadowResources(ShadowAtlasConfig.defaults, ShadowBiasConfig.defaults)

let private dir castsShadows =
  DirectionalLight3D.create Vector3.UnitY
  |> DirectionalLight3D.withCastsShadows castsShadows

let private castingPoint =
  PointLight3D.create(Vector3.Zero, 10.0f) |> PointLight3D.withCastsShadows true

[<Tests>]
let shadowPassTests =
  testList "ShadowPass.registerCasters (MonoGame)" [
    test "casters re-pack from slot 0 per registration" {
      let res = resources()
      let lights = LightBuffers.create 3 8 4
      lights.DirLights.Add(dir true)
      lights.PointLights.Add castingPoint
      lights.PointLights.Add castingPoint
      res.PointShadowSlots <- Array.create 2 -1

      let count =
        ShadowPass.registerCasters ShadowAtlasConfig.defaults res lights cam

      Expect.equal count 3 "dir + 2 point casters"

      Expect.sequenceEqual
        res.PointShadowSlots
        [ 1; 2 ]
        "dir takes slot 0, points follow"

      // A later registration with fewer casters re-packs from slot 0 (per-block shadow
      // passes re-register each block's light set from scratch).
      let later = LightBuffers.create 3 8 4
      later.PointLights.Add castingPoint
      res.PointShadowSlots <- Array.create 1 -1

      let laterCount =
        ShadowPass.registerCasters ShadowAtlasConfig.defaults res later cam

      Expect.equal laterCount 1 "one point caster"
      Expect.sequenceEqual res.PointShadowSlots [ 0 ] "re-packed from slot 0"
    }

    test "only the first directional light can cast" {
      let res = resources()

      let nonCastingFirst = LightBuffers.create 3 8 4
      nonCastingFirst.DirLights.Add(dir false)
      nonCastingFirst.DirLights.Add(dir true)

      let count =
        ShadowPass.registerCasters
          ShadowAtlasConfig.defaults
          res
          nonCastingFirst
          cam

      Expect.equal
        count
        0
        "a non-casting DirLights[0] means no directional caster"

      let castingFirst = LightBuffers.create 3 8 4
      castingFirst.DirLights.Add(dir true)
      castingFirst.DirLights.Add(dir false)

      let count2 =
        ShadowPass.registerCasters
          ShadowAtlasConfig.defaults
          res
          castingFirst
          cam

      Expect.equal count2 1 "DirLights[0] casts"
    }
  ]
