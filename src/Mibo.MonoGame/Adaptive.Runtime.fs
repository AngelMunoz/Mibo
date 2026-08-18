namespace Mibo.Adaptive

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Input
open Mibo.Elmish
open Mibo.Windowing

// ─────────────────────────────────────────────────────────────────────────────
// AdaptiveMonoGameGame: the MonoGame-backed host for adaptive programs.
//
// Mirrors MiboGame's structure (ctor config → Initialize → LoadContent →
// Update → Draw → Dispose) but delegates the per-frame simulation to an
// AdaptiveHeadless runner instead of the Elmish message-processing loop. This
// host is only the I/O shell: window/graphics lifecycle, input polling,
// rendering dispatch, and the per-frame time mapping.
//
// Work ordering each frame (matches MiboGame + AdaptiveHeadless.Step):
//   Update: poll input → resize check → Step (posts → time root → Update →
//           force frame) → exit check
//   Draw:   iterate renderers in add-order (reversed, since the list is
//           ::-prepended)
//
// Intentional differences from MiboGame (the adaptive contract):
//   - No Program/Cmd/Sub machinery — the program is an AdaptiveProgram.
//   - Input is opt-in: registered and polled only when the program opted in
//     via AdaptiveProgram.withInput (mirrors MiboGame's HasInput gate).
//   - The runner builds the graph lazily on the first Step.
//   - The runner owns the GameTime; the host reads runner.GameTime for draw.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The MonoGame-backed adaptive host. Subclasses
/// <c>Microsoft.Xna.Framework.Game</c> and drives an
/// <see cref="T:Mibo.Adaptive.AdaptiveHeadless`1"/> runner from
/// <c>Update</c>/<c>Draw</c>.
/// </summary>
/// <remarks>
/// Construct one with an
/// <see cref="T:Mibo.Adaptive.AdaptiveMonoGameProgram`1"/> and call
/// <c>Run()</c>. The host owns the MonoGame window/graphics lifecycle and
/// delegates simulation to the adaptive runner — it holds no simulation state
/// of its own.
/// </remarks>
type AdaptiveMonoGameGame<'Frame>(mgProgram: AdaptiveMonoGameProgram<'Frame>) as this
  =
  inherit Game()

  let program = mgProgram.Program

  let graphics =
    new GraphicsDeviceManager(this, GraphicsProfile = GraphicsProfile.HiDef)

  let renderers = ResizeArray<IRenderer<'Frame>>()
  let mutable inputServiceOpt: IInput voption = ValueNone
  let mutable ctxOpt: GameContext voption = ValueNone
  let mutable runnerOpt: AdaptiveHeadless<'Frame> voption = ValueNone

  // ── Config: apply the cumulative GameConfig callbacks once, in the ctor,
  // before Initialize runs (mirrors MiboGame applying config before device
  // creation, and RaylibGame applying config before InitWindow).
  do
    let config =
      List.fold
        (fun c f -> f c)
        GameConfig.defaultConfig
        (List.rev program.Config)

    graphics.PreferredBackBufferWidth <- config.Width
    graphics.PreferredBackBufferHeight <- config.Height

    // Resizable + presentation mode, before DeviceConfig callbacks so a game
    // can still override.
    MonoGameWindow.ApplyConfig(config, this, graphics)

    // Keep backbuffer contents across mid-frame render-target switches (mirrors
    // MiboGame — the 3D pipelines rebind the backbuffer after offscreen passes;
    // on backends that honor DiscardContents at rebind, everything drawn before
    // the switch is wiped without this).
    graphics.PreparingDeviceSettings.AddHandler(fun _ args ->
      args.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage <-
        RenderTargetUsage.PreserveContents)

    this.Window.Title <- config.Title
    this.IsMouseVisible <- true

    config.TargetFPS
    |> ValueOption.iter(fun fps ->
      this.IsFixedTimeStep <- true
      this.TargetElapsedTime <- TimeSpan.FromSeconds(1.0 / float fps))

    // Apply device-level config callbacks after the Core GameConfig. These run
    // before Initialize/GraphicsDevice creation, so settings like
    // GraphicsProfile, vsync, fullscreen, and window policy take effect.
    for configure in List.rev mgProgram.DeviceConfig do
      configure(this, graphics)

  // Constructed after the ctor's ApplyConfig, so it reads the resolved mode.
  let windowService = MonoGameWindow(graphics)

  // ── Initialize: build renderers (the MG GraphicsDevice exists by now).
  // LoadContent (below) finishes wiring and constructs the runner.
  override _.Initialize() =
    // Reverse to match add-order (the list is ::-prepended, like Config and
    // ServiceRegistrations). Same rationale as MiboGame.
    for f in List.rev program.Renderers do
      renderers.Add(f())

    base.Initialize()

  // ── LoadContent: build the GameContext, register MonoGame handles + backend
  // services, then construct the runner (lazy — the graph builds on the first
  // Step).
  override _.LoadContent() =
    base.LoadContent()

    let ctx =
      GameContext.create(
        this.Window.ClientBounds.Width,
        this.Window.ClientBounds.Height
      )

    ctxOpt <- ValueSome ctx

    // Register MonoGame handles so user init/update code can resolve them.
    MonoGameGameContext.register this ctx

    GameContext.register<IWindow> windowService ctx

    // Register the MonoGame asset service (always, mirroring MiboGame and
    // AdaptiveRaylibGame which register IAssets unconditionally).
    let assets = AssetsService.create this.Content
    GameContext.register<IAssets> assets ctx

    // Input is opt-in via AdaptiveProgram.withInput — mirrors MiboGame's
    // HasInput gate. The program reads the IInput service directly through
    // the context when it is enabled.
    if program.HasInput then
      let inputService = Input.create this
      GameContext.register<IInput> inputService ctx
      inputServiceOpt <- ValueSome inputService

    // User service registrations run before the runner builds the graph (the
    // runner initializes on its first Step). Mirrors MiboGame's reversal so
    // registration order matches add-order.
    for register in List.rev program.ServiceRegistrations do
      register ctx

    let runner = new AdaptiveHeadless<'Frame>(program, context = ctx)
    runnerOpt <- ValueSome runner

  // ── Update: poll hardware input, then advance the runner by one step.
  // NOTE: GameTime is fully-qualified because both Microsoft.Xna.Framework and
  // Mibo.Elmish define a GameTime; the host receives MonoGame's.
  override _.Update(gameTime: Microsoft.Xna.Framework.GameTime) =
    base.Update gameTime

    inputServiceOpt |> ValueOption.iter(fun svc -> svc.Poll())

    match struct (ctxOpt, runnerOpt) with
    | ValueSome ctx, ValueSome runner ->
      // Track the client area into the backbuffer and the context dims.
      MonoGameWindow.SyncBackBuffer(this, graphics, ctx)

      // The runner owns the clock: it builds the GameTime from the elapsed
      // delta and exposes runner.GameTime for the draw call.
      runner.Step(gameTime.ElapsedGameTime) |> ignore

      if runner.ShouldQuit then
        this.Exit()

    | _ -> ()

  // ── Draw: render in add-order, using the runner's forced frame and clock.
  // No host clear (mirrors MiboGame — renderers own their clear). The runner
  // owns the clock, so the host's GameTime is only forwarded to base.Draw.
  override _.Draw(gameTime: Microsoft.Xna.Framework.GameTime) =
    base.Draw gameTime

    match struct (ctxOpt, runnerOpt) with
    | ValueSome ctx, ValueSome runner ->
      for i = 0 to renderers.Count - 1 do
        renderers[i].Draw(ctx, runner.Frame, runner.GameTime)
    | _ -> ()

  override _.Dispose(disposing) =
    if disposing then
      for i = 0 to renderers.Count - 1 do
        match renderers[i] with
        | :? IDisposable as d -> d.Dispose()
        | _ -> ()

      // Mirror AdaptiveRaylibGame's teardown order: renderers → runner → assets.
      runnerOpt |> ValueOption.iter(fun runner -> runner.Dispose())

      ctxOpt
      |> ValueOption.iter(fun ctx ->
        match GameContext.tryGetService<IAssets> ctx with
        | ValueSome assets -> assets.Dispose()
        | _ -> ())

    base.Dispose(disposing)

  interface IDisposable with
    member this.Dispose() = this.Dispose(true)
