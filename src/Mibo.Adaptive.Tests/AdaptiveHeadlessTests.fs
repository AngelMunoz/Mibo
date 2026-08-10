module Mibo.Adaptive.Tests.AdaptiveHeadless

open System
open System.Threading
open Expecto
open AdaptiveSlop.Core
open Mibo.Adaptive
open Mibo.Elmish

[<Struct>]
type TestFrame = { Position: float32; Velocity: float32 }

/// Builds a world with two independent roots and two counted projections.
/// Returns the world, the roots, and the recompute counters.
let mkTestWorld() =
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

  let world =
    AdaptiveWorld.mk(fun _ctx -> {
      FrameBuilder =
        (fun () -> {
          Position = AVal.getValue posProj
          Velocity = AVal.getValue velProj
        })
      Disposables = []
    })

  struct (world, pos, vel, (fun () -> posRecomputes), (fun () -> velRecomputes))

/// A world whose frame is the time root's total time in seconds.
let mkTimeWorld() =
  AdaptiveWorld.mk(fun ctx ->
    let totalSeconds =
      CVal.value ctx.Time |> AVal.map(fun gt -> gt.TotalTime.TotalSeconds)

    {
      FrameBuilder = (fun () -> AVal.getValue totalSeconds)
      Disposables = []
    })

[<Tests>]
let adaptiveHeadlessTests =
  testList "AdaptiveHeadless" [
    testCase "Step returns the forced frame with current root values"
    <| fun _ ->
      let struct (world, pos, _vel, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

      pos.Set(42.0f)
      let frame = runner.Step(TimeSpan.FromMilliseconds(16))

      Expect.equal frame.Position 42.0f "Frame should reflect the written root"
      Expect.equal frame.Velocity 1.0f "Unchanged root should keep its value"

    testCase "Many writes between steps settle to one recompute per force"
    <| fun _ ->
      let struct (world, pos, _vel, posRecomputes, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

      // First step initializes the world (Init + first force).
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
      let struct (world, pos, _vel, posRecomputes, velRecomputes) =
        mkTestWorld()

      use runner = new AdaptiveHeadless<TestFrame>(world)

      // First step initializes the world (Init + first force).
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
      let struct (world, _pos, _vel, posRecomputes, velRecomputes) =
        mkTestWorld()

      use runner = new AdaptiveHeadless<TestFrame>(world)

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
      let struct (world, _pos, _vel, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

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
      use runner = new AdaptiveHeadless<float>(mkTimeWorld())

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
      use runner = new AdaptiveHeadless<float>(mkTimeWorld())

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

      let world =
        AdaptiveWorld.mk(fun ctx -> {
          FrameBuilder =
            (fun () ->
              let gt = AVal.getValue ctx.Time
              gt.ElapsedGameTime.TotalMilliseconds)
          Disposables = []
        })
        |> AdaptiveWorld.withUpdate(fun ctx gameTime ->
          updates <- updates + 1
          lastElapsed <- gameTime.ElapsedGameTime

          let gt = AVal.getValue ctx.Time
          observedFromTimeRoot <- gt.ElapsedGameTime)

      use runner = new AdaptiveHeadless<float>(world)

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

      let world =
        AdaptiveWorld.mk(fun ctx ->
          exitCell <- ctx.ExitRequested

          {
            FrameBuilder = (fun () -> AVal.getValue pos)
            Disposables = []
          })

      use runner = new AdaptiveHeadless<float32>(world)

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
      let struct (world, pos, _vel, _, _) = mkTestWorld()
      let order = ResizeArray<int>()

      let world =
        world
        |> AdaptiveWorld.withObserver(fun () ->
          AdaptiveWorld.observe(fun struct (_, _, _) -> order.Add(1)))
        |> AdaptiveWorld.withObserver(fun () ->
          AdaptiveWorld.observe(fun struct (_, _, _) -> order.Add(2)))
        |> AdaptiveWorld.withObserver(fun () ->
          AdaptiveWorld.observe(fun struct (_, _, _) -> order.Add(3)))

      use runner = new AdaptiveHeadless<TestFrame>(world)

      pos.Set(7.0f)
      runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.equal order.Count 3 "All three observers should fire once"
      Expect.equal order[0] 1 "First-registered observer fires first"
      Expect.equal order[1] 2 "Second-registered observer fires second"
      Expect.equal order[2] 3 "Third-registered observer fires third"

    testCase "Observer receives the forced frame and the frame's game time"
    <| fun _ ->
      let struct (world, pos, _vel, _, _) = mkTestWorld()
      let mutable observedFrame = Unchecked.defaultof<TestFrame>
      let mutable observedElapsed = TimeSpan.Zero

      let world =
        world
        |> AdaptiveWorld.withObserver(fun () ->
          AdaptiveWorld.observe(fun struct (_, frame, gameTime) ->
            observedFrame <- frame
            observedElapsed <- gameTime.ElapsedGameTime))

      use runner = new AdaptiveHeadless<TestFrame>(world)

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

      let struct (world, _, _, _, _) = mkTestWorld()
      let world = world |> AdaptiveWorld.withObserver factory

      do
        use runner = new AdaptiveHeadless<TestFrame>(world)
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue
        disposed
        "Observer should be disposed when runner is disposed"

    testCase "World disposables are disposed on runner dispose"
    <| fun _ ->
      let mutable disposed = false

      let world =
        AdaptiveWorld.mk(fun _ctx -> {
          FrameBuilder = (fun () -> 0.0f)
          Disposables = [
            { new IDisposable with
                member _.Dispose() = disposed <- true
            }
          ]
        })

      do
        use runner = new AdaptiveHeadless<float32>(world)
        runner.Step(TimeSpan.FromMilliseconds(16)) |> ignore

      Expect.isTrue disposed "World disposables should be disposed"

    testCase "StepN advances N frames and returns the last frame"
    <| fun _ ->
      let counter = CVal.create 0

      let world =
        AdaptiveWorld.mk(fun _ctx -> {
          FrameBuilder = (fun () -> AVal.getValue counter)
          Disposables = []
        })
        |> AdaptiveWorld.withUpdate(fun _ctx _gameTime ->
          counter.Set(AVal.getValue counter + 1))

      use runner = new AdaptiveHeadless<int>(world)

      let frame = runner.StepN(5, TimeSpan.FromMilliseconds(16))

      Expect.equal frame 5 "StepN should return the last forced frame"
      Expect.equal runner.Frame 5 "Runner frame should be the last frame"

    testCase "StepN with count 0 returns the current frame"
    <| fun _ ->
      let struct (world, pos, _vel, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

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

      let world =
        AdaptiveWorld.mk(fun _ctx -> {
          FrameBuilder = (fun () -> AVal.getValue counter)
          Disposables = []
        })
        |> AdaptiveWorld.withUpdate(fun _ctx _gameTime ->
          counter.Set(AVal.getValue counter + 1))

      use runner = new AdaptiveHeadless<int>(world)

      let met =
        runner.StepUntil((fun n -> n >= 3), TimeSpan.FromMilliseconds(16), 10)

      Expect.isTrue met "Should meet the predicate within 10 frames"
      Expect.equal runner.Frame 3 "Frame should be 3"

    testCase "StepUntil returns false when maxFrames reached"
    <| fun _ ->
      let struct (world, _, _, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

      let neverTrue _frame = false

      let met = runner.StepUntil(neverTrue, TimeSpan.FromMilliseconds(16), 5)

      Expect.isFalse met "Should return false when maxFrames reached"

    testCase "Run executes steps and yields frames"
    <| fun _ ->
      let struct (world, pos, _vel, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

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
      let struct (world, _, _, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

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
      let struct (world, _, _, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

      Expect.throwsT<ArgumentException>
        (fun () -> runner.Run(TimeSpan.Zero) |> Seq.iter ignore)
        "Should throw ArgumentException for zero interval"

    testCase "RunAsync yields frames with advancing time"
    <| fun _ ->
      let struct (world, pos, _vel, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

      pos.Set(2.0f)

      use cts = new CancellationTokenSource()

      task {
        let results = ResizeArray<struct (TestFrame * float)>()
        let enum = runner.RunAsync(TimeSpan.FromMilliseconds(16), cts.Token)
        let enumerator = enum.GetAsyncEnumerator(cts.Token)

        try
          let mutable running = true

          while running do
            try
              let! hasNext = enumerator.MoveNextAsync().AsTask()

              if hasNext then
                let struct (gt, frame) = enumerator.Current
                results.Add(struct (frame, gt.TotalTime.TotalSeconds))
              else
                running <- false
            with :? OperationCanceledException ->
              running <- false

            if results.Count >= 3 then
              cts.Cancel()
        finally
          enumerator.DisposeAsync().AsTask().Wait()

        Expect.isGreaterThanOrEqual
          results.Count
          3
          "Should yield at least 3 frames"

        for i = 0 to results.Count - 1 do
          let struct (frame, _) = results[i]
          Expect.equal frame.Position 2.0f "Each frame should reflect the root"

        let struct (_, t1) = results[0]
        let struct (_, t2) = results[1]
        Expect.isGreaterThan t2 t1 "Time should advance between frames"
      }
      |> Async.AwaitTask
      |> Async.RunSynchronously

    testCase "RunAsync with already-cancelled token yields nothing"
    <| fun _ ->
      let struct (world, _, _, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

      use cts = new CancellationTokenSource()
      cts.Cancel()

      task {
        let mutable count = 0
        let enum = runner.RunAsync(TimeSpan.FromMilliseconds(16), cts.Token)
        let enumerator = enum.GetAsyncEnumerator(cts.Token)

        try
          let mutable running = true

          while running do
            try
              let! hasNext = enumerator.MoveNextAsync().AsTask()

              if hasNext then count <- count + 1 else running <- false
            with :? OperationCanceledException ->
              running <- false
        finally
          enumerator.DisposeAsync().AsTask().Wait()

        Expect.equal count 0 "Should not yield any frames"
      }
      |> Async.AwaitTask
      |> Async.RunSynchronously

    testCase "RunAsync with zero interval throws ArgumentException"
    <| fun _ ->
      let struct (world, _, _, _, _) = mkTestWorld()
      use runner = new AdaptiveHeadless<TestFrame>(world)

      Expect.throwsT<ArgumentException>
        (fun () -> runner.RunAsync(TimeSpan.Zero) |> ignore)
        "Should throw ArgumentException for zero interval"
  ]
