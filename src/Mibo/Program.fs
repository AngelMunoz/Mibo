/// <summary>
/// Functions for creating and configuring Elmish game programs.
/// </summary>
/// <remarks>
/// A program defines the complete architecture of a Mibo game: initialization,
/// update logic, subscriptions, rendering, and MonoGame component integration.
/// </remarks>
/// <example>
/// <code>
/// Program.mkProgram init update
/// |&gt; Program.withSubscription subscribe
/// |&gt; Program.withRenderer (Batch2DRenderer.create view)
/// |&gt; Program.withTick Tick
/// |&gt; Program.withAssets
/// |&gt; Program.withInput
/// |&gt; ElmishGame |&gt; _.Run()
/// </code>
/// </example>
module Mibo.Elmish.Program

open System
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Input

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
let mkProgram (init: GameContext -> struct ('Model * Cmd<'Msg>)) update = {
  Init = init
  Update = update
  Subscribe = (fun _ctx _model -> Sub.none)
  Config = ValueNone
  Renderers = []
  Components = []
  Tick = ValueNone
  FixedStep = ValueNone
  DispatchMode = DispatchMode.Immediate
}

/// <summary>
/// Configures how the runtime schedules messages dispatched while processing a frame.
/// </summary>
/// <remarks>
/// Use <see cref="F:Mibo.Elmish.DispatchMode.Immediate"/> for maximum responsiveness (default), or
/// <see cref="F:Mibo.Elmish.DispatchMode.FrameBounded"/> to guarantee that messages dispatched during
/// processing are deferred to the next MonoGame <c>Update</c> call.
/// </remarks>
let withDispatchMode
  (mode: DispatchMode)
  (program: Program<'Model, 'Msg>)
  : Program<'Model, 'Msg> =
  { program with DispatchMode = mode }

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
let withSubscription
  (subscribe: GameContext -> 'Model -> Sub<'Msg>)
  (program: Program<'Model, 'Msg>)
  =
  { program with Subscribe = subscribe }

/// <summary>
/// Configure MonoGame game settings (resolution, vsync, window, etc).
/// </summary>
/// <remarks>
/// The callback receives the Game instance and GraphicsDeviceManager for configuration.
/// This is called during the game constructor, before Initialize.
/// </remarks>
/// <example>
/// <code>
/// program |&gt; Program.withConfig (fun (game, graphics) -&gt;
///     graphics.PreferredBackBufferWidth &lt;- 1920
///     graphics.PreferredBackBufferHeight &lt;- 1080
///     graphics.IsFullScreen &lt;- false
///     game.IsMouseVisible &lt;- true
/// )
/// </code>
/// </example>
let withConfig
  (configure: Game * GraphicsDeviceManager -> unit)
  (program: Program<'Model, 'Msg>)
  =
  {
    program with
        Config = ValueSome configure
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
/// program |&gt; Program.withRenderer (Batch2DRenderer.create view)
/// </code>
/// </example>
let withRenderer
  (factory: Game -> IRenderer<'Model>)
  (program: Program<'Model, 'Msg>)
  =
  {
    program with
        Renderers = factory :: program.Renderers
  }

/// <summary>
/// Adds a MonoGame component to the program.
/// </summary>
/// <remarks>
/// Components are added before Initialize is called, so they participate
/// in the normal MonoGame lifecycle (Initialize, LoadContent, Update, Draw).
/// </remarks>
/// <example>
/// <code>
/// program |&gt; Program.withComponent (fun game -&gt; new AudioComponent(game))
/// </code>
/// </example>
let withComponent
  (factory: Game -> IGameComponent)
  (program: Program<'Model, 'Msg>)
  =
  {
    program with
        Components = factory :: program.Components
  }

/// <summary>
/// Adds a component with a reference that can be accessed from update/subscribe.
/// </summary>
/// <remarks>
/// This allows Elmish code to interact with MonoGame components in a type-safe way
/// without relying on global state.
/// </remarks>
/// <example>
/// <code>
/// let audioRef = ComponentRef&lt;AudioComponent&gt;()
///
/// program |&gt; Program.withComponentRef audioRef (fun game -&gt; new AudioComponent(game))
///
/// // Later in update:
/// match audioRef.TryGet() with
/// | ValueSome audio -&gt; audio.Play("sound")
/// | ValueNone -&gt; ()
/// </code>
/// </example>
let withComponentRef<'Model, 'Msg, 'T when 'T :> IGameComponent>
  (componentRef: ComponentRef<'T>)
  (factory: Game -> 'T)
  (program: Program<'Model, 'Msg>)
  =
  withComponent
    (fun game ->
      let c = factory game
      componentRef.Set c
      c :> IGameComponent)
    program

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
let withTick (map: GameTime -> 'Msg) (program: Program<'Model, 'Msg>) = {
  program with
      Tick = ValueSome map
}

/// <summary>
/// Enables a framework-managed fixed timestep simulation.
/// </summary>
/// <remarks>
/// When enabled, the runtime will dispatch the mapped message zero or more times per MonoGame
/// <c>Update</c> call to advance simulation in stable increments.
/// <para>
/// This is complementary to <see cref="M:Mibo.Elmish.Program.withTick"/>: you can use fixed-step
/// messages for simulation and keep <c>Tick</c> for per-frame tasks (UI, camera smoothing, etc).
/// </para>
/// </remarks>
let withFixedStep
  (cfg: FixedStepConfig<'Msg>)
  (program: Program<'Model, 'Msg>)
  : Program<'Model, 'Msg> =

  if cfg.StepSeconds <= 0.0f then
    invalidArg (nameof cfg.StepSeconds) "StepSeconds must be > 0"

  if cfg.MaxStepsPerFrame <= 0 then
    invalidArg (nameof cfg.MaxStepsPerFrame) "MaxStepsPerFrame must be > 0"

  {
    program with
        FixedStep = ValueSome cfg
  }

/// <summary>
/// Registers the IAssets service for loading and caching game assets.
/// </summary>
/// <remarks>
/// The assets service provides texture, font, sound, and model loading with
/// automatic caching. It also supports custom asset types and JSON deserialization.
/// Assets are automatically disposed when the game exits.
/// </remarks>
/// <example>
/// <code>
/// program |&gt; Program.withAssets
///
/// // Then in your code:
/// let texture = Assets.texture "sprites/player" ctx
/// let font = Assets.font "fonts/main" ctx
/// </code>
/// </example>
let withAssets(program: Program<'Model, 'Msg>) : Program<'Model, 'Msg> =
  let originalInit = program.Init

  let wrappedInit ctx =
    let assets = AssetsService.createFromContext ctx

    // Replace any existing registration (defensive).
    try
      ctx.Game.Services.RemoveService typeof<IAssets>
    with _ ->
      ()

    ctx.Game.Services.AddService(typeof<IAssets>, assets)

    // Best-effort cleanup when the game is exiting.
    ctx.Game.Exiting.Add(fun _ ->
      try
        assets.Dispose()
      with _ ->
        ())

    originalInit ctx

  { program with Init = wrappedInit }

/// <summary>
/// Registers the input polling component for keyboard, mouse, touch, and gamepad.
/// </summary>
/// <remarks>
/// The input service polls hardware each frame and publishes deltas via <see cref="T:Mibo.Input.IInput"/>.
/// This is required for using the Keyboard, Mouse, Touch, and Gamepad subscription modules.
/// This helper is idempotent - calling it multiple times has no effect.
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
let withInput(program: Program<'Model, 'Msg>) : Program<'Model, 'Msg> =
  withComponent
    (fun game ->
      // Idempotent behavior: if input is already registered, do nothing.
      // This avoids accidentally creating multiple polling components when a user
      // composes `withInput`/`withInputMapper`/`withInputMapping` together.
      let existing = game.Services.GetService(typeof<IInput>)

      if not(isNull existing) then
        new GameComponent(game) :> IGameComponent
      else
        let input = Input.create game

        // Register as service so subscriptions can find it
        game.Services.AddService(typeof<IInput>, input)
        input :> IGameComponent)
    program


/// <summary>
/// Configures the game to register an <see cref="T:Mibo.Input.IInputMapper`1"/> service.
/// </summary>
/// <remarks>
/// <para>This registers <see cref="T:Mibo.Input.IInput"/> automatically (equivalent to <see cref="M:Mibo.Elmish.Program.withInput"/>).</para>
/// <para>The mapper is ticked via a MonoGame <see cref="T:Microsoft.Xna.Framework.GameComponent"/>.</para>
/// <para>If you want to stay fully "Elmish" (no service access), consider using
/// <see cref="M:Mibo.Input.InputMapper.subscribe"/> instead and handle a single message.</para>
/// </remarks>
let withInputMapper<'Model, 'Msg, 'Action when 'Action: comparison>
  (initialMap: InputMap<'Action>)
  (program: Program<'Model, 'Msg>)
  : Program<'Model, 'Msg> =

  // Ensure core input exists.
  let program = program |> withInput

  let originalInit = program.Init

  let wrappedInit ctx =
    let coreInput = Input.getService ctx

    // Replace any existing mapper registration (defensive).
    let existing = ctx.Game.Services.GetService(typeof<IInputMapper<'Action>>)

    match existing with
    | null -> ()
    | :? IDisposable as d ->
      try
        d.Dispose()
      with _ ->
        ()

      try
        ctx.Game.Services.RemoveService typeof<IInputMapper<'Action>>
      with _ ->
        ()
    | _ ->
      try
        ctx.Game.Services.RemoveService typeof<IInputMapper<'Action>>
      with _ ->
        ()

    let mapper = new InputMapperService<'Action>(coreInput, initialMap)
    ctx.Game.Services.AddService(typeof<IInputMapper<'Action>>, mapper)

    // Best-effort cleanup when the game is exiting.
    ctx.Game.Exiting.Add(fun _ ->
      try
        (mapper :> IDisposable).Dispose()
      with _ ->
        ())

    // Tick mapper via GameComponent.
    ctx.Game.Components.Add
      { new GameComponent(ctx.Game) with
          override _.Update _gameTime =
            (mapper :> IInputMapper<'Action>).Update()
      }

    originalInit ctx

  { program with Init = wrappedInit }
