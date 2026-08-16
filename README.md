# Mibo

> Install the templates:
>
> ```bash
> dotnet new install Mibo.Templates
> dotnet new mibo-2d -o MyGame            # MVU runtime
> dotnet new mibo-2d-adaptive -o MyGame   # adaptive runtime
> cd MyGame
> dotnet run
> ```

> **NOTE for ADVENTURERS:** raylib is a programming library to enjoy videogames programming; no fancy interface, no visual helpers, no debug button... just coding in the most pure spartan-programmers way.

Following that spirit, Mibo keeps it lean, just F# and the Elmish loop with a handful of commodities to get out of your way and let you enjoy the craft.

Mibo is an Elmish-based **F#** game framework with two interchangeable backends — **raylib-cs** and **MonoGame** (DesktopGL/OpenGL and WindowsDX/DirectX) — designed to allow developers to write games using familiar MVU patterns for all kinds of game genres and sizes.

Mibo aims to solve 80/20 of use cases for enabling developers to focus on game logic rather than boilerplate code, providing guidelines and architecture for structuring game code, handling input, rendering, asset management, and time management among others.

## What's in the box?

- **Elmish runtime** (MVU loop) with `Cmd`, `Sub`, optional **fixed timestep**, and **frame-bounded dispatch**
- **Input** — raw input (`Keyboard`, `Mouse`) + semantic mapping via `InputMap` / `ActionState`
- **Assets** — texture, font, sound, and model loading caches
- **Rendering** — Command buffer based rendering:
  - 2D batch renderer with layers and multi-camera support
  - 3D batch renderer with opaque/transparent passes and custom shader switching
  - Escape hatches for custom GPU work
- **Camera** helpers with screen-to-world, orbit, and ray casting
- **Layout** — 2D procedural grid layout (`CellGrid2D`) with platformer, top-down, and geometric primitives
- **Layout3D** — 3D voxel-style grid layout (`CellGrid3D`) with terrain, interior rooms, corridors, stairs, and procedural generation
- **Animation** — sprite sheet slicing, `AnimatedSprite` state machines, and grid-based animation definitions
- **Mibo.Adaptive** — a pull-based incremental computation library for tight-loop workloads: `CVal`/`AVal` roots and projections plus adaptive sets, maps, and lists with element-level deltas, allocation-free in steady state. The Mibo integration (`AdaptiveProgram`/`AdaptiveHeadless` and the windowed hosts) is experimental.
- **Input Mapper** — Listen to raw input and map it to semantic actions
- **Performance** — zero-allocation hot paths: spatial grid queries return a single result array per call, and per-frame dictionary lookups, light merging, and render-pipeline bookkeeping allocate nothing

## Getting started

Prerequisites:

- **.NET SDK 8** or later
- A working OpenGL setup

```bash
dotnet --version
dotnet tool restore
dotnet restore
dotnet build
dotnet test
```

To build the docs site locally:

```bash
dotnet tool restore
dotnet fsdocs build
# or for live editing:
dotnet fsdocs watch
```

## Samples

The samples are stored in a separate repository: [Mibo.Samples](https://github.com/AngelMunoz/Mibo.Samples).

You'll find examples of:

**2D:**

- **PlatformerSample** - A 2D side-scrolling platformer with procedural world generation, sprite animation, lighting, particles, and sound. Uses Mibo's Elmish architecture with `InputMap`, `AnimatedSprite`, `CellGrid2D`, and `LightContext2D`.
  - Mibo.Raylib targeting Desktop OpenGL
  - Mibo.MonoGame targeting DesktopGL (cross-platform)
- **SpaceBattle** - A turn-based tactical strategy game on a hex grid with fog of war, laser combat, particle effects, faction-based turns (Human + AI), and animated unit movement. Demonstrates complex game state management, hex grid spatial queries, and multi-phase turn resolution.
  - Mibo.Raylib targeting Desktop OpenGL
- **PingPong** - A networked multiplayer Pong game with a client-server architecture over WebSockets. The server runs game logic and broadcasts state; the client renders locally and sends input.
  - Mibo.Raylib Client
  - Mibo.MonoGame Client
  - dotnet app acting as a server running Mibo.Core's headless support

**3D:**

- **ThreeDSample** - A 3D platformer with procedurally generated voxel terrain, PBR lighting, shadow atlas, 3D character animation, minimap overlay, and physics. Showcases Mibo's `Renderer3D`, `ForwardPbrPipeline`, and `Animation3DState`.
  - Mibo.Raylib targeting Desktop OpenGL
  - Mibo.MonoGame targeting DesktopGL (cross-platform)

- **FPSSample** - A first-person shooter featuring enemy AI, weapon systems, health management, and atmospheric lighting. Demonstrates Mibo's composable systems architecture with per-system sub-models, event-driven cross-system communication, and a `System` pipeline with snapshot barriers.
  - Mibo.Raylib targeting Desktop OpenGL
  - Mibo.MonoGame targeting DesktopGL (cross-platform)
  - Mibo.MonoGame targeting WindowsDX (Windows only, DirectX)

## License

Mibo is distributed under the [zlib/libpng License](LICENSE).

## Built on

Mibo is built on top of:

- [raylib](https://github.com/raysan5/raylib) — the cross-platform graphics library that powers the raylib backend's rendering, input, and audio layers
- [raylib-cs](https://github.com/raylib-cs/raylib-cs) — the C# bindings that make raylib accessible from .NET
- [MonoGame](https://github.com/MonoGame/MonoGame) — the cross-platform framework that powers the MonoGame backend (DesktopGL/OpenGL and WindowsDX/DirectX)
- [AdaptiveSlop](https://github.com/TheAngryByrd/AdaptiveSlop) — the pull-based incremental computation library by [TheAngryByrd](https://github.com/TheAngryByrd) that Mibo.Adaptive was adopted from

> **Mibo.Adaptive originated from [AdaptiveSlop](https://github.com/TheAngryByrd/AdaptiveSlop)** — adopted in its entirety, renamed, and maintained as part of Mibo. All design and implementation credit goes to [TheAngryByrd](https://github.com/TheAngryByrd).

## Feedback

Issues and PRs are very welcome. If you're interested in using F# for game development beyond simple 2D games, Mibo aims to be a practical, batteries-included framework that scales with your ambition.
