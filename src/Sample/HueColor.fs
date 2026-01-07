module MiboSample.HueColor

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open Mibo.Elmish
open MiboSample.Domain

// ─────────────────────────────────────────────────────────────
// HueColor System: ReadonlySystem<ModelSnapshot, 'Msg>
//
// Post-snapshot readonly system that works with immutable data.
// Returns a ReadonlySystem delegate for pipeline composition.
// ─────────────────────────────────────────────────────────────

/// Convert HSV hue (0-360) to RGB Color
let hueToColor (hue: float32) : Color =
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

/// Lerp hue toward target
let private lerpHue (current: float32) (target: float32) (dt: float32) (lerpSpeed: float32) : struct (float32 * bool) =
  let diff = target - current
  if abs diff < 0.1f then struct (current, false)
  else struct ((current + diff * dt * lerpSpeed) % 360.f, true)

/// Shift target hue (triggers color animation) - works on mutable Model
let shiftTarget (entityId: Guid<EntityId>) (amount: float32) (model: Model) : Model =
  let current = model.TargetHues |> Map.tryFind entityId |> Option.defaultValue 0f
  let next = (current + amount) % 360.f
  { model with TargetHues = Map.add entityId next model.TargetHues }

/// ReadonlySystem: lerps hues toward targets
let update (dt: float32) (lerpSpeed: float32) (snapshot: ModelSnapshot) : struct (ModelSnapshot * Cmd<'Msg> list) =
  let playerId = snapshot.PlayerId
  let current = snapshot.Hues |> Map.tryFind playerId |> Option.defaultValue 0f
  let target = snapshot.TargetHues |> Map.tryFind playerId |> Option.defaultValue 0f
  let struct (newHue, changed) = lerpHue current target dt lerpSpeed

  let newSnapshot =
    if changed then { snapshot with Hues = Map.add playerId newHue snapshot.Hues }
    else snapshot

  struct (newSnapshot, [])
