# AdaptiveSlop — Rebuild Plan

This document specifies the rebuild of the AdaptiveSlop core library. It contains all the
information necessary for the implementation. It has no external references.

## Status

- Done: Phase 0, Phase 1, Phase 2, Phase 3, Phase 4 (2026-08-03), Phase 5 (2026-08-04),
  Phase 6 (2026-08-04, journals/drain model).
- Next: Phase 7 (hardening + collection API parity).
- Phase 6 design revised (2026-08-04): pull-lazy journals, `force` materialization
  returning `Frozen*`, collections moved to their own files with a shared inline
  `Collections` module.

## 1. Targets

In order of priority:

1. **Throughput.** Do the minimum runtime work per operation.
2. **Zero library-side allocation** on steady-state paths.
3. Engineering effort is not a constraint.

The library is general-purpose. It is not specific to game development. Any application
that writes frequently and reads frequently gets the performance for free.

## 2. Definitions

Use these terms with these exact meanings:

- **Source**: a changeable object (value, set, or map). The user writes to sources.
- **Node**: a computed object. The library creates nodes from combinators such as `map`.
- **Graph**: the sources, the nodes, and the edges between them.
- **Edge**: a link from an object to one of its dependents. Edges point up, from a
  dependency to a parent node.
- **Read**: a call to `GetValue` on a node or a source. A collection `GetValue`
  drains the pending journal entries of the node first, then returns a transient
  view of the internal state.
- **Force**: a call to `force` on a collection. It drains the node, then materializes
  the internal state as an immutable `FrozenSet`/`FrozenDictionary`. This is the only
  collection operation that allocates, and the only one whose result is safe to
  retain. After `force`, the library never touches the returned value again.
- **Transient view**: the internal `HashSet`/`Dictionary` of a collection, returned by
  `GetValue` without copying. Valid only until the next write on the owner thread.
  Computations consume it; they must not retain it or mutate it.
- **Journal**: the pending delta list of a collection node. A write appends the delta
  to the journal of every registered sink. A node processes its journal when it is
  read (drain).
- **Drain**: to apply the pending journal entries of a node: update the internal
  state, advance the version, and append the reduced output delta to the journal of
  every registered sink.
- **Write**: a call that changes a source (`Set`, `Add`, `Remove`).
- **Mark**: to set the dirty flag of a node.
- **Batch**: one transaction. The library applies the writes of a batch at commit.
- **Owner thread**: the single thread that operates on a graph.
- **Post**: to send a change from a foreign thread to the owner thread.
- **Pump**: the owner-thread procedure that applies posted changes.

## 3. Workload model

Make no assumptions about write rates or read rates. The design must support:

- Many writes to the same source between two reads.
- Many reads per time window.
- Arbitrary interleaving of writes and reads.
- Reads that cause further writes (the application updates the graph after a computation).
- One or more foreign threads that post changes to the owner thread.
- Large graphs of which only a part is read.

Correctness must not depend on batch boundaries. Batches improve efficiency. They are not
necessary for correctness.

## 4. Work budget

The work must be proportional to the changes and the reads. Nothing more.

Per operation:

- **Write**: O(m), where m is the number of nodes marked. Marking stops at nodes that are
  already dirty.
- **Clean read**: O(1) per node. One flag check. No lock. No dependency scan.
- **Dirty read**: the cost of the user function, plus O(fan-in) for the dependency
  comparison. No edge writes when the dependency set does not change.
- **Collection write**: O(k), where k is the number of elements added or removed.

A read must never scan the full graph. A write must never recompute.

## 5. Allocation budget

Library-side, in steady state:

| Path | Budget |
|---|---|
| Clean read | 0 |
| Dirty read, unchanged dependency set | 0 |
| Write + mark | 0 |
| Post / pump | 0 |
| Batch with N writes | 0 |
| Collection write (journal append) | 0 |
| Collection drain (delta processing) | 0 |
| Edge change (`bind` switch, observe, dispose) | Amortized; array growth only |
| Node initial load (first read) | O(n), once per node |
| Collection force (materialization) | O(n), only when forced after a change |

Immutable snapshots are inherently allocating. Hot paths must consume deltas and
transient views, not snapshots. `force` is the only allocation point for collections;
this is a usage rule, not a defect.

### Known violations in the current code

Remove these during the rebuild:

- `ChangeableSet/Map.ApplyAndFlush` rents two pooled arrays per single-element operation
  (`Library.fs:1467-1478, 1755-1766`).
- `FlushDeltas` allocates a snapshot of the sink array on each flush (`Library.fs:1412`).
- Every derived collection node updates a persistent F# `Set`/`Map` snapshot per delta
  element, also when never read (`AdaptiveCollections.fs`).
- Every derived node registers with its source in its constructor, even with zero sinks
  and zero reads (`AdaptiveCollections.fs`). Nodes are not disposable.
- `FilterMapNode` registers by concrete type dispatch instead of `IMapSinkRegistry`;
  `AMap.map` on top of `AMap.filter` never receives deltas (proven by test).

Keep these existing mechanisms: the reused transaction buffer, the reused dependency
collector buffers.

## 6. Core architecture

### 6.1 Push-mark, pull-evaluate

- On write: mark the dependents of the source. Do not recompute.
- On read: if the node is dirty, recompute. If not, return the cached value.
- A node recomputes at most once per change.

### 6.2 Edges

Each object stores its edges in two parallel arrays:

- `parents[]`: the dependent nodes.
- `parentSlots[]`: for each parent, the position of this object in the dependency list of
  that parent.

Each node also stores `deps[]` and `depSlots[]`: for each dependency, the position of this
node in the `parents[]` array of that dependency.

Removal of an edge:

1. Swap the last entry of the array into the removed position.
2. Update the stored slot of the moved entry.

Removal is O(1). It does not scan and does not allocate.

Edge formation and maintenance:

- When a node recomputes, it reads its dependencies. The runtime collects these reads.
- Compare the new dependency set with the stored set:
  - If the sets are equal: update the version snapshots only. Do not touch the edges.
  - If the sets differ: add edges for the new dependencies. Remove the edges for the
    dropped dependencies. Use the slots for O(1) removal.
- A recompute with an unchanged dependency set writes no edges. This is the common case.

### 6.3 Two-tier reads

- A node with at least one parent is **observed**. Marking reaches it. Its dirty check is
  one flag read.
- A node without parents is **unobserved**. Its dirty check compares the versions of its
  dependencies against the stored snapshots.
- The third state, `MaybeDirty`, means: parent links are incomplete. Fall back to the
  version check.

This split keeps unobserved subgraphs free on write and keeps observed reads at O(1).

A version check that verifies a node is clean promotes it to `Clean` (observed nodes with
complete dependency links only). Without the promotion, a registered node that never
recomputes stays `MaybeDirty` forever and every read re-walks its dependency closure
through the recursive `.Version` getters: measured 16,363 version checks per write on a
32,767-node tree, ~2 ms per read (2026-08-04). Unobserved nodes are never promoted: their
version check is the only signal, and the walk is inherent to the write-free-on-unobserved
design.

**Write-generation-keyed dirty cache (implemented 2026-08-04).** Every node caches the
verdict of its last version check (or recompute) keyed by the global write generation.
The generation increments unconditionally on every applied write (`MarkFrom`, which every
scalar and collection write path reaches), so a cache hit is sound until the next write:
repeated reads at the same generation are O(1) per node. This makes the 60 Hz write /
120 Hz read polling shape cheap for the reads after the first one per generation. It does
NOT fix DeepWide (write + read per iteration): every read is the first read at a fresh
generation and the O(subtree) walk remains — that corner still requires observation or
eager marking.

A recompute keys its cache to the generation at which it started; a write from user code
in the middle of a compute moves the generation, so the node stays `Dirty` and recomputes
on the next read (this also fixes a latent staleness hole: the recompute no longer clobbers
a mid-compute mark). `MapNNode` and `ReduceNode` carry the same cache. The per-evaluation
cache (evalId key) was removed; it only helped diamond shapes, and the generation key
subsumes it.

### 6.4 Registration cascade

- When a node gains its first parent, it registers itself with its dependencies. The
  dependencies become observed. This rule applies recursively.
- When a node loses its last parent, it unregisters from its dependencies.
- The cascade makes the dirty flag trustworthy for every observed node.

### 6.5 No evaluation during marking

- Marking must not trigger reads.
- Notification callbacks are queued during marking. The library delivers them after the
  batch or the pump completes.
- This rule removes the need for topological levels. Do not add levels, priority queues,
  or re-enqueue logic.

### 6.6 Equality at the source

- A write that does not change the value (per the equality check of the source) must not
  mark and must not increase the version.

### 6.7 Node types

- One generic node type implements the full protocol: edges, dirty state, registration.
- `ChangeableValue` implements the same parent protocol.
- Keep `MapNNode` and `ReduceNode` for wide fan-in.
- Remove `Map3Node` and `Map4Node`. Re-add them only if benchmarks show a need.

### 6.8 Transactions

- A write inside a transaction goes into a per-source pending slot. There is no object
  allocation per write.
- At commit: apply each pending write, then mark.
- If one source is written multiple times in one transaction, keep only the last value.
  This is correct because no read can observe the intermediate values inside a
  transaction.
- Reads inside a transaction see the pre-transaction values. Document this behavior.

### 6.9 Collections

**Internal state.** The state of every collection (source or node) is a mutable
`HashSet<'T>` or `Dictionary<'K,'V>`, plus per-node auxiliary state: refcounts for
operations that can map two source elements onto one output element, and reusable
output buffers. No persistent tree (F# `Set`/`Map`) exists inside the graph.

**Deltas flow through journals; processing happens on read.**

- A source write updates the source state, advances the source version, and appends the
delta to the journal of every registered sink. Append is an array write into a
node-owned reusable buffer. A write never processes a delta.
- A node read drains the node: it applies the journal entries to its internal state
(mapping, filter, refcounts), advances its version, and appends the reduced output
delta to the journal of every registered sink. Draining a node with an empty journal
is O(1).
- A node's version advances when its journal receives a delta, and again when the drain
changes its state. Versions are change counters, not state hashes. A parent detects a
pending change by comparing the version it last recorded against the current one.
- A read of a collection node first drains the node (which recursively drains nothing:
journal entries were appended by its dependencies' writes or drains), then returns a
**transient view** of the internal state and registers the dependency for the calling
computation.
- `force` drains the node and materializes `FrozenSet<'T>`/`FrozenDictionary<'K,'V>`
from the internal state. It is the only allocation point and the only result that is
safe to retain. The consumer may hand it to third parties; the library never touches
it again.
- `ASet.toSet`/`AMap.toMap` and similar helpers materialize the F# `Set`/`Map`
counterparts for consumers that need sorted iteration or F# interop.
- Unobserved nodes never drain (a read is the only trigger). A node that was never
read performs an O(n) initial load on its first read and registers as a sink of its
dependencies at that point. Registration is lazy; disposal unregisters and stops all
further delta processing.

**Public API.**

- `IAdaptiveSet<'T>`/`IAdaptiveMap<'K,'V>` lose the `: comparison` constraint. The
internal hash-based representations need only equality. The F#-interop helpers
(`toSet`, `toMap`, `CSet.set`, `CMap.set`, `ofSeq`) re-impose it at their boundary.
- `GetValue` returns a transient view (`IReadOnlySet`/`IReadOnlyDictionary` over the
internal state). Computations and node initial loads consume it. Retaining or
mutating it is a usage error; `force` is the retainer.
- `force` returns `FrozenSet<'T>`/`FrozenDictionary<'K,'V>`.

**Shared code.**

All collection node logic shares one internal module `Collections`. It is organized
for the F# compiler to inline the shared passes into each node:

- inline operations with `[<InlineIfLambda>]` function parameters for the per-node
lambda (mapping, predicate, identity): the delta application pass, the initial load
pass, the snapshot rebuild pass.
- operations over `byref` struct state for the per-node plumbing: the sink list
(add/remove/grow), the refcounted set (add/remove), the reusable delta buffers
(ensure capacity), the journal (append, reset), the flush to sinks.

The node state lives in structs held by the node classes so the byref operations can
address it. The node classes themselves shrink to: state fields, the sink interface,
`GetValue`, `force`, and disposal. No abstract base classes and no virtual dispatch
in the hot path.

**Codegen facts (measured, 2026-08-04).** These rules apply to every node and every
future collection combinator:

- The F# `match dict.TryGetValue key with | true, v ->` pattern allocates 24 B per
  call. Hot paths must use the explicit out-param form:
  `let mutable v = Unchecked.defaultof<_>; if dict.TryGetValue(key, &v) then ...`.
- F# forbids byref parameters in inline functions (FS0412). Byref operations are
  non-inline; inline `[<InlineIfLambda>]` passes take the state by value and return
  it.
- Byref parameters appear only at top-level call sites (a class field address,
  measured zero allocation). Per-element operations take the state struct by value
  and return the updated struct; measured zero allocation.

## 7. Threading

### 7.1 Confinement

- One graph has exactly one owner thread. All graph operations occur on the owner thread.
- The core contains no locks, no `Interlocked`, no `[<ThreadStatic>]`.
- Rationale: concurrent lock-free graph mutation is complex and error-prone. Confinement
  gives equal or better throughput for this workload, at zero synchronization cost.

### 7.2 Graph context

Each graph has one context object. It holds:

- the evaluation id,
- the dependency collector,
- the transaction buffer,
- the marking stack,
- the notification queue,
- the owner thread id.

In debug builds, each entry point checks the current thread against the owner thread id.
A violation throws an exception.

### 7.3 Cross-thread changes

Foreign threads may only post. They must not read or write the graph.

`Post(value)` on a source:

1. Write the value into a per-source pending field. The field is typed. There is no
   boxing.
2. If the source is not already in the post queue, push the source reference onto the
   queue.

The queue is a bounded multi-producer, single-consumer ring buffer. It is preallocated.
Each slot carries a sequence number for synchronization.

`Pump()` on the owner thread:

1. Drain the ring.
2. In one transaction: apply each pending value. Apply includes the equality check and
   the mark.

Duplicate posts to one source in one window collapse to one application. This is safe
because no read occurs during the drain.

Pumping is automatic: the outermost entry of any owner-thread graph operation drains the
ring first, so pending posts apply at the next read or write, as one batch with one
notification delivery. `Pump()` remains available as an explicit batch point for callers
that want to choose the application boundary (for example, once per frame). The auto-drain
never fires inside a nested operation or a transaction, so an evaluation never observes a
mid-recompute application.

### 7.4 Multiple graphs

An application may run one graph per thread. To synchronize two graphs, post collection
deltas between them. The collections already produce deltas.

## 8. Phases

### Phase 0 — Safety net

- Run the existing test suite as the baseline.
- Add characterization tests: deep chains, diamonds, `bind` inner switching, reads inside
  transactions, collection delta sequences.
- Exit: the suite is green with the new coverage.

### Phase 1 — De-thread the core

- Add the graph context object (Section 7.2).
- Remove all locks, `Interlocked`, `[<ThreadStatic>]`, and per-node sync roots.
- Remove the inactive push machinery. Phase 2 replaces it.
- Exit: the suite is green. Pure pull, version-checked, single-threaded, no locks.

### Phase 2 — Real edges

- Implement the edge protocol of Section 6.2 on every node type.
- Implement the dependency comparison on recompute. Mutate edges only on a real change.
- Implement the registration cascade of Section 6.4.
- Exit: the suite is green, plus new tests that prove edges form and break on recompute,
  on `bind` switches, and on cascade disposal. No behavior change yet.

### Phase 3 — Activate push-mark

- On source write: mark iteratively with the pooled marking stack. Do not use recursion.
- Observed dirty check: one flag read, no version reads.
- Add the source equality check (Section 6.6).
- Add per-source pending slots and write coalescing in transactions (Section 6.8).
- Regression tests:
  - An observed chain of several computed nodes must update after a source write.
  - A mixed chain of generic and specialized nodes must update after a source write.
  - Several writes to one source between two reads must produce the last value.
  - Arbitrary interleaving of writes and reads must stay correct.
- Exit: the suite is green. A steady-state observed clean read allocates 0 bytes, reads
  no versions, and writes no edges.

### Phase 4 — Observation

- `observe` forces an initial read and registers a callback sink as a parent.
- Deliver notifications after the batch or the pump (Section 6.5).
- `Dispose` removes the link and starts the unregistration cascade.
- Edges are strong. The user must dispose. Document this contract.
- Exit: tests for notification delivery, dispose cascade, and absence of leaks.

### Phase 5 — Cross-thread posting

- Implement the per-source pending field and the preallocated ring (Section 7.3).
- Implement `Post` and `Pump`.
- Write the threading guidance for users.
- Exit: a two-thread test (a producer posts; the owner pumps and reads) with no
  synchronization in the core. A stress test with many posts per window, interleaved with
  reads on the owner thread.

### Phase 6 — Collections lifecycle

- Move the collections to their own files (Section 6.9, shared code):
  - `src/AdaptiveSlop.Core/Collections/Shared.fs` — module `Collections`, the struct
    state holders, and the shared inline/byref operations.
  - `src/AdaptiveSlop.Core/Collections/SetNodes.fs` — `ConstantSet`, `MapSetNode`,
    `FilterSetNode`, `UnionSetNode`.
  - `src/AdaptiveSlop.Core/Collections/MapNodes.fs` — `ConstantMap`, `MapMapNode`,
    `FilterMapNode`.
  - `src/AdaptiveSlop.Core/Collections/Api.fs` — `ASet`/`AMap`/`CSet`/`CMap`.
  - File order in the project file: Library.fs, Collections/Shared.fs,
    Collections/SetNodes.fs, Collections/MapNodes.fs, Collections/Api.fs.
  - `ChangeableSet`/`ChangeableMap` and the collection interfaces stay in
    Library.fs (they are sources and interlock with transactions and edges).
- Implement the journal/drain model of Section 6.9: source writes append deltas to
  the journals of their registered sinks; nodes process journals only on read or
  force. A write must not process a delta.
- Node state is struct-held and shared through module `Collections` (inline
  `[<InlineIfLambda>]` passes; byref struct-state operations). No abstract base
  classes.
- `GetValue` returns a transient view of the internal state; `force` materializes
  `FrozenSet`/`FrozenDictionary`; `toSet`/`toMap` materialize F# `Set`/`Map`.
  Drop the `: comparison` constraint from the collection interfaces; the
  F#-interop helpers re-impose it.
- Registration is lazy (first read) and disposal unregisters. Derived nodes are
  disposable.
- Fix `FilterMapNode` registration (interface dispatch, not concrete types). Remove
  the dead `regLeft`/`regRight` flags in `UnionSetNode` and the dead refcounts in
  `FilterSetNode`.
- Remove the per-operation array rentals, the per-flush sink snapshots, and the
  per-delta F# `Set`/`Map` snapshot updates.
- Update the consumers (Demo, Tui, Mibo, benchmarks, tests) to the new API.
- Exit:
  - A leak test: derive, dispose, mutate the source, check the source has no sinks
    and the derived node processes nothing.
  - An allocation test: delivery of an N-element delta (write plus drain) allocates
    0 bytes. `force` after the drain allocates O(n).
  - A regression test: `AMap.map` on top of `AMap.filter` receives updates.
  - The full suite is green in Debug and Release.

### Phase 7 — Hardening and collection API parity

Scope: harden the Phase 6 core, complete incremental computation on collections, and
reach public API parity with FSharp.Data.Adaptive where the parity is sound. Game-shaped
workloads (KipoPhysicsBenchmarks) are stress tests only; the library is general-purpose.

Explicitly out of scope: grouped/spatial-grid nodes, AdaptiveSoA/AList, and the
`mapA`/`chooseA`/`filterA` per-element-adaptive family (measured 147x slower than
transient-view reads; parity "where possible" excludes them).

#### 7.1 Collection observation

- `ASet.observe` / `AMap.observe`: register a callback sink; the drain delivers
  `(state view, delta)` to the callback. Deltas are effective (no-op writes are
  elided at the source). Delivery happens after the batch or pump completes
  (Section 6.5); no evaluation during marking.
- The handle is `IObservation`; `Dispose` unregisters and starts the cascade.
- Parity shape: FDA `AddCallback(action: State -> Delta -> unit)`
  (EvaluationCallbackExtensions.fs:103, 114).

#### 7.2 Incremental reductions and derived checks

- `ASet.count/countBy/contains/isEmpty/exists/forall/single/fold/reduce/reduceBy`,
  `AMap.count/countBy/find/tryFind/isEmpty/exists/forall/single/fold/reduce`,
  `toAVal` (collection state as an adaptive scalar: count, contains, isEmpty).
- Reductions are delta-driven counters over the journal; a non-invertible reduction
  falls back to a full recompute (the FDA `AdaptiveReduction.sub` fallback protocol).
- Every option-returning operation has a voption counterpart (`V` suffix, per FDA:
  `tryFindV`, `findV`, `chooseV`, `choose2V`, `intersectV`, `ofSeqV`, ...).

#### 7.3 Collection algebra — DONE

- Two-source delta nodes: `ASet.unionMany/difference/intersect/xor`,
  `AMap.union/unionWith/intersect/intersectWith/choose2` (+ `choose2V`).
- Projections and construction: `AMap.ofASet/toASet/toASetValues`,
  `AMap.mapSet`, `ASet.ofAVal`/`AMap.ofAVal`, `ofArray/ofList/ofHashSet/ofHashMap`,
  `ofReader`, `constant`, `single`, `custom`.

Recorded FDA deviations:

- `unionMany` is static (`seq<IAdaptiveSet<'T>>` folded over `union`); FDA's is
  the dynamic `aset<aset<'A>>` form — that needs `collect` (7.4).
- `ofHashMap` is `ofMap` (no frozen HashMap type here; `ASet.ofHashSet` exists
  over the BCL `HashSet`).
- `custom`/`ofReader` are pull-based poll nodes (signatures in Api.fs).
- `intersect` returns a struct pair (collapses FDA `intersect`/`intersectV`).
- `choose2` is voption-only (FDA's `choose2V`; the option variant is not provided).
- `ofASetIgnoreDuplicates` is last-wins.
- Zero-allocation steady-state drains verified by permanent tests
  (`* drains allocate zero in steady state`). Two root causes were found and
  fixed (see BISECT-NOTES.md): reference tuples in the `unionWith`/`intersect`/
  `intersectWith` wrapper mappings (32 B per call, now struct tuples), and a
  generalized class-level identity lambda in `UnionSetNode` materialized per
  drain (24 B, now a module-level inline function; the Shared.fs drain/load
  functions are `inline` + `[<InlineIfLambda>]`).

#### 7.4 Dynamic dependencies — DONE

- `ASet.collect` (per-element dynamic union over a set source) with ref-counted
  contribution tracking (the `CountingHashSet` role): `CollectSetNode`. Per
  source element an entry holds the inner set, last-seen version, content,
  journal, and a key-routing sink. Output = global refcounted set; a removed
  source element unregisters its inner sink eagerly (Pitfall 1). Poll sources
  (`ofReader`/`custom`) work as inners via the per-entry version check.
- `ASet.bind`/`AMap.bind` over an **aval** source (FDA parity, `BindReader`
  semantics): the whole inner collection swaps on value change; the old inner
  sink is unregistered eagerly. `BindSetNode`/`BindMapNode`.
- `ASet.bind`/`AMap.bind` and `collect`: per-element adaptive mapping with ref-counted
  contribution tracking (the `CountingHashSet` role). Recompute re-reads all
  dependencies; old dynamic edges are removed eagerly (Pitfall 1).

Recorded deviations and notes:

- `bind` is the aval-driven whole-swap (FDA terminology: `AdaptiveHashSet.fs`
  `bind : ('A -> aset<'B>) -> aval<'A> -> aset<'B>`; the set-driven form is
  FDA's `collect`, which is what `ASet.collect` is here). A set-source bind
  alias is intentionally not provided: `ASet.collect` already has that exact
  signature.
- FDA has no `AMap.collect`; none is provided here either. A true map collect
  would need a key-conflict rule (a set union refcounts; a map has no natural
  answer when two inner maps collide on a key).
- Dynamic `unionMany` = `ASet.collect id` over `IAdaptiveSet<IAdaptiveSet<'T>>`
  (FDA's `unionMany : aset<aset<'A>> -> aset<'A>` is exactly that).
- The F# `for KeyValue(k, v) in dictionaryField` pattern allocates per element
  (measured 88 B/entry in the collect version-check loop). Hot loops over
  dictionary fields must use an explicit struct enumerator.
- Initial loads and entry creation read the source/inner view first and
  register the sink after: the view is complete and the sink sees only deltas
  that follow. This avoids the double-apply of a dirty source draining into
  the journal during the load.
- The same double-apply hazard existed in every Phase 6 node (MapSetNode,
  FilterSetNode, UnionSetNode, TwoSourceSetNode, MapMapNode, FilterMapNode,
  Choose2MapNode, SetToMapNode, SetToMapKeepAllNode, MapToSetNode, and both
  reduction nodes): they registered their sinks before the initial load, so a
  dirty derived source draining during the load pushed its delta into the
  journal and the subsequent drain double-applied it (measured: phantom
  refcounts that never released). All now read first and register after; the
  permanent regression test is `dirty derived source at first read does not
  double-apply` (set, map, and reduction paths).
- Tests: union/refcount/churn correctness, dynamic unionMany, poll inners,
  bind swap + eager-unregister leak checks, disposal leak checks, and a
  zero-allocation steady-state drain test (150/150 Debug and Release).

#### 7.5 Hardening

- Permanent tests: N-element delta delivery (write plus drain) allocates 0 bytes;
  O(changed) drain (one write touches one element); no-op elision (equal-value
  `AddOrUpdate` does not mark).
- Benchmark the full suite against the Phase 0 baseline and the 2026-08-04 row.
- Only if the data demands it: re-add specialized small-arity nodes, output-equality
  predicates, or marking-stack tuning.

## 9. Do not build

- Levels or topological marking.
- Weak-reference edges.
- Checkpoint history for collections. The journal is a pending-delta list, not history.
- Persistent tree structures for internal state or for deltas (no F# `Set`/`Map`
  inside the graph). Materialized `Frozen*` snapshots at `force` are allowed: they are
  outputs, not state.
- F# `Set`/`Map` as the public collection return type. `force` returns `Frozen*`;
  `toSet`/`toMap` are the explicit opt-ins.
- Abstract node base classes for collections. Shared logic is inlined from the
  `Collections` module.
- Lock-free graph internals.
- Specialized small-arity nodes (unless benchmarks show a need).
- Output-equality predicates on computed nodes (unless benchmarks show a need).

## 10. Definition of done for the base (end of Phase 3)

- A write touches only O(m) nodes and allocates 0 bytes.
- A clean read does one flag check and allocates 0 bytes.
- A recompute with an unchanged dependency set writes no edges and allocates 0 bytes on
  the library side.
- Correctness holds for arbitrary interleaving of writes and reads, including several
  writes to one source between two reads.
- The full test suite is green.
