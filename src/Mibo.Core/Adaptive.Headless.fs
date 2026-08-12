namespace Mibo.Adaptive

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open Mibo.Elmish

/// <summary>
/// One step's worth of simulation produced by
/// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.RunAsync"/>: the game time
/// and the forced frame of the step.
/// </summary>
[<Struct>]
type StepOutcome<'Frame> = {
  /// <summary>The total and per-step elapsed time of the step.</summary>
  GameTime: GameTime

  /// <summary>The forced frame of the step.</summary>
  Frame: 'Frame
}

/// <summary>
/// Runs an adaptive program with explicit frame stepping.
/// </summary>
/// <remarks>
/// <para>
/// The runner owns the frame boundary. Each
/// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>:
/// (1) applies cross-thread posts and drains the next-frame buffer at the
/// frame boundary, then diffs the program's subscription projection against
/// the attached table,
/// (2) writes the current game time into the time root (once, or once per
/// fixed step when <see cref="F:Mibo.Adaptive.AdaptiveProgram`1.FixedStep"/> is set),
/// (3) runs the program's <c>Update</c> phase,
/// (4) drains the intent queue until empty — posted work runs in post
/// order, and thunks posted during the drain run in the same pass (after
/// each sub-step's <c>Update</c> under fixed-step),
/// (5) forces the frame builder — the frame's projections recompute exactly
/// once if any of their dependencies moved, and not at all otherwise — and
/// (6) notifies the observers with the forced frame. Draw code reads the
/// returned frame: reads are O(1) until the next write.
/// </para>
/// <para>
/// The adaptive graph is confined to the thread that creates it (AdaptiveSlop's
/// owner-thread model: no locks, allocation-free steady state). The runner
/// creates the graph lazily on the first user — the thread that first calls
/// <c>Step</c>/<c>StepN</c>/<c>StepUntil</c>/<c>Run</c>, or the dedicated game
/// thread of <c>RunAsync</c>. All graph work then happens on that thread.
/// Cross-thread writes go through <c>cval.Post</c> — drained by
/// <c>Posting.pump</c> at the start of every step — and external work goes
/// through <see cref="P:Mibo.Adaptive.AdaptiveContext.Intents"/> (the post
/// lane is thread-safe, drained at the next post drain after <c>Update</c>).
/// </para>
/// </remarks>
type AdaptiveHeadless<'Frame>
  (
    program: AdaptiveProgram<'Frame>,
    ?width: int,
    ?height: int,
    ?context: GameContext
  ) =

  let w = defaultArg width 800
  let h = defaultArg height 600

  let observers =
    ResizeArray<IObserver<struct (GameContext * 'Frame * GameTime)>>()

  let disposables = ResizeArray<IDisposable>()

  let mutable gameContext = Unchecked.defaultof<GameContext>
  let mutable exitCell = Unchecked.defaultof<cval<bool>>
  let mutable frameCtx = Unchecked.defaultof<AdaptiveFrameContext>
  let mutable ctx = Unchecked.defaultof<AdaptiveContext>
  let mutable initialized = false

  // The framework-owned intent queue, created eagerly: external
  // injection (Post) must be safe before the graph exists, and the queue has
  // no thread affinity — only its drains do (owner thread, inside Step).
  let intents = IntentQueue()

  // Attached subscription disposables, keyed by SubId. The runtime diffs the
  // program's subscription projection against this table every step: new key
  // → attach, missing key → detach, surviving key → keep (identity is the key).
  let attachedSubs = Dictionary<SubId, IDisposable>()

  let mutable fixedAccSeconds = 0.0f

  let mutable gameTime = {
    TotalTime = TimeSpan.Zero
    ElapsedGameTime = TimeSpan.Zero
  }

  let mutable frame = Unchecked.defaultof<'Frame>
  let mutable frameBuilder: unit -> 'Frame = Unchecked.defaultof<unit -> 'Frame>
  // Guards RunAsync: at most one enumerator may drive the runner at a time.
  let mutable runAsyncActive = 0

  /// Create the graph on the current thread and force the first frame.
  /// Runs once: on the first user of the runner (a step or the RunAsync game thread).
  let ensureInitialized() =
    if not initialized then
      gameContext <- defaultArg context (GameContext.create(w, h))

      let timeCell =
        CVal.create {
          TotalTime = TimeSpan.Zero
          ElapsedGameTime = TimeSpan.Zero
        }

      exitCell <- CVal.create false

      // Phase reachability by type: Init and the subscription projection get
      // the queue-less frame context; only Update gets the full context with
      // the intent queue.
      frameCtx <- AdaptiveFrameContext(gameContext, timeCell, exitCell)

      ctx <- AdaptiveContext(frameCtx, intents)

      let init = program.Init frameCtx
      frameBuilder <- init.FrameBuilder
      disposables.AddRange init.Disposables

      // Force the first frame so Frame is never default after initialization.
      frame <- frameBuilder()

      // Observers are stored by prepending (see withObserver), so reverse to
      // initialize and notify in registration order — matching HeadlessRunner.
      for factory in List.rev program.Observers do
        observers.Add(factory())

      initialized <- true


  /// Diff the program's subscription projection against the attached table.
  /// Runs on the owner thread at the frame boundary, after the next-frame lane
  /// drain and before the time root write. The projection is an amap
  /// keyed by SubId: reading it is incremental — no work when no dependency
  /// moved — and the diff table does the real attach/detach work.
  let diffSubscriptions() =
    match program.Subscriptions with
    | ValueNone ->
      // No projection: detach anything left over (the projection is fixed at
      // program build, so this is a safety net rather than a live path).
      for KeyValueV(_, d) in attachedSubs do
        d.Dispose()

      attachedSubs.Clear()
    | ValueSome subscribe ->
      // Transient view of the current map content (AMap.getValue, not
      // AMap.force): zero allocation on clean steps — only the incremental
      // recompute runs when a dependency moved. Used synchronously inside
      // this step, so the transient view's validity window is respected.
      let current = subscribe frameCtx |> AMap.getValue

      // New key → attach, handing the subscription the post function:
      // events never run handlers directly, they post work for the next
      // post drain, on the owner thread, in order.
      for KeyValueV(id, sub) in current do
        if not(attachedSubs.ContainsKey id) then
          attachedSubs[id] <- sub.Attach intents.post

      // Missing key → detach. Surviving keys are kept as-is: the key is the
      // identity, never re-attach on a fresh closure value.
      if attachedSubs.Count > 0 then
        let stale = ResizeArray<SubId>()

        for KeyValueV(id, _) in attachedSubs do
          if not(current.ContainsKey id) then
            stale.Add id

        for id in stale do

          attachedSubs
          |> Dictionary.tryGetValue id
          |> ValueOption.iter(fun d ->
            d.Dispose()
            attachedSubs.Remove id |> ignore)

  /// <summary>Whether the runner has received an exit request.</summary>
  member _.ShouldQuit = if initialized then AVal.getValue exitCell else false

  /// <summary>Total elapsed virtual time.</summary>
  member _.GameTime = gameTime

  /// <summary>The last forced frame. Valid after the first <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>.</summary>
  member _.Frame = frame

  /// <summary>
  /// Advance the program by one frame and return the forced frame.
  /// </summary>
  /// <param name="elapsed">Frame delta (e.g. <c>TimeSpan.FromMilliseconds(16)</c> for 60fps). Negative values are clamped to zero.</param>
  /// <remarks>
  /// <para>
  /// When <see cref="F:Mibo.Adaptive.AdaptiveProgram`1.FixedStep"/> is set, the
  /// frame delta is converted into zero or more fixed-size steps: the time root
  /// is written, <c>Update</c> runs, and the intent queue drains until empty
  /// once per step. The frame is forced once at the end regardless, so
  /// intermediate steps are integrated but not observed by the frame. This
  /// mirrors the MVU <c>TickFrame</c> fixed-step loop, calling <c>Update</c>
  /// instead of dispatching a mapped message. The next-frame lane drains once
  /// per Step at the boundary — never per sub-step.
  /// </para>
  /// <para>
  /// This mutates the runner's internal state (time root, program roots, frame).
  /// Do not mix <c>Step</c>/<c>StepN</c>/<c>StepUntil</c> with <c>Run</c>/<c>RunAsync</c> on the same runner
  /// — they all advance the simulation and using them together will produce simulation corruption.
  /// </para>
  /// </remarks>
  member this.Step(elapsed: TimeSpan) : 'Frame =
    let f = this.StepCore elapsed

    // Observers (drain point 6): notify with the forced frame, after the step
    // completes. Post-quit steps are no-ops (StepCore returned the cached
    // frame early), so they do not notify.
    if not this.ShouldQuit then
      for i = 0 to observers.Count - 1 do
        observers[i].OnNext(gameContext, frame, gameTime)

    f

  /// The shared step engine: applies posts, drains the next-frame lane, diffs
  /// the subscription projection, writes the time root and runs <c>Update</c>
  /// (once, or once per fixed step, each followed by the post drain), and
  /// forces the frame. Returns the forced frame; the caller decides its
  /// disposition — Step notifies the observers, RunAsync packages it for the
  /// consumer.
  member private _.StepCore(elapsed: TimeSpan) : 'Frame =
    ensureInitialized()

    if AVal.getValue exitCell then
      frame
    else

      // Note: ticks comparison, not `elapsed < TimeSpan.Zero` — F# generic
      // comparison boxes both operands (48 bytes/call on the frame path).
      let elapsed = if elapsed.Ticks < 0L then TimeSpan.Zero else elapsed

      // Frame boundary (drain points 1-2 of the step table): apply cross-thread
      // posts (e.g. input-thread writes), then drain the next-frame lane —
      // explicit postNextFrame deferrals, once per Step, before the time write.
      Posting.pump()
      intents.DrainNextFrame()

      // Subscription lifecycle: diff the program's subscription projection
      // against the attached table.
      diffSubscriptions()

      match program.FixedStep with
      | ValueNone ->
        gameTime <- {
          TotalTime = gameTime.TotalTime + elapsed
          ElapsedGameTime = elapsed
        }

        // The time root is the framework's write into the graph.
        ctx.Time.Set gameTime

        // The imperative phase: reads projections, writes roots, posts
        // intents.
        program.Update ctx gameTime

        // Post drain (drain point 4): posted work runs until empty, in post
        // order — thunks posted during the drain (from any thread) run in the
        // same pass. Work posted during Update reacts within THIS step, before
        // the force; foreign-thread posts land at the next post drain.
        intents.Drain()

      | ValueSome cfg ->
        let maxFrame = cfg.MaxFrameSeconds |> ValueOption.defaultValue 0.25f
        let deltaSeconds = float32 elapsed.TotalSeconds

        let struct (acc2, steps, _dropped) =
          FixedStep.compute
            cfg.StepSeconds
            cfg.MaxStepsPerFrame
            maxFrame
            fixedAccSeconds
            deltaSeconds

        fixedAccSeconds <- acc2

        let stepElapsed = TimeSpan.FromSeconds(float cfg.StepSeconds)

        for _i = 1 to steps do
          gameTime <- {
            TotalTime = gameTime.TotalTime + stepElapsed
            ElapsedGameTime = stepElapsed
          }

          ctx.Time.Set gameTime
          program.Update ctx gameTime

          // Post drain after each sub-step's Update: every sub-step reads
          // settled state. Drained until empty.
          intents.Drain()

      // The force phase (drain point 5): recompute the frame's projections
      // exactly once (not at all if none of their dependencies moved) and pack
      // the struct.
      frame <- frameBuilder()
      frame

  /// <summary>Advance the simulation by N frames and return the last forced frame.</summary>
  /// <param name="count">Number of frames to run.</param>
  /// <param name="elapsed">Frame delta per step.</param>
  /// <remarks>
  /// This mutates the runner's internal state. Do not mix with <c>Run</c>/<c>RunAsync</c>
  /// on the same runner — they all advance the simulation and using them together
  /// will produce simulation corruption.
  /// </remarks>
  member this.StepN(count: int, elapsed: TimeSpan) : 'Frame =
    let mutable last = frame

    for _ = 1 to count do
      last <- this.Step elapsed

    last

  /// <summary>Advance until a predicate on the forced frame returns true.</summary>
  /// <param name="predicate">Condition to check after each frame.</param>
  /// <param name="elapsed">Frame delta per step.</param>
  /// <param name="maxFrames">Safety limit to prevent infinite loops.</param>
  /// <returns>True if predicate was met, false if maxFrames was reached.</returns>
  /// <remarks>
  /// This mutates the runner's internal state. Do not mix with <c>Run</c>/<c>RunAsync</c>
  /// on the same runner — they all advance the simulation and using them together
  /// will produce simulation corruption.
  /// </remarks>
  member this.StepUntil
    (predicate: 'Frame -> bool, elapsed: TimeSpan, [<Struct>] ?maxFrames: int)
    =
    // Initialize before the first predicate check: on a fresh runner the frame
    // is still Unchecked.defaultof (null for reference-type frames), so
    // evaluating the predicate against it would read garbage or throw.
    ensureInitialized()

    let max = defaultValueArg maxFrames 10000
    let mutable steps = 0
    let mutable met = predicate frame || this.ShouldQuit

    while steps < max && not met do
      this.Step elapsed |> ignore
      steps <- steps + 1
      met <- predicate frame || this.ShouldQuit

    met

  /// <summary>Run the simulation synchronously, yielding each frame as a sequence.</summary>
  /// <param name="interval">Tick interval (e.g. <c>TimeSpan.FromMilliseconds(16)</c> for 60fps).</param>
  /// <param name="ct">Optional cancellation token to stop the loop early.</param>
  /// <returns>A sequence of <c>(GameTime * 'Frame)</c> snapshots, paced by the interval.</returns>
  /// <remarks>
  /// Uses a spin-wait with <c>Thread.Sleep(1)</c> to pace the loop. This is the standard
  /// pattern for game servers — the <c>Stopwatch</c> controls timing precision while
  /// <c>Sleep</c> yields the CPU between ticks.
  /// <para>
  /// This advances the runner's internal state. Do not mix with <c>Step</c>/<c>StepN</c>/<c>StepUntil</c>
  /// on the same runner — they all advance the simulation and using them together
  /// will produce simulation corruption.
  /// </para>
  /// </remarks>
  member this.Run(interval: TimeSpan, [<Struct>] ?ct: CancellationToken) =
    if interval.Ticks <= 0L then
      invalidArg (nameof interval) "Interval must be greater than zero"

    let ct = defaultValueArg ct CancellationToken.None
    let sw = Stopwatch.StartNew()
    let intervalMs = interval.TotalMilliseconds
    let mutable nextTick = 0.0

    seq {
      while not this.ShouldQuit && not ct.IsCancellationRequested do
        let elapsed = sw.Elapsed.TotalMilliseconds

        if elapsed >= nextTick then
          this.Step interval |> ignore
          nextTick <- nextTick + intervalMs
          struct (this.GameTime, this.Frame)
        else
          Thread.Sleep 1
    }

  /// <summary>
  /// Thread-safe external injection: posts a thunk to the post lane, where it
  /// runs on the owner thread at the next post drain — after the next step's
  /// <c>Update</c> (after each sub-step's <c>Update</c> under fixed-step), in
  /// post order, drained until empty, before the frame is forced. A
  /// convenience for foreign code (tests, network callbacks, AI drivers) that
  /// holds the runner but not the Update context: the same as
  /// <see cref="M:Mibo.Adaptive.IntentQueue.post"/>.
  /// </summary>
  member _.Post(thunk: unit -> unit) = intents.post thunk

  /// <summary>Run the simulation asynchronously, yielding each frame as an async enumerable.</summary>
  /// <param name="interval">Tick interval.</param>
  /// <param name="ct">Optional cancellation token to stop the loop.</param>
  /// <returns>An async sequence of <see cref="T:Mibo.Adaptive.StepOutcome`1"/> snapshots: the game time and the forced frame of each step.</returns>
  /// <remarks>
  /// <para>
  /// The world loop runs on a dedicated background game thread — the graph is
  /// confined to its creating thread, and async continuation threads are not it.
  /// The async enumerable is a consumer of that thread: it receives the
  /// outcomes the loop produces, in order. The <c>for .. in</c> syntax in F# 8+
  /// can iterate over <c>IAsyncEnumerable</c> directly:
  /// <code>
  /// for outcome in world.RunAsync hz30 do
  ///   render outcome.Frame
  /// </code>
  /// The consumer renders the packed frame — it never touches the world's
  /// graph. Background work started with
  /// <see cref="M:Mibo.Adaptive.IntentQueue.postTask`1"/> / <c>postAsync</c>
  /// runs off the world thread, and its completion posts back into the world's
  /// intent queue (thread-safe) and runs at the next post drain on the game
  /// thread.
  /// </para>
  /// <para>
  /// At most one enumerator may consume a runner at a time: a second
  /// <c>GetAsyncEnumerator</c> while one is active throws. An exception on the
  /// game thread (from <c>Init</c>, <c>Update</c>, or a projection) is not
  /// thrown on the background thread — it is stored and rethrown from
  /// <c>MoveNextAsync</c> so the consumer sees a normal error.
  /// </para>
  /// <para>
  /// This advances the runner's internal state. Do not mix with <c>Step</c>/<c>StepN</c>/<c>StepUntil</c>
  /// on the same runner — they all advance the simulation and using them together
  /// will produce simulation corruption.
  /// </para>
  /// </remarks>
  member this.RunAsync(interval: TimeSpan, [<Struct>] ?ct: CancellationToken) =
    if interval.Ticks <= 0L then
      invalidArg (nameof interval) "Interval must be greater than zero"

    let ct = defaultValueArg ct CancellationToken.None

    { new IAsyncEnumerable<StepOutcome<'Frame>> with
        member _.GetAsyncEnumerator(cancellationToken) =
          // One consumer at a time: two enumerators would step the same runner
          // concurrently, racing on gameTime/frame and the fixed-step
          // accumulator. The flag is released in DisposeAsync.
          if Interlocked.CompareExchange(&runAsyncActive, 1, 0) <> 0 then
            invalidOp
              "RunAsync already has an active enumerator on this runner. Only one consumer at a time is supported."

          let linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
              ct,
              cancellationToken
            )

          // The game thread owns the graph (created on first use) and posts the
          // outcomes to this queue; the async enumerator waits on the signal.
          let frames = ConcurrentQueue<StepOutcome<'Frame>>()
          let signal = new SemaphoreSlim(0)
          let mutable current = Unchecked.defaultof<StepOutcome<'Frame>>
          // Set when the game thread dies with an exception. Rethrown from
          // MoveNextAsync so the consumer sees a normal error instead of the
          // process being killed by an unhandled thread exception.
          let mutable gameThreadError: exn = null

          let gameThread =
            Thread(fun () ->
              try
                try
                  ensureInitialized()

                  let sw = Stopwatch.StartNew()
                  let intervalMs = interval.TotalMilliseconds
                  let mutable nextTick = 0.0
                  let mutable running = true

                  while running do
                    if
                      this.ShouldQuit || linkedCts.IsCancellationRequested
                    then
                      running <- false
                    else
                      let elapsed = sw.Elapsed.TotalMilliseconds

                      if elapsed >= nextTick then
                        nextTick <- nextTick + intervalMs
                        let f = this.StepCore interval

                        frames.Enqueue { GameTime = this.GameTime; Frame = f }

                        // Observers (drain point 6): the forced frame of the
                        // step, notified on the game thread. The consumer
                        // receives the same frame through the outcome.
                        for i = 0 to observers.Count - 1 do
                          observers[i].OnNext(gameContext, frame, gameTime)

                        signal.Release() |> ignore
                      else
                        Thread.Sleep 1
                with ex ->
                  // Store and surface from MoveNextAsync; do not let an
                  // unhandled exception on a background thread kill the
                  // process.
                  gameThreadError <- ex
              finally
                // Wake a waiting consumer so it can observe the end of the stream.
                signal.Release() |> ignore)

          gameThread.IsBackground <- true
          gameThread.Start()

          { new IAsyncEnumerator<StepOutcome<'Frame>> with
              member _.Current = current

              member _.MoveNextAsync() =
                ValueTask<bool>(
                  task {
                    try
                      let! _ = signal.WaitAsync linkedCts.Token

                      match gameThreadError with
                      | null ->
                        let mutable item =
                          Unchecked.defaultof<StepOutcome<'Frame>>

                        if frames.TryDequeue(&item) then
                          current <- item
                          return true
                        else
                          return false
                      | ex ->
                        // Rethrow with the original stack trace.
                        ExceptionDispatchInfo.Capture(ex).Throw()
                        return false
                    with :? OperationCanceledException ->
                      return false
                  }
                )

              member _.DisposeAsync() =
                Interlocked.Exchange(&runAsyncActive, 0) |> ignore
                linkedCts.Cancel()
                signal.Release() |> ignore
                linkedCts.Dispose()
                ValueTask()
          }
    }

  /// <summary>Detach all subscriptions, dispose program disposables and observers, and clean up resources.</summary>
  member _.Dispose() =
    for KeyValueV(_, d) in attachedSubs do
      d.Dispose()

    attachedSubs.Clear()

    for i = 0 to disposables.Count - 1 do
      disposables[i].Dispose()

    disposables.Clear()

    for i = 0 to observers.Count - 1 do
      match observers[i] with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    observers.Clear()

  interface IDisposable with
    member this.Dispose() = this.Dispose()
