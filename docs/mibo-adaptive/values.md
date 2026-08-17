---
title: Values
category: Mibo.Adaptive
categoryindex: 4
index: 2
---

# Values: CVal and AVal

Scalars in the graph come in two kinds: **changeable inputs** you write, and **adaptive outputs** the library computes.

## Changeable values: the inputs

```fsharp
let counter = CVal.create 0
counter.Set(42)
```

A changeable value (`cval<'T>`) is already the adaptive view, so pass it directly to the combinators. `CVal.value` is an explicit conversion to the general `aval<'T>` type; it is optional except where a type annotation demands the general type. `Set` is equality-gated: writing an equal value marks nothing dirty.

## Adaptive values: computed nodes

```fsharp
let double (x: int) = x * 2
let add (x: int) (y: int) = x + y
let toRgb (r: float) (g: float) (b: float) = (r, g, b)

let doubled = counter |> AVal.map double
let sum = width |> AVal.map2 add height
let rgb = red |> AVal.map3 toRgb green blue
```

Recomputation is lazy: nothing recomputes until you read (`AVal.getValue`), and then only if a dependency changed since the last read. Dependencies are tracked automatically, including *dynamic* ones: with `AVal.bind` the dependency set can change between reads, and the graph re-wires itself.

```fsharp
// bind: the followed value depends on which option is active
let lookup id = world |> AMap.tryFind id

let current = selection |> AVal.bind lookup
```

## Wide fan-in: single-node operations

For five or more inputs, the single-node operations are much faster than chaining `map2`:

```fsharp
let dep (s: Sensor) = s.Dep

let deps = sensors |> Array.map dep
let intDeps = counters |> Array.map dep

let averageOf (values: float[]) = Array.average values

let average = deps |> AVal.mapN averageOf
let total = deps |> AVal.reduce 0.0 (+)      // no intermediate array
let intSum = intDeps |> AVal.sum             // convenience for int
```

`mapN` builds one node with N dependencies; `reduce` folds without materializing intermediate arrays. Both keep the wide graph shallow and the steady-state read allocation-free.

## Task and async variants

Task-based `map` variants exist for deriving nodes from asynchronous computations (see the API reference); the node re-subscribes when inputs change. Use them for cold paths only; the hot read path stays synchronous and allocation-free.

## Reading

`AVal.getValue` computes if dirty and caches: at most one recompute per change, per node. There is no push: no callbacks fire on write, so a write can never re-enter your code. In a Mibo game the projection reads your outputs once per step ([Adaptive Programs](../adaptive/program.html)); each dirty node on the path recomputes in dependency order.
