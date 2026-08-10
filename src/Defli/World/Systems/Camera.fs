module Defli.World.Systems.Camera

open System
open System.Numerics
open Raylib_cs
open Defli.World

// ─────────────────────────────────────────────────────────────
// Camera sub-system — owns the single 2D camera (Kimo analog:
// World/Systems/Camera.fs). The state IS the raylib Camera2D — a
// mutable struct — mutated IN PLACE by Mibo's byref helpers
// (Camera2D.clampTarget/screenToWorld/viewportBounds). It is
// created once at init and never re-created; the view never builds
// a throwaway camera.
//
// The window size is a RENDER-TIME fact (the sim is headless): the
// shell supplies it once at boot via setViewport (the window is
// fixed), which sets the camera's screen offset.
//
// No PrevTarget lerp: Kimo interpolates because its sim runs at a
// different rate than its draw (30 Hz sim / draw-rate renders). In
// Shape C the sim and the view share the 60 Hz frame, so there is
// nothing to interpolate.
//
// Shake is deterministic (fixed-frequency sinusoids — no RNG), so
// the same tick sequence always produces the same offset.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type CameraMsg =
  /// Screen-space drag delta (pixels) — grab semantics: the world
  /// follows the cursor, so the camera target moves opposite the
  /// drag, scaled by the current zoom. Conversion happens HERE (the
  /// subsystem owns the zoom); callers send raw screen pixels.
  /// (Keyboard pan mirrors a drag, so the shell sends the opposite
  /// sign.)
  | Pan of screenDelta: Vector2
  /// Multiplicative zoom step (e.g. 1.1 = zoom in, 0.9 = zoom out).
  | ZoomBy of factor: float32
  | SetTarget of target: Vector2
  /// Kick the shake timer (amplitude in world pixels).
  | Shake of strength: float32
  /// Back to the world center at zoom 1 (viewport offset untouched).
  | Reset

type CameraModel() =
  /// The underlying raylib camera — MUTATED in place by the
  /// subsystem. A `val mutable` FIELD on purpose: Mibo's helpers
  /// take it byref (`&model.Camera`), which auto-properties cannot
  /// provide.
  [<DefaultValue>]
  val mutable Camera: Raylib_cs.Camera2D

  /// World bounds (0,0 → WorldSize) — the view clamps the camera
  /// target to them so panning never shows void outside the map.
  [<DefaultValue>]
  val mutable WorldSize: Vector2

  /// Seconds of shake left (decayed by Camera.tick).
  [<DefaultValue>]
  val mutable ShakeRemaining: float32

  /// Peak shake amplitude in world pixels.
  [<DefaultValue>]
  val mutable ShakeStrength: float32

module Camera =

  let MinZoom = 0.5f
  let MaxZoom = 3f
  let ShakeDuration = 0.35f

  let init(worldSize: Vector2) : CameraModel =
    CameraModel(
      WorldSize = worldSize,
      ShakeRemaining = 0f,
      ShakeStrength = 0f,
      Camera =
        Camera2D(
          Vector2.Zero, // screen offset — set by setViewport (shell, boot)
          worldSize / 2f, // target — world center
          0f, // rotation
          1f // zoom
        )
    )

  /// Render-time fact: the window size. The sim stays headless — the
  /// shell supplies it ONCE at boot (the window is fixed). Sets the
  /// camera's screen offset (the viewport center).
  let inline setViewport (viewport: Vector2) (model: CameraModel) : unit =
    model.Camera.Offset <- viewport / 2f

  /// Cold path: apply an input intent by mutating the underlying
  /// camera (never re-creating it). Mutates in place — no return.
  let update (msg: CameraMsg) (model: CameraModel) : unit =
    match msg with
    | Pan d ->
      model.Camera.Target <- model.Camera.Target - d / model.Camera.Zoom
    | ZoomBy f ->
      model.Camera.Zoom <- Math.Clamp(model.Camera.Zoom * f, MinZoom, MaxZoom)
    | SetTarget t -> model.Camera.Target <- t
    | Shake strength ->
      model.ShakeRemaining <- ShakeDuration
      model.ShakeStrength <- strength
    | Reset ->
      model.Camera.Target <- model.WorldSize / 2f
      model.Camera.Zoom <- 1f
      model.ShakeRemaining <- 0f
      model.ShakeStrength <- 0f

  /// Hot path (per RoomTick): decay the shake timer.
  let tick (dt: float32) (model: CameraModel) : unit =
    if model.ShakeRemaining > 0f then
      model.ShakeRemaining <- max 0f (model.ShakeRemaining - dt)

/// Deterministic shake offset (no RNG): fixed-frequency sinusoids
/// scaled by the remaining strength. Zero once the shake expired.
let inline shakeOffset(model: CameraModel) : Vector2 =
  if model.ShakeRemaining <= 0f then
    Vector2.Zero
  else
    let amp =
      model.ShakeStrength * (model.ShakeRemaining / Camera.ShakeDuration)

    Vector2(
      amp * MathF.Sin(model.ShakeRemaining * 47f),
      amp * MathF.Cos(model.ShakeRemaining * 37f)
    )
