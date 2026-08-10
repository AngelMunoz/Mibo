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
/// The graph-building context handed to an <see cref="T:Mibo.Adaptive.AdaptiveWorld`1"/>.
/// </summary>
/// <remarks>
/// The context exposes the framework-owned roots: the time cell, written by the
/// runner at the start of every <c>Step</c>, the exit-request cell, read by the
/// runner to decide whether to stop, and the restart-request cell, read by the
/// host to decide whether to rebuild the world. The world reads these cells and
/// derives its projections from them like any other root; only
/// <c>ExitRequested</c>/<c>RestartRequested</c> are written by the world.
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
  /// The restart-request root. Set it to <c>true</c> to rebuild the world: the
  /// runner disposes the world's disposables, re-runs <c>Init</c> — a fresh
  /// graph over the same roots — and forces the first frame. The windowed
  /// hosts consume it after <c>Step</c>; headless users call
  /// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Restart"/> themselves.
  /// </summary>
  member _.RestartRequested = restartRequested

  /// <summary>
  /// The <see cref="T:Mibo.Elmish.GameContext"/> the runner owns: the window
  /// dimensions and the registered services (IAssets, IInput, custom). Worlds
  /// read services directly from here in <c>Init</c> and <c>Update</c> — the
  /// host registers what it owns and the world pulls the rest; there is no
  /// registration ceremony.
  /// </summary>
  member _.Context = ctx

  /// <summary>Current window width in pixels. Default: 800.</summary>
  member _.WindowWidth = ctx.WindowWidth

  /// <summary>Current window height in pixels. Default: 600.</summary>
  member _.WindowHeight = ctx.WindowHeight

/// <summary>The result of building an adaptive world.</summary>
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

/// <summary>
/// A program configuration for running an adaptive game world without graphics.
/// </summary>
/// <remarks>
/// The adaptive world replaces the Elmish triad (model, message, update) with a
/// dependency graph: changeable roots hold the state, derived projections compose
/// it, and the runner forces the frame's projections at the end of every step.
/// There is no <c>'Msg</c>, no <c>Cmd</c> and no <c>Sub</c> — handlers write roots
/// directly and effects run directly. Use <see cref="M:Mibo.Adaptive.AdaptiveWorld.mk"/> to create a world.
/// </remarks>
type AdaptiveWorld<'Frame> = {
  /// <summary>
  /// Builds the graph (roots and projections) and registers handlers.
  /// Returns the <see cref="T:Mibo.Adaptive.AdaptiveInit`1"/> — the frame builder
  /// plus the disposables released when the runner is disposed.
  /// </summary>
  Init: AdaptiveContext -> AdaptiveInit<'Frame>

  /// <summary>
  /// Optional per-frame phase. Runs after the time root is written and before
  /// the frame is forced. Reads projections, writes roots. Default: no-op.
  /// </summary>
  Update: AdaptiveContext -> GameTime -> unit

  /// <summary>Observer factories for receiving the forced frame each step.</summary>
  Observers: (unit -> IObserver<struct (GameContext * 'Frame * GameTime)>) list
}

/// <summary>Extension functions for building and configuring an <see cref="T:Mibo.Adaptive.AdaptiveWorld`1"/>.</summary>
module AdaptiveWorld =

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

  /// <summary>Creates a world with an empty per-frame phase and no observers.</summary>
  let mk
    (init: AdaptiveContext -> AdaptiveInit<'Frame>)
    : AdaptiveWorld<'Frame> =
    {
      Init = init
      Update = fun _ctx _gameTime -> ()
      Observers = []
    }

  /// <summary>Sets the per-frame phase of the world.</summary>
  let withUpdate
    (update: AdaptiveContext -> GameTime -> unit)
    (world: AdaptiveWorld<'Frame>)
    : AdaptiveWorld<'Frame> =
    { world with Update = update }

  /// <summary>Adds an observer factory to the world.</summary>
  let withObserver
    (factory: unit -> IObserver<struct (GameContext * 'Frame * GameTime)>)
    (world: AdaptiveWorld<'Frame>)
    : AdaptiveWorld<'Frame> =
    {
      world with
          Observers = factory :: world.Observers
    }

/// <summary>
/// Runs an adaptive game world with explicit frame stepping.
/// </summary>
/// <remarks>
/// <para>
/// The runner owns the frame boundary. Each <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>:
/// (1) applies cross-thread posts, (2) writes the current game time into the time root,
/// (3) runs the world's per-frame phase, (4) forces the frame builder — the frame's
/// projections recompute exactly once if any of their dependencies moved, and not at all
/// otherwise — and (5) notifies the observers with the forced frame. Draw code reads the
/// returned frame: reads are O(1) until the next write.
/// </para>
/// <para>
/// The adaptive graph is confined to the thread that creates it (AdaptiveSlop's
/// owner-thread model: no locks, allocation-free steady state). The runner creates
/// the graph lazily on the first user — the thread that first calls
/// <c>Step</c>/<c>StepN</c>/<c>StepUntil</c>/<c>Run</c>, or the dedicated game
/// thread of <c>RunAsync</c>. All graph work then happens on that thread.
/// </para>
/// </remarks>
type AdaptiveHeadless<'Frame>
  (
    world: AdaptiveWorld<'Frame>,
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

      let init = world.Init ctx
      frameBuilder <- init.FrameBuilder
      disposables.AddRange(init.Disposables)

      // Force the first frame so Frame is never default after initialization.
      frame <- frameBuilder()

      // Observers are stored by prepending (see withObserver), so reverse to
      // initialize and notify in registration order — matching HeadlessRunner.
      for factory in List.rev world.Observers do
        observers.Add(factory())

      initialized <- true

  /// <summary>Whether the runner has received an exit request.</summary>
  member _.ShouldQuit = if initialized then AVal.getValue exitCell else false

  /// <summary>
  /// Whether the world has requested a rebuild (it wrote
  /// <see cref="P:Mibo.Adaptive.AdaptiveContext.RestartRequested"/>). The
  /// windowed hosts check this after <c>Step</c> and call
  /// <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Restart"/>.
  /// </summary>
  member _.RestartRequested =
    if initialized then AVal.getValue restartCell else false

  /// <summary>
  /// Rebuild the world: disposes the world's disposables (e.g. input
  /// subscriptions), re-runs <c>Init</c> with the same context — a fresh graph
  /// over the same roots, which is what makes restart safe — resets the
  /// internal clock and forces the first frame. The world requests it by
  /// writing <c>RestartRequested</c>; the windowed hosts consume it after
  /// <c>Step</c>, headless users call this directly.
  /// </summary>
  member _.Restart() =
    if initialized then
      for i = 0 to disposables.Count - 1 do
        disposables[i].Dispose()

      disposables.Clear()

      let init = world.Init ctx
      frameBuilder <- init.FrameBuilder
      disposables.AddRange(init.Disposables)

      gameTime <- {
        TotalTime = TimeSpan.Zero
        ElapsedGameTime = TimeSpan.Zero
      }

      restartCell.Set(false)
      frame <- frameBuilder()

  /// <summary>Total elapsed virtual time.</summary>
  member _.GameTime = gameTime

  /// <summary>The last forced frame. Valid after the first <see cref="M:Mibo.Adaptive.AdaptiveHeadless`1.Step"/>.</summary>
  member _.Frame = frame

  /// <summary>
  /// Advance the world by one frame and return the forced frame.
  /// </summary>
  /// <param name="elapsed">Frame delta (e.g. <c>TimeSpan.FromMilliseconds(16)</c> for 60fps). Negative values are clamped to zero.</param>
  /// <remarks>
  /// This mutates the runner's internal state (time root, world roots, frame).
  /// Do not mix <c>Step</c>/<c>StepN</c>/<c>StepUntil</c> with <c>Run</c>/<c>RunAsync</c> on the same runner
  /// — they all advance the simulation and using them together will produce simulation corruption.
  /// </remarks>
  member _.Step(elapsed: TimeSpan) : 'Frame =
    ensureInitialized()

    if AVal.getValue exitCell then
      frame
    else

      // Note: ticks comparison, not `elapsed < TimeSpan.Zero` — F# generic
      // comparison boxes both operands (48 bytes/call on the frame path).
      let elapsed = if elapsed.Ticks < 0L then TimeSpan.Zero else elapsed

      gameTime <- {
        TotalTime = gameTime.TotalTime + elapsed
        ElapsedGameTime = elapsed
      }

      // Apply cross-thread posts (e.g. input-thread writes) at the frame boundary.
      Posting.pump()

      // The time root is the framework's write into the graph.
      ctx.Time.Set(gameTime)

      // The imperative phase: reads projections, writes roots.
      world.Update ctx gameTime

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

  /// <summary>Dispose world disposables and observers, and clean up resources.</summary>
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
