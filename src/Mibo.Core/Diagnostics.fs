namespace Mibo.Diagnostics

open System
open System.Diagnostics
open Mibo.Elmish

/// <summary>
/// Frame measurements collected over a time window.
/// </summary>
/// <remarks>
/// The profiler refreshes these values once per window. All rates are windowed
/// counts. All costs are means over the window, in milliseconds.
/// </remarks>
[<Struct>]
type FrameStats = {
  /// <summary>Host frames per second. A host frame is one update step of the host loop.</summary>
  FramesPerSecond: float32

  /// <summary>Draws per second. This is the frame rate the player sees. <c>ValueNone</c> on headless runners.</summary>
  DrawsPerSecond: float32 voption

  /// <summary>Simulation steps per second. With a fixed step this counts every step, not every frame.</summary>
  SimStepsPerSecond: float32

  /// <summary>Mean host frame interval in milliseconds.</summary>
  FrameMs: float32

  /// <summary>Worst host frame interval in the window, in milliseconds.</summary>
  WorstFrameMs: float32

  /// <summary>Mean cost of the update phase, in milliseconds.</summary>
  UpdateMs: float32

  /// <summary>Mean cost of the draw phase, in milliseconds. <c>ValueNone</c> on headless runners.</summary>
  DrawMs: float32 voption

  /// <summary>Bytes allocated on the frame thread during the window.</summary>
  AllocatedBytes: int64

  /// <summary>Generation 0 collections during the window.</summary>
  Gen0Collections: int

  /// <summary>Generation 1 collections during the window.</summary>
  Gen1Collections: int

  /// <summary>Generation 2 collections during the window.</summary>
  Gen2Collections: int

  /// <summary>Frames that ran behind in the window. Fixed step drops count here. On MonoGame, the fixed step catch up flag counts here.</summary>
  SlowFrames: int

  /// <summary>Total host frames since the profiler was created.</summary>
  TotalFrames: int64

  /// <summary>Draw calls of the last frame. <c>ValueNone</c> where the backend reports no such count.</summary>
  GpuDrawCalls: int64 voption

  /// <summary>Primitives of the last frame. <c>ValueNone</c> where the backend reports no such count.</summary>
  GpuPrimitives: int64 voption

  /// <summary>Texture binds of the last frame. <c>ValueNone</c> where the backend reports no such count.</summary>
  GpuTextureBinds: int64 voption
}

/// <summary>
/// Collects frame measurements for the running game and serves screenshot requests.
/// </summary>
/// <remarks>
/// Build one, pass it to the program with <c>withProfiler</c>, and the host
/// registers it in the <see cref="T:Mibo.Elmish.GameContext"/> and measures
/// every frame. When no profiler is supplied, nothing runs and nothing is
/// registered. Read it with
/// <see cref="M:Mibo.Diagnostics.Diagnostics.tryGetProfiler"/> from a renderer,
/// a subscription, or a context taking update.
/// <para>
/// All members are for the frame thread only. The stamp methods allocate
/// nothing and box nothing, so a host can call them every frame.
/// </para>
/// </remarks>
type FrameProfiler(window: TimeSpan, canScreenshot: bool) as this =

  // Stopwatch ticks for one window. Computed once so the per frame check is a
  // subtraction and a compare.
  let windowSwTicks =
    let ticks = window.Ticks * Stopwatch.Frequency / 10_000_000L
    if ticks < 1L then 1L else ticks

  let mutable windowStart = 0L
  let mutable lastFrameStamp = 0L
  let mutable updateStart = 0L
  let mutable drawStart = 0L

  let mutable windowFrames = 0
  let mutable windowSimSteps = 0
  let mutable windowDraws = 0
  let mutable windowWorstMs = 0f
  let mutable windowUpdateMsSum = 0f
  let mutable windowDrawMsSum = 0f
  let mutable windowSlowFrames = 0
  let mutable windowAllocStart = 0L
  let mutable windowGen0Start = 0
  let mutable windowGen1Start = 0
  let mutable windowGen2Start = 0

  let mutable totalFrames = 0L
  let mutable gpuDrawCalls = 0L
  let mutable gpuPrimitives = 0L
  let mutable gpuTextureBinds = 0L
  let mutable gpuPublished = false
  let mutable pendingScreenshot: string voption = ValueNone
  let mutable snapshot = Unchecked.defaultof<FrameStats>
  let mutable enabled = true

  /// <summary>Creates a profiler that cannot take screenshots. Headless hosts use this form.</summary>
  new(window: TimeSpan) = FrameProfiler(window, false)

  /// <summary>Whether measurement runs. On by default.</summary>
  /// <remarks>
  /// Turn it off and on at any time. While it is off every stamp and every
  /// request does nothing. Turning it back on starts a fresh window, so the
  /// time spent off never shows up as a frame spike.
  /// </remarks>
  member _.Enabled
    with get () = enabled
    and set value =
      if value && not enabled then
        windowStart <- 0L
        lastFrameStamp <- 0L

      enabled <- value

  /// <summary>The default measurement window of half a second.</summary>
  static member DefaultWindow = TimeSpan.FromSeconds 0.5

  /// <summary>
  /// Freezes the window that just ended and starts a new one.
  /// </summary>
  /// <param name="now">The current stopwatch stamp.</param>
  member private _.Publish(now: int64) =
    if windowFrames > 0 then
      let windowSec =
        float32(Stopwatch.GetElapsedTime(windowStart, now).TotalSeconds)

      let drawMs =
        if windowDraws > 0 then
          ValueSome(windowDrawMsSum / float32 windowDraws)
        else
          ValueNone

      let drawsPerSecond =
        if windowDraws > 0 then
          ValueSome(float32 windowDraws / windowSec)
        else
          ValueNone

      snapshot <- {
        FramesPerSecond = float32 windowFrames / windowSec
        DrawsPerSecond = drawsPerSecond
        SimStepsPerSecond = float32 windowSimSteps / windowSec
        FrameMs = windowSec * 1000f / float32 windowFrames
        WorstFrameMs = windowWorstMs
        UpdateMs = windowUpdateMsSum / float32 windowFrames
        DrawMs = drawMs
        AllocatedBytes =
          GC.GetAllocatedBytesForCurrentThread() - windowAllocStart
        Gen0Collections = GC.CollectionCount 0 - windowGen0Start
        Gen1Collections = GC.CollectionCount 1 - windowGen1Start
        Gen2Collections = GC.CollectionCount 2 - windowGen2Start
        SlowFrames = windowSlowFrames
        TotalFrames = totalFrames
        GpuDrawCalls =
          if gpuPublished then ValueSome gpuDrawCalls else ValueNone
        GpuPrimitives =
          if gpuPublished then ValueSome gpuPrimitives else ValueNone
        GpuTextureBinds =
          if gpuPublished then
            ValueSome gpuTextureBinds
          else
            ValueNone
      }

    windowStart <- now
    windowFrames <- 0
    windowSimSteps <- 0
    windowDraws <- 0
    windowWorstMs <- 0f
    windowUpdateMsSum <- 0f
    windowDrawMsSum <- 0f
    windowSlowFrames <- 0
    windowAllocStart <- GC.GetAllocatedBytesForCurrentThread()
    windowGen0Start <- GC.CollectionCount 0
    windowGen1Start <- GC.CollectionCount 1
    windowGen2Start <- GC.CollectionCount 2

  /// <summary>
  /// Starts a host frame. Hosts call this first in the frame, before input
  /// polling and the update phase.
  /// </summary>
  /// <remarks>
  /// When the window has elapsed, this call first freezes the finished window
  /// into <see cref="P:Mibo.Diagnostics.FrameProfiler.Snapshot"/> and then
  /// starts the next window.
  /// </remarks>
  member _.BeginFrame() =
    if this.Enabled then
      let now = Stopwatch.GetTimestamp()

      if windowStart = 0L then
        this.SeedWindow(now)
      elif now - windowStart >= windowSwTicks then
        this.Publish(now)

      if lastFrameStamp <> 0L then
        let ms =
          float32(
            Stopwatch.GetElapsedTime(lastFrameStamp, now).TotalMilliseconds
          )

        if ms > windowWorstMs then
          windowWorstMs <- ms

      lastFrameStamp <- now
      updateStart <- now
      windowFrames <- windowFrames + 1
      totalFrames <- totalFrames + 1L

  /// <summary>
  /// Records the first frame stamp and the window counters.
  /// </summary>
  member private _.SeedWindow(now: int64) =
    windowStart <- now
    windowAllocStart <- GC.GetAllocatedBytesForCurrentThread()
    windowGen0Start <- GC.CollectionCount 0
    windowGen1Start <- GC.CollectionCount 1
    windowGen2Start <- GC.CollectionCount 2

  /// <summary>
  /// Ends the update phase. Hosts call this after the update work of the frame
  /// is done, before any drawing.
  /// </summary>
  member _.EndUpdate() =
    if this.Enabled then
      let ms =
        float32(
          Stopwatch
            .GetElapsedTime(updateStart, Stopwatch.GetTimestamp())
            .TotalMilliseconds
        )

      windowUpdateMsSum <- windowUpdateMsSum + ms

  /// <summary>
  /// Starts the draw phase. Hosts call this right before they draw. Headless
  /// runners never call it, so draw fields stay <c>ValueNone</c> there.
  /// </summary>
  member _.BeginDraw() =
    if this.Enabled then
      drawStart <- Stopwatch.GetTimestamp()

  /// <summary>
  /// Ends the draw phase. Hosts call this after the last draw call of the
  /// frame, before they present.
  /// </summary>
  member _.EndDraw() =
    if this.Enabled then
      let ms =
        float32(
          Stopwatch
            .GetElapsedTime(drawStart, Stopwatch.GetTimestamp())
            .TotalMilliseconds
        )

      windowDrawMsSum <- windowDrawMsSum + ms
      windowDraws <- windowDraws + 1

  /// <summary>
  /// Counts simulation steps that ran in this frame. The shared loop calls
  /// this once per frame. <paramref name="dropped"/> marks that the fixed step
  /// hit its step cap and threw time away.
  /// </summary>
  member _.AddSimSteps(steps: int, dropped: bool) =
    if this.Enabled then
      windowSimSteps <- windowSimSteps + steps

      if dropped then
        windowSlowFrames <- windowSlowFrames + 1

  /// <summary>
  /// Counts one frame that ran behind. MonoGame hosts call this when the fixed
  /// step catch up flag is set.
  /// </summary>
  member _.NoteSlowFrame() =
    if this.Enabled then
      windowSlowFrames <- windowSlowFrames + 1

  /// <summary>
  /// Publishes the graphics counters of the frame that just drew. MonoGame
  /// hosts call this at the end of draw. Other backends never call it, so the
  /// fields stay <c>ValueNone</c>.
  /// </summary>
  member _.PublishGpuMetrics
    (drawCalls: int64, primitives: int64, textureBinds: int64)
    =
    if this.Enabled then
      gpuDrawCalls <- drawCalls
      gpuPrimitives <- primitives
      gpuTextureBinds <- textureBinds
      gpuPublished <- true

  /// <summary>The measurements of the last completed window.</summary>
  /// <remarks>Zeroed out until the first window completes.</remarks>
  member _.Snapshot = snapshot

  /// <summary>Whether this runtime can capture the screen.</summary>
  member _.CanScreenshot = canScreenshot

  /// <summary>
  /// Asks the host to save a screenshot at the given path at the end of the
  /// current frame.
  /// </summary>
  /// <remarks>
  /// The request does nothing when <see cref="P:Mibo.Diagnostics.FrameProfiler.CanScreenshot"/>
  /// is false, which is the case on headless runners. Check that property
  /// beforehand when the difference matters.
  /// <para>
  /// The file is written when the frame finishes drawing. The capture reads
  /// the whole screen and encodes a PNG, so expect one slow frame per request.
  /// </para>
  /// </remarks>
  member _.RequestScreenshot(path: string) =
    if this.Enabled && canScreenshot then
      pendingScreenshot <- ValueSome path

  /// <summary>
  /// Takes the pending screenshot request, if any. Hosts call this at the end
  /// of draw, then write the file themselves.
  /// </summary>
  member _.DrainScreenshot() =
    let pending = pendingScreenshot
    pendingScreenshot <- ValueNone
    pending

/// <summary>Access to the frame profiler and a display helper.</summary>
module Diagnostics =

  /// <summary>Returns the registered profiler, or <c>ValueNone</c>.</summary>
  let inline tryGetProfiler(ctx: GameContext) : FrameProfiler voption =
    GameContext.tryGetService<FrameProfiler> ctx

  /// <summary>Returns the registered profiler.</summary>
  /// <exception cref="T:System.Exception">Thrown when no profiler is registered.</exception>
  let inline getProfiler(ctx: GameContext) : FrameProfiler =
    GameContext.getService<FrameProfiler> ctx

  /// <summary>
  /// Formats a snapshot as two short lines for a text overlay.
  /// </summary>
  /// <remarks>
  /// The first line holds rates and costs. The second holds allocation,
  /// collection, and graphics counts. Call this once per window, not once per
  /// frame, because it allocates.
  /// </remarks>
  let format(stats: FrameStats) : string =
    let drawPart =
      match stats.DrawMs, stats.DrawsPerSecond with
      | ValueSome ms, ValueSome hz -> $" | draw {ms:F2} ms at {hz:F0}/s"
      | _ -> ""

    let gpuPart =
      match stats.GpuDrawCalls with
      | ValueSome calls ->
        let prims = stats.GpuPrimitives |> ValueOption.defaultValue 0L
        let binds = stats.GpuTextureBinds |> ValueOption.defaultValue 0L
        $" | gpu {calls} draws {prims} prims {binds} textures"
      | ValueNone -> ""

    let slowPart =
      if stats.SlowFrames > 0 then
        $" | slow {stats.SlowFrames}"
      else
        ""

    let line1 =
      $"sim {stats.SimStepsPerSecond:F0}/s | frame {stats.FrameMs:F1} ms | worst {stats.WorstFrameMs:F1} ms | update {stats.UpdateMs:F2} ms{drawPart}"

    let line2 =
      $"alloc {stats.AllocatedBytes / 1024L} KB | gen0 {stats.Gen0Collections} | gen1 {stats.Gen1Collections} | gen2 {stats.Gen2Collections}{gpuPart}{slowPart}"

    $"{line1}\n{line2}"
