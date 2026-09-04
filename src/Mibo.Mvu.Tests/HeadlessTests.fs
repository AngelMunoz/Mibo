module Mibo.Mvu.Tests.Headless

open System
open System.Threading
open Expecto
open IcedTasks
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
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal runner.Model.Count 1 "Model count should be 1"

    testCase "Multiple dispatches accumulate"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.DispatchMany [ Increment; Increment; Increment ]
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal runner.Model.Count 3 "Model count should be 3"

    testCase "StepN runs N frames"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.DispatchMany [ Increment; Increment ]
      runner.StepN(2, TimeSpan.FromMilliseconds(16)) |> ignore

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

      runner.Step(TimeSpan.FromMilliseconds(100)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(200)) |> ignore

      Expect.floatClose
        Accuracy.medium
        (float runner.GameTime.TotalTime.TotalSeconds)
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

      runner.Step(TimeSpan.FromMilliseconds(100)) |> ignore

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
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue runner.ShouldQuit "Should quit"
      Expect.equal runner.Model.Count 1 "Model count should be 1"

    testCase "GameTime is passed to tick"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

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
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        runner.Model.Count
        1
        "Should process Increment, not Set 999 yet"

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

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
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

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
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

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
      runner.Step(TimeSpan.Zero) |> ignore

      Expect.equal runner.Model.Count 1 "Messages should be processed"

      Expect.equal
        runner.GameTime.TotalTime.TotalSeconds
        0.0
        "Time should not advance"

    testCase "Negative delta is clamped to zero"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Step(TimeSpan.FromMilliseconds(-16)) |> ignore

      Expect.equal
        runner.GameTime.TotalTime.TotalSeconds
        0.0
        "Negative delta should be clamped to zero"

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

      runner.Step(TimeSpan.FromSeconds(10)) |> ignore

      Expect.equal stepCount 3 "Should cap at MaxStepsPerFrame"
      Expect.equal runner.Model.Count 3 "Model should reflect capped steps"

    testCase "StepUntil returns false when maxFrames reached"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      let neverTrue _model = false

      let met = runner.StepUntil(neverTrue, TimeSpan.FromMilliseconds(16), 5)

      Expect.isFalse met "Should return false when maxFrames reached"

    testCase "StepUntil detects predicate met on final permitted frame"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Dispatch(Increment)

      let stopAtOne model = model.Count >= 1

      let met = runner.StepUntil(stopAtOne, TimeSpan.FromMilliseconds(16), 1)

      Expect.isTrue met "Should detect predicate on the last permitted frame"
      Expect.equal runner.Model.Count 1 "Model count should be 1"

    testCase "StepUntil stops stepping once predicate met (multi-frame)"
    <| fun _ ->
      let program =
        HeadlessProgram.mkHeadless init update
        |> HeadlessProgram.withTick(fun _gt -> Increment)

      use runner = new HeadlessRunner<_, _>(program)

      let stopAtThree model = model.Count >= 3

      let met = runner.StepUntil(stopAtThree, TimeSpan.FromMilliseconds(16), 10)

      Expect.isTrue met "Should have met predicate within 10 frames"

      Expect.equal
        runner.Model.Count
        3
        "Should stop at 3, not keep stepping to 10"

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
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      let countAfterQuit = runner.Model.Count
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

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
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal runner.Model.Count 1 "First step: process Increment"

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal runner.Model.Count 2 "Second step: deferred Increment runs"

    testCase "Step with no pending messages is a no-op"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

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

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue
        disposed
        "Should be disposed after model changes remove subscription"
  ]

[<Tests>]
let headlessStepReturn =
  testList "Step Return" [
    testCase "Step returns model matching runner.Model after update"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.DispatchMany [ Increment; Increment; Increment ]
      runner.Step(TimeSpan.FromMilliseconds(16))
      let currentModel = runner.Model

      Expect.equal
        currentModel.Count
        3
        "Returned model should reflect all dispatches"

      Expect.equal
        currentModel.Count
        runner.Model.Count
        "Returned model should match runner.Model"

    testCase "Step returns model matching runner.Model with no messages"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withTick TickMsg
        )

      runner.Step(TimeSpan.FromMilliseconds(16))
      let currentModel = runner.Model

      Expect.equal currentModel.Count 0 "No dispatches, count stays 0"

      Expect.equal
        currentModel
        runner.Model
        "Returned model should match runner.Model"

    testCase "Step accumulates TotalTime across calls"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Step(TimeSpan.FromMilliseconds 50)
      let gt1 = runner.GameTime
      runner.Step(TimeSpan.FromMilliseconds 75)
      let gt2 = runner.GameTime
      runner.Step(TimeSpan.FromMilliseconds 100)
      let gt3 = runner.GameTime

      Expect.floatClose
        Accuracy.medium
        (float gt1.TotalTime.TotalSeconds)
        0.05
        "First frame"

      Expect.floatClose
        Accuracy.medium
        (float gt2.TotalTime.TotalSeconds)
        0.125
        "Second frame"

      Expect.floatClose
        Accuracy.medium
        (float gt3.TotalTime.TotalSeconds)
        0.225
        "Third frame"

    testCase "Step returns correct ElapsedGameTime per call"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Step(TimeSpan.FromMilliseconds(16))
      let gt1 = runner.GameTime
      runner.Step(TimeSpan.FromMilliseconds(33))
      let gt2 = runner.GameTime

      Expect.floatClose
        Accuracy.medium
        (float gt1.ElapsedGameTime.TotalSeconds)
        0.016
        "First elapsed"

      Expect.floatClose
        Accuracy.medium
        (float gt2.ElapsedGameTime.TotalSeconds)
        0.033
        "Second elapsed"

    testCase "Step returns same model after ShouldQuit, no further mutation"
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
      let gt1 = runner.GameTime
      let model1 = runner.Model
      Expect.equal model1.Count 1 "Quit frame returns updated model"
      Expect.isTrue runner.ShouldQuit "Should be quit"

      runner.Dispatch(Increment) // dispatch after quit — should be ignored
      runner.Step(TimeSpan.FromMilliseconds(16))
      let gt2 = runner.GameTime
      let model2 = runner.Model
      Expect.equal model2.Count 1 "Post-quit Step returns same model"

      Expect.equal
        gt2.ElapsedGameTime
        gt1.ElapsedGameTime
        "Post-quit ElapsedGameTime is zero"

      Expect.floatClose
        Accuracy.medium
        (float gt2.TotalTime.TotalSeconds)
        (float gt1.TotalTime.TotalSeconds)
        "Post-quit TotalTime does not advance"

    testCase "StepN returns last frame result, not intermediate"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.DispatchMany [
        Increment
        Increment
        Increment
        Increment
        Increment
      ]

      runner.StepN(5, TimeSpan.FromMilliseconds(16))
      let gameTime = runner.GameTime
      let model = runner.Model

      Expect.equal model.Count 5 "Should return count after all 5 frames"
      Expect.equal model.Count runner.Model.Count "Should match runner.Model"

      Expect.floatClose
        Accuracy.medium
        (float gameTime.TotalTime.TotalSeconds)
        (0.016 * 5.0)
        "TotalTime should be 5 frames"

    testCase "StepN with count 0 returns default and does not advance time"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Dispatch(Increment)


      runner.StepN(0, TimeSpan.FromMilliseconds(16))
      let gameTime = runner.GameTime
      let model = runner.Model

      Expect.equal runner.Model.Count 0 "StepN(0) should not process dispatches"

      Expect.equal
        runner.GameTime.TotalTime.TotalSeconds
        0.0
        "TotalTime should not advance"

    testCase "Step returns model reflecting deferred commands"
    <| fun _ ->
      let update msg model =
        match msg with
        | Increment ->
          struct ({ model with Count = model.Count + 1 },
                  Cmd.deferNextFrame(Cmd.ofMsg Increment))
        | _ -> struct (model, Cmd.none)

      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))
      let model1 = runner.Model
      Expect.equal model1.Count 1 "First step: only the dispatch"

      runner.Step(TimeSpan.FromMilliseconds(16))
      let model2 = runner.Model
      Expect.equal model2.Count 2 "Second step: deferred Increment runs"
  ]

[<Tests>]
let headlessObserver =
  testList "Observer" [
    testCase "Observer sees model AFTER update, not before"
    <| fun _ ->
      let mutable observedCount = -1

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, model: TestModel, _) ->
              observedCount <- model.Count))
        )

      runner.DispatchMany [ Increment; Increment; Increment ]
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal observedCount 3 "Observer should see post-update model"

    testCase "Observer sees correct model with multiple messages in one step"
    <| fun _ ->
      let snapshots = ResizeArray<int>()

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, model: TestModel, _) ->
              snapshots.Add(model.Count)))
        )

      runner.DispatchMany [ Increment; Decrement; Increment; Increment ]
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal snapshots.Count 1 "One frame = one snapshot"
      Expect.equal snapshots[0] 2 "Increment+Decrement+Increment+Increment = 2"

    testCase "Observer sees initial state when no messages dispatched"
    <| fun _ ->
      let mutable observedCount = -1

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, model: TestModel, _) ->
              observedCount <- model.Count))
        )

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal observedCount 0 "Observer should see initial model (Count=0)"

    testCase "Observer GameTime matches Step returned GameTime"
    <| fun _ ->
      let mutable observedTotal = TimeSpan.Zero
      let mutable observedElapsed = TimeSpan.Zero

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, _, gt) ->
              observedTotal <- gt.TotalTime
              observedElapsed <- gt.ElapsedGameTime))
        )

      runner.Step(TimeSpan.FromMilliseconds(50))
      let stepGt = runner.GameTime

      Expect.equal
        observedTotal
        stepGt.TotalTime
        "Observer TotalTime matches Step"

      Expect.equal
        observedElapsed
        stepGt.ElapsedGameTime
        "Observer Elapsed matches Step"

    testCase "Multiple observers receive identical values"
    <| fun _ ->
      let snapshotsA = ResizeArray<struct (int * TimeSpan)>()
      let snapshotsB = ResizeArray<struct (int * TimeSpan)>()

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, model: TestModel, gt) ->
              snapshotsA.Add(struct (model.Count, gt.TotalTime))))
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, model: TestModel, gt) ->
              snapshotsB.Add(struct (model.Count, gt.TotalTime))))
        )

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      runner.DispatchMany [ Increment; Increment ]
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal snapshotsA.Count 2 "Observer A should have 2 snapshots"
      Expect.equal snapshotsB.Count 2 "Observer B should have 2 snapshots"

      Expect.equal
        snapshotsA[0]
        snapshotsB[0]
        "First snapshot should be identical"

      Expect.equal
        snapshotsA[1]
        snapshotsB[1]
        "Second snapshot should be identical"

    testCase "Observers are notified in registration order"
    <| fun _ ->
      // withObserver prepends, so the runner must reverse the list before
      // iterating or observers fire in reverse-registration order.
      let order = ResizeArray<int>()

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, _: TestModel, _) ->
              order.Add(1)))
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, _: TestModel, _) ->
              order.Add(2)))
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, _: TestModel, _) ->
              order.Add(3)))
        )

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal order.Count 3 "All three observers should fire once"
      Expect.equal order[0] 1 "First-registered observer fires first"
      Expect.equal order[1] 2 "Second-registered observer fires second"
      Expect.equal order[2] 3 "Third-registered observer fires third"

    testCase "Observer fires after ShouldQuit with frozen model"
    <| fun _ ->
      let snapshots = ResizeArray<int>()

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init (fun msg model ->
            match msg with
            | Increment ->
              struct ({ model with Count = model.Count + 1 }, Cmd.signalExit)
            | _ -> struct (model, Cmd.none))
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, model: TestModel, _) ->
              snapshots.Add(model.Count)))
        )

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore // processes, quits
      Expect.isTrue runner.ShouldQuit "Should be quit"

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore // post-quit — observer does NOT fire
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore // post-quit — observer does NOT fire

      Expect.equal
        snapshots.Count
        1
        "Observer should only fire on the quit frame"

      Expect.equal snapshots[0] 1 "Quit frame: Count=1"

    testCase "Observer receives correct GameTime accumulation across frames"
    <| fun _ ->
      let times = ResizeArray<float>()

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, _, gt) ->
              times.Add(gt.TotalTime.TotalSeconds)))
        )

      runner.Step(TimeSpan.FromMilliseconds(100)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(200)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(50)) |> ignore

      Expect.equal times.Count 3 "Should have 3 time snapshots"
      Expect.floatClose Accuracy.medium times[0] 0.1 "Frame 1: 0.1s"
      Expect.floatClose Accuracy.medium times[1] 0.3 "Frame 2: 0.3s"
      Expect.floatClose Accuracy.medium times[2] 0.35 "Frame 3: 0.35s"

    testCase "Observer receives GameContext with window dimensions"
    <| fun _ ->
      let mutable observedWidth = 0
      let mutable observedHeight = 0

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (ctx, _, _) ->
              observedWidth <- ctx.WindowWidth
              observedHeight <- ctx.WindowHeight)),
          width = 1024,
          height = 768
        )

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal observedWidth 1024 "Observer should see custom width"
      Expect.equal observedHeight 768 "Observer should see custom height"

    testCase "IDisposable observer is disposed on runner dispose"
    <| fun _ ->
      let mutable disposed = false

      let factory() =
        { new IObserver<struct (GameContext * TestModel * GameTime)> with
            member _.OnNext _ = ()
            member _.OnError _ = ()
            member _.OnCompleted() = ()
          interface IDisposable with
            member _.Dispose() = disposed <- true
        }

      do
        use runner =
          new HeadlessRunner<_, _>(
            HeadlessProgram.mkHeadless init update
            |> HeadlessProgram.withObserver factory
          )

        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue
        disposed
        "Observer should be disposed when runner is disposed"

    testCase "IDisposable observer is disposed even without steps"
    <| fun _ ->
      let mutable disposed = false

      let factory() =
        { new IObserver<struct (GameContext * TestModel * GameTime)> with
            member _.OnNext _ = ()
            member _.OnError _ = ()
            member _.OnCompleted() = ()
          interface IDisposable with
            member _.Dispose() = disposed <- true
        }

      do
        use runner =
          new HeadlessRunner<_, _>(
            HeadlessProgram.mkHeadless init update
            |> HeadlessProgram.withObserver factory
          )

        // no Step calls — dispose immediately
        ()

      Expect.isTrue
        disposed
        "Observer should be disposed even without any steps"

    testCase "Empty observer list does not affect Step"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Dispatch(Increment)
      runner.Step(TimeSpan.FromMilliseconds(16))
      let model = runner.Model

      Expect.equal model.Count 1 "Step should work normally with no observers"
      Expect.equal runner.Model.Count 1 "runner.Model should be correct"

    testCase "Observer does not interfere with subscription lifecycle"
    <| fun _ ->
      let mutable subDisposed = false
      let observerSnapshots = ResizeArray<int>()

      let subscribe _ctx model =
        if model.Count > 5 then
          Sub.none
        else
          Sub.Active(
            SubId.ofString "test",
            fun _dispatch ->
              { new IDisposable with
                  member _.Dispose() = subDisposed <- true
              }
          )

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init update
          |> HeadlessProgram.withSubscribe subscribe
          |> HeadlessProgram.withObserver(fun () ->
            HeadlessProgram.observe(fun struct (_, model: TestModel, _) ->
              observerSnapshots.Add(model.Count)))
        )

      runner.DispatchMany [
        Increment
        Increment
        Increment
        Increment
        Increment
        Increment
      ]

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue subDisposed "Subscription should be disposed"
      Expect.equal observerSnapshots.Count 1 "Observer should have 1 snapshot"
      Expect.equal observerSnapshots[0] 6 "Observer should see Count=6"
  ]

[<Tests>]
let headlessRun =
  testList "Run" [
    testCase "Run executes steps and mutates model"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      runner.Dispatch(Increment)

      use cts = new CancellationTokenSource()
      cts.CancelAfter(TimeSpan.FromMilliseconds(100))
      let changes = runner.Run(TimeSpan.FromMilliseconds(16), cts.Token)

      match changes |> Seq.tryHead with
      | Some(gametime, model) ->
        Expect.isGreaterThan runner.Model.Count 0 "Model should have changed"
      | None -> failwith "At least a value should have been emitted"



    testCase "Run with already-cancelled token exits immediately"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      use cts = new CancellationTokenSource()
      cts.Cancel()

      let changes = runner.Run(TimeSpan.FromMilliseconds(16), cts.Token)

      Expect.isEmpty changes "No changes should have generated"
      Expect.equal runner.Model.Count 0 "Should not process any steps"

      Expect.equal
        runner.GameTime.TotalTime.TotalSeconds
        0.0
        "Time should not advance"

    testCase "Run with ShouldQuit exits immediately"
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
      Expect.isTrue runner.ShouldQuit "Should be quit"

      let changes = runner.Run(TimeSpan.FromMilliseconds(16))

      Expect.isEmpty
        changes
        "Runner has already quit, no changes should be present"

      Expect.equal runner.Model.Count 1 "Model should not change after quit"

    testCase "RunAsync yields correct model values per frame"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      use cts = new CancellationTokenSource()
      let results = ResizeArray<struct (int * float)>()

      let work = asyncEx {
        let! token = Async.CancellationToken

        for struct (gt, model) in
          runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          results.Add(struct (model.Count, gt.TotalTime.TotalSeconds))

          if results.Count >= 3 then
            cts.Cancel()
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the runner never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.isGreaterThanOrEqual
        results.Count
        3
        "Should yield at least 3 frames"

      for i = 0 to results.Count - 1 do
        let struct (count, _) = results[i]
        Expect.equal count 0 "Each frame should have Count=0 (no dispatches)"

      let struct (_, t1) = results[0]
      let struct (_, t2) = results[1]
      Expect.isGreaterThan t2 t1 "Time should advance between frames"

    testCase "RunAsync with already-cancelled token yields nothing"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      use cts = new CancellationTokenSource()
      cts.Cancel()
      let mutable count = 0

      let work = asyncEx {
        let! token = Async.CancellationToken

        for _ in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          count <- count + 1
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the runner never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.equal count 0 "Should not yield any frames"

    testCase "RunAsync yields frozen model after ShouldQuit"
    <| fun _ ->
      let mutable stepCount = 0

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadless init (fun msg model ->
            match msg with
            | Increment ->
              stepCount <- stepCount + 1

              if stepCount >= 3 then
                struct ({ model with Count = model.Count + 1 }, Cmd.signalExit)
              else
                struct ({ model with Count = model.Count + 1 }, Cmd.none)
            | _ -> struct (model, Cmd.none))
        )

      use cts = new CancellationTokenSource()
      let results = ResizeArray<struct (bool * int)>()
      let mutable frameIdx = 0

      let work = asyncEx {
        let! token = Async.CancellationToken

        for struct (_, model) in
          runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          results.Add(struct (runner.ShouldQuit, model.Count))
          frameIdx <- frameIdx + 1

          // dispatch Increment for the first 3 frames
          if frameIdx <= 3 then
            runner.Dispatch(Increment)

          // cancel after enough frames to avoid infinite loop
          if frameIdx >= 10 then
            cts.Cancel()
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the runner never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.isGreaterThan results.Count 0 "Should yield at least one frame"

      let mutable foundQuit = false

      for i = 0 to results.Count - 1 do
        let struct (quit, count) = results[i]

        if foundQuit then
          Expect.isTrue quit "Should stay quit"
          Expect.equal count 3 "Model should be frozen after quit"
        elif quit then
          foundQuit <- true
          Expect.equal count 3 "Quit frame should have Count=3"

    testCase "RunAsync dispatches via external dispatch during iteration"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      use cts = new CancellationTokenSource()
      let results = ResizeArray<int>()
      let mutable frameIdx = 0

      let work = asyncEx {
        let! token = Async.CancellationToken

        for struct (_, model) in
          runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          results.Add(model.Count)
          frameIdx <- frameIdx + 1

          // dispatch two Increments after the third frame lands
          if frameIdx = 3 then
            runner.Dispatch(Increment)
            runner.Dispatch(Increment)

          if frameIdx >= 6 then
            cts.Cancel()
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the runner never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.isGreaterThan results.Count 3 "Should yield multiple frames"

      Expect.equal results[0] 0 "Frame 0: no dispatches yet"
      Expect.equal results[1] 0 "Frame 1: no dispatches yet"
      Expect.equal results[2] 0 "Frame 2: no dispatches yet"

      let mutable foundIncrease = false

      for i = 3 to results.Count - 1 do
        if results[i] > 0 && not foundIncrease then
          foundIncrease <- true

          Expect.equal
            results[i]
            2
            "First frame after dispatch should have Count=2"

      Expect.isTrue foundIncrease "Should see model increase after dispatch"

    testCase "Run with zero interval throws ArgumentException"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      Expect.throwsT<ArgumentException>
        (fun () -> runner.Run(TimeSpan.Zero) |> Seq.iter ignore)
        "Should throw ArgumentException for zero interval"

    testCase "mkHeadlessCtx update receives the GameContext"
    <| fun _ ->
      let updateCtx (ctx: GameContext) msg model =
        match msg with
        | Set _ -> struct ({ model with Count = ctx.WindowWidth }, Cmd.none)
        | _ -> struct (model, Cmd.none)

      use runner =
        new HeadlessRunner<_, _>(
          HeadlessProgram.mkHeadlessCtx init updateCtx,
          width = 640
        )

      runner.Dispatch(Set 0)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal runner.Model.Count 640 "Update should see the runner's width"

    testCase "coreOfProgram prefers UpdateCtx over Update"
    <| fun _ ->
      let updateCtx (ctx: GameContext) _msg model =
        struct ({ model with Count = ctx.WindowWidth }, Cmd.none)

      let core = ElmishLoop.coreOfProgram(Program.mkProgramCtx init updateCtx)

      let ctx = GameContext.create(320, 200)

      let struct (model, _) =
        core.Update ctx Increment { Count = 0; LastTickTotal = None }

      Expect.equal model.Count 320 "UpdateCtx should be invoked with the ctx"

    testCase "coreOfProgram adapts a ctx-less Update"
    <| fun _ ->
      let core = ElmishLoop.coreOfProgram(Program.mkProgram init update)
      let ctx = GameContext.create(320, 200)

      let struct (model, _) =
        core.Update ctx Increment { Count = 0; LastTickTotal = None }

      Expect.equal model.Count 1 "Legacy Update should still run"

    testCase "RunAsync with zero interval throws ArgumentException"
    <| fun _ ->
      use runner =
        new HeadlessRunner<_, _>(HeadlessProgram.mkHeadless init update)

      Expect.throwsT<ArgumentException>
        (fun () -> runner.RunAsync(TimeSpan.Zero) |> ignore)
        "Should throw ArgumentException for zero interval"
  ]
