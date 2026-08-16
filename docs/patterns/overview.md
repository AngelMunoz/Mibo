---
title: Patterns Overview
category: Patterns
categoryindex: 11
index: 1
---

# Patterns Overview

Reusable game development patterns that work the same on both runtimes — techniques about memory, rendering structure, and game feel rather than program shape.

Each page presents a working recipe for a problem every game developer faces, not an API reference.

## Available Patterns

| Pattern | What it solves |
|---------|---------------|
| [Pooled Particles](pooled-particles.html) | Zero-GC particle effects with pre-allocated arrays and fade-and-compact |
| [Layered Rendering](layered-rendering.html) | Compositing multiple render passes — HUDs, minimaps, debug overlays |

## Where the other patterns went

The patterns that are about *program shape* live with their runtime, because that is where they differ:

* Structuring a growing game — sub-systems that own their state and report events as data: [Composable Systems](../mvu/composable-systems.html) for the Elmish runtime, [Systems](../adaptive/systems.html) for the adaptive runtime.
* Background work — [Background Work](../mvu/background-work.html) with `Cmd.ofAsync`; [Intents](../adaptive/intents.html) (`postTask`/`postAsync`) on the adaptive side.
* Derived state computed once instead of every frame — [Pre-computed State](../mvu/precomputed-state.html) does it by hand; the adaptive runtime's derived values do it for you (see [Mibo.Adaptive](../mibo-adaptive/overview.html)).

## How to read these pages

Each pattern follows the same structure:

1. **What and Why** — What the pattern does and when you need it.
2. **Use Cases** — Multiple scenarios where this pattern applies.
3. **The Technique** — The core idea, with generic code.
4. **When to use** — Concrete signals that this pattern applies.

## Samples

The `PlatformerSample` and `ThreeDSample` projects demonstrate these patterns in complete games. Each pattern page links to the relevant sample code.
