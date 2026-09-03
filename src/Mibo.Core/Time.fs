namespace Mibo.Elmish

open System

/// <summary>
/// Context for time-based information each frame.
/// </summary>
[<Struct>]
type GameTime = {
  TotalTime: TimeSpan
  ElapsedGameTime: TimeSpan
}

/// Helpers for fixed timestep simulation.
module FixedStep =
  /// <summary>
  /// Computes how many fixed steps to run and the resulting accumulator.
  /// </summary>
  /// <remarks>
  /// Returns (newAccumulator, stepsToRun, droppedTime).
  /// </remarks>
  let inline compute
    (stepSeconds: float32)
    (maxStepsPerFrame: int)
    (maxFrameSeconds: float32)
    (accumulatorSeconds: float32)
    (deltaSeconds: float32)
    : struct (float32 * int * bool) =

    let dt =
      if deltaSeconds < 0.0f then 0.0f
      elif deltaSeconds > maxFrameSeconds then maxFrameSeconds
      else deltaSeconds

    let mutable acc = accumulatorSeconds + dt
    let mutable steps = 0

    if stepSeconds <= 0.0f || maxStepsPerFrame <= 0 then
      struct (accumulatorSeconds, 0, false)
    else
      while steps < maxStepsPerFrame && acc >= stepSeconds do
        acc <- acc - stepSeconds
        steps <- steps + 1

      let dropped = (steps = maxStepsPerFrame) && (acc >= stepSeconds)

      if dropped then
        acc <- 0.0f

      struct (acc, steps, dropped)
