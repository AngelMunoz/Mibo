namespace Mibo.Elmish

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Input
open Mibo.Windowing

// ─────────────────────────────────────────────────────────────────────────────
// MiboGame: the MonoGame-backed Elmish host.
//
// Mirrors RaylibGame's responsibilities, but expressed through the MonoGame
// Game lifecycle (Initialize → LoadContent → Update → Draw) instead of a flat
// Run() loop. Owns no message-processing state of its own — it delegates all of
// that (dispatch queue, execCmd, updateSubs, deferred effects, FixedStep, tick,
// message pump) to the shared ElmishLoop (Mibo.Core). This host is only the
// I/O shell: window/graphics lifecycle, input polling, rendering dispatch, and
// the per-frame time mapping.
//
// Work ordering each frame (matches ElmishLoop.TickFrame + RaylibGame):
//   Update: poll input → ElmishLoop.TickFrame (deferred drain → FixedStep →
//           tick → message pump → subscription diff)
//   Draw:   iterate renderers in add-order (reversed, since the list is
//           ::-prepended)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The MonoGame-backed game host. Subclasses <c>Microsoft.Xna.Framework.Game</c>
/// and drives a shared <see cref="T:Mibo.Elmish.ElmishLoop`2"/> from
/// <c>Update</c>/<c>Draw</c>.
/// </summary>
/// <remarks>
/// Construct one with a <see cref="T:Mibo.Elmish.Program`2"/> and call
/// <c>Run()</c>. The host owns the MonoGame window/graphics lifecycle and
/// delegates message processing to <c>ElmishLoop</c> — it holds no
/// dispatch/update state of its own.
/// </remarks>
type MiboGame<'Model, 'Msg>(mgProgram: MonoGameProgram<'Model, 'Msg>) as this =
  inherit Game()

  let program = mgProgram.Program

  let graphics =
    new GraphicsDeviceManager(this, GraphicsProfile = GraphicsProfile.HiDef)

  let loop = ElmishLoop.create(ElmishLoop.coreOfProgram program)
  let renderers = ResizeArray<IRenderer<'Model>>()
  let mutable inputServiceOpt: IInput voption = ValueNone
  let mutable ctxOpt: GameContext voption = ValueNone

  // The cumulative GameConfig, resolved once (mirrors RaylibGame applying
  // config before InitWindow).
  let config =
    List.fold
      (fun c f -> f c)
      GameConfig.defaultConfig
      (List.rev program.Config)

  // ── Config: apply it in the ctor, before Initialize runs.
  do
    graphics.PreferredBackBufferWidth <- config.Width
    graphics.PreferredBackBufferHeight <- config.Height

    // Resizable + presentation mode, before DeviceConfig callbacks so a game
    // can still override.
    MonoGameWindow.ApplyConfig(config, this, graphics)

    // Keep backbuffer contents across mid-frame render-target switches. The 3D
    // pipelines rebind the backbuffer after offscreen passes (shadow atlas, scene
    // RT for post-processing); on backends that honor DiscardContents at rebind
    // (DX12-native), everything drawn before the switch — the frame clear, earlier
    // camera blocks — is wiped. Registered before DeviceConfig callbacks so a game
    // can still override.
    graphics.PreparingDeviceSettings.AddHandler(fun _ args ->
      args.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage <-
        RenderTargetUsage.PreserveContents)

    this.Window.Title <- config.Title
    this.IsMouseVisible <- true

    config.TargetFPS
    |> ValueOption.iter(fun fps ->
      this.IsFixedTimeStep <- true
      this.TargetElapsedTime <- TimeSpan.FromSeconds(1.0 / float fps))

    // Apply device-level config callbacks after the Core GameConfig.
    // These run before Initialize/GraphicsDevice creation, so settings like
    // GraphicsProfile, vsync, fullscreen, and window policy take effect.
    for configure in List.rev mgProgram.DeviceConfig do
      configure(this, graphics)

  // Constructed after the ctor's ApplyConfig, so it reads the resolved mode.
  let windowService =
    MonoGameWindow(
      graphics,
      config.MinWidth |> ValueOption.defaultValue 0,
      config.MinHeight |> ValueOption.defaultValue 0
    )

  // ── Initialize: build renderers (the MG GraphicsDevice exists by now).
  // LoadContent (below) finishes wiring and starts the loop.
  override _.Initialize() =
    // Reverse to match add-order (the list is ::-prepended, like Config and
    // ServiceRegistrations). Same rationale as RaylibGame: without this the
    // last renderer added draws first.
    for f in List.rev program.Renderers do
      renderers.Add(f())

    base.Initialize()

  // ── LoadContent: build the GameContext, register MonoGame handles + backend
  // services, then start the shared loop (calls program.Init, execCmd, subs).
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

    // Register the MonoGame asset service (always, mirroring RaylibGame which
    // registers IAssets unconditionally). Built over the host's ContentManager.
    let assets = AssetsService.create this.Content
    GameContext.register<IAssets> assets ctx

    if program.HasInput then
      let inputService = Input.create this
      GameContext.register<IInput> inputService ctx
      inputServiceOpt <- ValueSome inputService

    // Run backend-specific service registrations (e.g. IInputMapper) before
    // Init so user init code sees every registered service.
    for register in List.rev program.ServiceRegistrations do
      register ctx

    // Initialize the shared loop (calls program.Init, execCmd, updateSubs).
    loop.Init(ctx)

  // ── Update: poll hardware input, then advance the shared loop.
  override _.Update(gameTime: GameTime) =
    base.Update gameTime

    inputServiceOpt |> ValueOption.iter(fun svc -> svc.Poll())

    match ctxOpt with
    | ValueSome ctx ->
      // Track the client area into the backbuffer and the context dims.
      windowService.SyncBackBuffer(this, ctx)

      loop.TickFrame(
        gameTime.ElapsedGameTime,
        {
          TotalTime = gameTime.TotalGameTime
          ElapsedGameTime = gameTime.ElapsedGameTime
        }
      )
      |> ignore
    | ValueNone -> ()

    if loop.ShouldQuit then
      this.Exit()

  // ── Draw: render in add-order. Mibo.MonoGame ships no default renderers;
  // users implement IRenderer<'Model> against SpriteBatch/draw calls.
  override _.Draw(gameTime: GameTime) =
    base.Draw gameTime

    match ctxOpt with
    | ValueSome ctx ->
      let gameTime = {
        TotalTime = gameTime.TotalGameTime
        ElapsedGameTime = gameTime.ElapsedGameTime
      }

      for i = 0 to renderers.Count - 1 do
        renderers[i].Draw(ctx, loop.Model, gameTime)
    | ValueNone -> ()

  override _.Dispose(disposing) =
    if disposing then
      for i = 0 to renderers.Count - 1 do
        match renderers[i] with
        | :? IDisposable as d -> d.Dispose()
        | _ -> ()

      ctxOpt
      |> ValueOption.iter(fun ctx ->
        match GameContext.tryGetService<IAssets> ctx with
        | ValueSome assets -> assets.Dispose()
        | _ -> ())

      loop.DisposeSubs()

    base.Dispose(disposing)

  interface IDisposable with
    member this.Dispose() = this.Dispose(true)
