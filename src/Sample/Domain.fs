module MiboSample.Domain

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX

// ─────────────────────────────────────────────────────────────
// Core Types
// ─────────────────────────────────────────────────────────────

/// Entity identifier measure type
[<Measure>]
type EntityId

/// Input state for controllable entities
[<Struct>]
type InputState = {
  MovingLeft: bool
  MovingRight: bool
  MovingUp: bool
  MovingDown: bool
  IsFiring: bool
}
[<Struct>]
type Particle = {
  Position: Vector2
  Velocity: Vector2
  Life: float32
  MaxLife: float32
  Color: Color
}

module InputState =
  let empty = {
    MovingLeft = false
    MovingRight = false
    MovingUp = false
    MovingDown = false
    IsFiring = false
  }

// ─────────────────────────────────────────────────────────────
// Model: The World State (mutable containers for hot data)
// ─────────────────────────────────────────────────────────────

type Model = {
  Positions: Dictionary<Guid<EntityId>, Vector2>
  Inputs: Dictionary<Guid<EntityId>, InputState>
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
  Inputs: IReadOnlyDictionary<Guid<EntityId>, InputState>
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
    Inputs = model.Inputs :> IReadOnlyDictionary<_, _>
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
    Inputs = snapshot.Inputs :?> Dictionary<_, _>
    Particles = snapshot.Particles :?> ResizeArray<_>
    Speeds = snapshot.Speeds
    Hues = snapshot.Hues
    TargetHues = snapshot.TargetHues
    Sizes = snapshot.Sizes
    PlayerId = snapshot.PlayerId
    BoxBounces = snapshot.BoxBounces
  }
