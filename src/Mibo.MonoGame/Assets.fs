namespace Mibo.Elmish

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics

// ─────────────────────────────────────────────────────────────────────────────
// MonoGame asset service.
//
// Mirrors the raylib backend's IAssets/AssetsService shape: typed loaders
// (Texture2D/SpriteFont/SoundEffect/Model/Effect) cached in dictionaries,
// extending the Core IAssetCache so portable code can cache custom assets
// without referencing a backend.
//
// The difference from raylib: MonoGame loads XNB-compiled assets via
// ContentManager.Load<'T> (no loose-file loading). The GraphicsDevice and
// ContentManager are retrieved from the GameContext service registry, where
// MiboGame registers them at startup (see MonoGameGameContext.register).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-game asset loader/cache service for the MonoGame backend.
/// </summary>
/// <remarks>
/// Provides cached loading for textures, fonts, sounds, models, and effects via
/// the MonoGame content pipeline. Extends <see cref="T:Mibo.Elmish.IAssetCache"/>
/// so portable code can cache custom assets without referencing a backend.
/// </remarks>
/// <example>
/// <code>
/// let assets = GameContext.getService&lt;IAssets&gt; ctx
/// let tex = assets.Texture "sprites/player"
/// let font = assets.Font "fonts/main"
/// let config = assets.GetOrCreate "gameConfig" (fun () -> loadConfig())
/// </code>
/// </example>
type IAssets =
  inherit IAssetCache

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Graphics.Texture2D"/> from the content pipeline.</summary>
  abstract Texture: path: string -> Texture2D

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Graphics.SpriteFont"/> from the content pipeline.</summary>
  abstract Font: path: string -> SpriteFont

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Audio.SoundEffect"/> from the content pipeline.</summary>
  abstract Sound: path: string -> SoundEffect

  /// <summary>Loads and caches a 3D <see cref="T:Microsoft.Xna.Framework.Graphics.Model"/> from the content pipeline.</summary>
  abstract Model: path: string -> Model

  /// <summary>Loads and caches an <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/> from the content pipeline.</summary>
  abstract Effect: path: string -> Effect

/// <summary>
/// Implementation of <see cref="T:Mibo.Elmish.IAssets"/> backed by a MonoGame
/// <c>ContentManager</c>, with dictionary-based caches per asset type.
/// </summary>
/// <param name="content">The MonoGame content manager used to load XNB assets.</param>
type AssetsService(content: ContentManager) =

  let typedCache = Dictionary<string, obj>()

  let textures = Dictionary<string, Texture2D>()
  let fonts = Dictionary<string, SpriteFont>()
  let sounds = Dictionary<string, SoundEffect>()
  let models = Dictionary<string, Model>()
  let effects = Dictionary<string, Effect>()

  /// <summary>The <c>ContentManager</c> this service loads from.</summary>
  member _.Content = content

  interface IAssets with
    member _.Texture(path) =
      match textures.TryGetValue(path) with
      | true, tex -> tex
      | _ ->
        let tex = content.Load<Texture2D>(path)
        textures.Add(path, tex)
        tex

    member _.Font(path) =
      match fonts.TryGetValue(path) with
      | true, font -> font
      | _ ->
        let font = content.Load<SpriteFont>(path)
        fonts.Add(path, font)
        font

    member _.Sound(path) =
      match sounds.TryGetValue(path) with
      | true, sound -> sound
      | _ ->
        let sound = content.Load<SoundEffect>(path)
        sounds.Add(path, sound)
        sound

    member _.Model(path) =
      match models.TryGetValue(path) with
      | true, m -> m
      | _ ->
        let m = content.Load<Model>(path)
        models.Add(path, m)
        m

    member _.Effect(path) =
      match effects.TryGetValue(path) with
      | true, e -> e
      | _ ->
        let e = content.Load<Effect>(path)
        effects.Add(path, e)
        e

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
      effects.Clear()

    member _.Dispose() =
      // ContentManager owns the XNB assets; individual Texture2D/Effect/etc.
      // instances are NOT disposed here because ContentManager.Unload handles
      // them. Disposing typed user assets would be unsafe if they hold
      // references into content-loaded resources. Clear caches only.
      typedCache.Clear()
      textures.Clear()
      fonts.Clear()
      sounds.Clear()
      models.Clear()
      effects.Clear()

/// Factory for <see cref="T:Mibo.Elmish.IAssets"/> implementations.
module AssetsService =
  /// <summary>Creates an asset service over the given <c>ContentManager</c>.</summary>
  let create(content: ContentManager) : IAssets =
    new AssetsService(content) :> IAssets

  /// <summary>
  /// Creates an asset service from a <see cref="T:Mibo.Elmish.GameContext"/>,
  /// resolving the registered <c>ContentManager</c> (registered by the host).
  /// </summary>
  let createFromContext(ctx: GameContext) : IAssets =
    let content = MonoGameGameContext.getContentManager ctx
    create content
