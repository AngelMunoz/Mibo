# mapA Family Design — per-element adaptive nodes (Tier 2)

Status: **implemented (2026-08-05)**. Phases 1-4 landed on
`feat/adaptive-extensions` (044c157 ASet, a6706a8 AMap, b4497b4 AList,
089542e v2 reductions + i-variants, b25e101 benchmark). FsCheck property
suite still pending (added last, per plan).

## 1. Agreed extension points (2026-08-05, record)

Agreed with the user on 2026-08-05 (gap discussion). These are the extensions
the core will offer. They are separate work items from the `*A` family of this
document; this section records the agreement.

### 1.1 `ofExternal` — external sources via invalidate handles (new)

- `AVal.ofExternal ([<InlineIfLambda>] read: unit -> 'T) : aval<'T> * (unit -> unit)`
- `ASet.ofExternal ([<InlineIfLambda>] snapshot: unit -> IReadOnlySet<'T>) : aset<'T> * (unit -> unit)`
- `AMap.ofExternal ([<InlineIfLambda>] snapshot: unit -> IReadOnlyDictionary<'K, 'V>) : amap<'K, 'V> * (unit -> unit)`
- `AList.ofExternal ([<InlineIfLambda>] snapshot: unit -> IReadOnlyList<'T>) : alist<'T> * (unit -> unit)`

All four are `let inline` combinators; the read/snapshot lambda is
`[<InlineIfLambda>]` (stored on the node, matching the `FilterMapListNode`
constructor convention).

Semantics:

- The user provides a read/snapshot function and receives an `invalidate` handle.
- `invalidate` is O(1) at call time and deferred (no evaluation during marking,
  invariant 3). It is thread-safe via the post ring (the `cval.Post` pattern).
- On the next read, the library re-runs the user function once, diffs against
  the previous snapshot, and pushes deltas through its own machinery.
- Not invalidated → zero cost: no re-read, no diff, no allocation.

Safety contract (why users cannot blow up performance):

- The user never marks. No public `IAdaptiveNode`, no edge manipulation, no
  version writes. The `invalidate` handle is the user-facing form of the same
  mark path the `*A` nodes use internally (`MarkDirty` flag, §5).
- The only user-controlled cost is the cost of their own function, run at most
  once per invalidate.
- A spurious `invalidate` costs one recompute of that subtree on the next read.
  Bounded, visible, self-inflicted.

### 1.2 `observeWeak` (new)

- Weak variant of `observe` on AVal/ASet/AMap/AList. Fixes the
  observer-retention leak. Cost: one weakref dereference per delivery.

### 1.3 `AList.custom` (new)

- `let inline custom ([<InlineIfLambda>] compute: IReadOnlyList<'T> -> ListDeltaBuilder<'T> -> unit) : alist<'T>`
  — parity with the existing `ASet.custom`/`AMap.custom`. `ListDelta` already
  has the public `Insert`/`Remove`/`Update` helpers (Shared.fs).

### 1.4 Not now (recorded)

- Computation expressions (`aval { }`, `aset { }`, `amap { }`, `alist { }`) —
  deferred; the last item the project would ever do.

### 1.5 Explicitly not offered

- Public node subclassing, raw mark APIs, user-owned delta application
  (`IOpReader`-style), level/priority controls. These are the FDA extension
  points that create the perf blowups; the offering above covers the same use
  cases with the library owning the machinery.

## 2. Scope

- v1: `ASet.mapA` / `chooseA` / `filterA`, `AMap.mapA` / `chooseA` / `filterA`,
  `AList.mapA` / `chooseA` / `filterA`.
- v2 (composition, no new nodes): `countByA`, `sumByA`, `averageByA`,
  `existsA`, `forallA`, `reduceByA` = `mapA` + existing reduction nodes.
- v2: list `mapiA` / `chooseiA` / `filteriA` (the mapping receives the input
  position as `int`; the cache already knows it — free).
- Out of scope: `Index`, FDA per-element identity, computation expressions.

## 3. Unified node shape

One node type per collection kind; the three API functions are thin wrappers.

```fsharp
// set/map:  mapping : [<InlineIfLambda>] 'K -> 'T -> aval<'U voption>   ('K = unit for sets)
// list:     mapping : [<InlineIfLambda>] 'T -> aval<'U voption>
```

The mapping is a `[<InlineIfLambda>]` constructor parameter on the node
(matching `FilterMapListNode`/`CustomSetNode`); the API wrappers are
`let inline`.

- `mapA` wraps the user mapping in `ValueSome`.
- `filterA` wraps the predicate aval in `aval<bool>` → `aval<bool voption>`-style
  adapter (present = keep).
- `chooseA` is the raw form.

The node has two input kinds, mirroring `FilterMapListNode`
(Collections/ListNodes.fs):

1. **Source collection** — sink + journal + source-version snapshot (existing
   pattern, unchanged).
2. **Per-element avals** — a cache + version scan (new).

## 4. The cache

| Kind | Cache | Notes |
|---|---|---|
| set | `Dictionary<'T, struct(aval<'U> * int64)>` | keyed by element; pre-sized at load; removed entries leave reusable slots (no shrink) |
| map | `Dictionary<'K, struct(aval<'U> * int64)>` | keyed by key |
| list | `ResizeArray<struct(aval<'U> * int64)>` | indexed by **input position**, parallel to the source; covers non-surviving elements (chooseA/filterA) |

List semantics (Path A, positional):

- **Insert at p** — `cache.Insert(p, entry)`; entries after p shift with the
  element they belong to (memmove, same as the output array). The aval travels
  with its element: **mapping does not re-run** for shifted elements.
- **Remove at p** — `cache.RemoveAt(p)`.
- **Update at p** — replace `cache[p]` with the new element's aval (old aval
  unregistered, new one registered).

This is the positional-design equivalent of element identity: "the element at
input position p" is exactly what an `Update` op targets, so the cache stays
aligned without an `Index` type. Duplicates get one aval per occurrence — the
same as FDA (which caches per `Index`), so per-element mapping state stays
correct.

## 5. Drain protocol

1. **Journal first** (structural changes), with cache maintenance per op:
   - Insert — `mapping` → force the aval → contribution; insert cache entry;
     register with the aval (§5).
   - Remove — unregister; drop cache entry; remove contribution (set: counting,
     §7).
   - Update — unregister old aval; `mapping` new value; replace cache entry;
     register; apply contribution.
2. **Element scan** (only when the dirty gate fires, §5) — for every cache
   entry: read `aval.Version`; if it differs from the stored version, force
   `GetValue()` and apply the contribution change:
   - list: `LowerBound(p)` + `Contains` → `Update` / `Insert` / `Remove` at the
     output position (the existing filter/choose translation, extracted into a
     shared helper);
   - set/map: remove the old value / add the new value (counting).
   Store the new version. Skip delta emission when the value is unchanged
   (a version can bump to an equal value — `AdaptiveNode.Recompute` bumps
   unconditionally).
3. **Emit** — if the out buffer is non-empty: bump version, push to sinks,
   `MarkFrom(edges)` (existing pattern).
4. **Reentrant-write guard** — capture `writeGeneration` at drain start; if it
   moved mid-drain, keep the element-dirty flag set so the next read rescans
   (the scalar `checkedGen` pattern, Library.fs).

## 6. Dirty detection (the O(1) fast path)

Read path:

```
if journal empty
&& source.Version = depVersion
&& not elementDirty
&& (node fully registered  OR  writeGeneration = lastDrainWriteGen)
then return view                  // O(1), zero allocation
else drain                        // journal + scan
```

- **elementDirty flag** — set by the node's `MarkDirty`. The node registers
  itself with every cached aval (`IEdgeTarget.AddEdge(this, -1)`, the
  `Observation` pattern): on cache insert, on first edge
  (`OnFirstParent` → register all), on evict/dispose (`OnLastParent`,
  disposal walks the cache). An element aval's underlying write then marks the
  chain: `cval.Apply → MarkFrom(cval.edges) → aval.MarkDirty → PushDirty →
  mapA.MarkDirty` (Library.fs:935-960). Unrelated writes mark other chains and
  never touch this node → steady-state reads stay O(1).
- **Generation gate** — when the node is unobserved, or any cached aval does
  not implement `IEdgeTarget` (registration incomplete): fall back to
  `writeGeneration ≠ lastDrainWriteGen → scan`. The write generation moves on
  every applied write, so correctness holds without registration; the cost is
  one O(cache) version-field scan per generation.
- **MarkDirty body** — set the flag + `PushDirty(edges)` (mirror
  `AdaptiveNode.MarkDirty`; no `MarkFrom`, no generation bump — the source
  write already moved it).

Observed element avals get `DirtyState.Clean` promotion (their `Version` reads
become one flag check), which is what keeps the scan cheap: O(cache) field
reads, zero allocation, no dependency walks.

## 7. Correctness

- Invariant 2 (recompute = re-read all deps): the scan re-reads every element
  aval's version, and forced avals re-read their own deps. ✓
- Invariant 1 (pull-lazy): nothing recomputes at write time; the scan runs on
  read only. ✓
- Invariant 3 (no evaluation during marking): `MarkDirty` only flags and
  pushes; the scan is on the read path. ✓
- Self-healing: a reentrant write mid-scan keeps the flag set; the next read
  rescans and re-reads all versions (the scalar `checkedGen` contract).
- Journal compaction keeps the "ops appended during processing survive"
  contract (`FilterMapListNode` finally block).

## 8. Counting semantics (sets)

`ASet.mapA` can produce duplicate mapped values. The set node state must be
reference-counted: removing one of two equal contributions keeps the value in
the output until the last occurrence leaves. Precedent: `MapSetNode`
(SetNodes.fs:44) — "duplicate outputs share one reference count" via
`SetNodeState`. The mapA set node reuses that state.

## 9. Allocation story (invariant 5)

- Clean read: O(1), 0 B.
- Scan with no changes: O(cache) version field reads, 0 B.
- Scan with changes: reused out buffer; pre-sized caches; dictionary slot reuse;
  no per-change allocation other than the user's mapping code.
- New permanent allocation test (mirror of the AList one): N-element `mapA`,
  one element-aval write + drain + delivery = 0 B; a structural op in the same
  batch = 0 B.

## 10. API surface

All combinators are `let inline`; every function-valued parameter is
`[<InlineIfLambda>]` (the codebase convention, AGENTS.md).

```fsharp
// ASet
let inline mapA    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)        (set: aset<'T>) : aset<'U>
let inline chooseA ([<InlineIfLambda>] mapping: 'T -> aval<'U option>) (set: aset<'T>) : aset<'U>
let inline filterA ([<InlineIfLambda>] predicate: 'T -> aval<bool>)    (set: aset<'T>) : aset<'T>
// AMap
let inline mapA    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U>)        (mapValue: amap<'K, 'V>) : amap<'K, 'U>
let inline chooseA ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U option>) (mapValue: amap<'K, 'V>) : amap<'K, 'U>
let inline filterA ([<InlineIfLambda>] predicate: 'K -> 'V -> aval<bool>)    (mapValue: amap<'K, 'V>) : amap<'K, 'V>
// AList
let inline mapA    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)        (list: alist<'T>) : alist<'U>
let inline chooseA ([<InlineIfLambda>] mapping: 'T -> aval<'U option>) (list: alist<'T>) : alist<'U>
let inline filterA ([<InlineIfLambda>] predicate: 'T -> aval<bool>)    (list: alist<'T>) : alist<'T>
// v2 (phase 4) — same convention; FDA argument order
let inline mapiA      ([<InlineIfLambda>] mapping: int -> 'T -> aval<'U>)        (list: alist<'T>) : alist<'U>
let inline chooseiA   ([<InlineIfLambda>] mapping: int -> 'T -> aval<'U option>) (list: alist<'T>) : alist<'U>
let inline filteriA   ([<InlineIfLambda>] predicate: int -> 'T -> aval<bool>)    (list: alist<'T>) : alist<'T>
let inline countByA   ([<InlineIfLambda>] predicate: 'T -> aval<bool>)        (set: aset<'T>) : aval<int>
let inline sumByA     ([<InlineIfLambda>] mapping: 'T -> aval<'U>)            (set: aset<'T>) : aval<'U>
let inline averageByA ([<InlineIfLambda>] mapping: 'T -> aval<'U>)            (set: aset<'T>) : aval<'U>
let inline existsA    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)        (set: aset<'T>) : aval<bool>
let inline forallA    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)        (set: aset<'T>) : aval<bool>
let inline reduceByA  ([<InlineIfLambda>] mapping: 'T -> aval<'U>) (reduction: AdaptiveReduction<'U, 'S, 'V>) (set: aset<'T>) : aval<'V>
```

FDA parity note: FDA's `mapA` passes `Index` to the mapping on lists; ours
passes nothing in v1 (`mapiA` adds the `int` position in v2).

Implementation notes (deviations found while building):

- `reduceByA`/`sumByA`/`averageByA` map to distinct pairs `struct (x, v)`
  then project the value side in the reduction. A plain `mapA` output is a
  DEDUPLICATED set: `sumByA`/`countByA` over a bool/int-mapped set
  undercounted duplicates (found by testing). The pair preserves
  multiplicity; the mapped value must be equality-comparable (the mapA
  constraint).
- `countByA`/`existsA`/`forallA` are `filterA` + `count` compositions
  (element-preserving).
- `mapiA` positions are MAPPING-TIME positions: shifted elements keep their
  aval (the positional equivalent of FDA's stable `Index`, §4).

## 11. Phasing

1. **Phase 1 — ASet** (validates the cache + scan + counting on the simplest
   shape): node + wrappers + tests.
2. **Phase 2 — AMap** (same node shape, keyed).
3. **Phase 3 — AList** (positional cache, tail fixup, choose survival).
4. **Phase 4 — reductions by composition** (`countByA`, `sumByA`, `averageByA`,
   `existsA`, `forallA`, `reduceByA`), then `mapiA`/`chooseiA`/`filteriA`.

## 12. Tests

Testing rule: port FDA's correctness tests and write new tests at the public
API level only. Never test node types or internals — test that `AList.mapA`
behaves, not the node under it.

FDA test sources (`E:\FSharp.Data.Adaptive\src\Test\FSharp.Data.Adaptive.Tests\`):

- `AList.fs` — `[AList] mapA` (:159), `[AList] chooseA` (:201),
  `[AList] filterA` (:1098), `[AList] mapA inner change` (:1133): scripted
  scenarios — writes to shared cvls, structural edits, survival flips — each
  step asserts the full list state.
- `ASet.fs`, `AMap.fs` — the same scenario style for the set/map
  `mapA`/`chooseA`/`filterA` tests.
- The FsCheck property suite — `[AList] reference impl` (AList.fs:12,
  `MaxTest = 500`): random operation sequences are applied to both the real
  implementation and a reference implementation
  (`src/Test/FSharp.Data.Adaptive.Reference/AdaptiveIndexList.fs` etc.), and
  the outputs are compared. This is the model to follow for our property
  tests: generate random edit sequences through the public API, compare
  against a plain (non-adaptive) F# model.

Adaptation notes for the port:

- FDA asserts through `GetReader`/`IndexList`; we assert through `observe` +
  `force`/`toList` (the delta callback can assert the exact op list).
- FDA's `Index`-based lists map to our positional `int` lists (the
  `i`-variants receive the position).
- The permanent allocation test stays (steady-state batch = 0 B); it is the
  only test that touches measurement, not behavior.

## 13. Session handoff — implementation notes (2026-08-05)

For a fresh session: the design above plus these verified code facts.

### 13.1 Session environment (WSL2) — read first

- The agent bash tool runs on WSL2. Windows paths are under `/mnt/e/...`:
  `E:\AdaptiveSlop` = `/mnt/e/AdaptiveSlop`,
  `E:\FSharp.Data.Adaptive` = `/mnt/e/FSharp.Data.Adaptive` (the FDA
  reference source).
- dotnet is installed at `~/.dotnet` but is NOT on PATH in a fresh shell.
  Run `source ~/.bashrc` first (it exports `DOTNET_ROOT` and PATH), then
  verify with `dotnet --version`.

### 13.2 File conventions

- Nodes follow the struct-state + class-wrapper pattern: a struct state
  (`SetNodeState`, Shared.fs:441; `MapNodeState`, Shared.fs:~510) holds
  Version/Edges/Sinks/Journal/Out; the node class owns the per-element cache
  and the drain. Shared helpers live in `module internal Collections`
  (Shared.fs): `bufferAppend` (:548), `journalAppendSet/Map/List`
  (:559/:568/:583), `addSink`/`removeSink` (:607/:614), `clearSinks`,
  `pushSetDelta` (:676), `pushMapDelta` (:696), `pushListDelta` (:774).
- Suggested placement: one new file `Collections/ElementNodes.fs` for the
  three node kinds, inserted before `Api.fs` in the fsproj compile order.
- API wrappers go in `Collections/Api.fs` inside the existing ASet/AMap/AList
  modules, mirroring the FDA argument order (mapping first, collection last).

### 13.3 Set counting

- `RefCountedSet<'T>` (Shared.fs:426) = `HashSet<'T>` + `Dictionary<'T, int>`.
- The mapA set node keeps its own element cache
  (`Dictionary<'T, struct(aval<'U> * int64)>`) plus a `RefCountedSet<'U>`
  for the output. Drain emits remove-old/add-new through the RefCountedSet so
  duplicates net correctly (last occurrence removes). Mirror the counting
  discipline of `MapSetNode`'s drain (SetNodes.fs:44).

### 13.4 The node contract

- The node implements: the collection interface (`IAdaptiveSet`/`IAdaptiveMap`/
  `IAdaptiveList`), `IEdgeTarget` (edges for observations), `IAdaptiveNode`
  (`MarkDirty` = set elementDirty flag + `GraphContext.Default.PushDirty(edges)`,
  mirroring `AdaptiveNode.MarkDirty`, Library.fs:935; `SetDepSlot`/
  `OnFirstParent`/`OnLastParent`), and the source sink interface
  (`I*DeltaSink`) for the journal.
- Element-aval registration: on cache insert, when the aval implements
  `IEdgeTarget`, `AddEdge(this :> IAdaptiveNode, -1)`; otherwise mark
  registration incomplete → generation gate. On evict/dispose: `RemoveEdgeAt`.
  On the node's own first edge (`IEdgeTarget.AddEdge` when `edges.Count = 1`):
  register all cached avals; last edge removed: unregister all.
- Registering with an element aval makes the aval observe itself
  (`OnFirstParent` → the aval registers with its deps), which is what routes
  a source write to this node's `MarkDirty`.
- Element-aval versions are read via the public `Version` property (reports
  `version + 1` while dirty — fine; the scan forces the aval after detecting
  a difference).

### 13.5 Scan and reentrancy

- Scan order: list = index order (cache array); set/map = dictionary order
  (irrelevant — set/map deltas are add/remove sets).
- Reentrant writes during the scan: capture `writeGeneration` at drain start;
  if it moved by drain end, keep `elementDirty` set (next read rescans) and
  still emit — self-healing.
- Journal compaction: reuse the `FilterMapListNode` finally-block pattern
  (ops appended during processing survive for the next drain, ListNodes.fs:180).

### 13.6 Allocation test and benchmark

- Mirror the existing AList allocation test in Tests.fs (N-op batch: write +
  drain + delivery = 0 B), plus the new case: one element-aval write through
  a mapA node = 0 B.
- AGENTS.md requires BenchmarkDotNet for perf claims: add a mapA benchmark
  (N-element mapA, one element write per iteration) to
  `benchmarks/AdaptiveSlop.Benchmarks`; the naive `map` + `AVal.force`
  composition is the comparison baseline.

### 13.7 Sequencing notes

- Phase 4 composition (`countByA`/`sumByA`/`averageByA`/`existsA`/`forallA`/
  `reduceByA` on AList) needs `AList.reduce` first — AList has no reduction
  nodes yet (gap item 5.8.1).
- `mapiA`/`chooseiA`/`filteriA` pass the `int` position to the mapping; the
  cache already indexes by position, so the position is free.
- filterA adapter: `('T -> aval<bool>)` wraps into the voption shape
  (present = `ValueSome ()`).

### 13.8 Out of scope here

- The extension points (`ofExternal`, `observeWeak`, `AList.custom`) are
  separate work items — §1 records the agreement only. `AList.custom` reuses
  the public `ListDelta.Insert/Remove/Update` helpers (Shared.fs:357).


