module PingPong.Server.GameLogic

open System
open System.Numerics
open Mibo.Elmish
open PingPong.Shared.Types

// ── Server Messages ────────────────────────────────────────────────────────

type ServerMsg =
  | FromClient of int<peerId> * ClientMsg
  | GameTick

// ── Physics Constants ──────────────────────────────────────────────────────

let paddleWidth = 10f
let paddleHeight = 80f
let ballRadius = 8f

// ── Update Logic ───────────────────────────────────────────────────────────

let updateBall
  (ball: Ball)
  (leftPaddle: Paddle)
  (rightPaddle: Paddle)
  (dt: float32)
  : Ball =
  let mutable pos = ball.Position + ball.Velocity * dt
  let mutable vel = ball.Velocity

  // Top/bottom walls
  if pos.Y - ballRadius < 0f then
    pos <- Vector2(pos.X, ballRadius)
    vel <- Vector2(vel.X, -vel.Y)
  elif pos.Y + ballRadius > 800f then
    pos <- Vector2(pos.X, 800f - ballRadius)
    vel <- Vector2(vel.X, -vel.Y)

  // Left paddle collision
  if vel.X < 0f then
    let paddleRight = paddleWidth

    if pos.X - ballRadius < paddleRight && pos.X + ballRadius > 0f then
      if
        pos.Y > leftPaddle.Y - paddleHeight / 2f
        && pos.Y < leftPaddle.Y + paddleHeight / 2f
      then
        pos <- Vector2(paddleRight + ballRadius, pos.Y)
        vel <- Vector2(-vel.X, vel.Y)

  // Right paddle collision
  if vel.X > 0f then
    let paddleLeft = 800f - paddleWidth

    if pos.X + ballRadius > paddleLeft && pos.X - ballRadius < 800f then
      if
        pos.Y > rightPaddle.Y - paddleHeight / 2f
        && pos.Y < rightPaddle.Y + paddleHeight / 2f
      then
        pos <- Vector2(paddleLeft - ballRadius, pos.Y)
        vel <- Vector2(-vel.X, vel.Y)

  { Position = pos; Velocity = vel }

let clampPaddle(y: float32) : float32 =
  let halfHeight = paddleHeight / 2f
  max halfHeight (min (800f - halfHeight) y)

let step (model: GameState) (dt: float32) : GameState =
  let newBall = updateBall model.Ball model.LeftPaddle model.RightPaddle dt

  // Check for scoring
  let mutable scores = model.Scores
  let mutable newBall' = newBall
  let rng = Random()

  if newBall.Position.X < 0f then
    scores <- { scores with Right = scores.Right + 1 }
    let yDir = if rng.NextDouble() > 0.5 then 1.0f else -1.0f

    newBall' <- {
      Position = Vector2(model.Width / 2f, model.Height / 2f)
      Velocity = Vector2(300f, 200f * yDir)
    }
  elif newBall.Position.X > model.Width then
    scores <- { scores with Left = scores.Left + 1 }
    let yDir = if rng.NextDouble() > 0.5 then 1.0f else -1.0f

    newBall' <- {
      Position = Vector2(model.Width / 2f, model.Height / 2f)
      Velocity = Vector2(-300f, 200f * yDir)
    }

  {
    model with
        Ball = newBall'
        Scores = scores
  }

// ── Elmish Update ──────────────────────────────────────────────────────────

let init ctx =
  struct (initGameState 800f 800f, Cmd.none)

let update msg model =
  match msg with
  | GameTick -> struct (step model (1f / 60f), Cmd.none)

  | FromClient(_, clientMsg) ->
    match clientMsg with
    | MovePaddle(side, y) ->
      let clampedY = clampPaddle y

      match side with
      | Left ->
        struct ({
                  model with
                      LeftPaddle = { model.LeftPaddle with Y = clampedY }
                },
                Cmd.none)
      | Right ->
        struct ({
                  model with
                      RightPaddle = { model.RightPaddle with Y = clampedY }
                },
                Cmd.none)
