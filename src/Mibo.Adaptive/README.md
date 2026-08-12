# Mibo.Adaptive

A pull-based incremental computation library in F#, part of [Mibo](https://github.com/AngelMunoz/Mibo). It tracks dependencies automatically and recomputes only what changed, focusing on allocation-free steady state and tight-loop workloads (games, simulations, real-time UIs).

> **Origin.** Mibo.Adaptive originated from [AdaptiveSlop](https://github.com/TheAngryByrd/AdaptiveSlop) by [TheAngryByrd](https://github.com/TheAngryByrd). The library was adopted in its entirety, renamed, and is now maintained here as part of Mibo — the pull-based design and allocation-free steady state remain its core. See [Credit](#credit).

The main target is the tight-loop profile: many values change between reads, and
reads must be cheap and allocation-free.

The canonical shape is a derived state graph forced once per tick — for example, a
physics-simulation cache that derives per-entity state from a set of entities.

Write as many times as you want between reads, they're "free" until you read.
Or do the inverse, read as many times as you want between writes, they're "free" until the next read after write.

## Design

- **Pull-lazy evaluation.** Writes bump versions. Reads compute, per dirty node, once
  per change. Ten writes before one read cost one recompute. A read at a settled state
  is O(1).
- **Zero allocation on steady state.** Delta buffers are reused. Steady-state reads and
  writes allocate nothing.
- **Coarse scans instead of per-entry bookkeeping.** mapA nodes re-check every entry's
  version on read after a write. The trade is flat overhead in exchange for a scan
  after each write.
- **Transactions.** Writes inside `Transaction.run` apply at commit.

## When to use FSharp.Data.Adaptive instead

FSharp.Data.Adaptive is the mature choice for general incremental computing:

- It has the full API surface, `IndexList`, history, and Fable/JS support.
- It wins on very wide dependency graphs and on large mapA collections, where the coarse
  scan loses.

Rule of thumb: general incremental computing → FSharp.Data.Adaptive. Tight loops with
cheap reads → Mibo.Adaptive.

## Building

```bash
dotnet build Mibo.slnx
```

The core (`src/Mibo.Adaptive`) depends only on the BCL. The library ships as the
`Mibo.Adaptive` NuGet package; the Mibo integration (`AdaptiveProgram`/
`AdaptiveHeadless`) lives in `Mibo.Core`.

## Quick Start

```fsharp
open Mibo.Adaptive

let width = CVal.create 10.0
let height = CVal.create 20.0

// Computed values track dependencies automatically
let area =
    width
    |> AVal.map2 (fun w h -> w * h) height

AVal.getValue area   // 200.0
width.Set(15.0)
AVal.getValue area   // 300.0
```

## Core Concepts

### Changeable values (`CVal`) — the inputs

```fsharp
let counter = CVal.create 0
counter.Set(42)
```

A changeable value is already the adaptive view. Pass it directly to the combinators;
`CVal.value` is an explicit upcast, optional except where the interface type must be
named.

### Adaptive values (`AVal`) — computed nodes

```fsharp
let doubled =
    counter
    |> AVal.map (fun x -> x * 2)

let sum =
    a
    |> AVal.map2 (fun a b -> a + b) b

let rgb =
    r
    |> AVal.map3 (fun r g b -> (r, g, b)) g b
```

Recomputation is lazy: nothing recomputes until you call `GetValue()`, and then only if a
dependency changed since the last read.

**Wide fan-in (5+ inputs):** use the single-node operations — dramatically faster than
chaining `map2`:

```fsharp
let deps =
    sensors
    |> Array.map (_.Dep)

let average =
    deps
    |> AVal.mapN (fun values -> Array.average values)

let total =
    deps
    |> AVal.reduce 0.0 (+)      // no intermediate array

let intSum =
    intDeps
    |> AVal.sum                 // convenience for int
```

### Transactions

```fsharp
Transaction.run (fun () ->
    width.Set(100.0)
    height.Set(50.0))
// both changes apply atomically at commit
```

Note: changes inside a transaction are applied at commit — reads _inside_ the transaction
still see the pre-transaction values.

However, transactions are not strictly necessary: writes outside a transaction apply immediately.
They are useful for batching multiple writes into one notification delivery.

### Cross-thread posting

A graph belongs to one **owner thread**. Only the owner reads and writes the graph.
Foreign threads send changes with `Post`; the owner's next graph operation applies them
automatically:

```fsharp
// worker thread
CVal.post (health - 1) health

// owner thread: no pump call needed
let h = AVal.getValue health
```

Posting rules:

- `Post` is lock-free and allocates nothing. It writes a typed pending field and, if the
  source is not queued yet, pushes the source onto a bounded preallocated ring.
- Pending posts are applied automatically at the start of the next graph operation on
  the owner thread, as one batch with one notification delivery. Several posts to one
  source before the application collapse to one application of the last value.
- The source equality check still applies at application: posting an equal value marks
  nothing.
- `Posting.pump()` is optional: it forces application at a chosen boundary (for
  example, once per frame). It runs on the owner thread only and is cheap and
  allocation-free when the queue is empty.

### Adaptive collections

Sets, maps, and lists propagate **element-level deltas** (added/removed/updated) instead
of recomputing wholesale. Writes are journaled (zero allocation); nodes process pending
deltas on read:

```fsharp
let items = CSet.ofSeq [1; 2; 3]

let doubled =
    items
    |> ASet.map (fun x -> x * 2)

let filtered =
    items
    |> ASet.filter (fun x -> x > 2)

items.Add(4)    // downstream nodes process one element, not the whole set

let entries = CMap.empty<int, string>

let lookup =
    entries
    |> AMap.tryFind 1            // aval<string voption>

let names =
    entries
    |> AMap.mapV String.length

let sequence = CList.empty<int>

let total =
    sequence
    |> AList.sum                 // aval<int>, tracks the list

let sorted =
    sequence
    |> AList.sort                // stable, positional
```

Per-element adaptive mapping is available on all three collections (`mapA`/`filterA`/
`chooseA`, plus the positional `mapiA` on lists): the mapping returns an `aval`, the
output follows each element's aval, and entries whose aval holds `None`/`ValueNone` are
dropped:

```fsharp
let statuses =
    entities
    |> ASet.mapA(fun id -> world |> AMap.tryFind id)
    |> ASet.chooseV id
```

- `ASet.getValue` / `AMap.getValue` / `AList.getValue` return a **transient view** of the
  internal state: valid only until the next write. Computations consume it; do not
  retain or mutate it.
- `ASet.force` / `AMap.force` / `AList.force` materialize an immutable checkpoint. This
  is the only collection operation that allocates, and the only result safe to retain:
  the library never touches a forced value again.
- `ASet.toSet` / `AMap.toMap` (and `CSet.toSet` / `CMap.toMap`) materialize the F#
  `Set`/`Map` counterparts for sorted iteration and F# interop.
- Derived collections register with their dependencies lazily (first read) and are
  `IDisposable`; disposal stops all delta processing. Reading a disposed node throws.
- The collection interfaces do not require `: comparison` (hash-based internally); the
  F#-interop helpers re-impose it at their boundary.
- External snapshots: `AVal.ofExternal`, `ASet.ofExternal`, `AMap.ofExternal`,
  `AList.ofExternal` wrap a foreign mutable source with an explicit `invalidate` handle.
  Reads are O(1) until invalidated, then re-read the snapshot once.

## API surface

- **AVal** — `constant`, `delay`, `init`, `ofExternal`, `map`, `map2`, `map3`, `map4`,
  `mapN`, `reduce`, `sum`, `bind`, `bind2`, `bind3`, `getValue`, `force`
  (+ `Task`/`ValueTask` variants)
- **CVal** — `create`, `value`, `set`, `post`
- **CSet** — `empty`, `ofSeq`, `add`, `remove`, `set`, `updateTo`, `perform`, `unionWith`,
  `exceptWith`, `intersectWith`, `value`, `force`, `toSet`
- **CMap** — `empty`, `ofSeq`, `addOrUpdate`, `remove`, `set`, `updateTo`, `perform`,
  `containsKey`, `tryGetValue`, `item`, `clear`, `value`, `force`, `toMap`
- **CList** — `empty`, `append`, `insertAt`, `removeAt`, `updateAt`, `addRange`,
  `updateTo`, `perform`, `value`, `force`, `toArray`
- **ASet** — `map`, `filter`, `choose`, `chooseV`, `chooseA`, `chooseAV`, `union`, `intersect`,
  `mapA`, `filterA`, `collect`, `collect'`, `bind`, `bind2`, `bind3`, `range`, `mapUse`,
  `count`, `countBy`, `sum`, `average`, `sort`, `custom`, `getValue`, `force`, `toSet`,
  `ofSeq`, `reduceByA`, `countByA`, `existsA`, `forallA`, `sumByA`, `averageByA`,
  `tryMinA`, `tryMaxA`
- **AMap** — `map`, `mapV`, `filter`, `filterV`, `choose`, `chooseV`, `chooseA`, `chooseAV`,
  `choose2`, `choose2V`, `unionWith`, `union`, `intersect`, `intersectV`, `intersectWith`,
  `difference`, `groupBy`, `joinOn`, `mapA`, `filterA`, `mapUse`, `bind`, `bind2`, `bind3`,
  `keys`, `toASet`, `fold`, `foldGroup`, `foldHalfGroup`, `sumBy`, `averageBy`, `tryFind`,
  `find`, `getValue`, `force`, `toMap`, `ofSeq`, `reduceByA`, `countByA`, `existsA`,
  `forallA`, `sumByA`, `averageByA`, `tryMinA`, `tryMaxA`
- **AList** — `map`, `mapi`, `filter`, `choose`, `chooseV`, `choosei`, `chooseiV`, `indexed`,
  `mapA`, `mapiA`, `filterA`, `chooseA`, `chooseAV`, `chooseiA`, `chooseiAV`, `append`,
  `concat`, `bind`, `bind2`, `bind3`, `ofAVal`, `ofSeq`, `ofArray`, `range`, `init`,
  `toAVal`, `tryAt`, `tryGet`, `tryFirst`, `tryLast`, `rev`, `sort`, `pairwise`, `take`,
  `takeA`, `skip`, `skipA`, `sub`, `subA`, `reduce`, `reduceBy`, `fold`, `foldGroup`,
  `foldHalfGroup`, `exists`, `forall`, `countBy`, `tryMin`, `tryMax`, `sum`, `sumBy`,
  `average`, `averageBy`, `mapUse`, `custom`, `getValue`, `force`, `toArray`, `reduceByA`,
  `countByA`, `existsA`, `forallA`, `sumByA`, `averageByA`, `tryMinA`, `tryMaxA`
- **Transaction** — `run`
- **Posting** — `pump`

## Performance

The library aims to be fast. Steady-state reads and writes try to allocate nothing.

Benchmarks exist in `docs/BENCHMARKS.md`. They compare the library against
FSharp.Data.Adaptive on identical workloads.
While the workloads are shaped for FDA, Mibo.Adaptive still performs well.

Guidance: `map`/`map2` for 1–2 deps, `map3`/`map4` for 3–4, `mapN`/`reduce`/`sum` for
5+; consume collection **deltas** on hot paths rather than re-reading whole snapshots.

## Architecture

- **Pull-only, version-checked.** A write bumps the source's version and the graph's
  write generation. A read compares the recorded dependency versions against the
  current ones and recomputes only when one moved; the write-generation cache keeps
  repeated reads at a settled state O(1) per node.
- **Dependencies re-read on every recompute.** Dependency sets are re-discovered on
  every recompute, so dynamic graphs (`bind`) stay correct.
- **Collections push deltas.** Changeable collections journal added/removed/updated
  elements and push them to derived nodes, which update per-element (with ref-counting
  for shared outputs).

## Limitations

- **mapA scan.** mapA/filterA/chooseA nodes re-check every entry's version on
  each read after a write — O(N) per change, deliberate in exchange for zero per-entry
  bookkeeping.
- **Wide fan-out.** Very wide dependency graphs re-validate each level on a write and
  degrade exponentially with the fan-out.
- **Large list forces.** Forcing a large list materializes the output array per read.
- **No Fable/JS support**, no history, and a smaller API surface than
  FSharp.Data.Adaptive.
- The library is young. The API is covered by the unit suite and the FsCheck property
  suite, but production mileage is limited.

## Credit

Mibo.Adaptive originated from [AdaptiveSlop](https://github.com/TheAngryByrd/AdaptiveSlop)
by [TheAngryByrd](https://github.com/TheAngryByrd) — adopted in its entirety and renamed;
all design and implementation credit goes to its author.

AdaptiveSlop (and therefore Mibo.Adaptive) is inspired by
[FSharp.Data.Adaptive](https://github.com/fsprojects/FSharp.Data.Adaptive). The API
follows its conventions (`mapA`/`filterA`/`chooseA`, `AdaptiveReduction`, transactions, deltas).
Credit and thanks to the FDA authors.

## License

MIT
