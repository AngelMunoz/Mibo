---
title: Assets
category: Amenities
categoryindex: 12
index: 4
---

# Assets (loading + caching)

Mibo provides an `IAssets` interface for loading and caching game assets. Each backend implements it against its native resource system with automatic caching, so you never load the same asset twice. The shape is the same across backends; the concrete asset *types* differ (raylib types vs XNA/MonoGame types).

> _**NOTE**_: Both backend `IAssets` implementations live in the **`Mibo.Elmish`** namespace — resolve them the same way via `GameContext.getService<IAssets> ctx`. Only their **return types** and **path conventions** differ (raylib: raw file paths with extension; MonoGame: content-pipeline names without extension). See [MonoGame type quirks](monogame-types.html).

## Two layers

- **`IAssetCache`** (`Mibo.Core`, backend-agnostic) — the generic cache surface: `Get`/`Create`/`GetOrCreate`/`Clear`/`Dispose` for *any* user asset by string key. Portable code (and the Headless runner) can use this without referencing a backend.
- **`IAssets`** (backend-specific, extends `IAssetCache`) — the typed loaders (`Texture`, `Font`, `Sound`, `Model`, …). These return backend-native types.

## The `IAssets` interface (per backend)

The typed loaders differ because the native types differ:

```fsharp
// raylib backend — returns Raylib_cs types
type IAssets =
  inherit IAssetCache
  abstract Texture: path: string -> Texture2D
  abstract Font:     path: string -> Font
  abstract Sound:    path: string -> Sound
  abstract Model:    path: string -> Model
  abstract ModelAnimations: path: string -> ModelAnimation[]

// MonoGame backend — returns Microsoft.Xna.Framework types
type IAssets =
  inherit IAssetCache
  abstract Texture: path: string -> Texture2D
  abstract Font:     path: string -> SpriteFont
  abstract Sound:    path: string -> SoundEffect
  abstract Model:    path: string -> Model
  abstract Effect:   path: string -> Effect
  abstract ModelAnimations: path: string -> Animation3DClips
  abstract AnimatedMesh:    path: string -> AnimatedMesh voption
```

## Usage

Access assets through the `GameContext`. The `path` convention differs by backend:

```fsharp
let init (ctx: GameContext): struct(Model * Cmd<Msg>) =
  let assets = GameContext.getService<IAssets> ctx
  // raylib: paths are loose files on disk
  let player = assets.Texture("sprites/player.png")
  let font = assets.Font("fonts/ui.ttf")
  let enemyModel = assets.Model("models/enemy.glb")

  // MonoGame: paths are content-pipeline asset names (no extension);
  // the .xnb must be built by the MonoGame content pipeline.
  // let player = assets.Texture("sprites/player")
  // let font = assets.Font("fonts/ui")
  ...
```

| Method | raylib returns | MonoGame returns | Notes |
|--------|----------------|------------------|-------|
| `Texture` | `Texture2D` | `Texture2D` | 2D image |
| `Font` | `Font` | `SpriteFont` | raylib: TrueType file; MonoGame: compiled `.spritefont` |
| `Sound` | `Sound` | `SoundEffect` | Audio |
| `Model` | `Model` | `Model` | 3D model |
| `Effect` | — | `Effect` | MonoGame: compiled `.mgfx` |
| `ModelAnimations` | `ModelAnimation[]` | `Animation3DClips` | Skeletal animation clips |
| `AnimatedMesh` | — | `AnimatedMesh voption` | MonoGame: loaded via Assimp at runtime |

> _**NOTE (MonoGame animations)**_: MonoGame's content pipeline does not preserve animation
> data in `.xnb`. `ModelAnimations`/`AnimatedMesh` load the **raw** model file
> (`.glb`/`.gltf`/`.fbx`) via Assimp at runtime — the path is a filesystem path, and you must
> include the raw file in the output directory (e.g. `<CopyToOutputDirectory>` / `<Content>` in
> the `.fsproj`).

## Texture configuration (raylib)

The raylib loader generates mipmaps and forces **trilinear** filtering on every texture at load time — good for 3D/PBR surfaces, but it makes tiles sampled from a gutterless spritesheet bleed at the edges. A texture's filter is a property of the texture itself (not the draw batch), so override it per texture with the `Texture` helper module:

```fsharp
let assets = GameContext.getService<IAssets> ctx
// Point (nearest) filtering — stops adjacent tiles bleeding into each other.
let atlas = assets.Texture("tiles.png") |> Texture.filter TextureFilter.Point
```

| Helper | Description |
|--------|-------------|
| `Texture.filter TextureFilter.Point tex` | Set the texture's filter (overrides the load-time trilinear default) |
| `Texture.wrap TextureWrap.Repeat tex` | Set the wrap/addressing mode (Clamp/Repeat/MirrorClamp/MirrorRepeat) |
| `Texture.mipmaps tex` | Generate mipmaps (the loader already does this on load) |

Apply it once (e.g. in `init`) — not every frame — since it mutates the cached texture's sampler. (`TextureFilter` is `Raylib_cs.TextureFilter`: `Point`, `Bilinear`, `Trilinear`, `Aniso4x/8x/16x`; `TextureWrap` is `Raylib_cs.TextureWrap`: `Clamp`, `Repeat`, `MirrorClamp`, `MirrorRepeat`.) MonoGame controls sampling per draw via `.setSamplerState(...)` instead; see [2D Buffer & Commands](graphics2d/buffer-and-commands.html).



The inherited `IAssetCache` members work on any backend and let portable code cache custom
assets without referencing backend types:

```fsharp
let cache = GameContext.getService<IAssetCache> ctx
let config = cache.GetOrCreate("gameConfig", fun () -> loadConfig())
```

| Member | Description |
|--------|-------------|
| `Get<'T> key` | Retrieve a cached custom asset (`'T voption`) |
| `Create(key, factory)` | Create + cache a custom asset |
| `GetOrCreate(key, factory)` | Get cached, or create + cache |
| `Clear()` | Clear custom-asset caches |
| `Dispose()` | Unload resources + clear caches |

## Cache Behavior

**Automatic caching applies to:**
- All typed assets (texture, font, sound, model) — first call loads, subsequent calls return the cached reference.
- Custom assets via `IAssetCache`.

**Clearing caches:**

```fsharp
let assets = GameContext.getService<IAssets> ctx
assets.Dispose()
```

This unloads all GPU resources and clears all caches.

## Performance Notes

- First load reads from disk; subsequent loads return the cached reference.
- No built-in eviction — caches grow with unique keys loaded.
- GPU resources are created once and cached.

For large games, consider chunked loading (per level/biome) with separate `IAssets` scopes.

## Planned features

The following are **not yet implemented** but are planned:

- **JSON helpers** (JDeck integration for loading `.json` files)
- **Custom file loaders** (`fromCustom`, `fromCustomCache`)
