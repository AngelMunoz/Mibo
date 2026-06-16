---
title: Welcome to Mibo.Raylib
category: Documentation
index: 0
---

# Mibo.Raylib: A Functional Game Framework for F#

> **NOTE for ADVENTURERS:** raylib is a programming library to enjoy videogames programming; no fancy interface, no visual helpers, no debug button... just coding in the most pure spartan-programmers way.

Following that spirit, Mibo.Raylib keeps it lean — no editors, no pipelines, no wizards. Just F# and the Elmish loop, with a handful of commodities to get out of your way and let you enjoy the craft.

Mibo.Raylib is a lightweight, Elmish-based game framework built on top of raylib. It brings the power of the **Model-View-Update (MVU)** architecture to game development, encouraging pure game logic and predictable state management.

## Getting Started

To get started with Mibo.Raylib, you need the [dotnet SDK](https://get.dot.net) installed.

```bash
dotnet new install Mibo.Raylib.Templates
dotnet new mibo-2d -o MyGame
cd MyGame
dotnet run
```

Complete working samples are available in the [Mibo.Samples](https://github.com/AngelMunoz/Mibo.Samples) repository.

You can then start building your game using any of the following:

- [VsCode](https://code.visualstudio.com/) with the
  - [Ionide extension](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp) (MS Registry)
  - [Ionide extension](https://open-vsx.org/extension/Ionide/Ionide-fsharp) (Open VSX Registry)
- [JetBrains Rider](https://www.jetbrains.com/rider/)
- [Visual Studio](https://visualstudio.microsoft.com/)

## Why Mibo.Raylib?

Traditional game engines often rely heavily on complex object hierarchies, vendor specific tooling and no specific architecture guidance. Mibo.Raylib offers an alternative:

- **Functional First**
  - Write your game logic as pure functions that transform state.
  - When you grow enough you adopt mutable state in a predictable way to squeeze out more performance, but you can start simple and keep it pure as long as you want.
  - F# inline, compiler optimizations around functions, byrefs, structs and value types allow you to write high-level code without sacrificing performance.
- **Predictable State**
  - The MVU architecture enforces a clear separation of concerns with a single source of truth for your game state, making it easier to reason about and debug.
  - The unidirectional data flow ensures that state changes are predictable and traceable, which is especially beneficial in complex game logic.
- **Elmish Architecture**
  - A well-known architecture in the F# community with a twist for games.
- **raylib Power**
  - Built on top of raylib, a simple and easy-to-use library to enjoy videogames programming.
  - Cross-platform support and a rich set of features for graphics, input, audio, and more.
- **Deferred Rendering**
  - Be ready for efficient lighting and post-processing effects without coupling your render logic to the update loop.
  - Be ready for networked games with client-side prediction and server reconciliation without coupling your game logic to the rendering.

## Built on

Mibo.Raylib is built on top of:

- [raylib](https://github.com/raysan5/raylib) — the cross-platform graphics library that powers the rendering, input, and audio layers
- [raylib-cs](https://github.com/raylib-cs/raylib-cs) — the C# bindings that make raylib accessible from .NET
