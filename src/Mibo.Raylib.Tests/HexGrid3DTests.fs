module Mibo.Raylib.Tests.HexGrid3D

open Expecto
open System.Numerics
open Mibo.Layout
open Mibo.Layout3D

[<Tests>]
let tests =
  testList "HexGrid3D" [
    testList "CellGrid" [
      testCase "create initializes with ValueNone cells"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 5 8 32f 16f Vector3.Zero HexOrientation.PointyTop

        Expect.equal grid.Width 10 "Width should be 10"
        Expect.equal grid.Height 5 "Height should be 5"
        Expect.equal grid.Depth 8 "Depth should be 8"
        Expect.equal grid.HexSize 32f "HexSize should be 32"
        Expect.equal grid.LayerHeight 16f "LayerHeight should be 16"
        Expect.equal grid.Orientation HexOrientation.PointyTop "Orientation"

        for col in 0..9 do
          for row in 0..7 do
            for layer in 0..4 do
              Expect.equal
                (HexGrid3D.get col row layer grid)
                ValueNone
                "Cell should be empty"

      testCase "set and get roundtrip"
      <| fun _ ->
        let grid =
          HexGrid3D.create
            10
            10
            10
            32f
            16f
            Vector3.Zero
            HexOrientation.PointyTop

        HexGrid3D.set 5 3 7 42 grid

        Expect.equal
          (HexGrid3D.get 5 3 7 grid)
          (ValueSome 42)
          "Should get set value"

        Expect.equal
          (HexGrid3D.get 0 0 0 grid)
          ValueNone
          "Unset cell should be empty"

      testCase "set out of bounds is ignored"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 16f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set -1 0 0 1 grid
        HexGrid3D.set 0 -1 0 1 grid
        HexGrid3D.set 0 0 -1 1 grid
        HexGrid3D.set 5 0 0 1 grid
        HexGrid3D.set 0 5 0 1 grid
        HexGrid3D.set 0 0 5 1 grid

        Expect.equal
          (HexGrid3D.get -1 0 0 grid)
          ValueNone
          "Out of bounds should be None"

        Expect.equal
          (HexGrid3D.get 5 0 0 grid)
          ValueNone
          "Out of bounds should be None"

      testCase "clear sets cell to ValueNone"
      <| fun _ ->
        let grid =
          HexGrid3D.create
            10
            10
            10
            32f
            16f
            Vector3.Zero
            HexOrientation.PointyTop

        HexGrid3D.set 3 4 5 99 grid
        Expect.equal (HexGrid3D.get 3 4 5 grid) (ValueSome 99) "Should be set"

        HexGrid3D.clear 3 4 5 grid

        Expect.equal (HexGrid3D.get 3 4 5 grid) ValueNone "Should be cleared"
    ]

    testList "getWorldPos" [
      testCase "PointyTop origin cell"
      <| fun _ ->
        let grid =
          HexGrid3D.create
            10
            10
            10
            32f
            16f
            Vector3.Zero
            HexOrientation.PointyTop

        let pos = HexGrid3D.getWorldPos 0 0 0 grid
        let hexW = 32f * sqrt 3f
        let hexH = 32f * 2f

        Expect.floatClose
          Accuracy.medium
          (float pos.X)
          (float(hexW / 2f))
          "X should be hexW/2"

        Expect.floatClose Accuracy.medium (float pos.Y) 0.0 "Y should be 0"

        Expect.floatClose
          Accuracy.medium
          (float pos.Z)
          (float(hexH / 2f))
          "Z should be hexH/2"

      testCase "FlatTop origin cell"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 16f Vector3.Zero HexOrientation.FlatTop

        let pos = HexGrid3D.getWorldPos 0 0 0 grid
        let hexW = 32f * 2f
        let hexH = 32f * sqrt 3f

        Expect.floatClose
          Accuracy.medium
          (float pos.X)
          (float(hexW / 2f))
          "X should be hexW/2"

        Expect.floatClose Accuracy.medium (float pos.Y) 0.0 "Y should be 0"

        Expect.floatClose
          Accuracy.medium
          (float pos.Z)
          (float(hexH / 2f))
          "Z should be hexH/2"

      testCase "PointyTop with offset"
      <| fun _ ->
        let origin = Vector3(100f, 50f, 200f)

        let grid =
          HexGrid3D.create 10 10 10 32f 16f origin HexOrientation.PointyTop

        let pos = HexGrid3D.getWorldPos 0 0 0 grid

        Expect.floatClose
          Accuracy.medium
          (float pos.X)
          (float(100f + 32f * sqrt 3f / 2f))
          "X with origin"

        Expect.floatClose Accuracy.medium (float pos.Y) 50.0 "Y with origin"

        Expect.floatClose
          Accuracy.medium
          (float pos.Z)
          (float(200f + 32f * 2f / 2f))
          "Z with origin"

      testCase "Layer height affects Y"
      <| fun _ ->
        let grid =
          HexGrid3D.create
            10
            10
            10
            32f
            16f
            Vector3.Zero
            HexOrientation.PointyTop

        let pos0 = HexGrid3D.getWorldPos 0 0 0 grid
        let pos1 = HexGrid3D.getWorldPos 0 0 1 grid
        let pos2 = HexGrid3D.getWorldPos 0 0 2 grid

        Expect.floatClose
          Accuracy.medium
          (float(pos1.Y - pos0.Y))
          16.0
          "Layer 1 Y diff"

        Expect.floatClose
          Accuracy.medium
          (float(pos2.Y - pos0.Y))
          32.0
          "Layer 2 Y diff"
    ]

    testList "iter" [
      testCase "iter visits all occupied cells"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 16f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 1 2 3 10 grid
        HexGrid3D.set 4 0 2 20 grid

        let visited = System.Collections.Generic.List<int * int * int * int>()

        grid
        |> HexGrid3D.iter(fun col row layer content ->
          visited.Add(col, row, layer, content))

        Expect.equal visited.Count 2 "Should visit 2 cells"
        Expect.contains visited (1, 2, 3, 10) "Should contain cell 1"
        Expect.contains visited (4, 0, 2, 20) "Should contain cell 2"
    ]
  ]
