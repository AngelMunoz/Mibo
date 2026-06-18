namespace Mibo.Elmish.Graphics2D

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open Microsoft.Xna.Framework.Graphics
open MonoGame.Framework.Utilities

/// <summary>
/// Loads compiled MonoGame effect (.mgfx) files from embedded resources.
/// </summary>
/// <remarks>
/// <para>
/// The Mibo.MonoGame.fsproj embeds four compiled shader variants:
/// <c>LitSprite.dx.mgfx</c>, <c>LitSprite.ogl.mgfx</c>,
/// <c>LitSpriteNormalMap.dx.mgfx</c>, <c>LitSpriteNormalMap.ogl.mgfx</c>.
/// </para>
/// <para>
/// Platform detection uses <c>MonoGame.Framework.Utilities.PlatformInfo.MonoGamePlatform</c>
/// to pick the DirectX (<c>.dx.mgfx</c>) or OpenGL (<c>.ogl.mgfx</c>) variant.
/// </para>
/// <para>
/// This loader is not yet consumed by the renderer; it exists so the
/// lighting phase can call <c>ShaderLoader.loadEffect gd "LitSprite"</c>
/// without managing the platform-switching boilerplate.
/// </para>
/// </remarks>
module ShaderLoader =

  let private assembly = Assembly.GetExecutingAssembly()
  let private cache = Dictionary<string, Effect>()

  let private backendSuffix() =
    match PlatformInfo.GraphicsBackend with
    | GraphicsBackend.DirectX
    | GraphicsBackend.DirectX12 -> ".dx.mgfx"
    | GraphicsBackend.OpenGL -> ".ogl.mgfx"
    | GraphicsBackend.Vulkan
    | GraphicsBackend.Metal
    | _ -> failwith "Vulkan, Metal and others are not supported at this time."

  let private tryReadResource(name: string, suffix: string) : byte[] voption =
    let fullName = sprintf "Mibo.MonoGame.Shaders.%s%s" name suffix
    use stream = assembly.GetManifestResourceStream(fullName)

    match stream with
    | null -> ValueNone
    | s ->
      use ms = new MemoryStream()
      s.CopyTo(ms)
      ValueSome(ms.ToArray())

  /// <summary>
  /// Loads a compiled MonoGame effect from an embedded .mgfx resource.
  /// </summary>
  /// <param name="gd">The graphics device to create the effect on.</param>
  /// <param name="name">Base name of the effect (e.g. <c>"LitSprite"</c> or <c>"LitSpriteNormalMap"</c>).</param>
  /// <returns>The <see cref="Effect"/>, or <c>ValueNone</c> if the resource is missing.</returns>
  let loadEffect (gd: GraphicsDevice) (name: string) : Effect voption =
    let suffix = backendSuffix()
    let key = name + suffix

    match cache.TryGetValue(key) with
    | true, cached -> ValueSome cached
    | false, _ ->
      match tryReadResource(name, suffix) with
      | ValueNone -> ValueNone
      | ValueSome bytes ->
        let effect = new Effect(gd, bytes)
        cache[key] <- effect
        ValueSome effect
