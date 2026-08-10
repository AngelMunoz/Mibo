module Mibo.Adaptive.Demo.Physics

open System.Numerics
open Types

// ── Pure simulation functions (from the PingPong sample). No adaptive code:
//    these operate on plain values; the adaptive layer is the state around
//    them. ────────────────────────────────────────────────────────────────────

let inline clampPaddle (height: float32) (y: float32) : float32 =
  let halfHeight = paddleHeight / 2f
  max halfHeight (min (height - halfHeight) y)

let updateBall
  (width: float32)
  (height: float32)
  (ball: Ball)
  (leftPaddleY: float32)
  (rightPaddleY: float32)
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
    let paddleEdge = paddleWidth

    if pos.X - ballRadius < paddleEdge && pos.X + ballRadius > 0f then
      if
        pos.Y > leftPaddleY - paddleHeight / 2f
        && pos.Y < leftPaddleY + paddleHeight / 2f
      then
        pos <- Vector2(paddleEdge + ballRadius, pos.Y)
        vel <- Vector2(-vel.X, vel.Y)

  if vel.X > 0f then
    let paddleEdge = width - paddleWidth

    if pos.X + ballRadius > paddleEdge && pos.X - ballRadius < width then
      if
        pos.Y > rightPaddleY - paddleHeight / 2f
        && pos.Y < rightPaddleY + paddleHeight / 2f
      then
        pos <- Vector2(paddleEdge - ballRadius, pos.Y)
        vel <- Vector2(-vel.X, vel.Y)

  { Position = pos; Velocity = vel }
