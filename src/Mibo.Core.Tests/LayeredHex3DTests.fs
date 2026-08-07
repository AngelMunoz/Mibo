module Mibo.Core.Tests.LayeredHex3D

open Expecto
open System.Numerics
open Mibo.Layout
open Mibo.Layout3D

[<Tests>]
let tests =
  testList "LayeredHexGrid3D" [
    testList "PointyTop" [
      testCase "layer creates and accesses layers on demand"
      <| fun _ ->
        let layered =
          LayeredHexGrid3D.create
            10
            10
            10
            32f
            16f
            Vector3.Zero
            HexOrientation.PointyTop
          |> LayeredHexLayout3D.layer 0 (HexLayout3D.set 1 1 1 10)
          |> LayeredHexLayout3D.layer 1 (HexLayout3D.set 2 2 2 20)

        let struct (layer0, _) = LayeredHexGrid3D.getOrAddLayer 0 layered
        let struct (layer1, _) = LayeredHexGrid3D.getOrAddLayer 1 layered

        Expect.equal
          (HexGrid3D.get 1 1 1 layer0)
          (ValueSome 10)
          "Layer 0 content"

        Expect.equal
          (HexGrid3D.get 2 2 2 layer1)
          (ValueSome 20)
          "Layer 1 content"

        Expect.equal
          (HexGrid3D.get 2 2 2 layer0)
          ValueNone
          "Layer 0 doesn't have layer 1 content"

        Expect.equal
          (HexGrid3D.get 1 1 1 layer1)
          ValueNone
          "Layer 1 doesn't have layer 0 content"
    ]

    testList "FlatTop" [
      testCase "layer creates and accesses layers on demand"
      <| fun _ ->
        let layered =
          LayeredHexGrid3D.create
            10
            10
            10
            32f
            16f
            Vector3.Zero
            HexOrientation.FlatTop
          |> LayeredHexLayout3D.layer 0 (HexLayout3D.set 1 1 1 10)
          |> LayeredHexLayout3D.layer 1 (HexLayout3D.set 2 2 2 20)

        let struct (layer0, _) = LayeredHexGrid3D.getOrAddLayer 0 layered
        let struct (layer1, _) = LayeredHexGrid3D.getOrAddLayer 1 layered

        Expect.equal
          (HexGrid3D.get 1 1 1 layer0)
          (ValueSome 10)
          "Layer 0 content"

        Expect.equal
          (HexGrid3D.get 2 2 2 layer1)
          (ValueSome 20)
          "Layer 1 content"

        Expect.equal
          (HexGrid3D.get 2 2 2 layer0)
          ValueNone
          "Layer 0 doesn't have layer 1 content"

        Expect.equal
          (HexGrid3D.get 1 1 1 layer1)
          ValueNone
          "Layer 1 doesn't have layer 0 content"
    ]
  ]
