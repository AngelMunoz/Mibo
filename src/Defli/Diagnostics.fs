namespace Defli

open System
open System.Diagnostics
open System.Numerics
open AdaptiveSlop.Core
open Defli.World
// ─────────────────────────────────────────────────────────────
// Diagnostics overlay — Kimo's Systems/Diagnostics.fs pattern,
// simplified for the same-thread world: no bridge, no per-world
// regions. Two windowed lines, refreshed at the window boundary
// only (formatting every frame would allocate on the hot path).
//
//   FrameDiag — main-MVU frame rates (UpdateHz/Fps/FrameMs/WorstMs).
//               Rates are WINDOWED COUNTS (events per measured
//               wall-clock window), never an EMA of 1/interval.
//   WorldDiag — sim-side cost sampled inside RoomTick: TickHz
//               (windowed), SimMs (EMA of the tick body cost),
//               live entity/queue counts.
//
// F3 toggles visibility (shell input map).
// ─────────────────────────────────────────────────────────────

type FrameDiag() =
  /// Overlay visibility, toggled with F3 (read by the overlay view).
  member val Visible: bool = false with get, set
  /// Update-loop (Tick) calls per second over the last window.
  member val UpdateHz: float32 = 0f with get, set
  /// Draws per second over the last window — the FPS the player sees.
  member val Fps: float32 = 0f with get, set
  /// Mean draw interval (ms) over the last window.
  member val FrameMs: float32 = 0f with get, set
  /// Worst draw interval (ms) over the last window — makes frame-drop
  /// spikes visible that the mean smooths away.
  member val WorstMs: float32 = 0f with get, set
  /// Preformatted overlay line, refreshed per window.
  member val Display: string = "" with get, set
  // Bookkeeping (main thread only — not for views).
  member val LastDrawStamp: int64 = 0L with get, set
  member val WindowStartStamp: int64 = 0L with get, set
  member val WindowDraws: int = 0 with get, set
  member val WindowUpdates: int64 = 0L with get, set
  member val PendingWorstMs: float32 = 0f with get, set

type WorldDiag() =
  /// Ticks per second over the last window.
  member val TickHz: float32 = 0f with get, set
  /// EMA of the wall-clock cost of one RoomTick body.
  member val SimMs: float32 = 0f with get, set
  member val TickCount: int64 = 0L with get, set
  member val AliveEnemies: int = 0 with get, set
  member val QueueCount: int = 0 with get, set
  /// Preformatted overlay line, refreshed per window.
  member val Display: string = "" with get, set
  // Bookkeeping (sampled inside RoomTick only).
  member val WindowStartStamp: int64 = 0L with get, set
  member val WindowTicks: int = 0 with get, set

module Diagnostics =

  let private windowSeconds = 0.5

  /// Wall-clock timestamp at the start of a RoomTick body.
  let inline tickStart() : int64 = Stopwatch.GetTimestamp()

  /// Counts one update-loop Tick (rate computed at window refresh).
  let inline update(diag: FrameDiag) =
    diag.WindowUpdates <- diag.WindowUpdates + 1L

  /// Samples one rendered frame and refreshes the windowed rates/display
  /// when the window elapses. stamp is a wall-clock timestamp captured by
  /// the caller (the view runs once per Draw).
  let drawn (stamp: int64) (diag: FrameDiag) =
    if diag.LastDrawStamp <> 0L then
      let ms =
        float32(
          Stopwatch.GetElapsedTime(diag.LastDrawStamp, stamp).TotalMilliseconds
        )

      if ms > 0f then
        diag.PendingWorstMs <- max diag.PendingWorstMs ms

    diag.LastDrawStamp <- stamp
    diag.WindowDraws <- diag.WindowDraws + 1

    if diag.WindowStartStamp = 0L then
      diag.WindowStartStamp <- stamp

    let windowSec =
      Stopwatch.GetElapsedTime(diag.WindowStartStamp, stamp).TotalSeconds

    if windowSec >= windowSeconds then
      diag.Fps <- float32(float diag.WindowDraws / windowSec)
      diag.FrameMs <- float32(windowSec * 1000. / float diag.WindowDraws)
      diag.UpdateHz <- float32(float diag.WindowUpdates / windowSec)
      diag.WorstMs <- diag.PendingWorstMs
      diag.PendingWorstMs <- 0f
      diag.WindowDraws <- 0
      diag.WindowUpdates <- 0L
      diag.WindowStartStamp <- stamp

      diag.Display <-
        $"Update: {diag.UpdateHz:F0} Hz | Draw: {diag.Fps:F0} FPS | {diag.FrameMs:F1} ms | worst {diag.WorstMs:F1} ms"

  /// Folds one RoomTick into the world diagnostics: windowed tick rate,
  /// sim cost EMA, live counts. t0 is the tickStart() stamp; alive/queue
  /// are the direct values already computed by the router this tick.
  let tickEnd (t0: int64) (diag: WorldDiag) (alive: aval<int>) (queue: int) =
    let now = Stopwatch.GetTimestamp()
    diag.TickCount <- diag.TickCount + 1L

    let simMs = float32(Stopwatch.GetElapsedTime(t0, now).TotalMilliseconds)

    diag.SimMs <- diag.SimMs * 0.9f + simMs * 0.1f
    diag.AliveEnemies <- alive |> AVal.getValue
    diag.QueueCount <- queue

    if diag.WindowStartStamp = 0L then
      diag.WindowStartStamp <- now

    diag.WindowTicks <- diag.WindowTicks + 1

    let windowSec =
      Stopwatch.GetElapsedTime(diag.WindowStartStamp, now).TotalSeconds

    if windowSec >= windowSeconds then
      diag.TickHz <- float32(float diag.WindowTicks / windowSec)
      diag.WindowTicks <- 0
      diag.WindowStartStamp <- now

      diag.Display <-
        $"World: {diag.TickHz:F1} Hz | sim {diag.SimMs:F2} ms | ticks {diag.TickCount} | enemies {diag.AliveEnemies} | queue {diag.QueueCount}"
