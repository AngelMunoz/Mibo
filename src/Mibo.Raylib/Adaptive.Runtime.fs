namespace Mibo.Adaptive

open System
open Raylib_cs
open Mibo.Input
open Mibo.Elmish
open Mibo.Windowing

// ─────────────────────────────────────────────────────────────────────────────
// AdaptiveRaylibGame: the raylib-backed host for adaptive programs.
//
// Mirrors RaylibGame's responsibilities (window/audio lifecycle, input polling,
// render dispatch) but delegates the per-frame simulation to an
// AdaptiveHeadless runner instead of the Elmish message-processing loop. The
// host is only the I/O shell: it owns no simulation state of its own.
//
// Work ordering each frame (matches RaylibGame + AdaptiveHeadless.Step):
//   poll input → resize check → Step (posts → time root → Update → force frame)
//                → draw renderers in add-order.
//
// Intentional differences from RaylibGame (the adaptive contract):
//   - No Program/Cmd/Sub machinery — the program is an AdaptiveProgram.
//   - Input is opt-in: registered and polled only when the program opted in
//     via AdaptiveProgram.withInput (mirrors RaylibGame's HasInput gate).
//   - The runner builds the graph lazily on the first Step.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The raylib-backed host for adaptive programs. Owns the window/audio
/// lifecycle like <see cref="T:Mibo.Elmish.RaylibGame`2"/> and delegates the
/// per-frame simulation to an <see cref="T:Mibo.Adaptive.AdaptiveHeadless`1"/>
/// runner.
/// </summary>
/// <remarks>
/// Construct one with an <see cref="T:Mibo.Adaptive.AdaptiveProgram`1"/> and
/// call <c>Run()</c>. Each frame: poll input, check for resize, <c>Step</c> the
/// runner (which runs the program's Update phase and forces the frame), then
/// draw the renderers with the forced frame. The frame's transient views are
/// valid until the next <c>Step</c>, so the draw window is exactly the gap
/// between them.
/// </remarks>
type AdaptiveRaylibGame<'Frame>(program: AdaptiveProgram<'Frame>) =

  /// <summary>Run the host: open the window, drive the frame loop, then tear down.</summary>
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

    // Reverse to match add-order (the list is ::-prepended, like Config and
    // ServiceRegistrations). Without this, the last renderer added would draw
    // first, and earlier renderers would draw on top — opposite of the
    // expected layering. Mirrors RaylibGame.
    let renderers = ResizeArray<IRenderer<'Frame>>()

    for f in List.rev program.Renderers do
      renderers.Add(f())

    let ctx = GameContext.create(config.Width, config.Height)

    let assets =
      match program.AssetsBasePath with
      | ValueSome p -> AssetsService.createWithBasePath(p)
      | ValueNone -> AssetsService.create()

    GameContext.register<IAssets> assets ctx
    GameContext.register<IWindow> (RaylibWindow(config.WindowMode)) ctx

    let mutable inputServiceOpt: IInput voption = ValueNone

    // Input is opt-in via AdaptiveProgram.withInput — mirrors RaylibGame's
    // HasInput gate. The program reads the IInput service directly through
    // the context when it is enabled.
    if program.HasInput then
      let inputService = Input.create []
      GameContext.register<IInput> inputService ctx
      inputServiceOpt <- ValueSome inputService

    // User service registrations run before the runner builds the graph (the
    // runner initializes on its first Step). Mirrors RaylibGame's reversal so
    // registration order matches add-order.
    for register in List.rev program.ServiceRegistrations do
      register ctx

    let runner = new AdaptiveHeadless<'Frame>(program, context = ctx)

    while not(runner.ShouldQuit || RaylibHelpers.windowShouldClose()) do
      let dt = Raylib.GetFrameTime()
      let elapsed = TimeSpan.FromSeconds(float dt)

      // Poll hardware input before the program's Update phase reads it.
      inputServiceOpt |> ValueOption.iter(fun svc -> svc.Poll())

      // Check for window resize and update context dimensions.
      if Raylib.IsWindowResized().AsBool() then
        ctx.UpdateDimensions(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())

      runner.Step(elapsed) |> ignore

      Raylib.BeginDrawing()
      Raylib.ClearBackground(Color.Black)

      for i = 0 to renderers.Count - 1 do
        renderers[i].Draw(ctx, runner.Frame, runner.GameTime)

      Raylib.EndDrawing()

    for i = 0 to renderers.Count - 1 do
      match renderers[i] with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    runner.Dispose()
    assets.Dispose()
    Raylib.CloseAudioDevice()
    Raylib.CloseWindow()
