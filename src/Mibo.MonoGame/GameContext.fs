namespace Mibo.Elmish

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics

// ─────────────────────────────────────────────────────────────────────────────
// MonoGame handle access via the Core GameContext service registry.
//
// The Core `GameContext` is backend-neutral (window dims + a typed service
// registry) and has no slot for backend-specific handles. Rather than widen the
// Core type, the MonoGame backend follows the same pattern the raylib backend
// uses for `IInput`/`IAssets`: the host registers the long-lived MonoGame
// handles (`GraphicsDevice`, `ContentManager`, `Game`) into the Core registry,
// and user `init`/`update`/`subscribe` code retrieves them via the typed
// `getService` accessors below.
//
// `MiboGame` (the host) calls `MonoGameGameContext.register game this` once,
// after the `GraphicsDevice` exists and before `ElmishLoop.Init`.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Typed accessors for the MonoGame handles registered in a Core
/// <see cref="T:Mibo.Elmish.GameContext"/>.
/// </summary>
/// <remarks>
/// The MonoGame host registers <c>GraphicsDevice</c>, <c>ContentManager</c>, and
/// <c>Game</c> into the Core service registry (mirroring how the raylib backend
/// registers <c>IInput</c>/<c>IAssets</c>). These accessors retrieve them with
/// the same <c>getService</c>/<c>tryGetService</c> semantics as other services.
/// </remarks>
module MonoGameGameContext =

  /// <summary>
  /// Registers the host game's <c>GraphicsDevice</c>, <c>ContentManager</c>, and
  /// the <c>Game</c> itself into the <see cref="T:Mibo.Elmish.GameContext"/>.
  /// </summary>
  /// <remarks>
  /// Called once by <c>MiboGame</c> after the <c>GraphicsDevice</c> is created
  /// and before <see cref="M:Mibo.Elmish.ElmishLoop`2.Init"/>, so user
  /// <c>init</c> code can resolve every MonoGame handle.
  /// </remarks>
  let register (game: Game) (ctx: GameContext) =
    GameContext.register<GraphicsDevice> game.GraphicsDevice ctx
    GameContext.register<ContentManager> game.Content ctx
    GameContext.register<Game> game ctx

  /// <summary>Gets the registered <c>GraphicsDevice</c>.</summary>
  /// <exception cref="T:System.Exception">Thrown if not registered (the host must call <c>register</c> first).</exception>
  let getGraphicsDevice(ctx: GameContext) : GraphicsDevice =
    GameContext.getService<GraphicsDevice> ctx

  /// <summary>Gets the registered <c>ContentManager</c>.</summary>
  /// <exception cref="T:System.Exception">Thrown if not registered (the host must call <c>register</c> first).</exception>
  let getContentManager(ctx: GameContext) : ContentManager =
    GameContext.getService<ContentManager> ctx

  /// <summary>Gets the registered <c>Game</c> instance.</summary>
  /// <exception cref="T:System.Exception">Thrown if not registered (the host must call <c>register</c> first).</exception>
  let getGame(ctx: GameContext) : Game = GameContext.getService<Game> ctx

  /// <summary>Returns the registered <c>GraphicsDevice</c>, or <c>ValueNone</c>.</summary>
  let tryGetGraphicsDevice(ctx: GameContext) : GraphicsDevice voption =
    GameContext.tryGetService<GraphicsDevice> ctx

  /// <summary>Returns the registered <c>ContentManager</c>, or <c>ValueNone</c>.</summary>
  let tryGetContentManager(ctx: GameContext) : ContentManager voption =
    GameContext.tryGetService<ContentManager> ctx

  /// <summary>Returns the registered <c>Game</c> instance, or <c>ValueNone</c>.</summary>
  let tryGetGame(ctx: GameContext) : Game voption =
    GameContext.tryGetService<Game> ctx
