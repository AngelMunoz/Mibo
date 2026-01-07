# Mibo Roadmap (Small → Large Games)

This roadmap describes what Mibo needs to grow from a lightweight "Elmish game loop + helpers" library into a robust foundation for **large games** (RPG, platformer, FPS, etc.) while keeping **small-game ergonomics** excellent.

The core constraint: **MonoGame remains the underlying engine**, but Mibo should not require any single gameplay architecture. Mibo should provide primitives that enable multiple architectures (Elmish-style, data-oriented ECS-like, event-driven, etc.) and allow projects to scale without rewriting the framework.

This is a **pre-1.0** roadmap. We're building from the ground up and are free to move fast, including making breaking changes while the design settles.

Most importantly: **Elmish is the primary public API**.

- A game should scale from tiny to huge while still feeling like: `init`, `update`, `subscribe`, `view`.
- Alternative approaches (ECS, event buses, command buffers, adaptive state, etc.) should be usable as **implementation details** behind the Elmish surface (usually via services in `GameContext` and `Program.with…` combinators), not as the public mental model.

---

## Design goals

### 1) Growth without rewrites

A prototype should be able to ship and gradually evolve into a large codebase by adding capabilities (systems, state roots, buses, caches), not by swapping paradigms.

### 2) Explicit frame phases, deterministic behavior

Large games need predictable ordering: input → simulation → flush → render → UI, etc. Mibo should make this explicit, configurable, and testable.

When Mibo introduces phase concepts or ordering, they should be designed to **align with MonoGame's existing component model** (Update/Draw + UpdateOrder/DrawOrder), not compete with it.

### 3) Performance-friendly by default, but not hostile

Provide building blocks that make low-allocation patterns straightforward (pooled buffers, struct commands, diffing), while keeping the "hello world" API small.

### 4) Library-first API surface

Most core behavior should live in modules and data types. Classes should be thin integration points for MonoGame.

### 5) Pluggable subsystems

Input, assets, scenes, time-step, diagnostics, eventing, and simulation state should all be pluggable, so different game genres can mix-and-match.

### 6) Elmish-first ergonomics at every scale

Mibo should not force a "two-tier API" where Elmish is only for prototypes.

Instead, Mibo should keep a single guiding workflow (Elmish) and let advanced runtime features sit behind it:

- `Cmd` is for orchestration and async work.
- `Sub` is for subscriptions.
- High-frequency simulation mechanisms (writes/events/caches) run behind the scenes and are driven by the game loop.

---

## Elmish internals: making an Elmish API fast enough for games

An Elmish-style API is attractive because it makes gameplay logic easy to structure and compose. The risk is that a naive "everything is a message" approach can become allocation-heavy and difficult to optimize.

The goal is to keep the **public authoring experience** Elmish (`init/update/subscribe/view`) while ensuring the **runtime implementation** supports high throughput.

### What Elmish should do in a game framework

Elmish works best as the **control plane**:

- reacting to player intent, UI actions, scene transitions
- starting/stopping services
- orchestrating async operations (loading, matchmaking, IO)
- coordinating systems at a coarse level

### What Elmish should not be forced to do

Elmish should not be the mechanism for per-entity, per-frame hot loops. If the only way to move 10,000 entities is "dispatch 10,000 messages", the architecture will fight performance.

So the framework should make it natural to keep hot loops out of the message queue.

### Runtime strategy (internal) to preserve Elmish while scaling

The runtime should enforce a separation between:

- **messages** (low/medium frequency, semantically meaningful)
- **data iteration** (high frequency, bulk operations)

Concretely, Mibo should evolve its Elmish runtime along these lines:

1. **Frame phases around the Elmish step**

The core loop remains: dequeue messages → `update` → execute `Cmd` → refresh `Sub`.

But we add explicit phases so services can run hot loops in the right place:

- poll input (service)
- run simulation systems (service)
- flush buffered writes (service)
- draw (renderer)

Elmish `update` remains the coordinator; the hot work lives in services.

2. **Buffered writes instead of message storms**

Provide a standard "write buffer" pattern (pooled, end-of-frame flush). Systems enqueue write commands; a dedicated flush phase applies them.

Elmish modules can still _initiate_ work (e.g., enable a system, change a mode), but state churn is handled as bulk writes.

3. **Event aggregation and batching**

When high-frequency events are necessary (collisions, hits, AI perceptions), they should be published to an internal event stream and either:

- consumed directly by services, or
- aggregated into a small number of Elmish messages (e.g., `Msg.FrameEvents of FrameEvent[]`) once per frame.

This keeps `update` readable while bounding message overhead.

4. **Allocation discipline in the runtime**

Mibo's internals should make the common path allocation-light:

- prefer struct DUs for internal command/event representations
- pool frequently resized buffers (ArrayPool-backed)
- avoid per-frame intermediate collections when possible
- keep subscription diffing allocation-friendly (already a strength)

5. **Bounded work per frame**

The runtime should support safeguards:

- optional per-frame limits (max messages processed, max events flushed)
- predictable behavior under load (e.g., drop/merge policies for non-critical telemetry)

This is about preventing spirals where a single hitch creates a backlog that never recovers.

### The unclear part: "Elmish but fast" requires message semantics

To make Elmish scale, Mibo needs a strong stance on what a message _means_.

If a message is allowed to mean "a single entity moved 1cm," then high entity counts imply high message rates, and the runtime will drown.

Instead, treat messages as:

- **intent / decisions / mode transitions** (e.g., "player started firing", "entered targeting mode", "load scene X")
- **frame-level aggregates** (e.g., "these 120 hits occurred this frame", "input snapshot for this frame")

And explicitly avoid using messages for:

- per-entity deltas in the hot path
- direct mirrors of low-level input polling

This is not about restricting users; it's about making the ergonomic path also the fast path.

### Concrete internal mechanisms to enforce the semantics

Mibo should add runtime facilities that make the above semantics natural:

1. **Coalescing / latest-value channels**

Some information is "latest wins" (mouse position, camera target, analog stick). The runtime should support channels that keep only the latest value per frame and emit at most one message.

2. **Frame event aggregation**

If a subsystem produces many events, make it easy to aggregate them into a single per-frame batch (possibly pooled) and dispatch one Elmish message.

3. **Structured backpressure**

Provide optional policies when the system is overloaded:

- cap the number of messages processed per frame (or time budget)
- cap event flush size
- choose per-stream policies: drop, coalesce, defer

These policies should be explicit and observable.

4. **Two-plane execution model (without a second paradigm)**

Internally, treat the runtime as two coupled planes:

- Control plane: Elmish `update` and subscriptions
- Data plane: services/systems that iterate bulk data and buffer writes/events

The public model stays Elmish; this is purely an implementation detail for performance reasoning.

### Debuggability implications

If Mibo adds buffering, coalescing, and backpressure, it must add minimal visibility so debugging doesn't become guesswork:

- per-frame counters (messages dequeued, messages dropped/coalesced)
- event bus counts (published, flushed, dropped)
- buffer sizes (write buffers, render buffers)
- optional tracing of "top talkers" (which source produced the most)

This belongs in the runtime, not only in user code.

### Authoring guidance (how to stay Elmish and fast)

Rule of thumb:

- if something happens many times per frame, express it as bulk data iteration + buffered writes
- if something changes a mode or kicks off async work, express it as an Elmish message

### Public API principle

All of the above should be reachable through the Elmish surface:

- `Program.with…` combinators configure phases, services, and flush points.
- `GameContext` exposes the relevant services (input, assets, event stream, write buffers, diagnostics).

Users should not have to "switch paradigms" to stay performant; they should mostly decide _where work runs_ (service vs update) and _how it is buffered_.

### Roadmap implications

The phases below are still valid, but the implementation work should explicitly prioritize:

- phase-aware execution in the runtime
- pooled buffers and bulk operations
- event aggregation patterns

This is the key to keeping the API Elmish without letting Elmish become a performance bottleneck.

---

## Where Mibo is strong already (baseline)

These are strengths to preserve while adding scalability features:

- Elmish-style `Program` and runtime loop (`Init`, `Update`, `Subscribe`, `Cmd`, `Sub` mapping).
- Subscription diffing with stable identifiers (`SubId`).
- Per-game services and components (`Services`, `Components`) with MonoGame lifecycle.
- Command-buffer style rendering (2D/3D batch renderers) and a generic `RenderBuffer<'Key,'Cmd>`.
- Asset caching service (`IAssets`) and delta-style input service.

The roadmap below extends these while keeping "small game" workflows ergonomic.

---

## Roadmap overview

Mibo should evolve in **layers**:

1. **Runtime layer (frame pipeline)**: deterministic phases, system ordering, scheduling.
2. **State layer (state roots + write boundaries)**: scalable shared state without forcing one architecture.
3. **Messaging layer (event bus)**: high-frequency communication without turning everything into Elmish messages.
4. **Content layer (data-driven stores)**: configuration/stores/load queues.
5. **Rendering layer (orchestration)**: multi-pass, multi-camera, resource caches.
6. **Tooling layer (testing, diagnostics, profiling)**: confidence as the project scales.

Each layer should be optional and additive, and should be reachable through the Elmish `Program` surface (typically as `Program.withX` combinators and services available from `GameContext`).

---

## Phase 0 - Clarify contracts (groundwork)

### Deliverables

- Document the execution model:
  - what runs on the main thread
  - how services/components interact
  - when `Cmd` effects execute
  - subscription diffing rules and `SubId` guidance
- Define current build/runtime constraints:
  - current target framework(s)
  - current MonoGame baseline
  - threading model (what may be parallelized, what must remain on main thread)

### Current status

Documentation exists in `docs/`:

- `EXECUTION_MODEL.md` - frame lifecycle, execution thread, input latency, build constraints
- `DESIGN_PHASE_SYSTEM.md` - phase scheduling design, system pipeline pattern

### Success criteria

- A developer can answer: "where does my work run and in what order?" without reading the runtime code.

---

## Phase 1 - Frame pipeline + system scheduling (scale enabler #1)

Large games need explicit phases and ordering; small games need no ceremony.

In Mibo terms, this should still look like one Elmish program. The pipeline is an internal execution plan that the program configures, not a separate programming model.

Important: phases should not be a second scheduler. The intent is:

- phases are a clear semantic description of order
- the implementation maps them onto MonoGame ordering (`UpdateOrder`/`DrawOrder`) so there's one source of truth at runtime

### Deliverables

#### 1.1 Game configuration callback

Allow users to configure MonoGame settings without prescribing a specific config type:

```fsharp
Program.withConfig (fun game graphics ->
    game.IsMouseVisible <- true
    game.Window.AllowUserResizing <- true
    graphics.PreferredBackBufferWidth <- 1280
    graphics.PreferredBackBufferHeight <- 720
    graphics.IsFullScreen <- false
    game.IsFixedTimeStep <- true
    game.TargetElapsedTime <- TimeSpan.FromSeconds(1.0 / 60.0))
```

This callback runs in the `ElmishGame` constructor after `GraphicsDeviceManager` is created.

#### 1.2 Migrate to MonoGame interfaces

Deprecate custom `IEngineService` in favor of MonoGame's `IUpdateable`/`GameComponent`. Per `docs/DESIGN_PHASE_SYSTEM.md`:

- Everything becomes a `GameComponent` or `DrawableGameComponent`
- The Elmish loop itself becomes an internal component with `UpdateOrder = 0`
- Semantic phases map to `UpdateOrder` ranges (Input = -1000, PreUpdate = -100, etc.)

#### 1.3 Phase-aware registration

```fsharp
Program.withSystem Phase.Input (fun game -> InputPollingComponent(game))
Program.withSystem Phase.PostUpdate (fun game -> FlushComponent(game))
```

#### 1.4 Time-step options

Exposed via `withConfig` callback - user sets `game.IsFixedTimeStep` and `game.TargetElapsedTime` directly.

### Success criteria

- A project can add 30+ systems and still reason about ordering.
- A simple Mibo sample remains simple (no forced pipeline setup).
- Users can configure any MonoGame game setting via `withConfig`.

---

## Phase 2 — State roots + write boundaries (scale enabler #2)

Mibo should support shared state across many systems without forcing a specific "ECS" implementation.

The key requirement is: systems can share state safely and efficiently, while the _public API remains Elmish_.

> **Important clarification:** "Systems" in Mibo are **not** decoupled runtime entities that communicate via an EventBus. Instead, they are composable **functions** that run within the Elmish `update` handler. **The Msg type IS the event bus** — inter-system communication happens through message dispatch and pattern matching in `update`.

### Deliverables

#### 2.1 State roots and state containers

Avoid baking a single "World" concept into the framework. Games may have:

- one simulation state root
- multiple state roots (split-screen, server/client, multiple boards/levels)
- short-lived state roots (minigames, menus, editor tools)

Instead, provide a state container pattern that is:

- owned by a scope (game-scoped or scene-scoped)
- passed to systems explicitly
- implemented by the game (Mibo provides helpers, not a required shape)

Implementation choices can include:

- plain immutable records with pure transforms
- mutable dictionaries/arrays
- adaptive state (optional package)

The key is a stable boundary for "what systems read" and "what gets flushed."

#### 2.2 System pipeline pattern (frame-continuous updates)

For frame-continuous updates (physics, particles, animation), provide a composable pipeline with type-enforced snapshot boundary:

```fsharp
| Tick gt ->
    System.start model
    |> System.pipeMutable (Physics.update dt)   // Can mutate Model
    |> System.snapshot Model.toSnapshot          // Lock → readonly
    |> System.pipeReadonly (HueColor.update dt)  // Works with ModelSnapshot
    |> System.finish Model.fromSnapshot
```

The type difference between `Model` and `ModelSnapshot` enforces the boundary at compile time.

**Message categories:**

- **Frame messages (Tick):** Use the system pipeline for continuous updates
- **Input messages (KeyDown, MouseClick):** Direct state mutation, no pipeline needed
- **Event messages (PlayerFired, ItemPickedUp):** Dispatch via Cmd, handle in `update`

See `docs/DESIGN_PHASE_SYSTEM.md` for detailed comparison between EventBus and Elmish Msgs.

### Success criteria

- A game can process thousands of entities without turning every per-entity change into an Elmish message.
- State mutation timing is predictable and easy to test.
- Inter-system communication is visible in the centralized `update` function.

---

## Phase 3 — Asset loading helpers (scale enabler #3)

Mibo provides ergonomic asset loading helpers. Store patterns (composition roots, DI containers) are userland concerns — we just help users fill their stores.

### Deliverables

#### 3.1 JSON loading with JDeck decoders

```fsharp
Assets.fromJson "Content/skills.json" SkillConfig.decoder ctx
Assets.fromJsonCache "Content/skills.json" SkillConfig.decoder ctx  // cached for game lifetime
```

#### 3.2 Custom asset loaders

```fsharp
Assets.fromCustom "Content/levels.bin" LevelLoader.load ctx
Assets.fromCustomCache "Content/levels.bin" LevelLoader.load ctx  // cached
```

### Success criteria

- Users can load JSON and custom formats with minimal boilerplate.
- Framework does not prescribe store patterns or DI approaches.

---

## Phase 4 — Rendering orchestration (scale enabler #4)

Mibo's batch renderers (`Batch2DRenderer`, `Batch3DRenderer`) handle the common cases well. This phase adds infrastructure for more advanced rendering scenarios.

### Clarification: what orchestration means in Mibo

**User decides what to render and when.** Mibo provides the building blocks.

Multi-camera rendering is a user concern - you decide how many cameras exist, what each renders, and in what order. The framework gap is **viewport and clear commands**, not "framework iterates cameras for you."

### Deliverables

#### 4.1 Viewport and clear commands (multi-camera support)

Add commands so users can control viewport and per-camera clears:

```fsharp
type RenderCmd3D =
    | SetViewport of viewport: Viewport
    | ClearTarget of color: Color voption * clearDepth: bool
    | SetCamera of camera: Camera
    // ... existing commands
```

User code then looks like:

```fsharp
for camera in myCameras do
    buffer |> Draw3D.viewport camera.Viewport
    buffer |> Draw3D.clear (ValueSome Color.Black) true
    buffer |> Draw3D.camera camera
    // ... draw calls for this camera
```

This keeps users in control while enabling split-screen, minimaps, and editor viewports.

#### 4.2 Billboard renderer (3D particle infrastructure)

For particle effects, floating text, and health bars in 3D space - quads that always face the camera.

```fsharp
type BillboardCmd =
    | SetCamera of camera: Camera
    | DrawBillboard of texture: Texture2D * position: Vector3 * size: float32 * color: Color
```

This is common enough to warrant a dedicated renderer with batching for performance.

#### 4.3 Quad renderer (terrain/decal infrastructure)

For textured quads in 3D space - terrain tiles, decals, water surfaces.

```fsharp
type QuadCmd =
    | SetCamera of camera: Camera
    | DrawQuad of texture: Texture2D * position: Vector3 * size: Vector2 * rotation: Quaternion * color: Color
```

These are **optional additions** - games that don't need 3D particles or decals don't pay for them.

### Success criteria

- Users can implement multi-camera rendering (split-screen, minimaps) without workarounds.
- 3D particle systems and terrain rendering have ergonomic, performant primitives.

## Elmish is the API: scaling without a second paradigm

The goal is not "small mode vs large mode". The goal is **one Elmish API** that can opt into stronger runtime capabilities.

### The stable shape

- `Program.mkProgram init update`
- `Program.withSubscription`
- `Program.withTick`
- `Program.withInput`, `Program.withAssets`, `Program.withRenderer`

### The scalable capabilities (still Elmish)

These should be expressed as `Program.with…` combinators and services, so a codebase can adopt them incrementally without changing how gameplay is authored:

- frame pipeline phases and ordered execution
- shared simulation state and write boundaries (end-of-frame flush)
- high-frequency event stream (bus) for simulation internals
- scenes and scoped lifetimes
- content stores and safe load queues
- render orchestration

Design rule:

> Advanced capabilities must not force a new programming model; they should feel like additional knobs on the Elmish runtime.

---

## Migration path: small → large

Mibo should provide a staged migration story:

1. Start with Elmish-only.
2. Add explicit phases and ordered systems.
3. Introduce a state root + state-write service for shared state.
4. Move high-frequency events from Elmish messages to EventBus.
5. Add scenes and content stores.
6. Add render orchestration.

This migration should mostly be "opt into runtime services" rather than "rewrite gameplay modules."

---

## Non-goals (to keep scope sane)

- Mibo should not mandate a full ECS.
- Mibo should not dictate a specific AI/physics/combat model.
- Mibo should not require adaptive/reactive state, but can make it easy to plug in.
- Mibo should not hide MonoGame; it should provide safe, ergonomic ways to integrate with it.

---

## Definition of "Mibo can host a large game"

Mibo is "large-game ready" when a project can:

- manage many systems with deterministic order
- process large shared state with controlled mutation points
- handle high-frequency events without message storms
- load content/configuration scalably and safely
- run multi-pass rendering and multiple cameras
- test simulation logic headlessly
- diagnose performance and memory behavior

without requiring a rewrite of the application's architecture.
