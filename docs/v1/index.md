---
title: Mibo v1 (Raylib-only) Archive
index: 0
---

# Mibo v1 Docs — Archived (Raylib-only)

These pages are the original documentation for the **raylib-only** release of Mibo (when
`Mibo.Raylib` was the single package). They are kept here as a frozen snapshot so users on
that release can still find matching docs. All of these docs describe the raylib backend only.

**Looking for current docs?** Mibo is now **multi-backend** — a shared `Mibo.Core` with
pluggable `Mibo.Raylib` and `Mibo.MonoGame` backends. The up-to-date documentation lives at
the [site root](../index.html).

## What changed since v1

The biggest change is the **package split**:

```text
Mibo.Core          ← shared core (Cmd, Sub, System, Program, IRenderer, GameContext,
                     IInput/IInputMapper contracts, IAssetCache, HeadlessProgram, Layout)
Mibo.Raylib        ← raylib backend (host: RaylibGame, GLSL shaders)
Mibo.MonoGame      ← MonoGame backend (host: MiboGame, HLSL .fx shaders)
```

See [Migrating to Mibo v2](../migration-to-v2.html) for the full list of breaking
changes, and [Migrating from MonoGame](../mvu/migration-from-monogame.html) for the original
MonoGame → Mibo migration guide.

## Archived pages

- [Index (original landing)](index.html)
- [Rendering Overview](rendering.html)
- [Shaders](shaders.html) — GLSL only
- [Assets](assets.html) — raylib types only
- [Camera](camera.html)
- [Input](input.html)
- [Animation](animation.html) / [Animation 3D](animation3d.html)
- [Commands](commands.html) / [Subscriptions](subscriptions.html)
- [2D Rendering](graphics2d/overview.html)
- [3D Rendering](graphics3d/overview.html)