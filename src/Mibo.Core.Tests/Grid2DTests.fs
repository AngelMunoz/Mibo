module Mibo.Core.Tests.Grid2D

open Expecto
open System.Numerics
open Mibo.Layout

/// World positions captured from the retired HexGrid record before the
/// unified grid replaced it. They pin the hex offset math exactly.
[<Tests>]
let tests =
  testList "CellGrid2D" [
    testList "createHex" [
      testCase "stores the hex bounding box and geometry"
      <| fun _ ->
        let grid: CellGrid2D<int> =
          CellGrid2D.createHex
            CellGeometry.PointyTopHex
            32f
            4
            3
            (Vector2(10f, 20f))

        Expect.equal grid.Width 4 "width"
        Expect.equal grid.Height 3 "height"
        Expect.equal grid.Geometry CellGeometry.PointyTopHex "geometry"
        Expect.equal grid.CellSize (Vector2(32f * sqrt 3f, 64f)) "bounding box"

        Expect.equal
          (CellGrid2D.hexOrientation grid)
          HexOrientation.PointyTop
          "orientation"

        Expect.equal (CellGrid2D.hexSize grid) 32f "size round trip"

        for i = 0 to grid.Cells.Length - 1 do
          Expect.equal grid.Cells.[i] ValueNone "cells start empty"

      testCase "flat top bounding box"
      <| fun _ ->
        let grid: CellGrid2D<int> =
          CellGrid2D.createHex CellGeometry.FlatTopHex 32f 4 3 Vector2.Zero

        Expect.equal grid.CellSize (Vector2(64f, 32f * sqrt 3f)) "bounding box"

        Expect.equal
          (CellGrid2D.hexOrientation grid)
          HexOrientation.FlatTop
          "orientation"

      testCase "rejects square geometry"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            CellGrid2D.createHex CellGeometry.Square 32f 4 3 Vector2.Zero
            |> ignore)
          "square is not a hex geometry"

      testCase "hex accessors reject square grids"
      <| fun _ ->
        let grid: CellGrid2D<int> =
          CellGrid2D.create 4 3 (Vector2(16f, 16f)) Vector2.Zero

        Expect.throwsT<System.ArgumentException>
          (fun () -> CellGrid2D.hexOrientation grid |> ignore)
          "no orientation on square"

        Expect.throwsT<System.ArgumentException>
          (fun () -> CellGrid2D.hexSize grid |> ignore)
          "no size on square"
    ]

    testList "getWorldPos hex goldens" [
      testCase "pointy top matches the retired HexGrid math"
      <| fun _ ->
        let grid: CellGrid2D<int> =
          CellGrid2D.createHex
            CellGeometry.PointyTopHex
            32f
            4
            3
            (Vector2(10f, 20f))

        Expect.equal
          (CellGrid2D.getWorldPos 0 0 grid)
          (Vector2(37.712814f, 52f))
          "(0,0)"

        Expect.equal
          (CellGrid2D.getWorldPos 1 0 grid)
          (Vector2(93.13844f, 52f))
          "(1,0)"

        Expect.equal
          (CellGrid2D.getWorldPos 3 0 grid)
          (Vector2(203.98969f, 52f))
          "(3,0)"

        Expect.equal
          (CellGrid2D.getWorldPos 0 1 grid)
          (Vector2(65.42563f, 100f))
          "(0,1)"

        Expect.equal
          (CellGrid2D.getWorldPos 1 1 grid)
          (Vector2(120.85126f, 100f))
          "(1,1)"

        Expect.equal
          (CellGrid2D.getWorldPos 3 2 grid)
          (Vector2(203.98969f, 148f))
          "(3,2)"

      testCase "flat top matches the retired HexGrid math"
      <| fun _ ->
        let grid: CellGrid2D<int> =
          CellGrid2D.createHex
            CellGeometry.FlatTopHex
            32f
            4
            3
            (Vector2(10f, 20f))

        Expect.equal
          (CellGrid2D.getWorldPos 0 0 grid)
          (Vector2(42f, 47.712814f))
          "(0,0)"

        Expect.equal
          (CellGrid2D.getWorldPos 1 0 grid)
          (Vector2(90f, 75.42563f))
          "(1,0)"

        Expect.equal
          (CellGrid2D.getWorldPos 3 0 grid)
          (Vector2(186f, 75.42563f))
          "(3,0)"

        Expect.equal
          (CellGrid2D.getWorldPos 0 1 grid)
          (Vector2(42f, 103.13844f))
          "(0,1)"

        Expect.equal
          (CellGrid2D.getWorldPos 1 1 grid)
          (Vector2(90f, 130.85126f))
          "(1,1)"

        Expect.equal
          (CellGrid2D.getWorldPos 3 2 grid)
          (Vector2(186f, 186.27689f))
          "(3,2)"
    ]

    testList "square geometry unchanged" [
      testCase "create defaults to square and keeps the world math"
      <| fun _ ->
        let grid: CellGrid2D<int> =
          CellGrid2D.create 4 3 (Vector2(32f, 32f)) (Vector2(10f, 20f))

        Expect.equal grid.Geometry CellGeometry.Square "default geometry"

        Expect.equal
          (CellGrid2D.getWorldPos 0 0 grid)
          (Vector2(10f, 20f))
          "(0,0)"

        Expect.equal
          (CellGrid2D.getWorldPos 2 1 grid)
          (Vector2(74f, 52f))
          "(2,1)"

      testCase "iterVisible rejects hex grids"
      <| fun _ ->
        let grid: CellGrid2D<int> =
          CellGrid2D.createHex CellGeometry.FlatTopHex 32f 4 3 Vector2.Zero

        Expect.throwsT<System.ArgumentException>
          (fun () -> CellGrid2D.iterVisible 0 0 100 100 (fun _ _ _ -> ()) grid)
          "hex grids need hex-aware culling"
    ]
  ]
