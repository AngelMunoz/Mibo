module MiboSample.Physics

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open Mibo.Elmish
open MiboSample.Domain

// ─────────────────────────────────────────────────────────────
// Physics System: MutableSystem<Model, 'Msg>
//
// Pre-snapshot system that mutates positions in-place.
// Returns a MutableSystem delegate for pipeline composition.
// ─────────────────────────────────────────────────────────────

/// Compute direction from input state
let private computeDirection (input: InputState) : Vector2 =
  let mutable dir = Vector2.Zero
  if input.MovingLeft then dir <- dir - Vector2.UnitX
  if input.MovingRight then dir <- dir + Vector2.UnitX
  if input.MovingUp then dir <- dir - Vector2.UnitY
  if input.MovingDown then dir <- dir + Vector2.UnitY
  dir

/// Apply movement to position
let private applyMovement (position: Vector2) (direction: Vector2) (speed: float32) (dt: float32) : Vector2 =
  if direction = Vector2.Zero then position
  else position + direction * speed * dt

/// MutableSystem: mutates positions, returns model unchanged
let update (dt: float32) (model: Model) : struct (Model * Cmd<'Msg> list) =
  let entityId = model.PlayerId
  match model.Inputs.TryGetValue(entityId), model.Positions.TryGetValue(entityId) with
  | (true, input), (true, position) ->
    let speed = model.Speeds |> Map.tryFind entityId |> Option.defaultValue 0f
    let direction = computeDirection input
    let newPosition = applyMovement position direction speed dt
    model.Positions[entityId] <- newPosition
  | _ -> ()
  struct (model, [])
