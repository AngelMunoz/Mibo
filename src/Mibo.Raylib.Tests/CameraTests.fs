module Mibo.Raylib.Tests.Camera

open System
open Expecto
open System.Numerics
open Mibo.Elmish

[<Tests>]
let tests =
  testList "Camera" [
    let viewportWidth = 800f
    let viewportHeight = 600f

    testCase "Camera2D screen-to-world symmetry"
    <| fun _ ->
      let pos = Vector2(100.0f, 100.0f)
      let zoom = 1.5f
      let vpSize = Vector2(viewportWidth, viewportHeight)
      let cam = Camera2D.create pos zoom vpSize

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
      let zoom = 1.0f
      let vpSize = Vector2(viewportWidth, viewportHeight)
      let cam = Camera2D.create pos zoom vpSize

      let bounds = Camera2D.viewportBounds cam viewportWidth viewportHeight

      Expect.equal bounds.X -400.0f "Viewport bounds X should be centered"
      Expect.equal bounds.Y -300.0f "Viewport bounds Y should be centered"

      Expect.equal
        bounds.Width
        800.0f
        "Viewport bounds width should match viewport"

      Expect.equal
        bounds.Height
        600.0f
        "Viewport bounds height should match viewport"

    testCase "Camera2D viewportBounds scales with zoom"
    <| fun _ ->
      let pos = Vector2(0.0f, 0.0f)
      let zoom = 2.0f
      let vpSize = Vector2(viewportWidth, viewportHeight)
      let cam = Camera2D.create pos zoom vpSize

      let bounds = Camera2D.viewportBounds cam viewportWidth viewportHeight

      Expect.equal
        bounds.Width
        400.0f
        "Visible width should be halved at 2x zoom"

      Expect.equal
        bounds.Height
        300.0f
        "Visible height should be halved at 2x zoom"

    testCase "Camera2D smoothFollow mutates target"
    <| fun _ ->
      let pos = Vector2(0.0f, 0.0f)
      let vpSize = Vector2(viewportWidth, viewportHeight)
      let mutable cam = Camera2D.create pos 1.0f vpSize
      Camera2D.smoothFollow &cam (Vector2(100.0f, 50.0f)) 0.5f
      Expect.equal cam.Target.X 50.0f "Camera target X should lerp halfway"
      Expect.equal cam.Target.Y 25.0f "Camera target Y should lerp halfway"

    testCase "Camera2D clampTarget enforces bounds"
    <| fun _ ->
      let pos = Vector2(0.0f, 0.0f)
      let vpSize = Vector2(viewportWidth, viewportHeight)
      let mutable cam = Camera2D.create pos 1.0f vpSize
      cam.Target <- Vector2(9999.0f, -9999.0f)
      Camera2D.clampTarget &cam 0.0f 0.0f 100.0f 100.0f

      Expect.equal
        cam.Target.X
        100.0f
        "Camera target X should be clamped to max"

      Expect.equal cam.Target.Y 0.0f "Camera target Y should be clamped to min"
  ]
