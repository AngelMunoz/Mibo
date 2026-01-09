# Mibo

Mibo is a lightweight, Elmish-inspired **micro-framework** for **F#** that helps you build games on top of **MonoGame** (the underlying game framework).
It’s designed to stay fun for small games while still providing an upgrade path for bigger projects (ARPG/RTS-style complexity) without forcing you to rewrite your engine.

Key ideas:

- Keep `update` **pure** (MVU)
- Submit render commands to a `RenderBuffer` in `view`
- Use explicit boundaries (tick ownership, phases, snapshot barriers) when scaling up

If you prefer learning by example, check out the working sample projects:

- `samples/2DSample` (2D sample)
- `samples/3DSample` (3D sample)

## What’s in the box?

- **Elmish runtime** for MonoGame games (MVU loop)
  - optional **fixed timestep** support
  - optional **frame-bounded dispatch** for stricter frame boundaries
- **Input** (raw input + semantic mapping / rebinding-friendly)
- **Assets** (loading + caching helpers)
- **Rendering**
  - 2D SpriteBatch renderer (layers + multi-camera amenities)
  - 3D renderer with opaque/transparent passes + multi-camera amenities
  - Sprite3D “90% path” for unlit textured **quads and billboards**
  - escape hatches (`DrawCustom`) when you need custom GPU work
- **Camera** helpers + culling utilities

## Documentation

The docs live in `docs/` and are intended to be the authoritative reference.

Start here:

- Architecture
  - Elmish runtime: `docs/elmish.md`
  - System pipeline (phases + snapshot): `docs/system.md`
  - Scaling ladder (Simple → Complex): `docs/scaling.md`
- Core services
  - Input: `docs/input.md`
  - Assets: `docs/assets.md`
- Rendering
  - Overview / composition: `docs/rendering.md`
  - 2D: `docs/rendering2d.md`
  - 3D: `docs/rendering3d.md`
  - Camera: `docs/camera.md`

## Getting started

Prerequisites:

- **.NET SDK 10**
- A working OpenGL setup (MonoGame DesktopGL)

From the repo root:

```bash
dotnet --version
dotnet tool restore
dotnet restore
dotnet build
dotnet test
```

### Templates

Mibo provides project templates to get you started quickly.

Install the templates:

```bash
dotnet new install Mibo.Templates
```

Create a new project:

```bash
# 2D Game
dotnet new mibo2d -n My2DGame

# 3D Game
dotnet new mibo3d -n My3DGame

# Multi-Platform (Desktop, Android, iOS)
dotnet new mibo-multi -n MyMultiPlatformGame
```

### Run the samples

2D sample:

```bash
dotnet run --project samples/2DSample/MiboSample.fsproj
```

3D sample:

```bash
dotnet run --project samples/3DSample/3DSample.fsproj
```

### Content pipeline tooling

This repo uses local dotnet tools (see `.config/dotnet-tools.json`) for MonoGame content:

- `mgcb` / `mgcb-editor-*`

If you need to edit a content project:

```bash
dotnet tool restore
# Run the editor appropriate for your OS
dotnet mgcb-editor-linux 
# or dotnet mgcb-editor-windows / dotnet mgcb-editor-mac
```

### Build the documentation site (optional)

The repo uses `fsdocs-tool`.

```bash
dotnet tool restore
dotnet fsdocs build
```

To watch locally while editing:

```bash
dotnet tool restore
dotnet fsdocs watch
```

## Repo structure

- `src/Mibo` — the core framework library
- `src/Mibo.Tests` — unit tests
- `src/Templates` — project templates source
- `samples/2DSample` — 2D sample game
- `samples/3DSample` — 3D sample game
- `docs/` — documentation source

## Feedback welcome

If you’re interested in using F# beyond simple 2D games—while still staying in the MonoGame ecosystem—issues and PRs are very welcome.
