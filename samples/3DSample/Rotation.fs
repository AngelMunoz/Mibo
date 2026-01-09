module _3DSample.Rotation

open Microsoft.Xna.Framework
open Mibo.Elmish
open _3DSample

// ─────────────────────────────────────────────────────────────
// Rotation System: Ball rolling based on velocity
// ─────────────────────────────────────────────────────────────

/// Compute rotation delta from velocity (rolling ball effect)
let private computeRollDelta (dt: float32) (velocity: Vector3) : Quaternion =
  // Roll around X axis for Z movement (positive Z velocity = forward roll)
  // Roll around Z axis for X movement (positive X velocity = roll right)
  let rollX = velocity.Z * dt * Constants.rollSpeed
  let rollZ = -velocity.X * dt * Constants.rollSpeed
  Quaternion.CreateFromYawPitchRoll(0f, rollX, rollZ)

/// Rotation system update: applies rolling rotation based on velocity
/// Ball keeps rotating even when airborne for momentum effect
let update<'Msg> (dt: float32) (state: State) : struct (State * Cmd<'Msg>) =
  let rotationDelta = computeRollDelta dt state.Velocity
  let newRotation = Quaternion.Concatenate(state.Rotation, rotationDelta)

  { state with Rotation = newRotation }, Cmd.none
