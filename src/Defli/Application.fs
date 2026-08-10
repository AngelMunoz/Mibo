namespace Defli

open System.Numerics
open AdaptiveSlop.Core
open Mibo.Layout
open Defli.World

// ─────────────────────────────────────────────────────────────
// Application — the host-side driver. This replaces the MVU shell:
// there is no Msg, no Cmd, no Sub. The driver writes roots and
// calls world handlers directly. In the headless sim it runs an
// autonomous policy; a windowed frontend (milestone 2) would
// translate keyboard/mouse to the same handlers.
// ─────────────────────────────────────────────────────────────

module Application =

  /// The grid cell CONTAINING a world position (floor of world/size) —
  /// the tile under the cursor. Mibo's Grid2DSpatial.worldToCell rounds
  /// to the NEAREST CENTER (a cursor in the right/bottom half of a tile
  /// picks the NEXT one — the outline visibly cuts tiles in half); the
  /// game wants the containing tile, so the pick is floor-based and
  /// bounds-checked. Origin-aware (the map origin is Zero).
  let inline cellAt
    (worldPos: Vector2)
    (grid: CellGrid2D<MapTile>)
    : struct (int * int) voption =
    // floor, not int: int truncates toward zero, which would map a
    // position just left of the origin into cell 0.
    let x = int(floor((worldPos.X - grid.Origin.X) / grid.CellSize.X))

    let y = int(floor((worldPos.Y - grid.Origin.Y) / grid.CellSize.Y))

    if x >= 0 && x < grid.Width && y >= 0 && y < grid.Height then
      ValueSome(struct (x, y))
    else
      ValueNone

  /// The autonomous player. Runs before each Step: starts waves when
  /// none is active and a tower is affordable (waves fund towers via
  /// kill rewards — never start one broke), builds towers greedily on
  /// cells NEAR THE PATH (a tower off the road never fires), upgrades
  /// when there is spare gold. The world handlers validate everything
  /// (buildable tile, occupancy, gold).
  let policy (world: World) (frameNumber: int) : unit =
    let gold = AVal.getValue world.Economy.Gold

    // Wave director: the next wave waits until a small gold buffer is
    // saved — waves fund towers via kill rewards, so never start one
    // broke. 10 gold is enough for the first kills to roll in.
    let waveActive = AVal.getValue world.Waves.WaveActive
    let queueEmpty = world.Spawning.Queue.Count = 0

    if
      not waveActive
      && queueEmpty
      && not(AVal.getValue world.Economy.GameOver)
      && gold >= 10
    then
      Router.startNextWave world

    // Build director: every 8 frames, scan the path waypoints BACKWARD
    // from the base (every third one) and try each cell in an expanding
    // ring around them — the first buildable, unoccupied, affordable
    // cell wins. Defense-in-depth: every enemy funnels through the base
    // cell, so base-side towers fire on the whole wave, including the
    // rearmost stragglers a mouth-side tower lets slip.
    if frameNumber % 8 = 0 && gold >= TowerDefs.arrow.Cost then
      let towerCount = (world.Towers.Statics |> AMap.getValue).Count

      if towerCount < 15 then
        let tileSize = float32 Tiles.TileSize
        let mutable placed = false
        let mutable i = world.Map.Path.Length - 1

        while not placed && i >= 0 do
          let p = world.Map.Path[i]
          let cx = int(p.X / tileSize)
          let cy = int(p.Y / tileSize)
          let mutable ring = 0

          while not placed && ring <= 2 do
            let mutable dy = -ring

            while not placed && dy <= ring do
              let mutable dx = -ring

              while not placed && dx <= ring do
                if abs dx = ring || abs dy = ring then
                  placed <- Router.placeTower world struct (cx + dx, cy + dy)

                dx <- dx + 1

              dy <- dy + 1

            ring <- ring + 1

          i <- i - 3

    // Upgrade director: every 30 frames, upgrade the first tower the
    // row-major scan finds (upgradeTower validates gold and the cap).
    if frameNumber % 30 = 0 && gold >= TowerDefs.arrow.UpgradeCost * 2 then
      let mutable upgraded = false
      let mutable y = 0

      while not upgraded && y < world.Config.GridRows do
        let mutable x = 0

        while not upgraded && x < world.Config.GridCols do
          if Router.upgradeTower world struct (x, y) then
            upgraded <- true

          x <- x + 1

        y <- y + 1
