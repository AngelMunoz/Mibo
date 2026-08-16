---
title: Assets
index: 3
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# Assets (loading + caching)

Mibo.Raylib provides a simple `IAssets` interface for loading and caching game assets. It wraps raylib's resource loader functions with automatic caching so you never load the same texture twice.

## The `IAssets` interface

```fsharp
type IAssets =
  abstract Texture: path: string -> Texture2D
  abstract Font: path: string -> Font
  abstract Sound: path: string -> Sound
  abstract Model: path: string -> Model
  abstract Get: key: string -> 'T voption
  abstract Create: key: string * factory: (unit -> 'T) -> 'T
  abstract GetOrCreate: key: string * factory: (unit -> 'T) -> 'T
  abstract Clear: unit -> unit
  abstract Dispose: unit -> unit
```

Each method loads the file on first call and caches it. Subsequent calls return the cached reference.

## Usage

Access assets through the `GameContext`:

```fsharp
let init (ctx: GameContext): struct(Model * Cmd<Msg>) =
  let player = ctx.Assets.Texture("sprites/player.png")
  let font = ctx.Assets.Font("fonts/ui.ttf")
  let bgm = ctx.Assets.Sound("audio/background.wav")
  let enemyModel = ctx.Assets.Model("models/enemy.glb")

  { PlayerTex = player
    Font = font
    Bgm = bgm
    Enemy = enemyModel }, Cmd.none
```

All these functions cache results automatically:

| Method       | Returns           | Description                     |
|--------------|-------------------|---------------------------------|
| `Texture`    | `Texture2D`       | 2D image asset                  |
| `Font`       | `Font`            | TrueType/bitmap font            |
| `Sound`      | `Sound`           | Audio effect                    |
| `Model`      | `Model`           | 3D model                        |
| `Get`        | `'T voption`      | Retrieve cached custom asset    |
| `Create`     | `'T`              | Create and cache custom asset   |
| `GetOrCreate`| `'T`              | Get cached or create + cache    |
| `Clear`      | `unit`            | Clear custom asset caches       |

## Cache Behavior

**Automatic caching applies to:**
- All standard assets (texture, font, sound, model)
- First call loads from disk; subsequent calls return cached reference

**Clearing caches:**

```fsharp
ctx.Assets.Dispose()
```

This unloads all GPU resources and clears all caches.

## Performance Notes

- First load reads from disk; subsequent loads return cached reference
- No built-in eviction — caches grow with unique keys loaded
- GPU resources are created once and cached

For large games, consider chunked loading (per level/biome) with separate `IAssets` scopes.

## Planned features

The following are **not yet implemented** but are planned:

- **JSON helpers** (JDeck integration for loading `.json` files)
- **Custom file loaders** (`fromCustom`, `fromCustomCache`)
