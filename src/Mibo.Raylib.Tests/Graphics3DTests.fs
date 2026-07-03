module Mibo.Raylib.Tests.Graphics3D

open System
open System.Numerics
open Expecto
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

// ──────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────

let private v3a = Vector3(1.0f, 2.0f, 3.0f)
let private v3b = Vector3(4.0f, 5.0f, 6.0f)
let private v3c = Vector3(7.0f, 8.0f, 9.0f)
let private v2a = Vector2(10.0f, 20.0f)
let private v2b = Vector2(30.0f, 40.0f)
let private identity = Matrix4x4.Identity
let private mesh = Unchecked.defaultof<Mesh>
let private model = Unchecked.defaultof<Model>

let private tex =
  Texture2D(
    Id = 1u,
    Width = 64,
    Height = 64,
    Mipmaps = 1,
    Format = PixelFormat.UncompressedR8G8B8A8
  )

// ──────────────────────────────────────────────
// Command3D Factory Tests
// ──────────────────────────────────────────────

let command3DFactoryTests =
  testList "Command3D factories" [
    test "drawMesh creates DrawMesh with correct fields" {
      let mat = Material3D.colored Color.Red
      let cmd = Command3D.drawMesh mesh identity mat

      match cmd with
      | Command3D.DrawMesh(m, t, mat2) ->
        Expect.equal m mesh "Mesh should match"
        Expect.equal t identity "Transform should match"
        Expect.equal mat2.AlbedoColor Color.Red "Material albedo should match"
      | _ -> Tests.failtest "Expected DrawMesh"
    }

    test "drawModel creates DrawModel with correct fields" {
      let cmd = Command3D.drawModel model identity

      match cmd with
      | Command3D.DrawModel(m, t) ->
        Expect.equal m model "Model should match"
        Expect.equal t identity "Transform should match"
      | _ -> Tests.failtest "Expected DrawModel"
    }

    test "drawModelWith creates DrawModelWith with All override" {
      let mat = Material3D.colored Color.Red

      let cmd =
        Command3D.drawModelWith model identity (MaterialOverride.All mat)

      match cmd with
      | Command3D.DrawModelWith(m, t, MaterialOverride.All o) ->
        Expect.equal m model "Model should match"
        Expect.equal t identity "Transform should match"

        Expect.equal
          o.AlbedoColor
          Color.Red
          "Override material albedo should match"
      | _ -> Tests.failtest "Expected DrawModelWith (All)"
    }

    test "drawModelWith creates DrawModelWith with PerMesh override" {
      let resolver(_mi: int) = Material3D.colored Color.Blue

      let cmd =
        Command3D.drawModelWith
          model
          identity
          (MaterialOverride.PerMesh resolver)

      match cmd with
      | Command3D.DrawModelWith(m, t, MaterialOverride.PerMesh f) ->
        Expect.equal m model "Model should match"
        Expect.equal t identity "Transform should match"

        Expect.equal
          (f 0).AlbedoColor
          Color.Blue
          "Resolver material albedo should match"
      | _ -> Tests.failtest "Expected DrawModelWith (PerMesh)"
    }

    test "drawBillboard creates DrawBillboard with correct fields" {
      let cmd = Command3D.drawBillboard tex v3a v2a Color.Blue

      match cmd with
      | Command3D.DrawBillboard(t, pos, size, c) ->
        Expect.equal t tex "Texture should match"
        Expect.equal pos v3a "Position should match"
        Expect.equal size v2a "Size should match"
        Expect.equal c Color.Blue "Color should match"
      | _ -> Tests.failtest "Expected DrawBillboard"
    }

    test "drawLine3D creates DrawLine3D with correct fields" {
      let cmd = Command3D.drawLine3D v3a v3b Color.Green

      match cmd with
      | Command3D.DrawLine3D(s, f, c) ->
        Expect.equal s v3a "Start should match"
        Expect.equal f v3b "Finish should match"
        Expect.equal c Color.Green "Color should match"
      | _ -> Tests.failtest "Expected DrawLine3D"
    }

    test "drawSkinnedMesh creates DrawSkinnedMesh with correct fields" {
      let mat = Material3D.defaults
      let bones = [| Matrix4x4.Identity; Matrix4x4.CreateTranslation(v3a) |]
      let cmd = Command3D.drawSkinnedMesh mesh identity mat bones

      match cmd with
      | Command3D.DrawSkinnedMesh(m, t, mat2, b) ->
        Expect.equal m mesh "Mesh should match"
        Expect.equal t identity "Transform should match"
        Expect.equal mat2 mat "Material should match"
        Expect.equal b bones "Bones should match"
      | _ -> Tests.failtest "Expected DrawSkinnedMesh"
    }

    test "drawMeshInstanced creates DrawMeshInstanced with correct fields" {
      let mat = Material3D.colored Color.White
      let transforms = [| identity; Matrix4x4.CreateTranslation(v3a) |]
      let cmd = Command3D.drawMeshInstanced mesh transforms mat 2

      match cmd with
      | Command3D.DrawMeshInstanced(m, ts, mat2, count) ->
        Expect.equal m mesh "Mesh should match"
        Expect.equal ts transforms "Transforms should match"
        Expect.equal mat2.AlbedoColor Color.White "Material should match"
        Expect.equal count 2 "Instance count should match"
      | _ -> Tests.failtest "Expected DrawMeshInstanced"
    }

    test "drawBillboardBatch creates DrawBillboardBatch with correct fields" {
      let textures = [| tex; tex |]
      let positions = [| v3a; v3b |]
      let sizes = [| v2a; v2b |]
      let colors = [| Color.Red; Color.Blue |]
      let cmd = Command3D.drawBillboardBatch textures positions sizes colors 2

      match cmd with
      | Command3D.DrawBillboardBatch(ts, ps, ss, cs, count) ->
        Expect.equal ts textures "Textures should match"
        Expect.equal ps positions "Positions should match"
        Expect.equal ss sizes "Sizes should match"
        Expect.equal cs colors "Colors should match"
        Expect.equal count 2 "Count should match"
      | _ -> Tests.failtest "Expected DrawBillboardBatch"
    }

    test "beginCamera creates BeginCamera" {
      let cam = Unchecked.defaultof<Camera3D>
      let cmd = Command3D.beginCamera cam

      match cmd with
      | Command3D.BeginCamera c -> Expect.equal c cam "Camera should match"
      | _ -> Tests.failtest "Expected BeginCamera"
    }

    test "beginCameraConfig creates BeginCameraConfig" {
      let cam = Unchecked.defaultof<Camera3D>

      let cfg: Camera3DConfig = {
        Camera = cam
        Viewport = ValueNone
        ClearColor = ValueNone
        PostProcessPasses = ValueNone
      }

      let cmd = Command3D.beginCameraConfig cfg

      match cmd with
      | Command3D.BeginCameraConfig c ->
        Expect.equal c.Camera cam "Camera in config should match"
      | _ -> Tests.failtest "Expected BeginCameraConfig"
    }

    test "endCamera creates EndCamera" {
      let cmd = Command3D.endCamera()

      match cmd with
      | Command3D.EndCamera -> ()
      | _ -> Tests.failtest "Expected EndCamera"
    }

    test "setShadowOrigin creates SetShadowOrigin" {
      let cmd = Command3D.setShadowOrigin v3a

      match cmd with
      | Command3D.SetShadowOrigin o -> Expect.equal o v3a "Origin should match"
      | _ -> Tests.failtest "Expected SetShadowOrigin"
    }

    test "setAmbientLight creates SetAmbientLight" {
      let light = AmbientLight3D.create Mibo.Color.White
      let cmd = Command3D.setAmbientLight light

      match cmd with
      | Command3D.SetAmbientLight l ->
        Expect.equal l.Color Mibo.Color.White "Color should match"

        Expect.floatClose
          Accuracy.medium
          (float l.Intensity)
          1.0
          "Intensity should match"
      | _ -> Tests.failtest "Expected SetAmbientLight"
    }

    test "addDirectionalLight creates AddDirectionalLight" {
      let light = DirectionalLight3D.create v3a
      let cmd = Command3D.addDirectionalLight light

      match cmd with
      | Command3D.AddDirectionalLight l ->
        Expect.equal l.Direction v3a "Direction should match"
      | _ -> Tests.failtest "Expected AddDirectionalLight"
    }

    test "addPointLight creates AddPointLight" {
      let light = PointLight3D.create(v3a, 50.0f)
      let cmd = Command3D.addPointLight light

      match cmd with
      | Command3D.AddPointLight l ->
        Expect.equal l.Position v3a "Position should match"

        Expect.floatClose
          Accuracy.medium
          (float l.Radius)
          50.0
          "Radius should match"
      | _ -> Tests.failtest "Expected AddPointLight"
    }

    test "addSpotLight creates AddSpotLight" {
      let light = SpotLight3D.create(v3a, v3b, 100.0f)
      let cmd = Command3D.addSpotLight light

      match cmd with
      | Command3D.AddSpotLight l ->
        Expect.equal l.Position v3a "Position should match"
        Expect.equal l.Direction v3b "Direction should match"

        Expect.floatClose
          Accuracy.medium
          (float l.Radius)
          100.0
          "Radius should match"
      | _ -> Tests.failtest "Expected AddSpotLight"
    }

    test "enableShadows creates EnableShadows" {
      match Command3D.enableShadows() with
      | Command3D.EnableShadows -> ()
      | _ -> Tests.failtest "Expected EnableShadows"
    }

    test "disableShadows creates DisableShadows" {
      match Command3D.disableShadows() with
      | Command3D.DisableShadows -> ()
      | _ -> Tests.failtest "Expected DisableShadows"
    }

    test "drawImmediate creates DrawImmediate" {
      let mutable called = false

      let action(_ctx: Pipelines.SceneContext) = called <- true
      let cmd = Command3D.drawImmediate action

      match cmd with
      | Command3D.DrawImmediate a ->
        // The callback requires a SceneContext; pass an uninitialized one to test invocability.
        let ctx = Unchecked.defaultof<Pipelines.SceneContext>
        a ctx
        Expect.isTrue called "Action should be invocable"
      | _ -> Tests.failtest "Expected DrawImmediate"
    }

    test "beginEffect creates BeginEffect" {
      let shader = Unchecked.defaultof<Shader>
      let cmd = Command3D.beginEffect shader

      match cmd with
      | Command3D.BeginEffect _ -> ()
      | _ -> Tests.failtest "Expected BeginEffect"
    }

    test "endEffect creates EndEffect" {
      let cmd = Command3D.endEffect()

      match cmd with
      | Command3D.EndEffect -> ()
      | _ -> Tests.failtest "Expected EndEffect"
    }
  ]

// ──────────────────────────────────────────────
// Material3D Tests
// ──────────────────────────────────────────────

let material3DTests =
  testList "Material3D" [
    test "defaults has expected PBR values" {
      let m = Material3D.defaults
      Expect.equal m.AlbedoColor Color.White "Default albedo should be White"
      Expect.isFalse m.AlbedoMap.IsSome "Default should have no albedo map"

      Expect.floatClose
        Accuracy.medium
        (float m.Roughness)
        0.5
        "Default roughness should be 0.5"

      Expect.isFalse
        m.RoughnessMap.IsSome
        "Default should have no roughness map"

      Expect.floatClose
        Accuracy.medium
        (float m.Metallic)
        0.0
        "Default metallic should be 0"

      Expect.isFalse m.MetallicMap.IsSome "Default should have no metallic map"
      Expect.isFalse m.NormalMap.IsSome "Default should have no normal map"

      Expect.equal
        m.EmissionColor
        Color.Black
        "Default emission should be Black"

      Expect.isFalse m.EmissionMap.IsSome "Default should have no emission map"

      Expect.floatClose
        Accuracy.medium
        (float m.Opacity)
        1.0
        "Default opacity should be 1"

      Expect.equal m.Tiling Vector2.One "Default tiling should be (1,1)"
    }

    test "unlit creates emissive material with given color" {
      let m = Material3D.unlit Color.Red
      Expect.equal m.AlbedoColor Color.Red "Albedo should be Red"

      Expect.equal
        m.EmissionColor
        Color.Red
        "Emission should also be Red for unlit"
    }

    test "colored creates opaque material with given color" {
      let m = Material3D.colored Color.Blue
      Expect.equal m.AlbedoColor Color.Blue "Albedo should be Blue"
      Expect.equal m.EmissionColor Color.Black "Emission should remain Black"

      Expect.floatClose
        Accuracy.medium
        (float m.Opacity)
        1.0
        "Opacity should be 1"
    }

    test "withAlbedoMap sets the albedo texture" {
      let m = Material3D.defaults |> Material3D.withAlbedoMap tex

      match m.AlbedoMap with
      | ValueSome t -> Expect.equal t tex "Albedo map should match"
      | ValueNone -> Tests.failtest "Expected albedo map"
    }

    test "withNormalMap sets the normal texture" {
      let m = Material3D.defaults |> Material3D.withNormalMap tex

      match m.NormalMap with
      | ValueSome t -> Expect.equal t tex "Normal map should match"
      | ValueNone -> Tests.failtest "Expected normal map"
    }

    test "withRoughnessMap sets the roughness texture" {
      let m = Material3D.defaults |> Material3D.withRoughnessMap tex

      match m.RoughnessMap with
      | ValueSome t -> Expect.equal t tex "Roughness map should match"
      | ValueNone -> Tests.failtest "Expected roughness map"
    }

    test "withMetallicMap sets the metallic texture" {
      let m = Material3D.defaults |> Material3D.withMetallicMap tex

      match m.MetallicMap with
      | ValueSome t -> Expect.equal t tex "Metallic map should match"
      | ValueNone -> Tests.failtest "Expected metallic map"
    }

    test "chained material builders preserve previous values" {
      let m =
        Material3D.colored Color.Green
        |> Material3D.withAlbedoMap tex
        |> Material3D.withNormalMap tex

      Expect.equal m.AlbedoColor Color.Green "Albedo color should persist"
      Expect.isTrue m.AlbedoMap.IsSome "Albedo map should be set"
      Expect.isTrue m.NormalMap.IsSome "Normal map should be set"

      Expect.floatClose
        Accuracy.medium
        (float m.Metallic)
        0.0
        "Metallic should remain default"
    }
  ]

// ──────────────────────────────────────────────
// 3D Light Builder Tests
// ──────────────────────────────────────────────

let light3DTests =
  testList "3D Light builders" [
    test "AmbientLight3D.create sets color and default intensity" {
      let l = AmbientLight3D.create Mibo.Color.White
      Expect.equal l.Color Mibo.Color.White "Color should match"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        1.0
        "Default intensity should be 1"
    }

    test "AmbientLight3D.withIntensity overrides intensity" {
      let l =
        AmbientLight3D.create Mibo.Color.White
        |> AmbientLight3D.withIntensity 0.5f

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        0.5
        "Intensity should be overridden"
    }

    test "DirectionalLight3D.create sets direction and defaults" {
      let l = DirectionalLight3D.create v3a
      Expect.equal l.Direction v3a "Direction should match"
      Expect.equal l.Color Mibo.Color.White "Default color should be White"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        1.0
        "Default intensity should be 1"

      Expect.isTrue l.CastsShadows "Default CastsShadows should be true"
    }

    test "DirectionalLight3D with* overrides" {
      let l =
        DirectionalLight3D.create v3a
        |> DirectionalLight3D.withColor Mibo.Color.Red
        |> DirectionalLight3D.withIntensity 0.8f
        |> DirectionalLight3D.withCastsShadows false

      Expect.equal l.Color Mibo.Color.Red "Color should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        0.8
        "Intensity should be overridden"

      Expect.isFalse l.CastsShadows "CastsShadows should be overridden"
    }

    test "PointLight3D.create sets position, radius, and defaults" {
      let l = PointLight3D.create(v3a, 50.0f)
      Expect.equal l.Position v3a "Position should match"
      Expect.equal l.Color Mibo.Color.White "Default color should be White"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        1.0
        "Default intensity should be 1"

      Expect.floatClose
        Accuracy.medium
        (float l.Radius)
        50.0
        "Radius should match"

      Expect.floatClose
        Accuracy.medium
        (float l.Falloff)
        2.0
        "Default falloff should be 2"

      Expect.isFalse l.CastsShadows "Default CastsShadows should be false"
      Expect.isFalse l.ShadowBias.IsSome "Default ShadowBias should be None"
    }

    test "PointLight3D with* overrides" {
      let l =
        PointLight3D.create(v3a, 50.0f)
        |> PointLight3D.withColor Mibo.Color.Blue
        |> PointLight3D.withIntensity 0.5f
        |> PointLight3D.withFalloff 1.0f
        |> PointLight3D.withCastsShadows true
        |> PointLight3D.withShadowBias 0.005f

      Expect.equal l.Color Mibo.Color.Blue "Color should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        0.5
        "Intensity should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.Falloff)
        1.0
        "Falloff should be overridden"

      Expect.isTrue l.CastsShadows "CastsShadows should be overridden"

      match l.ShadowBias with
      | ValueSome b ->
        Expect.floatClose
          Accuracy.medium
          (float b)
          0.005
          "ShadowBias should be overridden"
      | ValueNone -> Tests.failtest "Expected ShadowBias to be set"
    }

    test "SpotLight3D.create sets position, direction, radius, and defaults" {
      let l = SpotLight3D.create(v3a, v3b, 100.0f)
      Expect.equal l.Position v3a "Position should match"
      Expect.equal l.Direction v3b "Direction should match"
      Expect.equal l.Color Mibo.Color.White "Default color should be White"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        1.0
        "Default intensity should be 1"

      Expect.floatClose
        Accuracy.medium
        (float l.Radius)
        100.0
        "Radius should match"

      Expect.floatClose
        Accuracy.medium
        (float l.InnerCutoff)
        0.5
        "Default inner cutoff should be 0.5"

      Expect.floatClose
        Accuracy.medium
        (float l.OuterCutoff)
        0.7
        "Default outer cutoff should be 0.7"

      Expect.isFalse l.CastsShadows "Default CastsShadows should be false"
      Expect.isFalse l.ShadowBias.IsSome "Default ShadowBias should be None"
    }

    test "SpotLight3D with* overrides" {
      let l =
        SpotLight3D.create(v3a, v3b, 100.0f)
        |> SpotLight3D.withColor Mibo.Color.Green
        |> SpotLight3D.withIntensity 0.7f
        |> SpotLight3D.withCutoff 0.3f 0.6f
        |> SpotLight3D.withCastsShadows true
        |> SpotLight3D.withShadowBias 0.01f

      Expect.equal l.Color Mibo.Color.Green "Color should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        0.7
        "Intensity should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.InnerCutoff)
        0.3
        "InnerCutoff should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.OuterCutoff)
        0.6
        "OuterCutoff should be overridden"

      Expect.isTrue l.CastsShadows "CastsShadows should be overridden"

      match l.ShadowBias with
      | ValueSome b ->
        Expect.floatClose
          Accuracy.medium
          (float b)
          0.01
          "ShadowBias should be overridden"
      | ValueNone -> Tests.failtest "Expected ShadowBias to be set"
    }
  ]

// ──────────────────────────────────────────────
// RenderBuffer3D Tests
// ──────────────────────────────────────────────

let renderBuffer3DTests =
  testList "RenderBuffer3D" [
    test "new buffer has count 0" {
      use buf = new RenderBuffer3D()
      Expect.equal buf.Count 0 "Empty buffer should have count 0"
    }

    test "Add increments count" {
      use buf = new RenderBuffer3D()
      let cmd = Command3D.drawLine3D v3a v3b Color.White
      buf.Add(cmd)
      Expect.equal buf.Count 1 "Count should be 1 after adding one command"
      buf.Add(cmd)
      Expect.equal buf.Count 2 "Count should be 2 after adding two commands"
    }

    test "Item returns the added command" {
      use buf = new RenderBuffer3D()
      let cmd = Command3D.drawLine3D v3a v3b Color.Red
      buf.Add(cmd)

      match buf.Item 0 with
      | Command3D.DrawLine3D(s, f, c) ->
        Expect.equal s v3a "Start should match"
        Expect.equal f v3b "Finish should match"
        Expect.equal c Color.Red "Color should match"
      | _ -> Tests.failtest "Expected DrawLine3D"
    }

    test "Clear resets count to 0" {
      use buf = new RenderBuffer3D()
      buf.Add(Command3D.drawLine3D v3a v3b Color.White)
      buf.Add(Command3D.drawLine3D v3b v3c Color.White)
      buf.Clear()
      Expect.equal buf.Count 0 "Count should be 0 after clear"
    }

    test "Sort with custom comparer reorders commands" {
      use buf = new RenderBuffer3D()
      buf.Add(Command3D.drawLine3D v3a v3b (Color(50uy, 0uy, 255uy, 255uy))) // R=50
      buf.Add(Command3D.drawLine3D v3b v3c (Color(255uy, 0uy, 0uy, 255uy))) // R=255
      buf.Add(Command3D.drawLine3D v3c v3a (Color(100uy, 255uy, 0uy, 255uy))) // R=100

      // Sort by color R channel descending: R=255 first, R=100, R=50 last
      let comparer =
        { new System.Collections.Generic.IComparer<Command3D> with
            member _.Compare(a, b) =
              let getColor cmd =
                match cmd with
                | Command3D.DrawLine3D(_, _, c) -> int c.R
                | _ -> 0

              -(getColor a - getColor b) // descending
        }

      buf.Sort(comparer)

      match buf.Item 0, buf.Item 1, buf.Item 2 with
      | Command3D.DrawLine3D(_, _, c1),
        Command3D.DrawLine3D(_, _, c2),
        Command3D.DrawLine3D(_, _, c3) ->
        Expect.equal c1.R 255uy "First should have R=255"
        Expect.equal c2.R 100uy "Second should have R=100"
        Expect.equal c3.R 50uy "Third should have R=50"
      | _ -> Tests.failtest "Expected DrawLine3D"
    }

    test "Sort on empty buffer does not crash" {
      use buf = new RenderBuffer3D()

      let comparer =
        { new System.Collections.Generic.IComparer<Command3D> with
            member _.Compare(_, _) = 0
        }

      buf.Sort(comparer)
      Expect.equal buf.Count 0 "Count should still be 0"
    }

    test "Buffer expands capacity when full" {
      use buf = new RenderBuffer3D(capacity = 4)

      for i in 0..9 do
        buf.Add(
          Command3D.drawLine3D (Vector3(float32 i, 0.0f, 0.0f)) v3b Color.White
        )

      Expect.equal
        buf.Count
        10
        "Should have 10 items after exceeding initial capacity"

      match buf.Item 9 with
      | Command3D.DrawLine3D(s, _, _) ->
        Expect.floatClose
          Accuracy.medium
          (float s.X)
          9.0
          "Last item should have X=9"
      | _ -> Tests.failtest "Expected DrawLine3D"
    }

    test "Clear + repopulate cycle works (frame lifecycle)" {
      use buf = new RenderBuffer3D()
      // Frame 1
      buf.Add(Command3D.drawLine3D v3a v3b Color.Red)
      buf.Add(Command3D.drawLine3D v3b v3c Color.Blue)
      Expect.equal buf.Count 2 "Frame 1 should have 2 commands"

      // Frame 2
      buf.Clear()
      Expect.equal buf.Count 0 "After clear should be 0"
      buf.Add(Command3D.drawLine3D v3c v3a Color.Green)
      Expect.equal buf.Count 1 "Frame 2 should have 1 command"

      match buf.Item 0 with
      | Command3D.DrawLine3D(_, _, c) ->
        Expect.equal c Color.Green "Should be the frame 2 command"
      | _ -> Tests.failtest "Expected DrawLine3D"
    }

    test "Dispose returns items to pool and resets" {
      let buf = new RenderBuffer3D()
      buf.Add(Command3D.drawLine3D v3a v3b Color.White)
      (buf :> IDisposable).Dispose()
      Expect.equal buf.Count 0 "Count should be 0 after dispose"
    }
  ]

// ──────────────────────────────────────────────
// Draw3D DSL Tests
// ──────────────────────────────────────────────

let draw3DDSLTests =
  testList "Draw3D DSL" [
    test "Draw3D functions return the same buffer for chaining" {
      use buf = new RenderBuffer3D()
      let returned = buf |> Draw3D.drawLine3D v3a v3b Color.White

      Expect.isTrue
        (obj.ReferenceEquals(buf, returned))
        "Draw3D should return the same buffer"
    }

    test "Draw3D pipeline adds commands in expected order" {
      use buf = new RenderBuffer3D()

      buf
      |> Draw3D.drawLine3D v3a v3b Color.Red
      |> Draw3D.drawBillboard tex v3a v2a Color.Blue
      |> Draw3D.setShadowOrigin v3b
      |> Draw3D.drop

      Expect.equal buf.Count 3 "Should have 3 commands"

      match buf.Item 0 with
      | Command3D.DrawLine3D _ -> ()
      | _ -> Tests.failtest "First should be DrawLine3D"

      match buf.Item 1 with
      | Command3D.DrawBillboard _ -> ()
      | _ -> Tests.failtest "Second should be DrawBillboard"

      match buf.Item 2 with
      | Command3D.SetShadowOrigin _ -> ()
      | _ -> Tests.failtest "Third should be SetShadowOrigin"
    }

    test "Draw3D.modelWith adds a DrawModelWith(All) command" {
      use buf = new RenderBuffer3D()
      let mat = Material3D.colored Color.Green
      buf |> Draw3D.modelWith model identity mat |> ignore

      Expect.equal buf.Count 1 "Should have 1 command"

      match buf.Item 0 with
      | Command3D.DrawModelWith(_, _, MaterialOverride.All o) ->
        Expect.equal o.AlbedoColor Color.Green "Override albedo should match"
      | _ -> Tests.failtest "Expected DrawModelWith (All)"
    }

    test "Draw3D.modelWithPerMesh adds a DrawModelWith(PerMesh) command" {
      use buf = new RenderBuffer3D()
      let resolver(_mi: int) = Material3D.colored Color.White
      buf |> Draw3D.modelWithPerMesh model identity resolver |> ignore

      Expect.equal buf.Count 1 "Should have 1 command"

      match buf.Item 0 with
      | Command3D.DrawModelWith(_, _, MaterialOverride.PerMesh _) -> ()
      | _ -> Tests.failtest "Expected DrawModelWith (PerMesh)"
    }

    test "Draw3D.drop returns unit" {
      use buf = new RenderBuffer3D()
      let result = Draw3D.drop buf
      Expect.equal result () "drop should return unit"
    }

    test "Draw3D camera commands chain correctly" {
      use buf = new RenderBuffer3D()
      let cam = Unchecked.defaultof<Camera3D>

      buf
      |> Draw3D.beginCamera cam
      |> Draw3D.drawLine3D v3a v3b Color.White
      |> Draw3D.endCamera
      |> Draw3D.drop

      Expect.equal buf.Count 3 "Should have 3 commands"

      match buf.Item 0 with
      | Command3D.BeginCamera _ -> ()
      | _ -> Tests.failtest "First should be BeginCamera"

      match buf.Item 2 with
      | Command3D.EndCamera -> ()
      | _ -> Tests.failtest "Third should be EndCamera"
    }

    test "Draw3D lighting commands chain correctly" {
      use buf = new RenderBuffer3D()
      let ambient = AmbientLight3D.create Mibo.Color.White
      let dirLight = DirectionalLight3D.create v3a
      let ptLight = PointLight3D.create(v3b, 50.0f)
      let spotLight = SpotLight3D.create(v3a, v3b, 100.0f)

      buf
      |> Draw3D.setAmbientLight ambient
      |> Draw3D.addDirectionalLight dirLight
      |> Draw3D.addPointLight ptLight
      |> Draw3D.addSpotLight spotLight
      |> Draw3D.enableShadows
      |> Draw3D.disableShadows
      |> Draw3D.drop

      Expect.equal buf.Count 6 "Should have 6 commands"

      match buf.Item 0 with
      | Command3D.SetAmbientLight _ -> ()
      | _ -> Tests.failtest "First should be SetAmbientLight"

      match buf.Item 1 with
      | Command3D.AddDirectionalLight _ -> ()
      | _ -> Tests.failtest "Second should be AddDirectionalLight"

      match buf.Item 2 with
      | Command3D.AddPointLight _ -> ()
      | _ -> Tests.failtest "Third should be AddPointLight"

      match buf.Item 3 with
      | Command3D.AddSpotLight _ -> ()
      | _ -> Tests.failtest "Fourth should be AddSpotLight"

      match buf.Item 4 with
      | Command3D.EnableShadows -> ()
      | _ -> Tests.failtest "Fifth should be EnableShadows"

      match buf.Item 5 with
      | Command3D.DisableShadows -> ()
      | _ -> Tests.failtest "Sixth should be DisableShadows"
    }

    test "Draw3D.postProcess adds a PostProcess command and invokes the action" {
      use buf = new RenderBuffer3D()
      let called = ref false

      let action(_: PostProcessContext3D) = called := true

      buf |> Draw3D.postProcess action |> Draw3D.drop |> ignore

      Expect.equal buf.Count 1 "Should have 1 command"

      match buf.Item 0 with
      | Command3D.PostProcess a ->
        a Unchecked.defaultof<_>
        Expect.isTrue !called "action should be invokable"
      | _ -> Tests.failtest "Should be PostProcess"
    }
  ]

// ──────────────────────────────────────────────
// Renderer3DConfig Tests
// ──────────────────────────────────────────────

let renderer3DConfigTests =
  testList "Renderer3DConfig" [
    test "defaults has black clear color" {
      let cfg = Renderer3DConfig.defaults

      match cfg.ClearColor with
      | ValueSome c ->
        Expect.equal c Color.Black "Default clear color should be Black"
      | ValueNone -> Tests.failtest "Expected ValueSome for ClearColor"
    }

    test "noClear has ValueNone clear color" {
      let cfg = Renderer3DConfig.noClear

      Expect.isFalse
        cfg.ClearColor.IsSome
        "noClear should have ValueNone ClearColor"
    }
  ]

// ──────────────────────────────────────────────
// ShadowAtlasConfig / ShadowBiasConfig Tests
// ──────────────────────────────────────────────

let shadowConfigTests =
  testList "Shadow configs" [
    test "ShadowAtlasConfig.defaults has expected values" {
      let cfg = Pipelines.ShadowAtlasConfig.defaults
      Expect.equal cfg.Resolution 2048 "Default resolution should be 2048"
      Expect.equal cfg.MaxCasters 16 "Default MaxCasters should be 16"

      Expect.isFalse
        cfg.ShowDebugOverlay
        "Default ShowDebugOverlay should be false"

      Expect.floatClose
        Accuracy.medium
        (float cfg.GridSnapSize)
        2.0
        "Default GridSnapSize should be 2.0"
    }

    test "ShadowBiasConfig.defaults has expected values" {
      let cfg = Pipelines.ShadowBiasConfig.defaults

      Expect.floatClose
        Accuracy.medium
        (float cfg.DirectionalBias)
        0.0005
        "DirectionalBias"

      Expect.floatClose Accuracy.medium (float cfg.PointBias) 0.01 "PointBias"
      Expect.floatClose Accuracy.medium (float cfg.SpotBias) 0.001 "SpotBias"

      Expect.floatClose
        Accuracy.medium
        (float cfg.SlopeScaleBias)
        0.0005
        "SlopeScaleBias"
    }
  ]

// ──────────────────────────────────────────────
// ShadowAtlas Tests (GPU-free members)
// ──────────────────────────────────────────────

let shadowAtlasTests =
  testList "ShadowAtlas" [
    test "GetUVOffsetScale returns correct UV for region 0 of 4x4 grid" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 16
            Resolution = 2048
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      let uv = atlas.GetUVOffsetScale(0)
      Expect.floatClose Accuracy.medium (float uv.X) 0.0 "Offset X for region 0"
      Expect.floatClose Accuracy.medium (float uv.Y) 0.0 "Offset Y for region 0"
      Expect.floatClose Accuracy.medium (float uv.Z) 0.25 "Scale X for 4x4 grid"
      Expect.floatClose Accuracy.medium (float uv.W) 0.25 "Scale Y for 4x4 grid"
    }

    test "GetUVOffsetScale returns correct UV for region 1 of 4x4 grid" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 16
            Resolution = 2048
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      let uv = atlas.GetUVOffsetScale(1)

      Expect.floatClose
        Accuracy.medium
        (float uv.X)
        0.25
        "Offset X for region 1 (col 1)"

      Expect.floatClose
        Accuracy.medium
        (float uv.Y)
        0.0
        "Offset Y for region 1 (row 0)"

      Expect.floatClose Accuracy.medium (float uv.Z) 0.25 "Scale X"
      Expect.floatClose Accuracy.medium (float uv.W) 0.25 "Scale Y"
    }

    test "GetUVOffsetScale returns correct UV for region on second row" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 16
            Resolution = 2048
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      // Region 4 = row 1, col 0
      let uv = atlas.GetUVOffsetScale(4)

      Expect.floatClose
        Accuracy.medium
        (float uv.X)
        0.0
        "Offset X for region 4 (col 0)"

      Expect.floatClose
        Accuracy.medium
        (float uv.Y)
        0.25
        "Offset Y for region 4 (row 1)"
    }

    test "GetUVOffsetScale for last region of 4x4 grid" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 16
            Resolution = 2048
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      // Region 15 = row 3, col 3
      let uv = atlas.GetUVOffsetScale(15)

      Expect.floatClose
        Accuracy.medium
        (float uv.X)
        0.75
        "Offset X for region 15 (col 3)"

      Expect.floatClose
        Accuracy.medium
        (float uv.Y)
        0.75
        "Offset Y for region 15 (row 3)"

      Expect.floatClose Accuracy.medium (float uv.Z) 0.25 "Scale X"
      Expect.floatClose Accuracy.medium (float uv.W) 0.25 "Scale Y"
    }

    test "GetUVOffsetScale for 9-caster (3x3) grid" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 9
            Resolution = 2048
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      let uv = atlas.GetUVOffsetScale(0)
      let expectedScale = 1.0f / 3.0f

      Expect.floatClose
        Accuracy.medium
        (float uv.Z)
        (float expectedScale)
        "Scale X for 3x3 grid"

      Expect.floatClose
        Accuracy.medium
        (float uv.W)
        (float expectedScale)
        "Scale Y for 3x3 grid"
    }

    test "GetBias returns per-type bias when no override" {
      let cfg = Pipelines.ShadowAtlasConfig.defaults
      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)

      let dirCaster: Pipelines.ShadowCasterData = {
        Id = Unchecked.defaultof<_>
        Type = Pipelines.ShadowCasterType.Directional
        LightPosition = Vector3.Zero
        LightDirection = v3a
        LightTarget = Vector3.Zero
        AtlasRegion = 0
        RegionCount = 1
        Enabled = true
        BiasOverride = ValueNone
        ViewProj = Matrix4x4.Identity
      }

      let dirBias = atlas.GetBias(dirCaster)

      Expect.floatClose
        Accuracy.medium
        (float dirBias)
        0.0005
        "Directional bias should match config"

      let ptCaster = {
        dirCaster with
            Type = Pipelines.ShadowCasterType.Point
      }

      let ptBias = atlas.GetBias(ptCaster)

      Expect.floatClose
        Accuracy.medium
        (float ptBias)
        0.01
        "Point bias should match config"

      let spotCaster = {
        dirCaster with
            Type = Pipelines.ShadowCasterType.Spot
      }

      let spotBias = atlas.GetBias(spotCaster)

      Expect.floatClose
        Accuracy.medium
        (float spotBias)
        0.001
        "Spot bias should match config"
    }

    test "GetBias returns override when set" {
      let cfg = Pipelines.ShadowAtlasConfig.defaults
      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)

      let caster: Pipelines.ShadowCasterData = {
        Id = Unchecked.defaultof<_>
        Type = Pipelines.ShadowCasterType.Directional
        LightPosition = Vector3.Zero
        LightDirection = v3a
        LightTarget = Vector3.Zero
        AtlasRegion = 0
        RegionCount = 1
        Enabled = true
        BiasOverride = ValueSome 0.05f
        ViewProj = Matrix4x4.Identity
      }

      let b = atlas.GetBias(caster)

      Expect.floatClose
        Accuracy.medium
        (float b)
        0.05
        "Should return override bias"
    }

    test "GridSize and RegionSize computed correctly for 16 casters at 2048" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 16
            Resolution = 2048
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      Expect.equal atlas.GridSize 4 "GridSize should be 4 for 16 casters"
      Expect.equal atlas.RegionSize 512 "RegionSize should be 512 for 2048/4"
    }

    test "GridSize and RegionSize computed correctly for 9 casters at 1024" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 9
            Resolution = 1024
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      Expect.equal atlas.GridSize 3 "GridSize should be 3 for 9 casters"
      Expect.equal atlas.RegionSize 341 "RegionSize should be 341 for 1024/3"
    }

    test "AddCaster allocates slots sequentially" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 4
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)

      let id1 =
        atlas.AddCaster(
          Pipelines.ShadowCasterType.Directional,
          Vector3.Zero,
          v3a,
          Vector3.Zero,
          true,
          ValueNone
        )

      Expect.isTrue id1.IsSome "First caster should be allocated"

      let id2 =
        atlas.AddCaster(
          Pipelines.ShadowCasterType.Point,
          v3a,
          v3b,
          Vector3.Zero,
          true,
          ValueNone
        )

      Expect.isTrue id2.IsSome "Second caster should be allocated"
      Expect.equal atlas.Count 2 "Should have 2 casters"
    }

    test "AddCaster returns None when atlas is full" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 4
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)

      for _ in 1..4 do
        atlas.AddCaster(
          Pipelines.ShadowCasterType.Directional,
          Vector3.Zero,
          v3a,
          Vector3.Zero,
          true,
          ValueNone
        )
        |> ignore

      let fifth =
        atlas.AddCaster(
          Pipelines.ShadowCasterType.Directional,
          Vector3.Zero,
          v3a,
          Vector3.Zero,
          true,
          ValueNone
        )

      Expect.isFalse fifth.IsSome "Fifth caster should not be allocated"
      Expect.equal atlas.Count 4 "Should still have 4 casters"
    }

    test "RemoveCaster frees slot for reuse" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 4
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)

      let id1 =
        atlas.AddCaster(
          Pipelines.ShadowCasterType.Directional,
          Vector3.Zero,
          v3a,
          Vector3.Zero,
          true,
          ValueNone
        )

      match id1 with
      | ValueSome casterId ->
        atlas.RemoveCaster(casterId)
        Expect.equal atlas.Count 0 "Should have 0 casters after removal"
      | ValueNone -> Tests.failtest "Expected caster to be allocated"
    }

    test "UpdateCaster modifies light properties" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 4
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)

      let id =
        atlas.AddCaster(
          Pipelines.ShadowCasterType.Point,
          v3a,
          v3b,
          Vector3.Zero,
          true,
          ValueNone
        )

      match id with
      | ValueSome casterId ->
        let newPos = Vector3(100.0f, 200.0f, 300.0f)
        atlas.UpdateCaster(casterId, lightPosition = newPos)

        let caster = atlas.Casters |> Seq.find(fun c -> c.Id = casterId)
        Expect.equal caster.LightPosition newPos "Position should be updated"
      | ValueNone -> Tests.failtest "Expected caster to be allocated"
    }

    test "MaxCasters=1 produces valid atlas (1x1 grid)" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 1
            Resolution = 1024
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)
      Expect.equal atlas.GridSize 1 "GridSize should be 1"
      Expect.equal atlas.RegionSize 1024 "RegionSize should equal resolution"
    }

    test "non-perfect-square MaxCasters throws" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 5
      }

      let bias = Pipelines.ShadowBiasConfig.defaults

      Expect.throws
        (fun () -> Pipelines.ShadowAtlas(cfg, bias) |> ignore)
        "Should throw for non-perfect square"
    }

    test "Clear resets caster count" {
      let cfg = {
        Pipelines.ShadowAtlasConfig.defaults with
            MaxCasters = 4
      }

      let bias = Pipelines.ShadowBiasConfig.defaults
      let atlas = Pipelines.ShadowAtlas(cfg, bias)

      atlas.AddCaster(
        Pipelines.ShadowCasterType.Directional,
        Vector3.Zero,
        v3a,
        Vector3.Zero,
        true,
        ValueNone
      )
      |> ignore

      Expect.equal atlas.Count 1 "Should have 1 caster"
      atlas.Clear()
      Expect.equal atlas.Count 0 "Should have 0 casters after clear"
    }
  ]

// ──────────────────────────────────────────────
// Pipeline Helper Tests (non-inline functions only)
// inline functions (MaterialKey.fromMaterial3D, colorToVec3, etc.)
// cannot be called across assembly boundaries with InternalsVisibleTo
// ──────────────────────────────────────────────

let preScanTests =
  testList "preScan" [
    test "collects first camera from BeginCamera" {
      use buffer = new RenderBuffer3D()
      let cam = Camera3D(Position = v3a, Target = v3b)
      buffer.Add(Command3D.beginCamera cam)
      let lights = createLightBuffers(8, 4)
      let mutable fwd = Unchecked.defaultof<ShaderVariant>
      let mutable inst = Unchecked.defaultof<ShaderVariant>
      let mutable sk = Unchecked.defaultof<ShaderVariant>

      let fs =
        preScan(
          buffer,
          lights,
          &fwd,
          &inst,
          &sk,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>
        )

      match fs.Camera with
      | ValueSome c ->
        Expect.equal c.Position v3a "Camera position should match"
      | ValueNone -> Tests.failtest "Expected camera"
    }

    test "first camera wins over subsequent" {
      use buffer = new RenderBuffer3D()
      let cam1 = Camera3D(Position = v3a)
      let cam2 = Camera3D(Position = v3b)
      buffer.Add(Command3D.beginCamera cam1)
      buffer.Add(Command3D.beginCamera cam2)
      let lights = createLightBuffers(8, 4)
      let mutable fwd = Unchecked.defaultof<ShaderVariant>
      let mutable inst = Unchecked.defaultof<ShaderVariant>
      let mutable sk = Unchecked.defaultof<ShaderVariant>

      let fs =
        preScan(
          buffer,
          lights,
          &fwd,
          &inst,
          &sk,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>
        )

      match fs.Camera with
      | ValueSome c -> Expect.equal c.Position v3a "Should keep first camera"
      | ValueNone -> Tests.failtest "Expected camera"
    }

    test "collects shadow origin" {
      use buffer = new RenderBuffer3D()
      buffer.Add(Command3D.setShadowOrigin v3c)
      let lights = createLightBuffers(8, 4)
      let mutable fwd = Unchecked.defaultof<ShaderVariant>
      let mutable inst = Unchecked.defaultof<ShaderVariant>
      let mutable sk = Unchecked.defaultof<ShaderVariant>

      let fs =
        preScan(
          buffer,
          lights,
          &fwd,
          &inst,
          &sk,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>
        )

      match fs.ShadowOrigin with
      | ValueSome o -> Expect.equal o v3c "Shadow origin should match"
      | ValueNone -> Tests.failtest "Expected shadow origin"
    }

    test "collects lights from buffer" {
      use buffer = new RenderBuffer3D()

      buffer.Add(
        Command3D.setAmbientLight(AmbientLight3D.create Mibo.Color.White)
      )

      buffer.Add(Command3D.addDirectionalLight(DirectionalLight3D.create v3a))
      buffer.Add(Command3D.addPointLight(PointLight3D.create(v3b, 10.0f)))
      buffer.Add(Command3D.addSpotLight(SpotLight3D.create(v3c, v3a, 5.0f)))
      let lights = createLightBuffers(8, 4)
      let mutable fwd = Unchecked.defaultof<ShaderVariant>
      let mutable inst = Unchecked.defaultof<ShaderVariant>
      let mutable sk = Unchecked.defaultof<ShaderVariant>

      let _fs =
        preScan(
          buffer,
          lights,
          &fwd,
          &inst,
          &sk,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>
        )

      Expect.isTrue lights.Ambient.IsSome "Should have 1 ambient"
      Expect.equal lights.DirLights.Count 1 "Should have 1 dir light"
      Expect.equal lights.PointLights.Count 1 "Should have 1 point light"
      Expect.equal lights.SpotLights.Count 1 "Should have 1 spot light"
    }

    test "empty buffer returns empty frame state" {
      use buffer = new RenderBuffer3D()
      let lights = createLightBuffers(8, 4)
      let mutable fwd = Unchecked.defaultof<ShaderVariant>
      let mutable inst = Unchecked.defaultof<ShaderVariant>
      let mutable sk = Unchecked.defaultof<ShaderVariant>

      let fs =
        preScan(
          buffer,
          lights,
          &fwd,
          &inst,
          &sk,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>,
          Unchecked.defaultof<_>
        )

      Expect.equal fs.Camera ValueNone "No camera"
      Expect.equal fs.ShadowOrigin ValueNone "No shadow origin"
    }
  ]

// ──────────────────────────────────────────────
// All tests
// ──────────────────────────────────────────────

[<Tests>]
let tests =
  testList "Graphics3D" [
    command3DFactoryTests
    material3DTests
    light3DTests
    renderBuffer3DTests
    draw3DDSLTests
    renderer3DConfigTests
    shadowConfigTests
    shadowAtlasTests
    preScanTests
  ]
