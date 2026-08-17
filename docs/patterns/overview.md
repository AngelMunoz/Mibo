---
title: Patterns Overview
category: Patterns
categoryindex: 13
index: 1
---

# Patterns Overview

Reusable game development patterns that work the same on both runtimes ([MVU](../mvu/elmish.html) and [Adaptive](../adaptive/overview.html)): techniques about memory, rendering structure, and game feel rather than program shape.

Each page presents a working recipe for a problem every game developer faces, not an API reference.

## Available Patterns

| Pattern | What it solves |
|---------|---------------|
| [Pooled Particles](pooled-particles.html) | Zero-GC particle effects with pre-allocated arrays and fade-and-compact |
| [Layered Rendering](layered-rendering.html) | Compositing multiple render passes: HUDs, minimaps, debug overlays |

## Where the other patterns went

The patterns that are about *program shape* live with their runtime, because that is where they differ; each MVU pattern has its adaptive counterpart:

* Structuring a growing game, sub-systems that own their state and report events as data: [Composable Systems](../mvu/composable-systems.html) (MVU) / [Systems](../adaptive/systems.html) (adaptive).
* Running heavy work without blocking the loop: [Background Work](../mvu/background-work.html) (`Cmd.ofAsync`) / [Background Work](../adaptive/background-work.html) (`postTask`, slicing).
* Computing derived values instead of refreshing them by hand: [Pre-computed State](../mvu/precomputed-state.html) (MVU, by hand) / [Derived State](../adaptive/derived-state.html) (adaptive, declared in the graph).

## How to read these pages

Each pattern follows the same structure:

1. **What and Why**: What the pattern does and when you need it.
2. **Use Cases**: Multiple scenarios where this pattern applies.
3. **The Technique**: The core idea, with generic code.
4. **When to use**: Concrete signals that this pattern applies.

## Samples

The samples in the Mibo.Samples repository demonstrate these patterns in complete games (the Platformer3D sample shows particles and layered rendering). Each pattern page links to the relevant sample code.
