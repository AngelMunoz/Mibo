namespace Mibo.Adaptive

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open AdaptiveSlop.Core
open Mibo.Elmish

/// <summary>
/// Runs an adaptive program with explicit frame stepping.
/// </summary>
/// <remarks>
/// <para>
/// The runner owns the frame boundary. Each <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>:
/// (1) applies cross-thread posts at the frame boundary,
/// (2) writes the current game time into the time root (once, or once per
/// fixed step when <see cref="F:Mibo.Adaptive.AdaptiveProgram`1.FixedStep"/> is set),
/// (3) runs the program's <c>Update</c> phase,
/// (4) forces the frame builder — the frame's projections recompute exactly
/// once if any of their dependencies moved, and not at all otherwise — and
/// (5) notifies the observers with the forced frame. Draw code reads the
/// returned frame: reads are O(1) until the next write.
/// </para>
/// <para>
/// The adaptive graph is confined to the thread that creates it (AdaptiveSlop's
/// owner-thread model: no locks, allocation-free steady state). The runner
/// creates the graph lazily on the first user — the thread that first calls
/// <c>Step</c>/<c>StepN</c>/<c>StepUntil</c>/<c>Run</c>, or the dedicated game
/// thread of <c>RunAsync</c>. All graph work then happens on that thread.
/// Cross-thread writes go through <c>cval.Post</c> and are drained by
/// <c>Posting.pump</c> at the start of every step.
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
  let mutable restartCell = Unchecked.defaultof<cval<bool>>
  let mutable ctx = Unchecked.defaultof<AdaptiveContext>
  let mutable initialized = false

  let mutable fixedAccSeconds = 0.0f

  let mutable gameTime = {
    TotalTime = TimeSpan.Zero
    ElapsedGameTime = TimeSpan.Zero
  }

  let mutable frame = Unchecked.defaultof<'Frame>
  let mutable frameBuilder: unit -> 'Frame = Unchecked.defaultof<unit -> 'Frame>

  /// Create the graph on the current thread and force the first frame.
  /// Runs once: on the first user of the runner (a step or the RunAsync game thread).
  let ensureInitialized() =
    if not initialized then
      gameContext <- defaultArg context (GameContext.create(w, h))

      let timeCell =
        CVal.create(
          {
            TotalTime = TimeSpan.Zero
            ElapsedGameTime = TimeSpan.Zero
          }
        )

      exitCell <- CVal.create false
      restartCell <- CVal.create false
      ctx <- AdaptiveContext(gameContext, timeCell, exitCell, restartCell)

      let init = program.Init ctx
      frameBuilder <- init.FrameBuilder
      disposables.AddRange(init.Disposables)

      // Force the first frame so Frame is never default after initialization.
      frame <- frameBuilder()

      // Observers are stored by prepending (see withObserver), so reverse to
      // initialize and notify in registration order — matching HeadlessRunner.
      for factory in List.rev program.Observers do
        observers.Add(factory())

      initialized <- true

  /// <summary>Whether the runner has received an exit request.</summary>
  member _.ShouldQuit = if initialized then AVal.getValue exitCell else false

  /// <summary>
  /// Whether the program has requested a rebuild (it wrote
  /// <see cref="P:Mibo.Adaptive.AdaptiveContext.RestartRequested"/>). The
  /// windowed hosts check this after <c>Step</c> and call
  /// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Restart"/>.
  /// </summary>
  member _.RestartRequested =
    if initialized then AVal.getValue restartCell else false

  /// <summary>
  /// Rebuild the program: disposes the program's disposables (e.g. input
  /// subscriptions), re-runs <c>Init</c> with the same context — a fresh graph
  /// over the same roots, which is what makes restart safe — resets the
  /// internal clock and fixed-step accumulator, and forces the first frame. The
  /// program requests it by writing <c>RestartRequested</c>; the windowed hosts
  /// consume it after <c>Step</c>, headless users call this directly.
  /// </summary>
  member _.Restart() =
    if initialized then
      for i = 0 to disposables.Count - 1 do
        disposables[i].Dispose()

      disposables.Clear()

      let init = program.Init ctx
      frameBuilder <- init.FrameBuilder
      disposables.AddRange(init.Disposables)

      gameTime <- {
        TotalTime = TimeSpan.Zero
        ElapsedGameTime = TimeSpan.Zero
      }

      fixedAccSeconds <- 0.0f

      restartCell.Set(false)
      frame <- frameBuilder()

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
  /// is written and <c>Update</c> runs once per step. The frame is forced once
  /// at the end regardless, so intermediate steps are integrated but not
  /// observed by the frame. This mirrors the MVU <c>TickFrame</c> fixed-step
  /// loop, calling <c>Update</c> instead of dispatching a mapped message.
  /// </para>
  /// <para>
  /// This mutates the runner's internal state (time root, program roots, frame).
  /// Do not mix <c>Step</c>/<c>StepN</c>/<c>StepUntil</c> with <c>Run</c>/<c>RunAsync</c> on the same runner
  /// — they all advance the simulation and using them together will produce simulation corruption.
  /// </para>
  /// </remarks>
  member _.Step(elapsed: TimeSpan) : 'Frame =
    ensureInitialized()

    if AVal.getValue exitCell then
      frame
    else

      // Note: ticks comparison, not `elapsed < TimeSpan.Zero` — F# generic
      // comparison boxes both operands (48 bytes/call on the frame path).
      let elapsed = if elapsed.Ticks < 0L then TimeSpan.Zero else elapsed

      // Apply cross-thread posts (e.g. input-thread writes) at the frame
      // boundary, once per Step — mirrors MVU draining deferred effects at the
      // top of TickFrame.
      Posting.pump()

      match program.FixedStep with
      | ValueNone ->
        gameTime <- {
          TotalTime = gameTime.TotalTime + elapsed
          ElapsedGameTime = elapsed
        }

        // The time root is the framework's write into the graph.
        ctx.Time.Set(gameTime)

        // The imperative phase: reads projections, writes roots.
        program.Update ctx gameTime

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

          ctx.Time.Set(gameTime)
          program.Update ctx gameTime

      // The force phase: recompute the frame's projections exactly once
      // (not at all if none of their dependencies moved) and pack the struct.
      frame <- frameBuilder()

      for i = 0 to observers.Count - 1 do
        observers[i].OnNext(gameContext, frame, gameTime)

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

  /// <summary>Run the simulation asynchronously, yielding each frame as an async enumerable.</summary>
  /// <param name="interval">Tick interval.</param>
  /// <param name="ct">Optional cancellation token to stop the loop.</param>
  /// <returns>An async sequence of <c>(GameTime * 'Frame)</c> snapshots.</returns>
  /// <remarks>
  /// <para>
  /// The world loop runs on a dedicated background game thread — the graph is
  /// confined to its creating thread, and async continuation threads are not it.
  /// The async enumerable is a consumer of that thread: it receives the frames
  /// the loop produces, in order. The <c>for .. in</c> syntax in F# 8+ can iterate
  /// over <c>IAsyncEnumerable</c> directly.
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

    { new IAsyncEnumerable<struct (GameTime * 'Frame)> with
        member _.GetAsyncEnumerator(cancellationToken) =
          let linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
              ct,
              cancellationToken
            )

          // The game thread owns the graph (created on first use) and posts the
          // frames to this queue; the async enumerator waits on the signal.
          let frames = ConcurrentQueue<struct (GameTime * 'Frame)>()
          let signal = new SemaphoreSlim(0)
          let mutable current = Unchecked.defaultof<struct (GameTime * 'Frame)>

          let gameThread =
            Thread(fun () ->
              try
                ensureInitialized()

                let sw = Stopwatch.StartNew()
                let intervalMs = interval.TotalMilliseconds
                let mutable nextTick = 0.0
                let mutable running = true

                while running do
                  if this.ShouldQuit || linkedCts.IsCancellationRequested then
                    running <- false
                  else
                    let elapsed = sw.Elapsed.TotalMilliseconds

                    if elapsed >= nextTick then
                      nextTick <- nextTick + intervalMs
                      this.Step interval |> ignore
                      frames.Enqueue(struct (this.GameTime, this.Frame))
                      signal.Release() |> ignore
                    else
                      Thread.Sleep 1
              finally
                // Wake a waiting consumer so it can observe the end of the stream.
                signal.Release() |> ignore)

          gameThread.IsBackground <- true
          gameThread.Start()

          { new IAsyncEnumerator<struct (GameTime * 'Frame)> with
              member _.Current = current

              member _.MoveNextAsync() =
                ValueTask<bool>(
                  task {
                    try
                      let! _ = signal.WaitAsync linkedCts.Token

                      let mutable item =
                        Unchecked.defaultof<struct (GameTime * 'Frame)>

                      if frames.TryDequeue(&item) then
                        current <- item
                        return true
                      else
                        return false
                    with :? OperationCanceledException ->
                      return false
                  }
                )

              member _.DisposeAsync() =
                linkedCts.Cancel()
                signal.Release() |> ignore
                linkedCts.Dispose()
                ValueTask()
          }
    }

  /// <summary>Dispose program disposables and observers, and clean up resources.</summary>
  member _.Dispose() =
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
