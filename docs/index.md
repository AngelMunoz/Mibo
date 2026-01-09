---
title: Welcome to Mibo
category: Documentation
index: 0
---

# Mibo: A Functional Game Framework for F#

Mibo is a lightweight, Elmish-based game framework built on top of MonoGame. It brings the power of the **Model-View-Update (MVU)** architecture to game development, encouraging pure game logic and predictable state management.

If you prefer learning by example, the projects in `src/Sample` and `src/3DSample` show complete, working setups.

## Why Mibo?

Traditional game engines often rely heavily on mutable state and complex object hierarchies. Mibo offers an alternative:

- **Functional First**: Write your game logic as pure functions that transform state.
- **Predictable State**: The entire game state (the Model) is centralized and immutable.
- **Elmish Architecture**: Leverage the robust MVU pattern for clear separation of concerns.
- **MonoGame Power**: Benefit from the performance and cross-platform capabilities of MonoGame.
- **Deferred Rendering**: Built-in 2D and 3D batchers that handle sorting and culling for you.

## Core Patterns

### The Elmish Loop

Every Mibo game follows a simple loop:

1. **Init**: Define your initial state.
2. **Update**: Purely calculate the next state based on messages (input, timers, etc.).
3. **View**: Describe what should be rendered based on the current state.
4. **Subscribe**: Listen to external events like keyboard or touch input.

### Semantic Input Mapping

Instead of checking for specific keys in your player logic, Mibo encourages mapping keys to **Actions**. This allows for easy input rebinding and multi-device support.

## Getting Started

Run one of the samples, then copy its program setup (composition root) into your own project.

## Documentation

- Architecture

  - [Elmish (MVU) runtime](elmish.html)
  - [Programs & composition](program.html)
  - [System pipeline (phases + snapshot)](system.html)
  - [Scaling Mibo (Simple → Complex)](scaling.html)

- Core services

  - [Input (raw + mapped)](input.html)
  - [Assets (loading + caching)](assets.html)

- Rendering
  - [Rendering overview + custom renderers](rendering.html)
  - [Rendering 2D](rendering2d.html)
  - [Rendering 3D](rendering3d.html)
  - [Camera](camera.html)
  - [Culling](culling.html)
