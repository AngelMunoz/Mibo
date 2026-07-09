#nowarn "9"

namespace Mibo.Elmish

open System
open System.Collections.Generic
open System.IO
open FSharp.NativeInterop
open Raylib_cs

/// <summary>
/// Per-game asset loader/cache service for the raylib backend.
/// </summary>
/// <remarks>
/// Provides cached loading for textures, fonts, sounds, models, and shaders
/// from loose files (no content pipeline). Extends <see cref="T:Mibo.Elmish.IAssetCache"/>
/// so portable code can cache custom assets without referencing a backend.
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
  inherit IAssetCache

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

/// <summary>
/// Implementation of <see cref="T:Mibo.Elmish.IAssets"/> with dictionary-based caches.
/// </summary>
/// <param name="baseAssetPath">Optional base path prepended to all relative asset paths.</param>
type AssetsService(baseAssetPath: string voption) =

  let resolvePath(path: string) =
    match baseAssetPath with
    | ValueSome bp -> Path.Combine(bp, path)
    | ValueNone -> path

  // Generate mipmaps + trilinear filtering on a texture. raylib's LoadTexture/
  // LoadModel default to bilinear/point filtering with no mipmaps, which aliases
  // specular highlights flat at perspective angles and makes PBR surfaces look
  // matte compared to a mipmapped backend (e.g. MonoGame/DX11). Applying this on
  // load gives model/texture surfaces clean minification and restores correct
  // specular response. (raylib-cs 8.0: GenTextureMipmaps takes the texture byref
  // and writes the updated mipmap count back into the same struct.)
  let applyMipmapFilter(tex: Texture2D) =
    let mutable t = tex
    Raylib.GenTextureMipmaps(&t)
    Raylib.SetTextureFilter(t, TextureFilter.Trilinear)
    t

  let typedCache = Dictionary<string, obj>()

  let textures = Dictionary<string, Texture2D>()
  let fonts = Dictionary<string, Font>()
  let sounds = Dictionary<string, Sound>()
  let models = Dictionary<string, Model>()
  let modelAnimations = Dictionary<string, ModelAnimation[]>()

  // raylib allocates the ModelAnimation array (and each keyframePoses) via
  // RL_MALLOC; UnloadModelAnimations frees the array contents AND the array
  // pointer itself. We copy structs into the managed array above for indexing
  // but must free through the original native pointer — freeing a pinned
  // managed array crashes ("pointer being freed was not allocated").
  let modelAnimationBuffers = Dictionary<string, struct (nativeint * int)>()

  member _.BasePath = baseAssetPath

  interface IAssets with
    member _.Texture(path) =
      let resolved = resolvePath path

      match textures.TryGetValue(resolved) with
      | true, tex -> tex
      | _ ->
        let tex = applyMipmapFilter(Raylib.LoadTexture(resolved))
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

        // Apply mipmaps + trilinear filter to every material map texture on the
        // loaded model (see applyMipmapFilter). Material.Maps is a MaterialMap*
        // into the material's fixed buffer; read/write via NativePtr because
        // indexing yields a copy (struct by value).
        for mi = 0 to m.MaterialCount - 1 do
          let mat = NativePtr.get m.Materials mi

          for mapIdx = 0 to int MaterialMapIndex.Brdf do
            let mutable map = NativePtr.get mat.Maps mapIdx

            if map.Texture.Id <> 0u then
              map.Texture <- applyMipmapFilter(map.Texture)
              NativePtr.set mat.Maps mapIdx map

        models.Add(resolved, m)
        m

    member _.ModelAnimations(path) =
      let resolved = resolvePath path

      match modelAnimations.TryGetValue(resolved) with
      | true, anims -> anims
      | _ ->
        let mutable count = 0
        let nativeAnims = Raylib.LoadModelAnimations(resolved, &count)
        let anims = Array.zeroCreate<ModelAnimation> count

        for i = 0 to count - 1 do
          anims[i] <- NativePtr.get nativeAnims i

        modelAnimations.Add(resolved, anims)

        modelAnimationBuffers.Add(
          resolved,
          struct (NativePtr.toNativeInt nativeAnims, count)
        )

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
      modelAnimationBuffers.Clear()

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

      for KeyValue(_, struct (ptr, count)) in modelAnimationBuffers do
        let nativeAnims: nativeptr<ModelAnimation> = NativePtr.ofNativeInt ptr
        Raylib.UnloadModelAnimations(nativeAnims, count)

      modelAnimationBuffers.Clear()
      modelAnimations.Clear()

      typedCache.Clear()

/// Factory for <see cref="T:Mibo.Elmish.IAssets"/> implementations.
module AssetsService =
  /// <summary>Creates an asset service with no base path.</summary>
  let create() : IAssets = new AssetsService(ValueNone) :> IAssets

  /// <summary>Creates an asset service where all relative paths are prepended with the given base path.</summary>
  let createWithBasePath(basePath: string) : IAssets =
    new AssetsService(ValueSome basePath) :> IAssets
