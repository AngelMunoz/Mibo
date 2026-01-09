namespace Mibo.Elmish

open System

/// <summary>
/// Controls when dispatched messages become eligible for processing.
/// </summary>
/// <remarks>
/// <para>
/// In <see cref="F:Mibo.Elmish.DispatchMode.Immediate"/>, cascaded messages can be processed in the same MonoGame
/// <c>Update</c> call.
/// </para>
/// <para>
/// In <see cref="F:Mibo.Elmish.DispatchMode.FrameBounded"/>, messages dispatched while processing a frame are deferred
/// until the next MonoGame <c>Update</c> call.
/// </para>
/// </remarks>
[<Struct>]
type DispatchMode =
  /// Process newly dispatched messages immediately.
  | Immediate
  /// Defer messages dispatched during processing until the next frame.
  | FrameBounded

/// <summary>
/// Configuration for a framework-managed fixed timestep simulation.
/// </summary>
/// <remarks>
/// When enabled, the runtime converts MonoGame's variable <c>ElapsedGameTime</c> into
/// zero or more fixed-size simulation steps per MonoGame <c>Update</c> call.
/// </remarks>
[<Struct>]
type FixedStepConfig<'Msg> = {
  /// <summary>Fixed simulation step size in seconds (e.g. 1/60 = 0.0166667).</summary>
  StepSeconds: float32

  /// <summary>
  /// Maximum number of fixed steps to run in a single MonoGame frame.
  /// </summary>
  /// <remarks>
  /// This prevents the "spiral of death" after long stalls. If the cap is hit, remaining
  /// accumulated time is dropped.
  /// </remarks>
  MaxStepsPerFrame: int

  /// <summary>
  /// Clamp the per-frame delta used for accumulation.
  /// </summary>
  /// <remarks>
  /// Default behavior is to clamp to 0.25 seconds.
  /// </remarks>
  MaxFrameSeconds: float32 voption

  /// <summary>Maps a fixed-step delta (seconds) to a message.</summary>
  Map: float32 -> 'Msg
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

    // Defensive clamping to keep math stable.
    let dt =
      if deltaSeconds < 0.0f then 0.0f
      elif deltaSeconds > maxFrameSeconds then maxFrameSeconds
      else deltaSeconds

    let mutable acc = accumulatorSeconds + dt
    let mutable steps = 0

    if stepSeconds <= 0.0f || maxStepsPerFrame <= 0 then
      // Invalid config; do not step.
      struct (accumulatorSeconds, 0, false)
    else
      while steps < maxStepsPerFrame && acc >= stepSeconds do
        acc <- acc - stepSeconds
        steps <- steps + 1

      // If we hit the cap and still have enough time for another step, drop.
      let dropped = (steps = maxStepsPerFrame) && (acc >= stepSeconds)

      if dropped then
        acc <- 0.0f

      struct (acc, steps, dropped)
