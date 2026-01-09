namespace Mibo.Elmish

open System
open System.Collections.Concurrent
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// Internal message queue that supports optional frame-bounded dispatch.
/// </summary>
/// <remarks>
/// This type exists to keep the runtime behavior testable without requiring a MonoGame
/// GraphicsDevice in unit tests.
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
        // Swap queues so that messages dispatched during processing become the
        // pending messages for the next frame.
        let tmp = current
        current <- next
        next <- tmp)

  member _.TryDequeue(msg: byref<'Msg>) = current.TryDequeue(&msg)


type ElmishGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) as this =
  inherit Game()

  let graphics = new GraphicsDeviceManager(this)
  let mutable state: 'Model = Unchecked.defaultof<'Model>
  let mutable ctxOpt: GameContext voption = ValueNone
  let msgQueue = DispatchQueue<'Msg>(program.DispatchMode)
  let activeSubs = Dictionary<SubId, IDisposable>()
  let subIdsInUse = HashSet<SubId>()
  let subIdsToRemove = ResizeArray<SubId>(32)
  let renderers = ResizeArray<IRenderer<'Model>>()
  let subBuffer = ResizeArray<struct (SubId * Subscribe<'Msg>)>()
  let subStack = ResizeArray<Sub<'Msg>>()
  let deferredEffs = ResizeArray<Effect<'Msg>>(64)
  let deferredEffsRun = ResizeArray<Effect<'Msg>>(64)

  let dispatch(msg: 'Msg) = msgQueue.Dispatch(msg)

  // Fixed-step accumulator (seconds). Only used when program.FixedStep is enabled.
  let mutable fixedAccSeconds = 0.0f

  let execCmd(cmd: Cmd<'Msg>) =
    match cmd with
    | Empty -> ()
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

    // Remove any subscriptions that are no longer present.
    // Avoid allocating an array of keys; gather to a reusable buffer first.
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
    // Apply user configuration or use sensible defaults
    match program.Config with
    | ValueSome configure -> configure(this, graphics)
    | ValueNone ->
      // Default settings (can be overridden via Program.withConfig)
      this.Content.RootDirectory <- "Content"
      this.IsMouseVisible <- true
      this.Window.AllowUserResizing <- true
      graphics.PreferredBackBufferWidth <- 800
      graphics.PreferredBackBufferHeight <- 600

  override _.Initialize() =
    // Add MonoGame components *before* base.Initialize() so they receive Initialize/LoadContent
    // lifecycle callbacks according to MonoGame's normal component pipeline.
    for f in program.Components do
      try
        this.Components.Add(f this) |> ignore
      with ex ->
        Console.WriteLine($"Error adding component: {ex}")

    for f in program.Renderers do
      renderers.Add(f this)

    base.Initialize()

  override _.LoadContent() =
    base.LoadContent()
    // Create context and call init after content is ready
    let ctx = {
      GraphicsDevice = this.GraphicsDevice
      Content = this.Content
      Game = this
    }

    // Persist the context for the lifetime of this game instance.
    ctxOpt <- ValueSome ctx

    let struct (initialState, initialCmds) = program.Init ctx
    state <- initialState

    execCmd initialCmds
    updateSubs ctx

  override _.Update gameTime =
    // MonoGame calls LoadContent before the first Update, but be defensive.
    match ctxOpt with
    | ValueNone -> base.Update gameTime
    | ValueSome ctx ->
    // Run MonoGame components first (e.g. input polling) so any messages
    // dispatched via subscriptions can be processed in this same frame.
    base.Update gameTime

    // Execute commands deferred from the previous frame before we enqueue Tick.
    // This provides a deterministic "next-frame" boundary without changing the Elmish
    // `update : Msg -> Model -> Model * Cmd` contract.
    if deferredEffs.Count <> 0 then
      deferredEffsRun.Clear()
      deferredEffsRun.AddRange(deferredEffs)
      deferredEffs.Clear()

      for i = 0 to deferredEffsRun.Count - 1 do
        deferredEffsRun[i].Invoke(dispatch)

    // Optional fixed-timestep stepping.
    match program.FixedStep with
    | ValueNone -> ()
    | ValueSome cfg ->
      let dtSec = float32 gameTime.ElapsedGameTime.TotalSeconds
      let maxFrame = cfg.MaxFrameSeconds |> ValueOption.defaultValue 0.25f

      let struct (acc2, steps, _dropped) =
        FixedStep.compute
          cfg.StepSeconds
          cfg.MaxStepsPerFrame
          maxFrame
          fixedAccSeconds
          dtSec

      fixedAccSeconds <- acc2

      for _i = 1 to steps do
        // Dispatch step messages before the batch starts so they are eligible this frame
        // even under FrameBounded dispatch mode.
        dispatch(cfg.Map cfg.StepSeconds)

    // Optional per-frame tick message.
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
      updateSubs ctx

  override _.Draw gameTime =
    match ctxOpt with
    | ValueNone -> base.Draw gameTime
    | ValueSome ctx ->
      for i = 0 to renderers.Count - 1 do
        renderers[i].Draw(ctx, state, gameTime)

      base.Draw gameTime
