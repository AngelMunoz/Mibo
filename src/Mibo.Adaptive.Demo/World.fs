module Mibo.Adaptive.Demo.World

open AdaptiveSlop.Core
open Mibo.Adaptive
open Mibo.Elmish
open Types

/// The composition root: every feature's roots and projections are retained
/// here, as fields of this record. The frame builder forces the projections
/// through it. For this game, this record is the store that owns the
/// long-lived graph — nothing lives at module scope.
type World = {
  /// Written by the host (keyboard or AI) every frame.
  Input: cval<InputState>

  /// Written by the host; freezes the simulation.
  Paused: cval<bool>

  LeftPaddle: Paddle.State
  RightPaddle: Paddle.State
  Ball: Ball.State
  Scores: Scores.State
}

/// Creates the world: builds every feature and wires their dependencies
/// (the ball feature receives the left paddle's root).
let create() : World =
  let leftPaddle = Paddle.create Left
  let rightPaddle = Paddle.create Right

  {
    Input = CVal.create { LeftMove = 0f; RightMove = 0f }
    Paused = CVal.create false
    LeftPaddle = leftPaddle
    RightPaddle = rightPaddle
    Ball = Ball.create leftPaddle.Y
    Scores = Scores.create()
  }

/// The per-frame simulation: a router over the features. It reads roots,
/// writes roots, and wires feature events. It runs after the time root is
/// written and before the frame is forced.
let step (world: World) (gameTime: GameTime) : unit =
  if not(AVal.getValue world.Paused) then
    let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
    let inp = AVal.getValue world.Input

    // Move both paddles, then feed the ball physics the values we just wrote
    // (no second read of the cells).
    let leftY = Paddle.move world.LeftPaddle (inp.LeftMove * paddleSpeed * dt)

    let rightY =
      Paddle.move world.RightPaddle (inp.RightMove * paddleSpeed * dt)

    match Ball.step world.Ball dt leftY rightY with
    | ValueSome side ->
      Scores.addPoint world.Scores side
      Ball.reset world.Ball side
    | ValueNone -> ()

/// The computer player moves at this fraction of full paddle speed, and only
/// defends when the ball is coming at it — a beatable opponent, so points
/// actually get scored and the score projection gets to work.
/// Tuning (8 seeds, ball 300/150 + 1.25x growth per paddle hit): at 0.5 the
/// first goal lands inside the demo's 6 s live window in every run; at 0.6+
/// it arrives after the window and the demo shows no goals.
let aiSpeedFactor = 0.5f

/// The computer player: defends only when the ball is coming at it, aiming
/// at the ball's current height with a small dead zone.
let aiMove (world: World) (isLeft: bool) : float32 =
  let b = AVal.getValue world.Ball.Value

  let incoming = isLeft && b.Velocity.X < 0f || not isLeft && b.Velocity.X > 0f

  if not incoming then
    0f
  else
    let paddle = if isLeft then world.LeftPaddle else world.RightPaddle
    let diff = b.Position.Y - AVal.getValue paddle.Y

    if diff > 3f then 1f
    elif diff < -3f then -1f
    else 0f

// ── The frame: what the renderer receives ────────────────────────────────────

/// Everything the renderer needs, resolved and packed. Drawing is plain
/// struct reads — O(1), no graph access.
[<Struct>]
type RenderFrame = {
  BallRect: Rect
  LeftPaddleRect: Rect
  RightPaddleRect: Rect
  ScoreLabel: string
  ClockLabel: string
  Threat: bool
}

/// Forcing the frame: resolve every projection once, pack the struct.
/// The clock label is passed in: it is a time-dependent projection created
/// in Init, because it depends on the runner-owned time root.
let buildFrame (world: World) (clockLabel: aval<string>) () : RenderFrame = {
  BallRect = AVal.getValue world.Ball.Rect
  LeftPaddleRect = AVal.getValue world.LeftPaddle.Rect
  RightPaddleRect = AVal.getValue world.RightPaddle.Rect
  ScoreLabel = AVal.getValue world.Scores.Label
  ClockLabel = AVal.getValue clockLabel
  Threat = AVal.getValue world.Ball.Threat
}

/// The adaptive program: Init retains the world's projections behind the
/// frame builder; Update runs the router.
let adaptiveProgram(world: World) : AdaptiveProgram<RenderFrame> =
  AdaptiveProgram.mkProgram
    (fun ctx ->
      // The HUD clock is a time-dependent projection: it is created here, in
      // Init, because it depends on the runner-owned time root.
      let clockLabel =
        CVal.value ctx.Time
        |> AVal.map(fun gt ->
          Telemetry.clockLabel <- Telemetry.clockLabel + 1
          sprintf "t = %.1f s" gt.TotalTime.TotalSeconds)

      AdaptiveInit.ofFrameBuilder(buildFrame world clockLabel))
    (fun _ctx gameTime -> step world gameTime)
