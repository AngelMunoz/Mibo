namespace Mibo.Elmish

open Mibo.Input

// ─────────────────────────────────────────────────────────────────────────────
// Raylib-specific Program builder extensions.
//
// The backend-neutral Program builder (mkProgram, withConfig, withRenderer,
// withTick, withFixedStep, withDispatchMode, withSubscription, withAssets,
// withAssetsBasePath, withInput, withServiceRegistration) lives in Mibo.Core.
//
// This module holds the only backend-coupled builder: withInputMapper, which
// instantiates the raylib IInputMapper implementation. It registers the service
// via a ServiceRegistration callback that the runtime host runs before Init,
// so the Core Program type never references a backend factory.
//
// It lives in its own module (not as `Program.withInputMapper`) because the
// factory is raylib-specific: each backend exposes its own withInputMapper that
// supplies its native IInputMapper implementation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Raylib-specific <see cref="T:Mibo.Elmish.Program"/> builder extensions.</summary>
module RaylibProgram =

  /// <summary>
  /// Configures the game to register an <see cref="T:Mibo.Input.IInputMapper`1"/> service
  /// backed by raylib's polling API.
  /// </summary>
  /// <remarks>
  /// <para>This registers <see cref="T:Mibo.Input.IInput"/> automatically (equivalent to <see cref="M:Mibo.Elmish.Program.withInput"/>).</para>
  /// <para>The mapper is registered as a service via a <see cref="F:Mibo.Elmish.Program.ServiceRegistrations"/>
  /// callback that the runtime host runs before <c>Init</c>, so the Core Program
  /// type does not reference the raylib factory directly.</para>
  /// <para>If you want to stay fully "Elmish" (no service access), consider using
  /// <see cref="M:Mibo.Input.InputMapper.subscribe"/> instead and handle a single message.</para>
  /// </remarks>
  /// <example>
  /// <code>
  /// program |&gt; RaylibProgram.withInputMapper inputMap
  /// </code>
  /// </example>
  let withInputMapper<'Model, 'Msg, 'Action when 'Action: comparison>
    (initialMap: InputMap<'Action>)
    (program: Program<'Model, 'Msg>)
    : Program<'Model, 'Msg> =
    let program = program |> Program.withInput

    // Register the raylib-backed IInputMapper service before Init runs.
    // The host invokes ServiceRegistrations after IInput is available.
    let withRegistration =
      program
      |> Program.withServiceRegistration(fun ctx ->
        let mapper = InputMapper.createService initialMap
        GameContext.register<IInputMapper<'Action>> mapper ctx)

    { withRegistration with HasInputMapper = true }
