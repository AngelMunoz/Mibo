module Mibo.Tests.Elmish

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
        let buffer = RenderBuffer<int, string>()
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

    testList "Dispatch" [
      testCase "Immediate dispatch processes re-entrant messages in same batch"
      <| fun _ ->
        let q = DispatchQueue<int>(DispatchMode.Immediate)

        q.Dispatch 1

        let results = ResizeArray<int>()
        let mutable msg = 0

        q.StartBatch()

        while q.TryDequeue(&msg) do
          results.Add msg

          if msg = 1 then
            // Re-entrant dispatch should be eligible immediately.
            q.Dispatch 2

        q.EndBatch()

        Expect.sequenceEqual
          results
          [ 1; 2 ]
          "Expected message 2 to run in the same batch"

      testCase "FrameBounded dispatch defers re-entrant messages to next batch"
      <| fun _ ->
        let q = DispatchQueue<int>(DispatchMode.FrameBounded)

        q.Dispatch 1

        let batch1 = ResizeArray<int>()
        let mutable msg = 0

        q.StartBatch()

        while q.TryDequeue(&msg) do
          batch1.Add msg

          if msg = 1 then
            // Re-entrant dispatch should be deferred.
            q.Dispatch 2

        q.EndBatch()

        let batch2 = ResizeArray<int>()

        q.StartBatch()

        while q.TryDequeue(&msg) do
          batch2.Add msg

        q.EndBatch()

        Expect.sequenceEqual
          batch1
          [ 1 ]
          "Expected only the original message in batch 1"

        Expect.sequenceEqual batch2 [ 2 ] "Expected deferred message in batch 2"
    ]

    testList "FixedStep" [
      testCase "compute runs expected number of steps and carries remainder"
      <| fun _ ->
        let step = 0.1f
        let maxSteps = 10
        let maxFrame = 1.0f

        // dt = 0.25 -> 2 steps, remainder 0.05
        let struct (acc2, steps, dropped) =
          FixedStep.compute step maxSteps maxFrame 0.0f 0.25f

        Expect.equal steps 2 "Expected 2 fixed steps"
        Expect.isFalse dropped "Should not drop time"
        Expect.isTrue (abs(acc2 - 0.05f) < 1e-5f) "Remainder should carry"

      testCase "compute clamps huge delta and respects max steps"
      <| fun _ ->
        let step = 0.016666667f // ~1/60
        let maxSteps = 5
        let maxFrame = 0.05f

        // dt=1.0 but clamped to 0.05; 0.05 / 0.0166... -> 3 steps (approx)
        let struct (_acc2, steps, dropped) =
          FixedStep.compute step maxSteps maxFrame 0.0f 1.0f

        Expect.isTrue (steps >= 2 && steps <= 5) "Steps should be bounded"
        Expect.isFalse dropped "Should not drop if under cap"

      testCase "compute drops excess time when cap is hit"
      <| fun _ ->
        let step = 0.1f
        let maxSteps = 2
        let maxFrame = 10.0f

        // dt=0.35 -> would require 3 steps, but cap is 2 => drop
        let struct (acc2, steps, dropped) =
          FixedStep.compute step maxSteps maxFrame 0.0f 0.35f

        Expect.equal steps 2 "Expected capped steps"
        Expect.isTrue dropped "Expected dropped time"
        Expect.equal acc2 0.0f "Accumulator should reset on drop"
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
