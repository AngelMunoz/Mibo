module Mibo.Core.Tests.Diagnostics

open System
open System.Threading
open Expecto
open Mibo.Diagnostics

[<Tests>]
let tests =
  testList "Diagnostics" [

    testList "FrameProfiler" [
      testCase "publishes windowed rates after the window closes"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        for _ in 1..3 do
          profiler.BeginFrame()
          profiler.EndUpdate()

        Thread.Sleep 20
        profiler.BeginFrame()

        let stats = profiler.Snapshot

        Expect.isGreaterThan stats.TotalFrames 0L "total frames counted"
        Expect.isTrue (stats.FramesPerSecond > 0f) "frame rate above zero"
        Expect.isTrue (stats.UpdateMs >= 0f) "update cost defined"

        Expect.equal
          stats.DrawsPerSecond
          ValueNone
          "no draws without draw stamps"

        Expect.equal stats.DrawMs ValueNone "no draw cost without draw stamps"

      testCase "draw stamps fill the draw fields"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        for _ in 1..3 do
          profiler.BeginFrame()
          profiler.EndUpdate()
          profiler.BeginDraw()
          profiler.EndDraw()

        Thread.Sleep 20
        profiler.BeginFrame()

        let stats = profiler.Snapshot

        Expect.isTrue
          (stats.DrawsPerSecond |> ValueOption.exists(fun hz -> hz > 0f))
          "draw rate above zero"

        Expect.isTrue
          (stats.DrawMs |> ValueOption.exists(fun ms -> ms >= 0f))
          "draw cost defined"

      testCase "counts sim steps and dropped frames"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        profiler.BeginFrame()
        profiler.AddSimSteps(4, true)
        profiler.EndUpdate()

        Thread.Sleep 20
        profiler.BeginFrame()

        let stats = profiler.Snapshot

        Expect.equal stats.SlowFrames 1 "dropped step counted as a slow frame"
        Expect.isTrue (stats.SimStepsPerSecond > 0f) "sim steps counted"

      testCase "counts thread allocation over the window"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        profiler.BeginFrame()

        let garbage = Array.create 4096 1uy
        garbage[0] <- 2uy

        profiler.AddSimSteps(1, false)
        profiler.EndUpdate()

        Thread.Sleep 20
        profiler.BeginFrame()

        let stats = profiler.Snapshot

        Expect.isTrue (stats.AllocatedBytes > 0L) "allocation counted"
        Expect.isTrue (stats.Gen0Collections >= 0) "gen0 delta defined"

      testCase "screenshot queue accepts and drains one request"
      <| fun _ ->
        let profiler =
          FrameProfiler(TimeSpan.FromMilliseconds 1.0, canScreenshot = true)

        Expect.isTrue profiler.CanScreenshot "screenshots enabled"

        profiler.RequestScreenshot("shot.png")

        match profiler.DrainScreenshot() with
        | ValueSome path -> Expect.equal path "shot.png" "path drained"
        | ValueNone -> failtest "request was lost"

        Expect.equal
          (profiler.DrainScreenshot())
          ValueNone
          "queue empties after drain"

      testCase "screenshot request is a no op without a screen"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        Expect.isFalse profiler.CanScreenshot "screenshots disabled"

        profiler.RequestScreenshot("shot.png")

        Expect.equal (profiler.DrainScreenshot()) ValueNone "nothing queued"

      testCase "disabled stamps do nothing"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)
        profiler.Enabled <- false

        for _ in 1..3 do
          profiler.BeginFrame()
          profiler.EndUpdate()
          profiler.AddSimSteps(1, false)

        Thread.Sleep 20
        profiler.BeginFrame()

        Expect.equal profiler.Snapshot.TotalFrames 0L "nothing was counted"

      testCase "turning measurement back on starts a fresh window"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        for _ in 1..3 do
          profiler.BeginFrame()
          profiler.EndUpdate()

        Thread.Sleep 20
        profiler.BeginFrame()

        let before = profiler.Snapshot.TotalFrames
        Expect.isGreaterThan before 0L "frames counted while on"

        profiler.Enabled <- false

        for _ in 1..3 do
          profiler.BeginFrame()
          profiler.EndUpdate()

        Thread.Sleep 20
        profiler.Enabled <- true

        // First frame after re-enable seeds a fresh window.
        profiler.BeginFrame()
        profiler.EndUpdate()

        Thread.Sleep 20
        profiler.BeginFrame()

        let stats = profiler.Snapshot
        Expect.isGreaterThan stats.TotalFrames before "counting resumed"

        // The off gap must not read as one giant frame.
        Expect.isLessThan stats.WorstFrameMs 1000f "no spike from the off gap"

      testCase "screenshot requests are ignored while disabled"
      <| fun _ ->
        let profiler =
          FrameProfiler(TimeSpan.FromMilliseconds 1.0, canScreenshot = true)

        profiler.Enabled <- false
        profiler.RequestScreenshot("shot.png")

        Expect.equal
          (profiler.DrainScreenshot())
          ValueNone
          "nothing queued while disabled"
    ]

    testList "format" [
      testCase "formats a full snapshot into lines"
      <| fun _ ->
        let text =
          Diagnostics.format {
            FramesPerSecond = 60f
            DrawsPerSecond = ValueSome 60f
            SimStepsPerSecond = 60f
            FrameMs = 16.6f
            WorstFrameMs = 33.2f
            UpdateMs = 2.5f
            DrawMs = ValueSome 5.5f
            AllocatedBytes = 2048L
            Gen0Collections = 1
            Gen1Collections = 0
            Gen2Collections = 0
            SlowFrames = 2
            TotalFrames = 900L
            GpuDrawCalls = ValueSome 120L
            GpuPrimitives = ValueSome 2400L
            GpuTextureBinds = ValueSome 30L
          }

        Expect.stringContains text "ms" "frame cost present"
        Expect.stringContains text "60" "frame rate present"
        Expect.stringContains text "gpu 120 draws" "gpu counters present"
        Expect.stringContains text "slow 2" "slow frames present"

      testCase "formats a headless snapshot without optional fields"
      <| fun _ ->
        let text =
          Diagnostics.format {
            FramesPerSecond = 240f
            DrawsPerSecond = ValueNone
            SimStepsPerSecond = 240f
            FrameMs = 4.1f
            WorstFrameMs = 9.0f
            UpdateMs = 1.2f
            DrawMs = ValueNone
            AllocatedBytes = 0L
            Gen0Collections = 0
            Gen1Collections = 0
            Gen2Collections = 0
            SlowFrames = 0
            TotalFrames = 10L
            GpuDrawCalls = ValueNone
            GpuPrimitives = ValueNone
            GpuTextureBinds = ValueNone
          }

        Expect.isNonEmpty text "text produced"
        Expect.isFalse (text.Contains "draw") "no draw section"
        Expect.isFalse (text.Contains "gpu") "no gpu section"
    ]
  ]
