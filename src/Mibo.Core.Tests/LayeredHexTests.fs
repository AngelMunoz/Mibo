module Mibo.Core.Tests.LayeredHex

open Expecto
open System.Numerics
open Mibo.Layout

[<Tests>]
let tests =
  testList "LayeredHexGrid" [
    testList "PointyTop" [
      testCase "layer creates and accesses layers on demand"
      <| fun _ ->
        let layered =
          LayeredHexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> LayeredHexLayout.layer 0 (HexLayout.set 1 1 10)
          |> LayeredHexLayout.layer 1 (HexLayout.set 2 2 20)

        let struct (layer0, _) = LayeredHexGrid.getOrAddLayer 0 layered
        let struct (layer1, _) = LayeredHexGrid.getOrAddLayer 1 layered

        Expect.equal (HexGrid.get 1 1 layer0) (ValueSome 10) "Layer 0 content"

        Expect.equal (HexGrid.get 2 2 layer1) (ValueSome 20) "Layer 1 content"

        Expect.equal
          (HexGrid.get 2 2 layer0)
          ValueNone
          "Layer 0 doesn't have layer 1 content"

        Expect.equal
          (HexGrid.get 1 1 layer1)
          ValueNone
          "Layer 1 doesn't have layer 0 content"
    ]

    testList "FlatTop" [
      testCase "layer creates and accesses layers on demand"
      <| fun _ ->
        let layered =
          LayeredHexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> LayeredHexLayout.layer 0 (HexLayout.set 1 1 10)
          |> LayeredHexLayout.layer 1 (HexLayout.set 2 2 20)

        let struct (layer0, _) = LayeredHexGrid.getOrAddLayer 0 layered
        let struct (layer1, _) = LayeredHexGrid.getOrAddLayer 1 layered

        Expect.equal (HexGrid.get 1 1 layer0) (ValueSome 10) "Layer 0 content"

        Expect.equal (HexGrid.get 2 2 layer1) (ValueSome 20) "Layer 1 content"

        Expect.equal
          (HexGrid.get 2 2 layer0)
          ValueNone
          "Layer 0 doesn't have layer 1 content"

        Expect.equal
          (HexGrid.get 1 1 layer1)
          ValueNone
          "Layer 1 doesn't have layer 0 content"
    ]
  ]
