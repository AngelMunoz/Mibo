---
title: Welcome to Mibo
category: Documentation
index: 0
---

# Mibo: A Functional Game Framework for F#

> **NOTE for ADVENTURERS:** raylib is a programming library to enjoy videogames programming; no fancy interface, no visual helpers, no debug button... just coding in the most pure spartan-programmers way.

Following that spirit, Mibo keeps it lean — no editors, no pipelines, no wizards. Just F# and the Elmish loop, with a handful of commodities to get out of your way and let you enjoy the craft.

Mibo is a lightweight, **Elmish-based game framework** built on a **backend-agnostic core** with pluggable rendering backends. It brings the power of the **Model-View-Update (MVU)** architecture to game development, encouraging pure game logic and predictable state management — and lets you choose the graphics backend that fits your target platform.

## The Mibo packages

Mibo is split into three packages so your game logic stays portable while the rendering backend stays swappable:

```text
Mibo.Core          ← the shared core (Cmd, Sub, System, GameTime, Program, IRenderer,
                     GameContext, IInput/IInputMapper contracts, IAssetCache,
                     HeadlessProgram, Layout/Layout3D)
Mibo.Raylib        ← the raylib backend (host: RaylibGame, GLSL shaders)
Mibo.MonoGame      ← the MonoGame backend (host: MiboGame, HLSL .fx shaders)
```

**The guiding rule:** if it is a contract that the Program builder, a runtime host, the headless runner, or portable user code needs, it lives in Mibo.Core. Backend-specific implementations and any type that leaks a backend handle stay in the backend.

| Backend | Host | Shaders | Best for |
|---------|------|---------|----------|
| Mibo.Raylib | RaylibGame<'Model,'Msg> | GLSL | Cross-platform Desktop OpenGL; lean, no content pipeline |
| Mibo.MonoGame | MiboGame<'Model,'Msg> | HLSL (.fx → .mgfx) | Windows Desktop DirectX 11, plus OpenGL cross-platform via MonoGame |

Both backends ship the same rendering surface: a 2D batch renderer and a 3D **Forward PBR pipeline** with a shadow atlas, post-processing, and built-in shaders — so your Draw/Draw3D view code is portable between them.

## Getting Started

To get started, you need the [dotnet SDK](https://get.dot.net) installed. The templates currently target the raylib backend:

```bash
dotnet new install Mibo.Raylib.Templates
dotnet new mibo-2d -o MyGame
cd MyGame
dotnet run
```

A minimal program looks the same regardless of backend — only the host type and the package reference change:

```fsharp
open Mibo.Elmish

let program =
  Program.mkProgram init update
  |> Program.withConfig (fun cfg ->
      { cfg with Width = 1280; Height = 720; Title = "My Game"; TargetFPS = 60 })
  |> Program.withRenderer (fun () -> Renderer2D.create view)

// raylib:
let game = new RaylibGame<Model, Msg>(program)
game.Run()

// MonoGame:
// let game = new MiboGame<Model, Msg>(program)
// game.Run()
```

You can then start building your game using any of the following:

- [VsCode](https://code.visualstudio.com/) with the
  - [Ionide extension](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp) (MS Registry)
  - [Ionide extension](https://open-vsx.org/extension/Ionide/Ionide-fsharp) (Open VSX Registry)
- [JetBrains Rider](https://www.jetbrains.com/rider/)
- [Visual Studio](https://visualstudio.microsoft.com/)

## Samples

The samples developed for the initial Raylib version and the new MonoGame Samples are stored in their own repository.
[Mibo.Samples](https://github.com/AngelMunoz/Mibo.Samples) is the place to visit.

You'll find examples of

**2D:**:

- Platformer - A simple platformer featuring lights, normal maps, occluders and particles
  - Sample Mibo.Raylib targeting Desktop OpenGL
  - Sample Mibo.MonoGame targeting Windows Desktop DirectX11
- Space Battle - A minimalistic hex grid strategy game a'la Wargroove or Advanced Wars
  - Sample Mibo.Raylib targeting Desktop OpenGL
- Ping Pong - A Small client-server example
  - Mibo.Raylib Client
  - Mibo.MonoGame Client
  - dotnet app acting as a server running Mibo.Core's headless support

**3D:**

- Platformer - A simple platformer with 3D models, lights, shadows, particles, and skeletal animation
  - Mibo.Raylib targeting Desktop OpenGL

## Why Mibo?

Traditional game engines often rely heavily on complex object hierarchies, vendor specific tooling and no specific architecture guidance. Mibo offers an alternative:

- **Functional First**
  - Write your game logic as pure functions that transform state.
  - When you grow enough you adopt mutable state in a predictable way to squeeze out more performance, but you can start simple and keep it pure as long as you want.
  - F# inline, compiler optimizations around functions, byrefs, structs and value types allow you to write high-level code without sacrificing performance.
- **Predictable State**
  - The MVU architecture enforces a clear separation of concerns with a single source of truth for your game state, making it easier to reason about and debug.
  - The unidirectional data flow ensures that state changes are predictable and traceable, which is especially beneficial in complex game logic.
- **Elmish Architecture**
  - A well-known architecture in the F# community with a twist for games.
- **Backend-agnostic core**
  - Game logic, the MVU loop, input contracts, and layout engines live in Mibo.Core and work on any backend.
  - Swap between raylib and MonoGame (or run headless) without rewriting your model or update logic.
- **Deferred Rendering**
  - Be ready for efficient lighting and post-processing effects without coupling your render logic to the update loop.
  - Both backends ship a Forward PBR pipeline with a shadow atlas out of the box.
  - Be ready for networked games with client-side prediction and server reconciliation without coupling your game logic to the rendering.

## Built on

Mibo is built on top of:

- [raylib](https://github.com/raysan5/raylib) — the cross-platform graphics library that powers the raylib backend's rendering, input, and audio layers
- [raylib-cs](https://github.com/raylib-cs/raylib-cs) — the C# bindings that make raylib accessible from .NET
- [MonoGame](https://www.monogame.net/) — the framework that powers the MonoGame backend's rendering, input, and audio layers