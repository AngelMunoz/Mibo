namespace Mibo.Adaptive

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Mibo.Elmish

/// <summary>
/// The intent queue of an adaptive program: the single place to post
/// <c>unit -> unit</c> work during <c>Update</c>. Intents are work items deferred
/// to a moment the runtime owns, exposed as moment-named entry points:
/// <see cref="M:Mibo.Adaptive.IntentQueue.post"/> (later in this step),
/// <see cref="M:Mibo.Adaptive.IntentQueue.postNextFrame"/> (top of the next
/// step), and <see cref="M:Mibo.Adaptive.IntentQueue.postTask"/> /
/// <see cref="M:Mibo.Adaptive.IntentQueue.postAsync"/> (background work whose
/// completion returns via <c>post</c>). The adaptive counterpart of <c>Cmd</c>
/// — closures capture the handler, so no message type is needed.
/// </summary>
/// <remarks>
/// All three lanes are multi-producer, single-consumer queues: a
/// <see cref="T:System.Collections.Concurrent.ConcurrentQueue`1"/> for
/// producers, drained by the single consumer (the runner) on the
/// owner thread. The post drain runs until empty — work posted during the
/// drain runs in the same drain, in post order, mirroring MVU's
/// <c>DispatchMode.Immediate</c> semantics; work that posts more work is the
/// user's responsibility, exactly as message cycles are in MVU. The
/// pre-step lane holds subscription events and drains once per <c>Step</c>
/// at the boundary, before <c>Update</c>; the next-frame lane also drains
/// once per <c>Step</c> at the boundary, never per sub-step.
/// Allocation: one queue node per posted work item — acceptable on the cold
/// path, zero on steps with no posted work.
/// </remarks>
type IntentQueue() =
  let posted = ConcurrentQueue<unit -> unit>()
  let preStep = ConcurrentQueue<unit -> unit>()
  let nextFrame = ConcurrentQueue<unit -> unit>()

  /// <summary>
  /// Defers <c>work</c> to the next post drain: it runs on the owner thread
  /// after the next <c>Update</c> (after each sub-step's <c>Update</c> under
  /// fixed-step), in post order, drained until empty, before the frame is
  /// forced. Cross-system reactions, foreign-thread posts, and async
  /// completions go here. Safe to call from any thread; the work itself must
  /// only run owner-thread legal writes (plain <c>Set</c> calls). The
  /// adaptive counterpart of <c>Cmd.ofMsg</c>.
  /// </summary>
  /// <param name="work">The work to run at the post drain.</param>
  member _.post(work: unit -> unit) = posted.Enqueue work

  /// <summary>
  /// Posts work for the pre-step drain: it runs on the owner thread at the
  /// next step's boundary, after cross-thread posts are applied and before
  /// <c>Update</c>. Used by the subscription machinery — subscription events
  /// are handled before the step's <c>Update</c> reads state.
  /// </summary>
  /// <param name="work">The work to run at the next step's boundary.</param>
  member internal _.postPreStep(work: unit -> unit) = preStep.Enqueue work

  /// <summary>
  /// Defers <c>work</c> to the top of the NEXT
  /// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>: it runs on the
  /// owner thread at the frame boundary, after cross-thread posts are applied
  /// (<c>Posting.pump</c>) and before the time root is written. The adaptive
  /// counterpart of <c>Cmd.deferNextFrame</c> — the lane exists for work that
  /// must not run in the step that posted it. Safe to call
  /// from any thread.
  /// </summary>
  /// <param name="work">The work to run at the next step's boundary.</param>
  member _.postNextFrame(work: unit -> unit) = nextFrame.Enqueue work

  /// <summary>
  /// Defers a background task to this queue: the starter runs on the
  /// owner thread at the next post drain and hands the work to the
  /// thread pool — <c>work</c> (the task creation call) and the task itself
  /// run off the owner thread — and the completion (<c>ofSuccess</c> or
  /// <c>ofError</c>) is posted to the same lane and runs on the owner thread
  /// at a later post drain, where root writes are legal. The runtime handles
  /// the thread handoff for you — you never post or pump directly. The
  /// adaptive counterpart of <c>Cmd.ofTask</c>.
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
  /// Defers an F# async workflow to this queue: the starter runs on the
  /// owner thread at the next post drain and starts the workflow on the
  /// thread pool — the workflow body runs off the owner thread — and the
  /// completion (<c>ofSuccess</c> or <c>ofError</c>) is posted to the same
  /// lane and runs on the owner thread at a later post drain, where root
  /// writes are legal. The runtime handles the thread handoff for you — you
  /// never post or pump directly. The adaptive counterpart of <c>Cmd.ofAsync</c>.
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
  /// Runs every pending work item in post order on the calling (owner)
  /// thread until the lane is empty: work posted during the drain (from
  /// any thread) runs in the same drain, in post order. No termination bound.
  /// </summary>
  member internal _.Drain() =
    let mutable next = Unchecked.defaultof<unit -> unit>

    while posted.TryDequeue(&next) do
      next()

  /// <summary>
  /// Runs the next-frame lane on the calling (owner) thread: everything queued
  /// at the top of the step, in post order. Work posted during this drain
  /// also runs in the same drain (same semantics as the post drain).
  /// </summary>
  member internal _.DrainNextFrame() =
    let mutable next = Unchecked.defaultof<unit -> unit>

    while nextFrame.TryDequeue(&next) do
      next()

  /// <summary>
  /// Runs the pre-step lane on the calling (owner) thread: subscription
  /// events queued at the step boundary, in post order. Work posted during
  /// this drain also runs in the same drain.
  /// </summary>
  member internal _.DrainPreStep() =
    let mutable next = Unchecked.defaultof<unit -> unit>

    while preStep.TryDequeue(&next) do
      next()

/// <summary>
/// The graph-building context handed to an
/// <see cref="T:Mibo.Adaptive.AdaptiveProgram`1"/> <c>Init</c> function and to
/// the subscription projection
/// (<see cref="M:Mibo.Adaptive.AdaptiveInit.withSubscriptions"/>).
/// </summary>
/// <remarks>
/// The context exposes the framework-owned roots: the time cell, written by
/// the runner at the start of every <c>Step</c> (once per fixed sub-step),
/// the exit-request cell, read by the runner to decide whether to stop, plus
/// the <see cref="T:Mibo.Elmish.GameContext"/> holding the window dimensions
/// and the registered services. The program reads these cells and derives
/// its projections from them like any other root; only <c>ExitRequested</c>
/// is written by the program. The context deliberately does NOT expose the
/// intent queue: the frame builder and init/projection construction must not
/// be able to defer work. Only the <c>Update</c> phase can reach the queue,
/// via <see cref="T:Mibo.Adaptive.AdaptiveContext"/>.
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
/// intents are <c>unit -> unit</c> work items deferred to a
/// framework-owned moment (after <c>Update</c>, at the top of the next step,
/// or as background work whose completion returns via <c>post</c>).
/// </summary>
/// <remarks>
/// The queue is reachable only here, where posting is legal: <c>Update</c>
/// reacts to what it read and defers work; the frame builder and projection
/// construction see only
/// <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/>, so the force phase
/// cannot enqueue work — the design makes it impossible, no runtime checks.
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

/// <summary>
/// The post surface handed to an <see cref="T:Mibo.Adaptive.AdaptiveSub"/>
/// attach function: <see cref="M:Mibo.Adaptive.SubPosting.Post"/> queues work
/// for the pre-step drain, which runs on the owner thread at the next step's
/// boundary, before <c>Update</c>. Framework subscriptions also queue work
/// for the post drain (after <c>Update</c>, before the frame is forced)
/// through an internal member; that moment is not part of the public
/// contract.
/// </summary>
type SubPosting
  internal ([<InlineIfLambda>]post: (unit -> unit) -> unit, [<InlineIfLambda>]afterUpdate: (unit -> unit) -> unit) =

  /// <summary>
  /// Queues work for the pre-step drain: it runs on the owner thread at the
  /// next step's boundary, before <c>Update</c>, in post order. Subscription
  /// events go here; the state they write is in place before the step's
  /// <c>Update</c> reads it.
  /// </summary>
  /// <param name="work">The work to run at the next step's boundary.</param>
  member _.Post(work: unit -> unit) = post work

  /// <summary>
  /// Queues work for the post drain: it runs on the owner thread after the
  /// next step's <c>Update</c> (after each fixed sub-step's <c>Update</c>),
  /// before the frame is forced. The framework's input mapper clears its
  /// consumed edges at this moment.
  /// </summary>
  /// <param name="work">The work to run at the next post drain.</param>
  member internal _.AfterUpdate(work: unit -> unit) = afterUpdate work

/// <summary>
/// A subscription spec: a stable identifier plus an attach function. The
/// adaptive counterpart of <see cref="T:Mibo.Elmish.Sub`1"/>: the attach
/// function receives the <see cref="T:Mibo.Adaptive.SubPosting"/> surface
/// instead of <c>Dispatch&lt;'Msg&gt;</c>, so subscription events never run
/// handlers directly — they post work, handled on the owner thread at the
/// next step's boundary, before <c>Update</c>, in order.
/// </summary>
[<Struct>]
type AdaptiveSub = {
  /// <summary>Stable key the runtime uses to diff subscriptions across steps.</summary>
  Id: SubId

  /// <summary>
  /// Attaches the subscription and returns a disposable that detaches it. The
  /// <paramref name="posting"/> argument is the
  /// <see cref="T:Mibo.Adaptive.SubPosting"/> surface: its
  /// <see cref="M:Mibo.Adaptive.SubPosting.Post"/> queues work for the
  /// pre-step drain, and the runner handles it on the owner thread at the
  /// next step's boundary, before <c>Update</c> — a foreign-thread callback
  /// can never run game code directly. Prefer the builders in the
  /// <see cref="T:Mibo.Adaptive.AdaptiveSub"/> module
  /// (<c>ofObservable</c>, <c>ofTimer</c>) over writing <c>Attach</c> by
  /// hand.
  /// </summary>
  Attach: SubPosting -> IDisposable
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
  /// Builds a subscription from an <see cref="T:System.IObservable`1"/>: the
  /// handler receives the <see cref="T:Mibo.Adaptive.SubPosting"/> surface and
  /// each value, and queues work for the pre-step drain. The runner handles
  /// the work on the owner thread at the next step's boundary, before
  /// <c>Update</c>, so a callback thread never runs game code directly.
  /// </summary>
  /// <param name="id">Stable key the runtime uses to diff subscriptions across steps.</param>
  /// <param name="source">The observable to subscribe to.</param>
  /// <param name="handler">Receives the posting surface and each value.</param>
  let inline ofObservable
    (id: SubId)
    (source: IObservable<'T>)
    ([<InlineIfLambda>]handler: SubPosting -> 'T -> unit)
    : AdaptiveSub =
    {
      Id = id
      Attach = fun posting -> source.Subscribe(handler posting)
    }

  /// <summary>
  /// Builds a subscription that ticks on a timer: the handler receives the
  /// <see cref="T:Mibo.Adaptive.SubPosting"/> surface once per tick and
  /// queues work for the pre-step drain. The timer ticks on a thread-pool
  /// thread; only the queued work's drain runs on the owner thread.
  /// </summary>
  /// <param name="id">Stable key the runtime uses to diff subscriptions across steps.</param>
  /// <param name="interval">Tick interval.</param>
  /// <param name="tick">Receives the posting surface once per tick.</param>
  let inline ofTimer
    (id: SubId)
    (interval: TimeSpan)
    ([<InlineIfLambda>]tick: SubPosting -> unit)
    : AdaptiveSub =
    {
      Id = id
      Attach =
        fun posting ->
          let timer = new Timers.Timer(interval.TotalMilliseconds)
          timer.Elapsed.Add(fun _ -> tick posting)
          timer.Start()

          { new IDisposable with
              member _.Dispose() =
                timer.Stop()
                timer.Dispose()
          }
    }

/// <summary>The result of building an adaptive program's graph.</summary>
[<Struct>]
type AdaptiveInit<'Frame> = {
  /// <summary>
  /// The frame builder: forces the frame's output projections (recomputing each
  /// exactly once if a dependency moved, and not at all otherwise) and packs
  /// them into <c>'Frame</c>.
  /// </summary>
  FrameBuilder: unit -> 'Frame

  /// <summary>
  /// The subscription projection, built in <c>Init</c> alongside the frame
  /// builder — both capture the world from the same place. The runner calls
  /// it once per step and compares the returned map against its attached
  /// table to start, keep, and stop subscriptions. See
  /// <see cref="M:Mibo.Adaptive.AdaptiveInit.withSubscriptions"/> for the
  /// stable-map requirement.
  /// </summary>
  Subscriptions: AdaptiveFrameContext -> amap<SubId, AdaptiveSub>

  /// <summary>Disposables released when the runner is disposed.</summary>
  Disposables: IDisposable list
}

/// <summary>Helpers for building an <see cref="T:Mibo.Adaptive.AdaptiveInit`1"/>.</summary>
/// <remarks>
/// These keep the <c>Disposables</c> list and the default subscription
/// projection hidden: <see cref="M:Mibo.Adaptive.AdaptiveInit.ofFrameBuilder"/>
/// defaults them (empty subscriptions, no disposables), and
/// <see cref="M:Mibo.Adaptive.AdaptiveInit.withSubscriptions"/> /
/// <see cref="M:Mibo.Adaptive.AdaptiveInit.withDisposables"/> /
/// <see cref="M:Mibo.Adaptive.AdaptiveInit.withDisposable"/> replace or append
/// to them. Callers never write <c>Disposables = []</c> by hand.
/// </remarks>
module AdaptiveInit =

  /// <summary>
  /// Creates an init from a frame builder with no subscriptions and no
  /// disposables.
  /// </summary>
  /// <param name="frameBuilder">Forces the frame's output projections and packs them into <c>'Frame</c>.</param>
  let inline ofFrameBuilder
    ([<InlineIfLambda>] frameBuilder: unit -> 'Frame)
    : AdaptiveInit<'Frame> =
    // One shared empty map: a fresh AMap.empty per call would defeat the
    // runner's version check and allocate every step.
    let emptySubs: AdaptiveFrameContext -> amap<SubId, AdaptiveSub> =
      let empty = AMap.empty
      fun _ -> empty

    {
      FrameBuilder = frameBuilder
      Subscriptions = emptySubs
      Disposables = []
    }

  /// <summary>
  /// Sets the subscription projection. The runner reads the map once per step
  /// (skipping the read when nothing changed), compares it by
  /// <see cref="T:Mibo.Elmish.SubId"/> against its attached table, and starts
  /// new, keeps matching, and stops vanished subscriptions. The adaptive
  /// counterpart of <see cref="M:Mibo.Elmish.Program.withSubscription"/>.
  /// </summary>
  /// <remarks>
  /// The projection must return a stable map: build it once and return the
  /// same instance. The runner checks the map's version every step and skips
  /// the diff when nothing changed.
  /// If you need subscriptions that change with game state, use
  /// <c>AMap.custom</c> and describe the changes yourself — do not build a
  /// fresh map from adaptive values (for example <c>AMap.ofAVal</c>) inside
  /// the projection, that would create a new graph every step.
  /// Subscription work feeds the step cycle: it runs at the next step's
  /// boundary, before <c>Update</c>. Do not modify game state from it beyond
  /// what the step cycle expects.
  /// </remarks>
  /// <param name="subscribe">Builds the subscription map from the context.</param>
  /// <param name="init">The init to configure.</param>
  let inline withSubscriptions
    ([<InlineIfLambda>] subscribe:
      AdaptiveFrameContext -> amap<SubId, AdaptiveSub>)
    (init: AdaptiveInit<'Frame>)
    : AdaptiveInit<'Frame> =
    { init with Subscriptions = subscribe }

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
/// The two contexts are split by phase: <c>Init</c> — and the subscription
/// projection it returns — receive the queue-less
/// <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/> (roots and services
/// only), while <c>Update</c> receives the full
/// <see cref="T:Mibo.Adaptive.AdaptiveContext"/> with the intent queue — so
/// the force phase cannot enqueue work.
/// </para>
/// <para>
/// The <c>Init</c>/<c>Update</c>/<c>Observers</c> slots
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
[<RequireQualifiedAccess>]
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

namespace Mibo.Input

open System
open Mibo.Elmish
open Mibo.Adaptive

/// <summary>
/// Builds the adaptive <see cref="T:Mibo.Input.ActionState`1"/> subscription
/// shared by the backend input mappers.
/// </summary>
module AdaptiveInput =

  /// <summary>
  /// Builds an <see cref="T:Mibo.Adaptive.AdaptiveSub"/> from a function that
  /// subscribes to input deltas. Every state built from an input delta is
  /// written into the root at the step boundary, before <c>Update</c>, with
  /// the edges merged (see
  /// <see cref="M:Mibo.Input.ActionState.mergeEdges"/>); the consumed edges
  /// are cleared after <c>Update</c>, before the frame is forced. So
  /// <c>Update</c> reads each edge exactly once, and the next step starts
  /// from an empty edge set. The after-update clear goes through an internal
  /// member of the posting surface; it is not part of the public contract.
  /// </summary>
  /// <param name="attachDeltas">
  /// Subscribes to the input deltas and invokes the callback with each newly
  /// built <c>ActionState</c>; the returned disposable detaches.
  /// </param>
  /// <param name="actions">The root the merged states are written into.</param>
  let subscribe
    (attachDeltas: (ActionState<'Action> -> unit) -> IDisposable)
    (actions: cval<ActionState<'Action>>)
    : AdaptiveSub =
    let attach(posting: SubPosting) =
      // One clear per drain cycle: several events in one frame queue several
      // merged states, but only the first queues the clear (the flag resets
      // when the clear runs). A build without edges (a mouse move) queues
      // nothing.
      let mutable clearPending = false

      attachDeltas(fun state ->
        posting.Post(fun () ->
          let merged = ActionState.mergeEdges (actions.GetValue()) state
          actions.Set merged

          if
            (not merged.Started.IsEmpty || not merged.Released.IsEmpty)
            && not clearPending
          then
            clearPending <- true

            posting.AfterUpdate(fun () ->
              actions.Set(ActionState.nextFrame(actions.GetValue()))
              clearPending <- false)))

    {
      Id = SubId.ofString "Mibo/Input/InputMapper/subscribeAdaptive"
      Attach = attach
    }
