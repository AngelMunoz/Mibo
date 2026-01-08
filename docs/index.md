---
title: Welcome to Mibo
category: Documentation
index: 0
---

# WIP THIS IS WORK IN PROGRESS DO NOT BELIVE ANYTHING YOU READ HERE UNLESS YOU DONT SEE THIS WARNING

# Mibo: A Functional Game Framework for F#

Mibo is a lightweight, Elmish-based game framework built on top of MonoGame. It brings the power of the **Model-View-Update (MVU)** architecture to game development, encouraging pure game logic and predictable state management.

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

Check out the [Getting Started](getting-started.html) guide to create your first Mibo game. [omit]
