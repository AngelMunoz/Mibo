---
title: Derived State
category: Adaptive
categoryindex: 3
index: 10
---

# Derived State — Organizing Projections

Once your game has more than one feature, you start writing values that join, filter, and reduce across feature data — a count of alive enemies, a scoreboard, a tower's effective stats after upgrades, the list of flowers a bee can reach. These are projections. Where you put them and how you build them decides whether your game stays understandable or turns into a web of reads. This is the organizational pattern for projections.

## The two homes

Every projection lives in one of two places, and the choice is one question: **does it touch more than one feature's data?**

**Inside the feature** when it reads only that feature's own containers. The feature builds it in its `init` and keeps it alongside the model:

```fsharp
module Towers =
    let init() : TowersModel =
        let m = TowersModel()
        // EffectiveDef reads only Statics + Levels — the tower's own data
        m.EffectiveDef <-
            m.Statics
            |> AMap.joinOn m.Levels (fun tid _ -> tid) (fun _ s lvl ->
                AVal.map2 (fun s (lvl: int voption) ->
                    ValueSome(effectiveDef s.Def (lvl |> ValueOption.defaultValue 1)))
                    s lvl)
        m
```

**At the top level** when it joins two features. Those live in one `Projections` object that the composition root owns, built after the feature models exist:

```fsharp
type Projections(enemies, towers, projectiles, ...) =
    // Suppression reads Towers.Statics and Enemies.BossPositions — two
    // features, so it lives here, not inside either system
    member val Suppression: amap<int<TowerId>, float32> =
        towers.Statics
        |> AMap.mapA(fun _ s ->
            enemies.BossPositions
            |> AMap.filter(fun _ bossPos -> inRadius s bossPos)
            |> AMap.count
            |> AVal.map(fun n -> if n > 0 then factor else 1f))
```

The rule keeps each feature self-contained: nothing in `Towers` knows what a `Boss` is, and nothing in `Enemies` knows what a tower's range is. Only the top-level projection sees both.

## Build once at init, never per frame

Projections are constructed once — at startup or when a feature's model is created — not inside `update`. A projection built per frame allocates a new graph node every frame and defeats the point (steady-state reads are free only if the node persists). Build it in `init`, store it on the model or the `Projections` object, and read it as many times as you like.

```fsharp
// Bad: rebuilt every frame
let update world ctx gameTime =
    let alive = world.Enemies.Alive |> AMap.count   // new node each call
    ...

// Good: built once, stored, read each frame
type World = { Enemies: EnemiesModel; AliveCount: aval<int>; ... }
// AliveCount = enemies.Alive |> AMap.count  -- set in init
```

## What the frame reads

The frame function reads projections and packs them. Read each once; don't re-read the same projection twice in one frame (the second read is a version check, but a wasted one). If two parts of the frame need the same derived value, read it into a local and reuse it.

```fsharp
let frame (world: World) () : Frame =
    let alive = world.Enemies.Alive |> AMap.getValue
    {
      AliveEnemies = alive
      AliveCount = alive.Count        // reuse, don't re-derive
      ...
    }
```

## When a join stops paying

A projection that filters a large collection inside a `mapA` over another collection re-scans the inner one every time it changes. When that gets expensive — you'll see it in a profile — don't keep paying for the live join. Drop to a plain row map over one collection and move the cross-feature behavior into update as direct values. The projection is a convenience for reads, not a requirement that everything be derived live.

For what each combinator costs, see [Mibo.Adaptive Performance](../mibo-adaptive/performance.html).
