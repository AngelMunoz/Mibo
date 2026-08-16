---
title: Adaptive Overview
category: Adaptive
categoryindex: 3
index: 1
---

# The Adaptive Architecture

In the adaptive architecture your game state lives in containers that know when they changed. You write facts into them, you describe the values you want derived from those facts, and the framework takes care of the rest: what to recompute, when, and how the renderer gets it.

```fsharp
open Mibo.Adaptive

// Facts you write
let honey = CVal.create 0
let bees = CMap.empty<int, Bee>

// Values derived from the facts — you never update these by hand
let beeCount = bees |> AMap.count
let status = honey |> AVal.map(fun h -> $"Honey: {h}")
```

Write to `bees` ten times between two reads and the derived values recompute once, not ten times. Don't write at all and reads are free. That is the whole trick: you stop keeping derived state up to date, because the graph does it.

## What a frame looks like

Once per frame the runner packs everything the renderer needs into one value — your "frame" — and hands it to your drawing code. The renderer never looks at the game state directly; it reads the frame. This keeps the two halves independent: you can reorganize your state without touching a single draw call, and vice versa.

The frame is packed after the update has run, so it always reflects this frame's world. Anything the renderer should see — positions, health bars, score, UI state — goes in there.

## Is this for my game?

Reach for the adaptive architecture when your game is mostly a simulation: things move, counts change, the HUD follows the world. The bigger that gets, the more this pays off.

If your game is turn-based, menu-driven, or small enough that recomputing everything each frame doesn't show up in the profiler, the simpler [Elmish architecture](../mvu/elmish.html) is a better fit. Both run on the same backends and share the same rendering — pick one per project and go.

## Where to go next

* [Adaptive Programs](program.html) — writing your first adaptive game and running it.
* [Adaptive Systems](systems.html) — keeping a growing game organized.
* [Scaling Adaptive](scaling.html) — how the architecture grows with your game.
* [Adaptive Input](input.html) — keyboard and mouse.
* [Adaptive Services](services.html) — audio, networking, save data.
* [Mibo.Adaptive](../mibo-adaptive/overview.html) — the library behind all of this, if you want the details.
