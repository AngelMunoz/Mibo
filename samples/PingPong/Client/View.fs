module PingPong.Client.View

open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Next.Graphics2D
open PingPong.Shared.Types

let view (_ctx: GameContext) (model: GameState) (buffer: RenderBuffer2D) =
  buffer
  |> Draw.fillRect
    (0<RenderLayer>, Color.White)
    (Rectangle(
      0f,
      model.LeftPaddle.Y - paddleHeight / 2f,
      paddleWidth,
      paddleHeight
    ))
  |> Draw.fillRect
    (0<RenderLayer>, Color.White)
    (Rectangle(
      model.Width - paddleWidth,
      model.RightPaddle.Y - paddleHeight / 2f,
      paddleWidth,
      paddleHeight
    ))
  |> Draw.fillCircle
    (0<RenderLayer>, Color.White)
    (Vector2(model.Ball.Position.X, model.Ball.Position.Y), ballRadius)
  |> ignore

  for y in 0.0f .. 20.0f .. model.Height do
    buffer
    |> Draw.fillRect
      (0<RenderLayer>, Color.Gray)
      (Rectangle(model.Width / 2f - 1f, y, 2f, 10f))
    |> ignore
