module Mibo.Adaptive.Mibo.Tests.Diagnostics

open System
open Expecto
open System.Threading
open Mibo.Adaptive
open Mibo.Diagnostics
open Mibo.Elmish

[<Struct>]
type DiagFrame = { FrameNumber: int }

[<Tests>]
let tests =
  testList "Diagnostics" [

    testList "Adaptive runner" [
      testCase "registers the profiler supplied with withProfiler"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        let program =
          AdaptiveProgram.mkProgram
            (fun _ctx ->
              AdaptiveInit.ofFrameBuilder(fun () -> { FrameNumber = 0 }))
            (fun _ctx _gameTime -> ())
          |> AdaptiveProgram.withProfiler profiler

        let ctx = GameContext.create(800, 600)

        use runner = new AdaptiveHeadless<DiagFrame>(program, context = ctx)

        for _ in 1..3 do
          runner.Step(TimeSpan.FromMilliseconds 16.0) |> ignore

        Thread.Sleep 20
        runner.Step(TimeSpan.FromMilliseconds 16.0) |> ignore

        let registered = Diagnostics.getProfiler ctx

        Expect.isTrue
          (obj.ReferenceEquals(registered, profiler))
          "the supplied profiler is the registered one"

        Expect.isFalse registered.CanScreenshot "headless has no screen"

        let stats = profiler.Snapshot
        Expect.isGreaterThan stats.TotalFrames 0L "frames counted"
        Expect.isTrue (stats.SimStepsPerSecond > 0f) "sim steps counted"
    ]
  ]
