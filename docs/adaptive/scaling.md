---
title: Scaling Adaptive
category: Adaptive
categoryindex: 3
index: 4
---

# Scaling an Adaptive Game (Simple → Complex)

Start simple, add structure when it hurts. This ladder is the path a growing adaptive game usually takes — each level fixes a pain you will genuinely feel before you get there.

## Level 0 — One state, one frame

**Best for:** small games, prototypes, learning the architecture.

One state record, one update, one frame function. If the game stays this size, great — nothing below is required.

## Level 1 — Split update by feature

**Best for:** the update function no longer fits on screen.

Give each feature its own model and its own tick; update becomes a schedule plus one place that reacts to what features report. [Adaptive Systems](systems.html) shows the shape. The test for this level: can you explain the bees module without mentioning flowers?

## Level 2 — Move derived state into the graph

**Best for:** you catch yourself recomputing the same thing every frame.

Counts, filtered lists, "nearest X", score strings — anything that's a function of state you already have becomes a derived value built once at startup. You delete the code that kept it updated; the graph does that now. This is usually the level that makes people pick the architecture in the first place.

```fsharp
// built once, always current:
let hungry = bees |> AMap.filter(fun _ bee -> bee.Energy < 0.2f)
let scoreboard = honey |> AVal.map(formatScore)
```

## Level 3 — Keep the frame cheap

**Best for:** real-time action where frame time shows up in the profiler.

Habits that matter once things move fast:

* In update, read another feature's data once and pass plain values to ticks — not containers, not closures created per frame.
* In the frame function, read each value once and pack; the renderer reads the packed result only.
* Diagnostics counters are plain fields on your world, written from update.

None of this is required for correctness — it's what keeps a busy frame from allocating and what keeps hot loops inline-friendly.

## Level 4 — Leave things out of the graph on purpose

**Best for:** big worlds where not everything needs to react.

The graph is for state somebody derives from or renders. Plenty of state isn't: an id counter, a random number generator, scratch tables only one tick reads, particle pools the frame carries by reference. Keep those as plain fields next to the code that uses them — putting them in containers buys nothing and costs clarity. The one thing to watch: changing the *inside* of a stored value doesn't notify the graph. That's fine exactly as long as nothing derives from that value.

## Level 5 — Big worlds

**Best for:** thousands of entities, late-game scale.

Two costs dominate and both are predictable:

* Derived values that combine two large collections re-scan the inner one when it changes. A filter over 500 bees inside a map over 200 flowers re-checks every flower when any bee moves. When that shows up, compute the same thing from a single collection instead, or move the pairing into your update as a plain loop. Details in [Mibo.Adaptive Performance](../mibo-adaptive/performance.html).
* The frame function reads every value the renderer needs — make sure it reads each once, and avoid `force`/`toMap` materialization there (a plain `getValue` view is cheaper).

If you need determinism — replays, lockstep multiplayer — `AdaptiveProgram.withFixedStep` runs the update in fixed slices with one frame pack at the end.

## Choosing the right level

You can ship a real game at Level 2–3. The step from 0 to 2 is usually motivated by derived state; everything after that is maintenance of a growing codebase, and each level is local: split a feature, lift a computation, tidy a frame function. The [Elmish scaling ladder](../mvu/scaling.html) covers the same journey for the other runtime, if you're comparing.
