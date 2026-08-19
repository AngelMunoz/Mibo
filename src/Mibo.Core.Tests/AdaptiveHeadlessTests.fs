module Mibo.Adaptive.Tests.AdaptiveHeadless

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input
open IcedTasks

[<Struct>]
type TestFrame = { Position: float32; Velocity: float32 }

/// Builds a program with two independent roots and two counted projections.
/// Returns the program, the roots, and the recompute counters.
let mkTestProgram() =
  let pos = CVal.create 0.0f
  let vel = CVal.create 1.0f
  let mutable posRecomputes = 0
  let mutable velRecomputes = 0

  let posProj =
    CVal.value pos
    |> AVal.map(fun p ->
      posRecomputes <- posRecomputes + 1
      p)

  let velProj =
    CVal.value vel
    |> AVal.map(fun v ->
      velRecomputes <- velRecomputes + 1
      v)

  let program =
    AdaptiveProgram.mkProgram
      (fun _ctx ->
        AdaptiveInit.ofFrameBuilder(fun () -> {
          Position = AVal.getValue posProj
          Velocity = AVal.getValue velProj
        }))
      (fun _ctx _gameTime -> ())

  struct (program,
          pos,
          vel,
          (fun () -> posRecomputes),
          (fun () -> velRecomputes))

/// A program whose root is created inside <c>Init</c> — the only legal shape
/// for <c>RunAsync</c>, because <c>Init</c> runs on the runner's dedicated game
/// thread and the adaptive graph is confined to its creating thread. The root
/// handle is published to the caller once <c>Init</c> has run (after the first
/// frame is yielded); the test thread writes it via <c>Post</c>, the only
/// cross-thread-safe write.
let mkRunAsyncProgram() =
  let mutable root = Unchecked.defaultof<cval<float32>>

  let program =
    AdaptiveProgram.mkProgram
      (fun _ctx ->
        let pos = CVal.create 0.0f
        root <- pos
        AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue pos))
      (fun _ctx _gameTime -> ())

  struct (program, (fun () -> root))

/// A program whose frame is the time root's total time in seconds.
let mkTimeProgram() =
  AdaptiveProgram.mkProgram
    (fun ctx ->
      let totalSeconds =
        CVal.value ctx.Time |> AVal.map(fun gt -> gt.TotalTime.TotalSeconds)

      AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue totalSeconds))
    (fun _ctx _gameTime -> ())

/// A RunAsync program that starts a background task on the first step; the
/// completion writes a world-owned root. The root handle is published once
/// Init has run.
let mkRunAsyncWorkProgram() =
  let mutable root = Unchecked.defaultof<cval<int>>
  let mutable started = false

  let program =
    AdaptiveProgram.mkProgram
      (fun _ctx ->
        let value = CVal.create 0
        root <- value

        AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
      (fun ctx _gameTime ->
        if not started then
          started <- true

          ctx.Intents.postTask(
            (fun () -> Task.FromResult(99)),
            (fun v -> root.Set v),
            (fun _ -> ())
          ))

  struct (program, (fun () -> root))

[<Tests>]
let adaptiveHeadlessTests =
  testList "AdaptiveHeadless" [
    testCase "Step returns the forced frame with current root values"
    <| fun _ ->
      let struct (program, pos, _vel, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      pos.Set(42.0f)
      let frame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal frame.Position 42.0f "Frame should reflect the written root"
      Expect.equal frame.Velocity 1.0f "Unchanged root should keep its value"

    testCase "Many writes between steps settle to one recompute per force"
    <| fun _ ->
      let struct (program, pos, _vel, posRecomputes, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      // First step initializes the program (Init + first force).
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      let baseline = posRecomputes()

      for i = 1 to 10 do
        pos.Set(float32 i)

      let frame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal
        (posRecomputes() - baseline)
        1
        "Ten writes before one force cost one recompute"

      Expect.equal frame.Position 10.0f "Frame should reflect the last write"

    testCase "Only the dirty fan recomputes"
    <| fun _ ->
      let struct (program, pos, _vel, posRecomputes, velRecomputes) =
        mkTestProgram()

      use runner = new AdaptiveHeadless<TestFrame>(program)

      // First step initializes the program (Init + first force).
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      let posBefore = posRecomputes()
      let velBefore = velRecomputes()

      pos.Set(5.0f)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (posRecomputes() - posBefore)
        1
        "Position fan should recompute once"

      Expect.equal
        (velRecomputes() - velBefore)
        0
        "Velocity fan should not recompute"

    testCase "Idle step recomputes nothing"
    <| fun _ ->
      let struct (program, _pos, _vel, posRecomputes, velRecomputes) =
        mkTestProgram()

      use runner = new AdaptiveHeadless<TestFrame>(program)

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      let posBefore = posRecomputes()
      let velBefore = velRecomputes()

      for _ = 1 to 10 do
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (posRecomputes() - posBefore)
        0
        "Idle steps must not recompute the position projection"

      Expect.equal
        (velRecomputes() - velBefore)
        0
        "Idle steps must not recompute the velocity projection"

    testCase "Warmed idle Step allocates nothing"
    <| fun _ ->
      let struct (program, _pos, _vel, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      for _ = 1 to 1000 do
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      let before = GC.GetAllocatedBytesForCurrentThread()

      for _ = 1 to 1000 do
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      let allocated = GC.GetAllocatedBytesForCurrentThread() - before

      // Budget < 1 byte/call for one-off runtime noise (JIT, GC bookkeeping);
      // the regression this guards is per-call allocation in the frame boundary
      // (time write, pump, force, observer iteration).
      Expect.isLessThan
        allocated
        1024L
        $"Idle Step allocated {allocated} bytes over 1000 warmed-up calls"

    testCase "Time root advances with each step"
    <| fun _ ->
      use runner = new AdaptiveHeadless<float>(mkTimeProgram())

      runner.Step(TimeSpan.FromMilliseconds(100)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(200)) |> ignore
      let frame = runner.Step(TimeSpan.FromMilliseconds(50))

      Expect.floatClose
        Accuracy.medium
        frame
        0.35
        "Time root should accumulate across steps"

      Expect.floatClose
        Accuracy.medium
        (runner.GameTime.TotalTime.TotalSeconds)
        0.35
        "Runner game time should accumulate across steps"

    testCase "Negative delta is clamped to zero"
    <| fun _ ->
      use runner = new AdaptiveHeadless<float>(mkTimeProgram())

      runner.Step(TimeSpan.FromMilliseconds(-16)) |> ignore

      Expect.floatClose
        Accuracy.medium
        (runner.GameTime.TotalTime.TotalSeconds)
        0.0
        "Negative delta should be clamped to zero"

    testCase "Update phase runs once per step with the frame's game time"
    <| fun _ ->
      let mutable updates = 0
      let mutable lastElapsed = TimeSpan.Zero
      let mutable observedFromTimeRoot = TimeSpan.Zero

      let program =
        AdaptiveProgram.mkProgram
          (fun ctx ->
            AdaptiveInit.ofFrameBuilder(fun () ->
              let gt = AVal.getValue ctx.Time
              gt.ElapsedGameTime.TotalMilliseconds))
          (fun ctx gameTime ->
            updates <- updates + 1
            lastElapsed <- gameTime.ElapsedGameTime

            let gt = AVal.getValue ctx.Time
            observedFromTimeRoot <- gt.ElapsedGameTime)

      use runner = new AdaptiveHeadless<float>(program)

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(33)) |> ignore

      Expect.equal updates 2 "Update should run once per step"

      Expect.equal
        lastElapsed
        (TimeSpan.FromMilliseconds 33)
        "Update should receive the frame's elapsed"

      Expect.equal
        observedFromTimeRoot
        (TimeSpan.FromMilliseconds 33)
        "Time root should already hold the frame's time"

    testCase "ExitRequested stops the runner; post-quit Step is a no-op"
    <| fun _ ->
      let mutable exitCell = Unchecked.defaultof<cval<bool>>
      let pos = CVal.create 0.0f

      let program =
        AdaptiveProgram.mkProgram
          (fun ctx ->
            exitCell <- ctx.ExitRequested
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue pos))
          (fun _ctx _gameTime -> ())

      use runner = new AdaptiveHeadless<float32>(program)

      pos.Set(5.0f)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      let timeBeforeQuit = runner.GameTime

      Expect.isFalse runner.ShouldQuit "Should not quit yet"
      Expect.equal runner.Frame 5.0f "Frame should reflect the written root"

      exitCell.Set(true)
      let postQuitFrame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.isTrue runner.ShouldQuit "Should quit after the request"

      Expect.equal
        postQuitFrame
        5.0f
        "Post-quit step should return the cached frame"

      Expect.equal
        runner.GameTime
        timeBeforeQuit
        "Post-quit time should not advance"

    testCase
      "Observer sees the forced frame after update, in registration order"
    <| fun _ ->
      let struct (program, pos, _vel, _, _) = mkTestProgram()
      let order = ResizeArray<int>()

      let program =
        program
        |> AdaptiveProgram.withObserver(fun () ->
          AdaptiveProgram.observe(fun struct (_, _, _) -> order.Add(1)))
        |> AdaptiveProgram.withObserver(fun () ->
          AdaptiveProgram.observe(fun struct (_, _, _) -> order.Add(2)))
        |> AdaptiveProgram.withObserver(fun () ->
          AdaptiveProgram.observe(fun struct (_, _, _) -> order.Add(3)))

      use runner = new AdaptiveHeadless<TestFrame>(program)

      pos.Set(7.0f)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal order.Count 3 "All three observers should fire once"
      Expect.equal order[0] 1 "First-registered observer fires first"
      Expect.equal order[1] 2 "Second-registered observer fires second"
      Expect.equal order[2] 3 "Third-registered observer fires third"

    testCase "Observer receives the forced frame and the frame's game time"
    <| fun _ ->
      let struct (program, pos, _vel, _, _) = mkTestProgram()
      let mutable observedFrame = Unchecked.defaultof<TestFrame>
      let mutable observedElapsed = TimeSpan.Zero

      let program =
        program
        |> AdaptiveProgram.withObserver(fun () ->
          AdaptiveProgram.observe(fun struct (_, frame, gameTime) ->
            observedFrame <- frame
            observedElapsed <- gameTime.ElapsedGameTime))

      use runner = new AdaptiveHeadless<TestFrame>(program)

      pos.Set(3.0f)
      runner.Step(TimeSpan.FromMilliseconds(25)) |> ignore

      Expect.equal
        observedFrame.Position
        3.0f
        "Observer should see the forced frame"

      Expect.equal
        observedElapsed
        (TimeSpan.FromMilliseconds 25)
        "Observer should see the frame's elapsed"

    testCase "IDisposable observer is disposed on runner dispose"
    <| fun _ ->
      let mutable disposed = false

      let factory() =
        { new IObserver<struct (GameContext * TestFrame * GameTime)> with
            member _.OnNext _ = ()
            member _.OnError _ = ()
            member _.OnCompleted() = ()
          interface IDisposable with
            member _.Dispose() = disposed <- true
        }

      let struct (program, _, _, _, _) = mkTestProgram()
      let program = program |> AdaptiveProgram.withObserver factory

      do
        use runner = new AdaptiveHeadless<TestFrame>(program)
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue
        disposed
        "Observer should be disposed when runner is disposed"

    testCase "Init disposables are disposed on runner dispose"
    <| fun _ ->
      let mutable disposed = false

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> 0.0f)
            |> AdaptiveInit.withDisposable
              { new IDisposable with
                  member _.Dispose() = disposed <- true
              })
          (fun _ctx _gameTime -> ())

      do
        use runner = new AdaptiveHeadless<float32>(program)
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue disposed "Init disposables should be disposed"

    testCase "StepN advances N frames and returns the last frame"
    <| fun _ ->
      let counter = CVal.create 0

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue counter))
          (fun _ctx _gameTime -> counter.Set(AVal.getValue counter + 1))

      use runner = new AdaptiveHeadless<int>(program)

      let frame = runner.StepN(5, TimeSpan.FromMilliseconds(16))

      Expect.equal frame 5 "StepN should return the last forced frame"
      Expect.equal runner.Frame 5 "Runner frame should be the last frame"

    testCase "StepN with count 0 returns the current frame"
    <| fun _ ->
      let struct (program, pos, _vel, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      pos.Set(4.0f)
      let frame = runner.StepN(0, TimeSpan.FromMilliseconds(16))

      Expect.equal frame.Position 0.0f "StepN(0) should not step"

      Expect.floatClose
        Accuracy.medium
        (runner.GameTime.TotalTime.TotalSeconds)
        0.0
        "TotalTime should not advance"

    testCase "StepUntil stops when the frame predicate is met"
    <| fun _ ->
      let counter = CVal.create 0

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue counter))
          (fun _ctx _gameTime -> counter.Set(AVal.getValue counter + 1))

      use runner = new AdaptiveHeadless<int>(program)

      let met =
        runner.StepUntil((fun n -> n >= 3), TimeSpan.FromMilliseconds(16), 10)

      Expect.isTrue met "Should meet the predicate within 10 frames"
      Expect.equal runner.Frame 3 "Frame should be 3"

    testCase "StepUntil returns false when maxFrames reached"
    <| fun _ ->
      let struct (program, _, _, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      let neverTrue _frame = false

      let met = runner.StepUntil(neverTrue, TimeSpan.FromMilliseconds(16), 5)

      Expect.isFalse met "Should return false when maxFrames reached"

    testCase "Run executes steps and yields frames"
    <| fun _ ->
      let struct (program, pos, _vel, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      pos.Set(9.0f)

      use cts = new CancellationTokenSource()
      cts.CancelAfter(TimeSpan.FromMilliseconds(100))
      let frames = runner.Run(TimeSpan.FromMilliseconds(16), cts.Token)

      match frames |> Seq.tryHead with
      | Some struct (_, frame) ->
        Expect.equal frame.Position 9.0f "Yielded frame should reflect the root"
      | None -> failwith "At least a value should have been emitted"

    testCase "Run with already-cancelled token exits immediately"
    <| fun _ ->
      let struct (program, _, _, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      use cts = new CancellationTokenSource()
      cts.Cancel()

      let frames = runner.Run(TimeSpan.FromMilliseconds(16), cts.Token)

      Expect.isEmpty frames "No frames should have been generated"
      Expect.equal runner.Frame.Position 0.0f "Should not process any steps"

      Expect.floatClose
        Accuracy.medium
        (runner.GameTime.TotalTime.TotalSeconds)
        0.0
        "Time should not advance"

    testCase "Run with zero interval throws ArgumentException"
    <| fun _ ->
      let struct (program, _, _, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      Expect.throwsT<ArgumentException>
        (fun () -> runner.Run(TimeSpan.Zero) |> Seq.iter ignore)
        "Should throw ArgumentException for zero interval"

    testCase "RunAsync yields frames with advancing time"
    <| fun _ ->
      // The graph must be created on the game thread: RunAsync evaluates on a
      // dedicated thread, and the adaptive graph is confined to its creating
      // thread (the owner-thread check in Debug builds). Roots built on the
      // test thread — as the other tests do for Step — violate that contract
      // and throw from the game thread.
      let struct (program, getRoot) = mkRunAsyncProgram()
      use runner = new AdaptiveHeadless<float32>(program)

      use cts = new CancellationTokenSource()
      let results = ResizeArray<struct (float32 * float)>()

      let work = asyncEx {
        let! token = Async.CancellationToken

        for outcome in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          results.Add(
            struct (outcome.Frame, outcome.GameTime.TotalTime.TotalSeconds)
          )

          // Init has run by the first frame, so the root handle is
          // published; cross-thread writes go through Post and land at
          // the next step boundary on the game thread.
          if results.Count = 1 then
            getRoot().Post(2.0f)

          // Stop once the posted value shows up in a frame; the timeout
          // bounds the wait if it never does.
          if outcome.Frame = 2.0f then
            cts.Cancel()
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the game thread never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.isGreaterThanOrEqual
        results.Count
        2
        "Should yield at least 2 frames"

      let struct (first, _) = results[0]
      Expect.equal first 0.0f "First frame should carry the initial value"

      let posted = results |> Seq.exists(fun struct (v, _) -> v = 2.0f)
      Expect.isTrue posted "Posted value should land in a later frame"

      let struct (_, t1) = results[0]
      let struct (_, t2) = results[1]
      Expect.isGreaterThan t2 t1 "Time should advance between frames"

    testCase "RunAsync with already-cancelled token yields nothing"
    <| fun _ ->
      // Trivial program: no roots are created, so nothing runs on the game
      // thread beyond Init itself (which builds its graph on that thread).
      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx -> AdaptiveInit.ofFrameBuilder(fun () -> 0.0f))
          (fun _ctx _gameTime -> ())

      use runner = new AdaptiveHeadless<float32>(program)

      use cts = new CancellationTokenSource()
      cts.Cancel()
      let mutable count = 0

      let work = asyncEx {
        let! token = Async.CancellationToken

        for _ in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          count <- count + 1
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the game thread never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.equal count 0 "Should not yield any frames"

    testCase "RunAsync with zero interval throws ArgumentException"
    <| fun _ ->
      let struct (program, _, _, _, _) = mkTestProgram()
      use runner = new AdaptiveHeadless<TestFrame>(program)

      Expect.throwsT<ArgumentException>
        (fun () -> runner.RunAsync(TimeSpan.Zero) |> ignore)
        "Should throw ArgumentException for zero interval"

    testCase "RunAsync surfaces game-thread failures from MoveNextAsync"
    <| fun _ ->
      // An exception on the game thread (e.g. Init or Update throwing) must
      // not kill the process: it is stored and rethrown from MoveNextAsync so
      // the consumer sees a normal error.
      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx -> failwith "boom")
          (fun _ctx _gameTime -> ())

      use runner = new AdaptiveHeadless<float32>(program)
      let mutable sawBoom = false

      let work = asyncEx {
        let! token = Async.CancellationToken

        for _ in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          ()
      }

      use cts = new CancellationTokenSource()
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with ex ->
        // No AggregateException unwrap: the runner rethrows the stored
        // game-thread failure via ExceptionDispatchInfo.
        sawBoom <- ex.Message = "boom"

      Expect.isTrue
        sawBoom
        "MoveNextAsync should rethrow the game-thread failure"

    testCase "RunAsync rejects a second concurrent enumerator"
    <| fun _ ->
      // Two enumerators would step the same runner concurrently (unprotected
      // gameTime/frame/accumulator); the second GetAsyncEnumerator is rejected.
      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx -> AdaptiveInit.ofFrameBuilder(fun () -> 0.0f))
          (fun _ctx _gameTime -> ())

      use runner = new AdaptiveHeadless<float32>(program)
      let enum = runner.RunAsync(TimeSpan.FromMilliseconds(16))
      let enumerator = enum.GetAsyncEnumerator()

      try
        Expect.throwsT<InvalidOperationException>
          (fun () -> enum.GetAsyncEnumerator() |> ignore)
          "A second concurrent enumerator should be rejected"
      finally
        enumerator.DisposeAsync().AsTask().Wait()

    testCase "FixedStep runs Update once per sub-step and forces the frame once"
    <| fun _ ->
      let mutable updateCount = 0
      let mutable observedElapsed = 0.0f
      let counter = CVal.create 0

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue counter))
          (fun _ctx gameTime ->
            updateCount <- updateCount + 1

            observedElapsed <-
              observedElapsed + float32 gameTime.ElapsedGameTime.TotalSeconds

            counter.Set(AVal.getValue counter + 1))
        |> AdaptiveProgram.withFixedStep {
          StepSeconds = 0.01f
          MaxStepsPerFrame = 5
          MaxFrameSeconds = ValueNone
        }

      use runner = new AdaptiveHeadless<int>(program)
      // One 50ms frame at a 10ms step → 5 sub-steps; the frame is forced once.
      let frame = runner.Step(TimeSpan.FromMilliseconds 50)

      Expect.equal updateCount 5 "Update should run once per sub-step"
      Expect.equal frame 5 "Counter increments once per sub-step"

      Expect.floatClose
        Accuracy.medium
        (float observedElapsed)
        0.05
        "Sub-step elapsed times should accumulate to the frame delta"

    testCase "FixedStep caps sub-steps at MaxStepsPerFrame"
    <| fun _ ->
      let mutable updateCount = 0
      let counter = CVal.create 0

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue counter))
          (fun _ctx _gameTime ->
            updateCount <- updateCount + 1
            counter.Set(AVal.getValue counter + 1))
        |> AdaptiveProgram.withFixedStep {
          StepSeconds = 0.01f
          MaxStepsPerFrame = 3
          MaxFrameSeconds = ValueNone
        }

      use runner = new AdaptiveHeadless<int>(program)
      // 50ms frame, 10ms step, max 3 steps → capped at 3.
      runner.Step(TimeSpan.FromMilliseconds 50) |> ignore

      Expect.equal updateCount 3 "Update should be capped at MaxStepsPerFrame"
      Expect.equal runner.Frame 3 "Frame should reflect the capped steps"

    testCase "FixedStep with no accumulated time runs zero sub-steps"
    <| fun _ ->
      let mutable updateCount = 0
      let counter = CVal.create 0

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue counter))
          (fun _ctx _gameTime -> updateCount <- updateCount + 1)
        |> AdaptiveProgram.withFixedStep {
          StepSeconds = 0.05f
          MaxStepsPerFrame = 5
          MaxFrameSeconds = ValueNone
        }

      use runner = new AdaptiveHeadless<int>(program)
      // 10ms < 50ms step → no sub-step this frame.
      runner.Step(TimeSpan.FromMilliseconds 10) |> ignore

      Expect.equal updateCount 0 "No sub-step should run below one step delta"

    testCase "withFixedStep rejects non-positive StepSeconds"
    <| fun _ ->
      Expect.throwsT<ArgumentException>
        (fun () ->
          AdaptiveProgram.mkProgram
            (fun _ctx -> AdaptiveInit.ofFrameBuilder(fun () -> 0.0f))
            (fun _ _ -> ())
          |> AdaptiveProgram.withFixedStep {
            StepSeconds = 0.0f
            MaxStepsPerFrame = 5
            MaxFrameSeconds = ValueNone
          }
          |> ignore)
        "StepSeconds <= 0 should throw"
  ]

[<Tests>]
let adaptiveHeadlessDeferredWorkAndSubscriptionsTests =
  testList "AdaptiveHeadless deferred work and subscriptions" [
    testCase
      "Intent posted during Init runs at the startup drain, before the first Update"
    <| fun _ ->
      let value = CVal.create 0
      let trace = ResizeArray<string>()

      let program =
        AdaptiveProgram.mkProgram
          (fun ctx ->
            ctx.Intents.post(fun () ->
              trace.Add "post"
              value.Set 7)

            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun _ctx _gameTime -> trace.Add "update")

      use runner = new AdaptiveHeadless<int>(program)
      let frame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal
        (List.ofSeq trace)
        [ "post"; "update" ]
        "An Init-posted intent should drain at startup, before the first Update"

      Expect.equal
        frame
        7
        "The first forced frame should include the Init-posted write"

    testCase
      "A chain posted during Init drains until empty at the startup drain"
    <| fun _ ->
      let value = CVal.create 0
      let trace = ResizeArray<string>()

      let program =
        AdaptiveProgram.mkProgram
          (fun ctx ->
            ctx.Intents.post(fun () ->
              trace.Add "a"
              value.Set 1

              ctx.Intents.post(fun () ->
                trace.Add "b"
                value.Set 2))

            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun _ctx _gameTime -> trace.Add "update")

      use runner = new AdaptiveHeadless<int>(program)
      let frame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal
        (List.ofSeq trace)
        [ "a"; "b"; "update" ]
        "The startup drain should run until empty, before the first Update"

      Expect.equal frame 2 "The first force should see the chain's final write"

    testCase
      "postNextFrame posted during Init runs at the first step's boundary, before Update"
    <| fun _ ->
      let value = CVal.create 0
      let trace = ResizeArray<string>()

      let program =
        AdaptiveProgram.mkProgram
          (fun ctx ->
            ctx.Intents.postNextFrame(fun () ->
              trace.Add "next-step"
              value.Set 9)

            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun _ctx _gameTime -> trace.Add "update")

      use runner = new AdaptiveHeadless<int>(program)

      // The initial predicate check sees the first force, built before the
      // first step's boundary drain, so the deferral is not there yet. The
      // next check sees it, after one step.
      let met =
        runner.StepUntil((fun f -> f = 9), TimeSpan.FromMilliseconds(16))

      Expect.isTrue met "The predicate should be met after one step"

      Expect.equal
        (List.ofSeq trace)
        [ "next-step"; "update" ]
        "The deferral should run at the first boundary, before Update"

      Expect.equal runner.Frame 9 "The write should reach the force"

    testCase "Intents posted during Init run exactly once"
    <| fun _ ->
      let value = CVal.create 0
      let mutable count = 0

      let program =
        AdaptiveProgram.mkProgram
          (fun ctx ->
            ctx.Intents.post(fun () ->
              count <- count + 1
              value.Set count)

            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun _ctx _gameTime -> ())

      use runner = new AdaptiveHeadless<int>(program)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal count 1 "The startup drain should run once, not per step"

    testCase
      "postTask started during Init: starter at the startup drain, completion at a later drain"
    <| fun _ ->
      let mutable root = Unchecked.defaultof<cval<int>>
      let started = TaskCompletionSource<int>()

      let program =
        AdaptiveProgram.mkProgram
          (fun ctx ->
            let value = CVal.create 0
            root <- value

            ctx.Intents.postTask(
              (fun () -> started.Task),
              (fun v -> root.Set v),
              (fun _ -> ())
            )

            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun _ctx _gameTime -> ())

      use runner = new AdaptiveHeadless<int>(program)
      use cts = new CancellationTokenSource()

      // The first frame is 0 because the task is unfinished at the startup
      // drain; the completion (thread pool) writes 42 at a later post drain.
      // Use RunAsync so the loop yields to the thread pool between steps —
      // a synchronous spin loop can starve the continuation on constrained CI.
      let mutable seenFirst = false
      let mutable seenCompletion = false

      let work = asyncEx {
        let! token = Async.CancellationToken

        for outcome in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          if not seenFirst then
            Expect.equal
              outcome.Frame
              0
              "The completion cannot land before the task finishes"

            seenFirst <- true
            started.SetResult(42)

          if outcome.Frame = 42 then
            seenCompletion <- true
            cts.Cancel()
      }

      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.isTrue seenFirst "Should have seen the initial frame"
      Expect.isTrue seenCompletion "The completion should reach a later drain"

    testCase
      "Posted intent runs in the same step, after Update and before the force"
    <| fun _ ->
      let value = CVal.create 0
      let mutable seenInUpdate = -1
      let trace = ResizeArray<string>()

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun _ctx _gameTime ->
            trace.Add "update"
            seenInUpdate <- AVal.getValue value)

      use runner = new AdaptiveHeadless<int>(program)

      // Warm up so the owner thread (and the intent queue) exist before posting.
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      trace.Clear()

      runner.Post(fun () ->
        trace.Add "post"
        value.Set 42)

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (List.ofSeq trace)
        [ "update"; "post" ]
        "Posted intent should drain after Update, in the same step"

      Expect.equal
        seenInUpdate
        0
        "Update should not yet see the intent of its own step"

      Expect.equal runner.Frame 42 "The force should see the intent's write"

    testCase
      "Intent posted during Update runs in the same step, before the force"
    <| fun _ ->
      let value = CVal.create 0
      let trace = ResizeArray<string>()

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun ctx _gameTime ->
            trace.Add "update"

            if trace.Count = 1 then
              ctx.Intents.post(fun () ->
                trace.Add "post"
                value.Set 7))

      use runner = new AdaptiveHeadless<int>(program)

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (List.ofSeq trace)
        [ "update"; "post" ]
        "An Update-posted intent should drain in the same step, after Update"

      Expect.equal
        runner.Frame
        7
        "The posted intent's write should reach the force"

    testCase
      "Intent posted from a foreign thread lands at the next post drain, in post order"
    <| fun _ ->
      let trace = ResizeArray<int>()
      // -1 marks the Update phase, so the post drain is observable.
      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx -> AdaptiveInit.ofFrameBuilder(fun () -> 0.0f))
          (fun _ctx _gameTime -> trace.Add -1)

      use runner = new AdaptiveHeadless<float32>(program)
      // Warm up so the owner thread (and the intent queue) exist before the
      // foreign thread posts. The warm-up step's Update already wrote to the
      // trace; reset it so the emptiness assertion below only sees the posts.
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      trace.Clear()

      use doneSignal = new ManualResetEventSlim(false)

      let thread =
        Thread(fun () ->
          runner.Post(fun () -> trace.Add 1)
          runner.Post(fun () -> trace.Add 2)
          runner.Post(fun () -> trace.Add 3)
          doneSignal.Set())

      thread.IsBackground <- true
      thread.Start()
      doneSignal.Wait()
      thread.Join()

      Expect.isEmpty
        trace
        "Foreign-thread intents must not run on the posting thread"

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (List.ofSeq trace)
        [ -1; 1; 2; 3 ]
        "Foreign-thread intents should drain in post order, at the post drain"

    testCase "A posted chain drains until empty within one step, in post order"
    <| fun _ ->
      let value = CVal.create 0
      let trace = ResizeArray<string>()

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun ctx _gameTime ->
            trace.Add "update"

            if trace.Count = 1 then
              // A posts B, B posts C: the post drain runs until empty, so
              // the whole chain settles within this step, before the force.
              ctx.Intents.post(fun () ->
                trace.Add "a"
                value.Set 1

                ctx.Intents.post(fun () ->
                  trace.Add "b"
                  value.Set 2

                  ctx.Intents.post(fun () ->
                    trace.Add "c"
                    value.Set 3))))

      use runner = new AdaptiveHeadless<int>(program)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (List.ofSeq trace)
        [ "update"; "a"; "b"; "c" ]
        "The chain should drain in one step, in post order"

      Expect.equal runner.Frame 3 "The force should see the chain's final write"

    testCase "NextStep runs at the next step's boundary, not this step"
    <| fun _ ->
      let value = CVal.create 0
      let trace = ResizeArray<string>()

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun ctx _gameTime ->
            trace.Add "update"

            if trace.Count = 1 then
              ctx.Intents.postNextFrame(fun () ->
                trace.Add "next-step"
                value.Set 9))

      use runner = new AdaptiveHeadless<int>(program)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (List.ofSeq trace)
        [ "update" ]
        "A next-step deferral must not run in the posting step"

      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (List.ofSeq trace)
        [ "update"; "next-step"; "update" ]
        "A next-step deferral runs at the next boundary, before Update"

      Expect.equal runner.Frame 9 "The deferral's write should reach the force"

    testCase
      "A posted intent's write is visible in the same step's forced frame"
    <| fun _ ->
      // The dead-enemy case: Update posts the removal; the post drain
      // applies it before the Force, so the frame never renders the
      // half-settled state (a dead enemy drawn standing for one frame).
      let alive = CVal.create true
      let mutable posted = false

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue alive))
          (fun ctx _gameTime ->
            if not posted && AVal.getValue alive then
              posted <- true
              ctx.Intents.post(fun () -> alive.Set false))

      use runner = new AdaptiveHeadless<bool>(program)
      let frame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.isTrue posted "The intent should have been posted"
      Expect.isFalse frame "The force must see the posted intent's write"

    testCase
      "Posted chain removing from a cmap under a live projection does not throw"
    <| fun _ ->
      let entities = CMap.ofSeq [ 1, "a"; 2, "b"; 3, "c" ]

      // A live projection over the map, forced by the frame builder every step.
      let names = entities |> AMap.map(fun _k v -> v)

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> (AMap.getValue names).Count))
          (fun ctx _gameTime ->
            ctx.Intents.post(fun () ->
              // Handler 1: enumerates the collection to completion.
              let mutable seen = ""

              for KeyValue(_, v) in AMap.getValue entities do
                seen <- seen + v

              // Handler 2 (chained from handler 1): mutates the collection.
              // The post drain runs thunks strictly sequentially, so the
              // enumeration above finished before this removal — no
              // mid-enumeration mutation.
              ctx.Intents.post(fun () ->
                CMap.remove 1 entities
                CMap.remove 2 entities)))

      use runner = new AdaptiveHeadless<int>(program)
      let frame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal
        frame
        1
        "The force should see the settled map (one entry left)"

    testCase "postTask completion reaches a later frame through RunAsync"
    <| fun _ ->
      let mutable root = Unchecked.defaultof<cval<int>>
      let mutable started = false
      let mutable onDoneThreadId = 0
      let testThreadId = Environment.CurrentManagedThreadId

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            let value = CVal.create 0
            root <- value

            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value))
          (fun ctx _gameTime ->
            if not started then
              started <- true

              ctx.Intents.postTask(
                (fun () -> Task.FromResult(42)),
                (fun v ->
                  onDoneThreadId <- Environment.CurrentManagedThreadId
                  root.Set v),
                (fun _ -> ())
              ))

      use runner = new AdaptiveHeadless<int>(program)
      use cts = new CancellationTokenSource()
      let mutable seen = 0

      let work = asyncEx {
        let! token = Async.CancellationToken

        for outcome in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          // The completion's write lands at a post drain; stop once it
          // reaches a frame — bounded by the timeout if it never does.
          if outcome.Frame = 42 then
            seen <- outcome.Frame
            cts.Cancel()
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the game thread never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.equal
        seen
        42
        "The completion's root write should reach a later frame"

      Expect.notEqual
        onDoneThreadId
        testThreadId
        "The completion should run on the game thread, not the test thread"

    testCase "postTask error path delivers the exception through RunAsync"
    <| fun _ ->
      let mutable onErrorRan = false
      let mutable onDoneRan = false
      let mutable receivedMessage = ""
      let mutable started = false

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx -> AdaptiveInit.ofFrameBuilder(fun () -> 0.0f))
          (fun ctx _gameTime ->
            if not started then
              started <- true

              ctx.Intents.postTask(
                (fun () -> Task.FromException<int>(exn "boom")),
                (fun _ -> onDoneRan <- true),
                (fun ex ->
                  onErrorRan <- true
                  receivedMessage <- ex.Message)
              ))

      use runner = new AdaptiveHeadless<float32>(program)
      use cts = new CancellationTokenSource()

      let work = asyncEx {
        let! token = Async.CancellationToken

        for _ in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          // Keep consuming outcomes until the error handler runs —
          // bounded by the timeout if it never does.
          if onErrorRan then
            cts.Cancel()
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the game thread never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.isTrue onErrorRan "The error handler should have run"

      // The handler runs on the game thread (posted intent); the message
      // arrives unwrapped through the asyncEx/Async pipeline.
      Expect.stringContains
        receivedMessage
        "boom"
        "The error handler should receive the exception"

      Expect.isFalse onDoneRan "The done handler must not run on failure"

    testCase
      "RunAsync yields outcomes; postTask completions re-enter via the intent queue"
    <| fun _ ->
      let struct (program, _) = mkRunAsyncWorkProgram()
      use runner = new AdaptiveHeadless<int>(program)
      use cts = new CancellationTokenSource()

      let outcomes = ResizeArray<StepOutcome<int>>()

      let work = asyncEx {
        let! token = Async.CancellationToken

        for outcome in runner.RunAsync(TimeSpan.FromMilliseconds(16), token) do
          outcomes.Add outcome

          // The completion's write lands at a post drain: stop once it
          // shows up in a frame. No fixed frame budget — the timeout
          // bounds the wait, so a slow completion still passes and a
          // lost one fails loudly instead of flaking on timing.
          if outcome.Frame = 99 then
            cts.Cancel()
      }

      // The bound: the token fires on both exits — the in-loop stop and this
      // timeout — so the game thread never outlives the test.
      cts.CancelAfter 10_000

      try
        Async.RunSynchronously(work, cancellationToken = cts.Token)
      with :? OperationCanceledException ->
        ()

      Expect.equal
        outcomes[0].Frame
        0
        "The first outcome predates the async completion"

      Expect.isTrue
        (outcomes |> Seq.exists(fun outcome -> outcome.Frame = 99))
        "The async completion's root write should appear in a later outcome"

    testCase "FixedStep settles after each sub-step's Update"
    <| fun _ ->
      let counter = CVal.create 0
      let trace = ResizeArray<string>()
      let mutable posted = false

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue counter))
          (fun ctx _gameTime ->
            trace.Add "update"
            counter.Set(AVal.getValue counter + 1)

            if not posted then
              posted <- true

              // Posted from sub-step 1: the post drain runs after each
              // sub-step's Update, so this applies BEFORE sub-step 2's Update
              // reads the counter.
              ctx.Intents.post(fun () ->
                trace.Add "post"
                counter.Set(AVal.getValue counter * 10)))
        |> AdaptiveProgram.withFixedStep {
          StepSeconds = 0.01f
          MaxStepsPerFrame = 5
          MaxFrameSeconds = ValueNone
        }

      use runner = new AdaptiveHeadless<int>(program)
      // One 50ms frame at a 10ms step → 5 sub-steps; the frame is forced once.
      runner.Step(TimeSpan.FromMilliseconds 50) |> ignore

      // Sub-step 1: update (0 → 1), post (1 → 10). Sub-steps 2-5: update
      // only (10 → 14).
      Expect.equal
        (List.ofSeq trace)
        [ "update"; "post"; "update"; "update"; "update"; "update" ]
        "The post drain runs after sub-step 1's Update, before sub-step 2's"

      Expect.equal runner.Frame 14 "Sub-step 2 must read the settled counter"

    testCase
      "Subscriptions attach once, survive recomputes by key, and detach when the key leaves"
    <| fun _ ->
      let driver = CVal.create 0
      let mutable attaches = 0
      let mutable disposes = 0

      let sub = {
        Id = SubId.ofString "test/sub"
        Attach =
          fun _post ->
            attaches <- attaches + 1

            { new IDisposable with
                member _.Dispose() = disposes <- disposes + 1
            }
      }

      let subMap =
        driver
        |> CVal.value
        |> AVal.map(fun n ->
          if n >= 0 then
            Map.ofList [ SubId.ofString "test/sub", sub ] |> Map.toSeq
          else
            Seq.empty<SubId * AdaptiveSub>)
        |> AMap.ofAVal

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> 0.0f)
            |> AdaptiveInit.withSubscriptions(fun _ctx -> subMap))
          (fun _ctx _gameTime -> ())

      use runner = new AdaptiveHeadless<float32>(program)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal attaches 1 "The first step should attach the subscription"
      Expect.equal disposes 0 "Nothing should be detached yet"

      // The map recomputes twice with fresh closure values; the key survives,
      // so the runtime must keep the attachment (identity is the key).
      runner.Post(fun () -> driver.Set 1)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      runner.Post(fun () -> driver.Set 2)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal attaches 1 "A surviving key must not re-attach on recompute"
      Expect.equal disposes 0 "A surviving key must not be detached"

      // The key leaves the map: dispose. The post lands in the post drain
      // after this step's Update; the diff at the NEXT step's boundary sees
      // the vanished key, so the detach needs one extra step.
      runner.Post(fun () -> driver.Set -1)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal disposes 1 "A vanished key should be detached"

      // The key returns: attach fresh (same post drain → boundary-diff lag).
      runner.Post(fun () -> driver.Set 3)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal attaches 2 "A returning key should attach again"

    testCase
      "Subscription events post work handled at the next boundary, before Update"
    <| fun _ ->
      let value = CVal.create 0
      let trace = ResizeArray<string>()
      let mutable capturedPost = Unchecked.defaultof<SubPosting>
      let mutable postThreadId = 0
      let testThreadId = Environment.CurrentManagedThreadId

      let sub = {
        Id = SubId.ofString "test/event"
        Attach =
          fun posting ->
            capturedPost <- posting

            { new IDisposable with
                member _.Dispose() = ()
            }
      }

      let subMap =
        AVal.constant(
          Map.ofList [ SubId.ofString "test/event", sub ] |> Map.toSeq
        )
        |> AMap.ofAVal

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> AVal.getValue value)
            |> AdaptiveInit.withSubscriptions(fun _ctx -> subMap))
          (fun _ctx _gameTime -> trace.Add "update")

      use runner = new AdaptiveHeadless<int>(program)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      trace.Clear()

      // A subscription event: the captured callback posts work for the
      // pre-step drain of the next step.
      capturedPost.Post(fun () ->
        postThreadId <- Environment.CurrentManagedThreadId
        trace.Add "event"
        value.Set 5)

      Expect.equal runner.Frame 0 "The event must not run synchronously"
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal
        (List.ofSeq trace)
        [ "event"; "update" ]
        "The event should be handled before Update"

      Expect.equal
        runner.Frame
        5
        "The posted event should apply before the step's force"

      Expect.equal
        postThreadId
        testThreadId
        "The posted work should run on the owner thread"

    testCase
      "AdaptiveInput.subscribe reads each edge once and clears before the force"
    <| fun _ ->
      let actions: cval<ActionState<string>> = CVal.create ActionState.empty
      let mutable edgeReads = 0
      let mutable forceSawStarted = false
      let mutable fire: unit -> unit = fun () -> ()

      // A delta attacher that hands out one fake input event on demand.
      let attachDeltas(onState: ActionState<string> -> unit) =
        fire <-
          fun () ->
            onState {
              ActionState.empty with
                  Started = Set.singleton "jump"
                  Held = Set.singleton "jump"
            }

        { new IDisposable with
            member _.Dispose() = ()
        }

      let sub = AdaptiveInput.subscribe attachDeltas actions
      let subMap = AMap.ofList [ sub.Id, sub ]

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () ->
              forceSawStarted <- not(actions.GetValue().Started.IsEmpty)
              0)
            |> AdaptiveInit.withSubscriptions(fun _ctx -> subMap))
          (fun _ctx _gameTime ->
            if not(actions.GetValue().Started.IsEmpty) then
              edgeReads <- edgeReads + 1)

      use runner = new AdaptiveHeadless<int>(program)

      // Step 1 attaches the subscription and captures the emitter.
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal edgeReads 0 "No event has fired yet"

      fire()
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal edgeReads 1 "Update must read the edge exactly once"

      Expect.isFalse forceSawStarted "The clear must run before the frame force"

      // A step with no event: the edge must not fire again.
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal edgeReads 1 "The cleared edge must not fire again"

      Expect.isTrue
        (actions.GetValue().Started.IsEmpty)
        "The root must be cleared after the step"

    testCase
      "AdaptiveInput.subscribe reads edges once per step, even under fixed step"
    <| fun _ ->
      let actions: cval<ActionState<string>> = CVal.create ActionState.empty
      let mutable edgeReads = 0
      let mutable fire: unit -> unit = fun () -> ()

      // A delta attacher that hands out one fake input event on demand.
      let attachDeltas(onState: ActionState<string> -> unit) =
        fire <-
          fun () ->
            onState {
              ActionState.empty with
                  Started = Set.singleton "jump"
                  Held = Set.singleton "jump"
            }

        { new IDisposable with
            member _.Dispose() = ()
        }

      let sub = AdaptiveInput.subscribe attachDeltas actions
      let subMap = AMap.ofList [ sub.Id, sub ]

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> 0)
            |> AdaptiveInit.withSubscriptions(fun _ctx -> subMap))
          (fun _ctx _gameTime ->
            if not(actions.GetValue().Started.IsEmpty) then
              edgeReads <- edgeReads + 1)
        |> AdaptiveProgram.withFixedStep {
          StepSeconds = 0.01f
          MaxStepsPerFrame = 5
          MaxFrameSeconds = ValueNone
        }

      use runner = new AdaptiveHeadless<int>(program)

      // Step 1 attaches the subscription and captures the emitter.
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
      fire()

      // One 50ms frame at a 10ms step: 5 sub-steps, one force.
      runner.Step(TimeSpan.FromMilliseconds 50) |> ignore

      Expect.equal
        edgeReads
        1
        "The edge must be read once, not once per sub-step"

      Expect.isTrue
        (actions.GetValue().Started.IsEmpty)
        "The root must be cleared after the step"

    testCase "AdaptiveInput.subscribe keeps edges across a zero-sub-step frame"
    <| fun _ ->
      let actions: cval<ActionState<string>> = CVal.create ActionState.empty
      let mutable edgeReads = 0
      let mutable fire: unit -> unit = fun () -> ()

      let attachDeltas(onState: ActionState<string> -> unit) =
        fire <-
          fun () ->
            onState {
              ActionState.empty with
                  Started = Set.singleton "jump"
                  Held = Set.singleton "jump"
            }

        { new IDisposable with
            member _.Dispose() = ()
        }

      let sub = AdaptiveInput.subscribe attachDeltas actions
      let subMap = AMap.ofList [ sub.Id, sub ]

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> 0)
            |> AdaptiveInit.withSubscriptions(fun _ctx -> subMap))
          (fun _ctx _gameTime ->
            if not(actions.GetValue().Started.IsEmpty) then
              edgeReads <- edgeReads + 1)
        |> AdaptiveProgram.withFixedStep {
          StepSeconds = 0.01f
          MaxStepsPerFrame = 5
          MaxFrameSeconds = ValueNone
        }

      use runner = new AdaptiveHeadless<int>(program)

      // Step 1 (10ms = one sub-step, empty accumulator) attaches the
      // subscription and captures the emitter.
      runner.Step(TimeSpan.FromMilliseconds 10) |> ignore
      fire()

      // A 5ms frame at a 10ms step converts to zero sub-steps: no Update
      // runs, the queued clear stays queued, and the edges survive for the
      // next sub-step's Update.
      runner.Step(TimeSpan.FromMilliseconds 5) |> ignore

      Expect.equal edgeReads 0 "A zero-sub-step frame runs no Update"

      // 21ms accumulated: two sub-steps. The first reads the edges, the
      // clear drains after it, the second reads a clean root.
      runner.Step(TimeSpan.FromMilliseconds 16) |> ignore

      Expect.equal
        edgeReads
        1
        "The next sub-step's Update must read the surviving edges"

    testCase "AdaptiveInput.subscribe clears again on every later frame"
    <| fun _ ->
      let actions: cval<ActionState<string>> = CVal.create ActionState.empty
      let mutable edgeReads = 0
      let mutable fire: unit -> unit = fun () -> ()

      let attachDeltas(onState: ActionState<string> -> unit) =
        fire <-
          fun () ->
            onState {
              ActionState.empty with
                  Started = Set.singleton "jump"
                  Held = Set.singleton "jump"
            }

        { new IDisposable with
            member _.Dispose() = ()
        }

      let sub = AdaptiveInput.subscribe attachDeltas actions
      let subMap = AMap.ofList [ sub.Id, sub ]

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> 0)
            |> AdaptiveInit.withSubscriptions(fun _ctx -> subMap))
          (fun _ctx _gameTime ->
            if not(actions.GetValue().Started.IsEmpty) then
              edgeReads <- edgeReads + 1)

      use runner = new AdaptiveHeadless<int>(program)

      // Step 1 attaches the subscription and captures the emitter.
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      // Three events in the first frame, two in the second: each frame's
      // Update reads the merged edges exactly once, and the clear resets
      // between frames.
      fire()
      fire()
      fire()
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal edgeReads 1 "Three events in one frame are read once"

      fire()
      fire()
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal edgeReads 2 "A later frame's edges are read once more"

    testCase "Dispose detaches all subscriptions"
    <| fun _ ->
      let mutable attaches = 0
      let mutable disposes = 0

      let sub = {
        Id = SubId.ofString "test/sub"
        Attach =
          fun _post ->
            attaches <- attaches + 1

            { new IDisposable with
                member _.Dispose() = disposes <- disposes + 1
            }
      }

      let subMap =
        AVal.constant(
          Map.ofList [ SubId.ofString "test/sub", sub ] |> Map.toSeq
        )
        |> AMap.ofAVal

      let program =
        AdaptiveProgram.mkProgram
          (fun _ctx ->
            AdaptiveInit.ofFrameBuilder(fun () -> 0.0f)
            |> AdaptiveInit.withSubscriptions(fun _ctx -> subMap))
          (fun _ctx _gameTime -> ())

      do
        use runner = new AdaptiveHeadless<float32>(program)
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore
        Expect.equal attaches 1 "The first step should attach the subscription"

        Expect.equal
          disposes
          0
          "An attached subscription must not be disposed early"

      Expect.equal disposes 1 "Dispose should detach all subscriptions"
  ]
