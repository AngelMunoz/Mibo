namespace Mibo.Elmish

open System.Collections.Generic
open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Content

/// Per-game asset loader/cache service.
///
/// Unlike the legacy `Assets` module API (which relies on module-level mutable state),
/// values of `IAssets` are safe to create per `ElmishGame` instance and can be stored
/// in a closure/ref and threaded through Elmish `update`/`view`.
type IAssets =
  abstract GraphicsDevice: GraphicsDevice
  abstract Content: ContentManager

  abstract Texture: path: string -> Texture2D
  abstract Font: path: string -> SpriteFont
  abstract Sound: path: string -> SoundEffect

  abstract Get<'T> : key: string -> 'T voption
  abstract Create<'T> : key: string -> factory: (GraphicsDevice -> 'T) -> 'T

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

    // Generic cache for user-created assets
    let assetCache = Dictionary<string, obj>()

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

/// Friendly `GameContext`-based accessors for the built-in `IAssets` service.
///
/// These functions assume `Program.withAssets` has been applied.
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
