# Design: Phase-Aware Component Scheduling

**Status:** Accepted
**Phase:** 1 (Frame pipeline + system scheduling)

## Implementation Status

| Component                                                          | Status       | Location                 |
| ------------------------------------------------------------------ | ------------ | ------------------------ |
| System pipeline (`start`/`pipeMutable`/`snapshot`/`pipe`/`finish`) | **Complete** | `src/Mibo/System.fs`     |
| Model/Snapshot pattern                                             | **Complete** | `Sample/Domain.fs`       |
| `Program.withConfig` callback                                      | **Complete** | `src/Mibo/Program.fs`    |
| `Input.create` object expression factory                           | **Complete** | `src/Mibo/Input.fs`      |
| `InputPolling` module-level functions                              | **Complete** | `src/Mibo/Input.fs`      |
| `SetViewport`/`ClearTarget` commands                               | **Complete** | `src/Mibo/Graphics3D.fs` |
| `Assets.fromJson`/`fromJsonCache`                                  | **Complete** | `src/Mibo/Assets.fs`     |
| `Assets.fromCustom`/`fromCustomCache`                              | **Complete** | `src/Mibo/Assets.fs`     |

**Note:** Phase enum and `Program.withSystem(phase, factory)` were intentionally not implemented - ordering is user-controlled via System pipeline in update.

## 1. Problem Statement

Mibo currently relies on an implicit execution order where `Tick` logic runs one frame behind input logic. To scale to larger games, we need explicit, deterministic frame phases (e.g., Input always runs before Simulation). However, we must avoid creating a "second scheduler" that competes with the underlying MonoGame engine.

## 2. The Solution: Managed Component Registration

We will utilize MonoGame's native `GameComponent` system and its `UpdateOrder` property as the single source of truth for scheduling. Mibo will enforce semantic ordering through its public API, hiding raw integer values from the user.

### 2.1 Core Concepts

1.  **Everything is a Component:** Custom interfaces like `IEngineService` and `IRenderer` will be deprecated/refactored. All internal and user systems will be standard `IGameComponent` or `DrawableGameComponent` instances.
2.  **Semantic Phases:** We define a set of named phases (buckets) that map to specific `UpdateOrder` ranges.
3.  **The Elmish Loop is a Component:** The core message processing loop will be refactored into an internal `GameComponent` with `UpdateOrder = 0`.

### 2.2 Execution Phases

We define a set of semantic buckets to represent the intent:

- **Input:** Polling hardware inputs. Maps to a high negative `UpdateOrder` (e.g., `-1000`).
- **PreUpdate:** Logic that runs before the main Elmish loop (e.g., `-100`).
- **Update (Internal):** The Elmish message loop and `update` calls. Fixed at `0`.
- **PostUpdate:** Logic that runs after the main Elmish loop (e.g., `+100`).
- **Flush:** Final cleanup or buffer flushes (e.g., `+1000`).

### 2.3 API Changes

The `Program` type will be updated to store system factories alongside their intended phase.

**Proposed API:**

- `Program.withSystem(phase, factory)`: Registers a system to run during a specific phase.
- `Program.withInput(factory)`: Convenience for the Input phase.

### 2.4 Internal Implementation (`ElmishGame`)

When `ElmishGame` initializes:

1.  It iterates through the registered systems.
2.  It creates the components.
3.  It assigns the `UpdateOrder` based on the requested phase.
4.  It adds them to the standard MonoGame `Components` collection.

MonoGame then handles the rest: sorting and executing them in the correct order every frame.

## 3. Benefits

1.  **Native Interop:** Any standard MonoGame library works out of the box.
2.  **Guarantees:** By placing Input at `-1000` and the Loop at `0`, we strictly guarantee that the simulation sees fresh input data in the same frame.
3.  **Safety:** Users are guided to pick a semantic "bucket" rather than guessing integer values.

---

## 4. System Pipeline Pattern (Phase 2 Clarification)

The roadmap mentions "separate systems" and "write boundaries" but this does **not** mean systems are decoupled via an EventBus. In Elmish, **the Msg type IS the event bus**.

### 4.1 How Inter-System Communication Works in Elmish

```
┌─────────────────────────────────────────────────────────────┐
│                      update function                        │
├─────────────────────────────────────────────────────────────┤
│  match msg with                                             │
│  | Tick gt ->        // Frame update - use pipeline         │
│  | KeyDown key ->    // Input event - direct mutation       │
│  | PlayerFired _ ->  // Generated event - handled here      │
│  | DemoBoxBounced -> // External event - handled here       │
└─────────────────────────────────────────────────────────────┘
```

The centralized `update` function is the single place where all inter-system communication happens. Systems don't need to "subscribe" to each other — they all communicate via the Msg type.

### 4.2 Comparison: EventBus vs Elmish Msgs

| Aspect             | EventBus (Kipo-style)                     | Elmish Msgs (Mibo)                 |
| ------------------ | ----------------------------------------- | ---------------------------------- |
| Decoupling         | Runtime (systems don't import each other) | Compile-time (all in one `update`) |
| Discoverability    | Harder (trace Observable subscriptions)   | Easy (all in one match)            |
| Type Safety        | Weaker (any subscriber can react)         | Strong (exhaustive match)          |
| Multiple consumers | Natural (all subscribers)                 | Must call each explicitly          |
| Testability        | Needs mocking                             | Pure functions, easy to test       |

For most games, the centralized `update` is preferable — you see all logic in one place.

### 4.3 The System Pipeline (Frame Updates)

For frame-continuous updates (physics, particles, animation), we use a composable pipeline with type-enforced snapshot boundary:

```fsharp
| Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    System.start model
    // Phase 1: Mutable systems (can mutate positions, particles)
    |> System.pipeMutable (Physics.update dt)
    |> System.pipeMutable (Particles.update dt)
    // SNAPSHOT: transition to readonly
    |> System.snapshot Model.toSnapshot
    // Phase 2: Readonly systems (work with immutable snapshot)
    |> System.pipe (HueColor.update dt 5.f)
    |> System.pipe (Player.processActions onFired)
    // Finish: convert back to Model
    |> System.finish Model.fromSnapshot
```

The type difference between `Model` and `ModelSnapshot` enforces the boundary:

- You can't call `pipe` before `snapshot` (types don't match)
- You can't call `pipeMutable` after `snapshot` (types don't match)

### 4.4 Message Categories

| Category  | When Runs        | Pattern                            |
| --------- | ---------------- | ---------------------------------- |
| **Frame** | Every tick       | Use the system pipeline            |
| **Input** | On user event    | Direct mutation, no pipeline       |
| **Event** | System-generated | Dispatch via Cmd, handle in update |

Frame messages use the full pipeline. Input and event messages are discrete state changes that don't need it.

### 4.5 When to Consider an EventBus

An EventBus becomes useful when:

- Many **unrelated** systems need to react to the same event
- You want runtime decoupling for plugin architectures
- Cross-cutting concerns (analytics, logging) need to observe without coupling

For most gameplay, Elmish Msgs are sufficient and preferable.
