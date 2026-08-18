namespace Mibo.Elmish

open System
open Mibo.Input

/// <summary>
/// Functions for creating and configuring Elmish game programs.
/// </summary>
/// <remarks>
/// A program defines the complete architecture of a Mibo game: initialization,
/// update logic, subscriptions, rendering, and service integration.
/// </remarks>
/// <example>
/// <code>
/// Program.mkProgram init update
/// |&gt; Program.withSubscription subscribe
/// |&gt; Program.withRenderer (fun () -&gt; Renderer2D.create view)
/// |&gt; Program.withTick Tick
/// |&gt; Program.withAssets
/// |&gt; Program.withInput
/// |&gt; RaylibGame |&gt; _.Run()
/// </code>
/// </example>
module Program =

  /// <summary>
  /// Creates a new program with the given init and update functions.
  /// </summary>
  /// <remarks>
  /// This is the starting point for building an Elmish game. The init function
  /// creates the initial model and startup commands, while update handles messages.
  /// </remarks>
  /// <param name="init">Function that receives GameContext and returns initial (Model, Cmd)</param>
  /// <param name="update">Function that receives a message and model, returns (Model, Cmd)</param>
  /// <example>
  /// <code>
  /// let init ctx = struct (initialModel, Cmd.none)
  /// let update msg model = struct (model, Cmd.none)
  /// let program = Program.mkProgram init update
  /// </code>
  /// </example>
  let mkProgram init update = {
    Init = init
    Update = update
    UpdateCtx = ValueNone
    Subscribe = (fun _ctx _model -> Sub.none)
    Config = []
    Renderers = []
    Tick = ValueNone
    FixedStep = ValueNone
    DispatchMode = DispatchMode.Immediate
    AssetsBasePath = ValueNone
    HasInput = false
    HasInputMapper = false
    ServiceRegistrations = []
  }

  /// <summary>
  /// Creates a new program whose update function also receives the GameContext.
  /// </summary>
  /// <remarks>
  /// <c>init</c>, <c>Subscribe</c>, and the renderer callbacks already receive
  /// the GameContext; this builder completes the set for <c>update</c>. The
  /// runtime resolves services and window dimensions from it, so update code
  /// no longer needs to capture the context from <c>init</c>.
  /// </remarks>
  /// <param name="init">Function that receives GameContext and returns initial (Model, Cmd)</param>
  /// <param name="update">Function that receives the GameContext, a message, and model, returns (Model, Cmd)</param>
  /// <example>
  /// <code>
  /// let init ctx = struct (initialModel, Cmd.none)
  /// let update ctx msg model = struct (model, Cmd.none)
  /// let program = Program.mkProgramCtx init update
  /// </code>
  /// </example>
  let mkProgramCtx
    (init: GameContext -> struct ('Model * Cmd<'Msg>))
    (update: GameContext -> 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>))
    : Program<'Model, 'Msg> =
    {
      Init = init
      // Never called: the runtime prefers UpdateCtx. Raises instead of
      // silently dropping messages if an internal path regresses.
      Update =
        (fun _ _ ->
          invalidOp
            "Program built with mkProgramCtx: the runtime invokes UpdateCtx.")
      UpdateCtx = ValueSome update
      Subscribe = (fun _ctx _model -> Sub.none)
      Config = []
      Renderers = []
      Tick = ValueNone
      FixedStep = ValueNone
      DispatchMode = DispatchMode.Immediate
      AssetsBasePath = ValueNone
      HasInput = false
      HasInputMapper = false
      ServiceRegistrations = []
    }

  /// <summary>
  /// Replaces the update function with a context-aware one. The runtime calls
  /// it instead of the <c>Update</c> set by <see cref="M:Mibo.Elmish.Program.mkProgram"/>.
  /// </summary>
  let withUpdateCtx
    (update: GameContext -> 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>))
    (program: Program<'Model, 'Msg>)
    =
    {
      program with
          UpdateCtx = ValueSome update
    }

  /// <summary>
  /// Configure game settings (resolution, title, framerate).
  /// </summary>
  /// <remarks>
  /// The callback receives the current GameConfig and returns a modified copy.
  /// </remarks>
  /// <example>
  /// <code>
  /// program
  /// |&gt; Program.withConfig (
  ///   GameConfig.withWidth 1920
  ///   &gt;&gt; GameConfig.withHeight 1080
  ///   &gt;&gt; GameConfig.withTitle "My Game"
  /// )
  /// </code>
  /// </example>
  let withConfig
    (configure: GameConfig -> GameConfig)
    (program: Program<'Model, 'Msg>)
    =
    {
      program with
          Config = configure :: program.Config
    }

  /// <summary>
  /// Adds a renderer to the program.
  /// </summary>
  /// <remarks>
  /// Renderers are called each frame to draw the current model state.
  /// Multiple renderers can be added (e.g., 2D UI on top of 3D scene).
  /// </remarks>
  /// <example>
  /// <code>
  /// program |&gt; Program.withRenderer (fun () -&gt; Renderer2D.create view)
  /// </code>
  /// </example>
  let withRenderer
    (factory: unit -> IRenderer<'Model>)
    (program: Program<'Model, 'Msg>)
    =
    {
      program with
          Renderers = factory :: program.Renderers
    }

  /// <summary>
  /// Adds a per-frame tick message to the program.
  /// </summary>
  /// <remarks>
  /// The tick function is called once per frame and can dispatch a message
  /// containing the GameTime for time-based updates.
  /// </remarks>
  /// <example>
  /// <code>
  /// type Msg = Tick of GameTime | ...
  /// program |&gt; Program.withTick Tick
  /// </code>
  /// </example>
  let withTick map program = { program with Tick = ValueSome map }

  /// <summary>
  /// Enables a framework-managed fixed timestep simulation.
  /// </summary>
  /// <remarks>
  /// When enabled, the runtime will dispatch the mapped message zero or more times per
  /// <c>Update</c> call to advance simulation in stable increments.
  /// <para>
  /// This is complementary to <see cref="M:Mibo.Elmish.Program.withTick"/>: you can use fixed-step
  /// messages for simulation and keep <c>Tick</c> for per-frame tasks (UI, camera smoothing, etc).
  /// </para>
  /// </remarks>
  let withFixedStep cfg program =
    if cfg.StepSeconds <= 0.0f then
      invalidArg (nameof cfg.StepSeconds) "StepSeconds must be > 0"

    if cfg.MaxStepsPerFrame <= 0 then
      invalidArg (nameof cfg.MaxStepsPerFrame) "MaxStepsPerFrame must be > 0"

    {
      program with
          FixedStep = ValueSome cfg
    }

  /// <summary>
  /// Configures how the runtime schedules messages dispatched while processing a frame.
  /// </summary>
  /// <remarks>
  /// Use <see cref="F:Mibo.Elmish.DispatchMode.Immediate"/> for maximum responsiveness (default), or
  /// <see cref="F:Mibo.Elmish.DispatchMode.FrameBounded"/> to guarantee that messages dispatched during
  /// processing are deferred to the next <c>Update</c> call.
  /// </remarks>
  let withDispatchMode mode program = { program with DispatchMode = mode }

  /// <summary>
  /// Adds a subscription function to the program.
  /// </summary>
  /// <remarks>
  /// The subscription function is called after each model update. It should return
  /// subscriptions based on the current model state. The runtime manages subscription
  /// lifecycle automatically through SubId diffing.
  /// </remarks>
  /// <example>
  /// <code>
  /// let subscribe ctx model =
  ///     Keyboard.onPressed KeyPressed ctx
  ///
  /// program |&gt; Program.withSubscription subscribe
  /// </code>
  /// </example>
  let withSubscription subscribe program = {
    program with
        Subscribe = subscribe
  }

  /// <summary>
  /// Ensures the IAssets service is available (always true, included for API parity).
  /// </summary>
  /// <remarks>
  /// The assets service is automatically created by the runtime. Use
  /// <see cref="M:Mibo.Elmish.Program.withAssetsBasePath"/> to configure a base path.
  /// </remarks>
  let withAssets(program: Program<'Model, 'Msg>) : Program<'Model, 'Msg> =
    program

  /// <summary>
  /// Configures a base path for asset loading.
  /// </summary>
  /// <remarks>
  /// When set, all relative asset paths are resolved relative to this base path.
  /// </remarks>
  /// <example>
  /// <code>
  /// program |&gt; Program.withAssetsBasePath "assets/"
  /// </code>
  /// </example>
  let withAssetsBasePath
    (basePath: string)
    (program: Program<'Model, 'Msg>)
    : Program<'Model, 'Msg> =
    {
      program with
          AssetsBasePath = ValueSome basePath
    }

  /// <summary>
  /// Enables the reactive input polling service.
  /// </summary>
  /// <remarks>
  /// Registers <see cref="T:Mibo.Input.IInput"/> in the GameContext service container.
  /// Required for using Keyboard, Mouse, Touch, and Gamepad subscription modules.
  /// </remarks>
  /// <example>
  /// <code>
  /// program |&gt; Program.withInput
  ///
  /// // Then subscribe to input:
  /// Keyboard.onPressed KeyPressed ctx
  /// Mouse.onLeftClick MouseClicked ctx
  /// Gamepad.listen GamepadInput ctx
  /// </code>
  /// </example>
  let withInput(program: Program<'Model, 'Msg>) : Program<'Model, 'Msg> = {
    program with
        HasInput = true
  }

  /// <summary>
  /// Appends a service-registration callback invoked by the runtime host after
  /// core services (assets, input) are registered but before <c>Init</c>.
  /// </summary>
  /// <remarks>
  /// This is the backend-neutral hook for registering extra services. Backend
  /// builder functions (e.g. raylib's <c>withInputMapper</c>) use it to register
  /// backend-specific service implementations without the Core Program builder
  /// referencing a backend factory directly.
  /// </remarks>
  let withServiceRegistration
    (register: GameContext -> unit)
    (program: Program<'Model, 'Msg>)
    : Program<'Model, 'Msg> =
    {
      program with
          ServiceRegistrations = register :: program.ServiceRegistrations
    }
