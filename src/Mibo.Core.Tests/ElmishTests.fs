module Mibo.Core.Tests.Elmish

open Expecto
open Mibo.Elmish

[<Tests>]
let tests =
  testList "Elmish" [
    testList "Cmd" [
      testCase "Cmd.batch with empty sequence returns Empty"
      <| fun _ ->
        let cmd = Cmd.batch Seq.empty
        Expect.equal cmd Cmd.none "Batching empty sequence should be none"

      testCase "Cmd.batch with multiple non-empty commands"
      <| fun _ ->
        let eff1 = Effect(fun dispatch -> dispatch 1)
        let eff2 = Effect(fun dispatch -> dispatch 2)
        let cmd1 = Cmd.ofEffect eff1
        let cmd2 = Cmd.ofEffect eff2
        let batched = Cmd.batch [ cmd1; cmd2 ]

        match batched with
        | Batch effs ->
          Expect.equal effs.Length 2 "Should have 2 effects"
          Expect.equal effs.[0] eff1 "First effect should match"
          Expect.equal effs.[1] eff2 "Second effect should match"
        | _ -> Tests.failtest "Expected a Batch command"

      testCase "Cmd.batch flattens nested batches"
      <| fun _ ->
        let eff1 = Effect(fun dispatch -> dispatch 1)
        let eff2 = Effect(fun dispatch -> dispatch 2)
        let cmd1 = Cmd.ofEffect eff1
        let cmd2 = Cmd.ofEffect eff2
        let nested = Cmd.batch [ cmd1; Cmd.batch [ cmd2 ] ]

        match nested with
        | Batch effs ->
          Expect.equal effs.Length 2 "Should have flattened to 2 effects"
          Expect.equal effs.[0] eff1 "First effect should match"
          Expect.equal effs.[1] eff2 "Second effect should match"
        | _ -> Tests.failtest "Expected a Batch command"

      testCase "Cmd.map preserves effect behavior"
      <| fun _ ->
        let mutable result = 0
        let eff = Effect(fun dispatch -> dispatch 1)
        let cmd = Cmd.ofEffect eff
        let mapped = Cmd.map (fun x -> x + 10) cmd

        match mapped with
        | Single e ->
          e.Invoke(fun x -> result <- x)
          Expect.equal result 11 "Mapped effect should increment and add 10"
        | _ -> Tests.failtest "Expected a Single command"

      testCase "Cmd.signalExit returns Quit"
      <| fun _ ->
        let cmd = Cmd.signalExit

        match cmd with
        | Quit -> ()
        | _ -> Tests.failtest "Expected Quit command"

      testCase "Cmd.ofMsg returns Msg case"
      <| fun _ ->
        let cmd = Cmd.ofMsg 42

        match cmd with
        | Msg msg -> Expect.equal msg 42 "Msg should contain the value"
        | _ -> Tests.failtest "Expected Msg command"

      testCase "Cmd.map on Msg case is allocation-free"
      <| fun _ ->
        let cmd = Cmd.ofMsg 5
        let mapped = Cmd.map (fun x -> x * 10) cmd

        match mapped with
        | Msg msg ->
          Expect.equal msg 50 "Mapped Msg should contain transformed value"
        | _ -> Tests.failtest "Expected Msg after map"

      testCase "Cmd.map preserves Quit"
      <| fun _ ->
        let cmd = Cmd.signalExit
        let mapped = Cmd.map (fun x -> x + 1) cmd

        match mapped with
        | Quit -> ()
        | _ -> Tests.failtest "Expected Quit after map"

      testCase "Cmd.batch with signalExit returns Quit"
      <| fun _ ->
        let eff = Effect(fun dispatch -> dispatch 1)
        let batched = Cmd.batch [ Cmd.ofEffect eff; Cmd.signalExit ]

        match batched with
        | Quit -> ()
        | _ -> Tests.failtest "Expected Quit (Quit takes precedence in batch)"

      testCase "Cmd.batch with single Msg uses fast path"
      <| fun _ ->
        let batched = Cmd.batch [ Cmd.ofMsg 42 ]

        match batched with
        | Msg msg ->
          Expect.equal msg 42 "Single Msg in batch should stay as Msg"
        | _ -> Tests.failtest "Expected Msg command"

      testCase "Cmd.batch unwraps single-effect NowAndDeferNextFrame"
      <| fun _ ->
        let eff = Effect(fun dispatch -> dispatch 7)
        let cmd: Cmd<int> = NowAndDeferNextFrame([| eff |], [||])
        let batched = Cmd.batch [ cmd ]

        match batched with
        | Single e ->
          let mutable got = 0
          e.Invoke(fun v -> got <- v)
          Expect.equal got 7 "Should preserve the now-effect"
        | other -> Tests.failtestf "Expected Single after batch, got %A" other

      testCase "Cmd.deferNextFrame on Msg produces DeferNextFrame"
      <| fun _ ->
        let cmd = Cmd.ofMsg 99
        let deferred = Cmd.deferNextFrame cmd

        match deferred with
        | DeferNextFrame effs ->
          Expect.equal effs.Length 1 "Should have 1 deferred effect"
          let mutable result = 0
          effs.[0].Invoke(fun x -> result <- x)
          Expect.equal result 99 "Deferred effect should dispatch original msg"
        | _ -> Tests.failtest "Expected DeferNextFrame"
    ]

    testList "Sub" [
      testCase "Sub.batch with no subs returns NoSub"
      <| fun _ ->
        let sub = Sub.batch Seq.empty

        match sub with
        | NoSub -> ()
        | _ -> Tests.failtest "Expected NoSub"

      testCase "Sub.batch2 combines single subs into BatchSub"
      <| fun _ ->
        let sub1 =
          Active(
            SubId.ofString "1",
            fun _ ->
              { new System.IDisposable with
                  member _.Dispose() = ()
              }
          )

        let sub2 =
          Active(
            SubId.ofString "2",
            fun _ ->
              { new System.IDisposable with
                  member _.Dispose() = ()
              }
          )

        let batched = Sub.batch2(sub1, sub2)

        match batched with
        | BatchSub subs -> Expect.equal subs.Length 2 "Should have 2 subs"
        | _ -> Tests.failtest "Expected a BatchSub"

      testCase "Sub.map prefixes IDs and handles nested batches"
      <| fun _ ->
        let subA =
          Active(
            SubId.ofString "A",
            fun _ ->
              { new System.IDisposable with
                  member _.Dispose() = ()
              }
          )

        let subB =
          Active(
            SubId.ofString "B",
            fun _ ->
              { new System.IDisposable with
                  member _.Dispose() = ()
              }
          )

        let batched = Sub.batch [ subA; Sub.batch [ subB ] ]

        let mapped = Sub.map "prefix" id batched

        match mapped with
        | BatchSub subs ->
          Expect.equal subs.Length 2 "Should have 2 mapped subs"

          match subs.[0] with
          | Active(id, _) ->
            Expect.equal (SubId.value id) "prefix/A" "ID A should be prefixed"
          | _ -> Tests.failtest "Expected Active sub A"

          match subs.[1] with
          | Active(id, _) ->
            Expect.equal (SubId.value id) "prefix/B" "ID B should be prefixed"
          | _ -> Tests.failtest "Expected Active sub B"
        | _ -> Tests.failtest "Expected a BatchSub"
    ]

    testList "RenderBuffer" [
      testCase "RenderBuffer sorts by key"
      <| fun _ ->
        use buffer = new RenderBuffer<int, string>()
        buffer.Add(10, "last")
        buffer.Add(5, "first")
        buffer.Sort()

        let struct (k1, v1) = buffer.Item(0)
        let struct (k2, v2) = buffer.Item(1)

        Expect.equal k1 5 "First key should be 5"
        Expect.equal v1 "first" "First value should be 'first'"
        Expect.equal k2 10 "Second key should be 10"
        Expect.equal v2 "last" "Second value should be 'last'"
    ]

    testList "FixedStep" [
      testCase "compute runs expected number of steps and carries remainder"
      <| fun _ ->
        let step = 0.1f
        let maxSteps = 10
        let maxFrame = 1.0f

        let struct (acc2, steps, dropped) =
          FixedStep.compute step maxSteps maxFrame 0.0f 0.25f

        Expect.equal steps 2 "Expected 2 fixed steps"
        Expect.isFalse dropped "Should not drop time"
        Expect.isTrue (abs(acc2 - 0.05f) < 1e-5f) "Remainder should carry"

      testCase "compute clamps huge delta and respects max steps"
      <| fun _ ->
        let step = 0.016666667f
        let maxSteps = 5
        let maxFrame = 0.05f

        let struct (_acc2, steps, dropped) =
          FixedStep.compute step maxSteps maxFrame 0.0f 1.0f

        Expect.isTrue (steps >= 2 && steps <= 5) "Steps should be bounded"
        Expect.isFalse dropped "Should not drop if under cap"

      testCase "compute drops excess time when cap is hit"
      <| fun _ ->
        let step = 0.1f
        let maxSteps = 2
        let maxFrame = 10.0f

        let struct (acc2, steps, dropped) =
          FixedStep.compute step maxSteps maxFrame 0.0f 0.35f

        Expect.equal steps 2 "Expected capped steps"
        Expect.isTrue dropped "Expected dropped time"
        Expect.equal acc2 0.0f "Accumulator should reset on drop"

      testCase "zero dt returns 0 steps"
      <| fun _ ->
        let struct (acc, steps, dropped) =
          FixedStep.compute 0.1f 10 1.0f 0.0f 0.0f

        Expect.equal steps 0 "Zero dt should give 0 steps"

        Expect.floatClose
          Accuracy.medium
          (float acc)
          0.0
          "Accumulator should be 0"

        Expect.isFalse dropped "Should not drop"

      testCase "negative dt is clamped to 0"
      <| fun _ ->
        let struct (acc, steps, dropped) =
          FixedStep.compute 0.1f 10 1.0f 0.0f -0.5f

        Expect.equal steps 0 "Negative dt should give 0 steps"

        Expect.floatClose
          Accuracy.medium
          (float acc)
          0.0
          "Accumulator should be 0"

        Expect.isFalse dropped "Should not drop"

      testCase "dt exactly equal to step gives 1 step"
      <| fun _ ->
        let struct (acc, steps, dropped) =
          FixedStep.compute 0.1f 10 1.0f 0.0f 0.1f

        Expect.equal steps 1 "Exactly one step"
        Expect.floatClose Accuracy.medium (float acc) 0.0 "No remainder"
        Expect.isFalse dropped "Should not drop"

      testCase "dt slightly less than step gives 0 steps"
      <| fun _ ->
        let struct (acc, steps, dropped) =
          FixedStep.compute 0.1f 10 1.0f 0.0f 0.099f

        Expect.equal steps 0 "Should not reach step threshold"

        Expect.floatClose
          Accuracy.medium
          (float acc)
          0.099
          "Accumulator should hold dt"

        Expect.isFalse dropped "Should not drop"

      testCase "dt = step * N exactly gives N steps with near-zero remainder"
      <| fun _ ->
        let struct (acc, steps, dropped) =
          FixedStep.compute 0.1f 10 1.0f 0.0f 0.3f

        Expect.equal steps 3 "Should be exactly 3 steps"
        Expect.isTrue (abs acc < 0.001f) "Remainder should be near zero"
        Expect.isFalse dropped "Should not drop"

      testCase "maxSteps = 0 returns 0 steps"
      <| fun _ ->
        let struct (acc, steps, dropped) =
          FixedStep.compute 0.1f 0 1.0f 0.0f 0.5f

        Expect.equal steps 0 "maxSteps=0 should give 0 steps"
        Expect.isFalse dropped "Should not drop"

      testCase "stepSeconds = 0 returns 0 steps"
      <| fun _ ->
        let struct (acc, steps, dropped) =
          FixedStep.compute 0.0f 10 1.0f 0.0f 0.5f

        Expect.equal steps 0 "step=0 should give 0 steps"
        Expect.isFalse dropped "Should not drop"

      testCase "accumulator carries over between calls"
      <| fun _ ->
        let struct (acc1, steps1, _) = FixedStep.compute 0.1f 10 1.0f 0.0f 0.15f

        Expect.equal steps1 1 "First call: 1 step"
        Expect.floatClose Accuracy.medium (float acc1) 0.05 "Remainder 0.05"

        let struct (acc2, steps2, _) = FixedStep.compute 0.1f 10 1.0f acc1 0.06f

        Expect.equal steps2 1 "Second call: 1 step (0.05+0.06=0.11 >= 0.1)"
        Expect.floatClose Accuracy.medium (float acc2) 0.01 "Remainder 0.01"
    ]

    testList "System" [
      testCase "System pipeline preserves command order"
      <| fun _ ->
        let eff n = Effect(fun dispatch -> dispatch n)

        let systemA(m: int) =
          struct (m + 1, Cmd.batch [ Cmd.ofEffect(eff 1); Cmd.ofEffect(eff 2) ])

        let systemB(m: int) = struct (m + 1, Cmd.ofEffect(eff 3))

        let struct (_model, cmd) =
          0
          |> System.start
          |> System.pipeMutable systemA
          |> System.snapshot id
          |> System.pipe systemB
          |> System.finish id

        let results = ResizeArray<int>()
        let dispatch(x: int) = results.Add x

        match cmd with
        | Batch effs ->
          for i = 0 to effs.Length - 1 do
            effs[i].Invoke dispatch

          Expect.sequenceEqual
            results
            [ 1; 2; 3 ]
            "Expected effects to run in insertion order"
        | _ -> Tests.failtest "Expected a Batch command"
    ]
  ]
