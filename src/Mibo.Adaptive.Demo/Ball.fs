module Mibo.Adaptive.Demo.Ball

open System.Numerics
open AdaptiveSlop.Core
open Types

/// The ball feature: the root plus the projections derived from it.
type State = {
  /// Position + velocity. The root.
  Value: cval<Ball>

  /// The ball rect for drawing, derived from the root. The projection.
  Rect: aval<Rect>

  /// Composed from the ball root and the left paddle root: true when the ball
  /// is inbound on the left and the left paddle is out of position.
  Threat: aval<bool>
}

/// Creates the feature. Takes the left paddle's root: a projection may depend
/// on another feature's root — that is how the graph composes.
let create(leftPaddleY: cval<float32>) : State =
  let value =
    CVal.create
      {
        Position = Vector2(courtWidth / 2f, courtHeight / 2f)
        Velocity = Vector2(300f, 150f)
      }

  let rect =
    value
    |> AVal.map(fun b ->
      Telemetry.ballRect <- Telemetry.ballRect + 1

      {
        X = b.Position.X - ballRadius
        Y = b.Position.Y - ballRadius
        Width = ballRadius * 2f
        Height = ballRadius * 2f
      })

  let threat =
    leftPaddleY
    |> AVal.map2
      (fun (b: Ball) (leftY: float32) ->
        Telemetry.threat <- Telemetry.threat + 1

        b.Velocity.X < 0f
        && b.Position.X < 300f
        && abs(b.Position.Y - leftY) > 60f)
      value

  {
    Value = value
    Rect = rect
    Threat = threat
  }

/// The ball speeds up after each paddle hit, so a perfect AI eventually
/// misses and points get scored.
let growth = 1.25f

let private rng = System.Random.Shared

/// Integrates the ball one frame and returns the side that scored, if any.
/// A goal is an event: the caller (the world's router) decides what to do.
let step
  (ball: State)
  (dt: float32)
  (leftPaddleY: float32)
  (rightPaddleY: float32)
  : PaddleSide voption =
  let b = AVal.getValue ball.Value

  let b' =
    Physics.updateBall courtWidth courtHeight b leftPaddleY rightPaddleY dt

  let b'' =
    if b'.Velocity.X <> b.Velocity.X then
      {
        b' with
            Velocity = b'.Velocity * growth
      }
    else
      b'

  if b''.Position.X < 0f then
    ValueSome Right
  elif b''.Position.X > courtWidth then
    ValueSome Left
  else
    ball.Value.Set(b'')
    ValueNone

/// Serves the ball toward the given side after a goal.
let reset (ball: State) (serveToward: PaddleSide) : unit =
  let dirX = if serveToward = Left then -1f else 1f

  ball.Value.Set
    {
      Position = Vector2(courtWidth / 2f, courtHeight / 2f)

      Velocity =
        Vector2(
          300f * dirX,
          200f * (if rng.NextDouble() > 0.5 then 1f else -1f)
        )
    }
