# Mibo

A Feedback looking sample of the Mibo micro-framework for F# and MonoGame.

I am looking to apply lessons learned from the [Kipo](https://github.com/AngelMunoz/Kipo) project while authoring an ARPG game in F#.
I would like however to follow a little bit more the F# path so others can join in and make their own games.

While I chase high perf in Kipo, I am aware that usual patterns like Elmish may not suite the best for Game performance, yet for simpler smaller games I think it is a really good fit.

Mibo aims to provide a pluggable-batteries included amenities for both 2D and 3D games.

Out of the box we provide a few ones already

- Asset Loading/Retrieving
- Camera
- 2D Graphics Rendering
- 3D Graphics Rendering
- Input
- Elmish Loop

Ideally using the Elmish architecture one is able to start writing games quite easy, this is demonstrated by [Xelmish](https://github.com/ChrisPritchard/Xelmish), I would like to extend it to 3D and larger games like I am doing with Kipo but in a more F# way.


The [Sample](./Sample) directory contains a project of how one can structure a game using Mibo.

while it has some of the elmish traits, you will notice that it diverges in the way it handles the world model.

In Kipo I learned that going for Domain modeled entities ends up causing performance and complexity issues when it comes to extensibility, so I would like to see if Mibo is able to promote a more component based world model.

This is not the final shape, but it certainly is a step in the vision of what I would like Mibo to be.

I would like some feedback specially if you are interested in using F# for more than just 2D games and would like to juice out MonoGame performance while staying in the F# ecosystem.


Current thoughts and Ideas follow after.

## Why Component-Based World vs Domain Models?

Traditional Elmish apps use **domain-shaped models**:

```fsharp
// Domain-shaped: Each entity owns its data
type Player = { Position: Vector2; Velocity: Vector2; Health: int }
type Enemy = { Position: Vector2; Velocity: Vector2; AIState: AIState }
type Model = { Player: Player; Enemies: Enemy list }
```

This works great for UIs and simple games, but **doesn't scale** for games with many entities:

| Problem | Why It Hurts |
|---------|--------------|
| **Nested updates** | `{ model with Player = { model.Player with Position = ... } }` - each level is an allocation |
| **Cross-entity operations** | "Find all entities near X" requires iterating multiple collections |
| **System reuse** | Movement logic duplicated for Player, Enemy, Projectile, etc. |
| **Growing complexity** | Adding a component to all entities means changing every type |

**Component-based world** solves these:

```fsharp
// Component-based: World owns components, entities are just IDs
[<Struct>]
type Model = {
  Positions: Dictionary<EntityId, Vector2>   // all positions
  Velocities: Dictionary<EntityId, Vector2>  // all velocities
  Health: Dictionary<EntityId, int>          // entities that have health
}
```

| Benefit | Why It Helps |
|---------|--------------|
| **Flat updates** | `positions[entityId] <- newPos` - O(1), no nesting |
| **Cross-entity queries** | Spatial queries just iterate the Positions dictionary |
| **System reuse** | One movement system for all entities with Position+Velocity |
| **Composition** | Add Health to any entity by inserting into the dictionary |

### When TO Use Domain-Shaped Models

Domain models are still the right choice when:

- **UI state**: Menus, dialogs, HUD - hierarchical, low frequency
- **Control flow**: Game modes, scene state, player intent
- **Configuration**: Level definitions, item databases, skill trees
- **Single-instance entities**: The player, the camera, the cursor

The key insight: **Use components for "many of the same thing", models for "one specific thing".**

## Storage Strategy

Choose the right collection based on update frequency and lifetime:

| Collection | When to Use |
|------------|-------------|
| **Immutable Map** | Static data set once at spawn (speed, color). Safe default; switch to Dictionary when it becomes slow. |
| **Mutable Dictionary** | Stable entity components that don't change every frame. Switch here when Map churn hurts. |
| **Mutable ResizeArray** | Burst, short-lived objects (particles, projectiles). Avoids allocation churn of Map updates. |

In this sample:
- `Positions`, `Inputs` → Dictionary (entity components, updated frequently)
- `Particles` → ResizeArray (short-lived effects, not entities)
- `Speeds`, `Colors`, `Sizes` → Map (static config, set once)

## Two-Plane Architecture

**Control Plane (Elmish):**
- Mode changes, events, async work
- Messages like `KeyDown`, `PlayerFired`, `DemoBoxBounced`
- Flows through the Elmish message queue

**Data Plane (Direct Mutation):**
- Per-frame position updates, particle aging
- Systems return computed values, parent applies mutations
- Bypasses message queue entirely

## Running the Sample

```powershell
cd src/Sample
dotnet run
```

- **Arrow keys**: Move the player
- **Space**: Fire particles
- **Box**: Bounces and spawns more particles

## File Structure

| File | Purpose |
|------|---------|
| `Domain.fs` | Shared types: `EntityId`, `InputState` |
| `Player.fs` | Player system: `World.tick`, `World.keyDown` (pure functions) |
| `Particles.fs` | Particle system: `World.emit`, `World.tick` |
| `Program.fs` | World model, orchestration, Elmish wiring |

## What's Next

See [ROADMAP.md](../../ROADMAP.md) for upcoming features:
- Frame pipeline and system scheduling (Phase 1)
- Write boundaries and end-of-frame flush (Phase 2)
- Event bus for high-frequency messaging (Phase 3)
