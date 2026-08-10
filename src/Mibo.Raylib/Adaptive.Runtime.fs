namespace Mibo.Elmish

open System
open Raylib_cs
open Mibo.Input
open Mibo.Adaptive

/// <summary>
/// The raylib-backed host for adaptive worlds. Owns the window/audio lifecycle
/// like <see cref="T:Mibo.Elmish.RaylibGame`2"/> and delegates the per-frame
/// simulation to an <see cref="T:Mibo.Adaptive.AdaptiveHeadless`1"/> runner.
/// </summary>
/// <remarks>
/// This is the adaptive counterpart of <see cref="T:Mibo.Elmish.RaylibGame`2"/>,
/// with the MVU ceremony removed: there is no <c>Program</c> record, no builder
/// chain, no Cmd/Sub machinery — the world is passed directly, services are
/// registered through the constructor, and input is always registered and
/// polled. Each frame: poll input, check for resize, <c>Step</c> the runner
/// (which runs the world's Update phase and forces the frame), consume a
/// restart request if the world wrote one, then draw the renderers with the
/// forced frame. The frame's transient views are valid until the next
/// <c>Step</c>, so the draw window is exactly the gap between them.
/// </remarks>
type AdaptiveRaylibGame<'Frame>
  (
    world: AdaptiveWorld<'Frame>,
    ?config: GameConfig,
    ?assetsBasePath: string,
    ?serviceRegistrations: (GameContext -> unit) list,
    ?renderers: (unit -> IRenderer<'Frame>) list
  ) =

  member _.Run() =
    let config = defaultArg config GameConfig.defaultConfig
    let serviceRegistrations = defaultArg serviceRegistrations []
    let renderers = defaultArg renderers []

    Raylib.InitWindow(config.Width, config.Height, config.Title)
    Raylib.SetExitKey(KeyboardKey.Null)
    Raylib.InitAudioDevice()

    config.TargetFPS |> ValueOption.iter Raylib.SetTargetFPS

    match config.MinWidth, config.MinHeight with
    | ValueSome w, ValueSome h -> Raylib.SetWindowMinSize(w, h)
    | ValueSome w, ValueNone -> Raylib.SetWindowMinSize(w, config.Height)
    | ValueNone, ValueSome h -> Raylib.SetWindowMinSize(config.Width, h)
    | ValueNone, ValueNone -> ()

    if config.MinWidth.IsSome || config.MinHeight.IsSome then
      Raylib.SetWindowState(ConfigFlags.ResizableWindow)

    let renderers = [| for f in renderers -> f() |]

    let ctx = GameContext.create(config.Width, config.Height)

    let assets =
      match assetsBasePath with
      | Some p -> AssetsService.createWithBasePath(p)
      | None -> AssetsService.create()

    GameContext.register<IAssets> assets ctx

    // Input is always registered and polled — the world reads the IInput
    // service directly through the context (no withInput toggle).
    let inputService = Input.create []
    GameContext.register<IInput> inputService ctx

    // User service registrations run in the order given, before the runner
    // builds the graph (the runner initializes on its first Step).
    for register in serviceRegistrations do
      register ctx

    let runner = new AdaptiveHeadless<'Frame>(world, context = ctx)

    while not(runner.ShouldQuit || RaylibHelpers.windowShouldClose()) do
      let dt = Raylib.GetFrameTime()
      let elapsed = TimeSpan.FromSeconds(float dt)

      // Poll hardware input before the world's Update phase reads it.
      inputService.Poll()

      // Check for window resize and update context dimensions.
      if Raylib.IsWindowResized().AsBool() then
        ctx.UpdateDimensions(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())

      runner.Step(elapsed) |> ignore

      // The world may have requested a rebuild (e.g. restart after game
      // over): re-run Init — fresh graph, fresh clock, first frame forced.
      if runner.RestartRequested then
        runner.Restart()

      Raylib.BeginDrawing()
      Raylib.ClearBackground(Color.Black)

      for i = 0 to renderers.Length - 1 do
        renderers[i].Draw(ctx, runner.Frame, runner.GameTime)

      Raylib.EndDrawing()

    for i = 0 to renderers.Length - 1 do
      match renderers[i] with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    runner.Dispose()
    assets.Dispose()
    Raylib.CloseAudioDevice()
    Raylib.CloseWindow()
