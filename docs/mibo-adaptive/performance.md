---
title: Performance
category: Mibo.Adaptive
categoryindex: 4
index: 5
---

# Performance

The first thing to internalize: **using adaptive data is rarely the bottleneck.** In a live simulation-shaped game (several systems, a spatial join per entity, HUD values that follow the world every frame) the adaptive machinery runs at a small fraction of a millisecond per frame at 60 fps. The frame's real cost is almost always elsewhere: the GPU, the draw batch, <abbr title="vertical sync: the display's cap on how fast finished frames get shown">vsync</abbr>. A healthy graph is close to free.

Where the cost actually comes from, in order of likelihood:

1. **Naive construction**: rebuilding a node per frame, or a hand-rolled join that re-scans instead of using the operator.
2. **Allocation on the hot path**: `force`/`toMap` called every frame, or nodes created inside `update`.
3. **The drawing phase**: not the graph at all.

The library is built for tight-loop work, and its behavior there is something you can rely on:

* **Steady state allocates nothing.** Once your graph has settled, reads, writes, and delta propagation don't allocate; buffers are reused. The exceptions are the deliberate materializations (`force`, `toSet`, `toMap`).
* **A value recomputes at most once per change.** Ten writes between two reads cost one recompute. A read when nothing changed is a cheap version check.
* **After a write, derived collections re-scan.** A `mapA` node re-checks every entry's version on the next read, which is flat, predictable work per entry. On very large derived collections this scan is the main cost, and [FSharp.Data.Adaptive](overview.html#When-to-use-FSharp-Data-Adaptive-instead) can win there.

If allocation matters to you, measure it rather than trusting anyone's claims: wrap a settled read in `GC.GetAllocatedBytesForCurrentThread` before and after, and look at the difference.

## How much a join costs

`AMap.joinOn` (joining two maps on the same key) is cheap on writes: when one entry changes, only that entry's subgraph updates.

`mapA` whose mapping function reads another adaptive collection is the one shape to watch. When the inner collection changes, every element's mapping re-runs, so the cost is linear in the outer collection, and it re-runs as often as the inner one changes. That's not a bug; it's what the shape does. In practice a live simulation-shaped game with a per-entity join still keeps its adaptive work at a small fraction of a millisecond per frame at 60 fps. Joins are fine.

The shape that stops paying is a join over inputs that change *every frame*. Mixing positions or time (which move constantly) into a join means the rescan never settles, and it grows with the collections. When that shows up in a profile, don't keep paying: derive from a single collection, or compute the pairing in a plain loop where you control the cost directly. The projection is a convenience for reads, not a rule that everything must stay derived.

The [benchmarks](https://github.com/AngelMunoz/Mibo/blob/main/src/Mibo.Adaptive/docs/BENCHMARKS.md) compare the combinators against FSharp.Data.Adaptive at several scales, if you want numbers.

## Reading patterns

* `getValue` on a value or collection returns the current state: a snapshot for collections, valid until your next write. Read it, use it, move on.
* `force` / `toSet` / `toMap` build immutable copies you can keep. They allocate, which is fine for setup and cold paths, wrong for a per-frame loop.
* In a Mibo game, the per-frame pattern is simple: write during update, read once when packing the frame. Every dirty node recomputes exactly once, in dependency order, and the renderer gets plain values.

## What the library does not do

* No push: no callbacks fire on write, so a write never re-enters your code.
* No history/undo, no Fable/JS backend, and persistence is limited to JSON round-trips of the changeable types.

If you need those, that's the [FSharp.Data.Adaptive](overview.html#When-to-use-FSharp-Data-Adaptive-instead) signal.
