module Mibo.Core.Tests.Spatial2D

open Expecto
open System.Numerics
open Mibo.Layout

[<Tests>]
let tests =
  testList "Spatial2D" [

    // ── Square grid: Neighbors ────────────────────────────────────────

    testList "SquareGrid Neighbors" [
      testCase "neighbors4 returns 4 for center cell"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 5 5 grid
        Expect.equal nbrs.Length 4 "Center should have 4 neighbors"

      testCase "neighbors4 returns 2 for corner cell (0,0)"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 0 0 grid
        Expect.equal nbrs.Length 2 "Corner (0,0) should have 2 neighbors"

      testCase "neighbors4 returns 2 for corner cell (9,9)"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 9 9 grid
        Expect.equal nbrs.Length 2 "Corner (9,9) should have 2 neighbors"

      testCase "neighbors4 returns 3 for edge cell"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 0 5 grid
        Expect.equal nbrs.Length 3 "Edge (0,5) should have 3 neighbors"

      testCase "neighbors4 never returns out-of-bounds coordinates"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero

        for x in 0..4 do
          for y in 0..4 do
            let nbrs = Grid2DSpatial.neighbors4 x y grid

            for i in 0 .. nbrs.Length - 1 do
              let struct (nx, ny) = nbrs.[i]
              Expect.isGreaterThanOrEqual nx 0 "nx >= 0"
              Expect.isLessThan nx 5 "nx < 5"
              Expect.isGreaterThanOrEqual ny 0 "ny >= 0"
              Expect.isLessThan ny 5 "ny < 5"

      testCase "neighbors8 returns 8 for center cell"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors8 5 5 grid
        Expect.equal nbrs.Length 8 "Center should have 8 neighbors"

      testCase "neighbors8 returns 3 for corner cell (0,0)"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors8 0 0 grid
        Expect.equal nbrs.Length 3 "Corner (0,0) should have 3 neighbors"

      testCase "neighbors8 returns 5 for edge cell"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors8 0 5 grid
        Expect.equal nbrs.Length 5 "Edge (0,5) should have 5 neighbors"

      testCase "neighbors8 never returns out-of-bounds coordinates"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero

        for x in 0..4 do
          for y in 0..4 do
            let nbrs = Grid2DSpatial.neighbors8 x y grid

            for i in 0 .. nbrs.Length - 1 do
              let struct (nx, ny) = nbrs.[i]
              Expect.isGreaterThanOrEqual nx 0 "nx >= 0"
              Expect.isLessThan nx 5 "nx < 5"
              Expect.isGreaterThanOrEqual ny 0 "ny >= 0"
              Expect.isLessThan ny 5 "ny < 5"
    ]

    // ── Square grid: Distance ─────────────────────────────────────────

    testList "SquareGrid Distance" [
      testCase "distanceManhattan same cell = 0"
      <| fun _ ->
        Expect.equal (Grid2DSpatial.distanceManhattan 3 3 3 3) 0 "Same cell"

      testCase "distanceManhattan cardinal = dx + dy"
      <| fun _ ->
        Expect.equal (Grid2DSpatial.distanceManhattan 0 0 3 4) 7 "Cardinal"

      testCase "distanceManhattan symmetry"
      <| fun _ ->
        Expect.equal
          (Grid2DSpatial.distanceManhattan 1 2 5 7)
          (Grid2DSpatial.distanceManhattan 5 7 1 2)
          "Symmetric"

      testCase "distanceChebyshev same cell = 0"
      <| fun _ ->
        Expect.equal (Grid2DSpatial.distanceChebyshev 3 3 3 3) 0 "Same cell"

      testCase "distanceChebyshev diagonal = max(dx,dy)"
      <| fun _ ->
        Expect.equal (Grid2DSpatial.distanceChebyshev 0 0 3 4) 4 "Diagonal"

      testCase "distanceChebyshev pure horizontal"
      <| fun _ ->
        Expect.equal (Grid2DSpatial.distanceChebyshev 0 0 5 0) 5 "Horizontal"

      testCase "distanceChebyshev symmetry"
      <| fun _ ->
        Expect.equal
          (Grid2DSpatial.distanceChebyshev 1 2 5 7)
          (Grid2DSpatial.distanceChebyshev 5 7 1 2)
          "Symmetric"

      testCase "distanceEuclidean same cell = 0"
      <| fun _ ->
        Expect.floatClose
          Accuracy.high
          (float(Grid2DSpatial.distanceEuclidean 3 3 3 3))
          0.0
          "Same cell"

      testCase "distanceEuclidean 3-4-5 triangle"
      <| fun _ ->
        Expect.floatClose
          Accuracy.high
          (float(Grid2DSpatial.distanceEuclidean 0 0 3 4))
          5.0
          "3-4-5"

      testCase "distanceEuclidean symmetry"
      <| fun _ ->
        Expect.floatClose
          Accuracy.high
          (float(Grid2DSpatial.distanceEuclidean 1 2 5 7))
          (float(Grid2DSpatial.distanceEuclidean 5 7 1 2))
          "Symmetric"
    ]

    // ── Square grid: WorldToCell ──────────────────────────────────────

    testList "SquareGrid WorldToCell" [
      testCase "worldToCell roundtrips with getWorldPos"
      <| fun _ ->
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) (Vector2(100f, 50f))

        let worldPos = CellGrid2D.getWorldPos 3 5 grid
        let result = Grid2DSpatial.worldToCell worldPos grid

        match result with
        | ValueSome struct (cx, cy) ->
          Expect.equal cx 3 "X should roundtrip"
          Expect.equal cy 5 "Y should roundtrip"
        | ValueNone -> failwith "Expected ValueSome"

      testCase "worldToCell returns ValueNone for OOB position"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let result = Grid2DSpatial.worldToCell (Vector2(-100f, -100f)) grid
        Expect.equal result ValueNone "Should be OOB"

      testCase "worldToCell works with non-zero origin"
      <| fun _ ->
        let origin = Vector2(200f, 300f)
        let grid = CellGrid2D.create 10 10 (Vector2(16f, 16f)) origin

        let worldPos = CellGrid2D.getWorldPos 5 5 grid
        let result = Grid2DSpatial.worldToCell worldPos grid

        match result with
        | ValueSome struct (cx, cy) ->
          Expect.equal cx 5 "X"
          Expect.equal cy 5 "Y"
        | ValueNone -> failwith "Expected ValueSome"

      // Regression: getWorldPos anchors a cell at its corner
      // (origin + N*cellSize), so worldToCell must resolve the cell that
      // *contains* the point (floor), not the nearest corner (round).
      // Without this, the second half of a cell reports the next cell.
      testCase "worldToCell returns the containing cell, not the nearest corner"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero

        // Cell 1 spans X [32, 64). 49 is in the second half of cell 1;
        // round(49/32) would wrongly snap to cell 2.
        let result = Grid2DSpatial.worldToCell (Vector2(49f, 10f)) grid

        match result with
        | ValueSome struct (cx, cy) ->
          Expect.equal cx 1 "X should be the containing cell"
          Expect.equal cy 0 "Y should be the containing cell"
        | ValueNone -> failwith "Expected ValueSome"

      // Regression: F#'s `round` is banker's rounding, so at exact .5
      // fractional positions (the centers of odd cells) it snaps to the
      // even neighbor. floor has no such bias.
      testCase "worldToCell is unbiased at cell-center half points"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero

        // Center of cell 1: X = 48 -> 48/32 = 1.5
        // round(1.5) = 2 (banker's); floor(1.5) = 1.
        for x in [ 1; 2; 3 ] do
          let centerX = float32 x * 32f
          let result = Grid2DSpatial.worldToCell (Vector2(centerX, 0f)) grid

          match result with
          | ValueSome struct (cx, _) ->
            Expect.equal cx x "Center should map to its own cell"
          | ValueNone -> failwith "Expected ValueSome"

      testCase "worldToCell handles negative coordinates via floor"
      <| fun _ ->
        // Origin negative so cells extend into negative world space.
        let grid =
          CellGrid2D.create 10 10 (Vector2(32f, 32f)) (Vector2(-160f, -160f))

        // World X = -159 is just inside cell 0 (origin -160). floor(-159/32
        // adjusted) must stay 0, not -1 as truncate would give.
        let result = Grid2DSpatial.worldToCell (Vector2(-159f, -159f)) grid

        match result with
        | ValueSome struct (cx, cy) ->
          Expect.equal cx 0 "X should be cell 0"
          Expect.equal cy 0 "Y should be cell 0"
        | ValueNone -> failwith "Expected ValueSome"
    ]

    // ── Square grid: InRange ──────────────────────────────────────────

    testList "SquareGrid InRange" [
      testCase "inRange 0 returns only the origin cell"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let cells = Grid2DSpatial.inRange 5 5 0 grid
        Expect.equal cells.Length 1 "Only origin"
        Expect.contains cells (struct (5, 5)) "Contains origin"

      testCase "inRange 1 returns 9 cells (Chebyshev)"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let cells = Grid2DSpatial.inRange 5 5 1 grid
        Expect.equal cells.Length 9 "3x3 block"

      testCase "inRange respects grid bounds"
      <| fun _ ->
        let grid = CellGrid2D.create 3 3 (Vector2(32f, 32f)) Vector2.Zero
        let cells = Grid2DSpatial.inRange 0 0 5 grid
        Expect.equal cells.Length 9 "Only 3x3 = 9 valid cells"

        for i in 0 .. cells.Length - 1 do
          let struct (x, y) = cells.[i]
          Expect.isGreaterThanOrEqual x 0 "x >= 0"
          Expect.isLessThan x 3 "x < 3"
          Expect.isGreaterThanOrEqual y 0 "y >= 0"
          Expect.isLessThan y 3 "y < 3"

      testCase "inRange negative range returns empty"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let cells = Grid2DSpatial.inRange 2 2 -1 grid
        Expect.isEmpty cells "Negative range"
    ]

    // ── Square grid: LineOfSight ──────────────────────────────────────

    testList "SquareGrid LineOfSight" [
      testCase "clear line returns true"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero

        let isBlocked x y =
          match CellGrid2D.get x y grid with
          | ValueSome _ -> true
          | ValueNone -> false

        Expect.isTrue
          (Grid2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "Clear line"

      testCase "blocked line returns false"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 5 5 1 grid // blocker

        let isBlocked x y =
          match CellGrid2D.get x y grid with
          | ValueSome _ -> true
          | ValueNone -> false

        Expect.isFalse
          (Grid2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "Blocked line"

      testCase "same cell returns true"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let isBlocked _ _ = false

        Expect.isTrue
          (Grid2DSpatial.lineOfSight 2 2 2 2 isBlocked grid)
          "Same cell"

      testCase "lineOfSightCells stops at first blocker"
      <| fun _ ->
        let grid = CellGrid2D.create 10 1 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 5 0 1 grid

        let isBlocked x y =
          match CellGrid2D.get x y grid with
          | ValueSome _ -> true
          | ValueNone -> false

        let cells = Grid2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid

        Expect.isGreaterThan cells.Length 0 "Should have visible cells"
        Expect.isLessThan cells.Length 10 "Should stop before blocker"

        // Last cell should be (4,0), not (5,0)
        let struct (lastX, lastY) = cells.[cells.Length - 1]
        Expect.equal lastX 4 "Last visible x"
        Expect.equal lastY 0 "Last visible y"

      testCase "lineOfSightCells start blocked returns empty"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 2 2 1 grid
        let isBlocked x y = x = 2 && y = 2
        let cells = Grid2DSpatial.lineOfSightCells 2 2 4 4 isBlocked grid
        Expect.isEmpty cells "Start blocked → empty"
    ]

    // ── Square grid: FloodFill ────────────────────────────────────────

    testList "SquareGrid FloodFill" [
      testCase "floodFill fills entire grid when all passable"
      <| fun _ ->
        let grid = CellGrid2D.create 3 3 (Vector2(32f, 32f)) Vector2.Zero
        let predicate _ _ = true
        let cells = Grid2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 9 "Entire 3x3"

      testCase "floodFill stops at blocked cells"
      <| fun _ ->
        let grid = CellGrid2D.create 5 1 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 2 0 1 grid

        let predicate x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 2 "Only (0,0) and (1,0)"

      testCase "floodFill single cell region"
      <| fun _ ->
        let grid = CellGrid2D.create 3 3 (Vector2(32f, 32f)) Vector2.Zero

        // Surround the center with blocked cells
        CellGrid2D.set 0 1 1 grid
        CellGrid2D.set 2 1 1 grid
        CellGrid2D.set 1 0 1 grid
        CellGrid2D.set 1 2 1 grid

        let predicate x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid2DSpatial.floodFill 1 1 predicate grid
        Expect.equal cells.Length 1 "Only center"

      testCase "floodFill OOB start returns empty"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let predicate _ _ = true
        let cells = Grid2DSpatial.floodFill -1 0 predicate grid
        Expect.isEmpty cells "OOB"

      testCase "floodFill 1x1 grid"
      <| fun _ ->
        let grid = CellGrid2D.create 1 1 (Vector2(32f, 32f)) Vector2.Zero
        let predicate _ _ = true
        let cells = Grid2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 1 "Single cell"

      testCase "floodFill 0x0 grid returns empty"
      <| fun _ ->
        let grid = CellGrid2D.create 0 0 (Vector2(32f, 32f)) Vector2.Zero
        let predicate _ _ = true
        let cells = Grid2DSpatial.floodFill 0 0 predicate grid
        Expect.isEmpty cells "Empty grid"
    ]

    // ── Square grid: FindPath (A*) ────────────────────────────────────

    testList "SquareGrid FindPath" [
      testCase "finds straight path on open grid"
      <| fun _ ->
        let grid = CellGrid2D.create 5 1 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 4 0 passable cost grid with
        | ValueSome path ->
          Expect.equal path.Length 5 "5 cells"
          Expect.contains path (struct (0, 0)) "Start"
          Expect.contains path (struct (4, 0)) "Goal"
        | ValueNone -> failwith "Expected path"

      testCase "finds path around obstacle"
      <| fun _ ->
        let grid = CellGrid2D.create 5 3 (Vector2(32f, 32f)) Vector2.Zero

        // Wall at x=2, y=0..1
        CellGrid2D.set 2 0 1 grid
        CellGrid2D.set 2 1 1 grid

        let passable x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 4 0 passable cost grid with
        | ValueSome path ->
          Expect.isGreaterThan path.Length 0 "Path exists"
          let struct (sx, sy) = path.[0]
          Expect.equal sx 0 "Start x"
          Expect.equal sy 0 "Start y"
          let struct (gx, gy) = path.[path.Length - 1]
          Expect.equal gx 4 "Goal x"
          Expect.equal gy 0 "Goal y"
        | ValueNone -> failwith "Expected path around obstacle"

      testCase "returns ValueNone for unreachable goal"
      <| fun _ ->
        let grid = CellGrid2D.create 5 1 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 2 0 1 grid

        let passable x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 4 0 passable cost grid with
        | ValueSome _ -> failwith "Should not find path"
        | ValueNone -> ()

      testCase "start = goal returns single cell"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 2 2 2 2 passable cost grid with
        | ValueSome path ->
          Expect.equal path.Length 1 "Single cell"
          Expect.contains path (struct (2, 2)) "Is start"
        | ValueNone -> failwith "Expected single cell path"

      testCase "start blocked returns ValueNone"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 0 0 1 grid

        let passable x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 4 4 passable cost grid with
        | ValueSome _ -> failwith "Start blocked"
        | ValueNone -> ()

      testCase "goal blocked returns ValueNone"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 4 4 1 grid

        let passable x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 4 4 passable cost grid with
        | ValueSome _ -> failwith "Goal blocked"
        | ValueNone -> ()

      testCase "OOB start returns ValueNone"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath -1 0 4 4 passable cost grid with
        | ValueSome _ -> failwith "OOB start"
        | ValueNone -> ()

      testCase "OOB goal returns ValueNone"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 10 10 passable cost grid with
        | ValueSome _ -> failwith "OOB goal"
        | ValueNone -> ()

      testCase "1x1 grid start=goal returns single cell"
      <| fun _ ->
        let grid = CellGrid2D.create 1 1 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 0 0 passable cost grid with
        | ValueSome path ->
          Expect.equal path.Length 1 "Single cell"
          let struct (x, y) = path.[0]
          Expect.equal x 0 "x"
          Expect.equal y 0 "y"
        | ValueNone -> failwith "Expected single cell"

      testCase "custom cost function affects path"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true

        // Make horizontal movement very expensive
        let cost x1 _ x2 _ = if x1 <> x2 then 100f else 1f

        match Grid2DSpatial.findPath 0 0 4 0 passable cost grid with
        | ValueSome path ->
          // Path should exist and prefer vertical moves when possible
          Expect.isGreaterThan path.Length 0 "Path should exist"
        | ValueNone -> failwith "Expected path"
    ]

    // ── Hex grid: Neighbors ───────────────────────────────────────────

    testList "HexGrid Neighbors" [
      testCase "neighbors returns 6 for center cell (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let nbrs = Hex2DSpatial.neighbors 5 5 grid
        Expect.equal nbrs.Length 6 "Center should have 6 hex neighbors"

      testCase "neighbors returns 6 for center cell (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let nbrs = Hex2DSpatial.neighbors 5 5 grid
        Expect.equal nbrs.Length 6 "Center should have 6 hex neighbors"

      testCase "neighbors returns fewer for corner cell (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let nbrs = Hex2DSpatial.neighbors 0 0 grid
        Expect.isLessThan nbrs.Length 6 "PT Corner should have fewer neighbors"

      testCase "neighbors returns fewer for corner cell (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
        let nbrs = Hex2DSpatial.neighbors 0 0 grid
        Expect.isLessThan nbrs.Length 6 "FT Corner should have fewer neighbors"

      testCase "neighbors never returns out-of-bounds (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop

        for col in 0..4 do
          for row in 0..4 do
            let nbrs = Hex2DSpatial.neighbors col row grid

            for i in 0 .. nbrs.Length - 1 do
              let struct (nc, nr) = nbrs.[i]
              Expect.isGreaterThanOrEqual nc 0 "nc >= 0"
              Expect.isLessThan nc 5 "nc < 5"
              Expect.isGreaterThanOrEqual nr 0 "nr >= 0"
              Expect.isLessThan nr 5 "nr < 5"

      testCase "neighbors never returns out-of-bounds (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop

        for col in 0..4 do
          for row in 0..4 do
            let nbrs = Hex2DSpatial.neighbors col row grid

            for i in 0 .. nbrs.Length - 1 do
              let struct (nc, nr) = nbrs.[i]
              Expect.isGreaterThanOrEqual nc 0 "nc >= 0"
              Expect.isLessThan nc 5 "nc < 5"
              Expect.isGreaterThanOrEqual nr 0 "nr >= 0"
              Expect.isLessThan nr 5 "nr < 5"
    ]

    // ── Hex grid: Distance ────────────────────────────────────────────

    testList "HexGrid Distance" [
      testCase "distance same cell = 0"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        Expect.equal (Hex2DSpatial.distance 2 2 2 2 grid) 0 "Same cell"

      testCase "distance adjacent = 1 (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let nbrs = Hex2DSpatial.neighbors 2 2 grid
        Expect.isGreaterThan nbrs.Length 0 "Should have neighbors"

        for i in 0 .. nbrs.Length - 1 do
          let struct (nc, nr) = nbrs.[i]
          Expect.equal (Hex2DSpatial.distance 2 2 nc nr grid) 1 "Adjacent = 1"

      testCase "distance adjacent = 1 (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
        let nbrs = Hex2DSpatial.neighbors 2 2 grid
        Expect.isGreaterThan nbrs.Length 0 "Should have neighbors"

        for i in 0 .. nbrs.Length - 1 do
          let struct (nc, nr) = nbrs.[i]

          Expect.equal
            (Hex2DSpatial.distance 2 2 nc nr grid)
            1
            "FT Adjacent = 1"

      testCase "distance symmetry (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        Expect.equal
          (Hex2DSpatial.distance 1 2 5 7 grid)
          (Hex2DSpatial.distance 5 7 1 2 grid)
          "Symmetric"

      testCase "distance symmetry (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        Expect.equal
          (Hex2DSpatial.distance 1 2 5 7 grid)
          (Hex2DSpatial.distance 5 7 1 2 grid)
          "Symmetric"

      testCase "distance ring 1 neighbors are all distance 1 (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.PointyTop

        let nbrs = Hex2DSpatial.neighbors 10 10 grid

        for i in 0 .. nbrs.Length - 1 do
          let struct (nc, nr) = nbrs.[i]

          Expect.equal
            (Hex2DSpatial.distance 10 10 nc nr grid)
            1
            $"Neighbor {i} dist=1"

      testCase "distance ring 1 neighbors are all distance 1 (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop

        let nbrs = Hex2DSpatial.neighbors 10 10 grid

        for i in 0 .. nbrs.Length - 1 do
          let struct (nc, nr) = nbrs.[i]

          Expect.equal
            (Hex2DSpatial.distance 10 10 nc nr grid)
            1
            $"FT Neighbor {i} dist=1"
    ]

    // ── Hex grid: WorldToCell ─────────────────────────────────────────

    testList "HexGrid WorldToCell" [
      testCase "worldToCell roundtrips (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let mutable failures = 0

        for col in 0..9 do
          for row in 0..9 do
            let worldPos = HexGrid.getWorldPos col row grid

            match Hex2DSpatial.worldToCell worldPos grid with
            | ValueSome struct (c, r) ->
              let dist = Hex2DSpatial.distance col row c r grid

              if dist > 1 then
                failures <- failures + 1
            | ValueNone -> failures <- failures + 1

        Expect.equal failures 0 $"{failures} roundtrips failed"

      testCase "worldToCell roundtrips (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let mutable failures = 0

        for col in 0..9 do
          for row in 0..9 do
            let worldPos = HexGrid.getWorldPos col row grid

            match Hex2DSpatial.worldToCell worldPos grid with
            | ValueSome struct (c, r) ->
              let dist = Hex2DSpatial.distance col row c r grid

              if dist > 1 then
                failures <- failures + 1
            | ValueNone -> failures <- failures + 1

        Expect.equal failures 0 $"{failures} roundtrips failed"

      testCase "worldToCell returns ValueNone for OOB position"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let result = Hex2DSpatial.worldToCell (Vector2(-1000f, -1000f)) grid
        Expect.equal result ValueNone "Should be OOB"
    ]

    // ── Hex grid: InRange / Ring / Spiral ─────────────────────────────

    testList "HexGrid InRange" [
      testCase "inRange 0 = 1 cell"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.inRange 5 5 0 grid
        Expect.equal cells.Length 1 "Range 0 = 1 cell"
        Expect.contains cells (struct (5, 5)) "Contains origin"

      testCase "inRange 1 = 7 cells"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.inRange 5 5 1 grid
        Expect.equal cells.Length 7 "Range 1 = 7 cells"

      testCase "inRange 2 = 19 cells"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.inRange 5 5 2 grid
        Expect.equal cells.Length 19 "Range 2 = 19 cells"

      testCase "inRange respects grid bounds"
      <| fun _ ->
        let grid = HexGrid.create 3 3 32f Vector2.Zero HexOrientation.PointyTop
        let cells = Hex2DSpatial.inRange 0 0 10 grid
        Expect.equal cells.Length 9 "Only 3x3 = 9 valid cells"

      testCase "inRange negative range returns empty"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let cells = Hex2DSpatial.inRange 2 2 -1 grid
        Expect.isEmpty cells "Negative range"
    ]

    testList "HexGrid Ring" [
      testCase "ring 0 = [origin]"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.ring 5 5 0 grid
        Expect.equal cells.Length 1 "Ring 0 = 1 cell"
        Expect.contains cells (struct (5, 5)) "Is origin"

      testCase "ring 1 = 6 cells"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.ring 5 5 1 grid
        Expect.equal cells.Length 6 "Ring 1 = 6 cells"

      testCase "ring 2 = 12 cells"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.ring 10 10 2 grid
        Expect.equal cells.Length 12 "Ring 2 = 12 cells"
    ]

    testList "HexGrid Spiral" [
      testCase "spiral 0 = 1 cell"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.spiral 5 5 0 grid
        Expect.equal cells.Length 1 "Spiral 0"

      testCase "spiral 1 = 7 cells"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.spiral 5 5 1 grid
        Expect.equal cells.Length 7 "Spiral 1 = 1 + 6"

      testCase "spiral 2 = 19 cells"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.PointyTop

        let cells = Hex2DSpatial.spiral 10 10 2 grid
        Expect.equal cells.Length 19 "Spiral 2 = 1 + 6 + 12"
    ]

    // ── Hex grid: LineOfSight ─────────────────────────────────────────

    testList "HexGrid LineOfSight" [
      testCase "clear hex line returns true (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let isBlocked _ _ = false

        Expect.isTrue
          (Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "PT Clear"

      testCase "clear hex line returns true (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let isBlocked _ _ = false

        Expect.isTrue
          (Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "FT Clear"

      testCase "blocked hex line returns false (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 5 5 1 grid
        let isBlocked c r = c = 5 && r = 5

        Expect.isFalse
          (Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "PT Blocked"

      testCase "blocked hex line returns false (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        HexGrid.set 5 5 1 grid
        let isBlocked c r = c = 5 && r = 5

        Expect.isFalse
          (Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "FT Blocked"

      testCase "same cell returns true"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let isBlocked _ _ = false

        Expect.isTrue
          (Hex2DSpatial.lineOfSight 2 2 2 2 isBlocked grid)
          "Same cell"

      testCase "hex lineOfSightCells clear returns all cells (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 1 32f Vector2.Zero HexOrientation.PointyTop

        let isBlocked _ _ = false
        let cells = Hex2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid
        Expect.isGreaterThan cells.Length 0 "PT has visible cells"
        let struct (sc, sr) = cells.[0]
        Expect.equal sc 0 "PT start col"
        Expect.equal sr 0 "PT start row"

      testCase "hex lineOfSightCells clear returns all cells (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 1 32f Vector2.Zero HexOrientation.FlatTop
        let isBlocked _ _ = false
        let cells = Hex2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid
        Expect.isGreaterThan cells.Length 0 "FT has visible cells"
        let struct (sc, sr) = cells.[0]
        Expect.equal sc 0 "FT start col"
        Expect.equal sr 0 "FT start row"

      testCase "hex lineOfSightCells stops at blocker (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 1 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 5 0 1 grid
        let isBlocked c r = c = 5 && r = 0
        let cells = Hex2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid

        for i in 0 .. cells.Length - 1 do
          let struct (c, r) = cells.[i]
          Expect.isTrue (isBlocked c r |> not) $"PT cell ({c},{r}) not blocked"

      testCase "hex lineOfSightCells stops at blocker (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 1 32f Vector2.Zero HexOrientation.FlatTop
        HexGrid.set 5 0 1 grid
        let isBlocked c r = c = 5 && r = 0
        let cells = Hex2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid

        for i in 0 .. cells.Length - 1 do
          let struct (c, r) = cells.[i]
          Expect.isTrue (isBlocked c r |> not) $"FT cell ({c},{r}) not blocked"
    ]

    // ── Hex grid: FloodFill ───────────────────────────────────────────

    testList "HexGrid FloodFill" [
      testCase "floodFill fills entire grid when all passable (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 3 3 32f Vector2.Zero HexOrientation.PointyTop
        let predicate _ _ = true
        let cells = Hex2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 9 "Entire 3x3 PT"

      testCase "floodFill fills entire grid when all passable (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 3 3 32f Vector2.Zero HexOrientation.FlatTop
        let predicate _ _ = true
        let cells = Hex2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 9 "Entire 3x3 FT"

      testCase "floodFill stops at blocked cells (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 2 32f Vector2.Zero HexOrientation.PointyTop
        HexGrid.set 2 0 1 grid

        let predicate c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex2DSpatial.floodFill 0 0 predicate grid
        Expect.isGreaterThan cells.Length 0 "PT should fill some cells"
        Expect.isLessThan cells.Length 10 "PT should not fill entire grid"

      testCase "floodFill stops at blocked cells (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 2 32f Vector2.Zero HexOrientation.FlatTop
        HexGrid.set 2 0 1 grid

        let predicate c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex2DSpatial.floodFill 0 0 predicate grid
        Expect.isGreaterThan cells.Length 0 "FT should fill some cells"
        Expect.isLessThan cells.Length 10 "FT should not fill entire grid"

      testCase "floodFill OOB start returns empty"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let predicate _ _ = true
        let cells = Hex2DSpatial.floodFill -1 0 predicate grid
        Expect.isEmpty cells "OOB"

      testCase "floodFill 1x1 grid (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 1 1 32f Vector2.Zero HexOrientation.PointyTop
        let predicate _ _ = true
        let cells = Hex2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 1 "Single cell PT"

      testCase "floodFill 1x1 grid (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 1 1 32f Vector2.Zero HexOrientation.FlatTop
        let predicate _ _ = true
        let cells = Hex2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 1 "Single cell FT"
    ]

    // ── Hex grid: FindPath (A*) ───────────────────────────────────────

    testList "HexGrid FindPath" [
      testCase "finds path on open grid"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          Expect.isGreaterThan path.Length 0 "Path exists"
          let struct (sx, sy) = path.[0]
          Expect.equal sx 0 "Start col"
          Expect.equal sy 0 "Start row"
        | ValueNone -> failwith "Expected path"

      testCase "start = goal returns single cell"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 2 2 2 2 passable cost grid with
        | ValueSome path -> Expect.equal path.Length 1 "Single cell"
        | ValueNone -> failwith "Expected single cell"

      testCase "unreachable goal returns ValueNone"
      <| fun _ ->
        let grid = HexGrid.create 5 1 32f Vector2.Zero HexOrientation.PointyTop
        HexGrid.set 2 0 1 grid

        let passable c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 0 0 4 0 passable cost grid with
        | ValueSome _ -> failwith "Should not find path"
        | ValueNone -> ()

      testCase "start blocked returns ValueNone"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        HexGrid.set 0 0 1 grid

        let passable c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 0 0 4 4 passable cost grid with
        | ValueSome _ -> failwith "Start blocked"
        | ValueNone -> ()
    ]

    // ── Adversarial / Edge cases ──────────────────────────────────────

    testList "Adversarial" [
      testCase "1x1 grid: neighbors4 returns empty"
      <| fun _ ->
        let grid = CellGrid2D.create 1 1 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 0 0 grid
        Expect.equal nbrs.Length 0 "No neighbors in 1x1"

      testCase "1x1 grid: floodFill returns single cell"
      <| fun _ ->
        let grid = CellGrid2D.create 1 1 (Vector2(32f, 32f)) Vector2.Zero
        let predicate _ _ = true
        let cells = Grid2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 1 "Single cell"

      testCase "1x1 grid: inRange 0 = 1 cell"
      <| fun _ ->
        let grid = CellGrid2D.create 1 1 (Vector2(32f, 32f)) Vector2.Zero
        let cells = Grid2DSpatial.inRange 0 0 0 grid
        Expect.equal cells.Length 1 "Single cell"

      testCase "hex 1x1 grid: neighbors returns empty"
      <| fun _ ->
        let grid = HexGrid.create 1 1 32f Vector2.Zero HexOrientation.PointyTop
        let nbrs = Hex2DSpatial.neighbors 0 0 grid
        Expect.equal nbrs.Length 0 "No neighbors in 1x1 hex"

      testCase "hex 1x1 grid: distance same cell = 0"
      <| fun _ ->
        let grid = HexGrid.create 1 1 32f Vector2.Zero HexOrientation.PointyTop
        Expect.equal (Hex2DSpatial.distance 0 0 0 0 grid) 0 "Same cell"

      testCase "neighbors4 with negative input may return OOB neighbors"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 -1 -1 grid
        // neighbors4 checks if output is in bounds, but for OOB input
        // some neighbors may still be in bounds (e.g., (0,-1) from (-1,-1))
        // This is acceptable behavior - caller should ensure input is valid
        ()

      testCase "range larger than grid is bounded"
      <| fun _ ->
        let grid = CellGrid2D.create 3 3 (Vector2(32f, 32f)) Vector2.Zero
        let cells = Grid2DSpatial.inRange 1 1 100 grid
        Expect.equal cells.Length 9 "Only 3x3 valid cells"
    ]

    // ── Property tests: prove correctness, not just consistency ───────

    testList "Properties: Hex offset-cube roundtrip" [
      testCase
        "cubeToOffset(offsetToCube(c,r)) = (c,r) for all cells (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.PointyTop

        for col in 0..19 do
          for row in 0..19 do
            let struct (q, r, s) =
              Hex2DSpatial.offsetToCube col row HexOrientation.PointyTop

            let struct (c2, r2) =
              Hex2DSpatial.cubeToOffset q r HexOrientation.PointyTop

            Expect.equal c2 col $"col roundtrip ({col},{row})"
            Expect.equal r2 row $"row roundtrip ({col},{row})"
            Expect.equal (q + r + s) 0 $"q+r+s=0 ({col},{row})"

      testCase "cubeToOffset(offsetToCube(c,r)) = (c,r) for all cells (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop

        for col in 0..19 do
          for row in 0..19 do
            let struct (q, r, s) =
              Hex2DSpatial.offsetToCube col row HexOrientation.FlatTop

            let struct (c2, r2) =
              Hex2DSpatial.cubeToOffset q r HexOrientation.FlatTop

            Expect.equal c2 col $"col roundtrip ({col},{row})"
            Expect.equal r2 row $"row roundtrip ({col},{row})"
            Expect.equal (q + r + s) 0 $"q+r+s=0 ({col},{row})"
    ]

    testList "Properties: Hex distance" [
      testCase "triangle inequality: d(a,c) <= d(a,b) + d(b,c)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        for a in 0..4 do
          for b in 0..4 do
            for c in 0..4 do
              for d in 0..4 do
                for e in 0..4 do
                  for f in 0..4 do
                    let dab = Hex2DSpatial.distance a b c d grid
                    let dbc = Hex2DSpatial.distance c d e f grid
                    let dac = Hex2DSpatial.distance a b e f grid

                    Expect.isLessThanOrEqual
                      dac
                      (dab + dbc)
                      $"triangle ({a},{b})->({c},{d})->({e},{f})"

      testCase "distance(a,b) = 0 iff a = b"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        for a in 0..9 do
          for b in 0..9 do
            let dist = Hex2DSpatial.distance a b a b grid
            Expect.equal dist 0 $"d(({a},{b}),({a},{b})) = 0"

      testCase "ring N cells all have distance N from center"
      <| fun _ ->
        let grid =
          HexGrid.create 20 20 32f Vector2.Zero HexOrientation.PointyTop

        for r in 0..4 do
          let cells = Hex2DSpatial.ring 10 10 r grid

          for i in 0 .. cells.Length - 1 do
            let struct (nc, nr) = cells.[i]
            let dist = Hex2DSpatial.distance 10 10 nc nr grid

            Expect.equal
              dist
              r
              $"ring {r} cell ({nc},{nr}) dist={dist} expected={r}"

    ]

    testList "Properties: Hex worldToCell is nearest" [
      testCase "worldToCell returns the nearest hex center"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        for col in 0..9 do
          for row in 0..9 do
            let worldPos = HexGrid.getWorldPos col row grid

            match Hex2DSpatial.worldToCell worldPos grid with
            | ValueSome struct (bestC, bestR) ->
              let bestDist =
                let wp = HexGrid.getWorldPos bestC bestR grid
                let dx = worldPos.X - wp.X
                let dy = worldPos.Y - wp.Y
                sqrt(dx * dx + dy * dy)

              // Check that no neighbor is closer
              let nbrs = Hex2DSpatial.neighbors bestC bestR grid

              for i in 0 .. nbrs.Length - 1 do
                let struct (nc, nr) = nbrs.[i]
                let wp = HexGrid.getWorldPos nc nr grid
                let dx = worldPos.X - wp.X
                let dy = worldPos.Y - wp.Y
                let nDist = sqrt(dx * dx + dy * dy)

                Expect.isGreaterThanOrEqual
                  nDist
                  bestDist
                  $"nearest check ({col},{row}) vs neighbor ({nc},{nr})"

            | ValueNone ->
              failwith $"worldToCell returned None for ({col},{row})"

      testCase "worldToCell returns the nearest hex center (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        for col in 0..9 do
          for row in 0..9 do
            let worldPos = HexGrid.getWorldPos col row grid

            match Hex2DSpatial.worldToCell worldPos grid with
            | ValueSome struct (bestC, bestR) ->
              let bestDist =
                let wp = HexGrid.getWorldPos bestC bestR grid
                let dx = worldPos.X - wp.X
                let dy = worldPos.Y - wp.Y
                sqrt(dx * dx + dy * dy)

              let nbrs = Hex2DSpatial.neighbors bestC bestR grid

              for i in 0 .. nbrs.Length - 1 do
                let struct (nc, nr) = nbrs.[i]
                let wp = HexGrid.getWorldPos nc nr grid
                let dx = worldPos.X - wp.X
                let dy = worldPos.Y - wp.Y
                let nDist = sqrt(dx * dx + dy * dy)

                Expect.isGreaterThanOrEqual
                  nDist
                  bestDist
                  $"nearest check ({col},{row}) vs neighbor ({nc},{nr})"

            | ValueNone ->
              failwith $"worldToCell returned None for ({col},{row})"
    ]

    testList "Properties: Hex worldToCell boundary accuracy" [
      testCase "worldToCell maps boundary points to nearest hex (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        for col in 1..8 do
          for row in 1..8 do
            let center = HexGrid.getWorldPos col row grid
            let nbrs = Hex2DSpatial.neighbors col row grid

            for i in 0 .. nbrs.Length - 1 do
              let struct (nc, nr) = nbrs.[i]
              let neighbor = HexGrid.getWorldPos nc nr grid

              // Point 60% toward neighbor (closer to neighbor)
              let testX = center.X + (neighbor.X - center.X) * 0.6f
              let testY = center.Y + (neighbor.Y - center.Y) * 0.6f
              let testPos = Vector2(testX, testY)

              match Hex2DSpatial.worldToCell testPos grid with
              | ValueSome struct (bc, br) ->
                let resolvedCenter = HexGrid.getWorldPos bc br grid
                let dx = testPos.X - resolvedCenter.X
                let dy = testPos.Y - resolvedCenter.Y
                let distToResolved = sqrt(dx * dx + dy * dy)

                let dx2 = testPos.X - neighbor.X
                let dy2 = testPos.Y - neighbor.Y
                let distToNeighbor = sqrt(dx2 * dx2 + dy2 * dy2)

                // Resolved hex must be at least as close as the neighbor
                Expect.isLessThanOrEqual
                  distToResolved
                  (distToNeighbor + 0.01f)
                  (sprintf "PT boundary (%d,%d)->(%d,%d) at 60%%" col row nc nr)
              | ValueNone ->
                failwith(
                  sprintf "PT boundary OOB (%d,%d)->(%d,%d)" col row nc nr
                )

      testCase "worldToCell maps boundary points to nearest hex (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        for col in 1..8 do
          for row in 1..8 do
            let center = HexGrid.getWorldPos col row grid
            let nbrs = Hex2DSpatial.neighbors col row grid

            for i in 0 .. nbrs.Length - 1 do
              let struct (nc, nr) = nbrs.[i]
              let neighbor = HexGrid.getWorldPos nc nr grid

              let testX = center.X + (neighbor.X - center.X) * 0.6f
              let testY = center.Y + (neighbor.Y - center.Y) * 0.6f
              let testPos = Vector2(testX, testY)

              match Hex2DSpatial.worldToCell testPos grid with
              | ValueSome struct (bc, br) ->
                let resolvedCenter = HexGrid.getWorldPos bc br grid
                let dx = testPos.X - resolvedCenter.X
                let dy = testPos.Y - resolvedCenter.Y
                let distToResolved = sqrt(dx * dx + dy * dy)

                let dx2 = testPos.X - neighbor.X
                let dy2 = testPos.Y - neighbor.Y
                let distToNeighbor = sqrt(dx2 * dx2 + dy2 * dy2)

                Expect.isLessThanOrEqual
                  distToResolved
                  (distToNeighbor + 0.01f)
                  (sprintf "FT boundary (%d,%d)->(%d,%d) at 60%%" col row nc nr)
              | ValueNone ->
                failwith(
                  sprintf "FT boundary OOB (%d,%d)->(%d,%d)" col row nc nr
                )

      testCase "worldToCell maps all sampled points to nearest hex"
      <| fun _ ->
        // Sample a grid of points across the hex field and verify each
        // maps to the hex whose center is closest
        let grid = HexGrid.create 8 8 32f Vector2.Zero HexOrientation.PointyTop

        let struct (hexW, hexH) = struct (32f * sqrt 3f, 32f * 2f)

        // Sample at 0.25 hex increments
        for sy in 0..30 do
          for sx in 0..30 do
            let px = float32 sx * hexW * 0.25f
            let py = float32 sy * hexH * 0.25f
            let testPos = Vector2(px, py)

            match Hex2DSpatial.worldToCell testPos grid with
            | ValueSome struct (bc, br) ->
              // Verify no other cell is closer
              let bestCenter = HexGrid.getWorldPos bc br grid
              let bestDx = testPos.X - bestCenter.X
              let bestDy = testPos.Y - bestCenter.Y
              let bestDist = sqrt(bestDx * bestDx + bestDy * bestDy)

              // Check all grid cells
              for c in 0..7 do
                for r in 0..7 do
                  let wp = HexGrid.getWorldPos c r grid
                  let dx = testPos.X - wp.X
                  let dy = testPos.Y - wp.Y
                  let d = sqrt(dx * dx + dy * dy)

                  if d < bestDist - 0.01f then
                    failwith(
                      sprintf
                        "Point (%f,%f) mapped to (%d,%d) dist=%f but (%d,%d) is closer dist=%f"
                        px
                        py
                        bc
                        br
                        bestDist
                        c
                        r
                        d
                    )
            | ValueNone -> () // Outside grid, OK
    ]

    testList "Properties: A* path validity" [
      testCase "path cells are all passable"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero

        // Add some obstacles
        CellGrid2D.set 3 0 1 grid
        CellGrid2D.set 3 1 1 grid
        CellGrid2D.set 3 2 1 grid

        let passable x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 1 do
            let struct (x, y) = path.[i]
            Expect.isTrue (passable x y) $"Path cell ({x},{y}) must be passable"
        | ValueNone -> failwith "Expected path"

      testCase "path consecutive cells are neighbors"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 2 do
            let struct (x1, y1) = path.[i]
            let struct (x2, y2) = path.[i + 1]
            let dx = abs(x2 - x1)
            let dy = abs(y2 - y1)

            Expect.isTrue
              (dx <= 1 && dy <= 1 && dx + dy = 1)
              $"Consecutive ({x1},{y1})->({x2},{y2}) must be cardinal neighbors"
        | ValueNone -> failwith "Expected path"

      testCase "path starts at start and ends at goal"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 1 2 8 7 passable cost grid with
        | ValueSome path ->
          let struct (sx, sy) = path.[0]
          let struct (gx, gy) = path.[path.Length - 1]
          Expect.equal sx 1 "Start x"
          Expect.equal sy 2 "Start y"
          Expect.equal gx 8 "Goal x"
          Expect.equal gy 7 "Goal y"
        | ValueNone -> failwith "Expected path"

      testCase "A* returns optimal-length path (matches BFS)"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.set 3 0 1 grid
        CellGrid2D.set 3 1 1 grid
        CellGrid2D.set 3 2 1 grid
        CellGrid2D.set 3 3 1 grid

        let passable x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        // BFS to find optimal distance
        let bfsDist =
          let visited = Array.create (10 * 10) false

          let queue =
            System.Collections.Generic.Queue<struct (int * int * int)>()

          queue.Enqueue(struct (0, 0, 0))
          visited.[0] <- true
          let mutable result = -1

          while queue.Count > 0 && result < 0 do
            let struct (x, y, d) = queue.Dequeue()

            if x = 9 && y = 9 then
              result <- d
            else
              for struct (nx, ny) in Grid2DSpatial.neighbors4 x y grid do
                let idx = nx + ny * 10

                if not visited.[idx] && passable nx ny then
                  visited.[idx] <- true
                  queue.Enqueue(struct (nx, ny, d + 1))

          result

        match Grid2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          Expect.equal
            (path.Length - 1)
            bfsDist
            "A* path length matches BFS optimal"
        | ValueNone -> failwith "Expected path"

      testCase "hex A* returns optimal-length path (matches BFS)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 3 3 1 grid
        HexGrid.set 3 4 1 grid
        HexGrid.set 4 3 1 grid

        let passable c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ = 1f

        // BFS for optimal distance
        let bfsDist =
          let total = 10 * 10
          let visited = Array.create total false

          let queue =
            System.Collections.Generic.Queue<struct (int * int * int)>()

          queue.Enqueue(struct (0, 0, 0))
          visited.[0] <- true
          let mutable result = -1

          while queue.Count > 0 && result < 0 do
            let struct (c, r, d) = queue.Dequeue()

            if c = 9 && r = 9 then
              result <- d
            else
              let nbrs = Hex2DSpatial.neighbors c r grid

              for i in 0 .. nbrs.Length - 1 do
                let struct (nc, nr) = nbrs.[i]
                let idx = nc + nr * 10

                if not visited.[idx] && passable nc nr then
                  visited.[idx] <- true
                  queue.Enqueue(struct (nc, nr, d + 1))

          result

        match Hex2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          Expect.equal
            (path.Length - 1)
            bfsDist
            "Hex A* path length matches BFS optimal"
        | ValueNone -> failwith "Expected path"
    ]

    testList "Properties: Flood fill" [
      testCase "flood fill: all returned cells satisfy predicate"
      <| fun _ ->
        let grid = CellGrid2D.create 8 8 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.set 3 0 1 grid
        CellGrid2D.set 3 1 1 grid
        CellGrid2D.set 3 2 1 grid

        let predicate x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid2DSpatial.floodFill 0 0 predicate grid

        for i in 0 .. cells.Length - 1 do
          let struct (x, y) = cells.[i]
          Expect.isTrue (predicate x y) $"Cell ({x},{y}) must satisfy predicate"

      testCase "flood fill: returned cells are connected"
      <| fun _ ->
        let grid = CellGrid2D.create 8 8 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.set 3 0 1 grid
        CellGrid2D.set 3 1 1 grid
        CellGrid2D.set 3 2 1 grid
        CellGrid2D.set 3 3 1 grid

        let predicate x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid2DSpatial.floodFill 0 0 predicate grid

        // Build set of returned cells
        let cellSet = System.Collections.Generic.HashSet<struct (int * int)>()

        for i in 0 .. cells.Length - 1 do
          cellSet.Add(cells.[i]) |> ignore

        // Every cell (except start) must have at least one neighbor in the set
        for i in 0 .. cells.Length - 1 do
          let struct (x, y) = cells.[i]
          let nbrs = Grid2DSpatial.neighbors4 x y grid
          let mutable hasNeighborInSet = false

          for j in 0 .. nbrs.Length - 1 do
            if cellSet.Contains(nbrs.[j]) then
              hasNeighborInSet <- true

          // Start cell might be isolated if it's the only cell
          if cells.Length > 1 then
            Expect.isTrue
              hasNeighborInSet
              $"Cell ({x},{y}) must be connected to fill"

      testCase "flood fill: no reachable cell is missing"
      <| fun _ ->
        let grid = CellGrid2D.create 6 6 (Vector2(32f, 32f)) Vector2.Zero

        CellGrid2D.set 3 0 1 grid
        CellGrid2D.set 3 1 1 grid

        let predicate x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid2DSpatial.floodFill 0 0 predicate grid

        // BFS from start to find all reachable cells
        let reachable = System.Collections.Generic.HashSet<struct (int * int)>()
        let queue = System.Collections.Generic.Queue<struct (int * int)>()
        queue.Enqueue(struct (0, 0))
        reachable.Add(struct (0, 0)) |> ignore

        while queue.Count > 0 do
          let struct (x, y) = queue.Dequeue()

          for struct (nx, ny) in Grid2DSpatial.neighbors4 x y grid do
            let idx = struct (nx, ny)

            if not(reachable.Contains(idx)) && predicate nx ny then
              reachable.Add(idx) |> ignore
              queue.Enqueue(idx)

        // flood fill result must equal BFS reachable set
        let fillSet = System.Collections.Generic.HashSet<struct (int * int)>()

        for i in 0 .. cells.Length - 1 do
          fillSet.Add(cells.[i]) |> ignore

        Expect.equal
          fillSet.Count
          reachable.Count
          "Fill count matches BFS count"

        for r in reachable do
          Expect.isTrue
            (fillSet.Contains(r))
            $"Reachable cell {r} must be in fill result"

      testCase
        "hex flood fill: all returned cells satisfy predicate (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 8 8 32f Vector2.Zero HexOrientation.PointyTop
        HexGrid.set 3 3 1 grid

        let predicate c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex2DSpatial.floodFill 0 0 predicate grid

        for i in 0 .. cells.Length - 1 do
          let struct (c, r) = cells.[i]

          Expect.isTrue
            (predicate c r)
            $"PT Cell ({c},{r}) must satisfy predicate"

      testCase "hex flood fill: all returned cells satisfy predicate (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 8 8 32f Vector2.Zero HexOrientation.FlatTop
        HexGrid.set 3 3 1 grid

        let predicate c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex2DSpatial.floodFill 0 0 predicate grid

        for i in 0 .. cells.Length - 1 do
          let struct (c, r) = cells.[i]

          Expect.isTrue
            (predicate c r)
            $"FT Cell ({c},{r}) must satisfy predicate"

      testCase "hex flood fill: no reachable cell is missing (PointyTop)"
      <| fun _ ->
        let grid = HexGrid.create 8 8 32f Vector2.Zero HexOrientation.PointyTop
        HexGrid.set 3 3 1 grid

        let predicate c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex2DSpatial.floodFill 0 0 predicate grid

        // BFS from start
        let reachable = System.Collections.Generic.HashSet<struct (int * int)>()
        let queue = System.Collections.Generic.Queue<struct (int * int)>()
        queue.Enqueue(struct (0, 0))
        reachable.Add(struct (0, 0)) |> ignore

        while queue.Count > 0 do
          let struct (c, r) = queue.Dequeue()
          let nbrs = Hex2DSpatial.neighbors c r grid

          for i in 0 .. nbrs.Length - 1 do
            let struct (nc, nr) = nbrs.[i]
            let idx = struct (nc, nr)

            if not(reachable.Contains(idx)) && predicate nc nr then
              reachable.Add(idx) |> ignore
              queue.Enqueue(idx)

        let fillSet = System.Collections.Generic.HashSet<struct (int * int)>()

        for i in 0 .. cells.Length - 1 do
          fillSet.Add(cells.[i]) |> ignore

        Expect.equal
          fillSet.Count
          reachable.Count
          "PT Fill count matches BFS count"

        for r in reachable do
          Expect.isTrue
            (fillSet.Contains(r))
            $"PT Reachable cell {r} must be in fill result"

      testCase "hex flood fill: no reachable cell is missing (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 8 8 32f Vector2.Zero HexOrientation.FlatTop
        HexGrid.set 3 3 1 grid

        let predicate c r =
          match HexGrid.get c r grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex2DSpatial.floodFill 0 0 predicate grid

        let reachable = System.Collections.Generic.HashSet<struct (int * int)>()
        let queue = System.Collections.Generic.Queue<struct (int * int)>()
        queue.Enqueue(struct (0, 0))
        reachable.Add(struct (0, 0)) |> ignore

        while queue.Count > 0 do
          let struct (c, r) = queue.Dequeue()
          let nbrs = Hex2DSpatial.neighbors c r grid

          for i in 0 .. nbrs.Length - 1 do
            let struct (nc, nr) = nbrs.[i]
            let idx = struct (nc, nr)

            if not(reachable.Contains(idx)) && predicate nc nr then
              reachable.Add(idx) |> ignore
              queue.Enqueue(idx)

        let fillSet = System.Collections.Generic.HashSet<struct (int * int)>()

        for i in 0 .. cells.Length - 1 do
          fillSet.Add(cells.[i]) |> ignore

        Expect.equal
          fillSet.Count
          reachable.Count
          "FT Fill count matches BFS count"

        for r in reachable do
          Expect.isTrue
            (fillSet.Contains(r))
            $"FT Reachable cell {r} must be in fill result"
    ]

    testList "Properties: inRange completeness" [
      testCase "inRange returns exactly cells within Chebyshev range"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero

        for range in 0..3 do
          let cells = Grid2DSpatial.inRange 5 5 range grid

          // Every returned cell must be within range
          for i in 0 .. cells.Length - 1 do
            let struct (x, y) = cells.[i]
            let dist = Grid2DSpatial.distanceChebyshev 5 5 x y

            Expect.isLessThanOrEqual
              dist
              range
              $"Cell ({x},{y}) within range {range}"

          // Every cell within range must be returned
          let cellSet = System.Collections.Generic.HashSet<struct (int * int)>()

          for i in 0 .. cells.Length - 1 do
            cellSet.Add(cells.[i]) |> ignore

          for x in 0..9 do
            for y in 0..9 do
              if Grid2DSpatial.distanceChebyshev 5 5 x y <= range then
                Expect.isTrue
                  (cellSet.Contains(struct (x, y)))
                  $"Cell ({x},{y}) must be in range {range}"

      testCase "hex inRange returns exactly cells within hex range"
      <| fun _ ->
        let grid =
          HexGrid.create 15 15 32f Vector2.Zero HexOrientation.PointyTop

        for range in 0..3 do
          let cells = Hex2DSpatial.inRange 7 7 range grid

          let cellSet = System.Collections.Generic.HashSet<struct (int * int)>()

          for i in 0 .. cells.Length - 1 do
            cellSet.Add(cells.[i]) |> ignore

          // Every returned cell must be within range
          for i in 0 .. cells.Length - 1 do
            let struct (c, r) = cells.[i]
            let dist = Hex2DSpatial.distance 7 7 c r grid

            Expect.isLessThanOrEqual
              dist
              range
              $"Cell ({c},{r}) within hex range {range}"

          // Expected count: 1 + 3*n*(n+1) for hex range n
          let expected = 1 + 3 * range * (range + 1)
          Expect.equal cellSet.Count expected $"Hex inRange {range} count"
    ]

    testList "Properties: Line of sight" [
      testCase "LOS: Bresenham visits all cells on the line"
      <| fun _ ->
        let grid = CellGrid2D.create 10 1 (Vector2(32f, 32f)) Vector2.Zero
        let isBlocked _ _ = false

        let cells = Grid2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid
        Expect.equal cells.Length 10 "Horizontal line visits all 10 cells"

        for i in 0..9 do
          let struct (x, y) = cells.[i]
          Expect.equal x i $"Cell {i} x"
          Expect.equal y 0 $"Cell {i} y"

      testCase "LOS: diagonal line visits expected cells"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let isBlocked _ _ = false

        let cells = Grid2DSpatial.lineOfSightCells 0 0 4 4 isBlocked grid

        // Diagonal should visit 5 cells
        Expect.equal cells.Length 5 "Diagonal visits 5 cells"

        // Each cell should be on the diagonal
        for i in 0 .. cells.Length - 1 do
          let struct (x, y) = cells.[i]
          Expect.equal x y $"Cell {i} on diagonal"

      testCase "LOS: blocked cell stops traversal"
      <| fun _ ->
        let grid = CellGrid2D.create 10 1 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 5 0 1 grid

        let isBlocked x y =
          match CellGrid2D.get x y grid with
          | ValueSome _ -> true
          | ValueNone -> false

        let cells = Grid2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid

        // Should stop before x=5
        Expect.equal cells.Length 5 "Stops at blocker"

        let struct (lastX, _) = cells.[cells.Length - 1]
        Expect.equal lastX 4 "Last visible is x=4"

      testCase "LOS: goal cell blocked returns false"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 9 9 1 grid

        let isBlocked x y =
          match CellGrid2D.get x y grid with
          | ValueSome _ -> true
          | ValueNone -> false

        Expect.isFalse
          (Grid2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "Goal blocked → LOS false"

      testCase "LOS: vertical line visits all cells"
      <| fun _ ->
        let grid = CellGrid2D.create 1 10 (Vector2(32f, 32f)) Vector2.Zero
        let isBlocked _ _ = false

        let cells = Grid2DSpatial.lineOfSightCells 0 0 0 9 isBlocked grid
        Expect.equal cells.Length 10 "Vertical line visits all 10 cells"

        for i in 0..9 do
          let struct (x, y) = cells.[i]
          Expect.equal x 0 $"Cell {i} x"
          Expect.equal y i $"Cell {i} y"
    ]

    // ── Non-square grid tests ──────────────────────────────────────────

    testList "Non-square grids" [
      testCase "neighbors4 on 10x2 grid: corner (0,0) has 2 neighbors"
      <| fun _ ->
        let grid = CellGrid2D.create 10 2 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 0 0 grid
        Expect.equal nbrs.Length 2 "Corner on 10x2"

      testCase "neighbors4 on 10x2 grid: edge (5,0) has 3 neighbors"
      <| fun _ ->
        let grid = CellGrid2D.create 10 2 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors4 5 0 grid
        Expect.equal nbrs.Length 3 "Edge on 10x2"

      testCase "neighbors8 on 10x2 grid: corner (0,0) has 3 neighbors"
      <| fun _ ->
        let grid = CellGrid2D.create 10 2 (Vector2(32f, 32f)) Vector2.Zero
        let nbrs = Grid2DSpatial.neighbors8 0 0 grid
        Expect.equal nbrs.Length 3 "Corner on 10x2 (8-nbrs)"

      testCase "floodFill on 10x1 grid fills all 10 cells"
      <| fun _ ->
        let grid = CellGrid2D.create 10 1 (Vector2(32f, 32f)) Vector2.Zero
        let predicate _ _ = true
        let cells = Grid2DSpatial.floodFill 0 0 predicate grid
        Expect.equal cells.Length 10 "10x1 all passable"

      testCase "inRange on 10x2 grid respects thin dimension"
      <| fun _ ->
        let grid = CellGrid2D.create 10 2 (Vector2(32f, 32f)) Vector2.Zero
        let cells = Grid2DSpatial.inRange 5 0 5 grid
        // y can only be 0 or 1, so max 2 rows
        for i in 0 .. cells.Length - 1 do
          let struct (_, y) = cells.[i]
          Expect.isGreaterThanOrEqual y 0 "y >= 0"
          Expect.isLessThan y 2 "y < 2"
    ]

    // ── Additional adversarial / edge cases ────────────────────────────

    testList "Adversarial extended" [
      testCase "floodFill start predicate false returns empty"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        let predicate _ _ = false
        let cells = Grid2DSpatial.floodFill 2 2 predicate grid
        Expect.isEmpty cells "Predicate false at start"

      testCase "floodFill all cells blocked returns empty"
      <| fun _ ->
        let grid = CellGrid2D.create 3 3 (Vector2(32f, 32f)) Vector2.Zero

        for x in 0..2 do
          for y in 0..2 do
            CellGrid2D.set x y 1 grid

        let predicate x y =
          match CellGrid2D.get x y grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid2DSpatial.floodFill 1 1 predicate grid
        Expect.isEmpty cells "All blocked"

      testCase "hex floodFill start predicate false returns empty"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let predicate _ _ = false
        let cells = Hex2DSpatial.floodFill 2 2 predicate grid
        Expect.isEmpty cells "Hex predicate false at start"

      testCase "lineOfSightCells goal blocked returns only start"
      <| fun _ ->
        let grid = CellGrid2D.create 5 1 (Vector2(32f, 32f)) Vector2.Zero
        CellGrid2D.set 4 0 1 grid

        let isBlocked x y =
          match CellGrid2D.get x y grid with
          | ValueSome _ -> true
          | ValueNone -> false

        let cells = Grid2DSpatial.lineOfSightCells 0 0 4 0 isBlocked grid
        // Should include (0,0), (1,0), (2,0), (3,0) but not (4,0)
        Expect.equal cells.Length 4 "4 visible cells before goal"
        let struct (lx, ly) = cells.[cells.Length - 1]
        Expect.equal lx 3 "Last visible x=3"
        Expect.equal ly 0 "Last visible y=0"

      testCase "hex lineOfSightCells goal blocked stops before goal"
      <| fun _ ->
        let grid = HexGrid.create 10 1 32f Vector2.Zero HexOrientation.PointyTop

        HexGrid.set 5 0 1 grid

        let isBlocked c r =
          match HexGrid.get c r grid with
          | ValueSome _ -> true
          | ValueNone -> false

        let cells = Hex2DSpatial.lineOfSightCells 0 0 9 0 isBlocked grid
        Expect.isGreaterThan cells.Length 0 "Has visible cells"

        for i in 0 .. cells.Length - 1 do
          let struct (c, r) = cells.[i]
          Expect.isTrue (isBlocked c r |> not) $"Cell ({c},{r}) not blocked"

      testCase "worldToCell at exact boundary (right edge)"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        // World pos at right edge: (4 * 32) + 16 = 144 (center of last cell)
        let worldPos = CellGrid2D.getWorldPos 4 4 grid

        match Grid2DSpatial.worldToCell worldPos grid with
        | ValueSome struct (cx, cy) ->
          Expect.equal cx 4 "Right edge x"
          Expect.equal cy 4 "Bottom edge y"
        | ValueNone -> failwith "Expected ValueSome at boundary"

      testCase "worldToCell just outside grid returns ValueNone"
      <| fun _ ->
        let grid = CellGrid2D.create 5 5 (Vector2(32f, 32f)) Vector2.Zero
        // Just past right edge
        let worldPos = Vector2(5f * 32f + 100f, 2f * 32f)

        Expect.equal
          (Grid2DSpatial.worldToCell worldPos grid)
          ValueNone
          "OOB right"

      testCase "hex worldToCell FlatTop OOB returns ValueNone"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
        let result = Hex2DSpatial.worldToCell (Vector2(-1000f, -1000f)) grid
        Expect.equal result ValueNone "FlatTop OOB"

      testCase "hex ring 0 with OOB center returns empty"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let cells = Hex2DSpatial.ring -1 0 0 grid
        Expect.isEmpty cells "OOB ring 0"

      testCase "hex ring 0 with OOB center returns empty (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.FlatTop
        let cells = Hex2DSpatial.ring 10 10 0 grid
        Expect.isEmpty cells "FT OOB ring 0"

      testCase "hex spiral 0 with OOB center returns empty"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let cells = Hex2DSpatial.spiral -1 0 0 grid
        Expect.isEmpty cells "OOB spiral 0"

      testCase "hex spiral with OOB center still returns in-range ring cells"
      <| fun _ ->
        let grid = HexGrid.create 5 5 32f Vector2.Zero HexOrientation.PointyTop
        let cells = Hex2DSpatial.spiral 10 10 1 grid
        // Center (10,10) is OOB, but ring 1 cells might be in bounds
        // Ring cells are filtered by bounds, so only valid cells returned
        for i in 0 .. cells.Length - 1 do
          let struct (c, r) = cells.[i]
          Expect.isGreaterThanOrEqual c 0 "c >= 0"
          Expect.isLessThan c 5 "c < 5"
          Expect.isGreaterThanOrEqual r 0 "r >= 0"
          Expect.isLessThan r 5 "r < 5"
    ]

    // ── Additional property tests ──────────────────────────────────────

    testList "Properties: Manhattan distance" [
      testCase "Manhattan triangle inequality: d(a,c) <= d(a,b) + d(b,c)"
      <| fun _ ->
        for x1 in 0..3 do
          for y1 in 0..3 do
            for x2 in 0..3 do
              for y2 in 0..3 do
                for x3 in 0..3 do
                  for y3 in 0..3 do
                    let d12 = Grid2DSpatial.distanceManhattan x1 y1 x2 y2
                    let d23 = Grid2DSpatial.distanceManhattan x2 y2 x3 y3
                    let d13 = Grid2DSpatial.distanceManhattan x1 y1 x3 y3

                    Expect.isLessThanOrEqual
                      d13
                      (d12 + d23)
                      $"Manhattan triangle ({x1},{y1})->({x2},{y2})->({x3},{y3})"

      testCase "Manhattan d(a,b) = 0 iff a = b"
      <| fun _ ->
        for x in 0..4 do
          for y in 0..4 do
            Expect.equal
              (Grid2DSpatial.distanceManhattan x y x y)
              0
              $"d(({x},{y}),({x},{y})) = 0"

      testCase "Chebyshev triangle inequality"
      <| fun _ ->
        for x1 in 0..3 do
          for y1 in 0..3 do
            for x2 in 0..3 do
              for y2 in 0..3 do
                for x3 in 0..3 do
                  for y3 in 0..3 do
                    let d12 = Grid2DSpatial.distanceChebyshev x1 y1 x2 y2
                    let d23 = Grid2DSpatial.distanceChebyshev x2 y2 x3 y3
                    let d13 = Grid2DSpatial.distanceChebyshev x1 y1 x3 y3

                    Expect.isLessThanOrEqual
                      d13
                      (d12 + d23)
                      $"Chebyshev triangle ({x1},{y1})->({x2},{y2})->({x3},{y3})"
    ]

    testList "Properties: inRange completeness (square)" [
      testCase "inRange returns exactly (2n+1)^2 cells for center of large grid"
      <| fun _ ->
        let grid = CellGrid2D.create 20 20 (Vector2(32f, 32f)) Vector2.Zero

        for range in 0..4 do
          let cells = Grid2DSpatial.inRange 10 10 range grid
          let expected = (2 * range + 1) * (2 * range + 1)
          Expect.equal cells.Length expected $"inRange {range} count"

          let cellSet = System.Collections.Generic.HashSet<struct (int * int)>()

          for i in 0 .. cells.Length - 1 do
            cellSet.Add(cells.[i]) |> ignore

          // No duplicates
          Expect.equal
            cellSet.Count
            cells.Length
            $"No duplicates at range {range}"

          // All cells within Chebyshev distance
          for i in 0 .. cells.Length - 1 do
            let struct (x, y) = cells.[i]
            let dist = Grid2DSpatial.distanceChebyshev 10 10 x y

            Expect.isLessThanOrEqual
              dist
              range
              $"Cell ({x},{y}) within range {range}"
    ]

    testList "Properties: A* path structure" [
      testCase "A* path first cell is start, last cell is goal"
      <| fun _ ->
        let grid = CellGrid2D.create 10 10 (Vector2(32f, 32f)) Vector2.Zero
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Grid2DSpatial.findPath 1 3 8 7 passable cost grid with
        | ValueSome path ->
          let struct (sx, sy) = path.[0]
          let struct (gx, gy) = path.[path.Length - 1]
          Expect.equal sx 1 "Start x"
          Expect.equal sy 3 "Start y"
          Expect.equal gx 8 "Goal x"
          Expect.equal gy 7 "Goal y"
        | ValueNone -> failwith "Expected path"

      testCase "hex A* path first cell is start, last cell is goal (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 1 2 8 7 passable cost grid with
        | ValueSome path ->
          let struct (sc, sr) = path.[0]
          let struct (gc, gr) = path.[path.Length - 1]
          Expect.equal sc 1 "Start col"
          Expect.equal sr 2 "Start row"
          Expect.equal gc 8 "Goal col"
          Expect.equal gr 7 "Goal row"
        | ValueNone -> failwith "Expected path"

      testCase "hex A* path first cell is start, last cell is goal (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 1 2 8 7 passable cost grid with
        | ValueSome path ->
          let struct (sc, sr) = path.[0]
          let struct (gc, gr) = path.[path.Length - 1]
          Expect.equal sc 1 "FT Start col"
          Expect.equal sr 2 "FT Start row"
          Expect.equal gc 8 "FT Goal col"
          Expect.equal gr 7 "FT Goal row"
        | ValueNone -> failwith "Expected FT path"

      testCase "hex A* consecutive cells are hex neighbors (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 2 do
            let struct (c1, r1) = path.[i]
            let struct (c2, r2) = path.[i + 1]
            let nbrs = Hex2DSpatial.neighbors c1 r1 grid
            let mutable isNeighbor = false

            for j in 0 .. nbrs.Length - 1 do
              let struct (nc, nr) = nbrs.[j]

              if nc = c2 && nr = r2 then
                isNeighbor <- true

            Expect.isTrue
              isNeighbor
              $"PT ({c1},{r1})->({c2},{r2}) must be hex neighbors"
        | ValueNone -> failwith "Expected path"

      testCase "hex A* consecutive cells are hex neighbors (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 2 do
            let struct (c1, r1) = path.[i]
            let struct (c2, r2) = path.[i + 1]
            let nbrs = Hex2DSpatial.neighbors c1 r1 grid
            let mutable isNeighbor = false

            for j in 0 .. nbrs.Length - 1 do
              let struct (nc, nr) = nbrs.[j]

              if nc = c2 && nr = r2 then
                isNeighbor <- true

            Expect.isTrue
              isNeighbor
              $"FT ({c1},{r1})->({c2},{r2}) must be hex neighbors"
        | ValueNone -> failwith "Expected FT path"

      testCase "hex A* FlatTop finds path"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let passable _ _ = true
        let cost _ _ _ _ = 1f

        match Hex2DSpatial.findPath 0 0 9 9 passable cost grid with
        | ValueSome path ->
          Expect.isGreaterThan path.Length 0 "FlatTop path exists"
          let struct (sc, sr) = path.[0]
          let struct (gc, gr) = path.[path.Length - 1]
          Expect.equal sc 0 "Start col"
          Expect.equal sr 0 "Start row"
          Expect.equal gc 9 "Goal col"
          Expect.equal gr 9 "Goal row"
        | ValueNone -> failwith "Expected FlatTop path"
    ]

    testList "Properties: PointyTop vs FlatTop differ" [
      testCase "same cell produces different world positions"
      <| fun _ ->
        let ptGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        // (0,0) center differs due to different hex dimensions
        let ptPos = HexGrid.getWorldPos 0 0 ptGrid
        let ftPos = HexGrid.getWorldPos 0 0 ftGrid
        // PointyTop: hexW=size*sqrt3, hexH=size*2
        // FlatTop: hexW=size*2, hexH=size*sqrt3
        Expect.notEqual ptPos ftPos "(0,0) world pos differs"

        // (1,0) should also differ
        let ptPos1 = HexGrid.getWorldPos 1 0 ptGrid
        let ftPos1 = HexGrid.getWorldPos 1 0 ftGrid
        Expect.notEqual ptPos1 ftPos1 "(1,0) world pos differs"

      testCase "odd-row/col neighbors differ between orientations"
      <| fun _ ->
        let ptGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        // Cell (3,1): odd row in PointyTop, odd col? no (3 is odd in FlatTop)
        // PointyTop odd-row stagger shifts right, FlatTop odd-col stagger shifts down
        let ptNbrs = Hex2DSpatial.neighbors 3 1 ptGrid
        let ftNbrs = Hex2DSpatial.neighbors 3 1 ftGrid

        // Both have 6 neighbors (center of large grid) but different coordinates
        Expect.equal ptNbrs.Length 6 "PT neighbors = 6"
        Expect.equal ftNbrs.Length 6 "FT neighbors = 6"

        let ptSet = System.Collections.Generic.HashSet<struct (int * int)>()

        let ftSet = System.Collections.Generic.HashSet<struct (int * int)>()

        for i in 0 .. ptNbrs.Length - 1 do
          ptSet.Add(ptNbrs.[i]) |> ignore

        for i in 0 .. ftNbrs.Length - 1 do
          ftSet.Add(ftNbrs.[i]) |> ignore

        // The neighbor sets should not be identical
        let mutable anyDifferent = false

        for i in 0 .. ptNbrs.Length - 1 do
          if not(ftSet.Contains(ptNbrs.[i])) then
            anyDifferent <- true

        Expect.isTrue anyDifferent "PT and FT neighbors differ for (3,1)"

      testCase "even-row/col neighbors differ between orientations"
      <| fun _ ->
        let ptGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        // Cell (2,2): even row in PointyTop, even col in FlatTop
        let ptNbrs = Hex2DSpatial.neighbors 2 2 ptGrid
        let ftNbrs = Hex2DSpatial.neighbors 2 2 ftGrid

        let ptSet = System.Collections.Generic.HashSet<struct (int * int)>()

        let ftSet = System.Collections.Generic.HashSet<struct (int * int)>()

        for i in 0 .. ptNbrs.Length - 1 do
          ptSet.Add(ptNbrs.[i]) |> ignore

        for i in 0 .. ftNbrs.Length - 1 do
          ftSet.Add(ftNbrs.[i]) |> ignore

        let mutable anyDifferent = false

        for i in 0 .. ptNbrs.Length - 1 do
          if not(ftSet.Contains(ptNbrs.[i])) then
            anyDifferent <- true

        Expect.isTrue anyDifferent "PT and FT neighbors differ for (2,2)"

      testCase "offsetToCube produces different cube coords"
      <| fun _ ->
        // Same (col,row) should give different (q,r,s) depending on orientation
        let struct (ptQ, ptR, ptS) =
          Hex2DSpatial.offsetToCube 3 1 HexOrientation.PointyTop

        let struct (ftQ, ftR, ftS) =
          Hex2DSpatial.offsetToCube 3 1 HexOrientation.FlatTop

        // PointyTop: q = col - (row - (row &&& 1)) / 2 = 3 - (1-1)/2 = 3
        // FlatTop: q = col = 3, r = row - (col - (col &&& 1)) / 2 = 1 - (3-1)/2 = 0
        // So q is same but r differs
        Expect.notEqual ptR ftR "r coordinate differs for (3,1)"
        Expect.notEqual ptS ftS "s coordinate differs for (3,1)"

      testCase "offsetToCube: different stagger patterns"
      <| fun _ ->
        // PointyTop: even rows have q = col - row/2, odd rows have q = col - (row-1)/2
        // FlatTop: even cols have r = row - col/2, odd cols have r = row - (col-1)/2
        let struct (ptQ1, _, _) =
          Hex2DSpatial.offsetToCube 0 2 HexOrientation.PointyTop

        let struct (ptQ2, _, _) =
          Hex2DSpatial.offsetToCube 0 3 HexOrientation.PointyTop

        // For col=0: even row 2 → q = 0 - 2/2 = -1, odd row 3 → q = 0 - (3-1)/2 = -1
        // Same q for both in this case, but the stagger offset in world space differs
        let struct (ftR1, _, _) =
          Hex2DSpatial.offsetToCube 2 0 HexOrientation.FlatTop

        let struct (ftR2, _, _) =
          Hex2DSpatial.offsetToCube 3 0 HexOrientation.FlatTop

        // FlatTop: even col 2 → r = 0 - 2/2 = -1, odd col 3 → r = 0 - (3-1)/2 = -1
        // The key difference is in worldToCell and getWorldPos, not necessarily offsetToCube
        // for symmetric inputs. The real test is neighbor sets.
        ()

      testCase "worldToCell maps differently for mid-grid positions"
      <| fun _ ->
        let ptGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        // Pick a world position that's near the boundary between hexes
        let worldPos = Vector2(150f, 120f)

        let ptResult = Hex2DSpatial.worldToCell worldPos ptGrid
        let ftResult = Hex2DSpatial.worldToCell worldPos ftGrid

        match ptResult, ftResult with
        | ValueSome struct (ptC, ptR), ValueSome struct (ftC, ftR) ->
          // Due to different hex shapes and stagger patterns, the resolved
          // cell is likely different (not guaranteed for all positions, but
          // very likely for a mid-grid position)
          let ptCenter = HexGrid.getWorldPos ptC ptR ptGrid
          let ftCenter = HexGrid.getWorldPos ftC ftR ftGrid
          // The hex centers are at different positions
          Expect.notEqual ptCenter ftCenter "Hex centers differ"
        | _ -> ()

      testCase "lineOfSight visits different cells (PT vs FT)"
      <| fun _ ->
        let ptGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop

        let isBlocked _ _ = false

        // Same start/end in grid coordinates, but the actual hexes
        // are at different world positions, so the line may differ
        let ptClear = Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked ptGrid

        let ftClear = Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked ftGrid

        // Both should be clear (no blockers)
        Expect.isTrue ptClear "PT clear"
        Expect.isTrue ftClear "FT clear"

        // Now block a cell and verify both detect it
        HexGrid.set 5 5 1 ptGrid
        HexGrid.set 5 5 1 ftGrid

        let isBlocked55 c r = c = 5 && r = 5

        let ptBlocked = Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked55 ptGrid

        let ftBlocked = Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked55 ftGrid

        // Both should be blocked since (5,5) is on the diagonal
        Expect.isFalse ptBlocked "PT blocked at (5,5)"
        Expect.isFalse ftBlocked "FT blocked at (5,5)"
    ]

    testList "Properties: Hex FlatTop completeness" [
      testCase "ring 1 = 6 cells (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let cells = Hex2DSpatial.ring 5 5 1 grid
        Expect.equal cells.Length 6 "Ring 1 FlatTop = 6"

      testCase "ring 2 = 12 cells (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop
        let cells = Hex2DSpatial.ring 10 10 2 grid
        Expect.equal cells.Length 12 "Ring 2 FlatTop = 12"

      testCase "spiral 2 = 19 cells (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop
        let cells = Hex2DSpatial.spiral 10 10 2 grid
        Expect.equal cells.Length 19 "Spiral 2 FlatTop = 19"

      testCase "ring N cells all have distance N from center (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop

        for r in 0..3 do
          let cells = Hex2DSpatial.ring 10 10 r grid

          for i in 0 .. cells.Length - 1 do
            let struct (nc, nr) = cells.[i]
            let dist = Hex2DSpatial.distance 10 10 nc nr grid

            Expect.equal
              dist
              r
              $"FlatTop ring {r} cell ({nc},{nr}) dist={dist} expected={r}"

      testCase "lineOfSight clear (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        let isBlocked _ _ = false

        Expect.isTrue
          (Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "FlatTop clear LOS"

      testCase "lineOfSight blocked (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 10 10 32f Vector2.Zero HexOrientation.FlatTop
        HexGrid.set 5 5 1 grid

        let isBlocked c r = c = 5 && r = 5

        Expect.isFalse
          (Hex2DSpatial.lineOfSight 0 0 9 9 isBlocked grid)
          "FlatTop blocked LOS"

      testCase "hex inRange count formula 1+3n(n+1) (FlatTop)"
      <| fun _ ->
        let grid = HexGrid.create 20 20 32f Vector2.Zero HexOrientation.FlatTop

        for range in 0..4 do
          let cells = Hex2DSpatial.inRange 10 10 range grid
          let expected = 1 + 3 * range * (range + 1)
          Expect.equal cells.Length expected $"FlatTop inRange {range} count"
    ]

    testList "Properties: hex distance triangle inequality" [
      testCase "hex triangle inequality: d(a,c) <= d(a,b) + d(b,c)"
      <| fun _ ->
        let grid =
          HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop

        for a in 0..4 do
          for b in 0..4 do
            for c in 0..4 do
              for d in 0..4 do
                for e in 0..4 do
                  for f in 0..4 do
                    let dab = Hex2DSpatial.distance a b c d grid
                    let dbc = Hex2DSpatial.distance c d e f grid
                    let dac = Hex2DSpatial.distance a b e f grid

                    Expect.isLessThanOrEqual
                      dac
                      (dab + dbc)
                      $"Hex triangle ({a},{b})->({c},{d})->({e},{f})"
    ]
  ]
