namespace Mibo.Elmish

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

/// <summary>
/// A program configuration for running the Elmish update loop without graphics.
/// </summary>
/// <remarks>
/// HeadlessProgram shares the core Elmish architecture (Init, Update, Subscribe, Tick, FixedStep)
/// with the full Program type, but excludes renderers and window configuration.
/// Use <see cref="M:Mibo.Elmish.HeadlessProgram.mkHeadless"/> to create one.
/// </remarks>
type HeadlessProgram<'Model, 'Msg> = {
  /// <summary>Creates initial model and commands when the headless runner starts.</summary>
  Init: GameContext -> struct ('Model * Cmd<'Msg>)
  /// <summary>Handles messages and returns updated model and commands.</summary>
  Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
  /// <summary>Returns subscriptions based on current model state.</summary>
  Subscribe: GameContext -> 'Model -> Sub<'Msg>
  /// <summary>Optional function to generate a message each frame.</summary>
  Tick: (GameTime -> 'Msg) voption
  /// <summary>Optional framework-managed fixed timestep configuration.</summary>
  FixedStep: FixedStepConfig<'Msg> voption
  /// <summary>Controls when dispatched messages become eligible for processing.</summary>
  DispatchMode: DispatchMode
  /// <summary>Observer factories for receiving model snapshots each frame.</summary>
  Observers: (unit -> IObserver<struct (GameContext * 'Model * GameTime)>) list
}

/// <summary>
/// Functions for creating and configuring headless Elmish programs.
/// </summary>
module HeadlessProgram =

  /// <summary>
  /// Creates a <c>System.IObserver</c> from an <c>onNext</c> callback, hiding
  /// the <c>OnError</c> and <c>OnCompleted</c> boilerplate.
  /// </summary>
  let inline observe(onNext: 'T -> unit) : IObserver<'T> =
    { new IObserver<'T> with
        member _.OnNext value = onNext value
        member _.OnError _ = ()
        member _.OnCompleted() = ()
    }

  /// <summary>
  /// Creates a new headless program with the given init and update functions.
  /// </summary>
  let mkHeadless
    (init: GameContext -> struct ('Model * Cmd<'Msg>))
    (update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>))
    : HeadlessProgram<'Model, 'Msg> =
    {
      Init = init
      Update = update
      Subscribe = (fun _ctx _model -> Sub.none)
      Tick = ValueNone
      FixedStep = ValueNone
      DispatchMode = DispatchMode.Immediate
      Observers = []
    }

  /// <summary>Adds a subscription function to the program.</summary>
  let withSubscribe subscribe program : HeadlessProgram<'Model, 'Msg> = {
    program with
        Subscribe = subscribe
  }

  /// <summary>Adds a per-frame tick message generated from the current <see cref="T:Mibo.Elmish.GameTime"/>.</summary>
  /// <param name="map">Function that converts the current game time into a message dispatched each frame.</param>
  let withTick map program : HeadlessProgram<'Model, 'Msg> = {
    program with
        Tick = ValueSome map
  }

  /// <summary>Enables a framework-managed fixed timestep that dispatches a message at a constant rate, independent of variable frame timing.</summary>
  /// <param name="cfg">Fixed step configuration (step size, max steps per frame, max frame budget, message mapper).</param>
  /// <exception cref="T:System.ArgumentException">Thrown when <c>StepSeconds</c> ≤ 0 or <c>MaxStepsPerFrame</c> ≤ 0.</exception>
  let withFixedStep cfg program : HeadlessProgram<'Model, 'Msg> =
    if cfg.StepSeconds <= 0.0f then
      invalidArg (nameof cfg.StepSeconds) "StepSeconds must be > 0"

    if cfg.MaxStepsPerFrame <= 0 then
      invalidArg (nameof cfg.MaxStepsPerFrame) "MaxStepsPerFrame must be > 0"

    {
      program with
          FixedStep = ValueSome cfg
    }

  /// <summary>Sets the dispatch mode controlling when messages become eligible for processing.</summary>
  /// <param name="mode"><c>Immediate</c> processes in-frame; <c>FrameBounded</c> defers to the next step.</param>
  let withDispatchMode mode program : HeadlessProgram<'Model, 'Msg> = {
    program with
        DispatchMode = mode
  }

  let withObserver
    (factory: unit -> IObserver<struct (GameContext * 'Model * GameTime)>)
    program
    : HeadlessProgram<'Model, 'Msg> =
    {
      program with
          Observers = factory :: program.Observers
    }

/// <summary>
/// Controls execution of a headless Elmish program with explicit frame stepping.
/// </summary>
/// <remarks>
/// The runner manages virtual time, message dispatching, command execution,
/// and subscription lifecycle. Call <see cref="Step"/> or <see cref="StepN"/>
/// to advance the simulation.
/// </remarks>
type HeadlessRunner<'Model, 'Msg>
  (program: HeadlessProgram<'Model, 'Msg>, ?width: int, ?height: int) =

  let msgQueue = DispatchQueue<'Msg> program.DispatchMode
  let mutable state: 'Model = Unchecked.defaultof<'Model>
  let mutable ctxOpt: GameContext voption = ValueNone
  let activeSubs = Dictionary<SubId, IDisposable>()
  let subIdsInUse = HashSet<SubId>()
  let subIdsToRemove = ResizeArray<SubId>(32)
  let subBuffer = ResizeArray<struct (SubId * Subscribe<'Msg>)>()
  let subStack = ResizeArray<Sub<'Msg>>()
  let deferredEffs = ResizeArray<Effect<'Msg>>(64)
  let deferredEffsRun = ResizeArray<Effect<'Msg>>(64)
  let mutable fixedAccSeconds = 0.0f

  let mutable gameTime = {
    TotalTime = TimeSpan.Zero
    ElapsedGameTime = TimeSpan.Zero
  }


  let mutable shouldQuit = false

  let observers =
    ResizeArray<IObserver<struct (GameContext * 'Model * GameTime)>>()

  let w = defaultArg width 800
  let h = defaultArg height 600

  let dispatch(msg: 'Msg) = msgQueue.Dispatch(msg)

  let execCmd(cmd: Cmd<'Msg>) =
    match cmd with
    | Empty -> ()
    | Quit -> shouldQuit <- true
    | Single eff -> eff.Invoke dispatch
    | Batch effs ->
      for i = 0 to effs.Length - 1 do
        effs[i].Invoke dispatch
    | DeferNextFrame effs -> deferredEffs.AddRange effs
    | NowAndDeferNextFrame(now, next) ->
      for i = 0 to now.Length - 1 do
        now[i].Invoke dispatch

      deferredEffs.AddRange next

  let updateSubs(ctx: GameContext) =
    subBuffer.Clear()
    subStack.Clear()
    subStack.Add(program.Subscribe ctx state)
    Sub.flatten subStack subBuffer

    subIdsInUse.Clear()
    subIdsToRemove.Clear()

    for id, subscribeFn in subBuffer do
      subIdsInUse.Add id |> ignore

      if not(activeSubs.ContainsKey id) then
        try
          activeSubs.Add(id, subscribeFn dispatch)
        with ex ->
          Console.WriteLine $"Error starting sub {SubId.value id}: {ex}"

    for KeyValue(key, _disp) in activeSubs do
      if not(subIdsInUse.Contains key) then
        subIdsToRemove.Add key

    for i = 0 to subIdsToRemove.Count - 1 do
      let key = subIdsToRemove[i]

      match activeSubs.TryGetValue key with
      | true, disp ->
        disp.Dispose()
        activeSubs.Remove key |> ignore
      | _ -> ()

  do
    let ctx = GameContext.create(w, h)
    ctxOpt <- ValueSome ctx
    let struct (initialState, initialCmds) = program.Init ctx
    state <- initialState
    execCmd initialCmds
    updateSubs ctx

    for factory in program.Observers do
      observers.Add(factory())

  /// <summary>Whether the runner has received a Quit signal.</summary>
  member _.ShouldQuit = shouldQuit

  /// <summary>The current model state.</summary>
  member _.Model = state

  /// <summary>Total elapsed virtual time.</summary>
  member _.GameTime = gameTime

  /// <summary>Dispatch a message to the runner.</summary>
  member _.Dispatch(msg: 'Msg) = dispatch msg

  /// <summary>Dispatch multiple messages at once.</summary>
  member _.DispatchMany(msgs: 'Msg seq) =
    for msg in msgs do
      dispatch msg

  /// <summary>Advance the simulation by one frame with the given delta time.</summary>
  /// <param name="elapsed">Frame delta (e.g. TimeSpan.FromMilliseconds(16) for 60fps). Negative values are clamped to zero.</param>
  /// <remarks>
  /// This mutates the runner's internal state (model, game time, subscriptions, deferred commands).
  /// Do not mix <c>Step</c>/<c>StepN</c>/<c>StepUntil</c> with <c>Run</c>/<c>RunAsync</c> on the same runner
  /// — they all advance the simulation and using them together will produce simulation corruption.
  /// </remarks>
  member _.Step(elapsed: TimeSpan) =
    if shouldQuit then
      ()
    else

      let elapsed = if elapsed < TimeSpan.Zero then TimeSpan.Zero else elapsed

      gameTime <- {
        TotalTime = gameTime.TotalTime + elapsed
        ElapsedGameTime = elapsed

      }

      if deferredEffs.Count <> 0 then
        deferredEffsRun.Clear()
        deferredEffsRun.AddRange(deferredEffs)
        deferredEffs.Clear()

        for i = 0 to deferredEffsRun.Count - 1 do
          deferredEffsRun[i].Invoke(dispatch)

      match program.FixedStep with
      | ValueNone -> ()
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

        for _i = 1 to steps do
          dispatch(cfg.Map cfg.StepSeconds)

      program.Tick |> ValueOption.iter(fun map -> dispatch(map gameTime))

      let mutable stateChanged = false
      let mutable msg = Unchecked.defaultof<'Msg>
      msgQueue.StartBatch()

      while msgQueue.TryDequeue(&msg) do
        let struct (newState, cmds) = program.Update msg state
        state <- newState
        execCmd cmds
        stateChanged <- true

      msgQueue.EndBatch()

      if stateChanged then
        match ctxOpt with
        | ValueSome ctx -> updateSubs ctx
        | ValueNone -> ()

      match ctxOpt with
      | ValueSome ctx ->
        for i = 0 to observers.Count - 1 do
          observers[i].OnNext(ctx, state, gameTime)
      | ValueNone -> ()

  /// <summary>Advance the simulation by N frames.</summary>
  /// <param name="count">Number of frames to run.</param>
  /// <param name="elapsed">Frame delta per step.</param>
  /// <remarks>
  /// This mutates the runner's internal state. Do not mix with <c>Run</c>/<c>RunAsync</c>
  /// on the same runner — they all advance the simulation and using them together
  /// will produce simulation corruption.
  /// </remarks>
  member this.StepN(count: int, elapsed: TimeSpan) =
    for _ = 1 to count do
      this.Step elapsed


  /// <summary>Advance until a predicate on the model returns true.</summary>
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
    (predicate: 'Model -> bool, elapsed: TimeSpan, [<Struct>] ?maxFrames: int)
    =
    let max = defaultValueArg maxFrames 10000
    let mutable met = false

    for _ = 1 to max do
      if not this.ShouldQuit && not(predicate this.Model) then
        this.Step elapsed
      else
        met <- true

    met

  /// <summary>Run the simulation synchronously, yielding each frame as a sequence.</summary>
  /// <param name="interval">Tick interval (e.g. TimeSpan.FromMilliseconds(16) for 60fps).</param>
  /// <param name="ct">Optional cancellation token to stop the loop early.</param>
  /// <returns>A sequence of <c>(GameTime * 'Model)</c> snapshots, paced by the interval.</returns>
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
    if interval <= TimeSpan.Zero then
      invalidArg (nameof interval) "Interval must be greater than zero"

    let ct = defaultValueArg ct CancellationToken.None
    let sw = Stopwatch.StartNew()
    let intervalMs = interval.TotalMilliseconds
    let mutable nextTick = 0.0

    seq {
      while not this.ShouldQuit && not ct.IsCancellationRequested do
        let elapsed = sw.Elapsed.TotalMilliseconds

        if elapsed >= nextTick then
          this.Step interval
          nextTick <- nextTick + intervalMs
          struct (this.GameTime, this.Model)
        else
          Thread.Sleep 1
    }


  /// <summary>Run the simulation asynchronously, yielding each frame as an async enumerable.</summary>
  /// <param name="interval">Tick interval.</param>
  /// <param name="ct">Cancellation token to stop the loop.</param>
  /// <returns>An async sequence of <c>(GameTime * 'Model)</c> snapshots.</returns>
  /// <remarks>
  /// Uses <c>PeriodicTimer</c> for efficient, precise pacing. The <c>for .. in</c> syntax
  /// in F# 8+ can iterate over <c>IAsyncEnumerable</c> directly.
  /// <para>
  /// This advances the runner's internal state. Do not mix with <c>Step</c>/<c>StepN</c>/<c>StepUntil</c>
  /// on the same runner — they all advance the simulation and using them together
  /// will produce simulation corruption.
  /// </para>
  /// </remarks>
  member this.RunAsync(interval: TimeSpan, [<Struct>] ?ct: CancellationToken) =
    if interval <= TimeSpan.Zero then
      invalidArg (nameof interval) "Interval must be greater than zero"

    let ct = defaultValueArg ct CancellationToken.None

    { new IAsyncEnumerable<struct (GameTime * 'Model)> with
        member _.GetAsyncEnumerator(cancellationToken) =
          let linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
              ct,
              cancellationToken
            )

          let timer = new PeriodicTimer(interval)
          let mutable current = Unchecked.defaultof<struct (GameTime * 'Model)>

          { new IAsyncEnumerator<struct (GameTime * 'Model)> with
              member _.Current = current

              member _.MoveNextAsync() =
                ValueTask<bool>(
                  task {
                    let! tick = timer.WaitForNextTickAsync linkedCts.Token

                    if tick && not this.ShouldQuit then
                      this.Step interval
                      current <- struct (this.GameTime, this.Model)
                      return true
                    else
                      return false
                  }
                )

              member _.DisposeAsync() =
                timer.Dispose()
                linkedCts.Dispose()
                ValueTask()
          }
    }

  /// <summary>Dispose active subscriptions, observers, and clean up resources.</summary>
  member _.Dispose() =
    for KeyValue(_key, disp) in activeSubs do
      disp.Dispose()

    activeSubs.Clear()

    for i = 0 to observers.Count - 1 do
      match observers[i] with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    observers.Clear()

  interface IDisposable with
    member this.Dispose() = this.Dispose()
