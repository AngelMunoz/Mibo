---
title: Scaling
category: Adaptive
categoryindex: 3
index: 6
---

# Scaling an Adaptive Game (Simple → Complex)

Start simple, add structure when it hurts. This ladder is the path a growing adaptive game usually takes: each level fixes a pain you will genuinely feel before you get there.

## Level 0: One state, one frame

**Best for:** small games, prototypes, learning the architecture.

One state record, one update, one projection. If the game stays this size, great; nothing below is required.

## Level 1: Split update by feature

**Best for:** the update function no longer fits on screen.

Give each feature its own model and its own tick; update becomes a schedule plus one place that reacts to what features report. [Adaptive Systems](systems.html) shows the shape. The test for this level: can you explain the bees module without mentioning flowers?

## Level 2: Move derived values into the graph

**Best for:** you catch yourself recomputing the same thing every frame.

Counts, filtered lists, "nearest X", score strings: anything that's a function of state you already have becomes a derived value built once at startup. You delete the code that kept it updated; the graph does that now. This is usually the level that makes people pick the architecture in the first place.

```fsharp
let isHungry _key (bee: Bee) = bee.Energy < 0.2f

// built once, always current:
let hungry = bees |> AMap.filter isHungry
let scoreboard = honey |> AVal.map formatScore
```

## Level 3: Keep the frame cheap

**Best for:** real-time action where frame time shows up in the profiler.

Habits that matter once things move fast:

* In update, read another feature's data once and pass plain values to ticks, not containers, and don't build function values per step.
* In the projection, read each value once and pack; the renderer reads the packed result only.
* Diagnostics counters are plain fields on your world, written from update.

None of this is required for correctness; it's what keeps a busy frame from allocating, and what keeps hot loops cheap for the compiler to optimize.

## Level 4: Leave things out of the graph on purpose

**Best for:** big worlds where not everything needs to react.

The graph is for state somebody derives from or renders. Plenty of state isn't: an id counter, a random number generator, scratch tables only one tick reads, particle pools the frame reads directly. Keep those as plain fields next to the code that uses them; putting them in containers buys nothing and costs clarity. The one thing to watch: changing the *inside* of a stored value doesn't notify the graph. That's fine exactly as long as nothing derives from that value.

## Level 5: Big worlds

**Best for:** thousands of entities, late-game scale.

Two costs dominate and both are predictable:

* Derived values that combine two large collections re-scan the inner one whenever it changes. When that shows up, compute the same thing from a single collection instead, or move the pairing into your update as a plain loop. [Derived State](derived-state.html) explains the mechanics and [Mibo.Adaptive Performance](../mibo-adaptive/performance.html) has the cost model.
* The projection reads every value the renderer needs; make sure it reads each once, and avoid `force`/`toMap` materialization there (a plain `getValue` view is cheaper).

If you need determinism (replays, <abbr title="multiplayer where every peer applies the same inputs in the same order, so every run stays in sync">lockstep</abbr> multiplayer), `AdaptiveProgram.withFixedStep` runs the update in fixed slices with the frame forced once at the end.

## Choosing the right level

You can ship a real game at Level 2–3. The step from 0 to 2 is usually motivated by derived state; everything after that is maintenance of a growing codebase, and each level is local: split a feature, lift a computation, tidy a projection. The [Elmish scaling ladder](../mvu/scaling.html) covers the same journey for the other runtime, if you're comparing.
