# Mibo Roadmap (Small → Large Games)

This roadmap describes what Mibo needs to grow from a lightweight “Elmish game loop + helpers” library into a robust foundation for **large games** (RPG, platformer, FPS, etc.) while keeping **small-game ergonomics** excellent.

The core constraint: **MonoGame remains the underlying engine**, but Mibo should not require any single gameplay architecture. Mibo should provide primitives that enable multiple architectures (Elmish-style, data-oriented ECS-like, event-driven, etc.) and allow projects to scale without rewriting the framework.

This is a **pre-1.0** roadmap. We’re building from the ground up and are free to move fast, including making breaking changes while the design settles.

Most importantly: **Elmish is the primary public API**.

- A game should scale from tiny to huge while still feeling like: `init`, `update`, `subscribe`, `view`.
- Alternative approaches (ECS, event buses, command buffers, adaptive state, etc.) should be usable as **implementation details** behind the Elmish surface (usually via services in `GameContext` and `Program.with…` combinators), not as the public mental model.

---

## Design goals

### 1) Growth without rewrites

A prototype should be able to ship and gradually evolve into a large codebase by adding capabilities (systems, state roots, buses, caches), not by swapping paradigms.

### 2) Explicit frame phases, deterministic behavior

Large games need predictable ordering: input → simulation → flush → render → UI, etc. Mibo should make this explicit, configurable, and testable.

When Mibo introduces phase concepts or ordering, they should be designed to **align with MonoGame’s existing component model** (Update/Draw + UpdateOrder/DrawOrder), not compete with it.

### 3) Performance-friendly by default, but not hostile

Provide building blocks that make low-allocation patterns straightforward (pooled buffers, struct commands, diffing), while keeping the “hello world” API small.

### 4) Library-first API surface

Most core behavior should live in modules and data types. Classes should be thin integration points for MonoGame.

### 5) Pluggable subsystems

Input, assets, scenes, time-step, diagnostics, eventing, and simulation state should all be pluggable, so different game genres can mix-and-match.

### 6) Elmish-first ergonomics at every scale

Mibo should not force a “two-tier API” where Elmish is only for prototypes.

Instead, Mibo should keep a single guiding workflow (Elmish) and let advanced runtime features sit behind it:

- `Cmd` is for orchestration and async work.
- `Sub` is for subscriptions.
- High-frequency simulation mechanisms (writes/events/caches) run behind the scenes and are driven by the game loop.

---

## Elmish internals: making an Elmish API fast enough for games

An Elmish-style API is attractive because it makes gameplay logic easy to structure and compose. The risk is that a naive “everything is a message” approach can become allocation-heavy and difficult to optimize.

The goal is to keep the **public authoring experience** Elmish (`init/update/subscribe/view`) while ensuring the **runtime implementation** supports high throughput.

### What Elmish should do in a game framework

Elmish works best as the **control plane**:

- reacting to player intent, UI actions, scene transitions
- starting/stopping services
- orchestrating async operations (loading, matchmaking, IO)
- coordinating systems at a coarse level

### What Elmish should not be forced to do

Elmish should not be the mechanism for per-entity, per-frame hot loops. If the only way to move 10,000 entities is “dispatch 10,000 messages”, the architecture will fight performance.

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

Provide a standard “write buffer” pattern (pooled, end-of-frame flush). Systems enqueue write commands; a dedicated flush phase applies them.

Elmish modules can still _initiate_ work (e.g., enable a system, change a mode), but state churn is handled as bulk writes.

3. **Event aggregation and batching**

When high-frequency events are necessary (collisions, hits, AI perceptions), they should be published to an internal event stream and either:

- consumed directly by services, or
- aggregated into a small number of Elmish messages (e.g., `Msg.FrameEvents of FrameEvent[]`) once per frame.

This keeps `update` readable while bounding message overhead.

4. **Allocation discipline in the runtime**

Mibo’s internals should make the common path allocation-light:

- prefer struct DUs for internal command/event representations
- pool frequently resized buffers (ArrayPool-backed)
- avoid per-frame intermediate collections when possible
- keep subscription diffing allocation-friendly (already a strength)

5. **Bounded work per frame**

The runtime should support safeguards:

- optional per-frame limits (max messages processed, max events flushed)
- predictable behavior under load (e.g., drop/merge policies for non-critical telemetry)

This is about preventing spirals where a single hitch creates a backlog that never recovers.

### The unclear part: “Elmish but fast” requires message semantics

To make Elmish scale, Mibo needs a strong stance on what a message _means_.

If a message is allowed to mean “a single entity moved 1cm,” then high entity counts imply high message rates, and the runtime will drown.

Instead, treat messages as:

- **intent / decisions / mode transitions** (e.g., “player started firing”, “entered targeting mode”, “load scene X”)
- **frame-level aggregates** (e.g., “these 120 hits occurred this frame”, “input snapshot for this frame”)

And explicitly avoid using messages for:

- per-entity deltas in the hot path
- direct mirrors of low-level input polling

This is not about restricting users; it’s about making the ergonomic path also the fast path.

### Concrete internal mechanisms to enforce the semantics

Mibo should add runtime facilities that make the above semantics natural:

1. **Coalescing / latest-value channels**

Some information is “latest wins” (mouse position, camera target, analog stick). The runtime should support channels that keep only the latest value per frame and emit at most one message.

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

If Mibo adds buffering, coalescing, and backpressure, it must add minimal visibility so debugging doesn’t become guesswork:

- per-frame counters (messages dequeued, messages dropped/coalesced)
- event bus counts (published, flushed, dropped)
- buffer sizes (write buffers, render buffers)
- optional tracing of “top talkers” (which source produced the most)

This belongs in the runtime, not only in user code.

### Authoring guidance (how to stay Elmish and fast)

Rule of thumb:

- if something happens many times per frame, express it as bulk data iteration + buffered writes
- if something changes a mode or kicks off async work, express it as an Elmish message

### Public API principle

All of the above should be reachable through the Elmish surface:

- `Program.with…` combinators configure phases, services, and flush points.
- `GameContext` exposes the relevant services (input, assets, event stream, write buffers, diagnostics).

Users should not have to “switch paradigms” to stay performant; they should mostly decide _where work runs_ (service vs update) and _how it is buffered_.

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

The roadmap below extends these while keeping “small game” workflows ergonomic.

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

## Phase 0 — Clarify contracts (groundwork)

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

### Success criteria

- A developer can answer: “where does my work run and in what order?” without reading the runtime code.

---

## Phase 1 — Frame pipeline + system scheduling (scale enabler #1)

Large games need explicit phases and ordering; small games need no ceremony.

In Mibo terms, this should still look like one Elmish program. The pipeline is an internal execution plan that the program configures, not a separate programming model.

Important: phases should not be a second scheduler. The intent is:

- phases are a clear semantic description of order
- the implementation maps them onto MonoGame ordering (components/services) so there’s one source of truth at runtime

### Deliverables

#### 1.1 Frame phases

Introduce a small set of explicit phases, e.g.

- `PollInput`
- `PreUpdate`
- `Update`
- `PostUpdate`
- `Flush`
- `PreDraw`
- `Draw`
- `PostDraw`

This can be implemented as an ordered list of “pipeline steps” with minimal overhead.

#### 1.2 Ordered systems

Provide a system abstraction that can be used with or without Elmish:

- Update-only systems
- Draw-only systems
- Systems with both
- `Order` within a phase
- `Enabled` toggles

Keep the implementation compatible with MonoGame (`GameComponent` / `DrawableGameComponent`) but don’t require inheritance.

#### 1.3 Time-step options

Add configurable time-step strategies:

- variable step (default)
- fixed step simulation with accumulator
- hybrid (fixed simulation, variable render)

### Success criteria

- A project can add 30+ systems and still reason about ordering.
- A simple Mibo sample remains simple (no forced pipeline setup).

---

## Phase 2 — State roots + write boundaries (scale enabler #2)

Mibo should support shared state across many systems without forcing a specific “ECS” implementation.

The key requirement is: systems can share state safely and efficiently, while the _public API remains Elmish_.

### Deliverables

#### 2.1 State roots and state containers

Avoid baking a single “World” concept into the framework. Games may have:

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

The key is a stable boundary for “what systems read” and “what gets flushed.”

#### 2.2 Write boundaries and end-of-frame flushing

Provide an optional state-write service pattern:

- systems enqueue write commands during `Update`
- a `Flush` phase applies them
- the flush point is a single place to ensure determinism

This should be supported even if the state root is fully immutable (flush applies a batch of transforms) or fully mutable (flush performs mutations).

#### 2.3 Pooled command buffers

Provide reusable command buffers for high-frequency writes:

- ArrayPool-backed resizable buffers
- optional auto-shrink after sustained low usage
- support for struct commands and value options

### Success criteria

- A game can process thousands of entities without turning every per-entity change into an Elmish message.
- State mutation timing is predictable and easy to test.

---

## Phase 3 — Event bus for high-frequency messaging (scale enabler #3)

Elmish messages are great for orchestration; large simulations also need a cheaper intra-frame signaling mechanism.

This should be exposed as a service/pattern that Elmish modules can use, not as a replacement for Elmish.

### Deliverables

#### 3.1 EventBus primitive

Add an optional event bus with:

- very cheap `Publish`
- deterministic `Flush`
- subscription support
- clear guidance on when to use Elmish messages vs bus events

Implementation options:

- ring buffer with pooled storage
- or double-buffered event lists per frame

#### 3.2 Patterns

Provide recommended patterns:

- use Elmish for UI/control-plane
- use EventBus for simulation-plane and cross-system signals

Also include conventions and minimal observability to avoid “event spaghetti”:

- when to prefer direct calls vs events
- event taxonomy (few, well-defined categories)
- tracing hooks to inspect per-frame event counts and hot publishers

### Success criteria

- High-frequency events (combat hits, collision contacts, AI perceptions, etc.) do not create message storms.

---

## Phase 4 — Scene lifecycle + resource scoping (scale enabler #4)

Large games need scene/level transitions, scoped services, and correct disposal.

The scene mechanism should integrate into Elmish cleanly (scene transitions can be initiated from `update` via `Cmd` effects or via services that ultimately dispatch Elmish messages).

### Deliverables

- A scene manager abstraction:
  - load/unload scenes
  - scoped services per scene
  - deterministic disposal
  - transition requests (queue, immediate, fade, etc.)
- Clear conventions for what is global vs scene-scoped:
  - content stores
  - simulation state
  - UI roots
  - audio

### Success criteria

- Scenes can be added/removed without leaks.
- Multiplayer/splitscreen or multiple state roots becomes feasible.

---

## Phase 5 — Data-driven content + stores (scale enabler #5)

To support large games, Mibo should make “content as data” straightforward without dictating formats.

### Deliverables

#### 5.1 Store abstraction

Introduce a small “store” interface pattern:

- `tryFind` / `find`
- `all`
- optional hot-reload hooks

#### 5.2 Load pipeline helpers

Provide helpers for:

- JSON (or other) deserialization
- validation and error reporting
- dependency mapping (IDs referencing assets)

#### 5.3 Main-thread load queue

MonoGame content loading is typically main-thread sensitive. Provide a shared primitive:

- enqueue “load work” from background tasks
- drain on main thread during an explicit phase

### Success criteria

- A project can scale to hundreds/thousands of content definitions with predictable loading behavior.

---

## Phase 6 — Rendering orchestration (scale enabler #6)

Mibo’s batch renderers are good building blocks. Large games need a higher-level orchestration layer.

### Deliverables

- A render orchestrator concept:
  - multi-pass ordering (opaque, transparent, UI, post effects)
  - multi-camera / multi-viewport rendering
  - centralized caches for models/textures/effects
  - consistent handling of device states

Keep `RenderBuffer<'Key,'Cmd>` as the fundamental “command list” abstraction.

### Success criteria

- A game can render a complex scene with multiple layers and cameras without duplicating orchestration code in every project.

---

## Phase 7 — Diagnostics, profiling, testing (scale enabler #7)

Large games need fast feedback and confidence.

### Deliverables

- Built-in diagnostics hooks:
  - frame timing (update/draw breakdown)
  - command buffer sizes and resizes
  - event bus counts
  - asset cache stats
- Testing support:
  - deterministic time provider
  - headless update loop for unit tests
  - ability to run systems without a `Game` window

### Success criteria

- Core gameplay logic can be tested without rendering.
- Performance regressions are detectable via metrics.

---

## Elmish is the API: scaling without a second paradigm

The goal is not “small mode vs large mode”. The goal is **one Elmish API** that can opt into stronger runtime capabilities.

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

This migration should mostly be “opt into runtime services” rather than “rewrite gameplay modules.”

---

## Non-goals (to keep scope sane)

- Mibo should not mandate a full ECS.
- Mibo should not dictate a specific AI/physics/combat model.
- Mibo should not require adaptive/reactive state, but can make it easy to plug in.
- Mibo should not hide MonoGame; it should provide safe, ergonomic ways to integrate with it.

---

## Definition of “Mibo can host a large game”

Mibo is “large-game ready” when a project can:

- manage many systems with deterministic order
- process large shared state with controlled mutation points
- handle high-frequency events without message storms
- load content/configuration scalably and safely
- run multi-pass rendering and multiple cameras
- test simulation logic headlessly
- diagnose performance and memory behavior

without requiring a rewrite of the application’s architecture.
