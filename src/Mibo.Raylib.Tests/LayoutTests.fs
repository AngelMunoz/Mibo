module Mibo.Raylib.Tests.Layout

open Expecto
open System.Numerics
open Mibo.Layout

[<Tests>]
let tests =
  testList "Layout" [
    testList "CellGrid2D" [
      testCase "create initializes with ValueNone cells"
      <| fun _ ->
        let grid = CellGrid2D.create 10 5 (Vector2(32f, 32f)) Vector2.Zero

        Expect.equal grid.Width 10 "Width should be 10"
        Expect.equal grid.Height 5 "Height should be 5"
        Expect.equal grid.CellSize (Vector2(32f, 32f)) "CellSize should match"

        for x in 0..9 do
          for y in 0..4 do
            Expect.equal
              (CellGrid2D.get x y grid)
              ValueNone
              "Cell should be empty"

      testCase "set and get roundtrip"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.set 5 3 42 grid

        Expect.equal
          (CellGrid2D.get 5 3 grid)
          (ValueSome 42)
          "Should get set value"

        Expect.equal
          (CellGrid2D.get 0 0 grid)
          ValueNone
          "Unset cell should be empty"

      testCase "set out of bounds is ignored"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.set -1 0 1 grid
        CellGrid2D.set 0 -1 1 grid
        CellGrid2D.set 5 0 1 grid
        CellGrid2D.set 0 5 1 grid

      testCase "get out of bounds returns ValueNone"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero

        Expect.equal (CellGrid2D.get -1 0 grid) ValueNone "Negative X"
        Expect.equal (CellGrid2D.get 0 -1 grid) ValueNone "Negative Y"
        Expect.equal (CellGrid2D.get 5 0 grid) ValueNone "X at width"
        Expect.equal (CellGrid2D.get 0 5 grid) ValueNone "Y at height"

      testCase "getWorldPos calculates correct position"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) (Vector2(100f, 50f))

        let pos = CellGrid2D.getWorldPos 3 2 grid

        Expect.equal pos.X 196f "X = 100 + 3*32"
        Expect.equal pos.Y 114f "Y = 50 + 2*32"

      testCase "clear removes cell content"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.set 2 2 42 grid
        Expect.equal (CellGrid2D.get 2 2 grid) (ValueSome 42) "Should be set"

        CellGrid2D.clear 2 2 grid
        Expect.equal (CellGrid2D.get 2 2 grid) ValueNone "Should be cleared"

      testCase "clear out of bounds is ignored"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.clear -1 0 grid
        CellGrid2D.clear 0 -1 grid
        CellGrid2D.clear 5 0 grid
        CellGrid2D.clear 0 5 grid

      testCase "iter visits all populated cells"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 1 1 10 grid
        CellGrid2D.set 3 2 20 grid

        let visited = ResizeArray<struct (int * int * int)>()
        grid |> CellGrid2D.iter(fun x y v -> visited.Add(struct (x, y, v)))

        Expect.equal visited.Count 2 "Should visit 2 cells"
        Expect.contains visited (struct (1, 1, 10)) "Should contain first cell"
        Expect.contains visited (struct (3, 2, 20)) "Should contain second cell"
    ]

    testList "Layout DSL" [
      testCase "run executes layout function"
      <| fun _ ->
        let grid =
          CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(fun section -> section |> Layout.set 2 2 42)

        Expect.equal (CellGrid2D.get 2 2 grid) (ValueSome 42) "Should set cell"

      testCase "fill creates rectangle"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.fill 2 3 4 2 99)

        for x in 2..5 do
          for y in 3..4 do
            Expect.equal
              (CellGrid2D.get x y grid)
              (ValueSome 99)
              $"Cell ({x},{y}) should be filled"

        Expect.equal (CellGrid2D.get 1 3 grid) ValueNone "Left of fill"
        Expect.equal (CellGrid2D.get 6 3 grid) ValueNone "Right of fill"

      testCase "border creates hollow rectangle"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.border 1 1 4 3 1)

        for x in 1..4 do
          Expect.equal
            (CellGrid2D.get x 1 grid)
            (ValueSome 1)
            $"Top edge ({x},1)"

          Expect.equal
            (CellGrid2D.get x 3 grid)
            (ValueSome 1)
            $"Bottom edge ({x},3)"

        for y in 1..3 do
          Expect.equal
            (CellGrid2D.get 1 y grid)
            (ValueSome 1)
            $"Left edge (1,{y})"

          Expect.equal
            (CellGrid2D.get 4 y grid)
            (ValueSome 1)
            $"Right edge (4,{y})"

        Expect.equal
          (CellGrid2D.get 2 2 grid)
          ValueNone
          "Interior should be empty"

        Expect.equal
          (CellGrid2D.get 3 2 grid)
          ValueNone
          "Interior should be empty"

      testCase "section provides relative coordinates"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(fun section ->
            section
            |> Layout.section 3 4 (fun inner -> inner |> Layout.set 0 0 42))

        Expect.equal
          (CellGrid2D.get 3 4 grid)
          (ValueSome 42)
          "Section offset should apply"

        Expect.equal
          (CellGrid2D.get 0 0 grid)
          ValueNone
          "Origin should be empty"

      testCase "padding shrinks section"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(fun section ->
            section
            |> Layout.padding 2 (fun inner -> inner |> Layout.set 0 0 42))

        Expect.equal
          (CellGrid2D.get 2 2 grid)
          (ValueSome 42)
          "Padding offset should apply"

      testCase "clear removes cells"
      <| fun _ ->
        let grid =
          CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(fun section ->
            section |> Layout.fill 0 0 5 5 1 |> Layout.clear 1 1 2 2)

        Expect.equal
          (CellGrid2D.get 0 0 grid)
          (ValueSome 1)
          "Corner should remain"

        Expect.equal (CellGrid2D.get 1 1 grid) ValueNone "Cleared area"
        Expect.equal (CellGrid2D.get 2 2 grid) ValueNone "Cleared area"
        Expect.equal (CellGrid2D.get 3 1 grid) (ValueSome 1) "Outside clear"

      testCase "repeatX creates horizontal line"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 5 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.repeatX 2 1 5 7)

        for x in 2..6 do
          Expect.equal (CellGrid2D.get x 1 grid) (ValueSome 7) $"Cell ({x},1)"

        Expect.equal (CellGrid2D.get 1 1 grid) ValueNone "Before start"
        Expect.equal (CellGrid2D.get 7 1 grid) ValueNone "After end"

      testCase "repeatY creates vertical line"
      <| fun _ ->
        let grid =
          CellGrid2D.create 5 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.repeatY 1 2 4 8)

        for y in 2..5 do
          Expect.equal (CellGrid2D.get 1 y grid) (ValueSome 8) $"Cell (1,{y})"
    ]

    testList "Geometry" [
      testCase "line draws horizontal line"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 5 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.line 1 2 5 2 1)

        for x in 1..5 do
          Expect.equal (CellGrid2D.get x 2 grid) (ValueSome 1) $"Point ({x},2)"

      testCase "line draws vertical line"
      <| fun _ ->
        let grid =
          CellGrid2D.create 5 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.line 2 1 2 5 1)

        for y in 1..5 do
          Expect.equal (CellGrid2D.get 2 y grid) (ValueSome 1) $"Point (2,{y})"

      testCase "line draws diagonal"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.line 0 0 4 4 1)

        for i in 0..4 do
          Expect.equal
            (CellGrid2D.get i i grid)
            (ValueSome 1)
            $"Point ({i},{i})"

      testCase "circle outline draws points at radius"
      <| fun _ ->
        let grid =
          CellGrid2D.create 20 20 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.circle 10 10 5 false 1)

        Expect.equal (CellGrid2D.get 15 10 grid) (ValueSome 1) "Right point"
        Expect.equal (CellGrid2D.get 5 10 grid) (ValueSome 1) "Left point"
        Expect.equal (CellGrid2D.get 10 15 grid) (ValueSome 1) "Bottom point"
        Expect.equal (CellGrid2D.get 10 5 grid) (ValueSome 1) "Top point"

        Expect.equal
          (CellGrid2D.get 10 10 grid)
          ValueNone
          "Center empty for outline"

      testCase "circle filled fills interior"
      <| fun _ ->
        let grid =
          CellGrid2D.create 20 20 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.circle 10 10 3 true 1)

        Expect.equal (CellGrid2D.get 10 10 grid) (ValueSome 1) "Center filled"
        Expect.equal (CellGrid2D.get 9 10 grid) (ValueSome 1) "Adjacent filled"

      testCase "checker creates alternating pattern"
      <| fun _ ->
        let grid =
          CellGrid2D.create 4 4 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Layout.checker 0 1)

        Expect.equal (CellGrid2D.get 0 0 grid) (ValueSome 0) "(0,0) = odd"
        Expect.equal (CellGrid2D.get 1 0 grid) (ValueSome 1) "(1,0) = even"
        Expect.equal (CellGrid2D.get 0 1 grid) (ValueSome 1) "(0,1) = even"
        Expect.equal (CellGrid2D.get 1 1 grid) (ValueSome 0) "(1,1) = odd"
    ]

    testList "Platformer" [
      testCase "box creates border with fill"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Platformer.box 5 4 1 2)

        Expect.equal (CellGrid2D.get 0 0 grid) (ValueSome 1) "Top-left border"

        Expect.equal
          (CellGrid2D.get 4 3 grid)
          (ValueSome 1)
          "Bottom-right border"

        Expect.equal (CellGrid2D.get 2 2 grid) (ValueSome 2) "Interior fill"

      testCase "platform creates horizontal line at y=0"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 5 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(Platformer.platform 4 9)

        for x in 0..3 do
          Expect.equal
            (CellGrid2D.get x 0 grid)
            (ValueSome 9)
            $"Platform at ({x},0)"

      testCase "pit clears cells"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(fun section ->
            section |> Layout.fill 0 0 10 10 1 |> Platformer.pit 3 5)

        for x in 0..2 do
          for y in 0..4 do
            Expect.equal (CellGrid2D.get x y grid) ValueNone $"Pit at ({x},{y})"

        Expect.equal (CellGrid2D.get 3 0 grid) (ValueSome 1) "Right of pit"
        Expect.equal (CellGrid2D.get 0 5 grid) (ValueSome 1) "Below pit"

      testCase "gap clears rectangular area"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(fun section ->
            section |> Layout.fill 0 0 10 10 1 |> Platformer.gap 4 3)

        Expect.equal (CellGrid2D.get 2 1 grid) ValueNone "Inside gap"
        Expect.equal (CellGrid2D.get 4 0 grid) (ValueSome 1) "Right of gap"
    ]

    testList "TopDown" [
      testCase "room creates walls with floor"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(TopDown.room 5 4 1 2)

        Expect.equal (CellGrid2D.get 0 0 grid) (ValueSome 2) "Top-left wall"
        Expect.equal (CellGrid2D.get 4 3 grid) (ValueSome 2) "Bottom-right wall"

        Expect.equal (CellGrid2D.get 2 2 grid) (ValueSome 1) "Interior floor"

      testCase "wallSegment creates horizontal wall"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 5 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(TopDown.wallSegment 6 5)

        for x in 0..5 do
          Expect.equal
            (CellGrid2D.get x 0 grid)
            (ValueSome 5)
            $"Wall at ({x},0)"

      testCase "doorway creates wall with gap"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 5 (Vector2(32f, 32f)) Vector2.Zero
          |> Layout.run(TopDown.doorway 5 1)

        Expect.equal (CellGrid2D.get 0 0 grid) (ValueSome 1) "Left wall"
        Expect.equal (CellGrid2D.get 1 0 grid) (ValueSome 1) "Left wall"

        Expect.equal (CellGrid2D.get 2 0 grid) ValueNone "Doorway gap"

        Expect.equal (CellGrid2D.get 3 0 grid) (ValueSome 1) "Right wall"
        Expect.equal (CellGrid2D.get 4 0 grid) (ValueSome 1) "Right wall"
    ]

    testList "LayeredGrid2D" [
      testCase "layer creates and accesses layers on demand"
      <| fun _ ->
        let layered =
          LayeredGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
          |> LayeredLayout.layer 0 (Layout.set 1 1 10)
          |> LayeredLayout.layer 1 (Layout.set 2 2 20)

        let (layer0, _) = LayeredGrid2D.getOrAddLayer 0 layered
        let (layer1, _) = LayeredGrid2D.getOrAddLayer 1 layered

        Expect.equal
          (CellGrid2D.get 1 1 layer0)
          (ValueSome 10)
          "Layer 0 content"

        Expect.equal
          (CellGrid2D.get 2 2 layer1)
          (ValueSome 20)
          "Layer 1 content"

        Expect.equal
          (CellGrid2D.get 2 2 layer0)
          ValueNone
          "Layer 0 doesn't have layer 1 content"

        Expect.equal
          (CellGrid2D.get 1 1 layer1)
          ValueNone
          "Layer 1 doesn't have layer 0 content"
    ]
  ]
