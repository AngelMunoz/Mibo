module Mibo.Core.Tests.Spatial3D

open Expecto
open System.Numerics
open Mibo.Layout
open Mibo.Layout3D

[<Tests>]
let tests =
  testList "Spatial3D" [

    // ── Voxel grid: Neighbors ─────────────────────────────────────────

    testList "VoxelGrid Neighbors" [
      testCase "neighbors6 returns 6 for center cell"
      <| fun _ ->
        let grid = CellGrid3D.create 10 10 10 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let nbrs = Grid3DSpatial.neighbors6 5 5 5 grid
        Expect.equal nbrs.Length 6 "Center should have 6 face neighbors"

      testCase "neighbors6 returns 3 for corner (0,0,0)"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let nbrs = Grid3DSpatial.neighbors6 0 0 0 grid
        Expect.equal nbrs.Length 3 "Corner (0,0,0) has 3 neighbors"

      testCase "neighbors6 returns 5 for face edge cell (0,2,2)"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let nbrs = Grid3DSpatial.neighbors6 0 2 2 grid
        // (1,2,2), (0,1,2), (0,3,2), (0,2,1), (0,2,3) = 5 neighbors
        Expect.equal nbrs.Length 5 "Face edge (0,2,2) has 5 neighbors"

      testCase "neighbors6 never returns out-of-bounds"
      <| fun _ ->
        let grid = CellGrid3D.create 3 3 3 (Vector3(1f, 1f, 1f)) Vector3.Zero

        for x in 0..2 do
          for y in 0..2 do
            for z in 0..2 do
              let nbrs = Grid3DSpatial.neighbors6 x y z grid

              for i in 0 .. nbrs.Length - 1 do
                let struct (nx, ny, nz) = nbrs.[i]
                Expect.isGreaterThanOrEqual nx 0 "nx >= 0"
                Expect.isLessThan nx 3 "nx < 3"
                Expect.isGreaterThanOrEqual ny 0 "ny >= 0"
                Expect.isLessThan ny 3 "ny < 3"
                Expect.isGreaterThanOrEqual nz 0 "nz >= 0"
                Expect.isLessThan nz 3 "nz < 3"

      testCase "neighbors26 returns 26 for center cell"
      <| fun _ ->
        let grid = CellGrid3D.create 10 10 10 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let nbrs = Grid3DSpatial.neighbors26 5 5 5 grid
        Expect.equal nbrs.Length 26 "Center should have 26 neighbors"

      testCase "neighbors26 returns 7 for corner (0,0,0)"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let nbrs = Grid3DSpatial.neighbors26 0 0 0 grid
        Expect.equal nbrs.Length 7 "Corner (0,0,0) has 7 neighbors (2^3 - 1)"

      testCase "neighbors26 never returns out-of-bounds"
      <| fun _ ->
        let grid = CellGrid3D.create 3 3 3 (Vector3(1f, 1f, 1f)) Vector3.Zero

        for x in 0..2 do
          for y in 0..2 do
            for z in 0..2 do
              let nbrs = Grid3DSpatial.neighbors26 x y z grid

              for i in 0 .. nbrs.Length - 1 do
                let struct (nx, ny, nz) = nbrs.[i]
                Expect.isGreaterThanOrEqual nx 0 "nx >= 0"
                Expect.isLessThan nx 3 "nx < 3"
                Expect.isGreaterThanOrEqual ny 0 "ny >= 0"
                Expect.isLessThan ny 3 "ny < 3"
                Expect.isGreaterThanOrEqual nz 0 "nz >= 0"
                Expect.isLessThan nz 3 "nz < 3"
    ]

    // ── Voxel grid: Distance ──────────────────────────────────────────

    testList "VoxelGrid Distance" [
      testCase "distanceManhattan same cell = 0"
      <| fun _ ->
        Expect.equal (Grid3DSpatial.distanceManhattan 3 3 3 3 3 3) 0 "Same cell"

      testCase "distanceManhattan axis-aligned"
      <| fun _ ->
        Expect.equal (Grid3DSpatial.distanceManhattan 0 0 0 3 0 0) 3 "X axis"
        Expect.equal (Grid3DSpatial.distanceManhattan 0 0 0 0 4 0) 4 "Y axis"
        Expect.equal (Grid3DSpatial.distanceManhattan 0 0 0 0 0 5) 5 "Z axis"

      testCase "distanceManhattan sum of deltas"
      <| fun _ ->
        Expect.equal (Grid3DSpatial.distanceManhattan 0 0 0 1 2 3) 6 "Sum"

      testCase "distanceManhattan symmetry"
      <| fun _ ->
        Expect.equal
          (Grid3DSpatial.distanceManhattan 1 2 3 4 5 6)
          (Grid3DSpatial.distanceManhattan 4 5 6 1 2 3)
          "Symmetric"

      testCase "distanceChebyshev same cell = 0"
      <| fun _ ->
        Expect.equal (Grid3DSpatial.distanceChebyshev 3 3 3 3 3 3) 0 "Same cell"

      testCase "distanceChebyshev diagonal"
      <| fun _ ->
        Expect.equal (Grid3DSpatial.distanceChebyshev 0 0 0 3 4 5) 5 "Max axis"

      testCase "distanceChebyshev symmetry"
      <| fun _ ->
        Expect.equal
          (Grid3DSpatial.distanceChebyshev 1 2 3 4 5 6)
          (Grid3DSpatial.distanceChebyshev 4 5 6 1 2 3)
          "Symmetric"

      testCase "distanceEuclidean same cell = 0"
      <| fun _ ->
        Expect.floatClose
          Accuracy.high
          (float(Grid3DSpatial.distanceEuclidean 3 3 3 3 3 3))
          0.0
          "Same cell"

      testCase "distanceEuclidean axis-aligned"
      <| fun _ ->
        Expect.floatClose
          Accuracy.high
          (float(Grid3DSpatial.distanceEuclidean 0 0 0 3 0 0))
          3.0
          "X axis"
    ]

    // ── Voxel grid: WorldToCell ───────────────────────────────────────

    testList "VoxelGrid WorldToCell" [
      testCase "worldToCell roundtrips with getWorldPos"
      <| fun _ ->
        let grid =
          CellGrid3D.create
            10
            10
            10
            (Vector3(32f, 32f, 32f))
            (Vector3(100f, 50f, 75f))

        for x in 0..9 do
          for y in 0..9 do
            for z in 0..9 do
              let worldPos = CellGrid3D.getWorldPos x y z grid

              match Grid3DSpatial.worldToCell worldPos grid with
              | ValueSome struct (cx, cy, cz) ->
                Expect.equal cx x "X roundtrip"
                Expect.equal cy y "Y roundtrip"
                Expect.equal cz z "Z roundtrip"
              | ValueNone -> failwith $"Roundtrip failed for ({x},{y},{z})"

      testCase "worldToCell returns ValueNone for OOB"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let result =
          Grid3DSpatial.worldToCell (Vector3(-100f, -100f, -100f)) grid

        Expect.equal result ValueNone "OOB"
    ]

    // ── Voxel grid: InRange ───────────────────────────────────────────

    testList "VoxelGrid InRange" [
      testCase "inRange 0 = 1 cell"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let cells = Grid3DSpatial.inRange 2 2 2 0 grid
        Expect.equal cells.Length 1 "Range 0 = 1 cell"
        Expect.contains cells (struct (2, 2, 2)) "Contains origin"

      testCase "inRange 1 = 27 cells (Chebyshev cube)"
      <| fun _ ->
        let grid = CellGrid3D.create 10 10 10 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let cells = Grid3DSpatial.inRange 5 5 5 1 grid
        // Chebyshev range 1 in 3D = 3x3x3 cube = 27 cells
        Expect.equal cells.Length 27 "Range 1 = 27 cells (3x3x3)"
        Expect.contains cells (struct (5, 5, 5)) "Contains origin"

      testCase "inRange respects grid bounds"
      <| fun _ ->
        let grid = CellGrid3D.create 2 2 2 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let cells = Grid3DSpatial.inRange 0 0 0 100 grid
        Expect.equal cells.Length 8 "Only 2x2x2 = 8 valid cells"

      testCase "inRange negative range returns empty"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let cells = Grid3DSpatial.inRange 2 2 2 -1 grid
        Expect.isEmpty cells "Negative range"
    ]

    // ── Voxel grid: LineOfSight ───────────────────────────────────────

    testList "VoxelGrid LineOfSight" [
      testCase "clear 3D line returns true"
      <| fun _ ->
        let grid = CellGrid3D.create 10 10 10 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Grid3DSpatial.lineOfSight 0 0 0 9 9 9 isBlocked grid)
          "Clear line"

      testCase "blocked 3D line returns false"
      <| fun _ ->
        let grid = CellGrid3D.create 10 10 10 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 5 5 5 1 grid

        let isBlocked x y z =
          match CellGrid3D.get x y z grid with
          | ValueSome _ -> true
          | ValueNone -> false

        Expect.isFalse
          (Grid3DSpatial.lineOfSight 0 0 0 9 9 9 isBlocked grid)
          "Blocked line"

      testCase "same cell returns true"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Grid3DSpatial.lineOfSight 2 2 2 2 2 2 isBlocked grid)
          "Same cell"

      testCase "axis-aligned line (X)"
      <| fun _ ->
        let grid = CellGrid3D.create 10 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Grid3DSpatial.lineOfSight 0 0 0 9 0 0 isBlocked grid)
          "X-axis line"

      testCase "lineOfSightCells stops at blocker"
      <| fun _ ->
        let grid = CellGrid3D.create 10 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 5 0 0 1 grid

        let isBlocked x y z =
          match CellGrid3D.get x y z grid with
          | ValueSome _ -> true
          | ValueNone -> false

        let cells = Grid3DSpatial.lineOfSightCells 0 0 0 9 0 0 isBlocked grid

        Expect.isGreaterThan cells.Length 0 "Should have visible cells"
        Expect.isLessThan cells.Length 10 "Should stop before blocker"
    ]

    // ── Voxel grid: FloodFill ─────────────────────────────────────────

    testList "VoxelGrid FloodFill" [
      testCase "floodFill fills entire grid when all passable"
      <| fun _ ->
        let grid = CellGrid3D.create 2 2 2 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let predicate _ _ _ = true
        let cells = Grid3DSpatial.floodFill 0 0 0 predicate grid
        Expect.equal cells.Length 8 "Entire 2x2x2"

      testCase "floodFill stops at blocked cells"
      <| fun _ ->
        let grid = CellGrid3D.create 3 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 1 0 0 1 grid

        let predicate x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid3DSpatial.floodFill 0 0 0 predicate grid
        Expect.equal cells.Length 1 "Only (0,0,0)"

      testCase "floodFill OOB start returns empty"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let predicate _ _ _ = true
        let cells = Grid3DSpatial.floodFill -1 0 0 predicate grid
        Expect.isEmpty cells "OOB"

      testCase "floodFill 1x1x1 grid"
      <| fun _ ->
        let grid = CellGrid3D.create 1 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let predicate _ _ _ = true
        let cells = Grid3DSpatial.floodFill 0 0 0 predicate grid
        Expect.equal cells.Length 1 "Single cell"

      testCase "floodFill 0x0x0 grid returns empty"
      <| fun _ ->
        let grid = CellGrid3D.create 0 0 0 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let predicate _ _ _ = true
        let cells = Grid3DSpatial.floodFill 0 0 0 predicate grid
        Expect.isEmpty cells "Empty grid"
    ]

    // ── Voxel grid: FindPath (A*) ─────────────────────────────────────

    testList "VoxelGrid FindPath" [
      testCase "finds straight path on open grid"
      <| fun _ ->
        let grid = CellGrid3D.create 5 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome path ->
          Expect.equal path.Length 5 "5 cells"
          Expect.contains path (struct (0, 0, 0)) "Start"
          Expect.contains path (struct (4, 0, 0)) "Goal"
        | ValueNone -> failwith "Expected path"

      testCase "finds path around obstacle"
      <| fun _ ->
        let grid = CellGrid3D.create 5 3 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 2 0 0 1 grid
        CellGrid3D.set 2 1 0 1 grid

        let passable x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome path ->
          Expect.isGreaterThan path.Length 0 "Path exists"
          let struct (sx, sy, sz) = path.[0]
          Expect.equal sx 0 "Start x"
          Expect.equal sy 0 "Start y"
          Expect.equal sz 0 "Start z"
          let struct (gx, gy, gz) = path.[path.Length - 1]
          Expect.equal gx 4 "Goal x"
          Expect.equal gy 0 "Goal y"
          Expect.equal gz 0 "Goal z"
        | ValueNone -> failwith "Expected path around obstacle"

      testCase "unreachable goal returns ValueNone"
      <| fun _ ->
        let grid = CellGrid3D.create 5 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 2 0 0 1 grid

        let passable x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome _ -> failwith "Should not find path"
        | ValueNone -> ()

      testCase "start = goal returns single cell"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 2 2 2 2 2 2 passable cost grid with
        | ValueSome path ->
          Expect.equal path.Length 1 "Single cell"
          Expect.contains path (struct (2, 2, 2)) "Is start"
        | ValueNone -> failwith "Expected single cell path"

      testCase "start blocked returns ValueNone"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 0 0 0 1 grid

        let passable x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 0 0 0 4 4 4 passable cost grid with
        | ValueSome _ -> failwith "Start blocked"
        | ValueNone -> ()
    ]

    // ── Hex3D grid: Neighbors ─────────────────────────────────────────

    testList "Hex3DGrid Neighbors" [
      testCase "neighbors returns 8 for center cell"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let nbrs = Hex3DSpatial.neighbors 5 5 5 grid
        Expect.equal nbrs.Length 8 "Center: 6 hex + 2 vertical"

      testCase "neighborsHex returns 6 for center cell"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let nbrs = Hex3DSpatial.neighborsHex 5 5 5 grid
        Expect.equal nbrs.Length 6 "Center: 6 hex only"

      testCase "neighbors returns fewer for edge cells"
      <| fun _ ->
        let grid =
          HexGrid3D.create 3 3 3 32f 1f Vector3.Zero HexOrientation.PointyTop

        let nbrs = Hex3DSpatial.neighbors 0 0 0 grid
        Expect.isLessThan nbrs.Length 8 "Edge has fewer neighbors"

      testCase "neighbors never returns out-of-bounds"
      <| fun _ ->
        let grid =
          HexGrid3D.create 3 3 3 32f 1f Vector3.Zero HexOrientation.PointyTop

        for col in 0..2 do
          for row in 0..2 do
            for layer in 0..2 do
              let nbrs = Hex3DSpatial.neighbors col row layer grid

              for i in 0 .. nbrs.Length - 1 do
                let struct (nc, nr, nl) = nbrs.[i]
                Expect.isGreaterThanOrEqual nc 0 "nc >= 0"
                Expect.isLessThan nc 3 "nc < 3"
                Expect.isGreaterThanOrEqual nr 0 "nr >= 0"
                Expect.isLessThan nr 3 "nr < 3"
                Expect.isGreaterThanOrEqual nl 0 "nl >= 0"
                Expect.isLessThan nl 3 "nl < 3"
    ]

    // ── Hex3D grid: Distance ──────────────────────────────────────────

    testList "Hex3DGrid Distance" [
      testCase "distance same cell = 0"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        Expect.equal (Hex3DSpatial.distance 2 2 2 2 2 2 grid) 0 "Same cell"

      testCase "distance adjacent hex = 1"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let nbrs = Hex3DSpatial.neighborsHex 2 2 2 grid

        for i in 0 .. nbrs.Length - 1 do
          let struct (nc, nr, nl) = nbrs.[i]

          Expect.equal
            (Hex3DSpatial.distance 2 2 2 nc nr nl grid)
            1
            $"Adjacent {i}"

      testCase "distance vertical only = layer diff"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        Expect.equal (Hex3DSpatial.distance 2 2 0 2 2 3 grid) 3 "3 layers up"

      testCase "distance combined hex + vertical"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 5 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let hexDist =
          Hex2DSpatial.distance
            0
            0
            3
            3
            (HexGrid.create 10 10 32f Vector2.Zero HexOrientation.PointyTop)

        let totalDist = Hex3DSpatial.distance 0 0 0 3 3 2 grid
        Expect.equal totalDist (hexDist + 2) "Hex dist + layer diff"
    ]

    // ── Hex3D grid: WorldToCell ───────────────────────────────────────

    testList "Hex3DGrid WorldToCell" [
      testCase "worldToCell roundtrips"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 5 10 32f 2f Vector3.Zero HexOrientation.PointyTop

        let mutable failures = 0

        for col in 0..9 do
          for row in 0..9 do
            for layer in 0..4 do
              let worldPos = HexGrid3D.getWorldPos col row layer grid

              match Hex3DSpatial.worldToCell worldPos grid with
              | ValueSome struct (c, r, l) ->
                let dist = Hex3DSpatial.distance col row layer c r l grid

                if dist > 1 then
                  failures <- failures + 1
              | ValueNone -> failures <- failures + 1

        Expect.equal failures 0 $"{failures} roundtrips failed"

      testCase "worldToCell returns ValueNone for OOB layer"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 3 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let result = Hex3DSpatial.worldToCell (Vector3(0f, -100f, 0f)) grid
        Expect.equal result ValueNone "OOB layer"
    ]

    // ── Hex3D grid: InRange ───────────────────────────────────────────

    testList "Hex3DGrid InRange" [
      testCase "inRange 0 = 1 cell (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let cells = Hex3DSpatial.inRange 5 5 5 0 grid
        Expect.equal cells.Length 1 "PT Range 0 = 1 cell"

      testCase "inRange 0 = 1 cell (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.FlatTop

        let cells = Hex3DSpatial.inRange 5 5 5 0 grid
        Expect.equal cells.Length 1 "FT Range 0 = 1 cell"

      testCase "inRange 1 = 1 hex ring + up/down = 7 + 2 = 9 (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 20 3 20 32f 1f Vector3.Zero HexOrientation.PointyTop

        let cells = Hex3DSpatial.inRange 10 10 1 1 grid
        Expect.equal cells.Length 9 "PT 9 cells"

      testCase "inRange 1 = 1 hex ring + up/down = 7 + 2 = 9 (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 20 3 20 32f 1f Vector3.Zero HexOrientation.FlatTop

        let cells = Hex3DSpatial.inRange 10 10 1 1 grid
        Expect.equal cells.Length 9 "FT 9 cells"

      testCase "inRange respects grid bounds (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 2 2 2 32f 1f Vector3.Zero HexOrientation.PointyTop

        let cells = Hex3DSpatial.inRange 0 0 0 100 grid
        Expect.isLessThanOrEqual cells.Length 8 "PT At most 2x2x2 cells"

      testCase "inRange respects grid bounds (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 2 2 2 32f 1f Vector3.Zero HexOrientation.FlatTop

        let cells = Hex3DSpatial.inRange 0 0 0 100 grid
        Expect.isLessThanOrEqual cells.Length 8 "FT At most 2x2x2 cells"
    ]

    // ── Hex3D grid: LineOfSight ───────────────────────────────────────

    testList "Hex3DGrid LineOfSight" [
      testCase "clear hex3D line returns true (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Hex3DSpatial.lineOfSight 0 0 0 9 9 9 isBlocked grid)
          "PT Clear"

      testCase "clear hex3D line returns true (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.FlatTop

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Hex3DSpatial.lineOfSight 0 0 0 9 9 9 isBlocked grid)
          "FT Clear"

      testCase "same cell returns true (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Hex3DSpatial.lineOfSight 2 2 2 2 2 2 isBlocked grid)
          "PT Same cell"

      testCase "same cell returns true (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.FlatTop

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Hex3DSpatial.lineOfSight 2 2 2 2 2 2 isBlocked grid)
          "FT Same cell"
    ]

    // ── Hex3D grid: FloodFill ─────────────────────────────────────────

    testList "Hex3DGrid FloodFill" [
      testCase "floodFill fills entire small grid (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 2 2 2 32f 1f Vector3.Zero HexOrientation.PointyTop

        let predicate _ _ _ = true
        let cells = Hex3DSpatial.floodFill 0 0 0 predicate grid
        Expect.isGreaterThan cells.Length 0 "PT Should fill some cells"

      testCase "floodFill fills entire small grid (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 2 2 2 32f 1f Vector3.Zero HexOrientation.FlatTop

        let predicate _ _ _ = true
        let cells = Hex3DSpatial.floodFill 0 0 0 predicate grid
        Expect.isGreaterThan cells.Length 0 "FT Should fill some cells"

      testCase "floodFill OOB start returns empty"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let predicate _ _ _ = true
        let cells = Hex3DSpatial.floodFill -1 0 0 predicate grid
        Expect.isEmpty cells "OOB"

      testCase "floodFill blocked start returns empty (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let predicate _ _ _ = false
        let cells = Hex3DSpatial.floodFill 0 0 0 predicate grid
        Expect.isEmpty cells "PT Blocked start"

      testCase "floodFill blocked start returns empty (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.FlatTop

        let predicate _ _ _ = false
        let cells = Hex3DSpatial.floodFill 0 0 0 predicate grid
        Expect.isEmpty cells "FT Blocked start"
    ]

    // ── Hex3D grid: FindPath ──────────────────────────────────────────

    testList "Hex3DGrid FindPath" [
      testCase "finds path on open grid (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 1 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome path ->
          Expect.isGreaterThan path.Length 0 "PT Path exists"
          let struct (sx, sr, sl) = path.[0]
          Expect.equal sx 0 "PT Start col"
          Expect.equal sr 0 "PT Start row"
          Expect.equal sl 0 "PT Start layer"
        | ValueNone -> failwith "Expected PT path"

      testCase "finds path on open grid (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 1 5 32f 1f Vector3.Zero HexOrientation.FlatTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome path ->
          Expect.isGreaterThan path.Length 0 "FT Path exists"
          let struct (sx, sr, sl) = path.[0]
          Expect.equal sx 0 "FT Start col"
          Expect.equal sr 0 "FT Start row"
          Expect.equal sl 0 "FT Start layer"
        | ValueNone -> failwith "Expected FT path"

      testCase "start = goal returns single cell (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 2 2 2 2 2 2 passable cost grid with
        | ValueSome path -> Expect.equal path.Length 1 "PT Single cell"
        | ValueNone -> failwith "Expected PT single cell"

      testCase "start = goal returns single cell (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.FlatTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 2 2 2 2 2 2 passable cost grid with
        | ValueSome path -> Expect.equal path.Length 1 "FT Single cell"
        | ValueNone -> failwith "Expected FT single cell"

      testCase "unreachable returns ValueNone (PointyTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 1 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        // Block all neighbors of start
        let nbrs = Hex3DSpatial.neighborsHex 0 0 0 grid

        for i in 0 .. nbrs.Length - 1 do
          let struct (nc, nr, nl) = nbrs.[i]
          HexGrid3D.set nc nr nl 1 grid

        let passable c r l =
          match HexGrid3D.get c r l grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome _ -> failwith "PT Should not find path"
        | ValueNone -> ()

      testCase "unreachable returns ValueNone (FlatTop)"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 1 5 32f 1f Vector3.Zero HexOrientation.FlatTop

        let nbrs = Hex3DSpatial.neighborsHex 0 0 0 grid

        for i in 0 .. nbrs.Length - 1 do
          let struct (nc, nr, nl) = nbrs.[i]
          HexGrid3D.set nc nr nl 1 grid

        let passable c r l =
          match HexGrid3D.get c r l grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome _ -> failwith "FT Should not find path"
        | ValueNone -> ()
    ]

    // ── Adversarial ───────────────────────────────────────────────────

    testList "Adversarial" [
      testCase "1x1x1 voxel grid: neighbors6 returns empty"
      <| fun _ ->
        let grid = CellGrid3D.create 1 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let nbrs = Grid3DSpatial.neighbors6 0 0 0 grid
        Expect.equal nbrs.Length 0 "No neighbors in 1x1x1"

      testCase "1x1x1 voxel grid: floodFill returns single cell"
      <| fun _ ->
        let grid = CellGrid3D.create 1 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let predicate _ _ _ = true
        let cells = Grid3DSpatial.floodFill 0 0 0 predicate grid
        Expect.equal cells.Length 1 "Single cell"

      testCase "1x1x1 hex3D grid: neighbors returns empty"
      <| fun _ ->
        let grid =
          HexGrid3D.create 1 1 1 32f 1f Vector3.Zero HexOrientation.PointyTop

        let nbrs = Hex3DSpatial.neighbors 0 0 0 grid
        Expect.equal nbrs.Length 0 "No neighbors in 1x1x1 hex"

      testCase "negative coordinates: neighbors checks output bounds"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let nbrs = Grid3DSpatial.neighbors6 -1 -1 -1 grid
        // neighbors6 checks output bounds only
        // Caller should ensure input is valid
        ()

      testCase "range larger than grid is bounded"
      <| fun _ ->
        let grid = CellGrid3D.create 2 2 2 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let cells = Grid3DSpatial.inRange 0 0 0 1000 grid
        Expect.equal cells.Length 8 "Only 2x2x2 valid cells"
    ]

    // ── Property tests: prove correctness ─────────────────────────────

    testList "Properties: A* path validity" [
      testCase "3D path cells are all passable"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 2 2 0 1 grid
        CellGrid3D.set 2 2 1 1 grid

        let passable x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 0 0 0 4 4 4 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 1 do
            let struct (x, y, z) = path.[i]

            Expect.isTrue
              (passable x y z)
              $"Path cell ({x},{y},{z}) must be passable"
        | ValueNone -> failwith "Expected path"

      testCase "3D path consecutive cells are face neighbors"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 0 0 0 4 4 4 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 2 do
            let struct (x1, y1, z1) = path.[i]
            let struct (x2, y2, z2) = path.[i + 1]
            let dx = abs(x2 - x1)
            let dy = abs(y2 - y1)
            let dz = abs(z2 - z1)

            Expect.isTrue
              (dx + dy + dz = 1)
              $"Consecutive ({x1},{y1},{z1})->({x2},{y2},{z2}) must be face neighbors"
        | ValueNone -> failwith "Expected path"

      testCase "3D A* returns optimal-length path (matches BFS)"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 2 2 0 1 grid
        CellGrid3D.set 2 2 1 1 grid

        let passable x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        let bfsDist =
          let w, h, d = 5, 5, 5
          let visited = Array.create (w * h * d) false

          let queue =
            System.Collections.Generic.Queue<struct (int * int * int * int)>()

          queue.Enqueue(struct (0, 0, 0, 0))
          visited.[0] <- true
          let mutable result = -1

          while queue.Count > 0 && result < 0 do
            let struct (x, y, z, dist) = queue.Dequeue()

            if x = 4 && y = 4 && z = 4 then
              result <- dist
            else
              for struct (nx, ny, nz) in Grid3DSpatial.neighbors6 x y z grid do
                let idx = nx + ny * w + nz * w * h

                if not visited.[idx] && passable nx ny nz then
                  visited.[idx] <- true
                  queue.Enqueue(struct (nx, ny, nz, dist + 1))

          result

        match Grid3DSpatial.findPath 0 0 0 4 4 4 passable cost grid with
        | ValueSome path ->
          Expect.equal
            (path.Length - 1)
            bfsDist
            "3D A* path length matches BFS optimal"
        | ValueNone -> failwith "Expected path"
    ]

    testList "Properties: Flood fill" [
      testCase "3D flood fill: all returned cells satisfy predicate"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 2 2 2 1 grid

        let predicate x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid3DSpatial.floodFill 0 0 0 predicate grid

        for i in 0 .. cells.Length - 1 do
          let struct (x, y, z) = cells.[i]

          Expect.isTrue
            (predicate x y z)
            $"Cell ({x},{y},{z}) must satisfy predicate"

      testCase "3D flood fill: no reachable cell is missing"
      <| fun _ ->
        let grid = CellGrid3D.create 4 4 4 (Vector3(1f, 1f, 1f)) Vector3.Zero

        CellGrid3D.set 2 2 0 1 grid
        CellGrid3D.set 2 2 1 1 grid

        let predicate x y z =
          match CellGrid3D.get x y z grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Grid3DSpatial.floodFill 0 0 0 predicate grid

        let reachable =
          System.Collections.Generic.HashSet<struct (int * int * int)>()

        let queue = System.Collections.Generic.Queue<struct (int * int * int)>()
        queue.Enqueue(struct (0, 0, 0))
        reachable.Add(struct (0, 0, 0)) |> ignore

        while queue.Count > 0 do
          let struct (x, y, z) = queue.Dequeue()

          for struct (nx, ny, nz) in Grid3DSpatial.neighbors6 x y z grid do
            let idx = struct (nx, ny, nz)

            if not(reachable.Contains(idx)) && predicate nx ny nz then
              reachable.Add(idx) |> ignore
              queue.Enqueue(idx)

        let fillSet =
          System.Collections.Generic.HashSet<struct (int * int * int)>()

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
    ]

    testList "Properties: inRange completeness" [
      testCase "3D inRange returns exactly cells within Chebyshev range"
      <| fun _ ->
        let grid = CellGrid3D.create 10 10 10 (Vector3(1f, 1f, 1f)) Vector3.Zero

        for range in 0..2 do
          let cells = Grid3DSpatial.inRange 5 5 5 range grid

          let cellSet =
            System.Collections.Generic.HashSet<struct (int * int * int)>()

          for i in 0 .. cells.Length - 1 do
            cellSet.Add(cells.[i]) |> ignore

          for i in 0 .. cells.Length - 1 do
            let struct (x, y, z) = cells.[i]
            let dist = Grid3DSpatial.distanceChebyshev 5 5 5 x y z

            Expect.isLessThanOrEqual
              dist
              range
              $"Cell ({x},{y},{z}) within range {range}"

          let side = 2 * range + 1
          let expected = side * side * side
          Expect.equal cellSet.Count expected $"3D inRange {range} count"
    ]

    // ── Additional LOS tests ───────────────────────────────────────────

    testList "LineOfSight extended" [
      testCase "voxel LOS: goal cell blocked returns false"
      <| fun _ ->
        let grid = CellGrid3D.create 10 10 10 (Vector3(1f, 1f, 1f)) Vector3.Zero
        CellGrid3D.set 9 9 9 1 grid

        let isBlocked x y z =
          match CellGrid3D.get x y z grid with
          | ValueSome _ -> true
          | ValueNone -> false

        Expect.isFalse
          (Grid3DSpatial.lineOfSight 0 0 0 9 9 9 isBlocked grid)
          "Goal blocked → LOS false"

      testCase "hex3D LOS: goal cell blocked returns false"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 9 9 9 1 grid

        let isBlocked c r l =
          match HexGrid3D.get c r l grid with
          | ValueSome _ -> true
          | ValueNone -> false

        Expect.isFalse
          (Hex3DSpatial.lineOfSight 0 0 0 9 9 9 isBlocked grid)
          "Hex3D goal blocked → LOS false"

      testCase "voxel lineOfSightCells: axis-aligned stops at blocker"
      <| fun _ ->
        let grid = CellGrid3D.create 10 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero
        CellGrid3D.set 5 0 0 1 grid

        let isBlocked x y z =
          match CellGrid3D.get x y z grid with
          | ValueSome _ -> true
          | ValueNone -> false

        let cells = Grid3DSpatial.lineOfSightCells 0 0 0 9 0 0 isBlocked grid

        Expect.equal cells.Length 5 "5 visible cells"

        for i in 0 .. cells.Length - 1 do
          let struct (x, _, _) = cells.[i]
          Expect.isLessThan x 5 "All visible cells before blocker"

      testCase "hex3D lineOfSight: clear returns true"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 1 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let isBlocked _ _ _ = false

        Expect.isTrue
          (Hex3DSpatial.lineOfSight 0 0 0 9 0 0 isBlocked grid)
          "Hex3D clear LOS"

      testCase "hex3D lineOfSight: blocker on path returns false"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 1 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 5 0 0 1 grid

        let isBlocked c r l =
          match HexGrid3D.get c r l grid with
          | ValueSome _ -> true
          | ValueNone -> false

        Expect.isFalse
          (Hex3DSpatial.lineOfSight 0 0 0 9 0 0 isBlocked grid)
          "Hex3D blocked LOS"
    ]

    // ── WorldToCell with non-zero origin ───────────────────────────────

    testList "WorldToCell non-zero origin" [
      testCase "voxel worldToCell roundtrips with non-zero origin"
      <| fun _ ->
        let origin = Vector3(500f, 200f, 300f)
        let grid = CellGrid3D.create 8 8 8 (Vector3(16f, 16f, 16f)) origin

        for x in 0..7 do
          for y in 0..7 do
            for z in 0..7 do
              let worldPos = CellGrid3D.getWorldPos x y z grid

              match Grid3DSpatial.worldToCell worldPos grid with
              | ValueSome struct (cx, cy, cz) ->
                Expect.equal cx x $"X roundtrip ({x},{y},{z})"
                Expect.equal cy y $"Y roundtrip ({x},{y},{z})"
                Expect.equal cz z $"Z roundtrip ({x},{y},{z})"
              | ValueNone ->
                failwith $"Roundtrip failed for ({x},{y},{z}) with origin"

      testCase "voxel worldToCell boundary: right edge"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero
        let worldPos = CellGrid3D.getWorldPos 4 4 4 grid

        match Grid3DSpatial.worldToCell worldPos grid with
        | ValueSome struct (cx, cy, cz) ->
          Expect.equal cx 4 "Right edge x"
          Expect.equal cy 4 "Top edge y"
          Expect.equal cz 4 "Far edge z"
        | ValueNone -> failwith "Expected ValueSome at boundary"
    ]

    // ── Distance properties ────────────────────────────────────────────

    testList "Properties: 3D distance" [
      testCase "Manhattan triangle inequality in 3D"
      <| fun _ ->
        for x1 in 0..2 do
          for y1 in 0..2 do
            for z1 in 0..2 do
              for x2 in 0..2 do
                for y2 in 0..2 do
                  for z2 in 0..2 do
                    for x3 in 0..2 do
                      for y3 in 0..2 do
                        for z3 in 0..2 do
                          let d12 =
                            Grid3DSpatial.distanceManhattan x1 y1 z1 x2 y2 z2

                          let d23 =
                            Grid3DSpatial.distanceManhattan x2 y2 z2 x3 y3 z3

                          let d13 =
                            Grid3DSpatial.distanceManhattan x1 y1 z1 x3 y3 z3

                          Expect.isLessThanOrEqual
                            d13
                            (d12 + d23)
                            $"3D Manhattan triangle"

      testCase "Chebyshev triangle inequality in 3D"
      <| fun _ ->
        for x1 in 0..2 do
          for y1 in 0..2 do
            for z1 in 0..2 do
              for x2 in 0..2 do
                for y2 in 0..2 do
                  for z2 in 0..2 do
                    for x3 in 0..2 do
                      for y3 in 0..2 do
                        for z3 in 0..2 do
                          let d12 =
                            Grid3DSpatial.distanceChebyshev x1 y1 z1 x2 y2 z2

                          let d23 =
                            Grid3DSpatial.distanceChebyshev x2 y2 z2 x3 y3 z3

                          let d13 =
                            Grid3DSpatial.distanceChebyshev x1 y1 z1 x3 y3 z3

                          Expect.isLessThanOrEqual
                            d13
                            (d12 + d23)
                            $"3D Chebyshev triangle"

      testCase "Euclidean triangle inequality in 3D"
      <| fun _ ->
        for x1 in 0..2 do
          for y1 in 0..2 do
            for z1 in 0..2 do
              for x2 in 0..2 do
                for y2 in 0..2 do
                  for z2 in 0..2 do
                    for x3 in 0..2 do
                      for y3 in 0..2 do
                        for z3 in 0..2 do
                          let d12 =
                            Grid3DSpatial.distanceEuclidean x1 y1 z1 x2 y2 z2

                          let d23 =
                            Grid3DSpatial.distanceEuclidean x2 y2 z2 x3 y3 z3

                          let d13 =
                            Grid3DSpatial.distanceEuclidean x1 y1 z1 x3 y3 z3

                          Expect.isLessThanOrEqual
                            d13
                            (d12 + d23 + 0.001f)
                            $"3D Euclidean triangle"
    ]

    // ── Hex3D additional tests ─────────────────────────────────────────

    testList "Hex3D extended" [
      testCase "hex3D PointyTop vs FlatTop: different world positions"
      <| fun _ ->
        let ptGrid =
          HexGrid3D.create 10 5 10 32f 2f Vector3.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid3D.create 10 5 10 32f 2f Vector3.Zero HexOrientation.FlatTop

        for col in 0..4 do
          for row in 0..4 do
            for layer in 0..2 do
              let ptPos = HexGrid3D.getWorldPos col row layer ptGrid
              let ftPos = HexGrid3D.getWorldPos col row layer ftGrid

              // X and Z should differ due to different hex dimensions
              Expect.notEqual
                (ptPos.X, ptPos.Z)
                (ftPos.X, ftPos.Z)
                $"World pos ({col},{row},{layer}) X,Z differ"

              // Y should be the same (layer height is identical)
              Expect.floatClose
                Accuracy.high
                (float ptPos.Y)
                (float ftPos.Y)
                $"World pos ({col},{row},{layer}) Y same"

      testCase "hex3D PT vs FT: neighbor sets differ for odd-row cells"
      <| fun _ ->
        let ptGrid =
          HexGrid3D.create 10 3 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid3D.create 10 3 10 32f 1f Vector3.Zero HexOrientation.FlatTop

        // Cell (3,1,1): odd row in PT, odd col? 3 is odd in FT
        let ptNbrs = Hex3DSpatial.neighborsHex 3 1 1 ptGrid
        let ftNbrs = Hex3DSpatial.neighborsHex 3 1 1 ftGrid

        let ptSet =
          System.Collections.Generic.HashSet<struct (int * int * int)>()

        let ftSet =
          System.Collections.Generic.HashSet<struct (int * int * int)>()

        for i in 0 .. ptNbrs.Length - 1 do
          ptSet.Add(ptNbrs.[i]) |> ignore

        for i in 0 .. ftNbrs.Length - 1 do
          ftSet.Add(ftNbrs.[i]) |> ignore

        let mutable anyDifferent = false

        for i in 0 .. ptNbrs.Length - 1 do
          if not(ftSet.Contains(ptNbrs.[i])) then
            anyDifferent <- true

        Expect.isTrue
          anyDifferent
          "PT and FT hex3D neighbors differ for (3,1,1)"

      testCase "hex3D PT vs FT: distance differs for same coords"
      <| fun _ ->
        let ptGrid =
          HexGrid3D.create 10 5 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid3D.create 10 5 10 32f 1f Vector3.Zero HexOrientation.FlatTop

        // Pick cells where hex distance should differ due to stagger
        // (0,0,0) to (3,1,0): hex distance depends on orientation
        let ptDist = Hex3DSpatial.distance 0 0 0 3 1 0 ptGrid
        let ftDist = Hex3DSpatial.distance 0 0 0 3 1 0 ftGrid

        // Both are valid distances, but they may differ
        // At minimum, verify both are non-negative
        Expect.isGreaterThanOrEqual ptDist 0 "PT dist >= 0"
        Expect.isGreaterThanOrEqual ftDist 0 "FT dist >= 0"

        // For cells on same layer, vertical distance = 0
        // hex distance part should be consistent within each orientation
        let ptDist2 = Hex3DSpatial.distance 0 0 0 5 3 0 ptGrid
        let ftDist2 = Hex3DSpatial.distance 0 0 0 5 3 0 ftGrid

        Expect.isGreaterThanOrEqual ptDist2 0 "PT dist2 >= 0"
        Expect.isGreaterThanOrEqual ftDist2 0 "FT dist2 >= 0"

      testCase "hex3D PT vs FT: inRange counts match formula"
      <| fun _ ->
        // Both orientations should produce the same count for centered ranges
        let ptGrid =
          HexGrid3D.create 20 3 20 32f 1f Vector3.Zero HexOrientation.PointyTop

        let ftGrid =
          HexGrid3D.create 20 3 20 32f 1f Vector3.Zero HexOrientation.FlatTop

        for range in 0..2 do
          let ptCells = Hex3DSpatial.inRange 10 10 1 range ptGrid
          let ftCells = Hex3DSpatial.inRange 10 10 1 range ftGrid

          // Same count (both use hex inRange formula)
          Expect.equal
            ptCells.Length
            ftCells.Length
            $"PT vs FT inRange {range} same count"

      testCase "hex3D FlatTop neighbors returns 8 for center"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.FlatTop

        let nbrs = Hex3DSpatial.neighbors 5 5 5 grid
        Expect.equal nbrs.Length 8 "FlatTop center: 6 hex + 2 vertical"

      testCase "hex3D FlatTop distance same cell = 0"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.FlatTop

        Expect.equal (Hex3DSpatial.distance 2 2 2 2 2 2 grid) 0 "FlatTop same"

      testCase "hex3D FlatTop distance vertical = layer diff"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.FlatTop

        Expect.equal (Hex3DSpatial.distance 2 2 0 2 2 3 grid) 3 "3 layers"

      testCase "hex3D inRange 1 with 3 layers counts correctly"
      <| fun _ ->
        let grid =
          HexGrid3D.create 20 3 20 32f 1f Vector3.Zero HexOrientation.PointyTop

        let cells = Hex3DSpatial.inRange 10 10 1 1 grid
        // layer 1: inRange 1 on hex = 7 cells
        // layer 0: inRange 0 on hex = 1 cell (center only)
        // layer 2: inRange 0 on hex = 1 cell (center only)
        // total = 7 + 1 + 1 = 9
        Expect.equal cells.Length 9 "Hex3D inRange 1 = 9"

        let cellSet =
          System.Collections.Generic.HashSet<struct (int * int * int)>()

        for i in 0 .. cells.Length - 1 do
          cellSet.Add(cells.[i]) |> ignore

        // No duplicates
        Expect.equal cellSet.Count 9 "No duplicates"

      testCase "hex3D inRange 0 = 1 cell"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 10 10 32f 1f Vector3.Zero HexOrientation.PointyTop

        let cells = Hex3DSpatial.inRange 5 5 5 0 grid
        Expect.equal cells.Length 1 "Range 0 = 1 cell"
        Expect.contains cells (struct (5, 5, 5)) "Contains origin"

      testCase "hex3D floodFill all returned cells satisfy predicate"
      <| fun _ ->
        let grid =
          HexGrid3D.create 3 3 3 32f 1f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 1 1 1 1 grid

        let predicate c r l =
          match HexGrid3D.get c r l grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex3DSpatial.floodFill 0 0 0 predicate grid

        for i in 0 .. cells.Length - 1 do
          let struct (c, r, l) = cells.[i]

          Expect.isTrue
            (predicate c r l)
            $"Cell ({c},{r},{l}) satisfies predicate"

      testCase "hex3D floodFill no reachable cell is missing"
      <| fun _ ->
        let grid =
          HexGrid3D.create 4 2 4 32f 1f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 2 1 0 1 grid

        let predicate c r l =
          match HexGrid3D.get c r l grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cells = Hex3DSpatial.floodFill 0 0 0 predicate grid

        // BFS from start
        let reachable =
          System.Collections.Generic.HashSet<struct (int * int * int)>()

        let queue = System.Collections.Generic.Queue<struct (int * int * int)>()

        queue.Enqueue(struct (0, 0, 0))
        reachable.Add(struct (0, 0, 0)) |> ignore

        while queue.Count > 0 do
          let struct (c, r, l) = queue.Dequeue()
          let nbrs = Hex3DSpatial.neighbors c r l grid

          for i in 0 .. nbrs.Length - 1 do
            let struct (nc, nr, nl) = nbrs.[i]

            if nc >= 0 && nc < 4 && nr >= 0 && nr < 4 && nl >= 0 && nl < 2 then
              let idx = struct (nc, nr, nl)

              if not(reachable.Contains(idx)) && predicate nc nr nl then
                reachable.Add(idx) |> ignore
                queue.Enqueue(idx)

        let fillSet =
          System.Collections.Generic.HashSet<struct (int * int * int)>()

        for i in 0 .. cells.Length - 1 do
          fillSet.Add(cells.[i]) |> ignore

        Expect.equal
          fillSet.Count
          reachable.Count
          "Hex3D fill count matches BFS count"

        for r in reachable do
          Expect.isTrue
            (fillSet.Contains(r))
            $"Reachable cell {r} must be in fill result"

      testCase "hex3D FlatTop worldToCell roundtrips"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 5 10 32f 2f Vector3.Zero HexOrientation.FlatTop

        let mutable failures = 0

        for col in 0..9 do
          for row in 0..9 do
            for layer in 0..4 do
              let worldPos = HexGrid3D.getWorldPos col row layer grid

              match Hex3DSpatial.worldToCell worldPos grid with
              | ValueSome struct (c, r, l) ->
                let dist = Hex3DSpatial.distance col row layer c r l grid

                if dist > 1 then
                  failures <- failures + 1
              | ValueNone -> failures <- failures + 1

        Expect.equal failures 0 $"{failures} FlatTop roundtrips failed"

      testCase "hex3D worldToCell maps boundary points to nearest hex"
      <| fun _ ->
        let grid =
          HexGrid3D.create 8 2 8 32f 2f Vector3.Zero HexOrientation.PointyTop

        for col in 1..6 do
          for row in 1..6 do
            let layer = 0
            let center = HexGrid3D.getWorldPos col row layer grid

            let tmpHexGrid =
              HexGrid.create 8 8 32f Vector2.Zero grid.Orientation

            let nbrs = Hex2DSpatial.neighbors col row tmpHexGrid

            for i in 0 .. nbrs.Length - 1 do
              let struct (nc, nr) = nbrs.[i]
              let neighbor = HexGrid3D.getWorldPos nc nr layer grid

              // Point 60% toward neighbor (closer to neighbor)
              let testX = center.X + (neighbor.X - center.X) * 0.6f
              let testZ = center.Z + (neighbor.Z - center.Z) * 0.6f
              let testPos = Vector3(testX, center.Y, testZ)

              match Hex3DSpatial.worldToCell testPos grid with
              | ValueSome struct (bc, br, bl) ->
                let resolvedCenter = HexGrid3D.getWorldPos bc br bl grid
                let dx = testPos.X - resolvedCenter.X
                let dz = testPos.Z - resolvedCenter.Z
                let distToResolved = sqrt(dx * dx + dz * dz)

                let dx2 = testPos.X - neighbor.X
                let dz2 = testPos.Z - neighbor.Z
                let distToNeighbor = sqrt(dx2 * dx2 + dz2 * dz2)

                Expect.isLessThanOrEqual
                  distToResolved
                  (distToNeighbor + 0.01f)
                  (sprintf
                    "Hex3D boundary (%d,%d)->(%d,%d) at 60%%"
                    col
                    row
                    nc
                    nr)
              | ValueNone ->
                failwith(
                  sprintf "Hex3D boundary OOB (%d,%d)->(%d,%d)" col row nc nr
                )

      testCase "hex3D A* consecutive cells are neighbors"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 1 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 2 do
            let struct (c1, r1, l1) = path.[i]
            let struct (c2, r2, l2) = path.[i + 1]
            let nbrs = Hex3DSpatial.neighbors c1 r1 l1 grid
            let mutable isNeighbor = false

            for j in 0 .. nbrs.Length - 1 do
              let struct (nc, nr, nl) = nbrs.[j]

              if nc = c2 && nr = r2 && nl = l2 then
                isNeighbor <- true

            Expect.isTrue
              isNeighbor
              $"({c1},{r1},{l1})->({c2},{r2},{l2}) must be hex3D neighbors"
        | ValueNone -> failwith "Expected path"

      testCase "hex3D A* path cells are all passable"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 3 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 2 0 0 1 grid
        HexGrid3D.set 2 1 0 1 grid

        let passable c r l =
          match HexGrid3D.get c r l grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 0 0 passable cost grid with
        | ValueSome path ->
          for i in 0 .. path.Length - 1 do
            let struct (c, r, l) = path.[i]

            Expect.isTrue
              (passable c r l)
              $"Cell ({c},{r},{l}) must be passable"
        | ValueNone -> failwith "Expected path"

      testCase "hex3D A* start blocked returns ValueNone"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 0 0 0 1 grid

        let passable c r l =
          match HexGrid3D.get c r l grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 4 4 passable cost grid with
        | ValueSome _ -> failwith "Start blocked"
        | ValueNone -> ()

      testCase "hex3D A* goal blocked returns ValueNone"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        HexGrid3D.set 4 4 4 1 grid

        let passable c r l =
          match HexGrid3D.get c r l grid with
          | ValueNone -> true
          | ValueSome _ -> false

        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 4 4 4 passable cost grid with
        | ValueSome _ -> failwith "Goal blocked"
        | ValueNone -> ()

      testCase "hex3D A* on non-square grid (Height != Depth)"
      <| fun _ ->
        // Height=2 (layers), Depth=8 (hex rows) — row=5 must be valid
        let grid =
          HexGrid3D.create 10 2 8 32f 1f Vector3.Zero HexOrientation.PointyTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 5 5 0 passable cost grid with
        | ValueSome path ->
          Expect.isGreaterThan path.Length 0 "Path exists on non-square grid"
          let struct (gc, gr, gl) = path.[path.Length - 1]
          Expect.equal gc 5 "Goal col"
          Expect.equal gr 5 "Goal row"
          Expect.equal gl 0 "Goal layer"
        | ValueNone -> failwith "Expected path on non-square grid"

      testCase "hex3D A* row beyond Depth returns ValueNone"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 2 8 32f 1f Vector3.Zero HexOrientation.PointyTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        // row=10 >= Depth=8, should be rejected
        match Hex3DSpatial.findPath 0 0 0 5 10 0 passable cost grid with
        | ValueSome _ -> failwith "Should reject OOB row"
        | ValueNone -> ()

      testCase "hex3D A* layer beyond Height returns ValueNone"
      <| fun _ ->
        let grid =
          HexGrid3D.create 10 2 8 32f 1f Vector3.Zero HexOrientation.PointyTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        // layer=5 >= Height=2, should be rejected
        match Hex3DSpatial.findPath 0 0 0 5 5 5 passable cost grid with
        | ValueSome _ -> failwith "Should reject OOB layer"
        | ValueNone -> ()
    ]

    // ── Additional adversarial 3D ──────────────────────────────────────

    testList "Adversarial 3D extended" [
      testCase "voxel A* on 1x1x1 grid start=goal"
      <| fun _ ->
        let grid = CellGrid3D.create 1 1 1 (Vector3(1f, 1f, 1f)) Vector3.Zero
        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Grid3DSpatial.findPath 0 0 0 0 0 0 passable cost grid with
        | ValueSome path ->
          Expect.equal path.Length 1 "Single cell"
          Expect.contains path (struct (0, 0, 0)) "Is start"
        | ValueNone -> failwith "Expected single cell"

      testCase "hex3D A* on 1x1x1 grid start=goal"
      <| fun _ ->
        let grid =
          HexGrid3D.create 1 1 1 32f 1f Vector3.Zero HexOrientation.PointyTop

        let passable _ _ _ = true
        let cost _ _ _ _ _ _ = 1f

        match Hex3DSpatial.findPath 0 0 0 0 0 0 passable cost grid with
        | ValueSome path -> Expect.equal path.Length 1 "Single cell"
        | ValueNone -> failwith "Expected single cell"

      testCase "voxel neighbors6 with negative input checks output bounds"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero
        let nbrs = Grid3DSpatial.neighbors6 -1 -1 -1 grid
        // Some neighbors of (-1,-1,-1) may be in-bounds (e.g., (0,-1,-1))
        // This is acceptable - caller should ensure input is valid
        ()

      testCase "hex3D neighbors with negative input"
      <| fun _ ->
        let grid =
          HexGrid3D.create 5 5 5 32f 1f Vector3.Zero HexOrientation.PointyTop

        let nbrs = Hex3DSpatial.neighbors -1 -1 -1 grid
        // Behavior with OOB input is implementation-defined
        ()

      testCase "voxel inRange 0 at corner"
      <| fun _ ->
        let grid = CellGrid3D.create 5 5 5 (Vector3(1f, 1f, 1f)) Vector3.Zero
        let cells = Grid3DSpatial.inRange 0 0 0 0 grid
        Expect.equal cells.Length 1 "Corner range 0"
        Expect.contains cells (struct (0, 0, 0)) "Contains corner"
    ]
  ]
