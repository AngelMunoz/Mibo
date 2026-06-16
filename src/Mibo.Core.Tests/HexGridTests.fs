module Mibo.Core.Tests.HexGrid

open Expecto
open System.Numerics
open Mibo.Layout

[<Tests>]
let tests =
  testList "HexGrid" [
    testList "CellGrid" [
      testCase "create initializes with ValueNone cells"
      <| fun _ ->
        let grid = HexGrid.create 10 5 32f Vector2.Zero HexOrientation.PointyTop

        Expect.equal grid.Width 10 "Width should be 10"
        Expect.equal grid.Height 5 "Height should be 5"
        Expect.equal grid.Size 32f "Size should be 32"
        Expect.equal grid.Orientation HexOrientation.PointyTop "Orientation"

        for col in 0..9 do
          for row in 0..4 do
            Expect.equal
              (HexGrid.get col row grid)
              ValueNone
              "Cell should be empty"

      testCase "set and get roundtrip"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 5 3 42 grid

        Expect.equal
          (HexGrid.get 5 3 grid)
          (ValueSome 42)
          "Should get set value"

        Expect.equal
          (HexGrid.get 0 0 grid)
          ValueNone
          "Unset cell should be empty"

      testCase "set out of bounds is ignored"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set -1 0 1 grid
        HexGrid.set 0 -1 1 grid
        HexGrid.set 5 0 1 grid
        HexGrid.set 0 5 1 grid

      testCase "get out of bounds returns ValueNone"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        Expect.equal (HexGrid.get -1 0 grid) ValueNone "Negative col"
        Expect.equal (HexGrid.get 0 -1 grid) ValueNone "Negative row"
        Expect.equal (HexGrid.get 5 0 grid) ValueNone "Col at width"
        Expect.equal (HexGrid.get 0 5 grid) ValueNone "Row at height"

      testCase "clear removes cell content"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 2 2 42 grid
        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 42) "Should be set"

        HexGrid.clear 2 2 grid
        Expect.equal (HexGrid.get 2 2 grid) ValueNone "Should be cleared"

      testCase "clear out of bounds is ignored"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.clear -1 0 grid
        HexGrid.clear 0 -1 grid
        HexGrid.clear 5 0 grid
        HexGrid.clear 0 5 grid

      testCase "iter visits all populated cells"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 1 1 10 grid
        HexGrid.set 3 2 20 grid

        let visited = ResizeArray<struct (int * int * int)>()
        grid |> HexGrid.iter(fun col row v -> visited.Add(struct (col, row, v)))

        Expect.equal visited.Count 2 "Should visit 2 cells"
        Expect.contains visited (struct (1, 1, 10)) "Should contain first cell"
        Expect.contains visited (struct (3, 2, 20)) "Should contain second cell"
    ]

    testList "World Position - PointyTop" [
      testCase "getWorldPos at origin"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        let pos = HexGrid.getWorldPos 0 0 grid
        let hexW = 32f * sqrt 3f
        let hexH = 64f

        Expect.equal pos.X (hexW / 2f) "X center of first hex"
        Expect.equal pos.Y (hexH / 2f) "Y center of first hex"

      testCase "getWorldPos with offset origin"
      <| fun _ ->
        let origin = Vector2(100f, 50f)

        let grid = HexGrid.create 10 10 32f origin HexOrientation.PointyTop

        let pos = HexGrid.getWorldPos 0 0 grid
        let hexW = 32f * sqrt 3f
        let hexH = 64f

        Expect.equal pos.X (100f + hexW / 2f) "X with origin offset"
        Expect.equal pos.Y (50f + hexH / 2f) "Y with origin offset"

      testCase "getWorldPos odd row shifted right"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        let pos0 = HexGrid.getWorldPos 0 0 grid
        let pos1 = HexGrid.getWorldPos 0 1 grid
        let hexW = 32f * sqrt 3f

        Expect.equal
          pos1.X
          (pos0.X + hexW / 2f)
          "Odd row shifted right by half hex width"

      testCase "getWorldPos column spacing"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        let pos0 = HexGrid.getWorldPos 0 0 grid
        let pos1 = HexGrid.getWorldPos 1 0 grid
        let hexW = 32f * sqrt 3f

        Expect.equal pos1.X (pos0.X + hexW) "Columns spaced by hex width"

      testCase "getWorldPos row spacing"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        let pos0 = HexGrid.getWorldPos 0 0 grid
        let pos2 = HexGrid.getWorldPos 0 2 grid

        Expect.equal
          pos2.Y
          (pos0.Y + 96f)
          "Two rows apart = 2 * hexH * 0.75 = 96"
    ]

    testList "World Position - FlatTop" [
      testCase "getWorldPos at origin"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop

        let pos = HexGrid.getWorldPos 0 0 grid
        let hexW = 64f
        let hexH = 32f * sqrt 3f

        Expect.equal pos.X (hexW / 2f) "X center of first hex"
        Expect.equal pos.Y (hexH / 2f) "Y center of first hex"

      testCase "getWorldPos odd column shifted down"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop

        let pos0 = HexGrid.getWorldPos 0 0 grid
        let pos1 = HexGrid.getWorldPos 1 0 grid
        let hexH = 32f * sqrt 3f

        Expect.equal
          pos1.Y
          (pos0.Y + hexH / 2f)
          "Odd column shifted down by half hex height"

      testCase "getWorldPos column spacing"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop

        let pos0 = HexGrid.getWorldPos 0 0 grid
        let pos2 = HexGrid.getWorldPos 2 0 grid

        Expect.equal
          pos2.X
          (pos0.X + 96f)
          "Two columns apart = 2 * hexW * 0.75 = 96"

      testCase "getWorldPos row spacing"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop

        let pos0 = HexGrid.getWorldPos 0 0 grid
        let pos1 = HexGrid.getWorldPos 0 1 grid
        let hexH = 32f * sqrt 3f

        Expect.equal pos1.Y (pos0.Y + hexH) "Rows spaced by hex height"
    ]

    testList "iterVisible" [
      testCase "iterVisible visits cells in viewport"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        for col in 0..9 do
          for row in 0..9 do
            HexGrid.set col row (col * 10 + row) grid

        let visited = ResizeArray<struct (int * int * int)>()

        grid
        |> HexGrid.iterVisible 0f 0f 200f 200f (fun col row v ->
          visited.Add(struct (col, row, v)))

        Expect.isGreaterThan visited.Count 0 "Should visit some cells"
        Expect.isLessThan visited.Count 100 "Should not visit all cells"

      testCase "iterVisible visits all cells when viewport covers grid"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        for col in 0..4 do
          for row in 0..4 do
            HexGrid.set col row (col * 10 + row) grid

        let visited = ResizeArray<struct (int * int * int)>()

        grid
        |> HexGrid.iterVisible -1000f -1000f 1000f 1000f (fun col row v ->
          visited.Add(struct (col, row, v)))

        Expect.equal visited.Count 25 "Should visit all 25 cells"

      testCase "iterVisible flat-top visits cells in viewport"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        for col in 0..9 do
          for row in 0..9 do
            HexGrid.set col row (col * 10 + row) grid

        let visited = ResizeArray<struct (int * int * int)>()

        grid
        |> HexGrid.iterVisible 0f 0f 200f 200f (fun col row v ->
          visited.Add(struct (col, row, v)))

        Expect.isGreaterThan visited.Count 0 "Should visit some cells"
        Expect.isLessThan visited.Count 100 "Should not visit all cells"

      testCase "iterVisible empty viewport visits nothing"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 2 2 42 grid

        let visited = ResizeArray<struct (int * int * int)>()

        grid
        |> HexGrid.iterVisible 5000f 5000f 6000f 6000f (fun col row v ->
          visited.Add(struct (col, row, v)))

        Expect.equal visited.Count 0 "Should visit no cells"
    ]
  ]
