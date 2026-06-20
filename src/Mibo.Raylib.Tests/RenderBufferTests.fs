module Mibo.Raylib.Tests.RenderBuffer

open Expecto
open Mibo.Elmish

[<Tests>]
let tests =
  testList "RenderBuffer Tests" [
    test "Sort should sort items by key" {
      use buffer = new RenderBuffer<int, string>()
      buffer.Add(2, "Second")
      buffer.Add(1, "First")
      buffer.Add(3, "Third")

      buffer.Sort()

      let struct (k1, v1) = buffer.Item 0
      let struct (k2, v2) = buffer.Item 1
      let struct (k3, v3) = buffer.Item 2

      Expect.equal k1 1 "First item key should be 1"
      Expect.equal v1 "First" "First item value should be 'First'"
      Expect.equal k2 2 "Second item key should be 2"
      Expect.equal v2 "Second" "Second item value should be 'Second'"
      Expect.equal k3 3 "Third item key should be 3"
      Expect.equal v3 "Third" "Third item value should be 'Third'"
    }
  ]
