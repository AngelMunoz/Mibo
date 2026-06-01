namespace Mibo.Elmish

open System
open System.Collections.Generic
open System.IO
open Raylib_cs

/// <summary>
/// Per-game asset loader/cache service.
/// </summary>
/// <remarks>
/// Provides cached loading for textures, fonts, sounds, models, and shaders
/// from loose files (no content pipeline). Also supports generic typed cache
/// for custom asset types.
/// </remarks>
/// <example>
/// <code>
/// let assets = GameContext.getService&lt;IAssets&gt; ctx
/// let tex = assets.Texture "sprites/player.png"
/// let font = assets.Font "fonts/main.ttf"
/// let config = assets.GetOrCreate "gameConfig" (fun () -> loadConfig())
/// </code>
/// </example>
type IAssets =
  /// <summary>Loads and caches a <see cref="T:Raylib_cs.Texture2D"/> from file.</summary>
  abstract Texture: path: string -> Texture2D

  /// <summary>Loads and caches a <see cref="T:Raylib_cs.Font"/> from file.</summary>
  abstract Font: path: string -> Font

  /// <summary>Loads and caches a <see cref="T:Raylib_cs.Sound"/> from file.</summary>
  abstract Sound: path: string -> Sound

  /// <summary>Loads and caches a <see cref="T:Raylib_cs.Model"/> from file.</summary>
  abstract Model: path: string -> Model

  /// <summary>Loads and caches <see cref="T:Raylib_cs.ModelAnimation"/>[] from file.</summary>
  /// <remarks>
  /// Loads all skeletal animations from a model file (glb/gltf/iqm).
  /// Returns an empty array if the model has no animations.
  /// </remarks>
  abstract ModelAnimations: path: string -> ModelAnimation[]

  /// <summary>Gets a previously created custom asset by key.</summary>
  abstract Get<'T> : key: string -> 'T voption

  /// <summary>Creates and caches a custom asset using the provided factory.</summary>
  abstract Create<'T> : key: string * factory: (unit -> 'T) -> 'T

  /// <summary>Gets a cached asset or creates it if not present.</summary>
  /// <remarks>This is the preferred method for custom assets - idempotent, ensures assets are created only once.</remarks>
  abstract GetOrCreate<'T> : key: string * factory: (unit -> 'T) -> 'T

  /// <summary>Clears all caches (does not dispose GPU resources).</summary>
  abstract Clear: unit -> unit

  /// <summary>Disposes all cached assets and clears caches.</summary>
  abstract Dispose: unit -> unit

/// <summary>
/// Implementation of <see cref="T:Mibo.Elmish.IAssets"/> with dictionary-based caches.
/// </summary>
/// <param name="baseAssetPath">Optional base path prepended to all relative asset paths.</param>
type AssetsService(baseAssetPath: string voption) =

  let resolvePath(path: string) =
    match baseAssetPath with
    | ValueSome bp -> Path.Combine(bp, path)
    | ValueNone -> path

  let typedCache = Dictionary<string, obj>()

  let textures = Dictionary<string, Texture2D>()
  let fonts = Dictionary<string, Font>()
  let sounds = Dictionary<string, Sound>()
  let models = Dictionary<string, Model>()
  let modelAnimations = Dictionary<string, ModelAnimation[]>()

  member _.BasePath = baseAssetPath

  interface IAssets with
    member _.Texture(path) =
      let resolved = resolvePath path

      match textures.TryGetValue(resolved) with
      | true, tex -> tex
      | _ ->
        let tex = Raylib.LoadTexture(resolved)
        textures.Add(resolved, tex)
        tex

    member _.Font(path) =
      let resolved = resolvePath path

      match fonts.TryGetValue(resolved) with
      | true, font -> font
      | _ ->
        let font = Raylib.LoadFont(resolved)
        fonts.Add(resolved, font)
        font

    member _.Sound(path) =
      let resolved = resolvePath path

      match sounds.TryGetValue(resolved) with
      | true, sound -> sound
      | _ ->
        let sound = Raylib.LoadSound(resolved)
        sounds.Add(resolved, sound)
        sound

    member _.Model(path) =
      let resolved = resolvePath path

      match models.TryGetValue(resolved) with
      | true, m -> m
      | _ ->
        let m = Raylib.LoadModel(resolved)
        models.Add(resolved, m)
        m

    member _.ModelAnimations(path) =
      let resolved = resolvePath path

      match modelAnimations.TryGetValue(resolved) with
      | true, anims -> anims
      | _ ->
        let animsSpan = Raylib.LoadModelAnimations(resolved)
        let anims = Array.zeroCreate<ModelAnimation> animsSpan.Length

        for i = 0 to animsSpan.Length - 1 do
          anims[i] <- animsSpan[i]

        modelAnimations.Add(resolved, anims)
        anims

    member _.Get<'T>(key: string) : 'T voption =
      match typedCache.TryGetValue(key) with
      | true, (:? 'T as v) -> ValueSome v
      | _ -> ValueNone

    member _.Create<'T>(key: string, factory: unit -> 'T) : 'T =
      let value = factory()
      typedCache[key] <- box value
      value

    member _.GetOrCreate<'T>(key: string, factory: unit -> 'T) : 'T =
      match typedCache.TryGetValue(key) with
      | true, (:? 'T as v) -> v
      | _ ->
        let value = factory()
        typedCache[key] <- box value
        value

    member _.Clear() =
      typedCache.Clear()
      textures.Clear()
      fonts.Clear()
      sounds.Clear()
      models.Clear()
      modelAnimations.Clear()

    member _.Dispose() =
      for kv in textures do
        Raylib.UnloadTexture(kv.Value)

      textures.Clear()

      for kv in fonts do
        Raylib.UnloadFont(kv.Value)

      fonts.Clear()

      for kv in sounds do
        Raylib.UnloadSound(kv.Value)

      sounds.Clear()

      for kv in models do
        Raylib.UnloadModel(kv.Value)

      models.Clear()

      for kv in modelAnimations do
        Raylib.UnloadModelAnimations(kv.Value.AsSpan())

      modelAnimations.Clear()

      typedCache.Clear()

/// Factory for <see cref="T:Mibo.Elmish.IAssets"/> implementations.
module AssetsService =
  /// <summary>Creates an asset service with no base path.</summary>
  let create() : IAssets = new AssetsService(ValueNone) :> IAssets

  /// <summary>Creates an asset service where all relative paths are prepended with the given base path.</summary>
  let createWithBasePath(basePath: string) : IAssets =
    new AssetsService(ValueSome basePath) :> IAssets
