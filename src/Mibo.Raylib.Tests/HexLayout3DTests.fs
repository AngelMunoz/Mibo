module Mibo.Raylib.Tests.HexLayout3D

open Expecto
open System.Numerics
open Mibo.Layout
open Mibo.Layout3D

[<Tests>]
let tests =
  testList "HexLayout3D" [
    testList "DSL" [
      testCase "run applies function to grid"
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

        let result =
          grid |> HexLayout3D.run(fun s -> s |> HexLayout3D.set 5 5 5 42)

        Expect.equal
          (HexGrid3D.get 5 5 5 result)
          (ValueSome 42)
          "Should have value"

      testCase "section creates subsection"
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

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s
            |> HexLayout3D.section 2 3 4 (fun s ->
              s |> HexLayout3D.set 0 0 0 99))

        Expect.equal
          (HexGrid3D.get 2 3 4 result)
          (ValueSome 99)
          "Should set at offset"

      testCase "padding creates inner section"
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

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s |> HexLayout3D.padding 2 (fun s -> s |> HexLayout3D.set 0 0 0 77))

        Expect.equal
          (HexGrid3D.get 2 2 2 result)
          (ValueSome 77)
          "Should set at padded offset"

      testCase "center creates centered section"
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

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s
            |> HexLayout3D.center 4 4 4 (fun s ->
              s |> HexLayout3D.set 0 0 0 55))

        Expect.equal
          (HexGrid3D.get 3 3 3 result)
          (ValueSome 55)
          "Should set at centered offset"
    ]

    testList "Geometry" [
      testCase "fill fills volume"
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

        let result =
          grid |> HexLayout3D.run(fun s -> s |> HexLayout3D.fill 1 1 1 3 3 3 42)

        let mutable count = 0

        for col in 1..3 do
          for row in 1..3 do
            for layer in 1..3 do
              match HexGrid3D.get col row layer result with
              | ValueSome v when v = 42 -> count <- count + 1
              | _ -> ()

        Expect.equal count 27 "Should fill 27 cells"

      testCase "clear clears volume"
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

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s
            |> HexLayout3D.fill 0 0 0 5 5 5 42
            |> HexLayout3D.clear 1 1 1 3 3 3)

        let mutable filledCount = 0
        let mutable emptyCount = 0

        for col in 0..4 do
          for row in 0..4 do
            for layer in 0..4 do
              match HexGrid3D.get col row layer result with
              | ValueSome _ -> filledCount <- filledCount + 1
              | ValueNone -> emptyCount <- emptyCount + 1

        Expect.equal filledCount (125 - 27) "Should have 98 filled"
        Expect.equal emptyCount 27 "Should have 27 empty"

      testCase "floorHex fills layer"
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

        let result =
          grid
          |> HexLayout3D.run(fun s -> s |> HexLayout3D.floorHex 0 0 3 5 5 42)

        let mutable count = 0

        for col in 0..4 do
          for row in 0..4 do
            match HexGrid3D.get col row 3 result with
            | ValueSome v when v = 42 -> count <- count + 1
            | _ -> ()

        Expect.equal count 25 "Should fill 25 cells in layer"

      testCase "line draws 3D line"
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

        let result =
          grid |> HexLayout3D.run(fun s -> s |> HexLayout3D.line 0 0 0 4 4 4 1)

        let mutable count = 0

        for i in 0..4 do
          match HexGrid3D.get i i i result with
          | ValueSome v when v = 1 -> count <- count + 1
          | _ -> ()

        Expect.equal count 5 "Should have 5 cells in diagonal"
    ]

    testList "Procedural" [
      testCase "generate fills with generator"
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

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s |> HexLayout3D.generate 0 0 0 3 3 3 (fun c r l -> c + r + l))

        Expect.equal (HexGrid3D.get 0 0 0 result) (ValueSome 0) "0+0+0"

        Expect.equal (HexGrid3D.get 1 1 1 result) (ValueSome 3) "1+1+1"

        Expect.equal (HexGrid3D.get 2 2 2 result) (ValueSome 6) "2+2+2"

      testCase "iter visits cells"
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

        let result =
          grid |> HexLayout3D.run(fun s -> s |> HexLayout3D.fill 0 0 0 3 3 3 42)

        let visited = System.Collections.Generic.List<int * int * int>()

        result
        |> HexLayout3D.run(fun s ->
          s
          |> HexLayout3D.iter 0 0 0 3 3 3 (fun c r l _ -> visited.Add(c, r, l)))

        Expect.equal visited.Count 27 "Should visit 27 cells"
    ]

    testList "Transformation" [
      testCase "replace replaces content"
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

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s |> HexLayout3D.fill 0 0 0 5 5 5 42 |> HexLayout3D.replace 42 99)

        for col in 0..4 do
          for row in 0..4 do
            for layer in 0..4 do
              Expect.equal
                (HexGrid3D.get col row layer result)
                (ValueSome 99)
                "Should be replaced"

      testCase "map transforms content"
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

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s
            |> HexLayout3D.fill 0 0 0 3 3 3 10
            |> HexLayout3D.map 0 0 0 3 3 3 (fun v -> v * 2))

        Expect.equal
          (HexGrid3D.get 0 0 0 result)
          (ValueSome 20)
          "Should be doubled"
    ]

    testList "Flow" [
      testCase "flowX arranges stamps along X"
      <| fun _ ->
        let grid =
          HexGrid3D.create
            20
            10
            10
            32f
            16f
            Vector3.Zero
            HexOrientation.PointyTop

        let result =
          grid
          |> HexLayout3D.run(fun s ->
            s
            |> HexLayout3D.flowX 5 [
              fun s -> s |> HexLayout3D.set 0 0 0 1
              fun s -> s |> HexLayout3D.set 0 0 0 2
              fun s -> s |> HexLayout3D.set 0 0 0 3
            ])

        Expect.equal (HexGrid3D.get 0 0 0 result) (ValueSome 1) "First stamp"

        Expect.equal (HexGrid3D.get 5 0 0 result) (ValueSome 2) "Second stamp"

        Expect.equal (HexGrid3D.get 10 0 0 result) (ValueSome 3) "Third stamp"
    ]
  ]
