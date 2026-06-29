namespace Mibo.Elmish

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Input

// ─────────────────────────────────────────────────────────────────────────────
// MonoGame-specific Program wrapper and builder extensions.
//
// The backend-neutral Program builder (mkProgram, withConfig, withRenderer,
// withTick, withFixedStep, withDispatchMode, withSubscription, withAssets,
// withAssetsBasePath, withInput, withServiceRegistration) lives in Mibo.Core.
//
// MonoGameProgram wraps a Core Program and adds a slot for device-level
// configuration callbacks (Game * GraphicsDeviceManager -> unit) that the
// Core type cannot hold without leaking MonoGame types. The host (MiboGame)
// runs these callbacks in its constructor, after the Core GameConfig but
// before Initialize, so settings like GraphicsProfile, vsync, and fullscreen
// take effect at device-creation time.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MonoGame-specific wrapper around a Core <see cref="T:Mibo.Elmish.Program`2"/>,
/// carrying device-level configuration that the backend-neutral Core type cannot hold.
/// </summary>
/// <remarks>
/// Build one with <see cref="M:Mibo.Elmish.MonoGameProgram.ofProgram"/>, add
/// MonoGame-specific configuration with
/// <see cref="M:Mibo.Elmish.MonoGameProgram.withConfig"/> and/or
/// <see cref="M:Mibo.Elmish.MonoGameProgram.withInputMapper"/>, then pass the result
/// to <see cref="T:Mibo.Elmish.MiboGame`2"/>.
/// </remarks>
/// <example>
/// <code>
/// let program =
///   Program.mkProgram init update
///   |&gt; Program.withConfig(fun cfg -&gt;
///       { cfg with Width = 1280; Height = 720; Title = "My Game" })
///   |&gt; Program.withInput
///   |&gt; Program.withTick Tick
///
/// let mgProgram =
///   program
///   |&gt; MonoGameProgram.ofProgram
///   |&gt; MonoGameProgram.withConfig(fun (game, graphics) -&gt;
///       graphics.SynchronizeWithVerticalRetrace &lt;- false)
///
/// use game = new MiboGame&lt;_, _&gt;(mgProgram)
/// game.Run()
/// </code>
/// </example>
type MonoGameProgram<'Model, 'Msg> = {
  /// <summary>The backend-neutral Core program.</summary>
  Program: Program<'Model, 'Msg>

  /// <summary>
  /// Device-level configuration callbacks invoked in the
  /// <see cref="T:Mibo.Elmish.MiboGame`2"/> constructor, after the Core
  /// <see cref="F:Mibo.Elmish.Program.Config"/> callbacks and before
  /// <c>Initialize</c>/<c>GraphicsDevice</c> creation.
  /// </summary>
  DeviceConfig: (Game * GraphicsDeviceManager -> unit) list
}

/// <summary>MonoGame-specific program builder extensions.</summary>
module MonoGameProgram =

  /// <summary>
  /// Wraps a Core <see cref="T:Mibo.Elmish.Program`2"/> into a
  /// <see cref="T:Mibo.Elmish.MonoGameProgram`2"/> with no device-level
  /// configuration.
  /// </summary>
  let ofProgram
    (program: Program<'Model, 'Msg>)
    : MonoGameProgram<'Model, 'Msg> =
    { Program = program; DeviceConfig = [] }

  /// <summary>
  /// Adds a device-level configuration callback that receives the <c>Game</c>
  /// instance and <c>GraphicsDeviceManager</c> for low-level setup.
  /// </summary>
  /// <remarks>
  /// <para>Invoked in the <see cref="T:Mibo.Elmish.MiboGame`2"/> constructor,
  /// after the Core <see cref="M:Mibo.Elmish.Program.withConfig"/> callbacks
  /// and before <c>Initialize</c> / <c>GraphicsDevice</c> creation — so
  /// settings like <c>GraphicsProfile</c> and
  /// <c>PreferredBackBufferWidth/Height</c> take effect.</para>
  /// <para>Use this for properties that require direct
  /// <c>GraphicsDeviceManager</c>/<c>Game</c> access:
  /// <c>GraphicsProfile</c>, <c>SynchronizeWithVerticalRetrace</c> (vsync),
  /// <c>IsFullScreen</c>, <c>HardwareModeSwitch</c>,
  /// <c>Window.AllowUserResizing</c>, <c>Content.RootDirectory</c>, etc.</para>
  /// </remarks>
  /// <example>
  /// <code>
  /// program
  /// |&gt; MonoGameProgram.ofProgram
  /// |&gt; MonoGameProgram.withConfig(fun (game, graphics) -&gt;
  ///     graphics.GraphicsProfile &lt;- GraphicsProfile.HiDef
  ///     graphics.SynchronizeWithVerticalRetrace &lt;- false
  ///     game.IsMouseVisible &lt;- true)
  /// </code>
  /// </example>
  let withConfig
    (configure: Game * GraphicsDeviceManager -> unit)
    (program: MonoGameProgram<'Model, 'Msg>)
    : MonoGameProgram<'Model, 'Msg> =
    {
      program with
          DeviceConfig = configure :: program.DeviceConfig
    }

  /// <summary>
  /// Configures the game to register an <see cref="T:Mibo.Input.IInputMapper`1"/> service
  /// backed by MonoGame's polling API.
  /// </summary>
  /// <remarks>
  /// <para>This registers <see cref="T:Mibo.Input.IInput"/> automatically (equivalent to
  /// <see cref="M:Mibo.Elmish.Program.withInput"/>).</para>
  /// <para>The mapper is registered as a service via a
  /// <see cref="F:Mibo.Elmish.Program.ServiceRegistrations"/> callback that
  /// <c>MiboGame</c> runs before <c>Init</c>, so the Core Program type never
  /// references a backend factory.</para>
  /// <para>If you want to stay fully "Elmish" (no service access), consider using
  /// <see cref="M:Mibo.Input.InputMapper.subscribe"/> instead and handle a single message.</para>
  /// </remarks>
  /// <example>
  /// <code>
  /// program
  /// |&gt; MonoGameProgram.ofProgram
  /// |&gt; MonoGameProgram.withInputMapper inputMap
  /// </code>
  /// </example>
  let withInputMapper<'Model, 'Msg, 'Action when 'Action: comparison>
    (initialMap: InputMap<'Action>)
    (program: MonoGameProgram<'Model, 'Msg>)
    : MonoGameProgram<'Model, 'Msg> =
    let coreProgram =
      program.Program
      |> Program.withInput
      |> Program.withServiceRegistration(fun ctx ->
        let mapper = InputMapper.createService initialMap
        GameContext.register<IInputMapper<'Action>> mapper ctx)

    {
      program with
          Program = {
            coreProgram with
                HasInputMapper = true
          }
    }
