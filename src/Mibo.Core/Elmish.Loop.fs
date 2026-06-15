namespace Mibo.Elmish

open System
open System.Collections.Concurrent
open System.Collections.Generic

// ─────────────────────────────────────────────────────────────────────────────
// The shared Elmish message-processing loop.
//
// Both RaylibGame (the windowed host) and HeadlessRunner (the headless host)
// run the exact same message-processing core: a DispatchQueue, execCmd,
// updateSubs, deferred-effect draining, FixedStep accumulation, tick dispatch,
// and the StartBatch/TryDequeue/EndBatch message pump. This type captures that
// shared core so the two hosts become thin I/O shells around it.
//
// A host constructs an ElmishLoop from a LoopCore (the six fields that define
// message-processing behavior) and then:
//   1. calls Init(ctx) once after registering backend services
//   2. calls TickFrame(dt, gameTime) each frame
//   3. reads Model / ShouldQuit / GameTime as needed
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Internal message queue supporting optional frame-bounded dispatch.
/// </summary>
/// <remarks>
/// Extracted from the runtime so both the windowed and headless hosts share it.
/// Kept internal — hosts interact with it through <see cref="T:Mibo.Elmish.ElmishLoop`2"/>.
/// </remarks>
type internal DispatchQueue<'Msg>(mode: DispatchMode) =
  let gate = obj()
  let mutable isProcessing = false
  let mutable current = ConcurrentQueue<'Msg>()
  let mutable next = ConcurrentQueue<'Msg>()

  member _.Mode = mode

  member _.Dispatch(msg: 'Msg) =
    match mode with
    | Immediate -> current.Enqueue(msg)
    | FrameBounded ->
      lock gate (fun () ->
        if isProcessing then
          next.Enqueue(msg)
        else
          current.Enqueue(msg))

  member _.StartBatch() =
    match mode with
    | Immediate -> ()
    | FrameBounded -> lock gate (fun () -> isProcessing <- true)

  member _.EndBatch() =
    match mode with
    | Immediate -> ()
    | FrameBounded ->
      lock gate (fun () ->
        isProcessing <- false
        let tmp = current
        current <- next
        next <- tmp)

  member _.TryDequeue(msg: byref<'Msg>) = current.TryDequeue(&msg)

/// <summary>
/// The six fields that define message-processing behavior, shared by
/// <see cref="T:Mibo.Elmish.Program`2"/> and <see cref="T:Mibo.Elmish.HeadlessProgram`2"/>.
/// </summary>
/// <remarks>
/// Each host projects its program type to a <c>LoopCore</c> via a trivial accessor,
/// so neither <c>Program</c> nor <c>HeadlessProgram</c> changes shape.
/// </remarks>
[<Struct>]
type LoopCore<'Model, 'Msg> = {
  Init: GameContext -> struct ('Model * Cmd<'Msg>)
  Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
  Subscribe: GameContext -> 'Model -> Sub<'Msg>
  Tick: (GameTime -> 'Msg) voption
  FixedStep: FixedStepConfig<'Msg> voption
  DispatchMode: DispatchMode
}

/// <summary>
/// The shared message-processing loop used by every Mibo host
/// (<c>RaylibGame</c>, <c>HeadlessRunner</c>, future backends).
/// </summary>
/// <remarks>
/// Owns all mutable loop state: the dispatch queue, current model, active
/// subscriptions, deferred-effect buffers, and the fixed-step accumulator.
/// Hosts call <see cref="M:Mibo.Elmish.ElmishLoop`2.Init"/> once after registering
/// backend services, then <see cref="M:Mibo.Elmish.ElmishLoop`2.TickFrame"/> each frame.
/// </remarks>
type ElmishLoop<'Model, 'Msg> internal (core: LoopCore<'Model, 'Msg>) =

  let msgQueue = DispatchQueue<'Msg>(core.DispatchMode)
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
  let mutable shouldQuit = false

  let dispatch(msg: 'Msg) = msgQueue.Dispatch(msg)

  let execCmd(cmd: Cmd<'Msg>) =
    match cmd with
    | Empty -> ()
    | Msg msg -> dispatch msg
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
    subStack.Add(core.Subscribe ctx state)
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

  /// <summary>Whether the loop has received a <c>Cmd.Quit</c> signal.</summary>
  member _.ShouldQuit = shouldQuit

  /// <summary>The current model state.</summary>
  member _.Model = state

  /// <summary>The <see cref="T:Mibo.Elmish.GameContext"/> passed to <see cref="M:Mibo.Elmish.ElmishLoop`2.Init"/>, if initialized.</summary>
  member _.Context = ctxOpt

  /// <summary>The registered active subscriptions (for host-side disposal).</summary>
  member internal _.ActiveSubs = activeSubs

  /// <summary>Dispatch a message into the loop's queue.</summary>
  member _.Dispatch(msg: 'Msg) = dispatch msg

  /// <summary>
  /// Initialize the loop: store the context, call the program's <c>Init</c>,
  /// execute startup commands, and start initial subscriptions.
  /// </summary>
  /// <remarks>Call exactly once, after the host has registered backend services.</remarks>
  member _.Init(ctx: GameContext) =
    ctxOpt <- ValueSome ctx
    let struct (initialState, initialCmds) = core.Init ctx
    state <- initialState
    execCmd initialCmds
    updateSubs ctx

  /// <summary>
  /// Advance the simulation by one frame: drain deferred effects, run fixed-step,
  /// dispatch the tick message, process all queued messages, and update
  /// subscriptions if the model changed.
  /// </summary>
  /// <param name="elapsed">Frame delta (e.g. <c>TimeSpan.FromMilliseconds(16)</c> for 60fps).</param>
  /// <param name="gameTime">The current game time, supplied by the host.</param>
  /// <returns><c>true</c> if the model changed this frame; <c>false</c> otherwise.</returns>
  member _.TickFrame(elapsed: TimeSpan, gameTime: GameTime) : bool =
    let deltaSeconds = float32 elapsed.TotalSeconds

    if deferredEffs.Count <> 0 then
      deferredEffsRun.Clear()
      deferredEffsRun.AddRange(deferredEffs)
      deferredEffs.Clear()

      for i = 0 to deferredEffsRun.Count - 1 do
        deferredEffsRun[i].Invoke(dispatch)

    match core.FixedStep with
    | ValueNone -> ()
    | ValueSome cfg ->
      let maxFrame = cfg.MaxFrameSeconds |> ValueOption.defaultValue 0.25f

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

    core.Tick |> ValueOption.iter(fun map -> dispatch(map gameTime))

    let mutable stateChanged = false
    let mutable msg = Unchecked.defaultof<'Msg>
    msgQueue.StartBatch()

    while msgQueue.TryDequeue(&msg) do
      let struct (newState, cmds) = core.Update msg state
      state <- newState
      execCmd cmds
      stateChanged <- true

    msgQueue.EndBatch()

    if stateChanged then
      ctxOpt |> ValueOption.iter(updateSubs)

    stateChanged

  /// <summary>
  /// Dispose all active subscriptions. Hosts should call this on shutdown.
  /// </summary>
  member _.DisposeSubs() =
    for KeyValue(_key, disp) in activeSubs do
      disp.Dispose()

    activeSubs.Clear()

/// Functions for constructing and working with <see cref="T:Mibo.Elmish.ElmishLoop`2"/>.
module ElmishLoop =

  /// <summary>Creates an <see cref="T:Mibo.Elmish.ElmishLoop`2"/> from a <see cref="T:Mibo.Elmish.LoopCore`2"/>.</summary>
  let create(core: LoopCore<'Model, 'Msg>) = ElmishLoop<'Model, 'Msg>(core)

  /// <summary>Projects a <see cref="T:Mibo.Elmish.Program`2"/> to a <see cref="T:Mibo.Elmish.LoopCore`2"/>.</summary>
  let coreOfProgram(program: Program<'Model, 'Msg>) : LoopCore<'Model, 'Msg> = {
    Init = program.Init
    Update = program.Update
    Subscribe = program.Subscribe
    Tick = program.Tick
    FixedStep = program.FixedStep
    DispatchMode = program.DispatchMode
  }
