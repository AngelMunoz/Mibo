namespace Mibo.Adaptive

open System
open Mibo.Elmish

/// <summary>
/// The graph-building context handed to an
/// <see cref="T:Mibo.Adaptive.AdaptiveProgram`1"/> <c>Init</c> function.
/// </summary>
/// <remarks>
/// The context exposes the framework-owned roots: the time cell, written by the
/// runner at the start of every <c>Step</c>, the exit-request cell, read by the
/// runner to decide whether to stop, and the restart-request cell, read by the
/// host to decide whether to rebuild the program. The program reads these cells
/// and derives its projections from them like any other root; only
/// <c>ExitRequested</c>/<c>RestartRequested</c> are written by the program.
/// </remarks>
type AdaptiveContext
  internal
  (
    ctx: GameContext,
    time: cval<GameTime>,
    exitRequested: cval<bool>,
    restartRequested: cval<bool>
  ) =
  /// <summary>The framework-owned time root. The runner writes it at the start of every <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>; projections may depend on it (animation, physics, timers).</summary>
  member _.Time = time

  /// <summary>The exit-request root. Set it to <c>true</c> to make the runner stop — the adaptive counterpart of <c>Cmd.signalExit</c>.</summary>
  member _.ExitRequested = exitRequested

  /// <summary>
  /// The restart-request root. Set it to <c>true</c> to rebuild the program: the
  /// runner disposes the program's disposables, re-runs <c>Init</c> — a fresh
  /// graph over the same roots — and forces the first frame. The windowed
  /// hosts consume it after <c>Step</c>; headless users call
  /// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Restart"/> themselves.
  /// </summary>
  member _.RestartRequested = restartRequested

  /// <summary>
  /// The <see cref="T:Mibo.Elmish.GameContext"/> the runner owns: the window
  /// dimensions and the registered services (IAssets, IInput, custom). Programs
  /// read services directly from here in <c>Init</c> and <c>Update</c> — the
  /// host registers what it owns and the program pulls the rest; there is no
  /// registration ceremony.
  /// </summary>
  member _.Context = ctx

  /// <summary>Current window width in pixels. Default: 800.</summary>
  member _.WindowWidth = ctx.WindowWidth

  /// <summary>Current window height in pixels. Default: 600.</summary>
  member _.WindowHeight = ctx.WindowHeight

/// <summary>The result of building an adaptive program's graph.</summary>
[<Struct>]
type AdaptiveInit<'Frame> = {
  /// <summary>
  /// The frame builder: forces the frame's output projections (recomputing each
  /// exactly once if a dependency moved, and not at all otherwise) and packs
  /// them into <c>'Frame</c>.
  /// </summary>
  FrameBuilder: unit -> 'Frame

  /// <summary>Disposables released when the runner is disposed or restarted.</summary>
  Disposables: IDisposable list
}

/// <summary>Helpers for building an <see cref="T:Mibo.Adaptive.AdaptiveInit`1"/>.</summary>
/// <remarks>
/// These keep the <c>Disposables</c> list hidden: <see cref="M:Mibo.Adaptive.AdaptiveInit.ofFrameBuilder"/>
/// defaults it to empty, and <see cref="M:Mibo.Adaptive.AdaptiveInit.withDisposables"/> /
/// <see cref="M:Mibo.Adaptive.AdaptiveInit.withDisposable"/> append to it. Callers never write
/// <c>Disposables = []</c> by hand.
/// </remarks>
module AdaptiveInit =

  /// <summary>
  /// Creates an init from a frame builder with no disposables.
  /// </summary>
  /// <param name="frameBuilder">Forces the frame's output projections and packs them into <c>'Frame</c>.</param>
  let ofFrameBuilder(frameBuilder: unit -> 'Frame) : AdaptiveInit<'Frame> = {
    FrameBuilder = frameBuilder
    Disposables = []
  }

  /// <summary>
  /// Appends a list of disposables to the init. The disposables are released
  /// when the runner is disposed or when the program is restarted.
  /// </summary>
  let withDisposables
    (disposables: IDisposable list)
    (init: AdaptiveInit<'Frame>)
    : AdaptiveInit<'Frame> =
    {
      init with
          Disposables = init.Disposables @ disposables
    }

  /// <summary>
  /// Adds a single disposable to the init. The disposable is released when the
  /// runner is disposed or when the program is restarted.
  /// </summary>
  let withDisposable
    (disposable: IDisposable)
    (init: AdaptiveInit<'Frame>)
    : AdaptiveInit<'Frame> =
    {
      init with
          Disposables = disposable :: init.Disposables
    }

/// <summary>
/// Fixed-step configuration for an adaptive program — the adaptive counterpart
/// of <see cref="T:Mibo.Elmish.FixedStepConfig`1"/>, without the message-mapping
/// slot (there is no <c>'Msg</c>).
/// </summary>
/// <remarks>
/// <para>
/// When enabled, the runner converts the variable frame time into zero or more
/// fixed-size steps per <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>: for
/// each step it applies cross-thread posts, writes the time root, and runs the
/// program's <c>Update</c> phase. The frame is forced once at the end, so the
/// intermediate steps are integrated but not observed by the frame.
/// </para>
/// <para>
/// This mirrors the MVU <c>TickFrame</c> fixed-step loop, calling
/// <c>Update</c> instead of dispatching a mapped message.
/// </para>
/// </remarks>
[<Struct>]
type AdaptiveFixedStepConfig = {
  /// <summary>Fixed simulation step size in seconds (e.g. 1/60 = 0.0166667).</summary>
  StepSeconds: float32

  /// <summary>
  /// Maximum number of fixed steps to run in a single frame.
  /// </summary>
  /// <remarks>
  /// This prevents the "spiral of death" after long stalls. If the cap is hit, remaining
  /// accumulated time is dropped.
  /// </remarks>
  MaxStepsPerFrame: int

  /// <summary>
  /// Clamp the per-frame delta used for accumulation. Default behavior is to
  /// clamp to 0.25 seconds.
  /// </summary>
  MaxFrameSeconds: float32 voption
}

/// <summary>
/// An adaptive program: the complete description of an adaptive game.
/// </summary>
/// <remarks>
/// <para>
/// This is the adaptive counterpart of <see cref="T:Mibo.Elmish.Program`2"/>,
/// with the message/command/subscription machinery removed. Changeable roots
/// hold the state, derived projections compose it, and the runner forces the
/// frame's projections at the end of every step. There is no <c>'Msg</c>, no
/// <c>Cmd</c> and no <c>Sub</c> — handlers write roots directly and effects run
/// directly.
/// </para>
/// <para>
/// The <c>Init</c>/<c>Update</c>/<c>Observers</c> slots are the dependency
/// graph (the simulation). The <c>Config</c>/<c>Renderers</c>/
/// <c>ServiceRegistrations</c>/<c>AssetsBasePath</c> slots are host
/// configuration (the presentation) consumed by the windowed hosts. The headless
/// runner consumes only the simulation slots plus <c>FixedStep</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// let program =
///   AdaptiveProgram.mkProgram
///     (fun ctx -&gt;
///       AdaptiveInit.ofFrameBuilder (Frame.buildFrame cell)
///       |&gt; AdaptiveInit.withDisposables [ d1; d2 ])
///     (fun ctx gameTime -&gt; Router.step world gameTime)
///   |&gt; AdaptiveProgram.withConfig cfg
///   |&gt; AdaptiveProgram.withRenderer (fun () -&gt; Renderer2D.create view)
///
/// AdaptiveRaylibGame(program).Run()
/// </code>
/// </example>
type AdaptiveProgram<'Frame> = {
  /// <summary>
  /// Builds the graph (roots and projections) and registers handlers.
  /// Returns the <see cref="T:Mibo.Adaptive.AdaptiveInit`1"/> — the frame
  /// builder plus the disposables released when the runner is disposed or
  /// restarted.
  /// </summary>
  Init: AdaptiveContext -> AdaptiveInit<'Frame>

  /// <summary>
  /// Optional per-frame phase. Runs after the time root is written and before
  /// the frame is forced. Reads projections, writes roots. Under fixed-step it
  /// runs once per fixed step.
  /// </summary>
  Update: AdaptiveContext -> GameTime -> unit

  /// <summary>Observer factories for receiving the forced frame each step.</summary>
  Observers: (unit -> IObserver<struct (GameContext * 'Frame * GameTime)>) list

  /// <summary>
  /// Configuration callbacks that transform the default
  /// <see cref="T:Mibo.Elmish.GameConfig"/>. Each callback receives the current
  /// config and returns a modified copy. Applied in registration order.
  /// </summary>
  Config: (GameConfig -> GameConfig) list

  /// <summary>
  /// Renderer factories called each frame to draw the forced frame. Multiple
  /// renderers draw in registration order.
  /// </summary>
  Renderers: (unit -> IRenderer<'Frame>) list

  /// <summary>
  /// Service-registration callbacks invoked by the windowed hosts after core
  /// services (assets, input) are registered but before <c>Init</c>. The
  /// backend-neutral hook for registering extra services.
  /// </summary>
  ServiceRegistrations: (GameContext -> unit) list

  /// <summary>Optional base path for asset loading.</summary>
  AssetsBasePath: string voption

  /// <summary>
  /// Optional framework-managed fixed-step configuration. When set, the runner
  /// sub-steps <c>Update</c> in fixed increments.
  /// </summary>
  FixedStep: AdaptiveFixedStepConfig voption

  /// <summary>
  /// Whether the reactive input polling service is enabled. Set via
  /// <see cref="M:Mibo.Adaptive.AdaptiveProgram.withInput"/>.
  /// </summary>
  HasInput: bool
}

/// <summary>Functions for creating and configuring adaptive programs.</summary>
[<Experimental("Under active development, the API may change without notice and carries no stability guarantees.");
  RequireQualifiedAccess>]
module AdaptiveProgram =

  /// <summary>
  /// Creates a <c>System.IObserver</c> from an <c>onNext</c> callback, hiding
  /// the <c>OnError</c> and <c>OnCompleted</c> boilerplate.
  /// </summary>
  let inline observe
    (onNext: struct (GameContext * 'Frame * GameTime) -> unit)
    : IObserver<struct (GameContext * 'Frame * GameTime)> =
    { new IObserver<struct (GameContext * 'Frame * GameTime)> with
        member _.OnNext value = onNext value
        member _.OnError _ = ()
        member _.OnCompleted() = ()
    }

  /// <summary>
  /// Creates a new adaptive program with the given <c>Init</c> and
  /// <c>Update</c> functions.
  /// </summary>
  /// <remarks>
  /// This is the starting point for building an adaptive game, mirroring
  /// <see cref="M:Mibo.Elmish.Program.mkProgram"/>. The init function builds the
  /// dependency graph (roots and projections) and returns the frame builder; the
  /// update function is the per-frame imperative phase that reads projections
  /// and writes roots.
  /// </remarks>
  /// <param name="init">Builds the graph and returns the <see cref="T:Mibo.Adaptive.AdaptiveInit`1"/>.</param>
  /// <param name="update">Per-frame phase: reads projections, writes roots.</param>
  let mkProgram
    (init: AdaptiveContext -> AdaptiveInit<'Frame>)
    (update: AdaptiveContext -> GameTime -> unit)
    : AdaptiveProgram<'Frame> =
    {
      Init = init
      Update = update
      Observers = []
      Config = []
      Renderers = []
      ServiceRegistrations = []
      AssetsBasePath = ValueNone
      FixedStep = ValueNone
      HasInput = false
    }

  /// <summary>Sets the per-frame phase of the program.</summary>
  let withUpdate
    (update: AdaptiveContext -> GameTime -> unit)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    { program with Update = update }

  /// <summary>
  /// Adds an observer factory to the program. Observers receive the forced
  /// frame each step, in registration order.
  /// </summary>
  let withObserver
    (factory: unit -> IObserver<struct (GameContext * 'Frame * GameTime)>)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          Observers = factory :: program.Observers
    }

  /// <summary>
  /// Configure game settings (resolution, title, framerate). The callback
  /// receives the current <see cref="T:Mibo.Elmish.GameConfig"/> and returns a
  /// modified copy.
  /// </summary>
  let withConfig
    (configure: GameConfig -> GameConfig)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          Config = configure :: program.Config
    }

  /// <summary>
  /// Adds a renderer factory to the program. Renderers are called each frame to
  /// draw the forced frame, in registration order.
  /// </summary>
  let withRenderer
    (factory: unit -> IRenderer<'Frame>)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          Renderers = factory :: program.Renderers
    }

  /// <summary>
  /// Appends a service-registration callback invoked by the windowed hosts
  /// after core services (assets, input) are registered but before
  /// <c>Init</c>.
  /// </summary>
  let withServiceRegistration
    (register: GameContext -> unit)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          ServiceRegistrations = register :: program.ServiceRegistrations
    }

  /// <summary>
  /// Configures a base path for asset loading.
  /// </summary>
  let withAssetsBasePath
    (basePath: string)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          AssetsBasePath = ValueSome basePath
    }

  /// <summary>
  /// Enables a framework-managed fixed timestep. When enabled, the runner
  /// converts the variable frame time into zero or more fixed-size steps per
  /// <c>Step</c>, running <c>Update</c> once per step and forcing the frame
  /// once at the end.
  /// </summary>
  let withFixedStep
    (cfg: AdaptiveFixedStepConfig)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    if cfg.StepSeconds <= 0.0f then
      invalidArg (nameof cfg.StepSeconds) "StepSeconds must be > 0"

    if cfg.MaxStepsPerFrame <= 0 then
      invalidArg (nameof cfg.MaxStepsPerFrame) "MaxStepsPerFrame must be > 0"

    {
      program with
          FixedStep = ValueSome cfg
    }

  /// <summary>
  /// Enables the reactive input polling service.
  /// </summary>
  /// <remarks>
  /// Registers <see cref="T:Mibo.Input.IInput"/> in the GameContext service
  /// container and polls it once per frame before <c>Update</c>. Required for
  /// programs that read keyboard, mouse, touch, or gamepad input. Off by
  /// default — mirrors <see cref="M:Mibo.Elmish.Program.withInput"/>.
  /// </remarks>
  let withInput(program: AdaptiveProgram<'Frame>) : AdaptiveProgram<'Frame> = {
    program with
        HasInput = true
  }
