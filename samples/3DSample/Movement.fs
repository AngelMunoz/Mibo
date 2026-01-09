module _3DSample.Movement

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Input
open _3DSample

// ─────────────────────────────────────────────────────────────
// Movement System: Input → velocity with acceleration/friction
// ─────────────────────────────────────────────────────────────

/// Compute movement direction from action state
let computeDirection(actions: ActionState<GameAction>) : Vector3 =
  let mutable dir = Vector3.Zero

  if actions.Held.Contains MoveLeft then
    dir <- dir - Vector3.UnitX

  if actions.Held.Contains MoveRight then
    dir <- dir + Vector3.UnitX

  if actions.Held.Contains MoveForward then
    dir <- dir - Vector3.UnitZ

  if actions.Held.Contains MoveBackward then
    dir <- dir + Vector3.UnitZ

  if dir.LengthSquared() > 0f then
    Vector3.Normalize(dir)
  else
    dir

/// Apply acceleration towards target velocity or friction when no input
let private applyAccelerationOrFriction
  (dt: float32)
  (moveDir: Vector3)
  (currentVel: Vector3)
  : Vector3 =
  let horizontalVel = Vector2(currentVel.X, currentVel.Z)
  let hasInput = moveDir.LengthSquared() > 0f

  let newHorizontalVel =
    if hasInput then
      // Accelerate towards target velocity
      let targetVel =
        Vector2(
          moveDir.X * Constants.moveSpeed,
          moveDir.Z * Constants.moveSpeed
        )

      let diff = targetVel - horizontalVel
      let accel = Constants.acceleration * dt

      if diff.Length() <= accel then
        targetVel
      else
        horizontalVel + Vector2.Normalize(diff) * accel
    else
      // Apply friction to slow down
      let frictionAmount = Constants.friction * dt
      let speed = horizontalVel.Length()

      if speed <= frictionAmount then
        Vector2.Zero
      else
        horizontalVel * ((speed - frictionAmount) / speed)

  Vector3(newHorizontalVel.X, currentVel.Y, newHorizontalVel.Y)

/// Movement system update: processes input and applies acceleration/friction
let update<'Msg> (dt: float32) (state: State) : struct (State * Cmd<'Msg>) =
  let moveDir = computeDirection state.Actions
  let newVelocity = applyAccelerationOrFriction dt moveDir state.Velocity

  { state with Velocity = newVelocity }, Cmd.none
