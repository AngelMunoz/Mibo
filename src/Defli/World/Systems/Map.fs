namespace Defli.World.Systems

open System.Numerics
open Mibo.Layout
open Defli.World

// ─────────────────────────────────────────────────────────────
// Map sub-system — owns a LayeredGrid2D<MapTile> (one parallel
// CellGrid2D per concern) and the path. Static content (built once
// at world init, never mutated — same rule as Kimo's map/stores;
// NOT adaptive).
//
// Layers (MapLayers):
//   Terrain    — grass fill (visual base)
//   Path       — the road, stamped over the waypoint segments
//   Buildable  — build permission; the road stamp overwrites the
//                cells under it with the non-buildable path tile
//   Waypoints  — the path's vertex cells (spawn/base markers)
//
// The road is carved with the stamp machinery (Layout.fill /
// repeatX / repeatY over GridSection2D), never hand-rolled loops.
// The view iterates with CellGrid2D.iterVisible over the camera's
// world-space view rect — culled to the visible cells even though
// the fixed screen currently shows the whole grid.
// ─────────────────────────────────────────────────────────────

/// Layer indices of the map's parallel grids.
[<RequireQualifiedAccess>]
module MapLayers =
  [<Literal>]
  let Terrain = 0

  [<Literal>]
  let Path = 1

  [<Literal>]
  let Buildable = 2

  [<Literal>]
  let Waypoints = 3

  [<Literal>]
  let Decorations = 4

type MapModel = {
  Grid: LayeredGrid2D<MapTile>
  /// World-space waypoint centers (spawn → base) — the movement
  /// (physics) phase walks these.
  Path: Vector2[]
  SpawnCell: struct (int * int)
  BaseCell: struct (int * int)
}

module MapModel =

  let private grassTile = {
    Terrain = TerrainKind.Grass
    IsPath = false
    Buildable = true
    IsWaypoint = false
    Decoration = ValueNone
  }

  let private pathTile = {
    Terrain = TerrainKind.Dirt
    IsPath = true
    Buildable = false
    IsWaypoint = false
    Decoration = ValueNone
  }

  let private nonBuildableTile = { grassTile with Buildable = false }

  /// A decorations-layer row: the sprite frame to draw over the
  /// terrain (dirt blends keep Buildable = true — ground paint;
  /// props on procedural maps set Buildable = false — obstacles).
  let inline private decoTile(frame: TileInfo) = {
    grassTile with
        Decoration = ValueSome frame
  }

  let inline private obstacleTile(frame: TileInfo) = {
    decoTile frame with
        Buildable = false
  }

  /// A layer's CellGrid2D (all layers exist after create).
  let inline layer (index: int) (m: MapModel) : CellGrid2D<MapTile> =
    let struct (grid, _) = LayeredGrid2D.getOrAddLayer index m.Grid
    grid

  let inline terrain(m: MapModel) = layer MapLayers.Terrain m
  let inline pathGrid(m: MapModel) = layer MapLayers.Path m
  let inline buildableGrid(m: MapModel) = layer MapLayers.Buildable m
  let inline waypoints(m: MapModel) = layer MapLayers.Waypoints m
  let inline decorations(m: MapModel) = layer MapLayers.Decorations m

  /// A cell is buildable iff its Buildable layer row carries Buildable
  /// (the road stamp overwrote the cells under it).
  let inline isBuildable (x: int) (y: int) (m: MapModel) : bool =
    m |> buildableGrid |> CellGrid2D.get x y |> ValueOption.exists _.Buildable

  /// Hand-authored Level-1 path, in cells (spawn left → base right).
  let private waypointCells = [|
    struct (0, 4)
    struct (7, 4)
    struct (7, 8)
    struct (14, 8)
    struct (14, 2)
    struct (19, 2)
  |]

  /// One axis-aligned road segment as a stamp (repeatX for horizontal,
  /// repeatY for vertical — inclusive of both endpoints).
  let inline private stampSegment
    (struct (px, py): struct (int * int))
    (struct (tx, ty): struct (int * int))
    (section: GridSection2D<MapTile>)
    : GridSection2D<MapTile> =
    if py = ty then
      Layout.repeatX (min px tx) py (abs(tx - px) + 1) pathTile section
    else
      Layout.repeatY px (min py ty) (abs(ty - py) + 1) pathTile section

  /// The whole road as one stamp chain (all waypoint segments).
  let inline private stampPath
    (section: GridSection2D<MapTile>)
    : GridSection2D<MapTile> =
    let mutable acc = section

    for i in 1 .. waypointCells.Length - 1 do
      acc <- stampSegment waypointCells[i - 1] waypointCells[i] acc

    acc

  // ── Level-2 procedural generation ──

  /// Prop frame picked deterministically from the placement cell — no
  /// extra RNG stream (Kimo's rule: RNG streams are owned, never shared).
  let inline private propFor (x: int) (y: int) : TileInfo =
    Tiles.decoProps[(x * 7 + y * 13) % Tiles.decoProps.Length]

  /// Deterministic roll in [0, 1) from a cell + salt (same rule).
  let inline private hashRoll (x: int) (y: int) (salt: int) : float =
    float((x * 31 + y * 17 + salt * 7) % 997) / 997.0

  /// Scatter props as OBSTACLES: the Decorations layer gets the prop
  /// row, the Buildable layer is cleared under it. The stamp's section
  /// offset IS the placement cell — prop variety derives from it.
  let private scatterObstacles
    (count: int)
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (buildable: CellGrid2D<MapTile>)
    : unit =
    Layout.scatterStamp
      count
      seed
      (fun s ->
        let gx = s.OffsetX
        let gy = s.OffsetY
        let frame = propFor gx gy
        CellGrid2D.set gx gy (obstacleTile frame) deco
        CellGrid2D.set gx gy nonBuildableTile buildable
        s)
      (createSection deco)
    |> ignore

  /// Visual-only props (HandAuthored): decoration rows, never on the
  /// road, buildability untouched.
  let private scatterVisualProps
    (count: int)
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (pathLayer: CellGrid2D<MapTile>)
    : unit =
    Layout.scatterStamp
      count
      seed
      (fun s ->
        let gx = s.OffsetX
        let gy = s.OffsetY

        let onPath =
          pathLayer |> CellGrid2D.get gx gy |> ValueOption.exists _.IsPath

        if onPath then
          s
        else
          CellGrid2D.set gx gy (decoTile(propFor gx gy)) deco
          s)
      (createSection deco)
    |> ignore

  /// Dirt blends hugging the road — ONE coherent family (dirt on
  /// grass: dots, patch edges, circle corners), each frame oriented by
  /// the neighbor's direction from the road cell. Props keep their
  /// spot (blends only fill empty decoration cells).
  let private scatterBlends
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (pathLayer: CellGrid2D<MapTile>)
    : unit =
    CellGrid2D.iter
      (fun x y tile ->
        if tile.IsPath then
          for struct (nx, ny) in Grid2DSpatial.neighbors4 x y pathLayer do
            // The Path layer is sparse: an absent cell is grass (free);
            // only a PRESENT path-marked cell is the road.
            let freeGrass =
              match pathLayer |> CellGrid2D.get nx ny with
              | ValueSome t -> not t.IsPath
              | ValueNone -> true

            let empty = (deco |> CellGrid2D.get nx ny).IsNone

            if freeGrass && empty && hashRoll nx ny seed < 0.45 then
              let r = hashRoll nx ny (seed + 1)
              let dx = nx - x
              let dy = ny - y

              let frame =
                if r < 0.2 then
                  Tiles.dirtDotOnGrass
                elif dx <> 0 && dy <> 0 then
                  if dx < 0 && dy < 0 then Tiles.dirtCircleOnGrassTL
                  elif dx > 0 && dy < 0 then Tiles.dirtCircleOnGrassTR
                  elif dx < 0 then Tiles.dirtCircleOnGrassBL
                  else Tiles.dirtCircleOnGrassBR
                elif dx < 0 then
                  Tiles.dirtPatchOnGrassLeft
                elif dx > 0 then
                  Tiles.dirtPatchOnGrassRight
                elif dy < 0 then
                  Tiles.dirtPatchOnGrassTop
                else
                  Tiles.dirtPatchOnGrassBottom

              CellGrid2D.set nx ny (decoTile frame) deco)
      pathLayer

  /// One procedural attempt on a FRESH grid: obstacles scattered with
  /// the given seed, the road carved by findPath around them, and a
  /// floodFill reachability validation (independent of A*).
  let private tryProcedural
    (cfg: WorldConfig)
    (seed: int)
    : struct (LayeredGrid2D<MapTile> *
      struct (int * int)[] *
      struct (int * int) *
      struct (int * int)) voption
    =
    let cellSize = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)

    let grid =
      LayeredGrid2D.create cfg.GridCols cfg.GridRows cellSize Vector2.Zero
      |> LayeredLayout.layer MapLayers.Terrain (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)
      |> LayeredLayout.layer MapLayers.Buildable (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)

    let struct (deco, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Decorations grid

    let struct (buildable, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Buildable grid

    let struct (terrain, _) = LayeredGrid2D.getOrAddLayer MapLayers.Terrain grid

    let obstacleCount = cfg.GridCols * cfg.GridRows / 10
    scatterObstacles obstacleCount seed deco buildable

    let rng = System.Random(seed)
    let spawnY = rng.Next(1, cfg.GridRows - 1)
    let baseY = rng.Next(1, cfg.GridRows - 1)

    let isPassable x y =
      match deco |> CellGrid2D.get x y with
      | ValueSome t -> t.Buildable
      | ValueNone -> true

    match
      Grid2DSpatial.findPath
        0
        spawnY
        (cfg.GridCols - 1)
        baseY
        isPassable
        (fun _ _ _ _ -> 1f)
        terrain
    with
    | ValueNone -> ValueNone
    | ValueSome pathCells ->
      // floodFill validation: the base must be reachable from spawn
      // over non-obstacle cells (independent of the A* result).
      let reachable = Grid2DSpatial.floodFill 0 spawnY isPassable terrain

      let baseReachable =
        reachable
        |> Array.exists(fun struct (x, y) ->
          struct (x, y) = struct (cfg.GridCols - 1, baseY))

      if not baseReachable then
        ValueNone
      else
        // Carve the road along the found path (stamp machinery — the
        // path is 4-adjacent, so each pair is one repeatX/repeatY).
        let struct (pathLayer, _) =
          LayeredGrid2D.getOrAddLayer MapLayers.Path grid

        for i in 1 .. pathCells.Length - 1 do
          stampSegment pathCells[i - 1] pathCells[i] (createSection pathLayer)
          |> ignore

          stampSegment pathCells[i - 1] pathCells[i] (createSection buildable)
          |> ignore

        // Waypoints: every path cell is a waypoint (spawn/base markers
        // ride on the same layer — the view keys the base mount on
        // BaseCell).
        let waypointTile = { grassTile with IsWaypoint = true }

        grid
        |> LayeredLayout.layer MapLayers.Waypoints (fun s ->
          pathCells
          |> Array.fold
            (fun acc struct (x, y) -> Layout.set x y waypointTile acc)
            s)
        |> ignore

        ValueSome(
          grid,
          pathCells,
          struct (0, spawnY),
          struct (cfg.GridCols - 1, baseY)
        )

  /// Shared tail: world-space path centers + the blend pass.
  let private buildModel
    (seed: int)
    (grid: LayeredGrid2D<MapTile>)
    (pathCells: struct (int * int)[])
    (spawn: struct (int * int))
    (baseCell: struct (int * int))
    : MapModel =
    let cellSize = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)

    let struct (terrainLayer, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Terrain grid

    let struct (pathLayer, _) = LayeredGrid2D.getOrAddLayer MapLayers.Path grid

    let struct (deco, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Decorations grid

    scatterBlends seed deco pathLayer

    let path =
      pathCells
      |> Array.map(fun struct (x, y) ->
        let topLeft = CellGrid2D.getWorldPos x y terrainLayer
        topLeft + cellSize / 2f)

    {
      Grid = grid
      Path = path
      SpawnCell = spawn
      BaseCell = baseCell
    }

  /// Level-1: the fixed hand-authored road + visual-only props.
  let private handAuthored(cfg: WorldConfig) : MapModel =
    let cellSize = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)

    let grid =
      LayeredGrid2D.create cfg.GridCols cfg.GridRows cellSize Vector2.Zero
      |> LayeredLayout.layer MapLayers.Terrain (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)
      |> LayeredLayout.layer MapLayers.Buildable (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)
      |> LayeredLayout.layer MapLayers.Path stampPath
      |> LayeredLayout.layer MapLayers.Buildable stampPath

    let waypointTile = { grassTile with IsWaypoint = true }

    grid
    |> LayeredLayout.layer MapLayers.Waypoints (fun s ->
      waypointCells
      |> Array.fold
        (fun acc struct (x, y) -> Layout.set x y waypointTile acc)
        s)
    |> ignore

    let struct (deco, _) =
      LayeredGrid2D.getOrAddLayer MapLayers.Decorations grid

    let struct (pathLayer, _) = LayeredGrid2D.getOrAddLayer MapLayers.Path grid

    scatterVisualProps (cfg.GridCols * cfg.GridRows / 8) cfg.Seed deco pathLayer

    buildModel
      cfg.Seed
      grid
      waypointCells
      waypointCells[0]
      waypointCells[waypointCells.Length - 1]

  /// Level-2: seeded obstacle scatter → findPath road → floodFill
  /// validation. Seeds advance until a valid layout lands; after 16
  /// attempts it falls back to the hand-authored road (guaranteed
  /// valid — the game never boots to a broken map).
  let private procedural(cfg: WorldConfig) : MapModel =
    let rec attempt (seed: int) (left: int) : MapModel =
      if left = 0 then
        handAuthored cfg
      else
        match tryProcedural cfg seed with
        | ValueSome struct (grid, pathCells, spawn, baseCell) ->
          buildModel cfg.Seed grid pathCells spawn baseCell
        | ValueNone -> attempt (seed + 1) (left - 1)

    attempt cfg.Seed 16

  let create(cfg: WorldConfig) : MapModel =
    match cfg.MapVariant with
    | MapVariant.HandAuthored -> handAuthored cfg
    | MapVariant.Procedural -> procedural cfg
