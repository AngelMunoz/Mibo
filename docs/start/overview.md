---
title: Where to Start
category: Start Here
categoryindex: 1
index: 1
---

# Where to start

Mibo ships two program runtimes (MVU and Adaptive) over one shared kernel,
with two interchangeable rendering backends (raylib and MonoGame). This page
routes you to the right guide. The linked pages teach; the API reference
holds the signatures.

## A new project

1. Install the templates and write the minimal program:
   [Welcome to Mibo](../index.html#Getting-Started).
2. Pick the runtime:
   [The Elmish Architecture](../mvu/elmish.html) for the classic MVU loop,
   or [Adaptive Overview](../adaptive/overview.html) for a derived-state graph.
3. Pick the backend and reference the matching host package; the
   [package table](../index.html#The-Mibo-packages) states the pairs.

## A game, in order

1. **Draw something.** [2D Rendering Overview](../graphics2d/overview.html) or
   [3D Rendering Overview](../graphics3d/overview.html).
2. **Design a level.** [Level Design Overview](../level-design/overview.html).
3. **Wire input, assets, and audio.** [Input](../input.html) covers input,
   then [Animation](../animation.html), [Assets](../assets.html),
   [Diagnostics](../diagnostics.html), and [Audio](../audio.html).
4. **Keep the frame budget.** [2D Performance](../graphics2d/performance.html),
   [F# for Perf](../performance.html), and [Scaling Mibo](../mvu/scaling.html).

## I want to…

| Goal | Start at |
|---|---|
| Author a level in F# | [Code-First Maps](../level-design/code-first.html) |
| Author a level in KDL or XML | [Authored Maps](../level-design/authored.html) |
| Build a 2D map | [Code-First Maps](../level-design/code-first.html) |
| Build a 3D map (height and spans) | [3D from 2D](../level-design/three-d.html) |
| Draw a grid with instancing | [GPU Instancing](../graphics3d/instancing.html) |
| Use hex grids | [Hex Grids](../level-design/hex.html) |
| Move off the retired grids | [Migrating to Mibo v6](../migration-to-v6.html) |

## Older docs

The [V5-and-below archive](../v5/index.html) and the
[V1 archive](../v1/index.html) hold frozen snapshots. They are not linked in
the sidebar.
