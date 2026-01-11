namespace Mibo.Elmish

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// Mutable handle for a MonoGame component instance created during game initialization.
/// </summary>
/// <remarks>
/// This is intended to be allocated in the composition root (per game instance) and then
/// threaded into Elmish <c>update</c>/<c>subscribe</c> functions, avoiding global/module-level mutable state.
/// </remarks>
type ComponentRef<'T when 'T :> IGameComponent>() =
  let mutable value: 'T voption = ValueNone

  /// Attempts to get the component, returning ValueNone if not yet initialized.
  member _.TryGet() : 'T voption = value

  /// Sets the component reference. Called automatically by Program.withComponentRef.
  member _.Set(v: 'T) = value <- ValueSome v

  /// Clears the component reference.
  member _.Clear() = value <- ValueNone

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
  /// <summary>Returns subscriptions based on current model state.</summary>
  Subscribe: GameContext -> 'Model -> Sub<'Msg>
  /// <summary>
  /// List of configuration callbacks invoked in the game constructor.
  /// Use this to set resolution, vsync, window settings, etc.
  /// </summary>
  Config: (Game * GraphicsDeviceManager -> unit) list
  /// <summary>List of renderer factories for drawing.</summary>
  Renderers: (Game -> IRenderer<'Model>) list
  /// <summary>List of MonoGame component factories.</summary>
  Components: (Game -> IGameComponent) list
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
}
