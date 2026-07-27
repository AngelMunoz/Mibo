module Mibo.Raylib.Tests.ShadowPassTests

open System.Numerics
open Expecto
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

let private dir castsShadows =
  DirectionalLight3D.create Vector3.UnitY
  |> DirectionalLight3D.withCastsShadows castsShadows

let private castingPoint =
  PointLight3D.create(Vector3.Zero, 10.0f) |> PointLight3D.withCastsShadows true

[<Tests>]
let shadowPassTests =
  testList "collectShadowCasters (raylib)" [
    test "casters re-pack from slot 0 per registration" {
      let atlas =
        ShadowAtlas(ShadowAtlasConfig.defaults, ShadowBiasConfig.defaults)

      let lights = LightBuffers.create 3 8 4
      lights.DirLights.Add(dir true)
      lights.PointLights.Add castingPoint
      lights.PointLights.Add castingPoint

      let pointSlots = Array.create<int> lights.PointLights.Count -1
      let spotSlots = Array.create<int> lights.SpotLights.Count -1

      let hasCasters =
        collectShadowCasters(lights, atlas, pointSlots, spotSlots)

      Expect.isTrue hasCasters "dir + 2 point casters"
      Expect.sequenceEqual pointSlots [ 1; 2 ] "dir takes slot 0, points follow"

      // A later registration with fewer casters re-packs from slot 0 (per-block shadow
      // passes clear the atlas and re-register each block's light set from scratch).
      atlas.Clear()

      let later = LightBuffers.create 3 8 4
      later.PointLights.Add castingPoint

      let laterPointSlots = Array.create<int> later.PointLights.Count -1
      let laterSpotSlots = Array.create<int> later.SpotLights.Count -1

      let laterHasCasters =
        collectShadowCasters(later, atlas, laterPointSlots, laterSpotSlots)

      Expect.isTrue laterHasCasters "one point caster"
      Expect.sequenceEqual laterPointSlots [ 0 ] "re-packed from slot 0"
    }

    test "only the first directional light can cast" {
      let atlas =
        ShadowAtlas(ShadowAtlasConfig.defaults, ShadowBiasConfig.defaults)

      let nonCastingFirst = LightBuffers.create 3 8 4
      nonCastingFirst.DirLights.Add(dir false)
      nonCastingFirst.DirLights.Add(dir true)

      let hasCasters = collectShadowCasters(nonCastingFirst, atlas, [||], [||])

      Expect.isFalse
        hasCasters
        "a non-casting DirLights[0] means no directional caster"

      atlas.Clear()

      let castingFirst = LightBuffers.create 3 8 4
      castingFirst.DirLights.Add(dir true)
      castingFirst.DirLights.Add(dir false)

      let hasCasters2 = collectShadowCasters(castingFirst, atlas, [||], [||])

      Expect.isTrue hasCasters2 "DirLights[0] casts"
    }
  ]
