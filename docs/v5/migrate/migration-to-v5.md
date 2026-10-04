---
title: Migrating to Mibo v5
category: Migrating
categoryindex: 7
index: 3
---

# Migrating to Mibo v5

This page collects the changes between the last v4 release (`4.5.3`) and v5.
v5 does not change any code you wrote: it changes **which packages carry which
code**. Mibo now ships as two independent runtime lanes — MVU and Adaptive — on
top of a shared kernel, and every namespace, type, and member keeps the home it
always had.

> _**The short answer for most games:** swap one package reference, recompile,
> done. There are no source-level breaks in v5. A full public-API diff between
> the v4 assemblies and the v5 assemblies shows zero names removed and zero
> names added. The only required edit is in your `.fsproj`._

## What v5 is

```
Mibo.Core              ← the shared kernel (GameContext, GameTime, SubId, IRenderer,
                         RenderBuffer, GameConfig, IInput/IInputMapper contracts,
                         IAssetCache, Layout/Layout3D, Diagnostics)
Mibo.Mvu               ← the MVU runtime (Cmd/Sub, Program builders, System pipeline,
                         ElmishLoop, HeadlessProgram, MVU input subscriptions)
Mibo.Adaptive          ← the incremental-computation library (unchanged, still
                         dependency-free)
Mibo.Adaptive.Mibo     ← the Mibo-side adaptive runtime (AdaptiveProgram,
                         AdaptiveHeadless, AdaptiveInput)
Mibo.Raylib            ← the neutral raylib shell (renderers, camera, windowing,
                         input/assets implementations; no hosts)
Mibo.Raylib.Mvu        ← MVU host for raylib (RaylibProgram, RaylibGame,
                         InputMapper.subscribe)
Mibo.Raylib.Adaptive   ← adaptive host for raylib (AdaptiveRaylibGame,
                         InputMapper.subscribeAdaptive)
Mibo.MonoGame          ← the neutral MonoGame shell (same surface as the raylib shell)
Mibo.MonoGame.Mvu      ← MVU host for MonoGame (MonoGameProgram, MiboGame,
                         InputMapper.subscribe)
Mibo.MonoGame.Adaptive ← adaptive host for MonoGame (AdaptiveMonoGameProgram,
                         AdaptiveMonoGameGame, InputMapper.subscribeAdaptive)
```

The motivation: in v4, `Mibo.Core` depended on `Mibo.Adaptive`, so every game —
including a plain MVU game — pulled the adaptive dataflow library. v5 removes
that dependency. MVU installs pull no adaptive code, and adaptive installs pull
no MVU code.

## 1. Swap the package reference for your lane

**Who is affected:** everyone who references `Mibo.Raylib` or `Mibo.MonoGame`.
Consumers of `Mibo.Core` or `Mibo.Adaptive` alone need no change.

| Your game | Before (v4) | After (v5) |
|-----------|-------------|------------|
| MVU, raylib | `Mibo.Raylib` | `Mibo.Raylib.Mvu` |
| MVU, MonoGame | `Mibo.MonoGame` | `Mibo.MonoGame.Mvu` |
| Adaptive, raylib | `Mibo.Raylib` + `Mibo.Adaptive` | `Mibo.Raylib.Adaptive` |
| Adaptive, MonoGame | `Mibo.MonoGame` + `Mibo.Adaptive` | `Mibo.MonoGame.Adaptive` |
| Headless MVU (servers, tests) | `Mibo.Core` | `Mibo.Mvu` |
| Headless adaptive | `Mibo.Core` | `Mibo.Adaptive.Mibo` |

Before (v4, MVU + MonoGame):

```xml
<PackageReference Include="Mibo.MonoGame" Version="4.*" />
<PackageReference Include="MonoGame.Framework.Native" Version="3.8.5" PrivateAssets="all" />
```

After (v5):

```xml
<PackageReference Include="Mibo.MonoGame.Mvu" Version="5.*" />
<PackageReference Include="MonoGame.Framework.Native" Version="3.8.5" PrivateAssets="all" />
```

Before (v4, adaptive + raylib):

```xml
<PackageReference Include="Mibo.Raylib" Version="4.*" />
<PackageReference Include="Mibo.Adaptive" Version="4.*" />
```

After (v5) — the adaptive host brings the shell, the kernel, `Mibo.Adaptive`,
and `Mibo.Adaptive.Mibo` transitively:

```xml
<PackageReference Include="Mibo.Raylib.Adaptive" Version="5.*" />
```

Notes:

- Keep `MonoGame.Framework.Native` (`PrivateAssets="all"`) exactly as it is; it
  supplies the managed MonoGame assembly and does not flow to your consumers.
- If you referenced `Mibo.Adaptive` explicitly, you may keep the reference; it
  is no longer required, and the stale `4.*` pin some templates carried would
  no longer resolve, since `Mibo.Adaptive` versions independently (1.x line).
  Let the host package bring it, or pin the adaptive version you actually use.
- Headless consumers: `Mibo.Mvu` (Elmish `HeadlessProgram`/`HeadlessRunner`) and
  `Mibo.Adaptive.Mibo` (`AdaptiveHeadless`) work with no backend referenced, as
  their v4 counterparts did inside `Mibo.Core`.

## 2. Recompile

**Who is affected:** everyone. This is the v5 binary break, and it is the same
one every package reorganization causes.

Code that moved packages kept its assembly identity change: `Program`,
`Cmd`, and `Sub` now live in `Mibo.Mvu.dll` (were `Mibo.Core.dll`);
`AdaptiveProgram` and `AdaptiveHeadless` live in `Mibo.Adaptive.Mibo.dll`;
`RaylibGame` lives in `Mibo.Raylib.Mvu.dll`, and so on. Pre-compiled assemblies
built against v4 must be recompiled against v5 — the standard contract of a
major version. There is nothing to change in your source while doing it.

A few runtime-neutral types that both runtimes share moved from the v4
Elmish-flavored files into the kernel and keep their exact names and
`Mibo.Elmish` namespace: `GameContext`, `IRenderer<'Model>`,
`RenderBuffer<_,_>`, `GameTime`, `FixedStep`, `SubId`, and `GameConfig`.
`open Mibo.Elmish` resolves them exactly as before.

## 3. Templates

The `dotnet new` template names are unchanged (`mibo-2d`, `mibo-mg-3d-adaptive`,
…). The MVU starters now reference the `*.Mvu` packages and the adaptive
starters reference the single `*.Adaptive` package; all starters pin `5.*`.

## 4. Versioning notes

- The repo-versioned packages (`Mibo.Core`, `Mibo.Mvu`, `Mibo.Adaptive.Mibo`,
  `Mibo.Raylib`, `Mibo.Raylib.*`, `Mibo.MonoGame`, `Mibo.MonoGame.*`,
  `Mibo.Templates`) move together to `5.0.0`.
- `Mibo.Adaptive` keeps its own changelog and version line, as in v4. The
  adaptive host packages declare a dependency on the adaptive version current
  at release time, so an adaptive-lane install always gets a compatible one.
