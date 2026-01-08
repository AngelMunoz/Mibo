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

[<Struct>]
type ParticleSpawn = { Position: Vector2; Count: int }

module ParticleFactory =
  let private rng = Random.Shared

  let createAt(pos: Vector2) : Particle =
    let angle = rng.NextDouble() * Math.PI * 2.0
    let speed = rng.NextDouble() * 100.0 + 50.0

    let velocity =
      Vector2(float32(Math.Cos angle), float32(Math.Sin angle)) * float32 speed

    {
      Position = pos
      Velocity = velocity
      Life = 1.0f
      MaxLife = 1.0f
      Color = Color.Yellow
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
  Crates: ResizeArray<Guid<EntityId>>
  Speeds: Map<Guid<EntityId>, float32>
  Hues: Map<Guid<EntityId>, float32>
  TargetHues: Map<Guid<EntityId>, float32>
  Sizes: Map<Guid<EntityId>, Vector2>
  PlayerId: Guid<EntityId>
  BoxBounces: int
  CrateHits: int
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
  Crates: IReadOnlyList<Guid<EntityId>>
  Speeds: Map<Guid<EntityId>, float32>
  Hues: Map<Guid<EntityId>, float32>
  TargetHues: Map<Guid<EntityId>, float32>
  Sizes: Map<Guid<EntityId>, Vector2>
  PlayerId: Guid<EntityId>
  BoxBounces: int
  CrateHits: int
}

module Model =
  /// Create readonly snapshot after physics mutations
  let toSnapshot(model: Model) : ModelSnapshot = {
    Positions = model.Positions :> IReadOnlyDictionary<_, _>
    Actions = model.Actions
    InputMap = model.InputMap
    Particles = model.Particles :> IReadOnlyList<_>
    Crates = model.Crates :> IReadOnlyList<_>
    Speeds = model.Speeds
    Hues = model.Hues
    TargetHues = model.TargetHues
    Sizes = model.Sizes
    PlayerId = model.PlayerId
    BoxBounces = model.BoxBounces
    CrateHits = model.CrateHits
  }

  let fromSnapshot(snapshot: ModelSnapshot) : Model = {
    Positions = snapshot.Positions :?> Dictionary<_, _>
    Actions = snapshot.Actions
    InputMap = snapshot.InputMap
    Particles = snapshot.Particles :?> ResizeArray<_>
    Crates = snapshot.Crates :?> ResizeArray<_>
    Speeds = snapshot.Speeds
    Hues = snapshot.Hues
    TargetHues = snapshot.TargetHues
    Sizes = snapshot.Sizes
    PlayerId = snapshot.PlayerId
    BoxBounces = snapshot.BoxBounces
    CrateHits = snapshot.CrateHits
  }
