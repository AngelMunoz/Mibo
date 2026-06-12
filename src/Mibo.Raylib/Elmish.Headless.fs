namespace Mibo.Elmish

open System

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
}

/// <summary>
/// Functions for creating and configuring headless Elmish programs.
/// </summary>
module HeadlessProgram =

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
    }

  /// <summary>Adds a subscription function to the program.</summary>
  let withSubscribe subscribe program : HeadlessProgram<'Model, 'Msg> = {
    program with
        Subscribe = subscribe
  }

  let withTick map program : HeadlessProgram<'Model, 'Msg> = {
    program with
        Tick = ValueSome map
  }

  let withFixedStep cfg program : HeadlessProgram<'Model, 'Msg> =
    if cfg.StepSeconds <= 0.0f then
      invalidArg (nameof cfg.StepSeconds) "StepSeconds must be > 0"

    if cfg.MaxStepsPerFrame <= 0 then
      invalidArg (nameof cfg.MaxStepsPerFrame) "MaxStepsPerFrame must be > 0"

    {
      program with
          FixedStep = ValueSome cfg
    }

  let withDispatchMode mode program : HeadlessProgram<'Model, 'Msg> = {
    program with
        DispatchMode = mode
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

  let msgQueue = DispatchQueue<'Msg>(program.DispatchMode)
  let mutable state: 'Model = Unchecked.defaultof<'Model>
  let mutable ctxOpt: GameContext voption = ValueNone
  let activeSubs = Collections.Generic.Dictionary<SubId, IDisposable>()
  let subIdsInUse = Collections.Generic.HashSet<SubId>()
  let subIdsToRemove = ResizeArray<SubId>(32)
  let subBuffer = ResizeArray<struct (SubId * Subscribe<'Msg>)>()
  let subStack = ResizeArray<Sub<'Msg>>()
  let deferredEffs = ResizeArray<Effect<'Msg>>(64)
  let deferredEffsRun = ResizeArray<Effect<'Msg>>(64)
  let mutable fixedAccSeconds = 0.0f
  let mutable totalTime = TimeSpan.Zero
  let mutable shouldQuit = false

  let w = defaultArg width 800
  let h = defaultArg height 600

  let dispatch(msg: 'Msg) = msgQueue.Dispatch(msg)

  let execCmd(cmd: Cmd<'Msg>) =
    match cmd with
    | Empty -> ()
    | Quit -> shouldQuit <- true
    | Single eff -> eff.Invoke(dispatch)
    | Batch effs ->
      for i = 0 to effs.Length - 1 do
        effs[i].Invoke(dispatch)
    | DeferNextFrame effs -> deferredEffs.AddRange(effs)
    | NowAndDeferNextFrame(now, next) ->
      for i = 0 to now.Length - 1 do
        now[i].Invoke(dispatch)

      deferredEffs.AddRange(next)

  let updateSubs(ctx: GameContext) =
    subBuffer.Clear()
    subStack.Clear()
    subStack.Add(program.Subscribe ctx state)
    Sub.flatten subStack subBuffer

    subIdsInUse.Clear()
    subIdsToRemove.Clear()

    for id, subscribeFn in subBuffer do
      subIdsInUse.Add(id) |> ignore

      if not(activeSubs.ContainsKey(id)) then
        try
          activeSubs.Add(id, subscribeFn dispatch)
        with ex ->
          Console.WriteLine($"Error starting sub {SubId.value id}: {ex}")

    for KeyValue(key, _disp) in activeSubs do
      if not(subIdsInUse.Contains(key)) then
        subIdsToRemove.Add(key)

    for i = 0 to subIdsToRemove.Count - 1 do
      let key = subIdsToRemove[i]

      match activeSubs.TryGetValue(key) with
      | true, disp ->
        disp.Dispose()
        activeSubs.Remove(key) |> ignore
      | _ -> ()

  do
    let ctx = GameContext.create(w, h)
    ctxOpt <- ValueSome ctx
    let struct (initialState, initialCmds) = program.Init ctx
    state <- initialState
    execCmd initialCmds
    updateSubs ctx

  /// <summary>Whether the runner has received a Quit signal.</summary>
  member _.ShouldQuit = shouldQuit

  /// <summary>The current model state.</summary>
  member _.Model = state

  /// <summary>Total elapsed virtual time.</summary>
  member _.TotalTime = totalTime

  /// <summary>Dispatch a message to the runner.</summary>
  member _.Dispatch(msg: 'Msg) = dispatch msg

  /// <summary>Dispatch multiple messages at once.</summary>
  member _.DispatchMany(msgs: 'Msg seq) =
    for msg in msgs do
      dispatch msg

  /// <summary>Advance the simulation by one frame with the given delta time.</summary>
  /// <param name="elapsed">Frame delta (e.g. TimeSpan.FromMiliSeconds(16) for 60fps). Negative values are clamped to zero.</param>
  member _.Step(elapsed: TimeSpan) =
    if shouldQuit then
      ()
    else

      let elapsed = if elapsed < TimeSpan.Zero then TimeSpan.Zero else elapsed

      totalTime <- totalTime + elapsed

      let gameTime = {
        TotalTime = totalTime
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

  /// <summary>Advance the simulation by N frames.</summary>
  /// <param name="count">Number of frames to run.</param>
  /// <param name="elapsed">Frame delta per step.</param>
  member this.StepN(count: int, elapsed: TimeSpan) =
    for _ = 1 to count do
      this.Step(elapsed)

  /// <summary>Advance until a predicate on the model returns true.</summary>
  /// <param name="predicate">Condition to check after each frame.</param>
  /// <param name="elapsed">Frame delta per step.</param>
  /// <param name="maxFrames">Safety limit to prevent infinite loops.</param>
  /// <returns>True if predicate was met, false if maxFrames was reached.</returns>
  member this.StepUntil
    (predicate: 'Model -> bool, elapsed: TimeSpan, ?maxFrames: int)
    =
    let max = defaultArg maxFrames 10000
    let mutable met = false

    for _ = 1 to max do
      if not this.ShouldQuit && not(predicate this.Model) then
        this.Step(elapsed)
      else
        met <- true

    met

  /// <summary>Dispose active subscriptions and clean up resources.</summary>
  member _.Dispose() =
    for KeyValue(_key, disp) in activeSubs do
      disp.Dispose()

    activeSubs.Clear()

  interface IDisposable with
    member this.Dispose() = this.Dispose()
