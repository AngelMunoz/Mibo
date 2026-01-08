module Mibo.Tests.Assets

open Expecto
open System
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Content
open Mibo.Elmish

type MockDisposable() =
  let mutable disposed = false
  member _.IsDisposed = disposed

  interface IDisposable with
    member _.Dispose() = disposed <- true

[<Tests>]
let tests =
  testList "Assets" [
    // Note: We can't easily test ContentManager loading without a real graphics device/service provider,
    // but we can test the generic asset cache and disposal logic which is custom in Mibo.

    testCase "GetOrCreate only calls factory once"
    <| fun _ ->
      // Since create requires GraphicsDevice and ContentManager, we can pass null for tests
      // if we only hit the user cache paths (GetOrCreate).
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

    testCase "Dispose cleans up IDisposable user assets"
    <| fun _ ->
      let assets = AssetsService.create null null
      let mock = new MockDisposable()

      assets.GetOrCreate "disposable" (fun _ -> mock) |> ignore
      Expect.isFalse mock.IsDisposed "Should not be disposed yet"

      assets.Dispose()
      Expect.isTrue mock.IsDisposed "Should be disposed after assets.Dispose()"

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
