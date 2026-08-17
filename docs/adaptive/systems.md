---
title: Systems
category: Adaptive
categoryindex: 3
index: 5
---

# Organizing a Growing Game

A one-record world is fine until it isn't. When `update` starts touching bees, flowers, weather and score in the same function, split the game into features. A feature owns its data and its logic; features don't reach into each other. (The Elmish runtime's version of this pattern is [Composable Systems](../mvu/composable-systems.html): same rules, different plumbing.)

## One feature, one owner

Give each feature its own model, its own tick, and its own events. The bees module owns the bees and nothing else:

```fsharp
module Bees =
    type Model = { Bees: cmap<int, Bee>; NextId: int }

    type Event =
        | Left of id: int        // a bee flew off: someone may care

    let tick (dt: float32) (model: Model) : Event list =
        // move bees, despawn the ones that left, report the leavers
        ...

    let spawn (bee: Bee) (model: Model) = ...
```

`Event` is a discriminated union: one type with a fixed set of cases, each carrying its data (here, the id of the bee that left). Your update function becomes a schedule plus a translator: it calls each feature's tick in order, then reacts to what the features reported.

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
            world.Honey.UpdateTo((world.Honey |> AVal.getValue) + 1) |> ignore
```

The rule that keeps this sane: **events are data, and one place handles them.** A feature returns plain values describing what happened; it never calls another feature directly. When you add a third feature that cares about bees leaving, you add one line to the translator; the bees module doesn't change.

## Reading another feature's data

Features still need to read each other: flowers need to know where the bees are. Do it through the update function: it reads one feature's containers and passes plain values to another feature's tick.

```fsharp
let update world ctx gameTime =
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

    // Bees move first...
    let beeEvents = Bees.tick dt world.Bees

    // ...so flowers get this step's bee positions as a plain argument
    let beePositions = world.Bees.Bees |> AMap.getValue
    let flowerEvents = Flowers.tick dt beePositions world.Flowers
```

Two habits worth keeping:

* Pass plain values (`IReadOnlyDictionary`, vectors, ints) instead of the containers themselves; the receiving feature then can't write to them, and the compiler keeps you honest.
* Avoid allocating function values inside `update`: a `fun x -> ...` created there runs fine but collects garbage at 60 fps; plain parameters and loops don't.

## Derived values that span features

Some values read more than one feature: "flowers with a bee nearby", the scoreboard. Build those from both containers once, at startup, and read them wherever you need them; they recompute when their inputs change.

```fsharp
// plain function: is any bee close to this flower?
let isPollinated (beePositions: IReadOnlyDictionary<int, Bee>) _key (flower: Flower) =
    ... // your distance check over beePositions.Values

// derived once, at startup, from both containers
let pollinated =
    flowers.Flowers
    |> AMap.filter (isPollinated (bees.Bees |> AMap.getValue))
```

Where such a value lives (next to its feature, or at the top level when it mixes features) is the one placement rule, and [Derived State](derived-state.html) covers it with the full example.

## When a decision spans features

Questions like "can the player afford this?" or "is this spot free?" read several features at once. Write them as small functions that take the relevant values and return an answer, and call them from your update. Keep the yes/no logic out of the update function itself; update stays a schedule and a translator.

## See also

- [Intents](intents.html): the queue that carries one feature's events to the translator.
- [Derived State](derived-state.html): where derived values live, and the two-home placement rule.
- [Composable Systems](../mvu/composable-systems.html): the same architecture on the Elmish runtime.
