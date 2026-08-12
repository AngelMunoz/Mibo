namespace Mibo.Adaptive

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Mibo.Elmish

/// <summary>
/// The intent queue of an adaptive program: the single place to post
/// <c>unit -> unit</c> work during <c>Update</c>. Intents are thunks deferred
/// to a moment the runtime owns, exposed as moment-named entry points:
/// <see cref="M:Mibo.Adaptive.IntentQueue.post"/> (later in this step),
/// <see cref="M:Mibo.Adaptive.IntentQueue.postNextFrame"/> (top of the next
/// step), and <see cref="M:Mibo.Adaptive.IntentQueue.postTask"/> /
/// <see cref="M:Mibo.Adaptive.IntentQueue.postAsync"/> (background work whose
/// completion returns via <c>post</c>). The adaptive counterpart of <c>Cmd</c>
/// — closures capture the handler, so no message type is needed.
/// </summary>
/// <remarks>
/// Both lanes are MPSC: a
/// <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1"/> for
/// producers, drained wholesale by the single consumer (the runner) on the
/// owner thread. The post drain runs until empty — thunks posted during the
/// drain run in the same pass, in post order, mirroring MVU's
/// <c>DispatchMode.Immediate</c> semantics; work-posting-work cycles are the
/// user's responsibility, exactly as message cycles are in MVU. The
/// next-frame lane drains once per <c>Step</c> at the boundary, never per
/// sub-step.
/// Allocation: one queue node per posted thunk — acceptable on the cold
/// path, zero on steps with no posted work.
/// </remarks>
type IntentQueue() =
  let posted = ConcurrentQueue<unit -> unit>()
  let nextFrame = ConcurrentQueue<unit -> unit>()

  /// <summary>
  /// Defers <c>work</c> to the next post drain: it runs on the owner thread
  /// after the next <c>Update</c> (after each sub-step's <c>Update</c> under
  /// fixed-step), in post order, drained until empty, before the frame is
  /// forced. Cross-system reactions, foreign-thread posts, and async
  /// completions go here. Safe to call from any thread; the thunk itself must
  /// only run owner-thread legal writes (plain <c>Set</c> calls). The
  /// adaptive counterpart of <c>Cmd.ofMsg</c>.
  /// </summary>
  /// <param name="work">The work to run at the post drain.</param>
  member _.post(work: unit -> unit) = posted.Enqueue work

  /// <summary>
  /// Defers <c>work</c> to the top of the NEXT
  /// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>: it runs on the
  /// owner thread at the frame boundary, after cross-thread posts are applied
  /// (<c>Posting.pump</c>) and before the time root is written. The adaptive
  /// counterpart of <c>Cmd.deferNextFrame</c> — the lane exists for explicit
  /// deferrals that must not react within the posting step. Safe to call
  /// from any thread.
  /// </summary>
  /// <param name="work">The work to run at the next step's boundary.</param>
  member _.postNextFrame(work: unit -> unit) = nextFrame.Enqueue work

  /// <summary>
  /// Defers a background task to this queue: the starter runs on the owner
  /// thread at the next post drain, <c>work</c> runs off the owner thread,
  /// and the completion (<c>ofSuccess</c> or <c>ofError</c>) is posted to the
  /// same lane and runs on the owner thread at a later post drain, where root
  /// writes are legal. The runtime owns the crossing — the caller never
  /// touches post/pump. The adaptive counterpart of <c>Cmd.ofTask</c>.
  /// </summary>
  /// <param name="work">The background work to start; must not touch the graph.</param>
  /// <param name="ofSuccess">Receives the result on the owner thread at a later post drain.</param>
  /// <param name="ofError">Receives the exception on the owner thread at a later post drain.</param>
  member this.postTask
    (work: unit -> Task<'T>, ofSuccess: 'T -> unit, ofError: exn -> unit)
    =
    this.post(fun () ->
      async {
        try
          let! r = work() |> Async.AwaitTask
          posted.Enqueue(fun () -> ofSuccess r)
        with ex ->
          posted.Enqueue(fun () -> ofError ex)
      }
      |> Async.Start)

  /// <summary>
  /// Defers an F# async workflow to this queue: the starter runs on the owner
  /// thread at the next post drain, the workflow runs off the owner thread,
  /// and the completion (<c>ofSuccess</c> or <c>ofError</c>) is posted to the
  /// same lane and runs on the owner thread at a later post drain, where root
  /// writes are legal. The adaptive counterpart of <c>Cmd.ofAsync</c>.
  /// </summary>
  /// <param name="work">The async workflow to start; must not touch the graph.</param>
  /// <param name="ofSuccess">Receives the result on the owner thread at a later post drain.</param>
  /// <param name="ofError">Receives the exception on the owner thread at a later post drain.</param>
  member this.postAsync
    (work: Async<'T>, ofSuccess: 'T -> unit, ofError: exn -> unit)
    =
    this.post(fun () ->
      async {
        try
          let! r = work
          posted.Enqueue(fun () -> ofSuccess r)
        with ex ->
          posted.Enqueue(fun () -> ofError ex)
      }
      |> Async.Start)

  /// <summary>
  /// Runs every pending posted thunk in post order on the calling (owner)
  /// thread until the lane is empty: thunks posted during the drain (from
  /// any thread) run in the same pass, in post order. No termination bound.
  /// </summary>
  member internal _.Drain() =
    let mutable next = Unchecked.defaultof<unit -> unit>

    while posted.TryDequeue(&next) do
      next()

  /// <summary>
  /// Runs the next-frame lane on the calling (owner) thread: everything queued
  /// at the top of the step, in post order. Thunks posted during this drain
  /// also run in the same pass (same semantics as the post drain).
  /// </summary>
  member internal _.DrainNextFrame() =
    let mutable next = Unchecked.defaultof<unit -> unit>

    while nextFrame.TryDequeue(&next) do
      next()

/// <summary>
/// The graph-building context handed to an
/// <see cref="T:Mibo.Adaptive.AdaptiveProgram`1"/> <c>Init</c> function and to
/// the subscription projection
/// (<see cref="M:Mibo.Adaptive.AdaptiveProgram.withSubscriptions"/>).
/// </summary>
/// <remarks>
/// The context exposes the framework-owned roots: the time cell, written by
/// the runner at the start of every <c>Step</c> (once per fixed sub-step),
/// the exit-request cell, read by the runner to decide whether to stop, plus
/// the <see cref="T:Mibo.Elmish.GameContext"/> holding the window dimensions
/// and the registered services. The program reads these cells and derives
/// its projections from them like any other root; only <c>ExitRequested</c>
/// is written by the program. The context deliberately does NOT expose the
/// intent queue — phase
/// reachability by type: the frame builder and init/projection construction
/// must not be able to defer work. The queue is reachable only from the
/// <c>Update</c> phase, via <see cref="T:Mibo.Adaptive.AdaptiveContext"/>.
/// </remarks>
type AdaptiveFrameContext
  internal (ctx: GameContext, time: cval<GameTime>, exitRequested: cval<bool>) =
  /// <summary>The framework-owned time root. The runner writes it at the start of every <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/> (once per fixed sub-step); projections may depend on it (animation, physics, timers).</summary>
  member _.Time = time

  /// <summary>The exit-request root. Set it to <c>true</c> to make the runner stop — the adaptive counterpart of <c>Cmd.signalExit</c>.</summary>
  member _.ExitRequested = exitRequested

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

/// <summary>
/// The <c>Update</c>-phase context: everything on
/// <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/> plus the framework-owned
/// intent queue, <see cref="P:Mibo.Adaptive.AdaptiveContext.Intents"/> —
/// intents are thunks of <c>unit -> unit</c> work deferred to a
/// framework-owned moment (after <c>Update</c>, at the top of the next step,
/// or as background work whose completion returns via <c>post</c>).
/// </summary>
/// <remarks>
/// The queue is reachable only here, where posting is legal: <c>Update</c>
/// reacts to what it read and defers work; the frame builder and projection
/// construction see only
/// <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/>, so the force phase
/// cannot enqueue — correctness by construction, no runtime guards.
/// </remarks>
type AdaptiveContext
  internal (frameCtx: AdaptiveFrameContext, intents: IntentQueue) =
  /// <summary>The framework-owned time root. The runner writes it at the start of every <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/> (once per fixed sub-step); projections may depend on it (animation, physics, timers).</summary>
  member _.Time = frameCtx.Time

  /// <summary>The exit-request root. Set it to <c>true</c> to make the runner stop — the adaptive counterpart of <c>Cmd.signalExit</c>.</summary>
  member _.ExitRequested = frameCtx.ExitRequested

  /// <summary>
  /// The <see cref="T:Mibo.Elmish.GameContext"/> the runner owns: the window
  /// dimensions and the registered services (IAssets, IInput, custom). Programs
  /// read services directly from here in <c>Init</c> and <c>Update</c> — the
  /// host registers what it owns and the program pulls the rest; there is no
  /// registration ceremony.
  /// </summary>
  member _.Context = frameCtx.Context

  /// <summary>Current window width in pixels. Default: 800.</summary>
  member _.WindowWidth = frameCtx.WindowWidth

  /// <summary>Current window height in pixels. Default: 600.</summary>
  member _.WindowHeight = frameCtx.WindowHeight

  /// <summary>
  /// The intent queue: the single place to post <c>unit -> unit</c> work
  /// during <c>Update</c>. Choose the moment by name —
  /// <see cref="M:Mibo.Adaptive.IntentQueue.post"/> (after this step's
  /// <c>Update</c>, until empty), <see cref="M:Mibo.Adaptive.IntentQueue.postNextFrame"/>
  /// (at the top of the next step), or
  /// <see cref="M:Mibo.Adaptive.IntentQueue.postTask"/>/<c>postAsync</c>
  /// (background work whose completion returns via <c>post</c>). The adaptive
  /// counterpart of <c>Cmd</c>.
  /// </summary>
  member _.Intents = intents

/// <summary>The result of building an adaptive program's graph.</summary>
[<Struct>]
type AdaptiveInit<'Frame> = {
  /// <summary>
  /// The frame builder: forces the frame's output projections (recomputing each
  /// exactly once if a dependency moved, and not at all otherwise) and packs
  /// them into <c>'Frame</c>.
  /// </summary>
  FrameBuilder: unit -> 'Frame

  /// <summary>Disposables released when the runner is disposed.</summary>
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
  let inline ofFrameBuilder
    ([<InlineIfLambda>] frameBuilder: unit -> 'Frame)
    : AdaptiveInit<'Frame> =
    {
      FrameBuilder = frameBuilder
      Disposables = []
    }

  /// <summary>
  /// Appends a list of disposables to the init. The disposables are released
  /// when the runner is disposed.
  /// </summary>
  let inline withDisposables
    (disposables: IDisposable list)
    (init: AdaptiveInit<'Frame>)
    : AdaptiveInit<'Frame> =
    {
      init with
          Disposables = init.Disposables @ disposables
    }

  /// <summary>
  /// Adds a single disposable to the init. The disposable is released when the
  /// runner is disposed.
  /// </summary>
  let inline withDisposable
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
/// fixed-size steps per <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>: it
/// applies cross-thread posts once at the boundary, then per fixed-size step
/// writes the time root, runs the program's <c>Update</c> phase, and drains the
/// intent queue until empty — each sub-step reads settled state. The frame is
/// forced once at the end, so the intermediate steps are integrated but not
/// observed by the frame.
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
/// A subscription spec: a stable identifier plus an attach function. The
/// adaptive counterpart of <see cref="T:Mibo.Elmish.Sub`1"/>: the attach
/// function receives the post function instead of
/// <c>Dispatch&lt;'Msg&gt;</c>, so subscription events never run handlers
/// directly — they post work, handled on the owner thread at the next post
/// drain, in order.
/// </summary>
[<Struct>]
type AdaptiveSub = {
  /// <summary>Stable key the runtime uses to diff subscriptions across steps.</summary>
  Id: SubId

  /// <summary>
  /// Attaches the subscription and returns a disposable that detaches it. The
  /// <c>post</c> argument is the same-step lane's entry point
  /// (<see cref="M:Mibo.Adaptive.IntentQueue.post"/>): event callbacks
  /// (possibly on foreign threads) may only enqueue work for the next post
  /// drain — confinement is preserved by construction.
  /// </summary>
  Attach: ((unit -> unit) -> unit) -> IDisposable
}

/// <summary>Functions for composing <see cref="T:Mibo.Adaptive.AdaptiveSub"/> values.</summary>
module AdaptiveSub =

  /// <summary>
  /// Prefixes the subscription's <see cref="T:Mibo.Elmish.SubId"/> with a
  /// namespace, for parent-child composition — the adaptive counterpart of
  /// <c>Sub.map</c> without the message mapping. Delegates to
  /// <see cref="M:Mibo.Elmish.SubId.prefix"/>.
  /// </summary>
  let inline prefix (prefix: string) (sub: AdaptiveSub) : AdaptiveSub = {
    sub with
        Id = SubId.prefix prefix sub.Id
  }

/// <summary>
/// An adaptive program: the complete description of an adaptive game.
/// </summary>
/// <remarks>
/// <para>
/// This is the adaptive counterpart of <see cref="T:Mibo.Elmish.Program`2"/>,
/// with the message/command machinery removed. Changeable roots hold the state,
/// derived projections compose it, and the runner forces the frame's
/// projections at the end of every step. There is no <c>'Msg</c> and no
/// <c>Cmd</c> — handlers write roots directly. Deferred work and subscriptions
/// have adaptive counterparts: systems post <c>unit -> unit</c> work through
/// <see cref="P:Mibo.Adaptive.AdaptiveContext.Intents"/> — post (after this
/// step's <c>Update</c>), postNextFrame (top of the next step), or
/// postTask/postAsync (background work whose completion returns via post) —
/// and external events arrive through
/// <see cref="T:Mibo.Adaptive.AdaptiveSub"/> subscriptions whose callbacks
/// post work.
/// </para>
/// <para>
/// The two contexts are split by phase: <c>Init</c> and the subscription
/// projection receive the queue-less
/// <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/> (roots and services
/// only), while <c>Update</c> receives the full
/// <see cref="T:Mibo.Adaptive.AdaptiveContext"/> with the intent queue — the
/// force phase cannot enqueue by construction.
/// </para>
/// <para>
/// The <c>Init</c>/<c>Update</c>/<c>Observers</c>/<c>Subscriptions</c> slots
/// are the dependency graph (the simulation). The <c>Config</c>/
/// <c>Renderers</c>/<c>ServiceRegistrations</c>/<c>AssetsBasePath</c> slots are
/// host configuration (the presentation) consumed by the windowed hosts. The
/// headless runner consumes only the simulation slots plus <c>FixedStep</c>.
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
  /// Receives the queue-less <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/>:
  /// graph building must not reach the intent queue. Returns the
  /// <see cref="T:Mibo.Adaptive.AdaptiveInit`1"/> — the frame builder plus the
  /// disposables released when the runner is disposed.
  /// </summary>
  Init: AdaptiveFrameContext -> AdaptiveInit<'Frame>

  /// <summary>
  /// Optional per-frame phase. Runs after the time root is written and before
  /// the frame is forced. Reads projections, writes roots, posts intents
  /// through the full <see cref="T:Mibo.Adaptive.AdaptiveContext"/>. Under
  /// fixed-step it runs once per fixed step, each followed by the post drain.
  /// </summary>
  Update: AdaptiveContext -> GameTime -> unit

  /// <summary>Observer factories for receiving the forced frame each step.</summary>
  Observers: (unit -> IObserver<struct (GameContext * 'Frame * GameTime)>) list

  /// <summary>
  /// Optional subscription projection: an <c>amap</c> keyed by
  /// <see cref="T:Mibo.Elmish.SubId"/> over
  /// <see cref="T:Mibo.Adaptive.AdaptiveSub"/> specs, built from the
  /// queue-less <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/>. The runner
  /// forces it once per step — incrementally, with no work when no dependency
  /// moved — and diffs it by key against its attached table to start, keep,
  /// and stop subscriptions. Wired via
  /// <see cref="M:Mibo.Adaptive.AdaptiveProgram.withSubscriptions"/>.
  /// </summary>
  Subscriptions: (AdaptiveFrameContext -> amap<SubId, AdaptiveSub>) voption

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
    ([<InlineIfLambda>] onNext: struct (GameContext * 'Frame * GameTime) -> unit)
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
  /// <param name="init">Builds the graph and returns the <see cref="T:Mibo.Adaptive.AdaptiveInit`1"/>. Receives the queue-less <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/> — no deferred work.</param>
  /// <param name="update">Per-frame phase: reads projections, writes roots, posts intents. Receives the full <see cref="T:Mibo.Adaptive.AdaptiveContext"/>.</param>
  let inline mkProgram
    ([<InlineIfLambda>] init: AdaptiveFrameContext -> AdaptiveInit<'Frame>)
    ([<InlineIfLambda>] update: AdaptiveContext -> GameTime -> unit)
    : AdaptiveProgram<'Frame> =
    {
      Init = init
      Update = update
      Observers = []
      Subscriptions = ValueNone
      Config = []
      Renderers = []
      ServiceRegistrations = []
      AssetsBasePath = ValueNone
      FixedStep = ValueNone
      HasInput = false
    }

  /// <summary>Sets the per-frame phase of the program.</summary>
  let inline withUpdate
    ([<InlineIfLambda>] update: AdaptiveContext -> GameTime -> unit)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    { program with Update = update }

  /// <summary>
  /// Adds an observer factory to the program. Observers receive the forced
  /// frame each step, in registration order.
  /// </summary>
  let inline withObserver
    ([<InlineIfLambda>] factory:
      unit -> IObserver<struct (GameContext * 'Frame * GameTime)>)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          Observers = factory :: program.Observers
    }

  /// <summary>
  /// Sets the program's subscription projection. The runner forces the map
  /// once per step (incremental — no work when no dependency moved), diffs it
  /// by <see cref="T:Mibo.Elmish.SubId"/> against its attached table, and
  /// starts new, keeps matching, and stops vanished subscriptions. The
  /// projection receives the queue-less
  /// <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/>. The adaptive
  /// counterpart of <see cref="M:Mibo.Elmish.Program.withSubscription"/>.
  /// </summary>
  /// <param name="subscribe">Builds the subscription map from the context.</param>
  /// <param name="program">The program to configure.</param>
  let inline withSubscriptions
    ([<InlineIfLambda>] subscribe:
      AdaptiveFrameContext -> amap<SubId, AdaptiveSub>)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          Subscriptions = ValueSome subscribe
    }

  /// <summary>
  /// Configure game settings (resolution, title, framerate). The callback
  /// receives the current <see cref="T:Mibo.Elmish.GameConfig"/> and returns a
  /// modified copy.
  /// </summary>
  let inline withConfig
    ([<InlineIfLambda>] configure: GameConfig -> GameConfig)
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
  let inline withRenderer
    ([<InlineIfLambda>] factory: unit -> IRenderer<'Frame>)
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
  let inline withServiceRegistration
    ([<InlineIfLambda>] register: GameContext -> unit)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    {
      program with
          ServiceRegistrations = register :: program.ServiceRegistrations
    }

  /// <summary>
  /// Configures a base path for asset loading.
  /// </summary>
  let inline withAssetsBasePath
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
  /// <c>Step</c>, running <c>Update</c> once per step (each followed by the
  /// post drain) and forcing the frame once at the end.
  /// </summary>
  let inline withFixedStep
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
  let inline withInput
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    { program with HasInput = true }
