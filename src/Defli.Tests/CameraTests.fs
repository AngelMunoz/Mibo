module Defli.Tests.CameraTests

open Expecto
open System.Numerics
open Defli.World.Systems
open Defli.World.Systems.Camera

// Phase 4 — the Camera sub-system (Kimo analog): the sim stores
// BACKEND-NEUTRAL camera facts (CameraState); the native camera is
// built at the view edge. All assertions read the State fields
// (Target/Zoom/WorldSize/Shake*).

let private worldSize = Vector2(1280f, 768f)
let private viewport = Vector2(1280f, 800f)
let private model() = Camera.Camera.init worldSize

/// A bare state for the pure-math tests (no model).
let private state (target: Vector2) (zoom: float32) (worldSize: Vector2) = {
  Target = target
  Zoom = zoom
  Rotation = 0f
  WorldSize = worldSize
  ShakeRemaining = 0f
  ShakeStrength = 0f
}

let tests =
  testList "Camera" [
    testCase "init centers on the world center at zoom 1" (fun () ->
      let m = model()
      Expect.equal m.State.Target (Vector2(640f, 384f)) "target"
      Expect.equal m.State.Zoom 1f "zoom"
      Expect.equal m.State.WorldSize worldSize "world size")

    testCase "Pan moves the target opposite the drag, scaled by zoom" (fun () ->
      let m = model()
      // Drag right 100 px at zoom 1 → the world moves left 100.
      Camera.Camera.update (CameraMsg.Pan(Vector2(100f, 0f))) m
      Expect.equal m.State.Target (Vector2(540f, 384f)) "pan at zoom 1"

      // At zoom 2 the same drag moves the world half as far.
      Camera.Camera.update (CameraMsg.ZoomBy 2f) m
      Camera.Camera.update (CameraMsg.Pan(Vector2(100f, 0f))) m
      Expect.equal m.State.Target (Vector2(490f, 384f)) "pan at zoom 2")

    testCase "ZoomBy multiplies and clamps to the zoom limits" (fun () ->
      let m = model()
      Camera.Camera.update (CameraMsg.ZoomBy 2f) m
      Expect.equal m.State.Zoom 2f "zoomed in"

      Camera.Camera.update (CameraMsg.ZoomBy 2f) m
      Expect.equal m.State.Zoom Camera.MaxZoom "clamped at max"

      Camera.Camera.update (CameraMsg.ZoomBy 0.01f) m
      Expect.equal m.State.Zoom Camera.MinZoom "clamped at min")

    testCase "Shake sets the timer, tick decays it, offset expires" (fun () ->
      let m = model()
      Camera.Camera.update (CameraMsg.Shake 8f) m
      Expect.equal m.State.ShakeRemaining Camera.ShakeDuration "timer set"
      Expect.notEqual (shakeOffset m.State) Vector2.Zero "offset active"

      Camera.Camera.tick 0.2f m

      Expect.equal
        m.State.ShakeRemaining
        (Camera.ShakeDuration - 0.2f)
        "decayed"

      Camera.Camera.tick 1f m
      Expect.equal m.State.ShakeRemaining 0f "expired"

      Expect.equal
        (shakeOffset m.State)
        Vector2.Zero
        "offset zero when expired")

    testCase "Reset restores the world center at zoom 1" (fun () ->
      let m = model()
      Camera.Camera.update (CameraMsg.Pan(Vector2(400f, 300f))) m
      Camera.Camera.update (CameraMsg.ZoomBy 2f) m
      Camera.Camera.update (CameraMsg.Shake 8f) m
      Camera.Camera.update CameraMsg.Reset m
      Expect.equal m.State.Target (Vector2(640f, 384f)) "target"
      Expect.equal m.State.Zoom 1f "zoom"
      Expect.equal m.State.ShakeRemaining 0f "shake cleared")

    // ── Pure view math (the trimmed view-side tests, restored as
    //    neutral CameraState math — milestone-2 frontend) ──

    testCase
      "clampToWorld pins the target when the view fits the world"
      (fun () ->
        // Zoomed out so far the whole world fits in the view: the
        // target pins to the world center no matter where it is.
        let s = state Vector2.Zero 1f (Vector2(100f, 100f))
        let clamped = Camera.clampToWorld s viewport
        Expect.equal clamped.Target (Vector2(50f, 50f)) "pinned to center")

    testCase "clampToWorld clamps panning beyond the world edges" (fun () ->
      // Zoom 2 → view 640x400 inside the 1280x768 world: the target
      // is limited to [view/2, world-view/2] per axis.
      let s = state Vector2.Zero 2f worldSize
      let clamped = Camera.clampToWorld s viewport
      Expect.equal clamped.Target (Vector2(320f, 200f)) "clamped at min"

      let s2 = state (Vector2(2000f, 2000f)) 2f worldSize
      let clamped2 = Camera.clampToWorld s2 viewport
      Expect.equal clamped2.Target (Vector2(960f, 568f)) "clamped at max")

    testCase "screenToWorld inverts the view transform" (fun () ->
      let s = state (Vector2(640f, 384f)) 2f worldSize

      // worldToScreen = (world - target) * zoom + viewport/2 — the
      // exact inverse of the sim formula.
      let roundTrip(pos: Vector2) =
        let world = Camera.screenToWorld s viewport pos
        (world - s.Target) * s.Zoom + viewport / 2f

      Expect.equal (roundTrip(Vector2(0f, 0f))) (Vector2(0f, 0f)) "origin"

      Expect.equal
        (roundTrip(Vector2(640f, 400f)))
        (Vector2(640f, 400f))
        "center"

      Expect.equal
        (roundTrip(Vector2(1279f, 799f)))
        (Vector2(1279f, 799f))
        "corner")

    testCase "viewBounds is the clamped view rect" (fun () ->
      // Panned far past the corner: the clamped view hugs the world.
      let s = state (Vector2(5000f, 5000f)) 1f worldSize
      let struct (min, max) = Camera.viewBounds s viewport
      Expect.equal min (Vector2(0f, -16f)) "min" // height 800 > world 768 → pinned, sticks out
      Expect.equal max (Vector2(1280f, 784f)) "max"

      // Zoomed in: the view fits entirely inside the world.
      let s2 = state Vector2.Zero 2f worldSize
      let struct (min2, max2) = Camera.viewBounds s2 viewport
      Expect.equal min2 Vector2.Zero "min zoomed"
      Expect.equal max2 (Vector2(640f, 400f)) "max zoomed")
  ]

// Headless-port note: the raylib-specific conversion (native
// Camera2D construction, culling rectangle inflation) lives in the
// Defli.Raylib frontend's CameraView; the sim holds only the neutral
// math above.
