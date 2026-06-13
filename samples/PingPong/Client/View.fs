module PingPong.Client.View

open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open PingPong.Shared.Types

// ── Constants ──────────────────────────────────────────────────────────────

let paddleWidth = 10f
let paddleHeight = 80f
let ballRadius = 8f

// ── View ───────────────────────────────────────────────────────────────────

let view (_ctx: GameContext) (model: GameState) (buffer: RenderBuffer2D) =
  // Draw paddles
  Command2D.fillRect
    (0<RenderLayer>, Color.White)
    (Rectangle(
      0f,
      model.LeftPaddle.Y - paddleHeight / 2f,
      paddleWidth,
      paddleHeight
    ))
  |> buffer.Add

  Command2D.fillRect
    (0<RenderLayer>, Color.White)
    (Rectangle(
      model.Width - paddleWidth,
      model.RightPaddle.Y - paddleHeight / 2f,
      paddleWidth,
      paddleHeight
    ))
  |> buffer.Add

  // Draw ball
  Command2D.fillCircle
    (0<RenderLayer>, Color.White)
    (Vector2(model.Ball.Position.X, model.Ball.Position.Y), ballRadius)
  |> buffer.Add

  // Draw center line
  for y in 0.0f .. 20.0f .. model.Height do
    Command2D.fillRect
      (0<RenderLayer>, Color.Gray)
      (Rectangle(model.Width / 2f - 1f, y, 2f, 10f))
    |> buffer.Add
