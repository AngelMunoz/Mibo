---
title: Derived State
category: Adaptive
categoryindex: 3
index: 10
---

# Derived State — Organizing Projections

A growing game accumulates values that aren't facts but functions of facts: the alive count, a tower's upgraded stats, "is a boss near this tower", the scoreboard string. These are projections, and the question that matters is not *how* to build one (that's the [Mibo.Adaptive](../mibo-adaptive/overview.html) section) but **where it lives and when it stays cheap.**

## The two homes

Every projection has one correct home, decided by a single question: **does it touch more than one feature's data?**

**Inside the feature** when it reads only that feature's own containers. Build it in the feature's `init`, store it on the model, done:

```fsharp
module Towers =
    let inline private withLevel (statics: TowerStatic) (level: int voption) : TowerDef =
        effectiveDef statics.Def (level |> ValueOption.defaultValue 1)

    let init() : TowersModel =
        let m = TowersModel()

        m.EffectiveDef <-
            AMap.joinOn m.Statics m.Levels (fun tid _ -> tid) (fun _ s lvl ->
                AVal.map2 withLevel s lvl)

        m
```

**At the top level** when it joins two features that don't know about each other. Two unrelated systems each own their data; a third party (the frame, another system, a test) needs the combination. That cross-system projection goes in one `Projections` object the composition root owns — *not* inside either system, because then one system would have to know the other exists:

```fsharp
let inline suppressedBy (tower: TowerStatic) (enemies: EnemiesModel) : aval<float32> =
    enemies.BossPositions
    |> AMap.filter(fun _ pos -> Vector2.Distance(pos, tower.Cell |> center) <= BossAura.Radius)
    |> AMap.count
    |> AVal.map(fun n -> if n > 0 then BossAura.Factor else 1f)

type Projections(enemies: EnemiesModel, towers: TowersModel, ...) =

    // Towers × Bosses: neither system knows the other. The frame and
    // Towers.tick both need the per-tower suppression factor.
    member val Suppression: amap<int<TowerId>, float32> =
        AMap.mapA (fun _ t -> suppressedBy t enemies) towers.Statics
```

Reserve the top-level `Projections` for cross-system data only. If a projection can live next to its feature, it should — putting a single-feature projection at the top level scatters that feature's logic for no benefit.

## Build once, never per frame

Construct each projection once — at startup, or when the feature's model is created. A projection built inside `update` allocates a new graph node every frame and throws away the whole point (a settled node's reads are free only because the node persists). Build it in `init`, keep it, read it as often as you want.

## Performance, in practice

The graph is cheap enough for real games. In the Defli tower-defense samples — a full nine-system world with homing projectiles, a per-tower boss-suppression join, and live HUD reads — the entire adaptive machinery costs well under a fifth of a millisecond per frame at 60 fps, with all entities active. You don't need to ration projections.

The costs that *do* show up in a profile are almost always one of these two, and both are yours to control:

* **Allocating unknowingly.** Building a projection node per frame, or calling `force`/`toMap` in the frame loop "to be safe", allocates real garbage at 60 fps. Steady-state reads allocate nothing — the drip only appears when you create nodes or materialize copies on the hot path.
* **A live join that rescans every frame.** A `mapA` over one collection that filters another collection rechecks the inner one whenever it changes. Mixing something that changes every frame (positions, time) with a join means the inner scan re-runs constantly, and it grows linearly with the collections. At some size it stops being worth it. The fix is not to fear joins — it's to notice, and drop to a plain loop over the data in `update` where you control the cost directly. A projection is a convenience for reads, not a rule that everything must stay derived.

For the per-combinator cost model, see [Mibo.Adaptive Performance](../mibo-adaptive/performance.html).
