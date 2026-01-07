module MiboSample.HueColor

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open MiboSample.Domain

// ─────────────────────────────────────────────────────────────
// Hue Color System
//
// Manages smooth color transitions via hue interpolation.
// Entities have a current hue that lerps toward a target hue.
// ─────────────────────────────────────────────────────────────

/// Convert HSV hue (0-360) to RGB Color (saturation=1, value=1)
let hueToColor(hue: float32) =
  let h = hue / 60.f
  let i = int h % 6
  let f = h - float32(int h)
  let q = 1.f - f

  match i with
  | 0 -> Color(1.f, f, 0.f)
  | 1 -> Color(q, 1.f, 0.f)
  | 2 -> Color(0.f, 1.f, f)
  | 3 -> Color(0.f, q, 1.f)
  | 4 -> Color(f, 0.f, 1.f)
  | _ -> Color(1.f, 0.f, q)

module World =

  /// Shift target hue by given amount (for triggering color change)
  let shiftTarget
    (entityId: Guid<EntityId>)
    (amount: float32)
    (targetHues: Map<Guid<EntityId>, float32>)
    : Map<Guid<EntityId>, float32> =
    let current = Map.find entityId targetHues
    let next = (current + amount) % 360.f
    Map.add entityId next targetHues

  /// Tick result: new hue value if changed, None if already at target
  [<Struct>]
  type TickResult = { NewHue: float32; Changed: bool }

  /// Lerp current hue toward target. Returns new hue and whether it changed.
  let tick
    (dt: float32)
    (entityId: Guid<EntityId>)
    (hues: Map<Guid<EntityId>, float32>)
    (targetHues: Map<Guid<EntityId>, float32>)
    (lerpSpeed: float32)
    : TickResult =
    let current = Map.find entityId hues
    let target = Map.find entityId targetHues
    let diff = target - current

    if abs diff < 0.1f then
      { NewHue = current; Changed = false }
    else
      let newHue = (current + diff * dt * lerpSpeed) % 360.f
      { NewHue = newHue; Changed = true }
