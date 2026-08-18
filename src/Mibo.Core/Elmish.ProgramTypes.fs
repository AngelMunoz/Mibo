namespace Mibo.Elmish

open System
open Mibo.Windowing

/// <summary>
/// Configuration for game window and framerate settings.
/// </summary>
/// <remarks>
/// Config callbacks return a new GameConfig with desired changes applied.
/// Use <see cref="M:Mibo.Elmish.Program.withConfig"/> to register callbacks.
/// </remarks>
[<Struct>]
type GameConfig = {
  /// Window width in pixels. Default: 800.
  Width: int
  /// Window height in pixels. Default: 600.
  Height: int
  /// Window title.
  Title: string
  /// Target render rate. <c>ValueNone</c> = use the backend's default (no cap
  /// imposed); <c>ValueSome n</c> = cap at n FPS (MonoGame fixed timestep at
  /// 1/n, Raylib <c>SetTargetFPS</c>). Defaults to <c>ValueNone</c>.
  TargetFPS: int voption
  /// Minimum window width in pixels. When set, enables resizable window.
  MinWidth: int voption
  /// Minimum window height in pixels. When set, enables resizable window.
  MinHeight: int voption
  /// Whether the user can resize the window. Default: false. Setting
  /// <c>MinWidth</c> or <c>MinHeight</c> also enables it.
  Resizable: bool
  /// Window presentation mode at startup. Default: <c>Windowed</c>.
  WindowMode: WindowMode
}

module GameConfig =
  let defaultConfig = {
    Width = 800
    Height = 600
    Title = "Mibo F#"
    TargetFPS = ValueNone
    MinWidth = ValueNone
    MinHeight = ValueNone
    Resizable = false
    WindowMode = Windowed
  }

  let withWidth width config = { config with Width = width }
  let withHeight height config = { config with Height = height }

  let withMinWidth width config = {
    config with
        MinWidth = ValueSome width
  }

  let withMinHeight height config = {
    config with
        MinHeight = ValueSome height
  }

  let withTitle title config = { config with Title = title }

  /// Cap the render rate at the given FPS (MonoGame fixed timestep at 1/fps,
  /// Raylib <c>SetTargetFPS</c>). Omit it (the default) to leave the backend's
  /// framerate behavior untouched.
  let withTargetFPS fps config = {
    config with
        TargetFPS = ValueSome fps
  }

  /// Allow the user to resize the window.
  let withResizable config = { config with Resizable = true }

  /// Set the window presentation mode at startup (windowed, borderless
  /// fullscreen, or exclusive fullscreen).
  let withWindowMode mode config = { config with WindowMode = mode }

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
}
