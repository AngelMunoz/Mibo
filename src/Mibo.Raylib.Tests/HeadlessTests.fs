module Mibo.Raylib.Tests.Headless

open System
open Expecto
open Mibo.Elmish

type TestMsg =
  | Increment
  | Decrement
  | Set of int
  | TickMsg of GameTime

type TestModel = {
  Count: int
  LastTickTotal: TimeSpan option
}

let init _ctx =
  struct ({ Count = 0; LastTickTotal = None }, Cmd.none)

let update msg model =
  match msg with
  | Increment -> struct ({ model with Count = model.Count + 1 }, Cmd.none)
  | Decrement -> struct ({ model with Count = model.Count - 1 }, Cmd.none)
  | Set n -> struct ({ model with Count = n }, Cmd.none)
  | TickMsg gt ->
    {
      model with
          LastTickTotal = Some gt.TotalTime
    },
    Cmd.none

[<Tests>]
let headlessTests =
  testList "Headless" [
    testCase "Step advances model"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 1 "Model count should be 1"

    testCase "Multiple dispatches accumulate"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.DispatchMany [ Increment; Increment; Increment ]
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 3 "Model count should be 3"

    testCase "StepN runs N frames"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.DispatchMany [ Increment; Increment ]
      runner.StepN(2, TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 2 "Model count should be 2"

    testCase "StepUntil stops when predicate met"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      let stopAtFive model = model.Count >= 5

      let mutable dispatched = 0

      while dispatched < 10 do
        runner.Dispatch(Increment)
        dispatched <- dispatched + 1

      let met = runner.StepUntil(stopAtFive, TimeSpan.FromMilliseconds(16))
      Expect.isTrue met "Should have met predicate"
      Expect.equal runner.Model.Count 10 "Model count should be 10"

    testCase "TotalTime accumulates"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Step(TimeSpan.FromMilliseconds(100))
      runner.Step(TimeSpan.FromMilliseconds(200))

      Expect.floatClose
        Accuracy.medium
        (float runner.TotalTime.TotalSeconds)
        0.3
        "TotalTime should be 0.3"

    testCase "FixedStep dispatches correct number of times"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withFixedStep {
            StepSeconds = 0.025f
            MaxStepsPerFrame = 4
            MaxFrameSeconds = ValueNone
            Map = fun _dt -> Set 0
          }
        )

      runner.Step(TimeSpan.FromMilliseconds(100))

      Expect.equal runner.Model.Count 0 "Set 0 was dispatched"

    testCase "Quit signal stops execution"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init (fun msg model ->
            match msg with
            | Increment ->
              struct ({ model with Count = model.Count + 1 }, Cmd.signalExit)
            | _ -> struct (model, Cmd.none))
        )

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.isTrue runner.ShouldQuit "Should quit"
      Expect.equal runner.Model.Count 1 "Model count should be 1"

    testCase "GameTime is passed to tick"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.isSome runner.Model.LastTickTotal "LastTickTotal should be Some"
  ]

[<Tests>]
let headlessAdversarial =
  testList "Adversarial" [
    testCase "FrameBounded mode defers messages dispatched during Update"
    <| fun _ ->
      let mutable dispatchDuringUpdate = Unchecked.defaultof<Dispatch<TestMsg>>

      let update msg model =
        match msg with
        | Increment ->
          dispatchDuringUpdate(Set 999)
          struct ({ model with Count = model.Count + 1 }, Cmd.none)
        | Set n -> struct ({ model with Count = n }, Cmd.none)
        | _ -> struct (model, Cmd.none)

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withDispatchMode FrameBounded
        )

      dispatchDuringUpdate <- runner.Dispatch
      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal
        runner.Model.Count
        1
        "Should process Increment, not Set 999 yet"

      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal
        runner.Model.Count
        999
        "Set 999 should be processed on next step"

    testCase "Immediate mode processes cascading dispatches in same step"
    <| fun _ ->
      let mutable dispatchDuringUpdate = Unchecked.defaultof<Dispatch<TestMsg>>

      let update msg model =
        match msg with
        | Increment ->
          dispatchDuringUpdate Decrement
          struct ({ model with Count = model.Count + 1 }, Cmd.none)
        | Decrement -> struct ({ model with Count = model.Count - 1 }, Cmd.none)
        | _ -> struct (model, Cmd.none)

      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      dispatchDuringUpdate <- runner.Dispatch
      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 0 "Increment +1 then Decrement -1 = 0"

    testCase "Quit mid-batch sets flag but finishes current frame"
    <| fun _ ->
      let update msg model =
        match msg with
        | Increment ->
          if model.Count = 1 then
            struct ({ model with Count = model.Count + 1 }, Cmd.signalExit)
          else
            struct ({ model with Count = model.Count + 1 }, Cmd.none)
        | _ -> struct (model, Cmd.none)

      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.DispatchMany [ Increment; Increment; Increment ]
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 3 "Should process all messages in batch"
      Expect.isTrue runner.ShouldQuit "Should be marked as quit"

    testCase "Zero delta processes messages but does not advance time"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.Zero)

      Expect.equal runner.Model.Count 1 "Messages should be processed"
      Expect.equal runner.TotalTime.TotalSeconds 0.0 "Time should not advance"

    testCase "Negative delta advances time by negative amount"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Step(TimeSpan.FromMilliseconds(-16))

      Expect.isLessThan
        runner.TotalTime.TotalSeconds
        0.0
        "Negative delta should result in negative time"

    testCase "Large delta with FixedStep caps at MaxStepsPerFrame"
    <| fun _ ->
      let mutable stepCount = 0

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withFixedStep {
            StepSeconds = 0.025f
            MaxStepsPerFrame = 3
            MaxFrameSeconds = ValueNone
            Map =
              fun _dt ->
                stepCount <- stepCount + 1
                Set stepCount
          }
        )

      runner.Step(TimeSpan.FromSeconds(10))

      Expect.equal stepCount 3 "Should cap at MaxStepsPerFrame"
      Expect.equal runner.Model.Count 3 "Model should reflect capped steps"

    testCase "StepUntil returns false when maxFrames reached"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      let neverTrue _model = false

      let met = runner.StepUntil(neverTrue, TimeSpan.FromMilliseconds(16), 5)

      Expect.isFalse met "Should return false when maxFrames reached"

    testCase "StepUntil returns immediately when condition already true"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      let alwaysTrue _model = true

      let met = runner.StepUntil(alwaysTrue, TimeSpan.FromMilliseconds(16))

      Expect.isTrue met "Should return true immediately"

    testCase "StepUntil stops early on Quit"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init (fun msg model ->
            match msg with
            | Increment ->
              if model.Count >= 2 then
                struct ({ model with Count = model.Count + 1 }, Cmd.signalExit)
              else
                struct ({ model with Count = model.Count + 1 }, Cmd.none)
            | _ -> struct (model, Cmd.none))
        )

      runner.DispatchMany [ Increment; Increment; Increment; Increment ]

      let neverTrue _model = false

      let met = runner.StepUntil(neverTrue, TimeSpan.FromMilliseconds(16))

      Expect.isTrue met "Should return true when quit stops execution early"
      Expect.isTrue runner.ShouldQuit "Runner should be in quit state"

    testCase "Step after Quit is a no-op"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init (fun msg model ->
            match msg with
            | Increment ->
              struct ({ model with Count = model.Count + 1 }, Cmd.signalExit)
            | _ -> struct (model, Cmd.none))
        )

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))

      let countAfterQuit = runner.Model.Count
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal
        runner.Model.Count
        countAfterQuit
        "Count should not change after quit"

    testCase "Deferred command runs on next step"
    <| fun _ ->
      let update msg model =
        match msg with
        | Increment ->
          struct ({ model with Count = model.Count + 1 },
                  Cmd.deferNextFrame(Cmd.ofMsg(Increment)))
        | _ -> struct (model, Cmd.none)

      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 1 "First step: process Increment"

      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 2 "Second step: deferred Increment runs"

    testCase "Step with no pending messages is a no-op"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal runner.Model.Count 0 "No messages dispatched, count stays 0"

    testCase "Subscription is disposed when model change removes it"
    <| fun _ ->
      let mutable disposed = false

      let subscribe _ctx model =
        if model.Count > 5 then
          Sub.none
        else
          Sub.Active(
            SubId.ofString "test",
            fun _dispatch ->
              { new IDisposable with
                  member _.Dispose() = disposed <- true
              }
          )

      let update msg model =
        match msg with
        | Increment -> struct ({ model with Count = model.Count + 1 }, Cmd.none)
        | _ -> struct (model, Cmd.none)

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withSubscribe subscribe
        )

      Expect.isFalse disposed "Should not be disposed yet"

      runner.DispatchMany [
        Increment
        Increment
        Increment
        Increment
        Increment
        Increment
      ]

      runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.isTrue
        disposed
        "Should be disposed after model changes remove subscription"
  ]
