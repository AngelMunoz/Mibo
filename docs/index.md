---
title: Welcome to Mibo
category: Documentation
index: 0
---

# Mibo: A Functional Game Framework for F#

> **NOTE for ADVENTURERS:** raylib is a programming library to enjoy videogames programming; no fancy interface, no visual helpers, no debug button... just coding in the most pure spartan-programmers way.

Following that spirit, Mibo keeps it lean: no editors, no pipelines, no wizards. Just F# and the Elmish loop, with a handful of conveniences to get out of your way and let you enjoy the craft.

Mibo is a lightweight, **functional game framework** built on a **backend-agnostic core** with pluggable rendering backends. It ships **two supported program runtimes**; pick per project, share everything below them:

- The classic **Model-View-Update (MVU)** loop: pure update functions, `Cmd`/`Sub` (commands and subscriptions: side effects and external events), deterministic message replay. See [The Elmish Architecture](mvu/elmish.html).
- The **Adaptive** architecture: a derived state graph forced once per step, for simulation-shaped games. See [The Adaptive Architecture](adaptive/overview.html).

Both encourage pure game logic and predictable state management, and both let you choose the graphics backend that fits your target platform.

## The Mibo packages

Mibo is split into four packages so your game logic stays portable while the rendering backend stays swappable:

```text
Mibo.Core          ← the shared core (Program builders for both runtimes, Cmd/Sub,
                     System pipeline, GameTime, IRenderer, GameContext,
                     IInput/IInputMapper contracts, IAssetCache, HeadlessProgram,
                     AdaptiveProgram/AdaptiveHeadless, Layout/Layout3D)
Mibo.Adaptive      ← the incremental-computation library powering the adaptive
                     runtime (cval/aval, cset/cmap/clist + views), usable standalone
Mibo.Raylib        ← the raylib backend (hosts: RaylibGame, AdaptiveRaylibGame; GLSL shaders)
Mibo.MonoGame      ← the MonoGame backend (hosts: MiboGame, AdaptiveMonoGameGame; HLSL .fx shaders)
```

`cval`/`aval` and friends are the adaptive containers; the [Adaptive](adaptive/overview.html) docs introduce them.

**The guiding rule:** if it is a contract that the Program builder, a runtime host, the headless runner, or portable user code needs, it lives in Mibo.Core. Backend-specific implementations and any type that leaks a backend handle stay in the backend.

| Backend | MVU host | Adaptive host | Shaders | Best for |
|---------|----------|---------------|---------|----------|
| Mibo.Raylib | RaylibGame<'Model,'Msg> | AdaptiveRaylibGame<'Frame> | GLSL | Cross-platform Desktop OpenGL; lean, no content pipeline |
| Mibo.MonoGame | MiboGame<'Model,'Msg> | AdaptiveMonoGameGame<'Frame> | HLSL (.fx → .mgfx) | Windows Desktop DirectX 11, plus OpenGL cross-platform via MonoGame |

Both backends ship the same rendering surface: a 2D batch renderer and a 3D **Forward PBR pipeline** with a shadow atlas, post-processing, and built-in shaders. (PBR is <abbr title="physically based rendering">physically based rendering</abbr>; the shadow atlas is a single texture holding all shadow maps.) So the same fluent view code drives both backends.

## Getting Started

To get started, you need the [dotnet SDK](https://get.dot.net) installed. The `Mibo.Templates` package includes MVU starters (`mibo-2d`/`mibo-3d` for raylib, `mibo-mg-2d`/`mibo-mg-3d` for MonoGame) and adaptive starters with an `-adaptive` suffix (`mibo-2d-adaptive`, `mibo-mg-3d-adaptive`, …). The MonoGame templates each ship a shared library plus thin clients for DesktopGL/OpenGL, DesktopVK/Vulkan, and WindowsDX12/DirectX 12:

```bash
dotnet new install Mibo.Templates
dotnet new mibo-2d -o MyGame            # MVU runtime
dotnet new mibo-2d-adaptive -o MyGame   # adaptive runtime
cd MyGame
dotnet run
```

A minimal program looks the same regardless of backend; only the host type and the package reference change:

```fsharp
open Mibo.Elmish

let configureWindow (cfg: GameConfig) =
    { cfg with Width = 1280; Height = 720; Title = "My Game" }

let createRenderer () = Renderer2D.create view

let program =
  Program.mkProgram init update
  |> Program.withConfig configureWindow
  |> Program.withRenderer createRenderer

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

> **NOTE:** the [v1 (raylib-only) docs](v1/index.html) are archived; reachable from this link, not from the sidebar.

You'll find examples of

**2D:**

- Platformer - A simple platformer featuring lights, normal maps, occluders and particles
  - Sample Mibo.Raylib targeting Desktop OpenGL
  - Sample Mibo.MonoGame targeting Windows Desktop DirectX11
- Space Battle - A minimalistic hex grid strategy game à la Wargroove or Advanced Wars
  - Sample Mibo.Raylib targeting Desktop OpenGL
- Ping Pong - A small client-server example
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
- **Elmish and Adaptive**
  - Start with the well-known Elmish architecture; graduate to the adaptive derived-state graph when your simulation outgrows per-frame recomputation.
- **Backend-agnostic core**
  - Game logic, both program runtimes, input contracts, and layout engines live in Mibo.Core and work on any backend.
  - Swap between raylib and MonoGame (or run headless) without rewriting your model or update logic.
- **Layered rendering**
  - Draw in layers: commands are sorted and rendered in order, with optional post-processing passes on top. (This is command layering, not to be confused with deferred shading, a lighting technique.)
  - Both backends ship a Forward PBR pipeline with a shadow atlas out of the box.
  - Be ready for networked games with client-side prediction and server reconciliation without coupling your game logic to the rendering.

## Built on

Mibo is built on top of:

- [raylib](https://github.com/raysan5/raylib): the cross-platform graphics library that powers the raylib backend's rendering, input, and audio layers
- [raylib-cs](https://github.com/raylib-cs/raylib-cs): the C# bindings that make raylib accessible from .NET
- [MonoGame](https://www.monogame.net/): the framework that powers the MonoGame backend's rendering, input, and audio layers