module Defli.Tests.MapTests

open Expecto
open Mibo.Layout
open Defli
open Defli.World
open Defli.World.Systems

let private cfg = TestData.Fixtures.cfg
let private map = MapModel.create cfg

let tests =
  testList "Map" [
    testCase "grid dimensions match config" (fun () ->
      Expect.equal map.Grid.Width cfg.GridCols "cols"
      Expect.equal map.Grid.Height cfg.GridRows "rows")

    testCase "path is continuous spawn → base" (fun () ->
      Expect.equal map.SpawnCell (struct (0, 4)) "spawn cell"
      Expect.equal map.BaseCell (struct (19, 2)) "base cell"
      Expect.isGreaterThan map.Path.Length 1 "waypoints")

    testCase "path layer: every cell marked, none buildable" (fun () ->
      let pathGrid = MapModel.pathGrid map
      let mutable pathCount = 0

      CellGrid2D.iter
        (fun _ _ tile ->
          pathCount <- pathCount + 1
          Expect.isTrue tile.IsPath "path tile marked"
          Expect.isFalse tile.Buildable "path tile not buildable")
        pathGrid

      Expect.isGreaterThan pathCount 0 "path exists")

    testCase "buildable layer: no buildable cell sits on the road" (fun () ->
      let pathGrid = MapModel.pathGrid map
      let buildable = MapModel.buildableGrid map

      CellGrid2D.iter
        (fun x y tile ->
          if tile.Buildable then
            match CellGrid2D.get x y pathGrid with
            | ValueSome p -> Expect.isFalse p.IsPath "buildable not on path"
            | ValueNone -> ())
        buildable)

    testCase "isBuildable: grass yes, road no, out of grid no" (fun () ->
      Expect.isTrue (MapModel.isBuildable 0 0 map) "grass buildable"
      Expect.isFalse (MapModel.isBuildable 1 4 map) "road not buildable"
      Expect.isFalse (MapModel.isBuildable -1 0 map) "out of grid")

    testCase "waypoints layer marks the path vertices" (fun () ->
      let waypoints = MapModel.waypoints map

      let marked =
        CellGrid2D.get 0 4 waypoints
        |> ValueOption.exists(fun t -> t.IsWaypoint)

      Expect.isTrue marked "spawn vertex marked"

      let baseMarked =
        CellGrid2D.get 19 2 waypoints
        |> ValueOption.exists(fun t -> t.IsWaypoint)

      Expect.isTrue baseMarked "base vertex marked"

      let offPath =
        CellGrid2D.get 3 3 waypoints
        |> ValueOption.exists(fun t -> t.IsWaypoint)

      Expect.isFalse offPath "off-path cell not marked")

    testCase "waypoint centers sit at cell centers" (fun () ->
      for p in map.Path do
        // Origin (0,0), 64px cells: centers are at x.5 offsets.
        Expect.equal (p.X % 64f) 32f "center x"
        Expect.equal (p.Y % 64f) 32f "center y")

    testCase "cellAt picks the CONTAINING cell (no half-tile shift)" (fun () ->
      // Mibo's worldToCell rounds to the NEAREST CENTER: a cursor in
      // the right half of cell (1,1) would pick (2,1). The game's pick
      // is floor-based — the tile under the cursor, always.
      let terrain = MapModel.terrain map

      // World (100, 84) is inside cell (1,1) — including its right and
      // bottom halves.
      match
        Application.cellAt (System.Numerics.Vector2(100f, 84f)) terrain
      with
      | ValueSome struct (1, 1) -> ()
      | other -> failtestf "expected (1,1), got %A" other

      match
        Application.cellAt (System.Numerics.Vector2(127f, 127f)) terrain
      with
      | ValueSome struct (1, 1) -> ()
      | other -> failtestf "expected (1,1), got %A" other

      // Out of grid → ValueNone.
      Expect.isTrue
        (Application.cellAt (System.Numerics.Vector2(-1f, 0f)) terrain).IsNone
        "negative x"

      Expect.isTrue
        (Application.cellAt (System.Numerics.Vector2(2000f, 2000f)) terrain)
          .IsNone
        "past the far edge")

    testCase "spawn and base cells are on the path" (fun () ->
      let pathGrid = MapModel.pathGrid map
      let spawnTile = CellGrid2D.get 0 4 pathGrid
      let baseTile = CellGrid2D.get 19 2 pathGrid

      match spawnTile, baseTile with
      | ValueSome s, ValueSome b ->
        Expect.isTrue s.IsPath "spawn on path"
        Expect.isTrue b.IsPath "base on path"
      | _ -> failtest "spawn/base cells must exist")

    testCase
      "decorations: props + dirt blends exist and stay off the road"
      (fun () ->
        let deco = MapModel.decorations map
        let pathGrid = MapModel.pathGrid map
        let mutable props = 0
        let mutable blends = 0

        CellGrid2D.iter
          (fun x y tile ->
            match tile.Decoration with
            | ValueSome frame ->
              // Never on the road.
              match CellGrid2D.get x y pathGrid with
              | ValueSome p -> Expect.isFalse p.IsPath "decoration off road"
              | ValueNone -> ()

              // Props vs blends: the frame's family (both keep
              // Buildable = true on the hand-authored map — the prop is
              // visual only; only procedural obstacles clear it).
              let isProp =
                Tiles.decoProps |> Array.exists(fun p -> p.Name = frame.Name)

              if isProp then props <- props + 1 else blends <- blends + 1

              // The frame is a real baked tile.
              Expect.isTrue (Tiles.tryByName frame.Name).IsSome "baked frame"
            | ValueNone -> ())
          deco

        Expect.isGreaterThan props 0 "props scattered"
        Expect.isGreaterThan blends 0 "road blends scattered")

    testCase "visual props (HandAuthored) do not block buildability" (fun () ->
      let buildable = MapModel.buildableGrid map
      let deco = MapModel.decorations map
      let mutable blockedProp = 0

      CellGrid2D.iter
        (fun x y tile ->
          if tile.Decoration.IsSome && not tile.Buildable then
            blockedProp <- blockedProp + 1

            // Hand-authored: the prop is visual only — the buildable
            // grid must still allow building there.
            match CellGrid2D.get x y buildable with
            | ValueSome b -> Expect.isTrue b.Buildable "prop does not block"
            | ValueNone -> failtest "buildable row exists")
        deco

      Expect.equal blockedProp 0 "no blocking props on the hand-authored map")
  ]

/// Procedural variant — Level-2 generation stress tests.
let proceduralTests =
  let procCfg = {
    cfg with
        MapVariant = MapVariant.Procedural
  }

  let pmap = MapModel.create procCfg

  testList "Map (procedural)" [
    testCase "road is continuous spawn → base, clear of obstacles" (fun () ->
      let struct (sx, _) = pmap.SpawnCell
      let struct (bx, _) = pmap.BaseCell
      Expect.equal sx 0 "spawn on the left edge"
      Expect.equal bx (procCfg.GridCols - 1) "base on the right edge"
      Expect.isGreaterThan pmap.Path.Length 1 "path exists"

      // Consecutive path cells are 4-adjacent (walkable road): the
      // world-space centers differ by exactly one cell.
      for i in 0 .. pmap.Path.Length - 2 do
        let a = pmap.Path[i]
        let b = pmap.Path[i + 1]
        let d = abs(int b.X - int a.X) + abs(int b.Y - int a.Y)
        Expect.equal d 64 "one cell step")

    testCase "floodFill from spawn reaches the base" (fun () ->
      let deco = MapModel.decorations pmap
      let struct (sx, sy) = pmap.SpawnCell
      let struct (bx, by) = pmap.BaseCell

      let isPassable x y =
        match deco |> CellGrid2D.get x y with
        | ValueSome t -> t.Buildable
        | ValueNone -> true

      let reachable = Grid2DSpatial.floodFill sx sy isPassable deco

      Expect.isTrue
        (reachable
         |> Array.exists(fun struct (x, y) -> struct (x, y) = struct (bx, by)))
        "base reachable")

    testCase
      "obstacles block buildability; road cells never hold one"
      (fun () ->
        let deco = MapModel.decorations pmap
        let buildable = MapModel.buildableGrid pmap
        let mutable obstacleCount = 0

        CellGrid2D.iter
          (fun x y tile ->
            if tile.Decoration.IsSome && not tile.Buildable then
              obstacleCount <- obstacleCount + 1

              match CellGrid2D.get x y buildable with
              | ValueSome b -> Expect.isFalse b.Buildable "obstacle blocks"
              | ValueNone -> ())
          deco

        Expect.isGreaterThan obstacleCount 0 "obstacles exist"

        // No obstacle sits on the road.
        let pathGrid = MapModel.pathGrid pmap

        CellGrid2D.iter
          (fun x y tile ->
            if tile.IsPath then
              match CellGrid2D.get x y deco with
              | ValueSome d ->
                Expect.isTrue d.Buildable "road cell has no obstacle"
              | ValueNone -> ())
          pathGrid)
  ]

/// Temporary probe — REMOVE BEFORE COMMIT.
let probeTests =
  let procCfg = {
    cfg with
        MapVariant = MapVariant.Procedural
  }

  let pmap = MapModel.create procCfg

  testList "Map (probe)" [
    testCase "dump path vs obstacles" (fun () ->
      let pathGrid = MapModel.pathGrid pmap
      let deco = MapModel.decorations pmap
      let mutable onRoad = 0
      let mutable obs = 0
      let mutable path = 0

      CellGrid2D.iter
        (fun x y t ->
          if t.Decoration.IsSome && not t.Buildable then
            obs <- obs + 1

            let onPath =
              pathGrid |> CellGrid2D.get x y |> ValueOption.exists _.IsPath

            if onPath then
              onRoad <- onRoad + 1
              printfn "PROBE obstacle ON ROAD at %d,%d" x y
            else
              printfn "PROBE obstacle at %d,%d" x y)
        deco

      CellGrid2D.iter
        (fun x y t ->
          if t.IsPath then
            path <- path + 1)
        pathGrid

      printfn "PROBE path=%d obstacles=%d onRoad=%d" path obs onRoad
      Expect.equal onRoad 0 "no obstacle on the road")

    testCase "game seed 42 road shape" (fun () ->
      let g = MapModel.create WorldConfig.defaults
      let pathGrid = MapModel.pathGrid g
      let deco = MapModel.decorations g
      let mutable cells = ""

      CellGrid2D.iter
        (fun x y t ->
          if t.IsPath then
            cells <- cells + $"%d{x},%d{y} ")
        pathGrid

      printfn "PROBE42 path: %s" cells

      printfn
        "PROBE42 spawn=%A base=%A len=%d"
        g.SpawnCell
        g.BaseCell
        g.Path.Length

      CellGrid2D.iter
        (fun x y t ->
          if t.Decoration.IsSome && not t.Buildable then
            let onPath =
              pathGrid |> CellGrid2D.get x y |> ValueOption.exists _.IsPath

            if onPath then
              printfn "PROBE42 OBSTACLE ON ROAD at %d,%d" x y)
        deco)
  ]
