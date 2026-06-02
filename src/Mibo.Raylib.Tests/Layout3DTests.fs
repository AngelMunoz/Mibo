module Mibo.Raylib.Tests.Layout3D

open Expecto
open System.Numerics
open Mibo.Layout3D

[<Tests>]
let tests =
  testList "Layout3D" [
    testList "CellGrid3D" [
      testCase "create initializes with ValueNone cells"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 5 8 (Vector3(32f, 32f, 32f)) Vector3.Zero

        Expect.equal grid.Width 10 "Width should be 10"
        Expect.equal grid.Height 5 "Height should be 5"
        Expect.equal grid.Depth 8 "Depth should be 8"

        Expect.equal
          grid.CellSize
          (Vector3(32f, 32f, 32f))
          "CellSize should match"

        for x in 0..9 do
          for y in 0..4 do
            for z in 0..7 do
              Expect.equal
                (CellGrid3D.get x y z grid)
                ValueNone
                "Cell should be empty"

      testCase "set and get roundtrip"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero

        CellGrid3D.set 5 3 7 42 grid

        Expect.equal
          (CellGrid3D.get 5 3 7 grid)
          (ValueSome 42)
          "Should get set value"

        Expect.equal
          (CellGrid3D.get 0 0 0 grid)
          ValueNone
          "Unset cell should be empty"

      testCase "set out of bounds is ignored"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero

        CellGrid3D.set -1 0 0 1 grid
        CellGrid3D.set 0 -1 0 1 grid
        CellGrid3D.set 0 0 -1 1 grid
        CellGrid3D.set 5 0 0 1 grid
        CellGrid3D.set 0 5 0 1 grid
        CellGrid3D.set 0 0 5 1 grid

      testCase "get out of bounds returns ValueNone"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero

        Expect.equal (CellGrid3D.get -1 0 0 grid) ValueNone "Negative X"
        Expect.equal (CellGrid3D.get 0 -1 0 grid) ValueNone "Negative Y"
        Expect.equal (CellGrid3D.get 0 0 -1 grid) ValueNone "Negative Z"
        Expect.equal (CellGrid3D.get 5 0 0 grid) ValueNone "X at width"
        Expect.equal (CellGrid3D.get 0 5 0 grid) ValueNone "Y at height"
        Expect.equal (CellGrid3D.get 0 0 5 grid) ValueNone "Z at depth"

      testCase "clear removes cell content"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero

        CellGrid3D.set 2 2 2 42 grid
        Expect.equal (CellGrid3D.get 2 2 2 grid) (ValueSome 42) "Should be set"

        CellGrid3D.clear 2 2 2 grid
        Expect.equal (CellGrid3D.get 2 2 2 grid) ValueNone "Should be cleared"

      testCase "getWorldPos calculates correct position"
      <| fun _ ->
        let grid =
          CellGrid3D.create
            10
            10
            10
            (Vector3(32f, 16f, 8f))
            (Vector3(100f, 50f, 25f))

        let pos = CellGrid3D.getWorldPos 3 2 4 grid

        Expect.equal pos.X 196f "X = 100 + 3*32"
        Expect.equal pos.Y 82f "Y = 50 + 2*16"
        Expect.equal pos.Z 57f "Z = 25 + 4*8"

      testCase "iter visits all populated cells"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
        CellGrid3D.set 1 1 1 10 grid
        CellGrid3D.set 3 2 4 20 grid

        let visited = ResizeArray<struct (int * int * int * int)>()
        grid |> CellGrid3D.iter(fun x y z v -> visited.Add(struct (x, y, z, v)))

        Expect.equal visited.Count 2 "Should visit 2 cells"

        Expect.contains
          visited
          (struct (1, 1, 1, 10))
          "Should contain first cell"

        Expect.contains
          visited
          (struct (3, 2, 4, 20))
          "Should contain second cell"

      testCase "iterVolume visits only cells in bounds"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero

        for x in 0..9 do
          for y in 0..9 do
            for z in 0..9 do
              CellGrid3D.set x y z (x * 100 + y * 10 + z) grid

        let bounds = {
          Min = Vector3(64f, 32f, 0f)
          Max = Vector3(96f, 64f, 32f)
        }

        let visited = ResizeArray<struct (int * int * int * int)>()

        grid
        |> CellGrid3D.iterVolume bounds (fun x y z v ->
          visited.Add(struct (x, y, z, v)))

        Expect.equal visited.Count 8 "Should visit 2*2*2 = 8 cells"
        Expect.contains visited (struct (2, 1, 0, 210)) "Should contain (2,1,0)"
        Expect.contains visited (struct (3, 2, 1, 321)) "Should contain (3,2,1)"
    ]

    testList "Layout3D DSL" [
      testCase "run executes layout function"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section -> section |> Layout3D.set 2 2 2 42)

        Expect.equal
          (CellGrid3D.get 2 2 2 grid)
          (ValueSome 42)
          "Should set cell"

      testCase "fill creates cuboid volume"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.fill 2 3 4 3 2 2 99)

        for x in 2..4 do
          for y in 3..4 do
            for z in 4..5 do
              Expect.equal
                (CellGrid3D.get x y z grid)
                (ValueSome 99)
                $"Cell ({x},{y},{z}) should be filled"

        Expect.equal (CellGrid3D.get 1 3 4 grid) ValueNone "Left of fill"
        Expect.equal (CellGrid3D.get 5 3 4 grid) ValueNone "Right of fill"

      testCase "section provides relative coordinates"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.section 3 4 5 (fun inner ->
              inner |> Layout3D.set 0 0 0 42))

        Expect.equal
          (CellGrid3D.get 3 4 5 grid)
          (ValueSome 42)
          "Section offset should apply"

        Expect.equal
          (CellGrid3D.get 0 0 0 grid)
          ValueNone
          "Origin should be empty"

      testCase "padding shrinks section on all sides"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.padding 2 (fun inner -> inner |> Layout3D.set 0 0 0 42))

        Expect.equal
          (CellGrid3D.get 2 2 2 grid)
          (ValueSome 42)
          "Padding offset should apply"

      testCase "paddingEx applies explicit padding per side"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.paddingEx 1 2 3 4 5 6 (fun inner ->
              inner |> Layout3D.set 0 0 0 42))

        Expect.equal
          (CellGrid3D.get 1 2 3 grid)
          (ValueSome 42)
          "Custom padding should apply"

      testCase "center centers a block within section"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.center 4 4 4 (fun inner ->
              inner |> Layout3D.set 0 0 0 42))

        Expect.equal
          (CellGrid3D.get 3 3 3 grid)
          (ValueSome 42)
          "Centered block should be at (3,3,3)"

      testCase "clear removes cells in volume"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.fill 0 0 0 5 5 5 1
            |> Layout3D.clear 1 1 1 2 2 2)

        Expect.equal
          (CellGrid3D.get 0 0 0 grid)
          (ValueSome 1)
          "Corner should remain"

        Expect.equal (CellGrid3D.get 1 1 1 grid) ValueNone "Cleared area"
        Expect.equal (CellGrid3D.get 2 2 2 grid) ValueNone "Cleared area"
        Expect.equal (CellGrid3D.get 3 1 1 grid) (ValueSome 1) "Outside clear"
    ]

    testList "Layout3D Planes" [
      testCase "floorXZ creates horizontal plane"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.floorXZ 2 3 4 4 3 99)

        for x in 2..5 do
          for z in 4..6 do
            Expect.equal
              (CellGrid3D.get x 3 z grid)
              (ValueSome 99)
              $"Floor at ({x},3,{z})"

        Expect.equal (CellGrid3D.get 3 2 5 grid) ValueNone "Below floor"
        Expect.equal (CellGrid3D.get 3 4 5 grid) ValueNone "Above floor"

      testCase "wallXY creates vertical XY plane"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.wallXY 2 3 4 3 2 99)

        for x in 2..4 do
          for y in 3..4 do
            Expect.equal
              (CellGrid3D.get x y 4 grid)
              (ValueSome 99)
              $"Wall at ({x},{y},4)"

        Expect.equal (CellGrid3D.get 3 3 3 grid) ValueNone "Before wall"
        Expect.equal (CellGrid3D.get 3 3 5 grid) ValueNone "After wall"

      testCase "wallYZ creates vertical YZ plane"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.wallYZ 2 3 4 2 3 99)

        for y in 3..4 do
          for z in 4..6 do
            Expect.equal
              (CellGrid3D.get 2 y z grid)
              (ValueSome 99)
              $"Wall at (2,{y},{z})"

        Expect.equal (CellGrid3D.get 1 3 5 grid) ValueNone "Before wall"
        Expect.equal (CellGrid3D.get 3 3 5 grid) ValueNone "After wall"
    ]

    testList "Layout3D Shapes" [
      testCase "shell creates hollow box (6 faces)"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.shell 1 1 1 4 4 4 1)

        Expect.equal (CellGrid3D.get 2 1 2 grid) (ValueSome 1) "Bottom face"
        Expect.equal (CellGrid3D.get 2 4 2 grid) (ValueSome 1) "Top face"
        Expect.equal (CellGrid3D.get 1 2 2 grid) (ValueSome 1) "West face"
        Expect.equal (CellGrid3D.get 4 2 2 grid) (ValueSome 1) "East face"
        Expect.equal (CellGrid3D.get 2 2 1 grid) (ValueSome 1) "South face"
        Expect.equal (CellGrid3D.get 2 2 4 grid) (ValueSome 1) "North face"

        Expect.equal (CellGrid3D.get 2 2 2 grid) ValueNone "Interior empty"
        Expect.equal (CellGrid3D.get 3 3 3 grid) ValueNone "Interior empty"

      testCase "edges creates only the 12 edges of a box"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.edges 1 1 1 4 4 4 1)

        Expect.equal (CellGrid3D.get 1 1 1 grid) (ValueSome 1) "Corner edge"
        Expect.equal (CellGrid3D.get 4 4 4 grid) (ValueSome 1) "Opposite corner"

        Expect.equal (CellGrid3D.get 2 2 1 grid) ValueNone "Face center empty"
        Expect.equal (CellGrid3D.get 1 2 2 grid) ValueNone "Face center empty"

        Expect.equal (CellGrid3D.get 2 1 1 grid) (ValueSome 1) "Edge along X"
        Expect.equal (CellGrid3D.get 1 2 1 grid) (ValueSome 1) "Edge along Y"
        Expect.equal (CellGrid3D.get 1 1 2 grid) (ValueSome 1) "Edge along Z"

      testCase "scatterEdges with zero width does not throw"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.scatterEdges 0 0 0 0 5 5 10 42 1)

        let mutable count = 0
        grid |> CellGrid3D.iter(fun _ _ _ _ -> count <- count + 1)
        Expect.equal count 0 "Should place no items"

      testCase "scatterEdges with zero height does not throw"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.scatterEdges 0 0 0 5 0 5 10 42 1)

        let mutable count = 0
        grid |> CellGrid3D.iter(fun _ _ _ _ -> count <- count + 1)
        Expect.equal count 0 "Should place no items"

      testCase "scatterEdges with zero depth does not throw"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.scatterEdges 0 0 0 5 5 0 10 42 1)

        let mutable count = 0
        grid |> CellGrid3D.iter(fun _ _ _ _ -> count <- count + 1)
        Expect.equal count 0 "Should place no items"
    ]

    testList "Layout3D Repetition" [
      testCase "repeatX creates line along X"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.repeatX 2 1 3 5 7)

        for x in 2..6 do
          Expect.equal
            (CellGrid3D.get x 1 3 grid)
            (ValueSome 7)
            $"Cell ({x},1,3)"

        Expect.equal (CellGrid3D.get 1 1 3 grid) ValueNone "Before start"
        Expect.equal (CellGrid3D.get 7 1 3 grid) ValueNone "After end"

      testCase "repeatY creates line along Y"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 10 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.repeatY 1 2 3 4 8)

        for y in 2..5 do
          Expect.equal
            (CellGrid3D.get 1 y 3 grid)
            (ValueSome 8)
            $"Cell (1,{y},3)"

      testCase "repeatZ creates line along Z"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.repeatZ 1 2 3 4 9)

        for z in 3..6 do
          Expect.equal
            (CellGrid3D.get 1 2 z grid)
            (ValueSome 9)
            $"Cell (1,2,{z})"

      testCase "column creates vertical column"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 10 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.column 2 1 3 4 5)

        for y in 1..4 do
          Expect.equal
            (CellGrid3D.get 2 y 3 grid)
            (ValueSome 5)
            $"Column at (2,{y},3)"
    ]

    testList "Layout3D Geometry" [
      testCase "line draws 3D line"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.line 1 2 3 5 2 3 1)

        for x in 1..5 do
          Expect.equal
            (CellGrid3D.get x 2 3 grid)
            (ValueSome 1)
            $"Line at ({x},2,3)"

      testCase "line draws diagonal 3D line"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.line 0 0 0 4 4 4 1)

        for i in 0..4 do
          Expect.equal
            (CellGrid3D.get i i i grid)
            (ValueSome 1)
            $"Point ({i},{i},{i})"

      testCase "sphere filled fills volume"
      <| fun _ ->
        let grid =
          CellGrid3D.create 20 20 20 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.sphere 10 10 10 3 true 1)

        Expect.equal
          (CellGrid3D.get 10 10 10 grid)
          (ValueSome 1)
          "Center filled"

        Expect.equal (CellGrid3D.get 9 10 10 grid) (ValueSome 1) "Near center"
        Expect.equal (CellGrid3D.get 10 9 10 grid) (ValueSome 1) "Near center"
        Expect.equal (CellGrid3D.get 13 10 10 grid) (ValueSome 1) "At radius"
        Expect.equal (CellGrid3D.get 14 10 10 grid) ValueNone "Outside radius"

      testCase "sphere outline creates hollow sphere"
      <| fun _ ->
        let grid =
          CellGrid3D.create 20 20 20 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.sphere 10 10 10 3 false 1)

        Expect.equal (CellGrid3D.get 10 10 10 grid) ValueNone "Center empty"

        Expect.equal
          (CellGrid3D.get 13 10 10 grid)
          (ValueSome 1)
          "Surface point"

      testCase "cylinder creates vertical cylinder"
      <| fun _ ->
        let grid =
          CellGrid3D.create 20 20 20 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.cylinder 10 10 5 3 4 true 1)

        Expect.equal (CellGrid3D.get 10 5 10 grid) (ValueSome 1) "Center base"
        Expect.equal (CellGrid3D.get 10 8 10 grid) (ValueSome 1) "Center top"
        Expect.equal (CellGrid3D.get 13 6 10 grid) (ValueSome 1) "At radius"
        Expect.equal (CellGrid3D.get 14 6 10 grid) ValueNone "Outside radius"
    ]

    testList "Layout3D Procedural" [
      testCase "generate fills with generator function"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(
            Layout3D.generate 1 1 1 3 3 3 (fun x y z -> x * 100 + y * 10 + z)
          )

        Expect.equal
          (CellGrid3D.get 1 1 1 grid)
          (ValueSome 111)
          "Generated value at (1,1,1)"

        Expect.equal
          (CellGrid3D.get 2 2 2 grid)
          (ValueSome 222)
          "Generated value at (2,2,2)"

        Expect.equal
          (CellGrid3D.get 3 3 3 grid)
          (ValueSome 333)
          "Generated value at (3,3,3)"

        Expect.equal (CellGrid3D.get 0 0 0 grid) ValueNone "Outside generation"

      testCase "scatter3D places random items"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.scatter3D 10 12345 42)

        let mutable count = 0
        grid |> CellGrid3D.iter(fun _ _ _ _ -> count <- count + 1)

        Expect.equal count 10 "Should place exactly 10 items"

      testCase "checker3D creates 3D checkerboard"
      <| fun _ ->
        let grid =
          CellGrid3D.create 4 4 4 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.checker3D 0 1)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 0) "(0,0,0) sum=0"
        Expect.equal (CellGrid3D.get 1 1 0 grid) (ValueSome 0) "(1,1,0) sum=2"
        Expect.equal (CellGrid3D.get 1 0 0 grid) (ValueSome 1) "(1,0,0) sum=1"
        Expect.equal (CellGrid3D.get 0 1 0 grid) (ValueSome 1) "(0,1,0) sum=1"
    ]

    testList "Layout3D Transformation" [
      testCase "iter provides read access to cells"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.set 1 1 1 10
            |> Layout3D.set 2 2 2 20
            |> Layout3D.iter 0 0 0 3 3 3 (fun x y z v ->
              match v with
              | ValueSome value ->
                CellGrid3D.set x y z (value * 2) section.BackingGrid
              | ValueNone -> ()))

        Expect.equal
          (CellGrid3D.get 1 1 1 grid)
          (ValueSome 20)
          "Iterated and doubled"

        Expect.equal
          (CellGrid3D.get 2 2 2 grid)
          (ValueSome 40)
          "Iterated and doubled"

      testCase "map transforms existing content"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.set 1 1 1 10
            |> Layout3D.set 2 2 2 20
            |> Layout3D.map 0 0 0 3 3 3 ((*) 2))

        Expect.equal (CellGrid3D.get 1 1 1 grid) (ValueSome 20) "Mapped value"
        Expect.equal (CellGrid3D.get 2 2 2 grid) (ValueSome 40) "Mapped value"

      testCase "replace swaps content values"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.set 1 1 1 1
            |> Layout3D.set 2 2 2 1
            |> Layout3D.set 3 3 3 2
            |> Layout3D.replace 1 99)

        Expect.equal (CellGrid3D.get 1 1 1 grid) (ValueSome 99) "Replaced"
        Expect.equal (CellGrid3D.get 2 2 2 grid) (ValueSome 99) "Replaced"
        Expect.equal (CellGrid3D.get 3 3 3 grid) (ValueSome 2) "Unchanged"

      testCase "setIfEmpty only sets when empty"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Layout3D.set 1 1 1 10
            |> Layout3D.setIfEmpty 1 1 1 99
            |> Layout3D.setIfEmpty 2 2 2 99)

        Expect.equal (CellGrid3D.get 1 1 1 grid) (ValueSome 10) "Original kept"
        Expect.equal (CellGrid3D.get 2 2 2 grid) (ValueSome 99) "New value set"
    ]

    testList "Layout3D Flow" [
      testCase "flowX places stamps along X"
      <| fun _ ->
        let stamps = [
          (fun s -> Layout3D.set 0 0 0 1 s)
          (fun s -> Layout3D.set 0 0 0 2 s)
          (fun s -> Layout3D.set 0 0 0 3 s)
        ]

        let grid =
          CellGrid3D.create 20 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.flowX 5 stamps)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 1) "First stamp"
        Expect.equal (CellGrid3D.get 5 0 0 grid) (ValueSome 2) "Second stamp"
        Expect.equal (CellGrid3D.get 10 0 0 grid) (ValueSome 3) "Third stamp"

      testCase "flowY places stamps along Y"
      <| fun _ ->
        let stamps = [
          (fun s -> Layout3D.set 0 0 0 1 s)
          (fun s -> Layout3D.set 0 0 0 2 s)
        ]

        let grid =
          CellGrid3D.create 5 20 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.flowY 3 stamps)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 1) "First stamp"
        Expect.equal (CellGrid3D.get 0 3 0 grid) (ValueSome 2) "Second stamp"

      testCase "flowZ places stamps along Z"
      <| fun _ ->
        let stamps = [
          (fun s -> Layout3D.set 0 0 0 1 s)
          (fun s -> Layout3D.set 0 0 0 2 s)
        ]

        let grid =
          CellGrid3D.create 5 5 20 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.flowZ 4 stamps)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 1) "First stamp"
        Expect.equal (CellGrid3D.get 0 0 4 grid) (ValueSome 2) "Second stamp"
    ]

    testList "Interior" [
      testCase "room creates enclosed space with floor, walls, ceiling"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Interior.room 5 4 5 1 2 3)

        Expect.equal (CellGrid3D.get 2 0 2 grid) (ValueSome 1) "Floor"
        Expect.equal (CellGrid3D.get 2 3 2 grid) (ValueSome 3) "Ceiling"
        Expect.equal (CellGrid3D.get 0 2 2 grid) (ValueSome 2) "West wall"
        Expect.equal (CellGrid3D.get 4 2 2 grid) (ValueSome 2) "East wall"
        Expect.equal (CellGrid3D.get 2 2 0 grid) (ValueSome 2) "South wall"
        Expect.equal (CellGrid3D.get 2 2 4 grid) (ValueSome 2) "North wall"
        Expect.equal (CellGrid3D.get 2 2 2 grid) ValueNone "Interior empty"

      testCase "openRoom creates room without ceiling"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Interior.openRoom 5 4 5 1 2)

        Expect.equal (CellGrid3D.get 2 0 2 grid) (ValueSome 1) "Floor exists"
        Expect.equal (CellGrid3D.get 0 2 2 grid) (ValueSome 2) "Wall exists"
        Expect.equal (CellGrid3D.get 2 3 2 grid) ValueNone "No ceiling"

      testCase "corridorX creates horizontal corridor"
      <| fun _ ->
        let grid =
          CellGrid3D.create 15 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Interior.corridorX 6 3 3 1 2 3)

        Expect.equal (CellGrid3D.get 0 0 1 grid) (ValueSome 1) "Floor start"
        Expect.equal (CellGrid3D.get 5 0 1 grid) (ValueSome 1) "Floor end"
        Expect.equal (CellGrid3D.get 0 2 1 grid) (ValueSome 3) "Ceiling start"
        Expect.equal (CellGrid3D.get 3 1 0 grid) (ValueSome 2) "South wall"
        Expect.equal (CellGrid3D.get 3 1 2 grid) (ValueSome 2) "North wall"

      testCase "corridorZ creates depth corridor"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 15 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Interior.corridorZ 6 3 3 1 2 3)

        Expect.equal (CellGrid3D.get 1 0 0 grid) (ValueSome 1) "Floor start"
        Expect.equal (CellGrid3D.get 1 0 5 grid) (ValueSome 1) "Floor end"
        Expect.equal (CellGrid3D.get 0 1 3 grid) (ValueSome 2) "West wall"
        Expect.equal (CellGrid3D.get 2 1 3 grid) (ValueSome 2) "East wall"

      testCase "doorway clears opening in wall"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 6 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Interior.room 5 6 5 1 2 3
            |> Layout3D.section
              0
              0
              0
              (Interior.doorway Interior.DoorSide.South 2 3))

        Expect.equal
          (CellGrid3D.get 1 1 0 grid)
          ValueNone
          "Doorway cleared at (1,1,0)"

        Expect.equal
          (CellGrid3D.get 2 1 0 grid)
          ValueNone
          "Doorway cleared at (2,1,0)"

        Expect.equal
          (CellGrid3D.get 1 2 0 grid)
          ValueNone
          "Doorway cleared at (1,2,0)"

        Expect.equal
          (CellGrid3D.get 2 3 0 grid)
          ValueNone
          "Doorway cleared at (2,3,0)"

        Expect.equal
          (CellGrid3D.get 0 2 0 grid)
          (ValueSome 2)
          "Wall beside door"

      testCase "stairs creates staircase"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Interior.stairs 3 4 4 1)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 1) "First step"
        Expect.equal (CellGrid3D.get 0 1 1 grid) (ValueSome 1) "Second step"
        Expect.equal (CellGrid3D.get 0 2 2 grid) (ValueSome 1) "Third step"
        Expect.equal (CellGrid3D.get 0 3 3 grid) (ValueSome 1) "Fourth step"

      testCase "shaft creates vertical shaft"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Interior.shaft 3 3 5 1)

        Expect.equal (CellGrid3D.get 0 2 0 grid) (ValueSome 1) "Wall"
        Expect.equal (CellGrid3D.get 2 2 2 grid) (ValueSome 1) "Wall"
        Expect.equal (CellGrid3D.get 1 2 1 grid) ValueNone "Shaft interior"

      testCase "pillar creates vertical column"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Interior.pillar 5 1 2 3)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 1) "Base"
        Expect.equal (CellGrid3D.get 0 1 0 grid) (ValueSome 2) "Middle"
        Expect.equal (CellGrid3D.get 0 2 0 grid) (ValueSome 2) "Middle"
        Expect.equal (CellGrid3D.get 0 3 0 grid) (ValueSome 2) "Middle"
        Expect.equal (CellGrid3D.get 0 4 0 grid) (ValueSome 3) "Top"

      testCase "window clears opening in wall"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 6 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section
            |> Interior.room 5 6 5 1 2 3
            |> Layout3D.section
              0
              0
              0
              (Interior.window Interior.DoorSide.East 2 2 2))

        Expect.equal
          (CellGrid3D.get 4 2 1 grid)
          ValueNone
          "Window cleared at (4,2,1)"

        Expect.equal
          (CellGrid3D.get 4 2 2 grid)
          ValueNone
          "Window cleared at (4,2,2)"

        Expect.equal
          (CellGrid3D.get 4 3 1 grid)
          ValueNone
          "Window cleared at (4,3,1)"

        Expect.equal
          (CellGrid3D.get 4 3 2 grid)
          ValueNone
          "Window cleared at (4,3,2)"

        Expect.equal (CellGrid3D.get 4 1 1 grid) (ValueSome 2) "Wall below"
        Expect.equal (CellGrid3D.get 4 4 1 grid) (ValueSome 2) "Wall above"
    ]

    testList "Terrain" [
      testCase "ground creates flat ground plane"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.ground 5 5 1)

        for x in 0..4 do
          for z in 0..4 do
            Expect.equal
              (CellGrid3D.get x 0 z grid)
              (ValueSome 1)
              $"Ground at ({x},0,{z})"

        Expect.equal (CellGrid3D.get 2 1 2 grid) ValueNone "Above ground empty"

      testCase "plateau creates elevated flat top"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.plateau 5 5 4 1 2)

        Expect.equal (CellGrid3D.get 2 3 2 grid) (ValueSome 1) "Top surface"
        Expect.equal (CellGrid3D.get 0 2 2 grid) (ValueSome 2) "Side"
        Expect.equal (CellGrid3D.get 2 0 2 grid) (ValueSome 2) "Bottom"

      testCase "pit clears depression"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section |> Layout3D.fill 0 0 0 5 5 5 1 |> Terrain.pit 3 3 2)

        for x in 0..2 do
          for z in 0..2 do
            for y in 0..1 do
              Expect.equal
                (CellGrid3D.get x y z grid)
                ValueNone
                $"Pit at ({x},{y},{z})"

        Expect.equal (CellGrid3D.get 3 0 0 grid) (ValueSome 1) "Outside pit"

      testCase "rampX creates ramp along X"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.rampX 3 4 3 1)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 1) "Start low"
        Expect.equal (CellGrid3D.get 1 0 0 grid) (ValueSome 1) "Still low"
        Expect.equal (CellGrid3D.get 2 1 0 grid) (ValueSome 1) "Rising"
        Expect.equal (CellGrid3D.get 3 2 0 grid) (ValueSome 1) "High"

      testCase "rampZ creates ramp along Z"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.rampZ 3 4 3 1)

        Expect.equal (CellGrid3D.get 0 0 0 grid) (ValueSome 1) "Start low"
        Expect.equal (CellGrid3D.get 0 0 3 grid) (ValueSome 1) "High"

      testCase "path creates path between waypoints"
      <| fun _ ->
        let points = [ (0, 0, 0); (3, 0, 0) ]

        let grid =
          CellGrid3D.create 10 5 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.path points 1 1)

        for x in 0..3 do
          Expect.equal
            (CellGrid3D.get x 0 0 grid)
            (ValueSome 1)
            $"Path at ({x},0,0)"

      testCase "scatter places items randomly on ground"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 5 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.scatter 15 99999 42)

        let mutable count = 0
        grid |> CellGrid3D.iter(fun _ _ _ _ -> count <- count + 1)

        Expect.isTrue (count > 0) "Should scatter at least some items"
        Expect.isTrue (count <= 15) "Should not exceed requested count"

        grid
        |> CellGrid3D.iter(fun x y z _ ->
          Expect.equal y 0 $"Item at ({x},{y},{z}) should be at ground level")

      testCase "heightmap generates terrain from height function"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.heightmap (fun x z -> x + z) 1)

        Expect.equal
          (CellGrid3D.get 0 0 0 grid)
          (ValueSome 1)
          "Height 0 at (0,0)"

        Expect.equal
          (CellGrid3D.get 1 1 0 grid)
          (ValueSome 1)
          "Height 1 at (1,0)"

        Expect.equal
          (CellGrid3D.get 2 2 0 grid)
          (ValueSome 1)
          "Height 2 at (2,0)"

        Expect.equal
          (CellGrid3D.get 1 0 1 grid)
          (ValueSome 1)
          "Height 1+1=2 at (1,1)"

        Expect.equal (CellGrid3D.get 0 1 0 grid) ValueNone "Above terrain empty"

      testCase "layeredHeightmap creates terrain with layers"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 10 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Terrain.layeredHeightmap (fun x z -> 4) 1 2 2 3)

        Expect.equal
          (CellGrid3D.get 2 4 2 grid)
          (ValueSome 1)
          "Top layer at Y=4"

        Expect.equal
          (CellGrid3D.get 2 3 2 grid)
          (ValueSome 2)
          "Middle layer at Y=3"

        Expect.equal
          (CellGrid3D.get 2 2 2 grid)
          (ValueSome 3)
          "Bottom layer at Y=2"

        Expect.equal
          (CellGrid3D.get 2 1 2 grid)
          (ValueSome 3)
          "Bottom layer at Y=1"

        Expect.equal
          (CellGrid3D.get 2 0 2 grid)
          (ValueSome 3)
          "Bottom layer at Y=0"
    ]

    testList "CellGridRenderer3D" [
      testCase "render iterates all cells"
      <| fun _ ->
        let grid =
          CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(fun section ->
            section |> Layout3D.set 1 1 1 10 |> Layout3D.set 2 2 2 20)

        let visited = ResizeArray<struct (Vector3 * int)>()

        CellGridRenderer3D.render grid (fun pos value ->
          visited.Add(struct (pos, value)))

        Expect.equal visited.Count 2 "Should visit 2 cells"

      testCase "render calculates correct world positions"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(32f, 16f, 8f)) Vector3.Zero
        CellGrid3D.set 2 1 3 99 grid

        let mutable capturedPos = Vector3.Zero
        CellGridRenderer3D.render grid (fun pos _ -> capturedPos <- pos)

        Expect.equal capturedPos.X 64f "X = 2*32"
        Expect.equal capturedPos.Y 16f "Y = 1*16"
        Expect.equal capturedPos.Z 24f "Z = 3*8"

      testCase "renderVolume only renders in bounds"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 10 10 (Vector3(32f, 32f, 32f)) Vector3.Zero
          |> Layout3D.run(Layout3D.fill 0 0 0 10 10 10 1)

        let bounds = {
          Min = Vector3(64f, 64f, 64f)
          Max = Vector3(96f, 96f, 96f)
        }

        let visited = ResizeArray<Vector3>()

        CellGridRenderer3D.renderVolume bounds grid (fun pos _ ->
          visited.Add(pos))

        Expect.equal visited.Count 8 "Should render only cells in bounds"

      testCase "renderWithIndices provides coordinates and position"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(32f, 32f, 32f)) Vector3.Zero
        CellGrid3D.set 2 3 4 99 grid

        let mutable capturedX, capturedY, capturedZ = 0, 0, 0
        let mutable capturedPos = Vector3.Zero

        CellGridRenderer3D.renderWithIndices grid (fun x y z pos _ ->
          capturedX <- x
          capturedY <- y
          capturedZ <- z
          capturedPos <- pos)

        Expect.equal capturedX 2 "X coordinate"
        Expect.equal capturedY 3 "Y coordinate"
        Expect.equal capturedZ 4 "Z coordinate"
        Expect.equal capturedPos (Vector3(64f, 96f, 128f)) "World position"
    ]

    testList "Non-cubic" [
      testCase "shell fills correct faces with non-cubic dimensions"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 3 8 (Vector3(32f, 32f, 32f)) Vector3.Zero

        let result =
          grid |> Layout3D.run(fun s -> s |> Layout3D.shell 1 1 1 4 2 5 42)

        for x in 0..9 do
          for y in 0..2 do
            for z in 0..7 do
              let isInside =
                x >= 1 && x <= 4 && y >= 1 && y <= 2 && z >= 1 && z <= 5

              let isFace =
                isInside && (y = 1 || y = 2 || z = 1 || z = 5 || x = 1 || x = 4)

              match CellGrid3D.get x y z result with
              | ValueSome v when v = 42 ->
                if not isFace then
                  failwith $"Cell {x} {y} {z} should NOT be filled"
              | ValueSome v ->
                failwith $"Cell {x} {y} {z} has unexpected value {v}"
              | ValueNone ->
                if isFace then
                  failwith $"Cell {x} {y} {z} should be filled"

      testCase
        "scatterShell scatters on correct faces with non-cubic dimensions"
      <| fun _ ->
        let grid =
          CellGrid3D.create 10 3 8 (Vector3(32f, 32f, 32f)) Vector3.Zero

        let result =
          grid
          |> Layout3D.run(fun s ->
            s |> Layout3D.scatterShell 1 1 1 4 2 5 100 42 99)

        for x in 0..9 do
          for y in 0..2 do
            for z in 0..7 do
              match CellGrid3D.get x y z result with
              | ValueSome v when v = 99 ->
                let isOnFace =
                  (x >= 1 && x <= 4 && z >= 1 && z <= 5 && (y = 1 || y = 2))
                  || (x >= 1 && x <= 4 && (z = 1 || z = 5) && y >= 1 && y <= 2)
                  || ((x = 1 || x = 4) && z >= 1 && z <= 5 && y >= 1 && y <= 2)

                if not isOnFace then
                  failwith $"Cell {x} {y} {z} should NOT be scattered"
              | _ -> ()
    ]
  ]
