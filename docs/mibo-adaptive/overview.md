---
title: Overview
category: Mibo.Adaptive
categoryindex: 4
index: 1
---

# Mibo.Adaptive

`Mibo.Adaptive` is the incremental-computation library under the [Adaptive architecture](../adaptive/overview.html) — a pull-based derived state graph that tracks dependencies automatically and recomputes only what changed. It ships as its own NuGet package, depends only on the BCL, and can be used without the rest of Mibo.

The main target is the tight-loop profile: many values change between reads, and reads must be cheap and allocation-free. The canonical shape is a derived state graph **forced once per tick**.

```fsharp
open Mibo.Adaptive

let width = CVal.create 10.0
let height = CVal.create 20.0

// Computed values track dependencies automatically
let area = width |> AVal.map2 (fun w h -> w * h) height

AVal.getValue area   // 200.0
width.Set(15.0)
AVal.getValue area   // 300.0
```

## Design

* **Pull-lazy evaluation.** Writes bump versions. Reads compute, per dirty node, once per change. Ten writes before one read cost one recompute. A read at a settled state is O(1).
* **Zero allocation on steady state.** Delta buffers are reused. Steady-state reads and writes allocate nothing.
* **Coarse scans instead of per-entry bookkeeping.** `mapA` nodes re-check every entry's version on read after a write. The trade is flat overhead in exchange for a scan after each write.
* **Transactions.** Writes inside `Transaction.run` apply at commit.

Write as many times as you want between reads — the writes are free until you read. Read as many times as you want between writes — the reads are free until the next read after a write.

## When to use FSharp.Data.Adaptive instead

[FSharp.Data.Adaptive](https://fsprojects.github.io/FSharp.Data.Adaptive/) is the mature choice for general incremental computing: it has the full API surface, `IndexList`, history, and Fable/JS support, and it wins on very wide dependency graphs and large `mapA` collections where the coarse scan loses.

Rule of thumb: general incremental computing → FSharp.Data.Adaptive. Tight loops with cheap reads → Mibo.Adaptive.

## Layout of this section

* [Adaptive Values](values.html) — `cval` inputs and `aval` computed nodes.
* [Adaptive Collections](collections.html) — `cset`/`cmap`/`clist` sources, `aset`/`amap`/`alist` views, joins.
* [Transactions & Threading](threading.html) — batching, owner-thread confinement, cross-thread posting.
* [Performance](performance.html) — the zero-allocation contract, measurement, and the join cost rule.

The Mibo integration on top of the library — `AdaptiveProgram`, hosts, the intent queue — is documented under [Adaptive](../adaptive/program.html); it lives in `Mibo.Core`, which references this package.
