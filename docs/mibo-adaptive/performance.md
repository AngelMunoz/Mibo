---
title: Adaptive Performance
category: Mibo.Adaptive
categoryindex: 4
index: 5
---

# Performance

The library is built for tight-loop work, and its behavior there is something you can rely on:

* **Steady state allocates nothing.** Once your graph has settled, reads, writes, and delta propagation don't allocate — buffers are reused. The exceptions are the deliberate materializations (`force`, `toSet`, `toMap`).
* **A value recomputes at most once per change.** Ten writes between two reads cost one recompute. A read when nothing changed is a cheap version check.
* **After a write, derived collections re-scan.** A `mapA` node re-checks every entry's version on the next read — flat, predictable work per entry. On very large derived collections this scan is the main cost, and [FSharp.Data.Adaptive](overview.html#when-to-use-fsharpdataadaptive-instead) can win there.

If allocation matters to you, measure it rather than trusting anyone's claims: wrap a settled read in `GC.GetAllocatedBytesForCurrentThread` before and after, and look at the difference.

## How much a join costs

`AMap.joinOn` — joining two maps on the same key — is cheap on writes: when one entry changes, only that entry's subgraph updates.

`mapA` with a closure that reads another adaptive collection is the one shape to watch. Say you map over 200 flowers, and each flower's lambda filters 500 bees. Every time the bee map changes, every flower's filter re-runs: 200 × 500 checks. That's not a bug — it's what the shape does — but at scale it dominates. When you hit it, restructure: derive from a single collection where possible, or compute the pairing yourself in a plain loop where you control the cost.

The [benchmarks](https://github.com/AngelMunoz/Mibo/blob/main/src/Mibo.Adaptive/docs/BENCHMARKS.md) compare the combinators against FSharp.Data.Adaptive at several scales, if you want numbers.

## Reading patterns

* `getValue` on a value or collection returns the current state — a snapshot for collections, valid until your next write. Read it, use it, move on.
* `force` / `toSet` / `toMap` build immutable copies you can keep. They allocate — fine for setup and cold paths, wrong for a per-frame loop.
* In a Mibo game, the per-frame pattern is simple: write during update, read once when packing the frame. Every dirty node recomputes exactly once, in dependency order, and the renderer gets plain values.

## What the library does not do

* No push — no callbacks fire on write, so a write never re-enters your code.
* No history/undo, no Fable/JS backend, and persistence is limited to JSON round-trips of the changeable types.

If you need those, that's the [FSharp.Data.Adaptive](overview.html#when-to-use-fsharpdataadaptive-instead) signal.
