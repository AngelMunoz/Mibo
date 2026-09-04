namespace Mibo.Elmish

open Mibo.Diagnostics

// GameConfig and its module live in the Mibo.Core kernel (GameConfig.fs); the
// program record below is MVU machinery and stays in Mibo.Mvu.

/// <summary>
/// The Elmish program record that defines the complete game architecture.
/// </summary>
/// <remarks>
/// A program ties together initialization, update logic, subscriptions, and rendering.
/// Use the <see cref="T:Mibo.Elmish.Program"/> module functions to construct and configure programs.
/// </remarks>
type Program<'Model, 'Msg> = {
  /// <summary>Creates initial model and commands when the game starts.</summary>
  Init: GameContext -> struct ('Model * Cmd<'Msg>)
  /// <summary>Handles messages and returns updated model and commands.</summary>
  Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
  /// <summary>
  /// Optional context-aware update. When set, the runtime calls this instead of
  /// <see cref="F:Mibo.Elmish.Program`2.Update"/>, passing the same
  /// <see cref="T:Mibo.Elmish.GameContext"/> that <c>Init</c>, <c>Subscribe</c>,
  /// and the renderer callbacks already receive.
  /// </summary>
  /// <remarks>Set via <see cref="M:Mibo.Elmish.Program.mkProgramCtx"/> or
  /// <see cref="M:Mibo.Elmish.Program.withUpdateCtx"/>.</remarks>
  UpdateCtx:
    (GameContext -> 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)) voption
  /// <summary>Returns subscriptions based on current model state.</summary>
  Subscribe: GameContext -> 'Model -> Sub<'Msg>
  /// <summary>
  /// List of configuration callbacks that transform the default GameConfig.
  /// </summary>
  /// <remarks>Each callback receives current config and returns a modified copy.</remarks>
  Config: (GameConfig -> GameConfig) list
  /// <summary>List of renderer factories for drawing.</summary>
  Renderers: (unit -> IRenderer<'Model>) list
  /// <summary>Optional function to generate a message each frame.</summary>
  Tick: (GameTime -> 'Msg) voption
  /// <summary>
  /// Optional framework-managed fixed timestep configuration.
  /// </summary>
  FixedStep: FixedStepConfig<'Msg> voption
  /// <summary>
  /// Controls when dispatched messages become eligible for processing.
  /// </summary>
  /// <remarks>
  /// See <see cref="T:Mibo.Elmish.DispatchMode"/>.
  /// </remarks>
  DispatchMode: DispatchMode
  /// <summary>Optional base path for asset loading. Set via <see cref="M:Mibo.Elmish.Program.withAssetsBasePath"/>.</summary>
  AssetsBasePath: string voption
  /// <summary>Whether the input service is enabled. Set via <see cref="M:Mibo.Elmish.Program.withInput"/>.</summary>
  HasInput: bool
  /// <summary>Whether an input mapper service is enabled. Set via a backend-specific <c>withInputMapper</c> function (e.g. <c>RaylibProgram.withInputMapper</c>).</summary>
  HasInputMapper: bool
  /// <summary>
  /// Service-registration callbacks invoked by the runtime host after core services
  /// (assets, input) are registered but before <see cref="F:Mibo.Elmish.Program.Init"/>.
  /// </summary>
  /// <remarks>
  /// Used by backend-specific builder functions (e.g. <c>withInputMapper</c>)
  /// to register backend-specific services without the Core Program builder
  /// referencing a backend factory directly.
  /// </remarks>
  ServiceRegistrations: (GameContext -> unit) list
  /// <summary>Optional frame profiler. Set via <see cref="M:Mibo.Elmish.Program.withProfiler"/>.</summary>
  /// <remarks>When unset, the host measures nothing.</remarks>
  Profiler: FrameProfiler voption
}
