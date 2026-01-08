namespace _3DSample

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Core Types
// ─────────────────────────────────────────────────────────────

/// Semantic game actions
[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveForward
  | MoveBackward
  | Jump

/// Platform with position and computed bounds
[<Struct>]
type PlatformData = {
  Position: Vector3
  Bounds: BoundingBox
}

/// Assets loaded at init, available for update and view
[<Struct>]
type GameAssets = {
  PlayerModel: Model
  PlayerBounds: BoundingBox
  PlatformModel: Model
  PlatformBounds: BoundingBox
}

/// Game state - fully immutable, idiomatic F#
[<Struct>]
type State = {
  PlayerPosition: Vector3
  Velocity: Vector3
  Rotation: Quaternion
  IsGrounded: bool
  Actions: ActionState<GameAction>
  InputMap: InputMap<GameAction>
  Assets: GameAssets
  Platforms: PlatformData list
}

// ─────────────────────────────────────────────────────────────
// Physics Constants
// ─────────────────────────────────────────────────────────────

module Constants =
  let gravity = -20.0f
  let jumpSpeed = 15.0f
  let moveSpeed = 5.0f
  let acceleration = 25.0f
  let friction = 8.0f
  let fallLimit = -20.0f
  let rollSpeed = 2.0f
