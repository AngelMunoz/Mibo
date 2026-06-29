namespace Mibo.Elmish

open System
open System.Collections.Generic
open System.IO
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open Assimp
open Mibo.Animation

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
//
// Animation note: ModelAnimations and AnimatedMesh load the raw model file
// (.glb/.gltf/.fbx/etc.) via AssimpNetter at runtime — the content pipeline
// does not preserve animation data in XNB. The caller MUST ensure the raw
// model file is included in the output directory (e.g. via
// <CopyToOutputDirectory> or <Content> items in the .fsproj). The path is a
// filesystem path, not a content-pipeline XNB path.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-game asset loader/cache service for the MonoGame backend.
/// </summary>
/// <remarks>
/// Provides cached loading for textures, fonts, sounds, models, effects,
/// and 3D animation clips via the MonoGame content pipeline. Extends
/// <see cref="T:Mibo.Elmish.IAssetCache"/> so portable code can cache custom
/// assets without referencing a backend.
/// </remarks>
/// <example>
/// <code>
/// let assets = GameContext.getService&lt;IAssets&gt; ctx
/// let tex = assets.Texture "sprites/player"
/// let font = assets.Font "fonts/main"
/// let clips = assets.ModelAnimations "assets/character.glb"
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
  /// Loads and caches 3D animation clips from a raw model file
  /// (.glb/.gltf/.fbx/.dae/.blend/etc.) via Assimp at runtime.
  /// </summary>
  /// <remarks>
  /// The MonoGame content pipeline does not preserve animation data in XNB,
  /// so clips are loaded directly from the raw model file via AssimpNetter.
  /// The <paramref name="path"/> is a filesystem path (relative to the
  /// <c>ContentManager.RootDirectory</c> or the working directory), NOT a
  /// content-pipeline XNB path. The caller MUST ensure the raw model file is
  /// copied to the output directory (e.g. <c>&lt;CopyToOutputDirectory&gt;</c>
  /// in the .fsproj). Returns an empty clip set if the file has no animations.
  /// </remarks>
  abstract ModelAnimations: path: string -> Animation3DClips

  /// <summary>
  /// Loads and caches skeleton data (bone names + inverse-bind matrices) from
  /// a raw model file via Assimp at runtime.
  /// </summary>
  /// <remarks>
  /// Used for GPU skinning via <c>Draw3D.drawSkinnedMesh</c>. Returns
  /// <c>ValueNone</c> if the model has no bones. Same path rules as
  /// <see cref="M:Mibo.Elmish.IAssets.ModelAnimations"/>.
  /// </remarks>
  abstract AnimatedMesh: path: string -> AnimatedMesh voption

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
  let modelAnimations = Dictionary<string, Animation3DClips>()
  let animatedMeshes = Dictionary<string, AnimatedMesh voption>()
  // Shared Assimp Scene cache: parsed once per path, reused by both
  // ModelAnimations and AnimatedMesh so the file isn't imported twice.
  // Both derive fully-owned copies (keyframe/bone arrays), so a cached
  // Scene holds no shared mutable state with its consumers.
  let scenes = Dictionary<string, Scene>()

  /// <summary>The <c>ContentManager</c> this service loads from.</summary>
  member _.Content = content

  /// <summary>
  /// Resolves a raw-file path for Assimp loading. The content pipeline uses
  /// XNB paths (no extension); raw model files keep their extension.
  /// Resolution order: as-given → relative to ContentManager.RootDirectory.
  /// </summary>
  member private _.resolveRawPath(path: string) =
    if Path.IsPathFullyQualified path then
      path
    else
      Path.Combine(content.RootDirectory, path)

  /// <summary>
  /// Loads an Assimp <c>Scene</c> from a raw model file, cached by path. The
  /// scene is parsed once and reused for both clip extraction
  /// (<c>ModelAnimations</c>) and skeleton extraction (<c>AnimatedMesh</c>).
  /// </summary>
  member private this.loadScene(path: string) : Scene =
    match scenes.TryGetValue(path) with
    | true, scene -> scene
    | _ ->
      let resolved = this.resolveRawPath path

      use importer = new AssimpContext()

      let scene =
        importer.ImportFile(
          resolved,
          PostProcessSteps.FindDegenerates
          ||| PostProcessSteps.FindInvalidData
          ||| PostProcessSteps.FlipUVs
          ||| PostProcessSteps.FlipWindingOrder
          ||| PostProcessSteps.JoinIdenticalVertices
          ||| PostProcessSteps.ImproveCacheLocality
          ||| PostProcessSteps.OptimizeMeshes
          ||| PostProcessSteps.Triangulate
        )

      scenes.Add(path, scene)
      scene

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

    member this.ModelAnimations(path) =
      match modelAnimations.TryGetValue(path) with
      | true, clips -> clips
      | _ ->
        let scene = this.loadScene path
        let clips = Animation3DClips.fromScene scene
        modelAnimations.Add(path, clips)
        clips

    member this.AnimatedMesh(path) =
      match animatedMeshes.TryGetValue(path) with
      | true, mesh -> mesh
      | _ ->
        let scene = this.loadScene path
        let mesh = AnimatedMesh.fromScene scene
        animatedMeshes.Add(path, mesh)
        mesh

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
      modelAnimations.Clear()
      animatedMeshes.Clear()
      scenes.Clear()

    member _.Dispose() =
      // Dispose user-created IDisposable assets. ContentManager owns the
      // XNB-loaded textures/fonts/etc., so the typed-loader caches below are
      // left for ContentManager.Unload — only the generic typedCache is ours.
      for kvp in typedCache do
        match kvp.Value with
        | :? IDisposable as d -> d.Dispose()
        | _ -> ()

      typedCache.Clear()
      textures.Clear()
      fonts.Clear()
      sounds.Clear()
      models.Clear()
      effects.Clear()
      modelAnimations.Clear()
      animatedMeshes.Clear()
      scenes.Clear()

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
