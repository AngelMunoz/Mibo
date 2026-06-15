namespace Mibo.Elmish

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

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
type MiboGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) as this =
  inherit Game()

  let graphics = new GraphicsDeviceManager(this)

  let loop = ElmishLoop.create(ElmishLoop.coreOfProgram program)
  let renderers = ResizeArray<IRenderer<'Model>>()
  // inputServiceOpt is wired in a later Phase 4 step when Mibo.MonoGame.Input lands.
  let mutable ctxOpt: GameContext voption = ValueNone

  // ── Config: apply the cumulative GameConfig callbacks once, in the ctor,
  // before Initialize runs (mirrors RaylibGame applying config before InitWindow).
  do
    let config =
      List.fold
        (fun c f -> f c)
        GameConfig.defaultConfig
        (List.rev program.Config)

    graphics.PreferredBackBufferWidth <- config.Width
    graphics.PreferredBackBufferHeight <- config.Height

    graphics.SynchronizeWithVerticalRetrace <- config.TargetFPS <= 0

    if config.TargetFPS > 0 then
      this.TargetElapsedTime <-
        TimeSpan.FromSeconds(1.0 / float config.TargetFPS)

    this.Window.Title <- config.Title
    this.IsMouseVisible <- true

  // ── Initialize: build renderers and register input (the MG GraphicsDevice
  // exists by now). LoadContent (below) finishes wiring and starts the loop.
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

    // NOTE: input service registration (IInput) lands in a later Phase 4 step
    // when the Mibo.MonoGame input module exists. For now, programs that use
    // input subscriptions won't find an IInput in the registry.

    // Run backend-specific service registrations (e.g. IInputMapper) before
    // Init so user init code sees every registered service.
    for register in List.rev program.ServiceRegistrations do
      register ctx

    // Initialize the shared loop (calls program.Init, execCmd, updateSubs).
    loop.Init(ctx)

  // ── Update: advance the shared loop. (Input polling lands in a later step.)
  override _.Update(gameTime: GameTime) =
    base.Update gameTime

    match ctxOpt with
    | ValueSome ctx ->
      // Reflect window resize into the portable context dimensions.
      let bounds = this.Window.ClientBounds

      if
        bounds.Width <> ctx.WindowWidth || bounds.Height <> ctx.WindowHeight
      then
        ctx.UpdateDimensions(bounds.Width, bounds.Height)

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

      loop.DisposeSubs()

    base.Dispose(disposing)

  interface IDisposable with
    member this.Dispose() = this.Dispose(true)
