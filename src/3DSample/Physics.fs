module _3DSample.Physics

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Input
open _3DSample

// ─────────────────────────────────────────────────────────────
// Physics System: Gravity, jump, position updates, collisions
// ─────────────────────────────────────────────────────────────

/// Apply gravity to velocity
let private applyGravity (dt: float32) (velocity: Vector3) : Vector3 =
  velocity + Vector3(0f, Constants.gravity * dt, 0f)

/// Apply jump if grounded and jump action started
let private applyJump(state: State) : Vector3 =
  if state.IsGrounded && state.Actions.Started.Contains Jump then
    Vector3(state.Velocity.X, Constants.jumpSpeed, state.Velocity.Z)
  else
    state.Velocity

/// Get player radius from bounds
let private getPlayerRadius(assets: GameAssets) : float32 =
  (assets.PlayerBounds.Max.Y - assets.PlayerBounds.Min.Y) / 2f

/// Resolve collision with platforms
let private resolveCollision
  (playerRadius: float32)
  (prevPos: Vector3)
  (newPos: Vector3)
  (velocity: Vector3)
  (platforms: PlatformData list)
  : struct (Vector3 * Vector3 * bool) =
  match Platform.checkCollision playerRadius prevPos newPos platforms with
  | Some top when velocity.Y <= 0f ->
    // Land on platform: place ball on top, zero vertical velocity
    Vector3(newPos.X, top + playerRadius, newPos.Z),
    Vector3(velocity.X, 0f, velocity.Z),
    true
  | _ -> newPos, velocity, false

/// Physics system update: gravity, jump, position, collision
let update<'Msg>
  (dt: float32)
  (state: State)
  : struct (State * Cmd<'Msg> list) =
  let playerRadius = getPlayerRadius state.Assets

  // Apply jump first (modifies velocity before gravity)
  let velocity = applyJump state

  // Apply gravity
  let velocity = applyGravity dt velocity

  // Update position
  let newPos = state.PlayerPosition + velocity * dt

  // Resolve platform collision
  let struct (finalPos, finalVel, grounded) =
    resolveCollision
      playerRadius
      state.PlayerPosition
      newPos
      velocity
      state.Platforms

  {
    state with
        PlayerPosition = finalPos
        Velocity = finalVel
        IsGrounded = grounded
  },
  []
