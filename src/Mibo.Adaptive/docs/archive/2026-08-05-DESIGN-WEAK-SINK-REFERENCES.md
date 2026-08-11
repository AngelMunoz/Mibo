# Design — Weak-Reference Sinks (GLM 10, 2026-08-05)

Status: **for review**. No code changed yet. This document proposes the fix for
the derived-node retention leak (GLM review item 10) and explains why
weak-reference sinks are the right mechanism.

## 1. Problem

A derived collection node registers a delta sink with its source on its first
read (`EnsureInitialized` → `Register`). The sink is removed only in
`Dispose()`. There is no finalizer. A caller that creates a derived node, reads
it, and then drops it while the source stays alive leaves the source holding a
strong reference to the node: the node, its journal, and its buffers stay
reachable forever.

The contract burden is on the user, and the user cannot reasonably carry it:

```fsharp
let myThing = cval 10

let timesTwo  = myThing |> AVal.map (fun a -> a * 2)

let longtransform =
    timesTwo
    |> AVal.map (fun a -> MyThing.ofA a)
    |> AVal.map getOtherFrom
    |> AVal.map anotherTransformation
```

Users chain combinators and never dispose the intermediates. Disposal is not
part of the FRP mental model. The same holds for `AMap`, `ASet`, `AList`, and
their combinators. `IDisposable` on the result does not communicate the burden
(the function name does not say "dispose this"), so the leak is silent and
default.

### 1.1 Scalar chains do not leak today; collection chains do

The retention channels differ by node kind:

- **Scalar nodes** (`AdaptiveNode`, `MapNNode`, `ReduceNode`) register *parent
  edges* with their dependencies only when **observed** (`RegisterWithDeps` on
  the first parent, `Library.fs`). An unobserved scalar chain has no edges and
  nothing to leak. Disposing the observation removes the edges.
- **Collection nodes** (`MapSetNode`, `FilterSetNode`, `CollectSetNode`,
  `TwoSourceSetNode`, the map/list counterparts) register a **sink** with the
  source on the **first read**, observed or not. The sink is the delta channel
  that keeps a read node consistent (pull reads drain the journal the sink
  fills). The sink is removed only in the node's own `Dispose`.

So the leak is specific to collection nodes, and it exists in two situations:

1. **Common non-observed use**: `let ys = ASet.map f xs`; `ASet.force ys` once
   or in a loop; drop `ys`; `xs` lives on. The classic leak is creating derived
   nodes in a loop over a long-lived source.
2. **Observed use**: disposing the `IObservation` removes only the observation's
   parent edge. The derived node keeps its sink on the source. Users who do
   everything right with observations still leak every derived node in the
   observed chain.

## 2. FDA precedent (verified against the source)

FDA solves this with weak references on the output channel. Verified in the
local checkout `E:\FSharp.Data.Adaptive`:

- `AdaptiveObject` owns `outputs : WeakOutputSet` (`Core/AdaptiveObject.fs:29`),
  created eagerly per object, plus a lazy cached `WeakReference<IAdaptiveObject>`
  (`Core/AdaptiveObject.fs:36-50`).
- `WeakOutputSet` stores `WeakReference<IAdaptiveObject>` entries
  (`Core/Core.fs:210-219`; the type comment: "The references to all contained
  elements are weak").
- The transaction mark consumes outputs via `e.Outputs.Consume(outputs)`
  (`Core/Transaction.fs:221`): `Consume` returns only the **currently live**
  entries into a reusable array and clears the set. Dead (collected) outputs are
  dropped there.
- Repo analysis doc `docs/archive/2026-08-04-ANALYSIS-FDA.md:60-63`: "Edges use
  **weak references** ... so parts of the graph that nothing holds onto are
  garbage-collected. **This is why FDA leaks nothing without manual
  unsubscribe.**"

FDA still exposes `Dispose` (eager release), but GC alone is safe. We adopt the
same principle.

## 3. Proposal: weak sink references

Make the **sink channel** weak. Parent edges stay strong (see 3.4).

### 3.1 Storage

`SinkList` (`Shared.fs:402`) currently holds `obj[]` (strong). Change the
entries to `WeakReference`:

- `addSink` wraps the boxed sink in a `WeakReference` (one allocation per sink
  registration; registration is amortized edge formation, not a hot path).
- `removeSink` finds the entry whose `WeakReference.Target` is
  `ReferenceEquals` to the sink being removed, then swap-pops. (`Dispose` on a
  live node still works; the GLM review already verified removal matches by
  `ReferenceEquals` — the target comparison keeps that semantic.)
- `clearSinks` is unchanged.
- `SinkCount` (used by tests and the changeable `Dispose` teardown) keeps
  reporting the raw count; entries are compacted on delivery (3.2).

### 3.2 Delivery

`pushSetDelta` / `pushMapDelta` / `pushListDelta` resolve each entry:

- `Target = null` → the node was collected: swap-pop the dead entry and
  re-examine the position (amortized O(1) cleanup, zero allocation, order does
  not matter for delivery).
- `Target = node` → deliver immediately. The local roots the node for the call:
  the GC cannot collect a node while its own method runs, so resolution and
  call are atomic in practice.

This preserves invariant 5 (zero allocation on delta delivery): resolving a
`WeakReference.Target` allocates nothing.

### 3.3 Liveness argument

- A derived node that the user dropped and that is not observed has exactly one
  strong inbound reference: the source's sink list. With weak sinks it becomes
  collectible; when collected, delivery skips it and compacts the entry.
- An **observed** node stays alive: the observation object holds the target
  strongly (`Observation.target`, and the collection `Observe*Node.target`),
  and the user holds the `IObservation`. Observed nodes therefore always
  receive deltas; nothing changes for the observed case except that disposing
  the observation now also *eventually* releases the chain (the derived nodes
  become unreachable and their sink entries die).
- A node the user still holds receives deltas exactly as today.

### 3.4 Scope: sinks only, not edges

Parent edges (`ParentEdges`, `Library.fs:262`) exist only while a node is
observed (they are built on the first parent and torn down on the last), and
observation is already an explicit-lifetime contract (`IObservation` +
documented `Dispose`). The leak lives entirely in the sink channel. Making
edges weak too would add cost to every observed read for no fix. FDA makes its
single output channel weak because FDA has no separate sink channel; we have
one, and it is the one that leaks.

### 3.5 Interaction with earlier fixes

- KIMI 14 (changeable `Dispose` detaches sinks and edges) is unchanged: it
  clears the sink list and the edges eagerly.
- The observed-collection delivery path (mark → drain → push) is unchanged:
  the observer's sink entry is kept alive by the observation's strong target
  reference.
- Per-element inner sinks (collect entries, two-source side sinks) are held
  strongly by their owning entry/node; only the registration on the *source*
  becomes weak. The inner node stays alive as long as its owner does.

## 4. Alternatives considered and rejected

| Option | Verdict |
|---|---|
| Finalizer on derived nodes that unregisters | Rejected: delays collection, resurrection risk, GC pressure, and unregistering from a finalizer thread breaks owner-thread confinement (invariant 4). |
| Debug leak detector (report undisposed nodes) | Rejected as the *only* fix: it finds the leak but keeps the user burden. The FRP mental model (section 1) is the product decision: no disposal chore. |
| Rename (`ASet.mapDisposable`) | Rejected: breaking API, still a burden. |
| Weak parent edges as well | Rejected (3.4): no leak there, cost on observed reads. |

## 5. Impact

- **API**: none. `IDisposable` stays (eager release; the KIMI 14 teardown
  remains meaningful). No function signature changes.
- **Performance**: one `WeakReference.Target` dereference (~1-3 ns) per sink
  per delivery, plus amortized dead-entry compaction. No allocation on the
  steady state. The existing zero-allocation benchmark tests
  (`two-source node drains allocate zero`, `choose2 node drains allocate zero`)
  must stay green; run before/after with BenchmarkDotNet.
- **Allocation**: one `WeakReference` per sink registration (amortized edge
  formation; matches FDA, which also allocates one `WeakReference` per object
  lazily, `AdaptiveObject.fs:36-50`).
- **Invariants** (AGENTS.md): 1, 2, 3, 6 unaffected. 4 holds: `WeakReference`
  access is thread-safe and everything stays owner-thread confined. 5 holds by
  the delivery design and is proven by the allocation tests.
- **Tests to add**:
  1. Drop a derived collection node after a read; `GC.Collect`; assert the
     node is collected (via a `WeakReference`) and that a subsequent source
     write does not throw and other sinks still receive deltas.
  2. Disposing the observation of a chain eventually releases the chain.
  3. `Dispose` still detaches immediately (`SinkCount` → 0).

## 6. Open questions for the reviewer

1. Compaction policy: **resolved** — dead entries are compacted at delivery
   start and on registration (`addSink`).
2. `SinkCount` semantics: **resolved** — raw entry count; dead entries are
   swept on the next delivery or registration, so the count is accurate
   between batches.
3. Should the changeable sources' `WeakReference` be created lazily (FDA
   caches one per object) or is the per-registration `WeakReference`
   sufficient? (We have no second use for a cached weak self-reference
   today.) — **resolved**: per-registration is sufficient; no cached weak
   self-reference exists in this design.
4. Confirm the scope decision (3.4): sinks only, edges stay strong. —
   **approved** by the design owner.

## 7. References

- GLM review item 10: `docs/2026-08-05-GLM_REVIEW_FINDINGS.md`
- Current sink machinery: `src/AdaptiveSlop.Core/Collections/Shared.fs:402`
  (`SinkList`), `Shared.fs:587` (`removeSink`), `pushSetDelta`/`pushMapDelta`/
  `pushListDelta` (delivery)
- FDA: `E:\FSharp.Data.Adaptive\src\FSharp.Data.Adaptive\Core\AdaptiveObject.fs:29,36-50`,
  `Core\Core.fs:210-219`, `Core\Transaction.fs:221`
- Repo analysis: `docs/archive/2026-08-04-ANALYSIS-FDA.md:60-63`
