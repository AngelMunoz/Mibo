namespace Mibo.Elmish

open System
open System.Collections.Generic
open Raylib_cs
open Mibo.Audio
open Mibo.Diagnostics
open Mibo.Input
open Mibo.Windowing

/// <summary>
/// The raylib-backed game host. Owns the window/audio lifecycle and delegates
/// message processing to a shared <see cref="T:Mibo.Elmish.ElmishLoop`2"/>.
/// </summary>
type RaylibGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) =
  let loop = ElmishLoop.create(ElmishLoop.coreOfProgram program)
  let renderers = ResizeArray<IRenderer<'Model>>()
  let mutable inputServiceOpt: IInput voption = ValueNone

  member _.Run() =
    let config =
      List.fold
        (fun c f -> f c)
        GameConfig.defaultConfig
        (List.rev program.Config)

    // Window flags are pre-init only (raylib ignores SetConfigFlags once the
    // window exists), so ApplyConfig runs before InitWindow.
    RaylibWindow.ApplyConfig(config)
    Raylib.InitWindow(config.Width, config.Height, config.Title)
    Raylib.SetExitKey(KeyboardKey.Null)
    Raylib.InitAudioDevice()

    config.TargetFPS |> ValueOption.iter Raylib.SetTargetFPS

    match config.MinWidth, config.MinHeight with
    | ValueSome w, ValueSome h -> Raylib.SetWindowMinSize(w, h)
    | ValueSome w, ValueNone -> Raylib.SetWindowMinSize(w, config.Height)
    | ValueNone, ValueSome h -> Raylib.SetWindowMinSize(config.Width, h)
    | ValueNone, ValueNone -> ()

    // Reverse to match add-order (the list is ::-prepended, like Config).
    // Without this, the last renderer added would draw first, and earlier
    // renderers would draw on top — opposite of the expected layering.
    for f in List.rev program.Renderers do
      renderers.Add(f())

    let ctx = GameContext.create(config.Width, config.Height)

    let assets =
      match program.AssetsBasePath with
      | ValueSome p -> AssetsService.createWithBasePath(p)
      | ValueNone -> AssetsService.create()

    GameContext.register<IAssets> assets ctx
    GameContext.register<IWindow> (RaylibWindow(config.WindowMode)) ctx

    // The audio service is registered unconditionally (like IAssets): the
    // program's bank registrations resolve it, and Tick/Dispose run whether
    // or not the program ever plays a sound. Paths resolve against the
    // program's asset base path, the same rule as the asset service.
    let audio = new AudioService(program.AssetsBasePath)
    GameContext.register<IAudio> audio ctx
    GameContext.register<AudioService> audio ctx

    // Only a profiler supplied with withProfiler is registered and measured.
    let profilerOpt = program.Profiler

    profilerOpt
    |> ValueOption.iter(fun profiler ->
      GameContext.register<FrameProfiler> profiler ctx)

    if program.HasInput then
      let inputService = Input.create []
      GameContext.register<IInput> inputService ctx
      inputServiceOpt <- ValueSome inputService

    // Run backend-specific service registrations (e.g. IInputMapper) before
    // Init so user init code can see every registered service.
    for register in List.rev program.ServiceRegistrations do
      register ctx

    // Initialize the shared loop (calls program.Init, execCmd, updateSubs).
    loop.Init(ctx)

    while not(loop.ShouldQuit || RaylibHelpers.windowShouldClose()) do
      (match profilerOpt with
       | ValueSome profiler -> profiler.BeginFrame()
       | ValueNone -> ())

      let dt = Raylib.GetFrameTime()
      let elapsed = TimeSpan.FromSeconds(float dt)

      let gameTime = {
        TotalTime = TimeSpan.FromSeconds(Raylib.GetTime())
        ElapsedGameTime = elapsed
      }

      // Poll hardware input before processing messages
      inputServiceOpt |> ValueOption.iter(fun svc -> svc.Poll())

      // Advance music streaming and fade interpolation.
      audio.Tick(dt)

      // Check for window resize and update context dimensions
      if Raylib.IsWindowResized().AsBool() then
        ctx.UpdateDimensions(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())

      // Advance the shared message-processing loop (deferred drain + FixedStep
      // + tick + message pump + subscription diffing).
      loop.TickFrame(elapsed, gameTime) |> ignore

      (match profilerOpt with
       | ValueSome profiler -> profiler.EndUpdate()
       | ValueNone -> ())

      Raylib.BeginDrawing()
      Raylib.ClearBackground(Color.Black)

      (match profilerOpt with
       | ValueSome profiler -> profiler.BeginDraw()
       | ValueNone -> ())

      for i = 0 to renderers.Count - 1 do
        renderers[i].Draw(ctx, loop.Model, gameTime)

      (match profilerOpt with
       | ValueSome profiler -> profiler.EndDraw()
       | ValueNone -> ())

      // Capture after the last draw call and before the swap, so the frame is
      // complete.
      (match profilerOpt with
       | ValueSome profiler ->
         match profiler.DrainScreenshot() with
         | ValueSome path -> RaylibDiagnostics.captureScreenshot path
         | ValueNone -> ()
       | ValueNone -> ())

      Raylib.EndDrawing()

    for i = 0 to renderers.Count - 1 do
      match renderers[i] with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    loop.DisposeSubs()
    (GameContext.getService<IAssets> ctx).Dispose()
    audio.Dispose()
    Raylib.CloseAudioDevice()
    Raylib.CloseWindow()
