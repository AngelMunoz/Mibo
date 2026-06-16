# Mibo.Raylib

> Install the templates:
>
> ```bash
> dotnet new install Mibo.Raylib.Templates
> dotnet new mibo-2d -o MyGame
> cd MyGame
> dotnet run
> ```

> **NOTE for ADVENTURERS:** raylib is a programming library to enjoy videogames programming; no fancy interface, no visual helpers, no debug button... just coding in the most pure spartan-programmers way.

Following that spirit, Mibo.Raylib keeps it lean, just F# and the Elmish loop with a handful of commodities to get out of your way and let you enjoy the craft.

Mibo.Raylib is a port of my first attempt at this [Mibo](https://github.com/AngelMunoz/Mibo) micro-framework from **MonoGame** to **raylib-cs**, designed to allow **F#** developers to write games using familiar Elmish patterns for all kinds of game genres and sizes.

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
- **Input Mapper** — Listen to raw input and map it to semantic actions

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

Samples live in a dedicated repository: **[Mibo.Samples](https://github.com/AngelMunoz/Mibo.Samples)**

That repo contains platformer, space battle, ping-pong, 3D, and other sample projects targeting both the raylib and MonoGame backends. Further sample development happens there.

## License

Mibo.Raylib is distributed under the [zlib/libpng License](LICENSE).

## Built on

Mibo.Raylib is built on top of:

- [raylib](https://github.com/raysan5/raylib) — the cross-platform graphics library that powers the rendering, input, and audio layers
- [raylib-cs](https://github.com/raylib-cs/raylib-cs) — the C# bindings that make raylib accessible from .NET

## Feedback

Issues and PRs are very welcome. If you're interested in using F# for game development beyond simple 2D games, Mibo.Raylib aims to be a practical, batteries-included framework that scales with your ambition.
