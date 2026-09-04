namespace Mibo.Elmish

/// <summary>
/// Controls when dispatched messages become eligible for processing.
/// </summary>
/// <remarks>
/// <para>
/// In <see cref="F:Mibo.Elmish.DispatchMode.Immediate"/>, cascaded messages can be processed in the same
/// <c>Update</c> call.
/// </para>
/// <para>
/// In <see cref="F:Mibo.Elmish.DispatchMode.FrameBounded"/>, messages dispatched while processing a frame are deferred
/// until the next <c>Update</c> call.
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
/// When enabled, the runtime converts the variable frame time into
/// zero or more fixed-size simulation steps per <c>Update</c> call.
/// </remarks>
[<Struct>]
type FixedStepConfig<'Msg> = {
  /// <summary>Fixed simulation step size in seconds (e.g. 1/60 = 0.0166667).</summary>
  StepSeconds: float32

  /// <summary>
  /// Maximum number of fixed steps to run in a single frame.
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
