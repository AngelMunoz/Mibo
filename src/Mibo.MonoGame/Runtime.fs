namespace Mibo.Elmish

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Input

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
  let mutable inputServiceOpt: IInput voption = ValueNone
  let mutable ctxOpt: GameContext voption = ValueNone

  // Target frame interval for the software FPS cap (TimeSpan.Zero = uncapped).
  let mutable targetInterval = TimeSpan.Zero

  // Stopwatch for the software FPS cap. We measure from an absolute reference
  // timestamp (nextFrameTime) and advance it by exactly targetInterval each
  // frame, drift-correcting — the same approach raylib's SetTargetFPS uses
  // (accumulate toward an absolute target, not "sleep for target - now").
  // This avoids the jitter/chop that "sleep for (target - elapsed)" produces,
  // because Thread.Sleep oversleeps by 1-4ms and relative sleeps drift.
  let stopWatch = System.Diagnostics.Stopwatch.StartNew()
  let mutable nextFrameTime = 0.0 // seconds, on the stopwatch timeline

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

    // Always run a variable-timestep loop, matching the raylib host's
    // SetTargetFPS behavior (frame-rate-capped, but each frame steps the
    // simulation by real elapsed time). With IsFixedTimeStep=true + VSync
    // off, MonoGame clamps ElapsedGameTime to TargetElapsedTime and may run
    // 0 or N Updates per Draw, causing stutter. Variable timestep gives one
    // Update per Draw with real elapsed time — same shape as RaylibGame.
    graphics.SynchronizeWithVerticalRetrace <- false
    this.IsFixedTimeStep <- false

    if config.TargetFPS > 0 then
      // Cap the frame rate without forcing fixed-step. MonoGame has no
      // native FPS cap, so we apply a Thread.Sleep at the end of Update
      // when the host is running faster than TargetFPS (see Update below).
      targetInterval <- TimeSpan.FromSeconds(1.0 / float config.TargetFPS)
    else
      // Unlocked: enable VSync instead of sleeping.
      graphics.SynchronizeWithVerticalRetrace <- true

    this.Window.Title <- config.Title
    this.IsMouseVisible <- true

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

    // Software FPS cap using an absolute reference timestamp, mirroring
    // raylib's SetTargetFPS: advance nextFrameTime by exactly targetInterval
    // each frame and sleep until that absolute point on the stopwatch timeline.
    // This is drift-free and avoids the chop that "sleep for (target - now)"
    // produces (Thread.Sleep oversleeps 1-4ms and relative sleeps accumulate
    // drift). If we fell behind by more than one interval (a hiccup), we resync
    // to now rather than burst-catch-up. TargetFPS <= 0 is uncapped (VSync).
    if targetInterval > TimeSpan.Zero then
      let now = stopWatch.Elapsed.TotalSeconds
      // Initialize the reference on the first capped frame.
      if nextFrameTime = 0.0 then
        nextFrameTime <- now

      let target = nextFrameTime + targetInterval.TotalSeconds

      if now < target then
        // On pace: sleep the remainder, capped to one interval to avoid
        // pathological oversleeps compounding.
        let remaining = target - now
        let maxSleep = targetInterval.TotalSeconds
        let sleepSec = min remaining maxSleep
        System.Threading.Thread.Sleep(int(sleepSec * 1000.0))
        nextFrameTime <- target
      else
        // Fell behind (work or a hiccup took longer than the interval):
        // resync to now so we don't try to catch up in a burst.
        nextFrameTime <- now

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
