---
title: Assets
category: Amenities
index: 21
---

# Assets (loading + caching)

Mibo exposes a per-game asset service (`IAssets`) that wraps MonoGame’s `ContentManager` plus a simple cache.

What it provides:

- **no module-level global caches** (safe with multiple game instances/tests)
- **fast repeated loads** (`Texture`, `Model`, `Effect`, etc are cached)
- **custom assets** (shaders, JSON config, decoded content)

## Enabling the service

Add it in your composition root:

```fsharp
Program.mkProgram init update
|> Program.withAssets
```

Then use the helpers from `Mibo.Elmish.Assets` anywhere you have a `GameContext`:

```fsharp
let init (ctx: GameContext): struct(Model, Cmd<Msg>) =
  let player = Assets.texture "sprites/player" ctx
  let font = Assets.font "fonts/ui" ctx
  let shader = Assets.effect "Effects/Grid" ctx
  { PlayerTex = player; Font = font; Shader = shader }, Cmd.none
```

## Custom assets (GPU-backed or not)

You can cache custom values (GPU-backed or not):

```fsharp
let outlineFx =
  Assets.getOrCreate "OutlineEffect" (fun gd -> new Effect(gd, bytecode)) ctx
```

## JSON helpers

Mibo includes JSON helpers via [JDeck](https://github.com/AngelMunoz/JDeck):

- `Assets.fromJson path decoder`
- `Assets.fromJsonCache path decoder ctx`

These read JSON files from disk and optionally cache them for the game lifetime.

If you’re writing your own decoders, see the JDeck docs:

- Source: https://github.com/AngelMunoz/JDeck
- Guide: https://angelmunoz.github.io/JDeck/

## Building your own “store” on top of `IAssets`

`IAssets` is a convenient place to centralize _loading + reuse_.

For game-specific data sets (skills, items, quests, dialogue, etc), it can be useful to wrap `IAssets` in a specialized store that:

- owns decoding/validation
- chooses a backend (JSON, SQLite, embedded resources, network)
- can decide whether to cache (and how)

### Example: `SkillStore` (JSON)

```fsharp
open Mibo.Elmish
open JDeck

type SkillId = SkillId of string

type Skill = {
  Id: SkillId
  Name: string
  CooldownSeconds: float32
}

module SkillDecoders =
  let skillDecoder : JDeck.Decoder<Skill> =
    fun json -> JDeck.decode {
      let! id = json |> Required.Property.get("id", Required.string)
      let! name = json |> Required.Property.get("name", Required.string)
      let! cd = json |> Required.Property.get("cooldownSeconds", Required.float32)
      return {
        Id = SkillId id
        Name = name
        CooldownSeconds = cd
      }
    }

type SkillStore(assets: IAssets) =
  /// Load a skill list (no caching). Useful if you want to manage lifetime yourself.
  member _.LoadAll(path: string) : Skill list =
    Assets.fromJson path (Required.list SkillDecoders.skillDecoder)

  /// Load and cache (game-lifetime). Useful for static data.
  member _.LoadAllCached(path: string, ctx: GameContext) : Skill list =
    Assets.fromJsonCache path (Required.list SkillDecoders.skillDecoder) ctx

  /// A typed cache for an indexed view.
  member _.IndexByIdCached(path: string, ctx: GameContext) : Map<SkillId, Skill> =
    Assets.getOrCreate ("SkillStore/IndexById/" + path)
      (fun _gd ->
        let skills = Assets.fromJson path (Required.list SkillDecoders.skillDecoder)
        skills |> List.map (fun s -> s.Id, s) |> Map.ofList)
      ctx
```

You can create the store from context:

```fsharp
let store = SkillStore(Assets.getService ctx)
let skillsById = store.IndexByIdCached("Content/skills.json", ctx)
```

### Example: `SkillStore` (SQLite)

If your source of truth is a database, the same idea applies: the store is where you choose I/O strategy and caching.

```fsharp
type SkillStoreDb(conn: System.Data.IDbConnection) =
  member _.GetById(id: SkillId) : Skill option =
    // query, map rows -> Skill
    None
```

In this case you typically keep caching _inside the store_ (LRU, size cap, explicit invalidation) instead of using `IAssets` as a forever-cache.

## Cache growth and lifetime

The built-in `IAssets` caches are designed for **game-lifetime reuse**:

- the cache grows as you load more keys
- there is no built-in eviction policy

If memory usage is a concern, prefer one of these patterns:

1. Use non-cached loads (`Assets.fromJson`) and manage lifetime in your own store.
2. Build a store with an explicit cap/eviction strategy (LRU) and keep only hot entries.
3. Structure data so it loads in “chunks” (per level/biome), and keep chunk lifetime separate from the global `IAssets` cache.

## Lifetime and disposal

- Content pipeline assets (`ContentManager.Load`) are managed by MonoGame.
- “Custom” assets you store via `Create/GetOrCreate` are kept in a dictionary.
- `IAssets.Dispose()` will dispose any cached values that implement `IDisposable`.

If you store GPU resources (Effects, RenderTargets, etc) in the cache, prefer `GetOrCreate` so they’re created once per game.
