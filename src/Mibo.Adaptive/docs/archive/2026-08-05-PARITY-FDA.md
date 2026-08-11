# AdaptiveSlop — FDA Public API Parity Plan

This document tracks public-API parity with `FSharp.Data.Adaptive` (FDA, the
reference at `E:\FSharp.Data.Adaptive`). It covers the **core** surface only:
`AVal`, `cval`/`ChangeableValue`, the type aliases, and the collection
constructors of `ASet`/`AMap`/`CSet`/`CMap`.

Out of scope: `IndexList` and `AList`/`clist` (pending, see
`docs/archive/2026-08-04-PLAN.md` §7), the per-element-adaptive `mapA`/`chooseA`/`filterA`
family (excluded by `PLAN.md` §7), and grouped/spatial-grid nodes.

Reference points in our code:

- `src/AdaptiveSlop.Core/Library.fs` — `IAdaptiveValue<'T>`, `ChangeableValue<'T>`,
  `AdaptiveNode<'T>`, the `AVal` and `CVal` modules.
- `src/AdaptiveSlop.Core/Collections/Shared.fs` — `IAdaptiveSet<'T>`,
  `IAdaptiveMap<'K,'V>`.
- `src/AdaptiveSlop.Core/Collections/Changeable.fs` — `ChangeableSet<'T>`,
  `ChangeableMap<'K,'V>`.
- `src/AdaptiveSlop.Core/Collections/Api.fs` — `ASet`, `CSet`, `AMap`, `CMap`.

## Status

- 2026-08-04: branch `feature/fda-parity`. Phase P0 done (aliases, `force`,
  `init`, `Value`/`UpdateTo`/`GetValue` on `cval`, `ASet.empty`,
  `AMap.empty`/`constant`/`delay`). Phase P1 done except `mapNonAdaptive`
  (deferred, not 1-1 — see §7). The DSL modules (`AVal`, `CVal`, `ASet`,
  `CSet`, `AMap`, `CMap`) carry the short aliases in their signatures and no
  result casts in their bodies. Suite green in Debug and Release (160 tests).
- Invariants from `AGENTS.md` hold for all changes here: pull-lazy, recompute =
  re-read all deps, no evaluation during marking, owner-thread confinement,
  zero allocation on hot paths.
- Phasing below is ordered by risk. Each phase is independently shippable.

## 7. Not 1-1 translatable (deferred or adapted)

These FDA functions do not translate cleanly. They stay open.

| Item | FDA | Why it is not 1-1 | Decision |
|---|---|---|---|
| `AVal.mapNonAdaptive` | `('T1 -> 'T2) -> aval<'T1> -> aval<'T2>` | FDA's `MapNonAdaptiveVal` is a decorator that always evaluates: no dirty tracking, no output cache, the mapping runs on every read. Our model is pull-lazy with a dirty cache per node; a faithful port needs a node that bypasses `IsDirty` and re-reads + re-maps every read. The gain is dubious: our clean read is already one flag check, and the mapping runs per recompute. | Deferred. Implement only if a benchmark shows a need. If taken, it needs a new node type, not `AdaptiveNode`. |
| `AVal.cast` | `IAdaptiveValue -> aval<'T>` | Needs the untyped `IAdaptiveValue` layer (`GetValueUntyped`, `ContentType`, `Accept`) and `IAdaptiveValueVisitor<'R>`. Our `IAdaptiveValue<'T>` is a single typed interface. Public-interface change. | Deferred (plan §2.3, phase P3). |
| `AVal.custom` | `AdaptiveToken -> 'T` | FDA passes an `AdaptiveToken`; we have no token. | DONE as `(unit -> 'T) -> aval<'T>` over `AdaptiveNode`. Signature adapted, documented. |
| `AVal.force` | `aval<'T> -> 'T` (token-based) | FDA evaluates with `AdaptiveToken.Top`; our reads are unit. | DONE as alias of `getValue`. |
| `AVal.map/map2/map3/bind` constant folding | `IsConstant` detection returns a `ConstantVal` instead of a node | Our combinators always build an `AdaptiveNode`; `AVal.map f (AVal.constant 5)` tracks a dependency. Semantic difference, benchmark-gated. | Plan §2.2 item A (phase P2). |
| `cheapEqual` output caching | `MapVal`/`Map2Val`/`Map3Val` skip the mapping when inputs are shallow-equal to the cached inputs | Our nodes recompute and call the mapping on every dirty read; equality-at-the-source already elides no-op writes. | Plan §2.2 item B (phase P2). |
| `AMap.ofHashMap` / `AMap.ofSeq` over `HashMap` | frozen hash map type | We have no frozen `HashMap`; the BCL `Dictionary`/`Map` is the boundary. | Recorded deviation (§4.2). |
| `ASet.unionMany` dynamic form | `aset<aset<'T>> -> aset<'T>` | Our `unionMany` folds a static `seq`; the dynamic form is `ASet.collect id`. | Recorded deviation (§4.2). |

## 1. Type aliases

FDA publishes short lowercase aliases; we publish only the long names. Add all
six. They are pure type abbreviations: no runtime change, no behavior change.
— DONE (2026-08-04).

| Add | Definition | File |
|---|---|---|
| `aval<'T>` | `IAdaptiveValue<'T>` | `Library.fs` (after the interface) |
| `cval<'T>` | `ChangeableValue<'T>` | `Library.fs` (after the type) |
| `aset<'T>` | `IAdaptiveSet<'T>` | `Collections/Shared.fs` (after the interface) |
| `amap<'K,'V>` | `IAdaptiveMap<'K,'V>` | `Collections/Shared.fs` (after the interface) |
| `cset<'T>` | `ChangeableSet<'T>` | `Collections/Changeable.fs` (after the type) |
| `cmap<'K,'V>` | `ChangeableMap<'K,'V>` | `Collections/Changeable.fs` (after the type) |

Open question: do we also rename the interfaces from `IAdaptiveSet`/`IAdaptiveMap`
to `IAdaptiveHashSet`/`IAdaptiveHashMap`? FDA's names carry the "hash" qualifier
because it also ships a tree-backed `AdaptiveMap`. We have no tree-backed map, so
the alias mapping above keeps our names. **Decision: keep our names; alias only.**
Revisit only if we add a comparison-constrained map.

Exit: a compile-clean tree with the six aliases exported, and a test that opens
each alias in a signature position.

## 2. AVal module

### 2.1 Missing functions (additive — new nodes, no change to existing ones)

| Function | FDA signature | Plan |
|---|---|---|
| `force` | `aval<'T> -> 'T` | **DONE** as alias of `getValue`. |
| `init` | `'T -> cval<'T>` | **DONE** as `AVal.init`, body `ChangeableValue value`. |
| `delay` | `(unit -> 'T) -> aval<'T>` | **DONE** as `LazyConstantValue<'T>` (separate node; the eager `ConstantValue` keeps its zero-overhead read path). |
| `mapNonAdaptive` | `('T1 -> 'T2) -> aval<'T1> -> aval<'T2>` | Not 1-1, deferred. See §7. |
| `bind2` | `('T1 -> 'T2 -> aval<'T3>) -> aval<'T1> -> aval<'T2> -> aval<'T3>` | **DONE** as `AdaptiveNode` over the inner read. Dependency-set comparison handles the inner swap. |
| `bind3` | `('T1 -> 'T2 -> 'T3 -> aval<'T4>) -> ... -> aval<'T4>` | **DONE** as `bind2`. |
| `custom` | `AdaptiveToken -> 'T` (FDA) | **DONE** as `(unit -> 'T) -> aval<'T>` over `AdaptiveNode`. Token deviation documented (§7). |

Exit: each new function has a test (value correctness, dirtiness after a source
write, constant-fold behavior where applicable).

### 2.2 Semantic differences (need a decision + benchmark)

These two are the only items that touch the hot path. They are gated on a
BenchmarkDotNet before/after per `AGENTS.md`.

**A. Constant folding in `map`/`map2`/`map3`/`bind`.**
FDA detects `value.IsConstant` and returns a `ConstantVal` instead of a node.
`AVal.map f (AVal.constant 5)` never recomputes and tracks no dependency. Our
`map` always builds an `AdaptiveNode`. To match: add an `IsConstant` notion (we
have `ConstantValue`; check by type) and short-circuit to a lazy constant in the
combinators.

**B. `cheapEqual` output caching in `MapVal`/`Map2Val`/`Map3Val`.**
FDA caches the last input(s) and output. On a dirty recompute, if the new inputs
are `cheapEqual` (shallow equality, `ShallowEqualityComparer`) to the cached
inputs, the mapping does not run and the cached output is returned. Our nodes
recompute and call the mapping every dirty read.

Impact note: our sources already do equality-at-the-source (a no-op write does
not mark), so the gain is narrow — it matters when an upstream value returns to a
prior value through computation, or when a struct/record output is compared. The
storage cost is one `ValueOption<struct(...)>` per node. Benchmark before adding.

Exit (for both): benchmark in `benchmarks/AdaptiveSlop.Benchmarks`, recorded in
`docs/archive/2026-08-04-BENCHMARKS.md` (or a successor). Apply only if the data shows a win
on a realistic workload and the allocation budget holds.

### 2.3 `cast` (separate — needs the untyped layer)

FDA's `IAdaptiveValue` is split: an untyped `IAdaptiveValue` with
`GetValueUntyped`, `ContentType`, and an `Accept` visitor, plus the typed
`IAdaptiveValue<'T>`. Our `IAdaptiveValue<'T>` is a single interface. `cast`
needs the untyped layer.

This is a public-interface change. **Deferred** unless the user wants FDA-style
reflection/visitor interop. If taken, do it as its own phase: add the untyped
interface, implement on every node, then add `cast`.

## 3. `cval` / `ChangeableValue`

FDA has no `CVal` module; it uses the type and its members. We keep our `CVal`
module (it is an addition, not a gap) and add the missing members.

| Item | FDA | Plan |
|---|---|---|
| `.Value` get/set property | yes | **DONE**. Setter routes through `Set` (defers inside a transaction); getter is the raw field. |
| `.UpdateTo : 'T -> bool` | yes | **DONE**. Returns whether the value changed; equal writes return `false` and mark nothing. |
| `.GetValue()` member | yes (FDA takes a token) | **DONE** as `member GetValue : unit -> 'T` (registers a dependency; no token here). |
| `Post` / `CVal.post` | no (our addition) | Keep. |

Exit: tests for `Value` get/set (including inside a transaction) and `UpdateTo`
return value. — DONE.

## 4. Collection constructors

### 4.1 Missing

| Constructor | FDA | Plan |
|---|---|---|
| `ASet.empty` | `aset<'T>` | **DONE**. `ConstantSet` over `FrozenSet<'T>.Empty`; one shared instance per `'T`. |
| `AMap.empty` | `amap<'K,'V>` | **DONE**. `ConstantMap` over `FrozenDictionary<'K,'V>.Empty`. |
| `AMap.constant` | `(unit -> HashMap<'K,'V>) -> amap<'K,'V>` | **DONE** as `(unit -> Dictionary<'K,'V>) -> amap<'K,'V>` (deviation: no frozen HashMap; `Dictionary` is the hash boundary). |
| `AMap.delay` | no FDA equivalent | **DONE** as alias of `constant` (symmetry with `ASet.delay`). |

Exit: each has a test (empty has count 0, never marks; `constant` runs the
creator once). — DONE.

### 4.2 Documented deviations (already in place, record here)

- `AMap.ofHashMap` is `AMap.ofMap` (we have no frozen HashMap type; the BCL
  `Map`/`Dictionary` is the boundary).
- `ASet.unionMany` is the static `seq` fold; the dynamic `aset<aset<'T>>` form
  is `ASet.collect id` (§7.4).
- `ASet.ofHashMap` is not provided; use `ASet.ofHashSet` over the BCL `HashSet`.
- `force` returns `FrozenSet`/`FrozenDictionary`, not FDA's `CountingHashSet`/
  `HashMap` (`PLAN.md` §6.9). This is by design.

## 5. Phasing

Each phase ships green in Debug and Release, with `dotnet fantomas .` run before
staging. No push without permission; no force push.

### Phase P0 — Aliases and trivial constructors
- §1 all six aliases. — DONE.
- §2.1 `force`, `init`. — DONE.
- §3 `.Value` get/set, `.UpdateTo`, `.GetValue()`. — DONE.
- §4.1 `ASet.empty`, `AMap.empty`, `AMap.constant`, `AMap.delay`. — DONE.
- DSL modules use the short aliases in signatures and no result casts. — DONE.
- Exit: suite green; a test opens each alias; `Value`/`UpdateTo` behave inside a
  transaction. — DONE (160 tests, Debug and Release).

### Phase P1 — New AVal nodes
- §2.1 `delay`, `bind2`, `bind3`, `custom`. — DONE.
- §2.1 `mapNonAdaptive`. — Deferred to §7 (not 1-1).
- Exit: suite green; one test per function; the `bind2`/`bind3` swap eagerly
  unregisters the old inner (leak test, mirroring §7.4). — DONE (switch-back
  covered; eager unregistration is inherent to the dependency-set comparison).

### Phase P2 — Semantic parity (gated on benchmark)
- §2.2 constant folding in `map`/`map2`/`map3`/`bind`.
- §2.2 `cheapEqual` output caching in the map nodes.
- Exit: benchmark recorded; applied only if the data shows a win and the
  allocation budget holds.

### Phase P3 (optional) — Untyped layer and `cast`
- §2.3 untyped `IAdaptiveValue` + visitor, then `cast`.
- Only if the user wants reflection/visitor interop.

## 6. Open questions

1. Rename collection interfaces to `IAdaptiveHashSet`/`IAdaptiveHashMap`?
   **Tentative: no** (alias only). Revisit if we add a comparison map.
2. Do we want the untyped/visitor layer (§2.3) at all? It is only useful for
   reflection-style code and `cast`.
3. `mapNonAdaptive` semantics in our pull model: confirm "always re-read and
   map, skip the dirty cache" is the right reading of FDA's decorator.
   **Tentative: deferred** (see §7) — our clean read is already one flag
   check, so the always-evaluate node has no measured use case yet.
