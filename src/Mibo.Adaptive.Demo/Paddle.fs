module Mibo.Adaptive.Demo.Paddle

open AdaptiveSlop.Core
open Types

/// A paddle feature: the position root and the rect projection derived from
/// it. The projection is retained here, as a field of the feature record.
type State = {
  /// The paddle's height position (center). The root.
  Y: cval<float32>

  /// The paddle rect for drawing, derived from the root. The projection.
  Rect: aval<Rect>
}

/// Creates the feature: builds the root and the projection, and returns the
/// bundle. The caller (the world) retains it.
let create(side: PaddleSide) : State =
  let y = CVal.create(courtHeight / 2f)

  let bump =
    if side = Left then
      fun () -> Telemetry.leftPaddle <- Telemetry.leftPaddle + 1
    else
      fun () -> Telemetry.rightPaddle <- Telemetry.rightPaddle + 1

  let x = if side = Left then 0f else courtWidth - paddleWidth

  let rect =
    y
    |> AVal.map(fun centerY ->
      bump()

      {
        X = x
        Y = centerY - paddleHeight / 2f
        Width = paddleWidth
        Height = paddleHeight
      })

  { Y = y; Rect = rect }

/// Moves the paddle by a signed amount (pixels) and returns the clamped new
/// height, so callers can pass it on without re-reading the cell.
let move (paddle: State) (deltaY: float32) : float32 =
  let next = Physics.clampPaddle courtHeight (AVal.getValue paddle.Y + deltaY)
  paddle.Y.Set next
  next
