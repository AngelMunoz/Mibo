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
  ]
