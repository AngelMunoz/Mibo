---
title: Hex Grids
category: Level Design
categoryindex: 8
index: 8
---

# Hex Grids

Hex grids trade the simplicity of rectangles for **equal-distance neighbors**
and more natural-looking terrain. If your game needs 6-directional movement,
strategy-map aesthetics, or organic-looking levels, hexes are the right tool.

**Hex is a storage configuration, not a separate API.** Hexes live in
`CellGrid2D` with hex geometry (`CellGrid2D.createHex`), the same grid type
as squares. Author hex maps with [Code-first maps](code-first.html) — zones,
docks, and landmarks work identically; geometry affects world positions and
spatial queries only. The `HexGrid` and `HexLayout` modules are retired; the
[archive](../v5/legacy-stamps/hex-layout-2d.html) keeps their reference.

## When to use hex vs rect

| Use Case | Rect Grid | Hex Grid |
|----------|-----------|----------|
| Platformers, top-down RPGs | ✅ Natural fit | ❌ Awkward edges |
| Strategy / tactics games | ❌ Diagonal advantage | ✅ Equal neighbors |
| Wargames, board game ports | ❌ Looks wrong | ✅ Authentic |
| Procedural terrain | ❌ Grid lines show | ✅ Organic feel |
| 4/8-directional movement | ✅ Built-in | ❌ Needs adaptation |
| 6-directional movement | ❌ Impossible | ✅ Natural |

Hex grids are harder to align with screen edges and rectangular sprites.
If your game is tile-based with axis-aligned art, stay with rect. If
adjacency or aesthetics matter more, hex wins.

## Orientation: pointy vs flat top

Hexes come in two rotations. The choice affects both visuals and coordinate math:

```
    Pointy Top              Flat Top
      /\                    ______
     /  \                  /      \
    /    \                /        \
    \    /                \        /
     \  /                  \      /
      \/                    ------
```

- **Pointy top**: Strategy maps, tactical RPGs. Rows align horizontally.
- **Flat top**: Isometric-style games, board game adaptations. Columns align vertically.

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

`Radius` is the hex radius (center to corner). `CellSize` stores the hex's
bounding box. `CellGrid2D.hexOrientation` and `CellGrid2D.hexRadius` read the
hex parameters back.

## Coordinate system

Hex grids use **offset coordinates**, a standard column/row pair, with a
visual offset to make hexes tessellate. Odd rows (pointy top) or odd columns
(flat top) shift by half a hex width. `getWorldPos` applies the offset, so
you address cells as `(col, row)` and never think about the stagger.

## Basic operations

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

For large maps, process only the cells on screen. `CellGrid2D.iterVisible`
culls hex grids too. The window is orientation-aware and padded one cell on
each axis. Bounds are int world coordinates:

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

## Authoring with Flow

Author hex levels with the [Flow DSL](code-first.html) — the same document
vocabulary as square grids:

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

Authored documents (KDL/XML) also build hex maps: the surface, layers, and
statements are geometry-neutral. Create the grid with `createHex` and run the
same `DocFlow.build` result through `Flow.run`.

## Adjacency and pathfinding

Each hex has exactly 6 neighbors. `Hex2DSpatial` owns the hex math and takes
the unified grid:

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

`Hex2DSpatial.inRange` returns every cell within N steps. Filter it by
walkability for movement budgets:

```fsharp
// every walkable cell within 4 steps of the start
let reachable col row steps grid =
    Hex2DSpatial.inRange col row steps grid
    |> Array.filter (fun (struct (c, r)) -> isWalkable c r grid)
```

For rule-driven regions (territory, auras, alarm zones), `floodFill` walks
neighbors while your predicate holds:

```fsharp
let territory = Hex2DSpatial.floodFill col row (fun x y -> isOwned x y) grid
```

`findPath`, `spiral`, `ring`, `distance`, and `worldToCell` cover the rest of
the hex queries; see the API reference.

## Rendering

Render with the same pattern as square grids — one sprite per populated cell,
culled to the viewport:

```fsharp
grid
|> CellGrid2D.iterVisible (int screenLeft) (int screenTop) (int screenRight) (int screenBottom)
    (fun col row tile ->
        let pos = CellGrid2D.getWorldPos col row grid

        buffer.sprite(
            SpriteState.create(
                textureFor tile,
                Rectangle(pos.X, pos.Y, cellWidth, cellHeight),
                sourceOf tile
            ),
            layer = tileLayer tile
        )
        |> ignore)
```

`textureFor`, `sourceOf`, `tileLayer`, and `cellWidth`/`cellHeight` are
your game's values.

For 3D hex maps, draw the grid with `InstancedRenderContext`; the piece
geometry, the height, and the span rules are unchanged. See
[3D from 2D](three-d.html).

## Performance

- **Flat array storage**: O(1) cell access, cache-friendly iteration.
- **Struct voption**: no heap allocation per cell.
- **Use `iterVisible`**: always cull for gameplay rendering.
- **Use `generate`**: for procedural content, it is faster than individual `set` calls.

The retired `HexLayout`/`HexGrid` reference lives in the
[archive](../v5/legacy-stamps/hex-layout-2d.html).
