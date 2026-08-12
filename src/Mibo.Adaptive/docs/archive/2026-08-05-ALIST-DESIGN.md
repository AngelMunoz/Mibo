# AdaptiveSlop — AList Design

This document specifies the design of the adaptive list (`AList`/`CList`) for
AdaptiveSlop. It follows the architecture of `docs/archive/2026-08-04-PLAN.md`
(journals, drains, transient views, `force` materialization) and the FDA parity
conventions of `docs/PARITY-FDA.md`.

## Status

- Design decision (2026-08-05): positional operations over ordered journals.
  No `Index` type, no persistent list, no FDA-style order-maintenance structure.
- Prototype scope is §7.

## 1. The problem

A list delta must express three operations at a position:

- insert at position `p` (before the element currently at `p`; `p = count`
  appends),
- remove at position `p`,
- update at position `p`.

Positions shift when an earlier operation changes the length. A derived node
(`filter`, `append`) must translate input positions to output positions. If the
translation is O(n) per operation, the O(changed) work budget of the plan is
violated.

## 2. What FDA does, and why we do not copy it

FDA's `Index` is an order-maintenance label: each element holds a node in a
doubly-linked cycle with a `uint64` tag. Insert takes the midpoint tag; when tag
space runs out, a local neighborhood is relabeled (amortized O(log n)).
`IndexList<'T>` is a persistent `MapExt<Index, 'T>`. `IndexListDelta<'T>` is a
persistent map `MapExt<Index, ElementOperation<'T>>`.

The decisive property: **FDA's delta is a keyed map**. That is why FDA needs the
`Index`: a keyed map needs stable, O(1)-comparable keys. The public API is
therefore full of indices (`insertAt: Index`, `tryGet: Index`, `indexed:
alist<Index * 'T>`, `mapi`/`filteri` receive an `Index`). The `Index` API is a
known source of friction for FDA users.

Our deltas are **ordered operation lists**, not keyed maps. The journals are
already ordered and applied sequentially. Positions can live in the operations.
A stable key is not needed. Therefore:

- no `Index` type,
- no persistent `IndexList`,
- no order-maintenance structure.

## 3. The design

### 3.1 Operation

```fsharp
[<Struct>]
type ListOpKind =
    | Insert = 0
    | Remove = 1
    | Update = 2

[<Struct>]
type ListOp<'T> =
    val Kind: ListOpKind      // + a Source tag for multi-source nodes (§3.4)
    val Position: int
    val Value: 'T
```

Positions are 0-based and refer to the state **as of the previous operation in
the same delta**. A delta is applied in order. There is no netting: a batch that
removes and reinserts at one position delivers remove+insert. FDA can deliver an
update because its delta is keyed; we cannot, and we do not need to.

### 3.2 Delta

```fsharp
[<Struct>]
type ListDelta<'T> =
    val mutable internal Ops: DeltaBuffer<ListOp<'T>>
    // helpers: Insert(pos, value), Remove(pos), Update(pos, value), IsEmpty, Clear
```

One ordered buffer, not three. Order is the semantics. The journal of a node is
a `ListDelta` (input coordinates); the output delta is a `ListDelta` (output
coordinates), exactly like the set/map nodes.

### 3.3 Source

`ChangeableList<'T>` holds a `ResizeArray<'T>` (mutable, transient-view
friendly). Writes mutate the array, build a one-delta op list, append it to
every registered sink's journal, and mark (mirror of `ChangeableSet`).
Transactions journal ops in order and replay them at commit — no netting.

Transaction position semantics (measured and fixed 2026-08-05): the batch
replays as one ordered delta, so positions are relative to the evolving replay
state. Appends journal a sentinel that the source resolves to the replay-time
end (a virtual count maintained per journal session), so several appends in one
batch land in write order — the naive pre-transaction position would reverse
them (regression test: `transaction appends land in write order`). A `Clear`
marker resets the virtual count; `Set` remains last-wins over the whole batch.

Members: `Append`, `Prepend`, `InsertAt`, `RemoveAt`, `UpdateAt` (with the
source equality check: an equal update marks nothing), `Remove(value)` (linear
search, O(n), write-time only), `Clear` (descending removes), `Set` (full
replace with a prefix/suffix-trim diff: common prefix and suffix become
no-ops, the middle becomes updates when the lengths match, otherwise descending
removes then ascending inserts).

`force` materializes `'T[]` (a fresh array). There is no `FrozenList` in
`System.Collections.Frozen` on net8/net10 (verified against the ref packs;
only `FrozenSet` and `FrozenDictionary` exist). `ImmutableArray<'T>` is
available in-box if a struct snapshot is wanted later.

### 3.4 Position translation

| Node | Translation | Cost |
|---|---|---|
| `map` / `choose` / `filter` | parallel `inputPositions` array: sorted input position of every output element; binary search (`LowerBound`) gives the output position; tail fixup on every insert/remove | O(log n) search + O(k) tail fixup, zero allocation |
| `append` / `concat` | base offset per source, maintained by a counter | O(1) per op |

**Filter/choose details.** The output is the subsequence of input elements
that survive the mapping. The node keeps the input position of every output
element in a sorted array parallel to the output. For an op at input position
`p`: `LowerBound(p)` is the output position of the element at `p` when it
survives; the element is present when the stored position equals `p`.

- Insert — map the value; on `Some u` insert at `LowerBound(p)`, emit insert;
  on `None` nothing. Either way, the stored positions `>= p` shift +1
  (tail fixup) — the input positions of later elements move whether or not the
  new element survives.
- Remove — if the element is present, remove at its output position and emit
  remove; the stored positions `> p` shift -1 regardless.
- Update — map the new value: alive + `Some u` -> update in place; not alive +
  `Some u` -> insert; alive + `None` -> remove; not alive + `None` -> no-op.
  This gives FDA's choose semantics: an update on a filtered-out element can
  bring it into the output, and an update that now fails the mapping removes
  it. No shift.

The initial load runs the mapping over the snapshot and fills both arrays in
O(n). A Fenwick tree was prototyped first and discarded: it is keyed by input
position, and positions shift on insert/remove, so the flags go stale (see
BISECT-notes-style finding, 2026-08-05).

**Append details.** One journal with a source tag per op. Cross-source order
matters (a right op's absolute position depends on `leftCount` at its
application point), so the two sources append into one tagged journal in
arrival order. At drain, `leftCount` is updated by left ops and read by right
ops: absolute position = `leftCount + p` for right ops. Initial load reads the
left view and the right view, concatenates, and registers after (the PLAN.md
§7.4 double-apply rule).

### 3.5 Drain protocol

Mirror of the set/map nodes (`MapSetNode` pattern):

1. `GetValue` claims the owner thread, checks disposed, ensures initialized
   (snapshot the source view, then register, then build state — register
   between snapshot and load, per PLAN.md §7.4).
2. If a source version differs from the stored snapshot, re-read the source to
   force its drain, then store the new version.
3. If the journal is non-empty, drain: apply ops in order, build the output
   delta, push it to the sinks, mark the edges.
4. Return the transient view and register the dependency.

### 3.6 Observation

`AList.observe : (IReadOnlyList<'T> -> ListDelta<'T> -> unit) -> alist<'T> ->
IObservation`. The callback receives the view and the ordered ops after each
batch. Parity shape: FDA `AddCallback(state, delta)`.

## 4. API surface

```fsharp
type IAdaptiveList<'T> =
    inherit IAdaptiveObject
    inherit IDisposable
    abstract member GetValue: unit -> IReadOnlyList<'T>

type alist<'T> = IAdaptiveList<'T>
type clist<'T> = ChangeableList<'T>
```

### 4.1 AList module — prototype (v1)

`empty`, `single`, `ofSeq`, `ofArray`, `ofList`, `ofResizeArray`, `constant`,
`delay`, `map`, `choose`, `chooseV`, `filter`, `append`, `getValue`, `force`
(`'T[]`), `toList`, `toArray`, `count`, `isEmpty`, `observe`.

### 4.2 CList module

`empty`, `ofSeq`, `ofArray`, `ofList`, `append`, `prepend`, `insertAt`,
`removeAt`, `updateAt`, `remove`, `clear`, `set`, `value`, `force`, `toList`.

### 4.3 Phase 2

`ofAVal`, `bind`, `ofReader`, `custom`, `concat` (static seq), `mapi`,
`filteri`, `indexed` (as `int * 'T`), `tryGet`, `item`, `tryFind`,
`tryFindIndex`, `tryFirst`, `tryLast`, `exists`, `forall`, `fold`,
`foldGroup`, `reduce`, `sumBy`, `toAVal`, `sub`/`take`/`skip`,
`pairwise`, `zip`.

### 4.4 Phase 3 (the hard ones)

`collect` (per-element inner lists; needs per-inner base offsets and is the
most complex node — deferred), `sortBy`/`sortByDescending`/`rev` (poll node:
re-sort + diff, same class as FDA's `SortByReader`), `bind2`/`bind3`.

## 5. Recorded deviations from FDA

| FDA | AdaptiveSlop |
|---|---|
| `Index`-based API (`insertAt: Index`, `tryGet: Index`, ...) | positional `int` API |
| `indexed : alist<'T> -> alist<Index * 'T>` | `indexed : alist<'T> -> alist<int * 'T>` (if taken) |
| `mapi`/`filteri` pass `Index` | pass `int` |
| `force` returns `IndexList<'T>` | `force` returns `'T[]` |
| delta is a keyed map (`IndexListDelta`) | delta is an ordered op list (`ListDelta`) |
| batch update netting possible | no netting; remove+insert instead of update |

## 6. Costs and risks

1. **Front-insert is O(n) memmove** on the source (`List.Insert`). Fine for
   typical sizes; adversarial front-insert-heavy workloads degrade. Mitigation:
   swap the source storage for a chunked structure later — the delta contract
   is storage-agnostic. Benchmark-gated.
2. **No batch netting** — observe deltas can be chunkier than FDA's. Documented
   behavior.
3. **`sortBy` is a poll node** — O(n log n) per change, same class as FDA.
4. **Filter/choose translation is O(k) per op** — a parallel `inputPositions`
   array (sorted, mirroring the output) with binary search and a tail fixup on
   every insert/remove (the stored positions of later elements shift whether or
   not the affected element survives). Same class as the output array's own
   memmove; zero allocation.
5. **Derived chain depth (matches the set world).** The version check in a
   node read reaches its direct source only: a chain updates correctly at
   depth 2 from a single read. Deeper unobserved chains are not drained by one
   read (the intermediate node's version bumps only when its own journal
   receives entries, i.e. when it drains). Observation works on any target
   that receives deltas; a node only pushes when its output delta is
   non-empty, so an observe fires exactly on real changes. The set/map nodes
   have the same semantics (verified against the set suite, 2026-08-05).

## 7. Prototype scope (next)

- `ListOp`/`ListDelta` in `Shared.fs`; `IAdaptiveList`/`alist` there;
  `ChangeableList`/`clist` in `Changeable.fs`.
- `FilterMapListNode`, `AppendListNode`, `ObserveListNode` in a new
  `Collections/ListNodes.fs`.
- `AList`/`CList` modules (v1 subset) in `Api.fs`.
- Tests: correctness (insert/remove/update through map/choose/filter/append,
  batch semantics, cross-source ordering), disposal, and the permanent
  allocation test: an N-op batch (write + drain + delivery) allocates 0 bytes;
  `force` after the drain allocates O(n).
- Exit: suite green in Debug and Release; allocation test green.
