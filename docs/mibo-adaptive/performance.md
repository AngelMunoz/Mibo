---
title: Adaptive Performance
category: Mibo.Adaptive
categoryindex: 4
index: 5
---

# Performance Contract

The library's contract for tight-loop work:

* **Zero allocation on steady state.** Clean reads, marks, static recomputes, and delta delivery allocate nothing. Delta buffers are reused; the `mapN`/`reduce` nodes avoid intermediate arrays. The deliberate exceptions: `force`/`toSet`/`toMap` (materialization), and `PollListSourceNode`'s rebuild (its bounds may themselves be adaptive, so a source-version gate would be unsound).
* **Recompute = at most once per change, per node.** Ten writes before a read cost one recompute. A read at a settled state is O(1) — a version check.
* **Coarse scans after writes.** `mapA`-family nodes re-check every entry's version on the read that follows a write — flat overhead per entry, no per-entry bookkeeping. On very large derived collections this scan is the cost center; [FSharp.Data.Adaptive](overview.html#when-to-use-fsharpdataadaptive-instead) wins there.

Prove allocation claims on your graph, not by inspection — the test suites assert them with `GC.GetAllocatedBytesForCurrentThread` around settled reads.

## The join cost rule

The one performance trap with a design history:

* **`AMap.joinOn`** is the sanctioned low-churn form for same-key joins — the per-key subgraph swaps its static input in place; no rebuild on write.
* **`mapA` closures rebuild by design.** A `mapA` whose closure captures another adaptive source re-scans that inner source per element per change — O(outer × inner) of graph work per frame when the inner map changes every frame. That is correct behavior, not a bug; it is simply the wrong shape at scale.
* The escape is the same one the tower-defense samples took: when a live join stops paying for itself, drop to a **plain row map** over a single map (`AMap.map` over one source) and move the cross-system behavior into the tick as direct values.

Measure before and after any join change — the [benchmarks](https://github.com/AngelMunoz/Mibo/blob/main/src/Mibo.Adaptive/docs/BENCHMARKS.md) compare the combinators against FSharp.Data.Adaptive at several scales.

## Forcing once per tick

The canonical shape: write freely during the step, then force every output once. Nodes settle in dependency order, each dirty node recomputes exactly once, and the render side reads the packed result without touching the graph ([Adaptive Programs](../adaptive/program.html) — the frame builder).

## What the library does not do

* No push, no callbacks on write — nothing re-enters your code from a write.
* No history/undo, no Fable/JS backend, no persistence story beyond JSON converters for the changeable types (`cval`/`cset`/`cmap`/`clist` round-trip via System.Text.Json).

If you need those, that is the [FSharp.Data.Adaptive](overview.html#when-to-use-fsharpdataadaptive-instead) signal.
