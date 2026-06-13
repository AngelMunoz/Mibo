module PingPong.Shared.Physics

open System
open System.Numerics
open PingPong.Shared.Types

let updateBall
  (width: float32)
  (height: float32)
  (ball: Ball)
  (leftPaddle: Paddle)
  (rightPaddle: Paddle)
  (dt: float32)
  : Ball =
  let mutable pos = ball.Position + ball.Velocity * dt
  let mutable vel = ball.Velocity

  if pos.Y - ballRadius < 0f then
    pos <- Vector2(pos.X, ballRadius)
    vel <- Vector2(vel.X, -vel.Y)
  elif pos.Y + ballRadius > height then
    pos <- Vector2(pos.X, height - ballRadius)
    vel <- Vector2(vel.X, -vel.Y)

  if vel.X < 0f then
    let paddleRight = paddleWidth

    if pos.X - ballRadius < paddleRight && pos.X + ballRadius > 0f then
      if
        pos.Y > leftPaddle.Y - paddleHeight / 2f
        && pos.Y < leftPaddle.Y + paddleHeight / 2f
      then
        pos <- Vector2(paddleRight + ballRadius, pos.Y)
        vel <- Vector2(-vel.X, vel.Y)

  if vel.X > 0f then
    let paddleLeft = width - paddleWidth

    if pos.X + ballRadius > paddleLeft && pos.X - ballRadius < width then
      if
        pos.Y > rightPaddle.Y - paddleHeight / 2f
        && pos.Y < rightPaddle.Y + paddleHeight / 2f
      then
        pos <- Vector2(paddleLeft - ballRadius, pos.Y)
        vel <- Vector2(-vel.X, vel.Y)

  { Position = pos; Velocity = vel }

let clampPaddle (height: float32) (y: float32) : float32 =
  let halfHeight = paddleHeight / 2f
  max halfHeight (min (height - halfHeight) y)

let step (rng: Random) (model: GameState) (dt: float32) : GameState =
  let newBall =
    updateBall
      model.Width
      model.Height
      model.Ball
      model.LeftPaddle
      model.RightPaddle
      dt

  let mutable scores = model.Scores
  let mutable newBall' = newBall

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

let lerp (a: float32) (b: float32) (t: float32) = a + (b - a) * t
