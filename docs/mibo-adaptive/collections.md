---
title: Adaptive Collections
category: Mibo.Adaptive
categoryindex: 4
index: 3
---

# Collections: CSet/CMap/CList and their views

Sets, maps, and lists propagate **element-level deltas** (added/removed/updated) instead of recomputing wholesale. Writes are journaled (zero allocation); nodes process pending deltas on read.

```fsharp
let items = CSet.ofSeq [ 1; 2; 3 ]

let doubled = items |> ASet.map (fun x -> x * 2)
let filtered = items |> ASet.filter (fun x -> x > 2)

items.Add(4)    // downstream nodes process one element, not the whole set

let entries = CMap.empty<int, string>
let lookup = entries |> AMap.tryFind 1          // aval<string voption>
let lengths = entries |> AMap.mapV String.length

let sequence = CList.empty<int>
let total = sequence |> AList.sum               // aval<int>, tracks the list
let sorted = sequence |> AList.sort             // stable, positional
```

## Per-element adaptive mapping

`mapA` / `filterA` / `chooseA` (plus the positional `mapiA` on lists) map each element to an `aval`; the output follows each element's aval, and entries whose aval holds `None`/`ValueNone` are dropped:

```fsharp
let statuses =
    entities
    |> ASet.mapA(fun id -> world |> AMap.tryFind id)
    |> ASet.chooseV id
```

## Joins

`AMap.joinOn` is the same-key join for maps — the sanctioned low-churn form:

```fsharp
// Healths × Motions per enemy: same key, combined row
let views =
    healths
    |> AMap.joinOn
        motions
        (fun eid _ -> eid)
        (fun _ healthV motionV -> healthV |> AVal.map2 (fun h m -> combine h m) motionV)
```

Nested joins compose — a three-way view joins the two-way result with a third map the same way. Keep joins **owned by the composition root** when they span two systems' maps ([Adaptive Systems](../adaptive/systems.html) has the ownership rule).

## Reading: transient views vs. forced checkpoints

* `ASet.getValue` / `AMap.getValue` / `AList.getValue` return a **transient view** — valid only until the next write. Consume it (the per-step tick, the frame pack); never retain or mutate it.
* `ASet.force` / `AMap.force` / `AList.force` materialize an immutable checkpoint. This is the only collection operation that allocates, and the only result safe to retain — the library never touches a forced value again.
* `ASet.toSet` / `AMap.toMap` (and `CSet.toSet` / `CMap.toMap`) materialize the F# `Set`/`Map` counterparts for sorted iteration and interop.

## Lifetimes and capabilities

* Derived collections register with their dependencies lazily (first read) and are `IDisposable`; disposal stops all delta processing. Reading a disposed node throws.
* The collection interfaces do not require `: comparison` (hash-based internally); the F#-interop helpers re-impose it at their boundary.
* External snapshots: `AVal.ofExternal`, `ASet.ofExternal`, `AMap.ofExternal`, `AList.ofExternal` wrap a foreign mutable source with an explicit `invalidate` handle — reads are O(1) until you invalidate, then re-read the snapshot once.

For the cost profile of joins and `mapA` (the coarse scan), see [Performance](performance.html).
