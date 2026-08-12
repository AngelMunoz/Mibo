# FDA Public API — Gap List (decision sheet)

Comparison of the core public API of `FSharp.Data.Adaptive` (reference at
`E:\FSharp.Data.Adaptive`) against `src/AdaptiveSlop.Core` on `master`.

Excluded by request: `AdaptiveHistory`, `AdaptiveFileSystem`, Fable helpers,
Adaptify helpers, the C# wrapper, and internals.

Decisions recorded 2026-08-05. Terms: **bring** = in scope, **betted** =
rejected (against the settled philosophy), **later** = agreed but not in the
current work set, **undecided** = no decision yet.

Settled criteria (from the 2026-08-05 extension discussion, see
`docs/2026-08-05-MAPA-DESIGN.md` §1):

- Path A: positional lists, no `Index`, no order-maintenance structure.
- No public node subclassing, no raw mark APIs, no user-owned delta
  application (reader infra), no level/priority controls.
- The extension offering is: `ofExternal` + invalidate handles, `observeWeak`,
  `AList.custom`.
- Plain snapshot-input functions: bring. The per-element adaptive family
  (`*A`): bring as Tier 2 (`docs/2026-08-05-MAPA-DESIGN.md`).
- Computation expressions: skipped until the end.

Items already tracked in `docs/archive/2026-08-05-PARITY-FDA.md` are marked
`(tracked)`.

---

## 1. Value layer (AVal / CVal)

- **1.1** `AVal.mapNonAdaptive` — `('T1 -> 'T2) -> aval<'T1> -> aval<'T2>`. Mapping runs on every read, no dirty tracking. Gain is dubious: our clean read is one flag check. Needs a new node type. `(tracked, deferred)` — Decision: **undecided** — not in this work set.
- **1.2** `AVal.cast` — `IAdaptiveValue -> aval<'T>`. Needs the untyped value layer (1.3). `(tracked, deferred)` — Decision: **betted** — needs the untyped layer, which is public-interface churn for reflection/visitor interop only.
- **1.3** Untyped `IAdaptiveValue` — `GetValueUntyped`, `ContentType`, `Accept`, `IAdaptiveValueVisitor<'R>`. Public-interface change. Only needed for reflection/visitor interop. `(tracked, deferred)` — Decision: **betted** — against the minimal-surface philosophy.
- **1.4** `addWeakCallback` on values — `aval<'T> -> ('T -> unit) -> IDisposable`. We have strong `observe`. — Decision: **betted** — the use case is covered by our own `observeWeak` (agreed extension, different API shape).
- **1.5** `AbstractVal<'T>` (user subclass) — `abstract Compute : AdaptiveToken -> 'T`. We have `AVal.custom`. — Decision: **betted** — public node authoring is explicitly not offered.

## 2. Core object model

- **2.1** `IAdaptiveObject` members — `OutOfDate`, `Level`, `Outputs`, `Tag`, `Mark()`, `AddMarkingCallback`, `AddOutput`, `RemoveOutput`. We have only `Version`. — Decision: **betted** — raw marking machinery; `ofExternal`'s invalidate handle replaces the need.
- **2.2** Public `AdaptiveToken` — caller + cancellation. We are token-less by design (collector). — Decision: **betted** — token-less design is settled; cancellation is not requested.
- **2.3** `AdaptiveObject` base class — public, extensible. We keep nodes internal. — Decision: **betted** — node authoring is not offered.
- **2.4** `DecoratorObject` — `EvaluateAlways`, `EvaluateIfNeeded`. — Decision: **betted** — follows 2.3.
- **2.5** Transaction API — `getCurrentTransaction`, `useTransaction`, `makeCurrent`, `Transaction.AddFinalizer`. — Decision: **betted** — `Transaction.run` suffices; finalizer-based callbacks are replaced by `observe`/`observeWeak`. Revisit only if a consumer asks.

## 3. ASet — missing 18

- **3.1** `range` — `aval< ^T > -> aval< ^T > -> aset< ^T >`. Adaptive numeric range (inline). Cheap. — Decision: **bring**.
- **3.2** `bind2` — `('A -> 'B -> aset<'C>) -> aval<'A> -> aval<'B> -> aset<'C>`. — Decision: **bring**.
- **3.3** `bind3` — 3-input bind. — Decision: **bring**.
- **3.4** `mapUse` — `('A -> 'B) -> aset<'A> -> IDisposable * aset<'B>` when `'B :> IDisposable`. Disposes removed values. — Decision: **bring**.
- **3.5** `flattenA` — `aset<aval<'A>> -> aset<'A>`. — Decision: **later** — same node as `mapA` (identity mapping), ships with the `*A` family.
- **3.6** `mapA` — `('A -> aval<'B>) -> aset<'A> -> aset<'B>`. — Decision: **bring** — Tier 2 per `docs/2026-08-05-MAPA-DESIGN.md` (Phase 1).
- **3.7** `chooseA` — `('A -> aval<option<'B>>) -> aset<'A> -> aset<'B>`. — Decision: **bring** — Tier 2 (Phase 1).
- **3.8** `filterA` — `('A -> aval<bool>) -> aset<'A> -> aset<'A>`. — Decision: **bring** — Tier 2 (Phase 1).
- **3.9** `existsA` / `forallA` — predicate returns `aval<bool>`. — Decision: **later** — composition over `mapA` (MAPA-DESIGN §1, phase 4).
- **3.10** `countByA` — `('a -> aval<bool>) -> aset<'a> -> aval<int>`. — Decision: **later** — phase 4.
- **3.11** `sumByA` / `averageByA` — mapping returns `aval<'T>`. — Decision: **later** — phase 4.
- **3.12** `average` / `averageBy` — non-adaptive average. — Decision: **bring**.
- **3.13** `reduceByA` — reduction over per-element adaptive mapping. — Decision: **later** — phase 4.
- **3.14** `collect'` — `('A -> seq<'B>) -> aset<'A> -> aset<'B>`. Static expand. Cheap; `collect` + `ASet.ofSeq` today. — Decision: **bring**.
- **3.15** `ofReader` (real) — `(unit -> #IOpReader<HashSetDelta<'T>>) -> aset<'T>`. Ours takes a snapshot function. — Decision: **betted** — reader infra is not offered; `ofExternal` replaces the use case. Our snapshot `ofReader` stays as-is.

## 4. AMap — missing 20 + one semantic fix

- **4.1** `ofHashMap` — `HashMap<'K,'V> -> amap<'K,'V>`. We have no frozen HashMap; `Dictionary`/`Map` is our boundary. `(tracked deviation)` — Decision: **betted** — follows 8.3 (no Traceable HashMap type).
- **4.2** `ofReader` — `(unit -> #IOpReader<HashMapDelta<'K,'V>>) -> amap<'K,'V>`. — Decision: **betted** — same as 3.15.
- **4.3** `map'` — `('V1 -> 'V2) -> amap<'K,'V1> -> amap<'K,'V2>`. Value-only map. `map (fun _ v -> ...)` today. — Decision: **bring**.
- **4.4** `choose'` — value-only choose. — Decision: **bring**.
- **4.5** `filter'` — value-only filter. — Decision: **bring**.
- **4.6** `intersectV` — `amap -> amap -> amap<'K, struct('V1 * 'V2)>`. Struct-pair variant of `intersect`. — Decision: **bring**.
- **4.7** `mapA` / `chooseA` / `filterA` — per-element adaptive, key-aware. — Decision: **bring** — Tier 2 (Phase 2).
- **4.8** `bind2` / `bind3` — bind over two/three avals. — Decision: **bring**.
- **4.9** `mapUse` — resource disposal map. — Decision: **bring**.
- **4.10** `reduceByA` — adaptive mapping reduction. — Decision: **later** — phase 4.
- **4.11** `foldHalfGroup` — `('S -> 'K -> 'V -> 'S) -> trySubtract -> zero -> amap -> aval<'S>`. We have it on ASet only. — Decision: **bring**.
- **4.12** `sumBy` / `averageBy` — non-adaptive. — Decision: **bring**.
- **4.13** `existsA` / `forallA` / `countByA` / `sumByA` / `averageByA` — adaptive variants. — Decision: **later** — phase 4.
- **4.14** `toASet` semantics — FDA returns `aset<'K * 'V>` (pairs). Ours returns `aset<'K>` (keys). — Decision: **bring** — align with FDA (pairs); the current keys behavior moves to `AMap.keys`.

## 5. AList — the largest gap (we have 16 of ~80)

### 5.1 Structure

- **5.1.1** `mapi` / `choosei` / `filteri` — index-aware variants. — Decision: **bring** — the index is the `int` input position (positional deviation, per ALIST-DESIGN §5).
- **5.1.2** `collect` / `collecti` / `collect'` — flat-map. — Decision: `collect`/`collecti` **later** (per-element inner lists, most complex node — ALIST-DESIGN §4.4); `collect'` (static `seq` expand) **bring**.
- **5.1.3** `indexed` — `alist<Index * 'T>`. — Decision: **bring** as `alist<int * 'T>` (positional deviation, per ALIST-DESIGN §5); the FDA `Index` form is betted with 8.1.
- **5.1.4** `rev` — Decision: **bring** (poll node: re-sort + diff).
- **5.1.5** `concat` — `#seq<alist<'T>> -> alist<'T>`. — Decision: **bring** (generalizes `append`).
- **5.1.6** `init` — `aval<int> -> (int -> 'T) -> alist<'T>`. — Decision: **bring**.
- **5.1.7** `range` — adaptive numeric range. — Decision: **bring**.
- **5.1.8** `ofAVal` — `aval<#seq<'T>> -> alist<'T>`. — Decision: **bring**.
- **5.1.9** `toAVal` — `alist<'T> -> aval<IndexList<'T>>`. Snapshot aval. Cheap with our types. — Decision: **bring** — as `aval<'T[]>` (positional deviation).
- **5.1.10** `ofReader` / `custom` — Decision: `ofReader` **betted** (reader infra); `custom` **bring** — `AList.custom` is an agreed extension (MAPA-DESIGN §1.3).
- **5.1.11** `ofIndexList` — Decision: **betted** — no IndexList type (8.2).

### 5.2 Bind

- **5.2.1** `bind` — `('A -> alist<'B>) -> aval<'A> -> alist<'B>`. — Decision: **bring**.
- **5.2.2** `bind2` / `bind3`. — Decision: **bring**.

### 5.3 Per-element adaptive

- **5.3.1** `mapA` / `mapAi`. — Decision: **bring** — Tier 2 (Phase 3); `mapAi` in phase 4.
- **5.3.2** `chooseA` / `chooseAi`. — Decision: **bring** — Tier 2 (Phase 3); `chooseAi` in phase 4.
- **5.3.3** `filterA` / `filterAi`. — Decision: **bring** — Tier 2 (Phase 3); `filterAi` in phase 4.

### 5.4 Slicing

- **5.4.1** `take` / `takeA` — `takeA` takes `aval<int>`. — Decision: **bring**.
- **5.4.2** `skip` / `skipA`. — Decision: **bring**.
- **5.4.3** `sub` / `subA`. — Decision: **bring**.

### 5.5 Lookup

- **5.5.1** `tryGet` (Index) / `tryAt` (int). — Decision: **bring** — `tryAt`/`tryGet` over the `int` position (positional deviation); the `Index` form is betted with 8.1.
- **5.5.2** `tryFirst` / `tryLast`. — Decision: **bring**.

### 5.6 Sorting

- **5.6.1** `sort` / `sortDescending`. — Decision: **bring** (poll node).
- **5.6.2** `sortBy` / `sortByDescending` / `sortByi` / `sortByDescendingi`. — Decision: **bring** (poll node).
- **5.6.3** `sortWith`. — Decision: **bring** (poll node).

### 5.7 Other

- **5.7.1** `pairwise` / `pairwiseCyclic`. — Decision: **bring**.
- **5.7.2** `mapUse` / `mapUsei`. — Decision: **bring**.

### 5.8 Reductions

- **5.8.1** `reduce` / `reduceBy` / `reduceByA` — we have `reduce` on ASet/AMap, not AList. — Decision: `reduce`/`reduceBy` **bring**; `reduceByA` **later** (phase 4).
- **5.8.2** `fold` / `foldGroup` / `foldHalfGroup`. — Decision: **bring**.
- **5.8.3** `forall` / `exists` / `forallA` / `existsA`. — Decision: plain **bring**; `*A` **later** (phase 4).
- **5.8.4** `tryMin` / `tryMax` / `sum` / `sumBy` / `average` / `averageBy`. — Decision: **bring**.
- **5.8.5** `countBy` / `countByA` / `sumByA` / `averageByA`. — Decision: plain **bring**; `*A` **later** (phase 4).

## 6. Changeables

- **6.1** `cset` — `UpdateTo`, `Perform`, `UnionWith`, `ExceptWith`, `IntersectWith`, `GetReader`. — Decision: `UpdateTo`/`Perform`/`UnionWith`/`ExceptWith`/`IntersectWith` **bring**; `GetReader` **betted** (reader infra).
- **6.2** `cmap` — `ContainsKey`, `TryGetValue`, `Item`, `UpdateTo`, `Perform`, `GetReader`, `Clear`. — Decision: all **bring** except `GetReader` **betted**.
- **6.3** `clist` — `UpdateTo`, `Perform`, `AddRange`. — Decision: **bring**.
- **6.4** `clist` index identity — `Add : 'T -> Index`, `InsertAfter`/`InsertBefore`, `Remove : Index -> bool`, `TryGet : Index -> ...`, `MinIndex`/`MaxIndex`, `Neighbours`, `TryGetNext`/`TryGetPrev`, `NewIndexAfter`/`NewIndexBefore`. — Decision: **betted** — needs Index (8.1); the positional `int` API is settled (Path A).

## 7. Reader infrastructure (extension point)

- **7.1** `GetReader()` on all collections + `IOpReader<'State,'Delta>` + `AbstractReader`. — Decision: **betted** — user-owned delta application is explicitly not offered; `observe` covers consumption, `ofExternal` covers source authoring.
- **7.2** `Traceable<'State,'Delta>` / `Monoid<'T>`. — Decision: **betted** — support machinery for 7.1.

## 8. Datastructures

- **8.1** `Index` (`zero`, `after`, `before`, `between`). — Decision: **betted** — Path A decision (ALIST-DESIGN §2).
- **8.2** `IndexList<'T>` — FDA's list type. — Decision: **betted** — follows 8.1.
- **8.3** `HashMap<'K,'V>` / `HashSet<'T>` (Traceable). We use `FrozenDictionary`/`FrozenSet`. — Decision: **betted** — drop-in type compat is not a goal.
- **8.4** `CountingHashSet<'T>` — used by `ASet.custom` in FDA. We use a delta builder. — Decision: **betted** — counting lives inside `SetNodeState` where it is needed.
- **8.5** `MapExt` / `MultiSetMap` — internal helpers. — Decision: **betted**.

## 9. Computation expressions

- **9.1** `aval { }` — Decision: **skipped until the end**.
- **9.2** `aset { }` — Decision: **skipped until the end**.
- **9.3** `alist { }` — Decision: **skipped until the end**.
- **9.4** `amap { }` — Decision: **skipped until the end**.

## 10. Extensions

- **10.1** `alist` slicing — `list.[a..b]`, also with `aval` bounds. — Decision: **bring**.
- **10.2** `ASet.sort` / `sortBy` / `sortByDescending` / `sortWith` / `sortDescending`. — Decision: **bring**.
- **10.3** conversions — `AList.toASet`, `toIndexedASet`, `AList.ofASet`, `AMap.toAList`, `AMap.ofAList`. — Decision: **bring**.

## 11. AdaptiveReduction

- **11.1** `par` / `structpar` — parallel composition of reductions. — Decision: **bring**.
- **11.2** `mapIn` — map on the element side. — Decision: **bring**.
- **11.3** `count`. — Decision: **bring**.
- **11.4** `product`. — Decision: **bring**.
- **11.5** `average`. — Decision: **bring**.

---

## Summary of decisions

**Bring** (plain snapshot-input family + agreed items): ASet 3.1-3.4, 3.12, 3.14; AMap 4.3-4.6, 4.8, 4.9, 4.11, 4.12, 4.14; AList 5.1.1, 5.1.3 (int form), 5.1.4-5.1.9, 5.1.10 custom, 5.2, 5.4, 5.5, 5.6, 5.7, 5.8 plain; changeables 6.1-6.3 (minus readers); extensions 10.1-10.3; reductions 11.1-11.5.

**Later** (agreed, not in this work set): the `*A` reduction family (3.9-3.11, 3.13, 4.10, 4.13, 5.8 `*A`), `flattenA` (3.5), `collect`/`collecti` (5.1.2), list `i`-variants of `*A` (5.3), `mapiA` family.

**This work set** (Tier 2, per `docs/2026-08-05-MAPA-DESIGN.md`): ASet/AMap/AList `mapA`/`chooseA`/`filterA` (3.6-3.8, 4.7, 5.3).

**Betted**: 1.2-1.5, 2.1-2.5, 3.15, 4.1, 4.2, 5.1.10 ofReader, 5.1.11, 6.x GetReader, 7.1, 7.2, 8.1-8.5, 6.4.

**Skipped until the end**: computation expressions (9.1-9.4).

**Undecided**: `AVal.mapNonAdaptive` (1.1) — not in this work set.

**Extension points** (our additions, not FDA gaps — recorded in
`docs/2026-08-05-MAPA-DESIGN.md` §1): `ofExternal` + invalidate handles,
`observeWeak`, `AList.custom`.

---

## 2026-08-10 — addendum (feat/joinon-groupby-reductions)

Items resolved since the decisions above were recorded; the resolution wins.

- **4.10, 4.13** — AMap `*A` family (`reduceByA`, `countByA`, `existsA`,
  `forallA`, `sumByA`, `averageByA`): **done** — pure compositions over
  `mapA`/`filterA` + the existing reduction nodes (no new node types).
- **5.8 `*A`** — AList `*A` family: **done** — value-only mapping
  (`'T -> aval<'U>`), FDA parity. The `mapiA`-based index-aware form was
  rejected during implementation: mapiA's mapping-time positions stick on
  shifts (documented semantic), so an index-aware reduction would not track
  live positions.
- **voption counterparts** — ASet and AList `tryMinA`/`tryMaxA`: **done**.
  Rule: any non-A function with an option/voption shape gets an `*A` form
  carrying voption in the same position (`aval<'U voption>` here). AMap has
  no plain `tryMin`/`tryMax`, so no counterparts; `AMap.tryFind` is already
  the adaptive voption form.
- **voption pairs scan (2026-08-10, second pass)** — the full option/voption
  pair inventory, resolved:
  - `choose`/`chooseV` on all three collections: complete (pre-existing).
  - `choose2`/`choose2V` on AMap: complete (pre-existing).
  - `chooseA` (option) on all three: `chooseAV` (voption) **added** — the
    mapping returns `aval<'U voption>` directly, skipping the
    option-to-voption wrapper node per element (the no-allocation path).
  - `AList.choosei` (option): `chooseiV` (voption) **added** (was missing).
  - `AList.chooseiA` (option): `chooseiAV` (voption) **added**.
  - `AMap.tryMinA`/`tryMaxA` **added** for family symmetry (AMap has no
    plain `tryMin`/`tryMax`; the members mirror the ASet/AList ones).
  - `AListSliceExtensions.GetSlice(start: int option, finish: int option)`:
    **excluded** — F# slice syntax is compiler-mandated `option` (the
    compiler generates `Option` arguments from `list.[a..b]`); a voption
    form is not reachable from the syntax. Recorded, not provided.
- **`AMap.difference`** — our addition (the AMap counterpart of
  `ASet.difference`): **done** — left-only keys on `Choose2MapNode`.
- **`AMap.joinOn`** — our addition (the map analog of `AVal.map2` with
  computed join keys; the measured join projection): **done** — per-key
  swappable inputs, no subgraph rebuild on updates. See
  `docs/2026-08-10-JOIN-DESIGN.md`.
- **`AMap.groupBy`** — untracked FDA parity: **done** — output
  `amap<'G, amap<'K,'V>>` (live per-group maps), plain key function, empty
  groups removed at the next drain. See `docs/2026-08-10-JOIN-DESIGN.md`.
