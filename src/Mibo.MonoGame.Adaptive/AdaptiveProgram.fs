namespace Mibo.Adaptive

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// MonoGame-specific AdaptiveProgram wrapper.
//
// Mirrors MonoGameProgram (the MVU wrapper): wraps an AdaptiveProgram and adds
// a slot for device-level configuration callbacks (Game * GraphicsDeviceManager
// -> unit) that the backend-neutral Core type cannot hold. The host
// (AdaptiveMonoGameGame) runs these callbacks in its constructor, after the Core
// GameConfig but before Initialize, so settings like GraphicsProfile, vsync, and
// fullscreen take effect at device-creation time.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MonoGame-specific wrapper around an
/// <see cref="T:Mibo.Adaptive.AdaptiveProgram`1"/>, carrying device-level
/// configuration that the backend-neutral type cannot hold.
/// </summary>
/// <remarks>
/// Build one with <see cref="M:Mibo.Adaptive.AdaptiveMonoGameProgram.ofProgram"/>,
/// add MonoGame-specific configuration with
/// <see cref="M:Mibo.Adaptive.AdaptiveMonoGameProgram.withConfig"/>, then pass
/// the result to <see cref="T:Mibo.Adaptive.AdaptiveMonoGameGame`1"/>.
/// </remarks>
/// <example>
/// <code>
/// let program =
///   AdaptiveProgram.mkProgram init update
///   |&gt; AdaptiveProgram.withConfig(fun cfg -&gt;
///       { cfg with Width = 1280; Height = 720; Title = "My Game" })
///
/// let mgProgram =
///   program
///   |&gt; AdaptiveMonoGameProgram.ofProgram
///   |&gt; AdaptiveMonoGameProgram.withConfig(fun (game, graphics) -&gt;
///       graphics.SynchronizeWithVerticalRetrace &lt;- false)
///
/// use game = new AdaptiveMonoGameGame&lt;_&gt;(mgProgram)
/// game.Run()
/// </code>
/// </example>
type AdaptiveMonoGameProgram<'Frame> = {
  /// <summary>The backend-neutral adaptive program.</summary>
  Program: AdaptiveProgram<'Frame>

  /// <summary>
  /// Device-level configuration callbacks invoked in the
  /// <see cref="T:Mibo.Adaptive.AdaptiveMonoGameGame`1"/> constructor, after the
  /// Core <see cref="F:Mibo.Adaptive.AdaptiveProgram`1.Config"/> callbacks and
  /// before <c>Initialize</c>/<c>GraphicsDevice</c> creation.
  /// </summary>
  DeviceConfig: (Game * GraphicsDeviceManager -> unit) list
}

/// <summary>MonoGame-specific adaptive program builder extensions.</summary>
module AdaptiveMonoGameProgram =

  /// <summary>
  /// Wraps an <see cref="T:Mibo.Adaptive.AdaptiveProgram`1"/> into an
  /// <see cref="T:Mibo.Adaptive.AdaptiveMonoGameProgram`1"/> with no device-level
  /// configuration.
  /// </summary>
  let ofProgram
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveMonoGameProgram<'Frame> =
    { Program = program; DeviceConfig = [] }

  /// <summary>
  /// Adds a device-level configuration callback that receives the <c>Game</c>
  /// instance and <c>GraphicsDeviceManager</c> for low-level setup.
  /// </summary>
  /// <remarks>
  /// <para>Invoked in the
  /// <see cref="T:Mibo.Adaptive.AdaptiveMonoGameGame`1"/> constructor, after the
  /// Core <see cref="M:Mibo.Adaptive.AdaptiveProgram.withConfig"/> callbacks and
  /// before <c>Initialize</c> / <c>GraphicsDevice</c> creation — so settings
  /// like <c>GraphicsProfile</c> and <c>PreferredBackBufferWidth/Height</c>
  /// take effect.</para>
  /// <para>Use this for properties that require direct
  /// <c>GraphicsDeviceManager</c>/<c>Game</c> access:
  /// <c>GraphicsProfile</c>, <c>SynchronizeWithVerticalRetrace</c> (vsync),
  /// <c>IsFullScreen</c>, <c>HardwareModeSwitch</c>,
  /// <c>Window.AllowUserResizing</c>, <c>Content.RootDirectory</c>, etc.</para>
  /// </remarks>
  let withConfig
    (configure: Game * GraphicsDeviceManager -> unit)
    (program: AdaptiveMonoGameProgram<'Frame>)
    : AdaptiveMonoGameProgram<'Frame> =
    {
      program with
          DeviceConfig = configure :: program.DeviceConfig
    }
