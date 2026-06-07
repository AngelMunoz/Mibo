namespace Mibo.Elmish

open System
open System.Collections.Concurrent
open System.Collections.Generic
open Raylib_cs
open Mibo.Input

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

type RaylibGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) =
  let msgQueue = DispatchQueue<'Msg>(program.DispatchMode)
  let mutable state: 'Model = Unchecked.defaultof<'Model>
  let mutable ctxOpt: GameContext voption = ValueNone
  let activeSubs = Dictionary<SubId, IDisposable>()
  let subIdsInUse = HashSet<SubId>()
  let subIdsToRemove = ResizeArray<SubId>(32)
  let renderers = ResizeArray<IRenderer<'Model>>()
  let subBuffer = ResizeArray<struct (SubId * Subscribe<'Msg>)>()
  let subStack = ResizeArray<Sub<'Msg>>()
  let deferredEffs = ResizeArray<Effect<'Msg>>(64)
  let deferredEffsRun = ResizeArray<Effect<'Msg>>(64)
  let mutable fixedAccSeconds = 0.0f

  let mutable shouldQuit = false
  let mutable inputServiceOpt: IInput voption = ValueNone

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

  member _.Run() =
    let config =
      List.fold
        (fun c f -> f c)
        GameConfig.defaultConfig
        (List.rev program.Config)

    Raylib.InitWindow(config.Width, config.Height, config.Title)
    Raylib.SetExitKey(KeyboardKey.Null)
    Raylib.InitAudioDevice()

    if config.TargetFPS > 0 then
      Raylib.SetTargetFPS(config.TargetFPS)

    match config.MinWidth, config.MinHeight with
    | ValueSome w, ValueSome h -> Raylib.SetWindowMinSize(w, h)
    | ValueSome w, ValueNone -> Raylib.SetWindowMinSize(w, config.Height)
    | ValueNone, ValueSome h -> Raylib.SetWindowMinSize(config.Width, h)
    | ValueNone, ValueNone -> ()

    if config.MinWidth.IsSome || config.MinHeight.IsSome then
      Raylib.SetWindowState(ConfigFlags.ResizableWindow)

    for f in program.Renderers do
      renderers.Add(f())

    let ctx = GameContext.create(config.Width, config.Height)

    let assets =
      match program.AssetsBasePath with
      | ValueSome p -> AssetsService.createWithBasePath(p)
      | ValueNone -> AssetsService.create()

    GameContext.register<IAssets> assets ctx

    if program.HasInput then
      let inputService = Input.create []
      GameContext.register<IInput> inputService ctx
      inputServiceOpt <- ValueSome inputService

    ctxOpt <- ValueSome ctx

    let struct (initialState, initialCmds) = program.Init ctx
    state <- initialState
    execCmd initialCmds
    updateSubs ctx

    while not(shouldQuit || RaylibHelpers.windowShouldClose()) do
      let dt = Raylib.GetFrameTime()
      let elapsed = TimeSpan.FromSeconds(float dt)

      let gameTime = {
        TotalTime = TimeSpan.FromSeconds(Raylib.GetTime())
        ElapsedGameTime = elapsed
      }

      // Poll hardware input before processing messages
      inputServiceOpt |> ValueOption.iter(fun svc -> svc.Poll())

      // Check for window resize and update context dimensions
      if Raylib.IsWindowResized().AsBool() then
        ctx.UpdateDimensions(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())

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

        let struct (acc2, steps, _dropped) =
          FixedStep.compute
            cfg.StepSeconds
            cfg.MaxStepsPerFrame
            maxFrame
            fixedAccSeconds
            dt

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
        updateSubs ctx

      Raylib.BeginDrawing()
      Raylib.ClearBackground(Color.Black)

      for i = 0 to renderers.Count - 1 do
        renderers[i].Draw(ctx, state, gameTime)

      Raylib.EndDrawing()

    for i = 0 to renderers.Count - 1 do
      match renderers[i] with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    (GameContext.getService<IAssets> ctx).Dispose()
    Raylib.CloseAudioDevice()
    Raylib.CloseWindow()
