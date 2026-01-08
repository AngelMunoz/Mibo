module Mibo.Tests.Assets

open Expecto
open Mibo.Elmish

[<Tests>]
let tests =
  testList "Assets" [
    testCase "GetOrCreate only calls factory once"
    <| fun _ ->
      // Passing null for dependencies since we are only testing the generic user-cache logic path.
      let assets = AssetsService.create null null

      let mutable callCount = 0

      let factory _ =
        callCount <- callCount + 1
        "asset-value"

      let val1 = assets.GetOrCreate "test" factory
      let val2 = assets.GetOrCreate "test" factory

      Expect.equal val1 "asset-value" "Value should match"
      Expect.equal val2 "asset-value" "Value should still match"
      Expect.equal callCount 1 "Factory should only be called once"

    testCase "Clear removes assets from cache"
    <| fun _ ->
      let assets = AssetsService.create null null
      assets.GetOrCreate "item" (fun _ -> "value") |> ignore

      assets.Clear()

      let result = assets.Get<string> "item"

      match result with
      | ValueNone -> ()
      | _ -> Tests.failtest "Item should be removed after clear"
  ]
