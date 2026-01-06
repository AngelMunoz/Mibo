namespace Mibo.Elmish

open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Content

module AssetsInternal =
  let mutable private contentManager: ContentManager = null
  let mutable private graphicsDevice: GraphicsDevice = null

  // Specialized caches for ContentManager-loaded assets
  let private textureCache = Dictionary<string, Texture2D>()
  let private fontCache = Dictionary<string, SpriteFont>()
  let private soundCache = Dictionary<string, SoundEffect>()

  // Generic cache for user-created assets
  let private assetCache = Dictionary<string, obj>()

  let initialize (content: ContentManager) (gd: GraphicsDevice) =
    contentManager <- content
    graphicsDevice <- gd

  let getGraphicsDevice() =
    if isNull graphicsDevice then
      failwith "Assets not initialized. Add Program.withAssets to your program."

    graphicsDevice

  let loadTexture(path: string) : Texture2D =
    if isNull contentManager then
      failwith "Assets not initialized. Add Program.withAssets to your program."

    match textureCache.TryGetValue(path) with
    | true, tex -> tex
    | false, _ ->
      let tex = contentManager.Load<Texture2D>(path)
      textureCache.[path] <- tex
      tex

  let loadFont(path: string) : SpriteFont =
    if isNull contentManager then
      failwith "Assets not initialized. Add Program.withAssets to your program."

    match fontCache.TryGetValue(path) with
    | true, font -> font
    | false, _ ->
      let font = contentManager.Load<SpriteFont>(path)
      fontCache.[path] <- font
      font

  let loadSound(path: string) : SoundEffect =
    if isNull contentManager then
      failwith "Assets not initialized. Add Program.withAssets to your program."

    match soundCache.TryGetValue(path) with
    | true, sound -> sound
    | false, _ ->
      let sound = contentManager.Load<SoundEffect>(path)
      soundCache.[path] <- sound
      sound

  let get<'T>(key: string) : 'T voption =
    match assetCache.TryGetValue(key) with
    | true, v -> ValueSome(v :?> 'T)
    | false, _ -> ValueNone

  let create<'T> (key: string) (factory: GraphicsDevice -> 'T) : 'T =
    let gd = getGraphicsDevice()
    let asset = factory gd
    assetCache.[key] <- box asset
    asset

  let getOrCreate<'T> (key: string) (factory: GraphicsDevice -> 'T) : 'T =
    match assetCache.TryGetValue(key) with
    | true, v -> v :?> 'T
    | false, _ ->
      let gd = getGraphicsDevice()
      let asset = factory gd
      assetCache.[key] <- box asset
      asset

module Assets =
  let texture(path: string) : Texture2D = AssetsInternal.loadTexture path
  let font(path: string) : SpriteFont = AssetsInternal.loadFont path
  let sound(path: string) : SoundEffect = AssetsInternal.loadSound path

  let get<'T>(key: string) : 'T voption = AssetsInternal.get<'T> key

  let create<'T> (key: string) (factory: GraphicsDevice -> 'T) : 'T =
    AssetsInternal.create<'T> key factory

  let getOrCreate<'T> (key: string) (factory: GraphicsDevice -> 'T) : 'T =
    AssetsInternal.getOrCreate<'T> key factory
