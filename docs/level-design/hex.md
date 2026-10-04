---
title: Hex Grids
category: Level Design
categoryindex: 8
index: 8
---

# Hex Grids

Hex grids give six equal-distance neighbors per cell. Hexes are a storage configuration: `CellGrid2D.createHex` builds a `CellGrid2D` with hex geometry, the same grid type as squares.

Author hex maps with the [Flow DSL](code-first.html). Zones, docks, and landmarks work unchanged. Geometry affects world positions and spatial queries. The `HexGrid` and `HexLayout` modules are retired; the [archive](../v5/legacy-stamps/hex-layout-2d.html) keeps their reference.

## When to use hex

| Use case | Rect grid | Hex grid |
|----------|-----------|----------|
| Platformers, top-down RPGs | Natural fit | Awkward edges |
| Strategy / tactics games | Diagonal advantage | Equal neighbors |
| Wargames, board game ports | Looks wrong | Authentic |
| Procedural terrain | Grid lines show | Organic feel |
| 4/8-directional movement | Built-in | Needs adaptation |
| 6-directional movement | Impossible | Natural |

Hex grids are harder to align with screen edges and rectangular sprites. Stay with rect for axis-aligned tile art. Use hex when adjacency matters.

## Orientation

Hexes come in two rotations. The choice affects visuals and coordinate math:

```
    Pointy Top              Flat Top
      /\                    ______
     /  \                  /      \
    /    \                /        \
    \    /                \        /
     \  /                  \      /
      \/                    ------
```

- **Pointy top**: strategy maps, tactical RPGs. Rows align horizontally.
- **Flat top**: isometric-style games, board game ports. Columns align vertically.

```fsharp
open Mibo.Layout

// Strategy map with pointy-top hexes
let grid = CellGrid2D.createHex {
  Orientation = HexOrientation.PointyTop
  Width = 20
  Height = 15
  Radius = 32f
  Origin = Vector2.Zero
}

// Isometric board with flat-top hexes
let board = CellGrid2D.createHex {
  Orientation = HexOrientation.FlatTop
  Width = 12
  Height = 10
  Radius = 48f
  Origin = Vector2.Zero
}
```

`Radius` is the hex radius (center to corner). `CellSize` stores the hex's bounding box. `CellGrid2D.hexOrientation` and `CellGrid2D.hexRadius` read the parameters back.

## Coordinates

Hex grids use offset coordinates: a column/row pair with a stagger. Odd rows (pointy top) or odd columns (flat top) shift by half a hex width. `getWorldPos` applies the offset. Address cells as `(col, row)`.

## Cells

```fsharp
open Mibo.Layout
open System.Numerics

let grid = CellGrid2D.createHex {
  Orientation = HexOrientation.PointyTop
  Width = 20
  Height = 15
  Radius = 32f
  Origin = Vector2.Zero
}

// Place content
CellGrid2D.set 5 3 myTile grid

// Read content (returns voption: no heap allocation)
match CellGrid2D.get 5 3 grid with
| ValueSome tile -> // handle tile
| ValueNone -> // empty cell

// Remove content
CellGrid2D.clear 5 3 grid

// Get world position for rendering (staggers odd rows for pointy top)
let worldPos = CellGrid2D.getWorldPos 5 3 grid  // Vector2
```

## Iteration

```fsharp
// Process every populated cell
grid |> CellGrid2D.iter (fun col row tile ->
  let pos = CellGrid2D.getWorldPos col row grid
  renderTile pos tile
)
```

`CellGrid2D.iterVisible` culls hex grids. The window is orientation-aware and padded one cell per axis. Bounds are `int` world coordinates:

```fsharp
let screenLeft = cameraX - viewportWidth / 2f
let screenTop = cameraY - viewportHeight / 2f
let screenRight = cameraX + viewportWidth / 2f
let screenBottom = cameraY + viewportHeight / 2f

grid
|> CellGrid2D.iterVisible (int screenLeft) (int screenTop) (int screenRight) (int screenBottom)
  (fun col row tile ->
    let pos = CellGrid2D.getWorldPos col row grid
    renderTile pos tile
  )
```

## Flow authoring

```fsharp
let kingdom =
  Flow.grid {
    Cols = [| Fixed 10; Weight 1f; Fixed 10 |]
    Rows = [| Fixed 8; Weight 1f; Fixed 8 |]
    Gap = 0
    Areas = [| "plains plains hills"; "plains plains hills" |]
    Places = [|
      struct (Area "plains", Flow.canvas [ Flow.fill GrassTile; Flow.noise { Count = 25; Seed = 7 } ForestTile ])
      struct (Area "hills", Stamp.tagged [ "no-build" ] (Flow.canvas [ Flow.fill RockTile ]))
    |]
  }

let struct (grid, marks) =
  CellGrid2D.createHex {
  Orientation = HexOrientation.PointyTop
  Width = 30
  Height = 20
  Radius = 32f
  Origin = Vector2.Zero
}
  |> Flow.run kingdom
```

Authored KDL/XML documents also build hex maps. The surface, layers, and statements are geometry-neutral. Create the grid with `createHex` and run the `DocFlow.build` result through `Flow.run`.

## Adjacency and pathfinding

Each hex has six neighbors. `Hex2DSpatial` owns the hex math and takes the unified grid:

```fsharp
open Mibo.Layout

// walkability reads the grid directly
let isWalkable col row (grid: CellGrid2D<Tile>) =
  match CellGrid2D.get col row grid with
  | ValueSome tile -> tile.IsWalkable
  | ValueNone -> false

// the 6 neighbors of a cell, filtered to grid bounds
let walkableNeighbors col row grid =
  Hex2DSpatial.neighbors col row grid
  |> Array.filter (fun (struct (c, r)) -> isWalkable c r grid)
```

`Hex2DSpatial.inRange` returns every cell within N steps. Filter by walkability for movement budgets:

```fsharp
// every walkable cell within 4 steps of the start
let reachable col row steps grid =
  Hex2DSpatial.inRange col row steps grid
  |> Array.filter (fun (struct (c, r)) -> isWalkable c r grid)
```

`floodFill` walks neighbors while a predicate holds. Use it for territory, auras, and alarm zones:

```fsharp
let territory = Hex2DSpatial.floodFill col row (fun x y -> isOwned x y) grid
```

`findPath`, `spiral`, `ring`, `distance`, and `worldToCell` cover the rest. See the API reference.

## Rendering

Walk the visible cells and submit one sprite per tile:

```fsharp
grid
|> CellGrid2D.iterVisible (int screenLeft) (int screenTop) (int screenRight) (int screenBottom)
  (fun col row tile ->
    let pos = CellGrid2D.getWorldPos col row grid

    buffer
      .sprite(
        SpriteState.create(
          textureFor tile,
          Rectangle(pos.X, pos.Y, cellWidth, cellHeight),
          sourceOf tile
        ),
        layer = tileLayer tile
      )
      .drop())
```

`textureFor`, `sourceOf`, `tileLayer`, and `cellWidth`/`cellHeight` are game values.

For 3D hex maps, draw with `InstancedRenderContext`. The piece geometry, the height, and the span rules are unchanged. See [3D from 2D](three-d.html).

## Performance

- Flat array storage: O(1) cell access.
- Struct voption: no heap allocation per cell.
- Use `iterVisible` for gameplay rendering.
- Use `generate` for procedural content.
