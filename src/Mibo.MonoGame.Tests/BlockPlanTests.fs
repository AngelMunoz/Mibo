module Mibo.MonoGame.Tests.BlockPlanTests

open Expecto
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

let private cam = Unchecked.defaultof<Camera3D>

let private planOf(cmds: Command3D list) =
  use buffer = new RenderBuffer3D()

  for cmd in cmds do
    buffer.Add cmd

  BlockPlan.build buffer

let private ambient(intensity: float32) =
  AmbientLight3D.create Mibo.Color.White
  |> AmbientLight3D.withIntensity intensity

let private dir castsShadows (direction: Vector3) =
  DirectionalLight3D.create direction
  |> DirectionalLight3D.withCastsShadows castsShadows

let private point(at: Vector3) = PointLight3D.create(at, 10.0f)

let private spot(at: Vector3) =
  SpotLight3D.create(at, Vector3.UnitY, 20.0f)

[<Tests>]
let blockPlanTests =
  testList "BlockPlan (MonoGame)" [
    test "empty buffer has no camera blocks" {
      let plan = planOf []
      Expect.equal plan.BlockCount 0 "BlockCount"
      Expect.isEmpty plan.Blocks "Blocks"
    }

    test "single camera block counts once" {
      let plan = planOf [ Command3D.BeginCamera cam; Command3D.EndCamera ]
      Expect.equal plan.BlockCount 1 "BlockCount"
      Expect.equal plan.Blocks.Length 1 "Blocks"
    }

    test "two camera blocks count twice" {
      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      Expect.equal plan.BlockCount 2 "BlockCount"
    }

    test "nested BeginCamera closes the previous block" {
      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      Expect.equal plan.BlockCount 2 "BlockCount"
    }

    test "a block with light commands resets to defaults plus its own" {
      let p1 = point Vector3.Zero
      let dB = dir false Vector3.UnitX

      let plan =
        planOf [
          Command3D.SetAmbientLight(ambient 0.5f)
          Command3D.BeginCamera cam
          Command3D.SetAmbientLight(ambient 0.1f)
          Command3D.AddPointLight p1
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.SetAmbientLight(ambient 0.2f)
          Command3D.AddDirectionalLight dB
          Command3D.EndCamera
        ]

      let block1 = plan.Blocks[0].Lights
      Expect.equal block1.Ambient (ValueSome(ambient 0.1f)) "block1 ambient"
      Expect.sequenceEqual block1.PointLights [ p1 ] "block1 points"
      Expect.isEmpty block1.DirLights "block1 dirs"

      let block2 = plan.Blocks[1].Lights
      Expect.equal block2.Ambient (ValueSome(ambient 0.2f)) "block2 ambient"
      Expect.sequenceEqual block2.DirLights [ dB ] "block2 dirs"

      Expect.isEmpty
        block2.PointLights
        "block2 does not inherit block1's own lights"
    }

    test
      "a block without light commands inherits the previous block's final set" {
      let p1 = point Vector3.Zero

      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.SetAmbientLight(ambient 0.3f)
          Command3D.AddPointLight p1
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      for i = 0 to 2 do
        let lights = plan.Blocks[i].Lights

        Expect.equal
          lights.Ambient
          (ValueSome(ambient 0.3f))
          $"block{i} ambient"

        Expect.sequenceEqual lights.PointLights [ p1 ] $"block{i} points"
    }

    test "lights before the first BeginCamera appear in every block" {
      let d0 = dir false Vector3.UnitY
      let p1 = point Vector3.One

      let plan =
        planOf [
          Command3D.SetAmbientLight(ambient 0.4f)
          Command3D.AddDirectionalLight d0
          Command3D.BeginCamera cam
          Command3D.AddPointLight p1
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      Expect.equal
        plan.FrameDefaults.Ambient
        (ValueSome(ambient 0.4f))
        "defaults ambient"

      Expect.sequenceEqual plan.FrameDefaults.DirLights [ d0 ] "defaults dirs"

      let block1 = plan.Blocks[0].Lights
      Expect.equal block1.Ambient (ValueSome(ambient 0.4f)) "block1 ambient"
      Expect.sequenceEqual block1.DirLights [ d0 ] "block1 dirs"
      Expect.sequenceEqual block1.PointLights [ p1 ] "block1 points"

      let block2 = plan.Blocks[1].Lights
      Expect.equal block2.Ambient (ValueSome(ambient 0.4f)) "block2 ambient"
      Expect.sequenceEqual block2.DirLights [ d0 ] "block2 dirs"
      Expect.sequenceEqual block2.PointLights [ p1 ] "block2 points"
    }

    test
      "between-block commands update defaults; after-last-block commands affect no block" {
      let dBetween = dir false Vector3.UnitX
      let p2 = point Vector3.One
      let sAfter = spot Vector3.Zero

      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.EndCamera
          Command3D.AddDirectionalLight dBetween
          Command3D.BeginCamera cam
          Command3D.AddPointLight p2
          Command3D.EndCamera
          Command3D.AddSpotLight sAfter
        ]

      Expect.isEmpty
        plan.Blocks[0].Lights.DirLights
        "block1 predates the between-block dir"

      let block2 = plan.Blocks[1].Lights

      Expect.sequenceEqual
        block2.DirLights
        [ dBetween ]
        "block2 sees the updated defaults"

      Expect.sequenceEqual block2.PointLights [ p2 ] "block2 points"
      Expect.isEmpty block2.SpotLights "after-last-block spot affects no block"

      Expect.sequenceEqual
        plan.FrameDefaults.SpotLights
        [ sAfter ]
        "after-last-block spot lands in the defaults"
    }

    test "an empty block inherits between-block commands via the running set" {
      let p1 = point Vector3.Zero
      let dBetween = dir false Vector3.UnitX

      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.AddPointLight p1
          Command3D.EndCamera
          Command3D.AddDirectionalLight dBetween
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      let block2 = plan.Blocks[1].Lights

      Expect.sequenceEqual
        block2.PointLights
        [ p1 ]
        "block2 keeps block1's lights"

      Expect.sequenceEqual
        block2.DirLights
        [ dBetween ]
        "block2 sees the between-block dir"
    }

    test
      "a resetting block after between-block commands sees them via the defaults" {
      let p1 = point Vector3.Zero
      let dBetween = dir false Vector3.UnitX
      let s2 = spot Vector3.One

      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.AddPointLight p1
          Command3D.EndCamera
          Command3D.AddDirectionalLight dBetween
          Command3D.BeginCamera cam
          Command3D.AddSpotLight s2
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      let block2 = plan.Blocks[1].Lights

      Expect.sequenceEqual
        block2.DirLights
        [ dBetween ]
        "block2 sees the between-block dir"

      Expect.sequenceEqual block2.SpotLights [ s2 ] "block2 spots"

      Expect.isEmpty
        block2.PointLights
        "a resetting block does not inherit block1's lights"

      let block3 = plan.Blocks[2].Lights

      Expect.sequenceEqual
        block3.DirLights
        [ dBetween ]
        "block3 inherits block2's final set"

      Expect.sequenceEqual block3.SpotLights [ s2 ] "block3 spots"
    }

    test "block slices cover the block's commands, half-open" {
      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.EnableShadows
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.DisableShadows
        ]

      Expect.equal plan.Blocks[0].StartIndex 1 "block1 start"
      Expect.equal plan.Blocks[0].EndIndex 2 "block1 end"
      Expect.equal plan.Blocks[1].StartIndex 4 "block2 start"

      Expect.equal
        plan.Blocks[1].EndIndex
        5
        "an unclosed trailing block ends at the buffer end"
    }

    test "each block sees exactly its own directional light" {
      let dNoCast = dir false Vector3.UnitX
      let dCast = dir true Vector3.UnitY

      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.SetAmbientLight(ambient 0.1f)
          Command3D.AddDirectionalLight dNoCast
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.SetAmbientLight(ambient 0.6f)
          Command3D.AddDirectionalLight dCast
          Command3D.EndCamera
        ]

      let dirs1 = plan.Blocks[0].Lights.DirLights
      Expect.equal dirs1.Length 1 "block1 dir count"
      Expect.isFalse dirs1[0].CastsShadows "block1 dir does not cast"

      let dirs2 = plan.Blocks[1].Lights.DirLights
      Expect.equal dirs2.Length 1 "block2 dir count"
      Expect.isTrue dirs2[0].CastsShadows "block2 dir casts"
    }

    test
      "DisableShadows before a block sets that block's initial cast state off" {
      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.EndCamera
          Command3D.DisableShadows
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      Expect.isTrue plan.Blocks[0].InitialCastEnabled "block1 initial"
      Expect.isFalse plan.Blocks[1].InitialCastEnabled "block2 initial"
    }

    test "EnableShadows mid-block carries into the next block's initial state" {
      let plan =
        planOf [
          Command3D.DisableShadows
          Command3D.BeginCamera cam
          Command3D.EnableShadows
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      Expect.isFalse plan.Blocks[0].InitialCastEnabled "block1 initial"
      Expect.isTrue plan.Blocks[1].InitialCastEnabled "block2 initial"
    }

    test "SetShadowOrigin does not leak across blocks" {
      let origin = Microsoft.Xna.Framework.Vector3(1.0f, 2.0f, 3.0f)

      let plan =
        planOf [
          Command3D.BeginCamera cam
          Command3D.SetShadowOrigin origin
          Command3D.EndCamera
          Command3D.BeginCamera cam
          Command3D.EndCamera
        ]

      Expect.equal
        plan.Blocks[0].ShadowOrigin
        (ValueSome origin)
        "block1 origin"

      Expect.equal plan.Blocks[1].ShadowOrigin ValueNone "block2 origin"
    }

    test "LightBuffers.copyInto copies contents without stale entries" {
      // LightBuffers.create — LightBuffers.defaults is a shared module-level instance.
      let source = LightBuffers.create 3 8 4
      source.Ambient <- ValueSome(ambient 0.7f)
      source.DirLights.Add(dir true Vector3.UnitY)
      source.PointLights.Add(point Vector3.Zero)

      let target = LightBuffers.create 3 8 4
      target.DirLights.Add(dir false Vector3.UnitX)
      target.DirLights.Add(dir false Vector3.One)
      target.SpotLights.Add(spot Vector3.One)

      LightBuffers.copyInto source target

      Expect.equal target.Ambient (ValueSome(ambient 0.7f)) "ambient"
      Expect.sequenceEqual target.DirLights (Seq.toList source.DirLights) "dirs"

      Expect.sequenceEqual
        target.PointLights
        (Seq.toList source.PointLights)
        "points"

      Expect.isEmpty target.SpotLights "stale spots are gone"
    }
  ]
