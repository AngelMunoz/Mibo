namespace Defli.Raylib

open System.Numerics
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Defli.World.Systems.Camera

// ─────────────────────────────────────────────────────────────
// CameraView — the raylib EDGE of the neutral camera: builds the
// native Camera2D from the sim's CameraState each frame ("convert at
// edges"). The clamp and the deterministic shake are applied on the
// copy; picking (screenToWorld) composes the same clamp so the
// cursor maps to the cells the player actually sees.
// ─────────────────────────────────────────────────────────────

module CameraView =

  /// The camera as recorded into the buffer: clamped to the world,
  /// with the deterministic shake applied to the target. The screen
  /// offset is the viewport center (a render-time fact the sim never
  /// stores).
  let toRaylib (state: CameraState) (viewport: Vector2) : Camera2D =
    let clamped = Camera.clampToWorld state viewport
    let target = clamped.Target + shakeOffset clamped
    Camera2D(viewport / 2f, target, state.Rotation, state.Zoom)

  /// Records the camera into the buffer (the world-space block).
  let beginFrame
    (state: CameraState)
    (viewport: Vector2)
    (buffer: RenderBuffer2D)
    =
    buffer.beginCamera(toRaylib state viewport).drop()

  /// Screen → world through the DRAWN camera (clamped, shake-free —
  /// picking must match the cells the player sees, not the shaken
  /// ones).
  let screenToWorld
    (state: CameraState)
    (viewport: Vector2)
    (screenPos: Vector2)
    : Vector2 =
    Camera.screenToWorld (Camera.clampToWorld state viewport) viewport screenPos

  /// The culling rect for the map passes: the clamped view inflated
  /// by 15% on each side (CellGrid2D.iterVisible culls to it).
  let cullingBounds (state: CameraState) (viewport: Vector2) : Rectangle =
    let struct (min, max) = Camera.viewBounds state viewport
    let w = max.X - min.X
    let h = max.Y - min.Y
    let marginX = w * 0.15f
    let marginY = h * 0.15f
    Rectangle(min.X - marginX, min.Y - marginY, w * 1.3f, h * 1.3f)
