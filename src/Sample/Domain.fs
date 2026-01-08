module MiboSample.Domain

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Core Types
// ─────────────────────────────────────────────────────────────

/// Entity identifier measure type
[<Measure>]
type EntityId

/// Semantic Actions
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown
  | Fire
[<Struct>]
type Particle = {
  Position: Vector2
  Velocity: Vector2
  Life: float32
  MaxLife: float32
  Color: Color
}


// ─────────────────────────────────────────────────────────────
// Model: The World State (mutable containers for hot data)
// ─────────────────────────────────────────────────────────────

type Model = {
  Positions: Dictionary<Guid<EntityId>, Vector2>
  // Replaced manual input state with generic ActionState
  Actions: ActionState<GameAction>
  InputMap: InputMap<GameAction>
  Particles: ResizeArray<Particle>
  Speeds: Map<Guid<EntityId>, float32>
  Hues: Map<Guid<EntityId>, float32>
  TargetHues: Map<Guid<EntityId>, float32>
  Sizes: Map<Guid<EntityId>, Vector2>
  PlayerId: Guid<EntityId>
  BoxBounces: int
}

// ─────────────────────────────────────────────────────────────
// ModelSnapshot: Readonly view for post-physics systems
// ─────────────────────────────────────────────────────────────

[<Struct>]
type ModelSnapshot = {
  Positions: IReadOnlyDictionary<Guid<EntityId>, Vector2>
  Actions: ActionState<GameAction>
  InputMap: InputMap<GameAction>
  Particles: IReadOnlyList<Particle>
  Speeds: Map<Guid<EntityId>, float32>
  Hues: Map<Guid<EntityId>, float32>
  TargetHues: Map<Guid<EntityId>, float32>
  Sizes: Map<Guid<EntityId>, Vector2>
  PlayerId: Guid<EntityId>
  BoxBounces: int
}

module Model =
  /// Create readonly snapshot after physics mutations
  let toSnapshot (model: Model) : ModelSnapshot = {
    Positions = model.Positions :> IReadOnlyDictionary<_, _>
    Actions = model.Actions
    InputMap = model.InputMap
    Particles = model.Particles :> IReadOnlyList<_>
    Speeds = model.Speeds
    Hues = model.Hues
    TargetHues = model.TargetHues
    Sizes = model.Sizes
    PlayerId = model.PlayerId
    BoxBounces = model.BoxBounces
  }

  let fromSnapshot (snapshot: ModelSnapshot) : Model = {
    Positions = snapshot.Positions :?> Dictionary<_, _>
    Actions = snapshot.Actions
    InputMap = snapshot.InputMap
    Particles = snapshot.Particles :?> ResizeArray<_>
    Speeds = snapshot.Speeds
    Hues = snapshot.Hues
    TargetHues = snapshot.TargetHues
    Sizes = snapshot.Sizes
    PlayerId = snapshot.PlayerId
    BoxBounces = snapshot.BoxBounces
  }
