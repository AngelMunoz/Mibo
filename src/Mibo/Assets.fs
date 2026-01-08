namespace Mibo.Elmish

open System.Collections.Generic
open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Content
open System.IO

/// <summary>
/// Per-game asset loader/cache service.
/// </summary>
/// <remarks>
/// <para>Unlike the legacy <c>Assets</c> module API (which relies on module-level mutable state),
/// values of <see cref="T:Mibo.Elmish.IAssets"/> are safe to create per <see cref="T:Mibo.Elmish.ElmishGame`2"/> instance and can be stored
/// in a closure/ref and threaded through Elmish <c>update</c>/<c>view</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// // In init:
/// let texture = Assets.texture "sprites/player" ctx
/// let font = Assets.font "fonts/main" ctx
///
/// // For custom assets:
/// let shader = Assets.getOrCreate "myShader" (fun gd -&gt; new MyEffect(gd)) ctx
/// </code>
/// </example>
type IAssets =
  /// The GraphicsDevice for creating GPU resources.
  abstract GraphicsDevice: GraphicsDevice

  /// The ContentManager for loading compiled XNB assets.
  abstract Content: ContentManager

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Graphics.Texture2D"/> from the content pipeline.</summary>
  abstract Texture: path: string -> Texture2D

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Graphics.SpriteFont"/> from the content pipeline.</summary>
  abstract Font: path: string -> SpriteFont

  /// <summary>Loads and caches a <see cref="T:Microsoft.Xna.Framework.Audio.SoundEffect"/> from the content pipeline.</summary>
  abstract Sound: path: string -> SoundEffect

  /// <summary>Loads and caches a 3D <see cref="T:Microsoft.Xna.Framework.Graphics.Model"/> from the content pipeline.</summary>
  abstract Model: path: string -> Model

  /// <summary>Gets a previously created custom asset by key.</summary>
  abstract Get<'T> : key: string -> 'T voption

  /// <summary>Creates and caches a custom asset using the provided factory.</summary>
  /// <remarks>The factory receives the <see cref="T:Microsoft.Xna.Framework.Graphics.GraphicsDevice"/> for creating GPU resources.</remarks>
  abstract Create<'T> : key: string -> factory: (GraphicsDevice -> 'T) -> 'T

  /// <summary>Gets a cached asset or creates it if not present.</summary>
  /// <remarks>This is the preferred method for custom assets - it's idempotent and ensures assets are created only once.</remarks>
  abstract GetOrCreate<'T> :
    key: string -> factory: (GraphicsDevice -> 'T) -> 'T

  /// Clears all caches (does not dispose ContentManager-managed assets).
  abstract Clear: unit -> unit

  /// Disposes any cached user-created assets implementing `IDisposable` and clears all caches.
  abstract Dispose: unit -> unit

/// Factory for `IAssets` implementations.
module AssetsService =

  let create (content: ContentManager) (gd: GraphicsDevice) : IAssets =
    // Specialized caches for ContentManager-loaded assets
    let textureCache = Dictionary<string, Texture2D>()
    let fontCache = Dictionary<string, SpriteFont>()
    let soundCache = Dictionary<string, SoundEffect>()
    let modelCache = Dictionary<string, Model>()

    // Generic cache for user-created assets
    let assetCache = Dictionary<string, obj>()

    let nonMonoGamePath(path: string) : string =
      if Path.IsPathRooted path then
        path
      else
        Path.Combine(AppContext.BaseDirectory, "Content", path)

    let loadWithCache
      (cache: Dictionary<string, 'T>)
      (path: string)
      (loader: string -> 'T)
      : 'T =

      match cache.TryGetValue(path) with
      | true, v -> v
      | false, _ ->
        let v = loader path
        cache.[path] <- v
        v

    let clear() =
      textureCache.Clear()
      fontCache.Clear()
      soundCache.Clear()
      assetCache.Clear()

    let disposeUserAssets() =
      for KeyValue(_, v) in assetCache do
        match v with
        | :? IDisposable as d ->
          try
            d.Dispose()
          with _ ->
            ()
        | _ -> ()

    { new IAssets with
        member _.GraphicsDevice = gd
        member _.Content = content

        member _.Texture(path: string) : Texture2D =
          loadWithCache textureCache path (fun p -> content.Load<Texture2D>(p))

        member _.Font(path: string) : SpriteFont =
          loadWithCache fontCache path (fun p -> content.Load<SpriteFont>(p))

        member _.Sound(path: string) : SoundEffect =
          loadWithCache soundCache path (fun p -> content.Load<SoundEffect>(p))

        member _.Model(path: string) : Model =
          loadWithCache modelCache path (fun p -> content.Load<Model>(p))

        member _.Get<'T>(key: string) : 'T voption =
          match assetCache.TryGetValue(key) with
          | true, v -> ValueSome(v :?> 'T)
          | false, _ -> ValueNone

        member _.Create<'T> (key: string) (factory: GraphicsDevice -> 'T) : 'T =
          let asset = factory gd
          assetCache.[key] <- box asset
          asset

        member _.GetOrCreate<'T>
          (key: string)
          (factory: GraphicsDevice -> 'T)
          : 'T =
          match assetCache.TryGetValue(key) with
          | true, v -> v :?> 'T
          | false, _ ->
            let asset = factory gd
            assetCache.[key] <- box asset
            asset

        member _.Clear() = clear()

        member _.Dispose() =
          disposeUserAssets()
          clear()
    }

  let createFromContext(ctx: GameContext) : IAssets =
    create ctx.Content ctx.GraphicsDevice

/// <summary>
/// Friendly <see cref="T:Mibo.Elmish.GameContext"/>-based accessors for the built-in <see cref="T:Mibo.Elmish.IAssets"/> service.
/// </summary>
/// <remarks>
/// These functions assume <see cref="M:Mibo.Elmish.Program.withAssets"/> has been applied.
/// </remarks>
module Assets =

  let tryGetService(ctx: GameContext) : IAssets voption =
    let svc = ctx.Game.Services.GetService(typeof<IAssets>)

    match svc with
    | null -> ValueNone
    | :? IAssets as a -> ValueSome a
    | _ -> ValueNone

  let getService(ctx: GameContext) : IAssets =
    match tryGetService ctx with
    | ValueSome a -> a
    | ValueNone ->
      failwith
        "IAssets service not registered. Add Program.withAssets to your program."

  let texture (path: string) (ctx: GameContext) : Texture2D =
    (getService ctx).Texture path

  let model (path: string) (ctx: GameContext) : Model =
    (getService ctx).Model path

  let font (path: string) (ctx: GameContext) : SpriteFont =
    (getService ctx).Font path

  let sound (path: string) (ctx: GameContext) : SoundEffect =
    (getService ctx).Sound path

  let get<'T> (key: string) (ctx: GameContext) : 'T voption =
    (getService ctx).Get<'T> key

  let create<'T>
    (key: string)
    (factory: GraphicsDevice -> 'T)
    (ctx: GameContext)
    : 'T =
    (getService ctx).Create<'T> key factory

  let getOrCreate<'T>
    (key: string)
    (factory: GraphicsDevice -> 'T)
    (ctx: GameContext)
    : 'T =
    (getService ctx).GetOrCreate<'T> key factory

  /// <summary>
  /// Load a JSON file and decode it using the provided JDeck decoder.
  /// </summary>
  /// <remarks> Reads from the Content directory (uses <see cref="T:Microsoft.Xna.Framework.TitleContainer"/>).</remarks>
  let fromJson<'T> (path: string) (decoder: JDeck.Decoder<'T>) =
    try
      let json = File.ReadAllText path

      match JDeck.Decoding.fromString(json, decoder) with
      | Ok value -> value
      | Error e -> failwith e.message
    with ex ->
      failwithf "Failed to load %s: %s" path ex.Message

  /// <summary>Load a JSON file with caching (game-lifetime).</summary>
  /// <remarks>On first call, decodes and caches; subsequent calls return cached value.</remarks>
  let fromJsonCache<'T>
    (path: string)
    (decoder: JDeck.Decoder<'T>)
    (ctx: GameContext)
    =
    let assets = getService ctx

    match assets.Get path with
    | ValueSome cached -> cached
    | ValueNone ->
      let value = fromJson path decoder
      assets.Create path (fun _ -> value)

  /// <summary>Load an asset using a custom loader function.</summary>
  /// <remarks>The loader receives the file path and should return the loaded asset.</remarks>
  let fromCustom<'T>
    (path: string)
    (loader: string -> 'T)
    (_ctx: GameContext)
    : 'T =
    loader path

  /// <summary>Load an asset using a custom loader with caching (game-lifetime).</summary>
  let fromCustomCache<'T>
    (path: string)
    (loader: string -> 'T)
    (ctx: GameContext)
    : 'T =
    (getService ctx).GetOrCreate<'T> path (fun _ -> loader path)
