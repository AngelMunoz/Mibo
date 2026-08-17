---
title: Collections
category: Mibo.Adaptive
categoryindex: 4
index: 3
---

# Collections: CSet/CMap/CList and their views

Sets, maps, and lists propagate **element-level deltas** (added/removed/updated) instead of recomputing wholesale. Writes are journaled (zero allocation); nodes process pending deltas on read.

```fsharp
let items = CSet.ofSeq [ 1; 2; 3 ]

let double (x: int) = x * 2
let isBig (x: int) = x > 2

let doubled = items |> ASet.map double
let filtered = items |> ASet.filter isBig

items.Add(4)    // downstream nodes process one element, not the whole set
```

```fsharp
let entries = CMap.empty<int, string>
let lookup = entries |> AMap.tryFind 1          // aval<string voption>
let lengths = entries |> AMap.mapV String.length
```

```fsharp
let sequence = CList.empty<int>
let total = sequence |> AList.sum               // aval<int>, tracks the list
let sorted = sequence |> AList.sort             // stable, positional
```

## Per-element adaptive mapping

`mapA` / `filterA` / `chooseA` (plus the positional `mapiA` on lists) map each element to an `aval`; the output follows each element's aval, and entries whose aval holds `None`/`ValueNone` are dropped:

```fsharp
let entityView id = world |> AMap.tryFind id

// chooseV with `id` (F#'s identity function) drops the ValueNones:
// ids the world no longer has fall out of the view
let statuses =
    entities
    |> ASet.mapA entityView
    |> ASet.chooseV id
```

## Joins

`AMap.joinOn` is the same-key join for maps, the recommended low-churn form:

```fsharp
// Healths × Motions per enemy: same key, combined row
let sameEnemy (eid: int<EnemyId>) _ = eid

let combineRow _ (healthV: aval<float32>) (motionV: aval<Vector2 voption>) =
    let merge (h: float32) (m: Vector2 voption) =
        match m with
        | ValueSome motion -> combine h motion |> ValueSome
        | ValueNone -> ValueNone

    AVal.map2 merge healthV motionV

let views = AMap.joinOn healths motions sameEnemy combineRow
```

Note the shape: the two maps come first; the pipe form does not apply, a piped value would land in the mapping slot. The right-hand value arrives as `aval<Vector2 voption>` (`ValueNone` when the key has no entry in the right map), and the mapping returns `aval<'U voption>`: a `ValueNone` result drops the entry from the join.

Nested joins compose: a three-way view joins the two-way result with a third map the same way. If a join spans two features of your game, build it at the top level rather than inside one feature, so each feature stays understandable alone ([Adaptive Systems](../adaptive/systems.html) covers the split).

## Reading: snapshots vs. immutable copies

* `ASet.getValue` / `AMap.getValue` / `AList.getValue` return a **snapshot** of the current state, valid only until the next write. Consume it and move on; never store it and never mutate it.
* `ASet.force` / `AMap.force` / `AList.force` build an immutable copy. This is the only collection operation that allocates, and the only result safe to keep; the library never touches a forced value again.
* `ASet.toSet` / `AMap.toMap` (and `CSet.toSet` / `CMap.toMap`) build the F# `Set`/`Map` counterparts for sorted iteration and interop.

### Which one, when

`getValue` is the default, and the per-frame rule in a game is this: **if the value is consumed before the next write, read it with `getValue`.** The projection in a Mibo game runs right before drawing and nothing writes during it, so it packs with `getValue` and that's the end of it.

Reach for `force` when the data has to survive past the next write, or leave the thread that owns the graph:

| Situation | Use | Why |
|---|---|---|
| Packing the frame for the renderer | `getValue` | Free, consumed immediately on the same thread |
| Handing data to another thread (your own render thread, a worker) | `force` | Snapshots belong to the graph's owner thread; an immutable copy doesn't |
| Sending state over the network / saving to disk | `force` | You serialize an immutable value that can't change under you |
| Keeping a value for later frames (history, interpolation buffers) | `force` | `getValue` results are invalid after the next write |
| A one-time read in setup code, F# pattern matching on the structure | `toSet`/`toMap` | The immutable F# collections integrate with the rest of F# |

Iteration speed itself is comparable; both enumerate the current contents. The difference is lifetime: a `getValue` snapshot is borrowed, a `force`d copy is yours.

The one thing not to do is call `force` per frame out of caution: it allocates on every call, and at 60 fps that is real garbage for nothing.

## Lifetimes and capabilities

* Derived collections register with their dependencies lazily (first read) and are `IDisposable`; disposal stops all delta processing. Reading a disposed node throws.
* The collection interfaces do not require F#'s `comparison` constraint (they are hash-based internally); the F#-interop helpers re-impose it at their boundary.
* External snapshots: `AVal.ofExternal`, `ASet.ofExternal`, `AMap.ofExternal`, `AList.ofExternal` wrap a foreign mutable source with an explicit `invalidate` handle; reads are O(1) until you invalidate, then re-read the snapshot once.

For the cost profile of joins and `mapA` (the coarse scan), see [Performance](performance.html).
