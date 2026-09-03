module Mibo.Mvu.Tests.Diagnostics

open System
open System.Threading
open Expecto
open Mibo.Diagnostics
open Mibo.Elmish

type DiagMsg = | DiagTick

type DiagModel = { Steps: int }

let init _ctx = struct ({ Steps = 0 }, Cmd.none)

let update msg model =
  match msg with
  | DiagTick -> struct ({ model with Steps = model.Steps + 1 }, Cmd.none)

/// Captures the profiler the runner registers, through the observer
/// notification that hands over the context.
type DiagObserver() =
  let mutable captured: FrameProfiler voption = ValueNone

  member _.Observer: IObserver<struct (GameContext * DiagModel * GameTime)> =
    { new IObserver<struct (GameContext * DiagModel * GameTime)> with
        member _.OnNext(struct (ctx, _, _)) =
          match captured with
          | ValueNone -> captured <- Diagnostics.tryGetProfiler ctx
          | ValueSome _ -> ()

        member _.OnError(_: exn) = ()
        member _.OnCompleted() = ()
    }

  member _.Captured = captured

/// Runs three steps, waits past a one millisecond window, then runs one more
/// step so the profiler publishes the finished window.
let runFramesAndPublish(step: TimeSpan -> unit) =
  for _ in 1..3 do
    step(TimeSpan.FromMilliseconds 16.0)

  Thread.Sleep 30
  step(TimeSpan.FromMilliseconds 16.0)

[<Tests>]
let tests =
  testList "Diagnostics" [

    testList "Headless runner" [
      testCase "registers the profiler supplied with withProfiler"
      <| fun _ ->
        let holder = DiagObserver()
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        let program =
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withProfiler profiler
          |> HeadlessProgram.withObserver(fun () -> holder.Observer)

        use runner = new HeadlessRunner<DiagModel, DiagMsg>(program)

        runFramesAndPublish runner.Step

        // The observer saw the same instance through the context.
        match holder.Captured with
        | ValueSome registered ->
          Expect.isTrue
            (obj.ReferenceEquals(registered, profiler))
            "the supplied profiler is the registered one"

          Expect.isFalse registered.CanScreenshot "headless has no screen"
        | ValueNone -> failtest "profiler was never registered"

        let stats = profiler.Snapshot
        Expect.isGreaterThan stats.TotalFrames 0L "frames counted"
        Expect.isTrue (stats.SimStepsPerSecond > 0f) "sim steps counted"

      testCase "without a profiler nothing is registered"
      <| fun _ ->
        let holder = DiagObserver()

        let program =
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () -> holder.Observer)

        use runner = new HeadlessRunner<DiagModel, DiagMsg>(program)

        runFramesAndPublish runner.Step

        Expect.equal holder.Captured ValueNone "no profiler is registered"

      testCase "fixed step drops count as slow frames"
      <| fun _ ->
        let profiler = FrameProfiler(TimeSpan.FromMilliseconds 1.0)

        let program =
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withFixedStep {
            StepSeconds = 0.016f
            MaxStepsPerFrame = 1
            MaxFrameSeconds = ValueNone
            Map = fun _ -> DiagTick
          }
          |> HeadlessProgram.withProfiler profiler

        use runner = new HeadlessRunner<DiagModel, DiagMsg>(program)

        // One second of delta against a one step cap: the step runs, the rest
        // of the time drops.
        runner.Step(TimeSpan.FromSeconds 1.0) |> ignore

        Thread.Sleep 20
        runner.Step(TimeSpan.FromMilliseconds 16.0) |> ignore

        Expect.isGreaterThan runner.Model.Steps 0 "the fixed step ran"

        let stats = profiler.Snapshot
        Expect.isGreaterThan stats.SlowFrames 0 "dropped time counted"
    ]
  ]
