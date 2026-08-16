---
title: Adaptive Overview
category: Adaptive
categoryindex: 3
index: 1
---

# The Adaptive Architecture

Mibo ships two runtimes for the same backends: the classic **MVU** loop (`Program.mkProgram`, `Cmd`, `Sub` — see the [MVU](../mvu/elmish.html) section) and the **Adaptive** architecture described here. Both use the same renderers, the same asset pipeline, and the same input contracts. The difference is how state changes flow to the draw side.

MVU serializes every state change through a message loop. That is simple and replayable, but at simulation scale it costs you: every derived value (counts, filtered views, joined tables) is recomputed by hand, and the whole model is re-read by the view every frame.

The Adaptive architecture replaces the message loop with a **derived state graph** (the [Mibo.Adaptive](../mibo-adaptive/overview.html) library) plus a fixed per-step phase order:

```
   State  →  Projection  →  Update  →  Force
   (roots)    (derived)     (ticks)    (frame pack)
```

* **State** — the composition root. Changeable roots (`cval`, `cmap`) hold the facts; each sub-system owns its slice.
* **Projection** — derived nodes (`aval`, `amap`) computed by the graph, only when dirty, at most once per change.
* **Update** — one function per step that ticks the sub-systems in order and translates their events into posted intents (the replacement for `Cmd`).
* **Force** — one call per step that resolves every output projection into a **frame**; the renderer reads the frame and never touches the graph.

## The three contracts

These are the rules that make the architecture work. They are enforced by construction in the framework where possible:

1. **The draw side never touches the graph.** Everything the renderer needs is packed into a frame struct once per step. Drawing is plain reads. Transient views inside the frame are valid until the next step — the draw window is exactly the gap between two steps.
2. **Derived state lives in the graph, not in the tick.** If a value is a function of other state (a count, a filtered view, an upgraded stat), it is a projection node — it recomputes when its inputs change, not every frame, and never by hand in the update loop.
3. **The force phase cannot enqueue work.** The frame builder and projection construction see a queue-less context; only the update phase can post intents. The design makes re-entrant work impossible — no runtime checks needed.

## When to choose Adaptive

Pick Adaptive when your game is simulation-shaped: many entities, per-frame derived views, HUD state that follows the world. The [Defli](https://github.com/AngelMunoz/Mibo.Samples) tower-defense samples are the reference implementation — a nine-system world where the graph serves the render side for a fraction of a millisecond per frame.

Stay with MVU (see [Scaling Mibo](../scaling.html)) when the model is small, the message stream *is* the domain (turn-based games, menus, networked lockstep), or you want deterministic replay of the message log. MVU remains the default starting point; the Adaptive rung sits past Level 3 on the ladder, for when re-reading the model and recomputing derived state by hand stops being free.

> **NOTE:** The two runtimes are not a migration path in either direction — they share the backend layers, not the program shape. Choose per project.

## Where to go next

* [Adaptive Programs](program.html) — the `AdaptiveProgram` API, hosts, the step order, and the intent queue.
* [Adaptive Systems](systems.html) — how the routed sub-system rules translate without `Cmd`.
* [Adaptive Input](input.html) — continuous state vs. discrete actions.
* [Adaptive Services](services.html) — the environment pattern with `boot ctx`.
* [Mibo.Adaptive](../mibo-adaptive/overview.html) — the graph library itself, documented as its own package.
