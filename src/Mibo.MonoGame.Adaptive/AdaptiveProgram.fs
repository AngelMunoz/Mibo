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
  /// One entry of a sound bank: a game-vocabulary key bound to the source
  /// that plays under it. The whole bank loads before the program's
  /// <c>Init</c> runs.
  /// </summary>
  type BankEntry =
    /// <summary>A sound effect: plays with <c>Intents.play</c>, overlaps itself through an 8-slot pool.</summary>
    | Sound of key: string * Source
    /// <summary>A music track for the single music channel.</summary>
    | Music of key: string * Source

  /// <summary>
  /// Configures the game's sound bank: every entry loads before the program's
  /// <c>Init</c> runs, so <c>Intents.play</c>/<c>Intents.playMusic</c> can
  /// start sounds from the first step.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Each entry names its own <see cref="T:Mibo.Elmish.Source"/> —
  /// <c>Pipeline</c> for MGCB assets, <c>File</c> for loose files (WAV sound
  /// effects; music goes through the platform decoders). List order is load
  /// order; a key registered twice keeps the first entry.
  /// </para>
  /// <para>
  /// The adaptive counterpart of
  /// <see cref="M:Mibo.Elmish.MonoGameProgram.withBank"/>: same entries, same
  /// load slot (service registrations run before <c>Init</c>).
  /// </para>
  /// </remarks>
  /// <example>
  /// <code>
  /// let mgProgram =
  ///   AdaptiveProgram.mkProgram init update
  ///   |&gt; AdaptiveMonoGameProgram.ofProgram
  ///   |&gt; AdaptiveMonoGameProgram.withBank
  ///     [ Sound("jump", Pipeline "audio/jump")
  ///       Music("overworld", File "music/overworld.ogg") ]
  /// </code>
  /// </example>
  let withBank
    (bank: BankEntry list)
    (program: AdaptiveMonoGameProgram<'Frame>)
    : AdaptiveMonoGameProgram<'Frame> =
    {
      program with
          Program =
            program.Program
            |> AdaptiveProgram.withServiceRegistration(fun ctx ->
              match GameContext.tryGetService<AudioService> ctx with
              | ValueSome audio ->
                for entry in bank do
                  match entry with
                  | Sound(key, source) -> audio.RegisterSound(key, source)
                  | Music(key, source) -> audio.RegisterMusic(key, source)
              | ValueNone -> ())
    }

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
