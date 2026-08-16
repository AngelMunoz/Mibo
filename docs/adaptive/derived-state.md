---
title: Derived State
category: Adaptive
categoryindex: 3
index: 10
---

# Derived State

Most games keep values that aren't facts — they're functions of facts. The bee count, "any bee hungry?", the scoreboard string, the flower with the nearest bee. Two ways to keep them: recompute them yourself when something changes, or declare them and let the graph keep them current. This architecture is built on the second.

## Declare, don't recompute

```fsharp
// Facts — you write these
let bees = CMap.empty<int, Bee>
let honey = CVal.create 0

// Derived values — you never update these by hand
let beeCount = bees |> AMap.count
let scoreboard = honey |> AVal.map(fun h -> $"Honey: {h}")
let anyHungry =
    bees |> AMap.exists(fun _ bee -> bee.Energy < 0.2f)
```

Build these once at startup. After that they are always current: read them in update, in the frame, in a test — whenever a fact changed since your last read, the value recomputes; when nothing changed, the read is a cheap version check. The code that used to increment a count, rebuild a list, and format a string in three different places simply disappears.

## Where derived values live

The placement rule is one line: **a value derived from one feature's data lives next to that feature; a value derived from two features lives at the top level.**

```fsharp
// Next to Bees: only touches the bees map
let alive = bees |> AMap.filter(fun _ bee -> bee.Hp > 0)

// Top level: joins bees with flowers — not inside either feature
let pollinated =
    bees
    |> AMap.mapA(fun _ bee ->
        flowers |> AMap.tryFind bee.NearestFlower)
```

The split keeps each feature understandable on its own: nothing in the flowers module should know what a bee is, and the join that needs both belongs where both are visible. [Systems](systems.html) covers the feature organization this builds on.

## What it buys

* **No stale reads.** The HUD can't show last frame's count, because there is no last frame's count — only the derived value, always current.
* **No wasted work.** The scoreboard recomputes only when honey changes. A paused frame recomputes nothing — every read is a version check.
* **No allocation on steady state.** These reads are the per-frame hot path, and they're free.

For the mechanics — `map`/`bind`/`mapN` on values, `filter`/`mapA`/joins on collections, and what each costs — see the [Mibo.Adaptive](../mibo-adaptive/overview.html) section, particularly [Performance](../mibo-adaptive/performance.html) for how joins scale.
