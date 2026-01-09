module Mibo.Tests.Camera

open Expecto
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

[<Tests>]
let tests =
  testList "Camera" [
    let viewportSize = Point(800, 600)
    let viewport = Viewport(0, 0, 800, 600)

    testCase "Camera2D screen-to-world symmetry"
    <| fun _ ->
      let pos = Vector2(100.0f, 100.0f)
      let zoom = 1.5f
      let cam = Camera2D.create pos zoom viewportSize

      let worldPos = Vector2(200.0f, 300.0f)
      let screenPos = Camera2D.worldToScreen cam worldPos
      let roundTrip = Camera2D.screenToWorld cam screenPos

      Expect.floatClose
        Accuracy.medium
        (float roundTrip.X)
        (float worldPos.X)
        "World X should be identical after round trip"

      Expect.floatClose
        Accuracy.medium
        (float roundTrip.Y)
        (float worldPos.Y)
        "World Y should be identical after round trip"

    testCase "Camera2D viewportBounds captures visible area"
    <| fun _ ->
      let pos = Vector2(0.0f, 0.0f)
      let zoom = 1.0f // 1:1 scale
      let cam = Camera2D.create pos zoom viewportSize

      // With pos at 0,0 and centered, Bounds should be -400, -300, 800, 600
      let bounds = Camera2D.viewportBounds cam viewport

      Expect.equal bounds.X -400 "Viewport bounds X should be centered"
      Expect.equal bounds.Y -300 "Viewport bounds Y should be centered"

      Expect.equal
        bounds.Width
        800
        "Viewport bounds width should match viewport"

      Expect.equal
        bounds.Height
        600
        "Viewport bounds height should match viewport"

    testCase "Camera2D viewportBounds scales with zoom"
    <| fun _ ->
      let pos = Vector2(0.0f, 0.0f)
      let zoom = 2.0f // Zoom in (visible area decreases)
      let cam = Camera2D.create pos zoom viewportSize

      let bounds = Camera2D.viewportBounds cam viewport

      Expect.equal bounds.Width 400 "Visible width should be halved at 2x zoom"

      Expect.equal
        bounds.Height
        300
        "Visible height should be halved at 2x zoom"

    testCase "Camera3D screenPointToRay generates correct ray at center"
    <| fun _ ->
      let position = Vector3(0.0f, 0.0f, 10.0f)
      let target = Vector3.Zero
      let up = Vector3.Up
      let fov = MathHelper.PiOver4
      let aspect = 800.0f / 600.0f
      let cam = Camera3D.lookAt position target up fov aspect 0.1f 100.0f

      let screenCenter = Vector2(400.0f, 300.0f)
      let ray = Camera3D.screenPointToRay cam screenCenter viewport

      // Ray should start near the camera and point towards the origin (0,0,-1)
      Expect.floatClose
        Accuracy.medium
        (float ray.Position.X)
        0.0
        "Ray X should be 0"

      Expect.floatClose
        Accuracy.medium
        (float ray.Position.Y)
        0.0
        "Ray Y should be 0"

      Expect.isTrue (ray.Position.Z < 10.0f) "Ray should start at near plane"

      Expect.floatClose
        Accuracy.medium
        (float ray.Direction.X)
        0.0
        "Direction X should be 0"

      Expect.floatClose
        Accuracy.medium
        (float ray.Direction.Y)
        0.0
        "Direction Y should be 0"

      Expect.floatClose
        Accuracy.medium
        (float ray.Direction.Z)
        -1.0
        "Direction Z should be -1"
  ]
