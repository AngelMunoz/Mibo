---
title: Adaptive Values
category: Mibo.Adaptive
categoryindex: 4
index: 2
---

# Values: CVal and AVal

Scalars in the graph come in two kinds: **changeable inputs** you write, and **adaptive outputs** the library computes.

## Changeable values — the inputs

```fsharp
let counter = CVal.create 0
counter.Set(42)
```

A changeable value (`cval<'T>`) is already the adaptive view — pass it directly to the combinators. `CVal.value` is an explicit upcast, optional except where the interface type must be named. `Set` is equality-gated: writing an equal value marks nothing dirty.

## Adaptive values — computed nodes

```fsharp
let doubled = counter |> AVal.map (fun x -> x * 2)
let sum = a |> AVal.map2 (fun a b -> a + b) b
let rgb = r |> AVal.map3 (fun r g b -> (r, g, b)) g b
```

Recomputation is lazy: nothing recomputes until you read (`AVal.getValue`), and then only if a dependency changed since the last read. Dependencies are tracked automatically — including *dynamic* ones: with `AVal.bind` the dependency set can change between reads, and the graph re-wires itself.

```fsharp
// bind: the followed value depends on which option is active
let current =
    selection
    |> AVal.bind(fun id -> world |> AMap.tryFind id)
```

## Wide fan-in: single-node operations

For five or more inputs, the single-node operations are dramatically faster than chaining `map2`:

```fsharp
let deps = sensors |> Array.map (_.Dep)

let average = deps |> AVal.mapN (fun values -> Array.average values)
let total = deps |> AVal.reduce 0.0 (+)      // no intermediate array
let intSum = intDeps |> AVal.sum             // convenience for int
```

`mapN` builds one node with N dependencies; `reduce` folds without materializing intermediate arrays. Both keep the wide graph shallow and the steady-state read allocation-free.

## Task and async variants

Task-based `map` variants exist for deriving nodes from asynchronous computations (see the API reference) — the node re-subscribes when inputs change. Use them for cold paths only; the hot read path stays synchronous and allocation-free.

## Reading

`AVal.getValue` computes if dirty and caches — at most one recompute per change, per node. There is no push: no callbacks fire on write, so writes can never re-enter your code. When the [frame builder](../adaptive/program.html) resolves outputs once per step, every node on the path settles in dependency order.
