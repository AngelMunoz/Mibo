# FSharp.Data.Adaptive — Hard Facts Analysis

> Goal: extract *exactly* what makes FDA's incremental computation work, where its
> allocations come from, and what a zero-allocation / game-loop-targeted reimplementation
> must keep, must change, and can drop. Every claim below is backed by a file reference in
> `~/repos/FSharp.Data.Adaptive/src/FSharp.Data.Adaptive/`.

---

## Part 1 — The Core Mechanism (why incremental computation works)

FDA is a **hybrid push-mark / pull-evaluate** system. Incremental correctness comes from
the *interaction* of four mechanisms. Remove any one and the guarantee breaks.

### Mechanism A — The `OutOfDate` dirty flag + lazy recompute (the dual)

`IAdaptiveObject` exposes a single boolean `OutOfDate` (`Core/Core.fs:36`); `AdaptiveObject`
stores it as a field (`Core/AdaptiveObject.fs:27`). (`ConstantObject` fakes it — getter
always `false`, setter ignored.)

- **PUSH side (marking):** when an input changes, a *transaction* flips `OutOfDate <- true`
  walking **UP** through `Outputs`, and **nothing else** — it does not recompute. The walk
  **stops at already-dirty nodes** and at nodes whose `Mark()` returns `false`
  (`Core/Transaction.fs:197-198, 223-224`), so cost is `O(newly-dirty frontier)`, not
  `O(whole graph)`.
- **PULL side (evaluation):** `GetValue()` checks `OutOfDate` under the node's lock. If
  true → recompute and cache; if false → return the cached value
  (`AdaptiveValue/AdaptiveValue.fs`, `AbstractVal.GetValue`).

**The incremental guarantee, stated precisely:**
> A node recomputes **iff** it is both (1) dirty and (2) actually read. Each such node
> recomputes **exactly once** per evaluation (until the next mark).

That single sentence is the whole point of the library. Everything else is engineering to
make it correct, composable, and cheap.

**Where changes enter.** `ChangeableValue.Value`'s setter equality-checks the new value,
then calls `MarkOutdated()` (`AdaptiveValue.fs:26-31`). `MarkOutdated` enqueues the object
into the current transaction — and **throws "cannot mark object without transaction"** if
the object has outputs and no `transact` is running (`Core/Transaction.fs:333-341`).
`transact` = create `Transaction` + set it current + `Commit` (`Transaction.fs:311-315`);
transactions are `[<ThreadStatic>]` (`Transaction.fs:94-98`) and nested `transact` calls
join the running one rather than stacking.

### Mechanism B — Dependency edges are discovered lazily, during evaluation

The graph is **never declared**. It is *self-assembling* from actual reads.

- Each node has an `Outputs` set (`IAdaptiveObject.Outputs`). Despite the name, `Outputs`
  points **UP** the graph: it holds the node's *consumers* (parents/dependents).
- `AdaptiveToken` is a **struct** carrying one field, `caller : IAdaptiveObject`
  (`Core/AdaptiveToken.fs:13-21`). It is threaded into every `GetValue(token)`.
- When `A` evaluates and calls `B.GetValue(token)` where `token.caller = A`, the
  `EvaluateAlways` extension (`Core/AdaptiveObject.fs:140-176`) does `B.Outputs.Add(A)` and
  sets `A.Level <- max(A.Level, B.Level+1)`.

Consequences:
- Combinators need **zero wiring** — they just call `input.GetValue token` inside `Compute`
  and edges appear automatically.
- Edges use **weak references** (`IWeakOutputSet`, `Core/Core.fs:40`; `WeakOutputSet`
  stores `WeakReference<IAdaptiveObject>`, `Core.fs:210-219`), so parts of the graph that
  nothing holds onto are garbage-collected. This is why FDA leaks nothing without manual
  unsubscribe.
- Re-reading on every recompute means edges are **self-healing**: if a `bind` switches to a
  new inner value, the recompute registers on the new inner. `BindVal` additionally
  **explicitly unregisters** from the old inner (`old.Outputs.Remove x`,
  `AdaptiveValue.fs:322`) — it does not rely on weak refs alone.

### Mechanism C — Level-based topological ordering inside a transaction

Each node has `Level : int` = `1 + max(input levels)`. The transaction
(`Core/Transaction.fs`, `Transaction.Commit`) processes the marked set **in ascending
`Level` order** using a duplicate-aware priority queue (`Utilities/PriorityQueue.fs`;
each object enqueued at most once, entries recycled through a free-list).

Why this matters:
- A node is only marked *after* all its inputs have been processed → no "read from the
  future."
- During marking, callbacks (`Mark`, `InputChanged`, `AllInputsProcessed`) fire per node
  and are *allowed* to evaluate nodes (`Transaction.fs:183, 212-217, 253`). Evaluation can
  raise `LevelChangedException(newLevel)` if a dynamic dependency raised a level; the
  transaction catches it, bumps the level, and **re-enqueues** the node
  (`Transaction.fs:228-234`).
- A second, independent level-drift mechanism: if a node's `Level` changed since it was
  enqueued, it is re-enqueued at its new level at dequeue time *without* being marked
  (`Transaction.fs:206-207`).
- `EvaluateAlways` enforces the invariant: pulling from a node whose `Level` exceeds the
  transaction's current level raises `LevelChangedException`
  (`AdaptiveObject.fs:163-170`; checking can be disabled via `UnsafePerformLevelChecking`).

This is what makes `bind` (dynamic dependencies) correct without re-topo-sorting the whole
graph.

**Threading model.** There is **no global commit lock**: `Commit` locks each object
individually (`EnterWrite`/`ExitWrite`, `Transaction.fs:190, 237`), holding at most one
lock at a time, with an `IsOutdatedCaller` check for objects concurrently locked by another
thread's commit. Per-object `Monitor` + thread-static transactions are the entire
concurrency story.

### Mechanism D — Delta streams for collections (Traceable + Reader + History)

For single values (`aval`), Mechanisms A–C suffice: cache the scalar. For *collections*
(`aset`/`alist`/`amap`) the same dirty-flag trick is lifted to the **element** level. Three
abstractions compose:

1. **`Traceable<'State, 'Delta>`** (`Traceable/Traceable.fs:17-40`) — a record:
   - `tempty` — empty state
   - `tmonoid : { mempty; mappend; misEmpty }` — **deltas form a monoid**
   - `tapplyDelta : State -> Delta -> State * Delta` — apply a delta, return the *new state*
     **and the effective/reduced delta** (no-op operations stripped)
   - `tcomputeDelta : State -> State -> Delta` — diff two states
   - `tsize`, `tprune`
   - Concrete instances: `Traceable/Instances.fs` (HashSet, HashMap, IndexList).

2. **`IOpReader<'Delta>`** (`Traceable/History.fs:8-12`) — the incremental interface:
   ```
   GetChanges(token) : Delta   // "what changed since I last asked"
   ```
   A reader *is* an `IAdaptiveObject`: it has `OutOfDate`, it gets marked, it participates
   in Mechanisms A–C. `AbstractReader<'Delta>.GetChanges` calls `Compute` when dirty and
   folds the result through `Apply` (`History.fs:39-45`) — identity for the base class;
   `AbstractReader<'State,'Delta>` overrides `Apply` with `tapplyDelta` on the reader's own
   state (`History.fs:58-61`).

3. **`History<'State,'Delta>`** (`Traceable/History.fs`) — the checkpoint machinery that
   lets multiple readers at different cadences share one delta stream:
   - A linked list of `RelevantNode` (each = accumulated delta since previous node + a base
     state + refcount); strong `Next` links, **weak `Prev` links**
     (`History.fs:114-123`).
   - A reader checks out a node; on next `Read`, History walks forward merging deltas and
     ref-counts nodes so they can be reclaimed.
   - The `last` pointer is a weak ref: when it dies, appended ops are silently discarded
     (`History.fs:210-223`). A reader whose node was reclaimed is rebuilt via
     `tcomputeDelta oldState state` (`History.fs:381-385`) — the sole reason `tcomputeDelta`
     must exist for cset-backed graphs.
   - **Effective-delta reduction happens at the source:** `History.append` runs
     `tapplyDelta` *before* storing; ops that reduce to empty never mark readers
     (`History.fs:201-231`). Add-of-existing / Rem-of-missing produces zero downstream work.

**How a combinator is actually built** (concrete: `AdaptiveHashSet/AdaptiveHashSet.fs`,
`MapReader`, lines 407–428):
```
type MapReader<'A,'B>(input, mapping) =
    inherit AbstractReader<HashSetDelta<'B>>(empty)
    let cache = Cache mapping            // ref-counted A->B memo
    let reader = input.GetReader()       // inner reader
    override x.Compute(token) =
        reader.GetChanges token          // pull upstream delta
        |> HashSetDelta.map (fun d ->    // transform each SetOperation
            if d.Count =  1 then Add(cache.Invoke d.Value)
            elif d.Count = -1 then Rem(cache.RevokeUnsafe d.Value)
            else ...)
```
A 1000-element set where 1 element changed therefore processes **1 element**, not 1000.
`choose`, `filter`, `collect`, `union`, `intersect`, `bind` all follow this same
*transform-the-delta-stream* shape. `Cache<'A,'B>` (`Utilities/Cache.fs`) is the shared
ref-counted memoizer that keeps the mapping stable (so `Remove`/`Add` cycles don't churn
the output identity) and supports `IDisposable` outputs (`MapUseReader`).

Two structural facts the pipeline view hides:

- **Reader state is a `CountingHashSet`** — per-element reference counts
  (`Traceable/CountingHashSet.fs`). This is what powers `collect`/`union`: a dying inner
  set removes all its contributions via `removeAll`, and an output stays alive while any
  parent contributes it.
- **History flattening:** every `aset` wraps its reader in a `History`
  (`AdaptiveHashSet.fs:349-355`), but combinators like `ASet.map` check `set.History` and
  attach a mapping reader directly to the *input's* History when present
  (`AdaptiveHashSet.fs:1298-1304`), bypassing intermediate Histories. Logically a pipeline
  of readers; structurally, readers share checkpoints.

---

## Part 2 — Composability (why combinators compose for free)

Three design choices make composition automatic:

1. **One uniform interface.** `cval`, `aval`, `aset`/`alist`/`amap` readers, `History`,
   even callback handles (`MultiCallbackObject`) all implement `IAdaptiveObject`. Marking,
   locking, leveling, and edge-formation are implemented **once** in `AdaptiveObject` /
   `EvaluateAlways`.

2. **The token carries the caller.** Because `AdaptiveToken` is a struct passed to every
   `GetValue`, *any* read — including reads hidden inside another combinator's `Compute` —
   registers an edge. You cannot read an adaptive value without establishing the
   dependency. This is why `map (map (map x))` "just works" with no subscription
   bookkeeping. (`AVal.force` reads with `AdaptiveToken.Top` — caller-less, registering no
   edge.)

3. **Combinators are stateless transformers over delta streams** (collections) or thin
   cached computations (`aval`). They never reach across the abstraction: a `MapReader`
   only ever talks to its inner reader via `GetChanges`. So `aset |> ASet.map f |>
   ASet.filter g |> ASet.collect h` is just a pipeline of readers, each pulling deltas from
   the previous.

The flip side: composability is **pull-based by default** — push marking only propagates
along edges that exist, and edges only exist after a read has registered them. FDA's
observation bridge is `AddMarkingCallback` / `MultiCallbackObject` (`Core/Callbacks.fs`)
plus `EvaluationCallbackExtensions.AddCallback`: a marking callback that defers evaluation
to a **transaction finalizer** (`Transaction.AddFinalizer`), equality-checks the result,
then fires.

---

## Part 3 — Authoring patterns

Two layers, two recipes.

### Single value (`aval`)
- Subclass `AVal.AbstractVal<'T>` and implement `Compute(token) : 'T`, or use
  `AVal.custom` (wraps a function in the sealed `CustomVal`, `AdaptiveValue.fs:404-408`).
  Inside `Compute`, read dependencies with `dep.GetValue token`.
- That's it. Caching, locking, versioning, edge registration are inherited.
- For fixed arity there are pre-baked nodes (`MapVal`, `Map2Val`, `Map3Val`, `BindVal`,
  `Bind2Val`, `Bind3Val`) with **struct** caches (`ValueOption<struct(...)>`) so the
  unchanged-input hot path allocates nothing (`AdaptiveValue/AdaptiveValue.fs:238-397`).
- `BindVal` optimization worth copying: an `inputDirty` flag (set via `InputChangedObject`)
  lets it skip re-running the `mapping` function when only the *inner* value changed
  (`AdaptiveValue.fs:301-325`).

### Collection (`aset` / `alist` / `amap`)
- Implement an `IOpReader<'Delta>` (subclass `AbstractReader<'Delta>` or
  `AbstractReader<'State,'Delta>`).
- In `Compute(token)`, pull `inner.GetChanges token`, transform element-operations, return
  a delta.
- Wrap with `ofReader` (`AdaptiveHashSet.fs:1252`) to get the public `aset<'T>`.
- Use `Cache<'A,'B>` for any per-element mapping so add/remove stays balanced.
- For reductions (`ASet.fold`, `sum`), see `AdaptiveReduction`
  (`AdaptiveValue/AdaptiveReduction.fs:3-10`): a struct record `{ seed; add; sub; view }`.
  The `sub` returns `ValueOption` to signal "I can't reverse this, please recompute from
  scratch" — a clean fallback protocol. `ReduceValue` additionally recomputes from scratch
  heuristically when the delta batch is large relative to the state
  (`AdaptiveHashSet.fs:61-77`). (`ASet.contains` does **not** use `AdaptiveReduction` — it
  keeps a simple integer refcount for its key.)

The `ConstantObject` base (`Core/AdaptiveObject.fs:211-237`) is an important optimization:
nodes known to never change serve a static `EmptyOutputSet`, skip marking and (via
`ConstantVal.GetValue` bypassing `EvaluateAlways`) skip locking — they are quasi-free.
Every combinator special-cases `if value.IsConstant then ConstantVal.Lazy …` to collapse
constant subgraphs at construction time (`AdaptiveValue.fs:422-426, 448-484`).

---

## Part 4 — Allocation sources (HARD FACTS — where garbage comes from)

This is the part that matters for a game-loop target. Allocations are grouped by *when*
they happen.

### 4.1 Per-node, at graph construction (pay once, but pay for *every* node)

| Alloc | FDA location | Avoidable? |
|---|---|---|
| The node object itself | every combinator | **No** (structural). Minimize node *count* by fusing combinators. |
| `WeakOutputSet` field | `AdaptiveObject` always does `let outputs = WeakOutputSet()` (`AdaptiveObject.fs:29`) | **Yes** — allocate lazily / use a sentinel `EmptyOutputSet` until first output added (FDA has `EmptyOutputSet` but uses it only for `ConstantObject`/`MultiCallbackObject`). |
| `WeakReference<IAdaptiveObject>` | lazy on `Weak` access (`AdaptiveObject.fs:40-50`) | **Yes** — defer; many nodes never need it. |
| `OptimizedClosures.FSharpFunc.Adapt(...)` | one per `Map2/3`, `Bind2/3` constructor (`AdaptiveValue.fs:259, 278, 332, 369`) | Mostly **no**; one-time closure→delegate adapter. Avoid by accepting already-curried `Func`. |

### 4.2 Per-evaluation, hot path (this is what a game loop hits)

- `Monitor.Enter/Exit` on every `GetValue` that routes through `EvaluateAlways`
  (`AdaptiveObject.fs:145`). **Zero allocation *unless*** contention forces the CLR to
  inflate the thin lock into a fat `SyncBlock` — then it's a heap alloc. In a
  single-threaded game loop this is free; with worker threads it is a real cost.
  (`ConstantVal.GetValue` bypasses `EvaluateAlways` — no lock at all.)
- `AdaptiveToken.WithCaller` is a struct — **zero alloc**. ✓
- `MapVal/Map2Val/Map3Val` caches are `ValueOption<struct(...)>` mutable fields. When
  inputs are unchanged the hot path does: lock → check `OutOfDate` → `cheapEqual` per input
  field (IL-emitted shallow comparer, `ShallowEquality.fs`) → return. **Zero heap
  allocation.** ✓ This is the design to copy verbatim.
- `AbstractVal.valueCache` is a mutable field — **zero alloc** on cache hit. ✓

**Conclusion for 4.2:** FDA's *scalar* hot path is already near allocation-free. The wins
available are (a) drop `Monitor` for a single-threaded mode, (b) avoid the `WeakOutputSet`
field until needed.

### 4.3 Per-transaction (marking)

| Alloc | Where | Notes |
|---|---|---|
| `Transaction` object | `transact` (`Transaction.fs:311`) | 1 per transaction. Not pooled. **Poolable.** |
| `TransactQueueEntry<'V>` | priority queue (`Utilities/PriorityQueue.fs:44`) | A class, but **recycled through a free-list** (`PriorityQueue.fs:138-153`) and each object enqueues at most once — allocation is bounded by *peak queue occupancy*, not per-mark. A level-bucketed queue would eliminate even that. |
| `outputs : ref<IAdaptiveObject[]>` | reused within a transaction, `ref (Array.zeroCreate 8)` (`Transaction.fs:108`) | **Already reused.** ✓ (Dies with the unpooled Transaction, though.) |

### 4.4 Per-collection-delta — **THE DOMINANT allocation source**

This is where FDA spends most of its garbage. Every delta operation builds persistent
structures:

| Alloc | Why |
|---|---|
| `HashSetDelta<'T>` wraps `HashMap<'T,int>` | `Datastructures/HashSetDelta.fs:13` (itself a struct). Even a 1-element delta builds a trie. |
| `HashMap<'K,'V>` / `HashSet<'T>` are **custom persistent tries** | `Datastructures/HashCollections.fs` (4500 lines) — technically a big-endian binary Patricia trie keyed by hash, not a 32-way HAMT. Each "modify" allocates a new root-to-leaf **path** (structural sharing). Unchanged siblings are reused, but the path is fresh. |
| `ElementOperation<'T>`, `SetOperation<'T>` | structs ✓ (`Datastructures/Operations.fs`) — these are fine. |
| `Cache<'A,'B>` | `Dictionary<'A, struct('B * ref<int>)>` (`Utilities/Cache.fs:23`). The `ref<int>` is a **heap cell per cached element**, plus the Dictionary entry slot. Grows with distinct elements seen. |
| Reader objects | one per combinator per reader-chain. Structural. |

(Iteration is fine: `HashSetDelta.GetEnumerator` returns a **struct** enumerator —
`HashCollections.fs:3177`; boxing only via the non-generic `IEnumerable` interfaces.)

The persistent-trie delta is FDA's biggest allocation tax *by design*: it buys structural
sharing, cheap snapshots, and trie-level `applyDelta`/`computeDelta`. For a
GUI/declarative workload that is a great trade. **For a game loop / physics engine it is
the wrong trade** — those workloads mutate a bounded working set every frame and want
flat, pooled buffers, not persistent tries.

### 4.5 Lambda/closure allocations (subtle)

FDA leans heavily on `OptimizedClosures.FSharpFunc.Adapt` and `static let setOp = ...
Adapt(...)` patterns (e.g. `HashSetDelta.fs:15-16`) to avoid re-allocating closures. A
reimplementation must be equally disciplined:
- Mark combinators' user functions `[<InlineIfLambda>]` where possible.
- Hoist per-combinator adapters to `static` fields.
- Prefer `struct` delegates / `Func` stored in fields over F# closures captured in loops.

---

## Part 5 — Custom collections vs BCL (the decision)

### Why FDA wrote ~9,900 lines of custom collections

| File | Lines | Purpose |
|---|---|---|
| `HashCollections.fs` | 4,500 | persistent Patricia-trie `HashMap`/`HashSet` |
| `MapExt.fs` | 3,908 | persistent balanced tree (ordered map) |
| `IndexList.fs` | 1,479 | index-stable persistent list |
| `IndexListDelta.fs`, `HashMapDelta.fs`, `HashSetDelta.fs` | 736 | delta types + monoids |

They exist for **four reasons**, none of which the BCL satisfies simultaneously:

1. **Persistent + structural sharing.** `State` must be cheaply snapshot-able (so a reader
   can hold an old version) **and** cheaply differentiable.
   `System.Collections.Generic.Dictionary` is mutable; snapshotting is `O(n)`.
2. **Trie-level delta operations.** `applyDelta` walks the trie and emits a *reduced* delta
   (e.g. "Add X then Remove X" → nothing). BCL collections cannot diff themselves
   structurally.
3. **Reference-counted deltas.** `HashSetDelta<'T>` = `HashMap<'T,int>`; counts enable
   multi-set/union semantics and idempotent combine. No BCL type does this.
4. **Baked-in custom equality** via a stored `IEqualityComparer`.

### Can a zero-allocation reimplementation use the BCL? **Yes, with a different delta model.**

The cleanest design for a game loop is **mutable BCL state + pooled flat-array deltas**:

- **State** = `System.Collections.Generic.HashSet<'T>` / `Dictionary<'K,'V>`, mutated in
  place. No persistence, no snapshot sharing. Acceptable because game-loop readers re-pull
  every frame.
- **Delta** = a flat `SetOperation<'T>[]` (or `Delta<T>` struct wrapping `added:
  ReadOnlySpan`, `removed: ReadOnlySpan`) **rented from `ArrayPool<T>.Shared`**. Combine
  via in-place merge into a scratch buffer. Monoid `mappend` = sort + merge two arrays;
  `mempty` = `Array.Empty()`.
- **applyDelta** = mutate the `HashSet`/`Dictionary` directly from the array. Return the
  *effective* delta by recording which ops actually changed state (filter no-ops into a
  second pooled buffer) — this preserves FDA's source-side reduction, so no-op changes
  never mark readers.
- **computeDelta** = diff old vs new BCL collection into a pooled buffer (O(n) but
  allocation-free with pooling).

**What you lose vs FDA:**
- No cheap snapshot sharing → readers cannot lag behind cheaply. Fine for a
  frame-synchronous game loop (all readers pull every frame); **bad** for an
  async/event-driven UI where a reader might be 5 frames stale.
- No structural sharing of large unchanged collections → re-diffs are O(n) if you ever need
  `computeDelta`. Mitigate the way FDA's `cset` does: the source applies ops through
  `tapplyDelta` as they arrive (`History.Perform`), so the effective delta is computed
  incrementally and `computeDelta` is only a recovery path for reclaimed readers.

**What you keep:**
- The entire Mechanisms A–C (dirty flag, lazy edges, levels) — independent of collections.
- The Traceable/Reader shape — just plug in flat-array deltas instead of trie deltas.
- The `Cache<'A,'B>` ref-counted memo (but back it with a pooled dictionary and inline the
  count into a small struct wrapper instead of `ref<int>`).

### Recommended split for the reimplementation

| Concern | Use BCL? | Notes |
|---|---|---|
| `cval<'T>` / `aval<'T>` | Yes (nothing needed) | Keep FDA's struct-cache `Map/Map2/Map3` design exactly. It is already allocation-free on the hot path. |
| Marking / transaction / levels | Custom, but small | Replace the entry-recycling priority queue with a level-bucketed queue (array per level). ~150 lines. Keep the thread-static `transact` contract (marking outside a transaction is an error). |
| `cset`/`cmap`/`clist` STATE | **BCL** (`HashSet`/`Dictionary`/`List`) | Mutate in place. |
| DELTAS | **Pooled flat arrays** | `ArrayPool<SetOperation<'T>>`. Struct `Delta<T>` wrapper. |
| Readers for `map`/`filter`/`choose`/`collect` | Custom (small) | Same shape as FDA's readers; emit flat-array deltas. `collect`/`union` still need per-element refcounts (the `CountingHashSet` role). |
| `Cache<'A,'B>` | Custom (small) | Pooled dictionary + inline refcount. |
| Ordered map / persistent snapshot | Only if you need UI-style stale readers | Otherwise **skip entirely** — ~3,900 lines you don't need for a game loop. |
| History / version checkpointing | **Skip** unless you need multi-cadence readers | Game loops are single-cadence. But note you also lose History's source-side effective-delta dedup and its `computeDelta` recovery path — reproduce the former in your `cset`'s `tapplyDelta`, drop the latter. |

---

## Part 6 — Pitfalls to avoid (lessons encoded in FDA's source)

These are non-obvious correctness traps FDA solved; a reimplementation must solve them too.

1. **Edges must be re-established on every recompute — and old dynamic edges removed.**
   FDA re-reads all deps every time it recomputes (`EvaluateAlways` re-runs `f`). If you
   cache deps and skip re-reading, `bind` breaks (the inner value changes but you never
   registered on the new inner). FDA also *explicitly* removes the old inner edge in
   `BindVal` rather than waiting for the GC. → Keep "recompute = re-read all deps", and
   unregister dynamic edges eagerly.

2. **Marking must be level-ordered to avoid reading the future.** If you mark a parent
   before its child is settled, a `GetValue` during a callback can observe an inconsistent
   state. FDA's `LevelChangedException` + re-enqueue (plus dequeue-time level-drift
   re-enqueue) is the escape hatch for dynamic deps. → Either replicate level-ordering, or
   forbid evaluation during marking (simpler, less general).

3. **`OutOfDate` must be checked *under the lock* that guards the cache.** Otherwise a
   reader can see a stale cache while a writer is mid-update. FDA does `Monitor.Enter` then
   checks `OutOfDate` inside `EvaluateAlways`. → For single-threaded mode you can drop the
   lock but must keep the ordering.

4. **Push invalidation only works after an edge exists.** A node nobody has read yet has an
   empty `Outputs` set, so marking it does nothing downstream. First read materializes the
   graph. → If you need eager updates (physics), you must "observe" roots up-front;
   laziness and eagerness are mutually exclusive defaults. FDA's marking callbacks +
   transaction finalizers (`AddCallback`) are the bridge.

5. **Marking an observed value outside a transaction is an error in FDA.** `MarkOutdated`
   throws when the object has outputs and no `transact` is current. → Decide your contract
   up front: implicit auto-transactions per change, or an explicit `transact` requirement.
   Silent marking without a transaction is how inconsistent propagation bugs are born.

6. **Reductions need a recompute-from-scratch fallback.** `AdaptiveReduction.sub` returns
   `ValueOption` — `ValueNone` means "I can't reverse this add, please rebuild." Without
   this, e.g. `ASet.map f |> ASet.fold (+)` over a set where the same key is removed then
   re-added with a different value double-counts. → Any incremental reduction needs the
   same protocol.

7. **Effective vs raw deltas.** `tapplyDelta` returns `(newState, effectiveDelta)` where
   `effectiveDelta` strips no-ops (Set X to X, Remove absent key), and `History` applies it
   *before* storing so phantoms never mark readers. Downstream readers must receive
   *effective* deltas or they churn on phantoms. → Always filter deltas through the state
   application, at the source.

8. **`WeakOutputSet` GC-safety has a real cost.** Resolving weak refs on every `Consume`
   and the cleanup heuristic are non-trivial. For a bounded game-loop graph, **strong edges
   + explicit disposal** (the AdaptiveSlop `IObservation` route) is simpler and faster, at
   the cost of requiring the user to dispose.

---

## TL;DR for the reimplementation

- **Keep verbatim:** dirty-flag dual (Mechanism A, including "stop at already-dirty"), lazy
  edge formation via a caller-carrying token (B), level-ordered marking with
  re-enqueue-on-drift (C), struct-cached `Map/Map2/Map3` nodes, `BindVal`'s eager
  edge-swap + `inputDirty` skip, the Traceable/Reader/Cache *shape* for collections,
  `AdaptiveReduction`-style fallback protocol, constant-subgraph collapsing.
- **Change:** replace persistent-trie deltas with **pooled flat-array deltas**; replace
  persistent collection state with **mutable BCL** `HashSet`/`Dictionary` (keeping
  per-element refcounts for `collect`/`union`); drop `History` and ordered-`MapExt` unless
  you need stale-async readers — but keep source-side effective-delta reduction.
- **Add:** single-threaded mode that drops `Monitor` (fat-lock inflation is the only
  per-eval allocation risk); a level-bucketed mark queue (kills even the recycled
  `TransactQueueEntry` allocs); lazy `Outputs` allocation (kills the per-node
  `WeakOutputSet` alloc); strong-edge observation model with explicit `Dispose` (kills
  weak-ref resolution cost); pooled `Transaction`.
- **The single sentence that defines "incremental":** *recompute only nodes that are both
  dirty and read, exactly once each*; for collections, lift "recompute" to "process only
  changed elements."
