module Defli.Tests.DiagnosticsTests

open System
open System.Diagnostics
open Expecto
open Defli
open AdaptiveSlop.Core

// ─────────────────────────────────────────────────────────────
// Windowed diagnostics — FrameDiag is driven with SYNTHETIC
// wall-clock stamps (both drawn() args are synthetic, so the
// math is deterministic; no test sleeps). The window counts ALL
// draws since the start stamp (the opening frame costs 0), so
// expected rates are asserted against the exact formula:
//
//   draws / (lastStamp - windowStart)
//
// WorldDiag's tickEnd measures against the real clock, so its
// test uses real tickStart()/tickEnd() pairs and asserts only
// the state that does not need a window to elapse.
// ─────────────────────────────────────────────────────────────

let private baseStamp = Stopwatch.GetTimestamp()

/// A synthetic stamp: base + the given milliseconds (pure arithmetic).
let private ms(ms: float) : int64 =
  baseStamp + int64(ms * float Stopwatch.Frequency / 1000.0)

let tests =
  testList "Diagnostics" [
    testCase "drawn: windowed FPS/frame ms/worst ms" (fun () ->
      let diag = FrameDiag()

      // 10 frames at 100 ms from 0: the window refreshes at 500 ms
      // (first stamp where elapsed ≥ 0.5 s) with 6 draws over 0.5 s
      // → exactly 12 FPS, 83.3 ms mean.
      for i in 0..9 do
        Diagnostics.drawn (ms(float i * 100.0)) diag

      Expect.isFalse (String.IsNullOrEmpty diag.Display) "display formatted"
      Expect.floatClose Accuracy.medium (float diag.Fps) 12.0 "fps = 6 / 0.5"

      Expect.floatClose
        Accuracy.medium
        (float diag.FrameMs)
        (500.0 / 6.0)
        "frame ms = 500 / 6"

      Expect.isTrue (diag.WorstMs >= diag.FrameMs) "worst ≥ mean")

    testCase "drawn: rates are windowed, not cumulative" (fun () ->
      let diag = FrameDiag()

      // Window 1: refresh at 500 ms with 6 draws → 12 FPS.
      for i in 0..9 do
        Diagnostics.drawn (ms(float i * 100.0)) diag

      Expect.floatClose Accuracy.medium (float diag.Fps) 12.0 "fps window 1"

      // Window 2, CONTINUOUS from 1000 ms: the next refresh happens
      // at 1000 ms (5 draws since the 500 ms refresh: 600..1000) over
      // 0.5 s → 10 FPS. A cumulative counter would read 20/1.45 ≈ 13.8.
      for i in 0..9 do
        Diagnostics.drawn (ms(1000.0 + float i * 50.0)) diag

      Expect.floatClose
        Accuracy.medium
        (float diag.Fps)
        10.0
        "fps resets per window"

      Expect.isLessThan (float diag.Fps) 13.8 "windowed beats cumulative")

    testCase "update counts Tick calls into UpdateHz" (fun () ->
      let diag = FrameDiag()
      Diagnostics.drawn (ms 0.0) diag // establishes the window start

      Diagnostics.update diag
      Diagnostics.update diag
      Diagnostics.update diag

      // One draw 1 s later: window = 1 s → 3 updates/s.
      Diagnostics.drawn (ms 1000.0) diag

      Expect.floatClose
        Accuracy.medium
        (float diag.UpdateHz)
        3.0
        "update hz = 3")

    testCase "tickEnd: tick count, sim EMA, live counts" (fun () ->
      let diag = WorldDiag()

      // Real tickStart/tickEnd pairs (the window needs real time to
      // elapse; the state asserted here does not).
      Diagnostics.tickEnd (Diagnostics.tickStart()) diag (AVal.constant 4) 2
      Diagnostics.tickEnd (Diagnostics.tickStart()) diag (AVal.constant 4) 2
      Diagnostics.tickEnd (Diagnostics.tickStart()) diag (AVal.constant 7) 0

      Expect.equal diag.TickCount 3L "tick count"
      Expect.isTrue (diag.SimMs >= 0f) "sim ms sampled"
      Expect.equal diag.AliveEnemies 7 "last alive count"
      Expect.equal diag.QueueCount 0 "last queue count")
  ]
