---
title: Overview
category: Adaptive
categoryindex: 3
index: 1
---

# The Adaptive Architecture

In the adaptive architecture your game is **State, Projection, Update** (SPU). The state lives in containers that know when they changed; the projections describe the values you want derived from those facts; the update advances the game by writing facts. The framework takes care of the rest: what to recompute, when, and how the renderer gets it.

Two bits of F# you will see in every example: `|>` feeds the value on its left into the function on its right, and `$"Honey: {h}"` plugs a value into a string.

```fsharp
open Mibo.Adaptive

// State: the facts you write
let honey = CVal.create 0
let bees = CMap.empty<int, Bee>

// Projections: values derived from the facts, never updated by hand
let beeCount = bees |> AMap.count        // read with AVal.getValue when packing
let honeyStatus h = $"Honey: {h}"
let status = honey |> AVal.map honeyStatus
```

Write to `bees` ten times between two reads and the projections recompute once, not ten times. Don't write at all and reads are free. That is the whole trick: you stop keeping derived values up to date, because the graph does it.

## The frame projection

Once per step the runner forces the projection you registered, which packs everything the renderer needs into one value (your "frame") and hands it to your drawing code. The renderer never looks at the game state directly; it reads the frame. This keeps the two halves independent: you can reorganize your state without touching a single draw call, and vice versa.

The frame is forced after the update has run, so it always reflects this step's world. Anything the renderer should see (positions, health bars, score, UI state) goes in there. [Adaptive Programs](program.html) shows the full shape.

## Is this for my game?

Reach for the adaptive architecture when your game is mostly a simulation: things move, counts change, the HUD follows the world. The bigger that gets, the more this pays off.

If your game is turn-based, menu-driven, or small enough that recomputing everything each frame doesn't show up in the profiler, the simpler [Elmish architecture](../mvu/elmish.html) is a better fit. Both run on the same backends and share the same rendering; pick one per project and go.

## Where to go next

* [Adaptive Programs](program.html): writing your first adaptive game (SPU, end to end) and running it.
* [Intents](intents.html): deferring work to the right moment.
* [Subscriptions](subscriptions.html): input, timers, network, events that arrive on their own.
* [Systems](systems.html): keeping a growing game organized.
* [Scaling Adaptive](scaling.html): how the architecture grows with your game.
* [Services](services.html): audio, networking, save data.
* [Headless Mode](headless.html): testing and servers, no window.
* [Background Work](background-work.html): heavy computation off the game thread.
* [Derived State](derived-state.html): where projections live, and staying cheap.
* [Mibo.Adaptive](../mibo-adaptive/overview.html): the library behind all of this, if you want the details.
