---
title: Patterns Overview
category: Patterns
categoryindex: 6
index: 61
---

# Patterns Overview

General game development patterns for Mibo. Each page presents a reusable technique — not an API reference, but a working recipe for problems every game developer faces.

These patterns are independent. Apply one, two, or all of them depending on what your game needs.

## Available Patterns

| Pattern | What it solves |
|---------|---------------|
| [Composable Systems](composable-systems.html) | Splitting a game into independent sub-systems (owned state, declarative events) coordinated by a router |
| [Background Work](background-work.html) | Running heavy computation off the main thread without blocking the game loop |
| [Pooled Particles](pooled-particles.html) | Zero-GC particle effects with pre-allocated arrays and fade-and-compact |
| [Layered Rendering](layered-rendering.html) | Compositing multiple render passes — HUDs, minimaps, debug overlays |
| [Pre-computed State](precomputed-state.html) | Computing derived values once per frame, reading them cheaply everywhere |

## How to read these pages

Each pattern follows the same structure:

1. **What and Why** — What the pattern does and when you need it.
2. **Use Cases** — Multiple scenarios where this pattern applies.
3. **The Technique** — The core idea, with generic code.
4. **When to use** — Concrete signals that this pattern applies.

## Samples

The `PlatformerSample` and `ThreeDSample` projects demonstrate these patterns in complete games. Each pattern page links to the relevant sample code.
