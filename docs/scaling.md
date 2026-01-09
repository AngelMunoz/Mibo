---
title: Scaling Mibo
category: Architecture
index: 4
---

# Scaling Mibo (Simple → Complex)

Mibo is designed to stay fun for small games while still giving you an upgrade path for “serious” games.
This document is a practical ladder you can climb as complexity increases—without rewriting your engine.

The recurring theme is:

- keep **state changes** serialized (Elmish)
- keep expensive logic **data-oriented** (snapshots + mutable hot paths when needed)
- introduce **explicit boundaries** (per-tick phases, and optionally frame-bounded dispatch)

## Level 0 — Pure MVU (card games, menus, puzzle games)

**Goal:** maximum simplicity.

**Model**: mostly immutable records.

**Update discipline:** handle one message at a time, return `Cmd.none` most of the time.

**Mibo helpers you’ll use:**

- `Program.mkProgram`, `Program.withRenderer`, `Program.withSubscription`
- `Cmd.ofMsg`, `Cmd.batch`

**What you gain:**

- trivially testable logic
- deterministic replay (record the message stream)

## Level 1 — Add semantic input (platformers, arcade games)

**Goal:** stop sprinkling device-specific checks across gameplay.

**Pattern:** map hardware input → semantic actions → update your model.

**Mibo helpers you’ll use:**

- `InputMap` + `InputMapper.subscribe` (or `Program.withInputMapper` if you prefer services)
- model field like `Actions: ActionState<_>` updated by an `InputMapped` message

**Recommendation:** treat input as _data for the next simulation step_.

That usually looks like:

- `InputMapped actions` updates a field (`model.Actions <- actions`)
- `Tick gt` consumes `model.Actions` to advance simulation

## Level 2 — Establish a simulation “transaction” (`Tick` owns gameplay writes)

**Goal:** keep your mental model simple when the game grows.

**Rule of thumb:**

> Non-`Tick` messages update _buffers_ (input snapshots, event queues, pending requests). Only `Tick` mutates the “world”.

This gives you an explicit boundary:

- gather external events during the frame
- run simulation once on `Tick`
- commit results

**Why it helps:**

- fewer ordering surprises
- easier to reason about “what changed this frame”
- makes later deterministic/multiplayer work much easier

## Level 3 — Phase pipelines + snapshot barriers (ARPG/RTS-style subsystems)

**Goal:** support many subsystems without turning update into spaghetti.

Mibo provides a type-guided pipeline in `Mibo.Elmish.System`:

- `System.pipeMutable` for mutation-heavy phases
- `System.snapshot` to freeze a readonly view
- `System.pipe` for readonly/query/decision phases

The pipeline accumulates a single `Cmd<'Msg>` (not a list), so it stays allocation-friendly even as you add phases.

See: [System pipeline (phases + snapshot)](system.html)

**Typical layout:**

1. Integrate physics / movement (mutable)
2. Update particles / animation state (mutable)
3. Snapshot
4. AI decisions, queries, overlap detection (readonly)
5. Emit commands/messages

This is an “ECS-ish” approach that works well even if your storage is still dictionaries/arrays.

## Level 4 — Fixed timestep and determinism (network-ready foundation)

**Goal:** stable simulation independent of framerate.

**Pattern:** run your simulation in fixed slices $\Delta t$.

You can do this manually (accumulator in the model), or use Mibo's framework-managed fixed timestep:

```fsharp
Program.mkProgram init update
|> Program.withFixedStep {
	StepSeconds = 1.0f / 60.0f
	MaxStepsPerFrame = 5
	MaxFrameSeconds = ValueSome 0.25f
	Map = FixedStep
}
```

- variable `GameTime` arrives once per frame
- your simulation runs in fixed steps (e.g. 1/60s) potentially multiple times

See: [The Elmish Architecture](elmish.html) (fixed timestep + dispatch modes)

**Guidelines for determinism:**

- put RNG state (seed) in the model (don’t call ambient `System.Random()` from update)
- avoid reading mutable global state from `update`
- represent time as data (the `Tick` message already does this)

## Level 5 — Frame-stable message processing (optional “advanced mode”)

By default, Mibo processes messages **immediately**: a message dispatched while the runtime is draining the queue can be processed in the same MonoGame `Update` call.

For some advanced architectures (strict frame boundaries, rollback/lockstep friendliness, avoiding re-entrant cascades), you may want:

> messages dispatched while processing frame N are not eligible until frame N+1.

Mibo supports this via `DispatchMode`:

- `DispatchMode.Immediate` (default): maximum responsiveness
- `DispatchMode.FrameBounded`: stronger frame boundary, up to 1-frame extra latency for cascades

Enable it like this:

```fsharp
Program.mkProgram init update
|> Program.withDispatchMode DispatchMode.FrameBounded
```

### Interaction with `Cmd.deferNextFrame`

`Cmd.deferNextFrame` delays an _effect_ until the next MonoGame `Update` call.
In `FrameBounded` mode:

- if the deferred effect dispatches immediately when it runs (synchronous dispatch), it will typically be processed **next frame** as expected
- if it dispatches later (async completion), and that completion happens while the runtime is draining messages, it may be deferred **one more frame**

This is not a bug; it’s the natural result of combining “defer effect execution” with “frame-bounded message eligibility”.

## Choosing the right rung

You can ship a lot of games at Level 2–3.

- **Card/turn-based:** Level 0–1
- **Platformer/shooter:** Level 1–2
- **ARPG:** Level 3 (+ maybe Level 4)
- **RTS:** Level 3–4 (+ Level 5 if you want strict boundaries)

Pick the simplest level that fits your game today, and add the next pieces only when you feel the need.
