# Mibo Execution Model (Current Baseline)

This document describes the execution lifecycle of a Mibo game as of Phase 0. Understanding this order is critical for reasoning about input latency, state consistency, and side effects.

## The Frame Lifecycle (`ElmishGame.Update`)

Mibo follows a strict, single-threaded execution order within the MonoGame `Update` and `Draw` calls.

### 1. Update Phase

When MonoGame calls `Update`, the following steps occur in order:

#### A. Tick Enqueue
If `Program.withTick` is configured, a "Tick" message is enqueued immediately. This is usually the first message in the queue for a new frame.

#### B. Service Update & Subscription Triggering
All registered `IEngineService` instances run their `Update` method. This is the primary phase for external systems to inject data into the Elmish loop.

**The Mechanism:**
1. **Service Polling:** A service updates its internal state (e.g., polling hardware, checking network buffers).
2. **Event Firing:** If relevant changes occur, the service triggers a standard .NET event or `IObservable`.
3. **Subscription Handling:** Active subscriptions (created via `Program.subscribe`) are listening to these events. When an event fires, the subscription's handler is invoked immediately.
4. **Dispatch:** The handler calls the provided `dispatch` function, pushing a new message into the `pendingMsgs` queue.

*Note:* Because this happens *before* the message loop runs, any messages dispatched here are guaranteed to be processed in the current frame.

#### C. Elmish Message Loop
Mibo drains the `pendingMsgs` queue until it is empty.
1. `Update` is called for each message.
2. `Cmd` effects returned by `Update` are executed **immediately** (synchronously).
    - If a `Cmd` dispatches a new message synchronously, it is added to the *end* of the current queue and processed within the same frame.
3. If the state was modified during this loop, subscriptions are refreshed at the end of the phase (`updateSubs`).

#### D. MonoGame Component Update
`base.Update` is called, allowing standard MonoGame `GameComponent`s to run.

---

### 2. Draw Phase

When MonoGame calls `Draw`:

1. **Renderer Execution:** All registered `IRenderer` instances are called with the current `state`.
2. **MonoGame Component Draw:** `base.Draw` is called for standard `DrawableGameComponent`s.

---

## Key Guarantees & Behaviors

### Execution Thread
All Elmish logic (`init`, `update`, `view`, `subscribe`) and `Cmd` execution occurs on the **Main (UI) Thread**.

### Input Latency (The "Input Lag" Property)
Because the `Tick` message is enqueued (Step A) *before* Services poll for input (Step B):
1. Frame N: `Tick` is enqueued.
2. Frame N: `InputService` (or similar) detects a change and enqueues a Message.
3. Frame N: `Update` processes `Tick` (using state captured *before* the service update).
4. Frame N: `Update` processes the Service Message.

**Result:** Logic driven by `Tick` operates on the *previous* frame's service state. The new state is applied later in the same frame.

### Side Effects (`Cmd`)
- `Cmd`s are not buffered. They execute as soon as the `update` function returns them.
- `Async` effects (`Cmd.ofAsync`) are started immediately via `Async.StartImmediate`, meaning they begin execution on the main thread but may yield.

### State Mutation
While the Elmish model is conceptually immutable, Mibo allows (and the samples encourage) the use of mutable collections (Dictionaries, ResizeArrays) within the model for high-frequency data. These mutations are applied immediately during the Message Loop.

---

## Build & Runtime Constraints

As of Phase 0, Mibo is developed under the following technical constraints:

- **Target Framework:** `.NET 10.0`
- **MonoGame Baseline:** `3.8.*` (DesktopGL)
- **Threading:** Single-threaded (Main Thread) for all core Elmish and Rendering logic. Use of background threads is permitted via `Async` but must dispatch back to the main thread via Elmish messages for state updates.
- **Dependencies:**
    - `FSharp.UMX`: Used for unit-of-measure based type safety (e.g., `SubId`).
    - `MonoGame.Framework.DesktopGL`: The underlying engine.