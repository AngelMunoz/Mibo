module Mibo.Core.Tests.HexLayout

open Expecto
open System.Numerics
open Mibo.Layout

[<Tests>]
let tests =
  testList "HexLayout" [
    testList "DSL - PointyTop" [
      testCase "run executes layout function"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section -> section |> HexLayout.set 2 2 42)

        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 42) "Should set cell"

      testCase "fill creates filled region"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.fill 2 3 4 2 99)

        for col in 2..5 do
          for row in 3..4 do
            Expect.equal
              (HexGrid.get col row grid)
              (ValueSome 99)
              $"Cell ({col},{row}) should be filled"

        Expect.equal (HexGrid.get 1 3 grid) ValueNone "Left of fill"
        Expect.equal (HexGrid.get 6 3 grid) ValueNone "Right of fill"

      testCase "border creates hollow region"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.border 1 1 4 3 1)

        for col in 1..4 do
          Expect.equal
            (HexGrid.get col 1 grid)
            (ValueSome 1)
            $"Top edge ({col},1)"

          Expect.equal
            (HexGrid.get col 3 grid)
            (ValueSome 1)
            $"Bottom edge ({col},3)"

        for row in 1..3 do
          Expect.equal
            (HexGrid.get 1 row grid)
            (ValueSome 1)
            $"Left edge (1,{row})"

          Expect.equal
            (HexGrid.get 4 row grid)
            (ValueSome 1)
            $"Right edge (4,{row})"

        Expect.equal (HexGrid.get 2 2 grid) ValueNone "Interior should be empty"

      testCase "section provides relative coordinates"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.section 3 4 (fun inner ->
              inner |> HexLayout.set 0 0 42))

        Expect.equal
          (HexGrid.get 3 4 grid)
          (ValueSome 42)
          "Section offset should apply"

        Expect.equal (HexGrid.get 0 0 grid) ValueNone "Origin should be empty"

      testCase "padding shrinks section"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.padding 2 (fun inner -> inner |> HexLayout.set 0 0 42))

        Expect.equal
          (HexGrid.get 2 2 grid)
          (ValueSome 42)
          "Padding offset should apply"

      testCase "clear removes cells"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section |> HexLayout.fill 0 0 5 5 1 |> HexLayout.clear 1 1 2 2)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 1) "Corner should remain"

        Expect.equal (HexGrid.get 1 1 grid) ValueNone "Cleared area"
        Expect.equal (HexGrid.get 2 2 grid) ValueNone "Cleared area"
        Expect.equal (HexGrid.get 3 1 grid) (ValueSome 1) "Outside clear"

      testCase "repeatX creates horizontal line"
      <| fun _ ->
        let grid =
          HexGrid.create 10 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.repeatX 2 1 5 7)

        for col in 2..6 do
          Expect.equal (HexGrid.get col 1 grid) (ValueSome 7) $"Cell ({col},1)"

        Expect.equal (HexGrid.get 1 1 grid) ValueNone "Before start"
        Expect.equal (HexGrid.get 7 1 grid) ValueNone "After end"

      testCase "repeatY creates vertical line"
      <| fun _ ->
        let grid =
          HexGrid.create 5 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.repeatY 1 2 4 8)

        for row in 2..5 do
          Expect.equal (HexGrid.get 1 row grid) (ValueSome 8) $"Cell (1,{row})"

      testCase "setIfEmpty only sets when empty"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 10
            |> HexLayout.setIfEmpty 1 1 99
            |> HexLayout.setIfEmpty 2 2 99)

        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 10) "Original kept"

        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 99) "New value set"
    ]

    testList "DSL - FlatTop" [
      testCase "run executes layout function"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section -> section |> HexLayout.set 2 2 42)

        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 42) "Should set cell"

      testCase "fill creates filled region"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.fill 2 3 4 2 99)

        for col in 2..5 do
          for row in 3..4 do
            Expect.equal
              (HexGrid.get col row grid)
              (ValueSome 99)
              $"Cell ({col},{row}) should be filled"

        Expect.equal (HexGrid.get 1 3 grid) ValueNone "Left of fill"
        Expect.equal (HexGrid.get 6 3 grid) ValueNone "Right of fill"

      testCase "border creates hollow region"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.border 1 1 4 3 1)

        for col in 1..4 do
          Expect.equal
            (HexGrid.get col 1 grid)
            (ValueSome 1)
            $"Top edge ({col},1)"

          Expect.equal
            (HexGrid.get col 3 grid)
            (ValueSome 1)
            $"Bottom edge ({col},3)"

        for row in 1..3 do
          Expect.equal
            (HexGrid.get 1 row grid)
            (ValueSome 1)
            $"Left edge (1,{row})"

          Expect.equal
            (HexGrid.get 4 row grid)
            (ValueSome 1)
            $"Right edge (4,{row})"

        Expect.equal (HexGrid.get 2 2 grid) ValueNone "Interior should be empty"

      testCase "section provides relative coordinates"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.section 3 4 (fun inner ->
              inner |> HexLayout.set 0 0 42))

        Expect.equal
          (HexGrid.get 3 4 grid)
          (ValueSome 42)
          "Section offset should apply"

        Expect.equal (HexGrid.get 0 0 grid) ValueNone "Origin should be empty"

      testCase "padding shrinks section"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.padding 2 (fun inner -> inner |> HexLayout.set 0 0 42))

        Expect.equal
          (HexGrid.get 2 2 grid)
          (ValueSome 42)
          "Padding offset should apply"

      testCase "clear removes cells"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section |> HexLayout.fill 0 0 5 5 1 |> HexLayout.clear 1 1 2 2)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 1) "Corner should remain"

        Expect.equal (HexGrid.get 1 1 grid) ValueNone "Cleared area"
        Expect.equal (HexGrid.get 2 2 grid) ValueNone "Cleared area"
        Expect.equal (HexGrid.get 3 1 grid) (ValueSome 1) "Outside clear"

      testCase "repeatX creates horizontal line"
      <| fun _ ->
        let grid =
          HexGrid.create 10 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.repeatX 2 1 5 7)

        for col in 2..6 do
          Expect.equal (HexGrid.get col 1 grid) (ValueSome 7) $"Cell ({col},1)"

        Expect.equal (HexGrid.get 1 1 grid) ValueNone "Before start"
        Expect.equal (HexGrid.get 7 1 grid) ValueNone "After end"

      testCase "repeatY creates vertical line"
      <| fun _ ->
        let grid =
          HexGrid.create 5 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.repeatY 1 2 4 8)

        for row in 2..5 do
          Expect.equal (HexGrid.get 1 row grid) (ValueSome 8) $"Cell (1,{row})"

      testCase "setIfEmpty only sets when empty"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 10
            |> HexLayout.setIfEmpty 1 1 99
            |> HexLayout.setIfEmpty 2 2 99)

        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 10) "Original kept"

        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 99) "New value set"
    ]

    testList "Geometry - PointyTop" [
      testCase "line draws horizontal line"
      <| fun _ ->
        let grid =
          HexGrid.create 10 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.line 1 2 5 2 1)

        for col in 1..5 do
          Expect.equal (HexGrid.get col 2 grid) (ValueSome 1) $"Point ({col},2)"

      testCase "line draws vertical line"
      <| fun _ ->
        let grid =
          HexGrid.create 5 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.line 2 1 2 5 1)

        for row in 1..5 do
          Expect.equal (HexGrid.get 2 row grid) (ValueSome 1) $"Point (2,{row})"

      testCase "line draws diagonal"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.line 0 0 4 4 1)

        for i in 0..4 do
          Expect.equal (HexGrid.get i i grid) (ValueSome 1) $"Point ({i},{i})"

      testCase "circle outline draws points at radius"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.circle 10 10 5 false 1)

        Expect.equal (HexGrid.get 15 10 grid) (ValueSome 1) "Right point"
        Expect.equal (HexGrid.get 5 10 grid) (ValueSome 1) "Left point"
        Expect.equal (HexGrid.get 10 15 grid) (ValueSome 1) "Bottom point"
        Expect.equal (HexGrid.get 10 5 grid) (ValueSome 1) "Top point"

        Expect.equal
          (HexGrid.get 10 10 grid)
          ValueNone
          "Center empty for outline"

      testCase "circle filled fills interior"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.circle 10 10 3 true 1)

        Expect.equal (HexGrid.get 10 10 grid) (ValueSome 1) "Center filled"
        Expect.equal (HexGrid.get 9 10 grid) (ValueSome 1) "Adjacent filled"

      testCase "checker creates alternating pattern"
      <| fun _ ->
        let grid =
          HexGrid.create 4 4 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.checker 0 1)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 0) "(0,0) = odd"
        Expect.equal (HexGrid.get 1 0 grid) (ValueSome 1) "(1,0) = even"
        Expect.equal (HexGrid.get 0 1 grid) (ValueSome 1) "(0,1) = even"
        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 0) "(1,1) = odd"

      testCase "scatterBorder with zero width does not throw"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.scatterBorder 0 0 0 5 10 42 1)

        let mutable count = 0
        grid |> HexGrid.iter(fun _ _ _ -> count <- count + 1)
        Expect.equal count 0 "Should place no items"

      testCase "scatterBorder with zero height does not throw"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.scatterBorder 0 0 5 0 10 42 1)

        let mutable count = 0
        grid |> HexGrid.iter(fun _ _ _ -> count <- count + 1)
        Expect.equal count 0 "Should place no items"
    ]

    testList "Geometry - FlatTop" [
      testCase "line draws horizontal line"
      <| fun _ ->
        let grid =
          HexGrid.create 10 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.line 1 2 5 2 1)

        for col in 1..5 do
          Expect.equal (HexGrid.get col 2 grid) (ValueSome 1) $"Point ({col},2)"

      testCase "line draws vertical line"
      <| fun _ ->
        let grid =
          HexGrid.create 5 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.line 2 1 2 5 1)

        for row in 1..5 do
          Expect.equal (HexGrid.get 2 row grid) (ValueSome 1) $"Point (2,{row})"

      testCase "line draws diagonal"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.line 0 0 4 4 1)

        for i in 0..4 do
          Expect.equal (HexGrid.get i i grid) (ValueSome 1) $"Point ({i},{i})"

      testCase "circle outline draws points at radius"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.circle 10 10 5 false 1)

        Expect.equal (HexGrid.get 15 10 grid) (ValueSome 1) "Right point"
        Expect.equal (HexGrid.get 5 10 grid) (ValueSome 1) "Left point"
        Expect.equal (HexGrid.get 10 15 grid) (ValueSome 1) "Bottom point"
        Expect.equal (HexGrid.get 10 5 grid) (ValueSome 1) "Top point"

        Expect.equal
          (HexGrid.get 10 10 grid)
          ValueNone
          "Center empty for outline"

      testCase "circle filled fills interior"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.circle 10 10 3 true 1)

        Expect.equal (HexGrid.get 10 10 grid) (ValueSome 1) "Center filled"
        Expect.equal (HexGrid.get 9 10 grid) (ValueSome 1) "Adjacent filled"

      testCase "checker creates alternating pattern"
      <| fun _ ->
        let grid =
          HexGrid.create 4 4 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.checker 0 1)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 0) "(0,0) = odd"
        Expect.equal (HexGrid.get 1 0 grid) (ValueSome 1) "(1,0) = even"
        Expect.equal (HexGrid.get 0 1 grid) (ValueSome 1) "(0,1) = even"
        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 0) "(1,1) = odd"
    ]

    testList "Procedural - PointyTop" [
      testCase "generate fills with generator function"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.generate 1 1 3 3 (fun c r -> c * 10 + r))

        Expect.equal
          (HexGrid.get 1 1 grid)
          (ValueSome 11)
          "Generated value at (1,1)"

        Expect.equal
          (HexGrid.get 2 2 grid)
          (ValueSome 22)
          "Generated value at (2,2)"

        Expect.equal
          (HexGrid.get 3 3 grid)
          (ValueSome 33)
          "Generated value at (3,3)"

        Expect.equal (HexGrid.get 0 0 grid) ValueNone "Outside generation"

      testCase "scatter places random items"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.scatter 10 12345 42)

        let mutable count = 0
        grid |> HexGrid.iter(fun _ _ _ -> count <- count + 1)

        Expect.isGreaterThan count 0 "Should place at least some items"
        Expect.isLessThanOrEqual count 10 "Should not exceed requested count"

      testCase "scatterStamp places stamps"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(
            HexLayout.scatterStamp 3 42 (fun s -> s |> HexLayout.set 0 0 99)
          )

        let mutable count = 0
        grid |> HexGrid.iter(fun _ _ _ -> count <- count + 1)

        Expect.equal count 3 "Should place 3 stamps"
    ]

    testList "Procedural - FlatTop" [
      testCase "generate fills with generator function"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.generate 1 1 3 3 (fun c r -> c * 10 + r))

        Expect.equal
          (HexGrid.get 1 1 grid)
          (ValueSome 11)
          "Generated value at (1,1)"

        Expect.equal
          (HexGrid.get 2 2 grid)
          (ValueSome 22)
          "Generated value at (2,2)"

        Expect.equal
          (HexGrid.get 3 3 grid)
          (ValueSome 33)
          "Generated value at (3,3)"

        Expect.equal (HexGrid.get 0 0 grid) ValueNone "Outside generation"

      testCase "scatter places random items"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.scatter 10 12345 42)

        let mutable count = 0
        grid |> HexGrid.iter(fun _ _ _ -> count <- count + 1)

        Expect.isGreaterThan count 0 "Should place at least some items"
        Expect.isLessThanOrEqual count 10 "Should not exceed requested count"

      testCase "scatterStamp places stamps"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(
            HexLayout.scatterStamp 3 42 (fun s -> s |> HexLayout.set 0 0 99)
          )

        let mutable count = 0
        grid |> HexGrid.iter(fun _ _ _ -> count <- count + 1)

        Expect.equal count 3 "Should place 3 stamps"
    ]

    testList "Transformation - PointyTop" [
      testCase "iter provides read access to cells"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 10
            |> HexLayout.set 2 2 20
            |> HexLayout.iter 0 0 3 3 (fun c r v ->
              match v with
              | ValueSome value ->
                HexGrid.set c r (value * 2) section.BackingGrid
              | ValueNone -> ()))

        Expect.equal
          (HexGrid.get 1 1 grid)
          (ValueSome 20)
          "Iterated and doubled"

        Expect.equal
          (HexGrid.get 2 2 grid)
          (ValueSome 40)
          "Iterated and doubled"

      testCase "map transforms existing content"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 10
            |> HexLayout.set 2 2 20
            |> HexLayout.map 0 0 3 3 ((*) 2))

        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 20) "Mapped value"
        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 40) "Mapped value"

      testCase "replace swaps content values"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 1
            |> HexLayout.set 2 2 1
            |> HexLayout.set 3 3 2
            |> HexLayout.replace 1 99)

        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 99) "Replaced"
        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 99) "Replaced"
        Expect.equal (HexGrid.get 3 3 grid) (ValueSome 2) "Unchanged"
    ]

    testList "Transformation - FlatTop" [
      testCase "iter provides read access to cells"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 10
            |> HexLayout.set 2 2 20
            |> HexLayout.iter 0 0 3 3 (fun c r v ->
              match v with
              | ValueSome value ->
                HexGrid.set c r (value * 2) section.BackingGrid
              | ValueNone -> ()))

        Expect.equal
          (HexGrid.get 1 1 grid)
          (ValueSome 20)
          "Iterated and doubled"

        Expect.equal
          (HexGrid.get 2 2 grid)
          (ValueSome 40)
          "Iterated and doubled"

      testCase "map transforms existing content"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 10
            |> HexLayout.set 2 2 20
            |> HexLayout.map 0 0 3 3 ((*) 2))

        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 20) "Mapped value"
        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 40) "Mapped value"

      testCase "replace swaps content values"
      <| fun _ ->
        let grid =
          HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.set 1 1 1
            |> HexLayout.set 2 2 1
            |> HexLayout.set 3 3 2
            |> HexLayout.replace 1 99)

        Expect.equal (HexGrid.get 1 1 grid) (ValueSome 99) "Replaced"
        Expect.equal (HexGrid.get 2 2 grid) (ValueSome 99) "Replaced"
        Expect.equal (HexGrid.get 3 3 grid) (ValueSome 2) "Unchanged"
    ]

    testList "Flow - PointyTop" [
      testCase "flowX places stamps along X"
      <| fun _ ->
        let stamps = [
          (fun s -> HexLayout.set 0 0 1 s)
          (fun s -> HexLayout.set 0 0 2 s)
          (fun s -> HexLayout.set 0 0 3 s)
        ]

        let grid =
          HexGrid.create 20 5 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.flowX 5 stamps)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 1) "First stamp"
        Expect.equal (HexGrid.get 5 0 grid) (ValueSome 2) "Second stamp"
        Expect.equal (HexGrid.get 10 0 grid) (ValueSome 3) "Third stamp"

      testCase "flowY places stamps along Y"
      <| fun _ ->
        let stamps = [
          (fun s -> HexLayout.set 0 0 1 s)
          (fun s -> HexLayout.set 0 0 2 s)
        ]

        let grid =
          HexGrid.create 5 20 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(HexLayout.flowY 3 stamps)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 1) "First stamp"
        Expect.equal (HexGrid.get 0 3 grid) (ValueSome 2) "Second stamp"
    ]

    testList "Flow - FlatTop" [
      testCase "flowX places stamps along X"
      <| fun _ ->
        let stamps = [
          (fun s -> HexLayout.set 0 0 1 s)
          (fun s -> HexLayout.set 0 0 2 s)
          (fun s -> HexLayout.set 0 0 3 s)
        ]

        let grid =
          HexGrid.create 20 5 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.flowX 5 stamps)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 1) "First stamp"
        Expect.equal (HexGrid.get 5 0 grid) (ValueSome 2) "Second stamp"
        Expect.equal (HexGrid.get 10 0 grid) (ValueSome 3) "Third stamp"

      testCase "flowY places stamps along Y"
      <| fun _ ->
        let stamps = [
          (fun s -> HexLayout.set 0 0 1 s)
          (fun s -> HexLayout.set 0 0 2 s)
        ]

        let grid =
          HexGrid.create 5 20 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(HexLayout.flowY 3 stamps)

        Expect.equal (HexGrid.get 0 0 grid) (ValueSome 1) "First stamp"
        Expect.equal (HexGrid.get 0 3 grid) (ValueSome 2) "Second stamp"
    ]

    testList "Composition - PointyTop" [
      testCase "center centers a block"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.center 4 4 (fun inner ->
              inner |> HexLayout.set 0 0 42))

        Expect.equal
          (HexGrid.get 3 3 grid)
          (ValueSome 42)
          "Centered block should be at (3,3)"

      testCase "section creates sub-section"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.section 2 3 (fun inner ->
              inner
              |> HexLayout.section 1 1 (fun inner2 ->
                inner2 |> HexLayout.set 0 0 42)))

        Expect.equal
          (HexGrid.get 3 4 grid)
          (ValueSome 42)
          "Nested section offset"
    ]

    testList "Composition - FlatTop" [
      testCase "center centers a block"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.center 4 4 (fun inner ->
              inner |> HexLayout.set 0 0 42))

        Expect.equal
          (HexGrid.get 3 3 grid)
          (ValueSome 42)
          "Centered block should be at (3,3)"

      testCase "section creates sub-section"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
          |> HexLayout.run(fun section ->
            section
            |> HexLayout.section 2 3 (fun inner ->
              inner
              |> HexLayout.section 1 1 (fun inner2 ->
                inner2 |> HexLayout.set 0 0 42)))

        Expect.equal
          (HexGrid.get 3 4 grid)
          (ValueSome 42)
          "Nested section offset"
    ]

    testList "Non-square" [
      testCase
        "border fills correct edges with non-square dimensions - PointyTop"
      <| fun _ ->
        let grid = HexGrid.create 10 8 32f Vector2.Zero HexOrientation.PointyTop

        let result =
          grid |> HexLayout.run(fun s -> s |> HexLayout.border 1 1 4 5 42)

        for col in 0..9 do
          for row in 0..7 do
            let isInside = col >= 1 && col <= 4 && row >= 1 && row <= 5

            let isEdge = isInside && (col = 1 || col = 4 || row = 1 || row = 5)

            match HexGrid.get col row result with
            | ValueSome v when v = 42 ->
              if not isEdge then
                failwith $"Cell {col} {row} should NOT be filled"
            | ValueSome v ->
              failwith $"Cell {col} {row} has unexpected value {v}"
            | ValueNone ->
              if isEdge then
                failwith $"Cell {col} {row} should be filled"

      testCase "border fills correct edges with non-square dimensions - FlatTop"
      <| fun _ ->
        let grid = HexGrid.create 10 8 32f Vector2.Zero HexOrientation.FlatTop

        let result =
          grid |> HexLayout.run(fun s -> s |> HexLayout.border 1 1 4 5 42)

        for col in 0..9 do
          for row in 0..7 do
            let isInside = col >= 1 && col <= 4 && row >= 1 && row <= 5

            let isEdge = isInside && (col = 1 || col = 4 || row = 1 || row = 5)

            match HexGrid.get col row result with
            | ValueSome v when v = 42 ->
              if not isEdge then
                failwith $"Cell {col} {row} should NOT be filled"
            | ValueSome v ->
              failwith $"Cell {col} {row} has unexpected value {v}"
            | ValueNone ->
              if isEdge then
                failwith $"Cell {col} {row} should be filled"

      testCase
        "corners fills correct corners with non-square dimensions - PointyTop"
      <| fun _ ->
        let grid = HexGrid.create 10 8 32f Vector2.Zero HexOrientation.PointyTop

        let result =
          grid |> HexLayout.run(fun s -> s |> HexLayout.corners 1 1 4 5 42)

        let expectedCorners = [ (1, 1); (4, 1); (1, 5); (4, 5) ]

        for col in 0..9 do
          for row in 0..7 do
            let isCorner = expectedCorners |> List.contains(col, row)

            match HexGrid.get col row result with
            | ValueSome v when v = 42 ->
              if not isCorner then
                failwith $"Cell {col} {row} should NOT be filled"
            | ValueSome v ->
              failwith $"Cell {col} {row} has unexpected value {v}"
            | ValueNone ->
              if isCorner then
                failwith $"Cell {col} {row} should be filled"

      testCase
        "scatterBorder scatters on correct edges with non-square dimensions - PointyTop"
      <| fun _ ->
        let grid = HexGrid.create 10 8 32f Vector2.Zero HexOrientation.PointyTop

        let result =
          grid
          |> HexLayout.run(fun s ->
            s |> HexLayout.scatterBorder 1 1 4 5 100 42 99)

        for col in 0..9 do
          for row in 0..7 do
            match HexGrid.get col row result with
            | ValueSome v when v = 99 ->
              let isOnEdge =
                (col >= 1 && col <= 4 && (row = 1 || row = 5))
                || ((col = 1 || col = 4) && row >= 1 && row <= 5)

              if not isOnEdge then
                failwith $"Cell {col} {row} should NOT be scattered"
            | _ -> ()
    ]
  ]
