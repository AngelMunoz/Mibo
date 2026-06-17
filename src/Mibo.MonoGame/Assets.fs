namespace Mibo.Elmish

open System
open System.Collections.Generic
open System.IO
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

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Graphics.Texture2D"/> from a loose file.</summary>
  abstract TextureFromFile: path: string -> Texture2D

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Graphics.SpriteFont"/> from the content pipeline.</summary>
  abstract Font: path: string -> SpriteFont

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Audio.SoundEffect"/> from the content pipeline.</summary>
  abstract Sound: path: string -> SoundEffect

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Audio.SoundEffect"/> from a loose file.</summary>
  abstract SoundFromFile: path: string -> SoundEffect

  /// <summary>Loads and caches a 3D <see cref="T:Microsoft.Xna.Framework.Graphics.Model"/> from the content pipeline.</summary>
  abstract Model: path: string -> Model

  /// <summary>Loads and caches an <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/> from the content pipeline.</summary>
  abstract Effect: path: string -> Effect

/// <summary>
/// Implementation of <see cref="T:Mibo.Elmish.IAssets"/> backed by a MonoGame
/// <c>ContentManager</c>, with dictionary-based caches per asset type.
/// </summary>
/// <param name="content">The MonoGame content manager used to load XNB assets.</param>
type AssetsService
  (content: ContentManager, graphicsDevice: GraphicsDevice voption) =

  let typedCache = Dictionary<string, obj>()

  let textures = Dictionary<string, Texture2D>()
  let fileTextures = Dictionary<string, Texture2D>()
  let fonts = Dictionary<string, SpriteFont>()
  let sounds = Dictionary<string, SoundEffect>()
  let fileSounds = Dictionary<string, SoundEffect>()
  let models = Dictionary<string, Model>()
  let effects = Dictionary<string, Effect>()

  /// <summary>The <c>ContentManager</c> this service loads from.</summary>
  member _.Content = content

  /// <summary>The graphics device used for loose-file texture loading.</summary>
  member _.GraphicsDevice = graphicsDevice

  interface IAssets with
    member _.Texture(path) =
      match textures.TryGetValue(path) with
      | true, tex -> tex
      | _ ->
        let tex = content.Load<Texture2D>(path)
        textures.Add(path, tex)
        tex

    member _.TextureFromFile(path) =
      match fileTextures.TryGetValue(path) with
      | true, tex -> tex
      | _ ->
        match graphicsDevice with
        | ValueNone ->
          invalidOp
            $"TextureFromFile requires a GraphicsDevice. Use AssetsService.createFromContext or provide a device."
        | ValueSome gd ->
          use stream = File.OpenRead(path)
          let tex = Texture2D.FromStream(gd, stream)
          fileTextures.Add(path, tex)
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

    member _.SoundFromFile(path) =
      match fileSounds.TryGetValue(path) with
      | true, sound -> sound
      | _ ->
        use stream = File.OpenRead(path)
        let sound = SoundEffect.FromStream(stream)
        fileSounds.Add(path, sound)
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
      // Loose-file assets are loaded via Texture2D.FromStream / SoundEffect.FromStream
      // and are NOT owned by ContentManager, so they must be disposed here
      // (the XNB-loaded caches below are left for ContentManager.Unload).
      for kvp in fileTextures do
        kvp.Value.Dispose()

      for kvp in fileSounds do
        kvp.Value.Dispose()

      typedCache.Clear()
      textures.Clear()
      fileTextures.Clear()
      fonts.Clear()
      sounds.Clear()
      fileSounds.Clear()
      models.Clear()
      effects.Clear()

    member _.Dispose() =
      // Dispose user-created IDisposable assets. ContentManager owns the
      // XNB-loaded textures/fonts/etc., so the typed-loader caches below are
      // left for ContentManager.Unload — only the generic typedCache is ours.
      // Loose-file assets (FromStream) are unmanaged by ContentManager too,
      // so they are disposed here as well.
      for kvp in typedCache do
        match kvp.Value with
        | :? IDisposable as d -> d.Dispose()
        | _ -> ()

      for kvp in fileTextures do
        kvp.Value.Dispose()

      for kvp in fileSounds do
        kvp.Value.Dispose()

      typedCache.Clear()
      textures.Clear()
      fileTextures.Clear()
      fonts.Clear()
      sounds.Clear()
      fileSounds.Clear()
      models.Clear()
      effects.Clear()

/// Factory for <see cref="T:Mibo.Elmish.IAssets"/> implementations.
module AssetsService =
  /// <summary>Creates an asset service over the given <c>ContentManager</c>.
  /// Loose-file texture loading requires a GraphicsDevice; use createFromContext for full support.
  /// </summary>
  let create(content: ContentManager) : IAssets =
    new AssetsService(content, ValueNone) :> IAssets

  /// <summary>Creates an asset service with an explicit graphics device for loose-file texture loading.</summary>
  let createWithDevice
    (content: ContentManager)
    (graphicsDevice: GraphicsDevice)
    : IAssets =
    new AssetsService(content, ValueSome graphicsDevice) :> IAssets

  /// <summary>
  /// Creates an asset service from a <see cref="T:Mibo.Elmish.GameContext"/>,
  /// resolving the registered <c>ContentManager</c> and <c>GraphicsDevice</c>.
  /// </summary>
  let createFromContext(ctx: GameContext) : IAssets =
    let content = MonoGameGameContext.getContentManager ctx
    let gd = MonoGameGameContext.getGraphicsDevice ctx
    new AssetsService(content, ValueSome gd) :> IAssets
