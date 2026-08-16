---
title: Adaptive Systems
category: Adaptive
categoryindex: 3
index: 3
---

# Organizing a Growing Game

A one-record world is fine until it isn't. When `update` starts touching bees, flowers, weather and score in the same function, split the game into features. A feature owns its data and its logic; features don't reach into each other.

## One feature, one owner

Give each feature its own model, its own tick, and its own events. The bees module owns the bees and nothing else:

```fsharp
module Bees =
    type Model = { Bees: cmap<int, Bee>; NextId: int }

    type Event =
        | Left of id: int        // a bee flew off — someone may care

    let tick (dt: float32) (model: Model) : Event list =
        // move bees, despawn the ones that left, report the leavers
        ...

    let spawn (bee: Bee) (model: Model) = ...
```

Your update function becomes a schedule plus a translator: it calls each feature's tick in order, then reacts to what the features reported.

```fsharp
let update world ctx gameTime =
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    let beeEvents = Bees.tick dt world.Bees
    let flowerEvents = Flowers.tick dt world.Flowers

    for event in beeEvents do
        match event with
        | Bees.Left id ->
            // a bee that leaves might have pollinated something
            Flowers.onVisitorLeft id world.Flowers
            world.Honey.UpdateTo(world.Honey.GetValue() + 1) |> ignore
```

The rule that keeps this sane: **events are data, and one place handles them.** A feature returns plain values describing what happened; it never calls another feature directly. When you add a third feature that cares about bees leaving, you add one line to the translator — the bees module doesn't change.

## Reading another feature's data

Features still need to read each other — flowers need to know where the bees are. Do it through the update function: it reads one feature's containers and passes plain values to another feature's tick.

```fsharp
let update world ctx gameTime =
    // Bees move first...
    let beeEvents = Bees.tick dt world.Bees

    // ...so flowers see this frame's bee positions
    let flowerEvents =
        Flowers.tick dt (world.Bees.Bees |> AMap.getValue) world.Flowers
```

Two habits worth keeping:

* Pass plain values (`IReadOnlyDictionary`, vectors, ints) instead of the containers themselves — the receiving feature then can't write to them, and the compiler keeps you honest.
* Avoid creating closures per frame. A `fun x -> ...` allocated inside `update` runs fine but collects garbage at 60 fps; plain parameters and loops don't.

## Derived values that span features

Anything you want derived from two features — "flowers with a bee nearby", a scoreboard — build it from both containers at startup, not inside update:

```fsharp
let pollinated =
    bees.Bees
    |> AMap.filter(fun _ bee -> nearAnyFlower bee)
```

Build these once in your setup code and read them in update or in the frame. They recompute when their inputs change — you never refresh them.

Keep values derived from a single feature next to that feature's code; values that mix features belong at the top level, next to your `update`. That's the whole placement rule — it keeps each feature understandable alone.

## When a decision spans features

"Can the player afford this?", "is this spot free?" — questions like these read several features at once. Write them as small functions that take the relevant values and return an answer, and call them from your update. Keep the yes/no logic out of the update function itself; update stays a schedule and a translator.
