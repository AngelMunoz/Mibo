# Join + GroupBy Design — per-key joins and live per-group maps

Status: **implemented (2026-08-10)** on `feat/joinon-groupby-reductions`.
Motivation and measurements: a profiled join projection in a game loop
accounted for 28.5% of busy time; ~5% of busy time was pure library
allocation from the per-update subgraph rebuild of the `mapA` + `tryFind`
join idiom (docs/BENCHMARKS.md, 2026-08-10).

## 1. The problem

The measured join idiom is always `AMap.mapA`/`chooseA` over one map plus a
per-element `AMap.tryFind` into another — a hand-rolled equi-join. The
library offered no alternative: `Choose2MapNode` takes plain values and
combines on equal map keys only; it cannot express a computed join key (a
value inside the left row), a 3-way join, or a `voption` output.

The structural cost: `ElementMapNode.DrainJournal` re-runs the mapping
closure for every journal entry — including `addOrUpdate` of an existing key —
and rebuilds the per-key aval subgraph from scratch. In the churn regime
every key updates per frame, so the join rebuilt `MapLookupNode +
AdaptiveNode` per key per frame. The first recompute of every fresh node
allocates its `deps`/`depVersions` arrays (the measured `ZeroCreate`).

## 2. The join node (`AMap.joinOn`)

`JoinMapNode<'K1,'V1,'K2,'V2,'U>` (ElementMapNode.fs):

- The left map is enumerated per key; the join key is computed from the left
  entry (`keyOfLeft`); the right map is looked up per entry
  (`MapLookupNode`: registers nothing, read-time gate), never enumerated or
  rebuilt.
- The mapping receives `'K1 -> aval<'V1> -> aval<'V2 voption> -> aval<'U voption>`:
  the left value as a **swappable input** (a `ChangeableValue` the node
  re-applies on every update) and the right-side lookup. A `ValueNone`
  output drops the entry (choose semantics); the right side as `voption`
  gives left-join semantics.
- The per-key subgraph is built **once** and updated in place: a left update
  re-applies the cell (equality-gated, marks the subgraph dirty) — no
  rebuild, no per-frame allocation. A join-key change (rare: a target
  switch) re-runs the mapping against the new lookup; the cell survives.
- Right-map changes reach the entries through the lookups (version-gated,
  re-read at force time) and through the element scan (gated on the write
  generation, which every delta delivery bumps).

Measured (docs/BENCHMARKS.md, 2026-08-10): on the join churn workload
(100 left entries, 200 right entries, per-update left churn) the idiom
allocates ~2.1 MB per operation; `joinOn` allocates 0 B and is ~36% faster.

### 2.1 Why not change `mapA` instead?

The generic `mapA` closure owns its subgraph; the library cannot reuse it
without changing the mapping contract. `joinOn` is the sanctioned low-churn
form: the node owns the subgraph construction, so it can swap the input.
`mapA` closures that are value-independent still rebuild — that is inherent
to the closure-owned subgraph. `tryFind` memoization was considered and
rejected for this PR (identity of returned nodes would change; `joinOn` owns
its lookups).

## 3. The groupBy node (`AMap.groupBy`)

`GroupByMapNode<'K,'V,'G>` + `GroupMapChildNode<'K,'V>` (MapNodes.fs):

- Every group is a **live adaptive map** (a `GroupMapChildNode`): the groupBy
  drain routes source deltas into the children by computed key; the children
  deliver their own deltas to their own consumers. Group-content changes
  never re-read the whole map and never re-run the key function for other
  groups.
- A group disappears when it becomes empty (removed at the next drain).
- A key whose value changes group is moved: removed from the old child,
  added to the new one. The per-key group is tracked in a `memberGroup` map,
  because a remove delta carries no value to compute the group from.
- The output is `amap<'G, amap<'K,'V>>`; the output map's version moves only
  for group add/remove.

Decisions recorded (open questions from the review):

- Output shape: `amap<'G, amap<'K,'V>>` — adaptive per-group maps.
- Key function: plain `'K -> 'V -> 'G`. An aval-returning key function
  (adaptive membership) is not provided: it would need per-entry key avals
  and re-keying machinery; revisit when usage appears.
- Empty-group timing: removed at the next drain (never visible as an empty
  group in the output).

## 4. Argument order

`joinOn` takes `(left) (right) (keyOfLeft) (mapping)` — the maps first. The
order is deliberate: the lambdas elaborate after the map types are pinned, so
a record-field access in `keyOfLeft` or the mapping resolves against the
actual map types. With the lambdas first, F# resolves the field against the
first record in scope with a matching field (verified: a `v.Id` access
resolved to the wrong record type and failed to compile). The pipe form does
not apply: a piped value lands in the mapping slot, a compile error (with the
old order it landed in the right slot and could compile with silently swapped
maps).

## 5. Non-goals (recorded)

- Radius/spatial query: userland composition (`filterA` with an adaptive
  predicate over `center`/`radius` avals) once the swap machinery landed; a
  grid-indexed collection is not warranted (small N, churn not asymptotics).
- `AList.collect`: deferred by decision (gap sheet).
- `tryFind` memoization: rejected for this PR (see §2.1).

## 6. Performance guidance for hot per-key subgraphs

The remaining per-update cost of a join is the per-key force machinery
(version checks, recompute, contribution diff) — shared by every derived
node, not specific to the join. Measured split on the join churn workload:
~80% of the iteration cost is the read/drain path, ~20% is the write path.
The levers, in order of effect:

1. **Entry count.** The force runs per updated key per read. A coarser join
   key (fewer left entries) reduces the forces directly.
2. **The mapping's subgraph shape.** A hot subgraph with static inputs is
   cheaper on a node with a fixed dependency set than on the general
   collect-based node: measured `AVal.mapN` recomputes ~40% faster than
   `AVal.map2` on the same churn shape (fixed-dep node skips the per-
   recompute dependency collection and array copies). Constraint: `mapN`
   and `reduce` require same-typed inputs. A heterogeneous subgraph (the
   join mapping's `cell + lookup`) has no fixed-dep combinator today; the
   floor is the general node.
3. **Write batching.** Batching the left-map writes of one frame in
   `Transaction.run` cuts the write-side cost (the CMap flushes the batch
   once); the read/drain cost is unchanged.
4. **Read discipline.** A clean read (no write since the last read) costs
   nothing: no scan, no force. Do not re-read the same derivation within a
   frame.

A library-side follow-up (recorded, not in this PR): reimplement
`AVal.map2`/`map3`/`map4` on pre-allocated-input nodes (the `MapNNode`
pattern generalized to heterogeneous inputs) — semantically identical
(static deps by construction), ~40% cheaper per recompute on the measured
shape. Core hot-path change: requires its own benchmark run and sign-off.
