---
title: Migrating to Mibo v6
category: Migrating
categoryindex: 7
index: 4
---

# Migrating to Mibo v6

## What v6 is

v6 puts every grid behind one storage type and one authoring DSL:

- **One grid type.** `CellGrid2D<'T>` stores squares and hexes. Hex is a
  geometry setting (`CellGrid2D.createHex`), not a separate API.
- **One authoring model.** The [Flow DSL](level-design/2d/flow.html)
  authors both geometries: grid template areas, flexbox rows and
  columns, docks, and landmark queries (named rectangles and tags).
- **3D becomes a heightmap.** The vertical axis moves into the tile
  (`{ Kind: BlockKind; Height: int }`). The grid stores the footprint.

Nothing breaks compilation today. Every retired module carries
`[<Obsolete>]` and still works. You can migrate one level at a time
while the compat surface still compiles.

One rule covers every migration in this page: **the grid stores a 2D
footprint, and everything vertical becomes data in the tile.**

## 1. Recompile and read the warnings

Build your project. Every FS0044 warning marks a call site that must
migrate. The obsolete message names the replacement. Nothing else
changes: the kernel, both runtimes, and both backends keep their
namespaces and signatures.

## 2. Migrate hex grids to `CellGrid2D.createHex`

Hex grids are `CellGrid2D` with hex geometry. Storage, the Flow DSL,
and landmarks stay the same. Geometry affects two things only: world
positions (rows or columns stagger) and spatial queries
(`Hex2DSpatial` replaces `Grid2DSpatial`).

### Storage

```fsharp
// before
let grid = HexGrid.create 20 15 32f Vector2.Zero PointyTop

// after
let grid =
  CellGrid2D.createHex {
    Orientation = HexOrientation.PointyTop
    Width = 20
    Height = 15
    Radius = 32f
    Origin = Vector2.Zero
  }
```

### Cell access

`set`, `get`, `clear`, `iter`, and `getWorldPos` moved to `CellGrid2D`
with identical arguments and behavior. The grid's geometry applies the
hex stagger:

```fsharp
// before                          // after
HexGrid.set 5 3 tile grid          CellGrid2D.set 5 3 tile grid
HexGrid.get 5 3 grid               CellGrid2D.get 5 3 grid
HexGrid.getWorldPos 5 3 grid       CellGrid2D.getWorldPos 5 3 grid
```

### Culling

`CellGrid2D.iterVisible` culls hex grids too, with an
orientation-aware window. The bounds type changes: the retired
`HexGrid.iterVisible` took `float32` bounds, the unified one takes
`int`:

```fsharp
// before (float32 bounds)                     // after (int bounds)
HexGrid.iterVisible l t r b action grid         CellGrid2D.iterVisible
                                                  (int l) (int t) (int r) (int b)
                                                  action grid
```

### Pipelines

`HexLayout` pipelines map one-to-one onto the square `Layout` ops. Hex
storage runs them unchanged. You can also author with
[Flow](level-design/2d/flow.html):

```fsharp
// before: HexLayout
let board =
  HexGrid.create 12 10 48f origin FlatTop
  |> HexLayout.run(fun section ->
      section
      |> HexLayout.fill 0 0 12 10 Grass
      |> HexLayout.border 0 0 12 10 Water)

// after: the same Layout ops over hex storage
let board =
  CellGrid2D.createHex {
    Orientation = HexOrientation.FlatTop
    Width = 12
    Height = 10
    Radius = 48f
    Origin = origin
  }
  |> Layout.run(fun section ->
      section
      |> Layout.fill 0 0 12 10 Grass
      |> Layout.border 0 0 12 10 Water)
```

### Spatial queries

`Hex2DSpatial` keeps its signatures. Its grid parameters are
`CellGrid2D<'T>` now, so it no longer emits deprecation warnings:

```fsharp
// unchanged calls, no warnings
let steps = Hex2DSpatial.distance 5 3 9 7 board
let ring = Hex2DSpatial.ring 5 3 2 board
let path = Hex2DSpatial.findPath sc sr gc gr passable (fun _ _ _ _ -> 1f) board
```

`Hex2DSpatial.worldToCell`, `neighbors`, `inRange`, `spiral`, and
`floodFill` are unchanged as well.

## 3. Migrate 3D levels to footprint plus column height

The entire `Mibo.Layout3D` family is retired: `CellGrid3D`,
`GridSection3D`, the `Layout3D` DSL, `HexGrid3D` / `HexLayout3D`,
`Grid3DSpatial` / `Hex3DSpatial`, the layered 3D grids, and the 3D grid
renderers.

### The model shift

A `CellGrid3D<'T>` stored one `'T voption` per voxel. The vertical axis
was an index. The replacement stores one tile per footprint cell, and
the vertical extent rides **in the tile**:

```fsharp
// before: the voxel was the unit
type Cell = | Floor | Wall | Chest

// after: the column is the unit. Kind plus vertical extent.
type BlockKind =
  | Floor
  | Wall
  | Door
  | Brick
  | Stone
  | Crate
  | Step

type Column = { Kind: BlockKind; Height: int }

let floor = { Kind = Floor; Height = 0 }
let wall = { Kind = Wall; Height = 3 }
let door = { Kind = Door; Height = 2 }
```

What each old concept becomes:

| Before | After |
|---|---|
| `CellGrid3D<'T>` | `CellGrid2D<Column>` |
| The Y axis | `Column.Height` |
| `GridSection3D` / `Layout3D.section` | `GridSection2D` / `Layout.section`, or Flow containers |
| `Layout3D.fill` (volume) | `Flow.fill` / `Layout.fill` on the footprint |
| `Layout3D.shell` (hollow box) | `Flow.border` on the footprint, walls get a taller `Height` |
| `Layout3D.column` / `repeatY` | The `Height` field itself |
| `Layout3D.floorXZ` | `Flow.fill` at footprint level |
| `Layout3D.wallXY` / `wallYZ` | `Flow.border` / `Flow.line` footprints with wall columns |
| `Layout3D.scatter3D` / `scatterXZ` | `Flow.noise` / `Flow.scatterLine` on the footprint |
| `Layout3D.generate` | `Flow.texture` (callback decides kind and height per cell) |
| `Layout3D.sphere` / `cylinder` | No direct equivalent. Author the footprint (`Flow.circle`, `Flow.polygon`) and compute `Height` per column. |
| `Layout3D.checker3D` / planar checkers | `Flow.checker` on the footprint |
| `CellGrid3D.iterVolume` | `CellGrid2D.iterVisible` (world-space window, hex-aware) |
| `renderCellGridVolumeInstanced` / `renderHexGridVolumeInstanced` | The [volume-culled instancing pattern](#volume-culled-instanced-rendering) below |
| `Grid3DSpatial.findPath` (3D) | `Grid2DSpatial.findPath` on the footprint |

### A worked level

The retired docs built a dungeon from stamp composition. Before (from
the 3D Layout Engine page):

```fsharp
module Dungeon =
    let cell = room 5 5 5 FloorCell WallCell CeilingCell

    let corridor length =
        Layout3D.fill 0 0 0 length 3 3 FloorCell
        >> Layout3D.shell 0 0 0 length 3 3 WallCell

    let intersection =
        cell
        >> Layout3D.clear 2 0 2 1 5 1  // North door
        >> Layout3D.clear 2 0 0 1 5 1  // South door
        >> Layout3D.clear 0 2 2 5 1 1  // West door
        >> Layout3D.clear 0 2 0 5 1 1  // East door

level
|> Layout3D.run (fun section ->
    section
    |> Layout3D.section 0 0 0 Dungeon.cell
    |> Layout3D.section 5 1 0 (Dungeon.corridor 10)
    |> Layout3D.section 15 0 0 Dungeon.intersection)
```

After: the same level as one Flow document over height-carrying
columns. Rooms are grid areas, corridors are strips, walls are border
styles, and doors are cleared border cells:

```fsharp
type Column =
  | Floor
  | Wall of height: int
  | Door

let wall = Wall 3

// one 5x5 room: floor with a wall ring
let room = Stamp.box 5 5 [ Flow.fill floor; Flow.border wall ]

// a corridor: a 10x3 floor strip with a wall ring, open at the short
// sides' middle cells so rooms connect
let corridor =
  Stamp.box 10 3 [
    Flow.fill floor
    Flow.border wall
    Flow.cell { X = 0; Y = 1 } floor
    Flow.cell { X = 9; Y = 1 } floor
  ]

// the intersection: a room with all four doorways open
let intersection =
  Stamp.box 5 5 [
    Flow.fill floor
    Flow.border wall
    Flow.cell { X = 2; Y = 0 } door
    Flow.cell { X = 2; Y = 4 } door
    Flow.cell { X = 0; Y = 2 } door
    Flow.cell { X = 4; Y = 2 } door
  ]

let levelDoc =
  Flow.row FlowOpts.Default [ room; corridor; intersection ]

let grid = CellGrid2D.create 20 5 cellSize origin
let struct (level, marks) = grid |> Flow.run levelDoc
```

Height appears in gameplay and rendering. It never appears in the
layout math.

### World positions

Before, the grid gave you a `Vector3` per voxel. After, the grid gives
the footprint position (`Vector2`), and your renderer lifts it by the
column height:

```fsharp
// before
let worldPos = CellGrid3D.getWorldPos x y z grid   // Vector3

// after
let footprint = CellGrid2D.getWorldPos x y grid    // Vector2 (XZ)

let worldPos =
  Vector3(
    footprint.X,
    float32 column.Height * cellHeight,
    footprint.Y
  )
```

### Rendering

The retired `CellGridRenderer3D` / `HexGrid3DRenderer` and the
`renderCellGrid` / `renderHexGrid` buffer members are gone. The draw
surface stays: iterate your grid and feed `drawInstanced` your own
transforms. Per column, scale the unit block by its height:

```fsharp
// before
grid
|> CellGrid3D.iter (fun x y z content ->
    let worldPos = CellGrid3D.getWorldPos x y z grid
    spawnModel worldPos content)

// after: one instance per column, scaled to its height
let mutable n = 0

grid
|> CellGrid2D.iter (fun x y column ->
    let footprint = CellGrid2D.getWorldPos x y grid
    let height = float32 column.Height

    transforms.[n] <-
      Matrix4x4.CreateScale(cellW, height, cellD)
      * Matrix4x4.CreateTranslation(footprint.X, 0f, footprint.Y)

    n <- n + 1)

// raylib:  buffer.AddDrawInstanced(mesh, transforms, material, n, ValueNone)
// MonoGame: Draw3D.drawInstanced(mesh, transforms, material, n, ValueNone)
```

### Volume-culled instanced rendering

`renderCellGridVolumeInstanced` / `renderHexGridVolumeInstanced` (and
their `...WithEffect` variants) have direct replacements.
`InstancedRenderContext` now renders 2D footprint grids itself:
`RenderInstanced` for the whole grid and `RenderWindowInstanced` for a
world-space window (hex-aware). The `BoundingBox`'s XZ extent becomes
the window. The vertical axis is your column heights.

The window takes **`int` world coordinates**. The retired volume path
took `float32` world units, so cast your camera rect when you port:

```fsharp
// before: the retired voxel renderer drove the context
CellGridRenderer3D.renderVolumeInstanced ctx bounds grid buffer
CellGridRenderer3D.renderVolumeInstancedWithEffect ctx bounds grid shaderForKey buffer

// after: the context renders the footprint grid. The XZ extent of the
// old BoundingBox becomes the int world-space window.
ctx.RenderWindowInstanced(buffer, int left, int top, int right, int bottom, footprintGrid)
ctx.RenderWindowInstancedWithEffect(
  buffer, int left, int top, int right, int bottom, footprintGrid, shaderForKey)

// or through the Draw DSL (both backends resolve the witness)
Draw.renderFootprintInstanced(buffer, ctx, footprintGrid) |> ignore
Draw.renderFootprintWindowInstanced(buffer, ctx, int left, int top, int right, int bottom, footprintGrid)
|> ignore
```

The only construction change is the transform function. It receives
the column's **base** world position (the footprint position lifted to
`y = 0`), and you scale the unit block by the column's height there:

```fsharp
let columnTransform (basePos: Vector3) (col: Column) =
  Matrix4x4.CreateScale(cellW, float32 col.Height, cellD)
  * Matrix4x4.CreateTranslation(basePos)

let keyOf (col: Column) = col.Kind

let ctx =
  InstancedRenderContext(
    getKey = keyOf,
    getMeshesAndMaterial = meshesFor,
    getTransform = columnTransform)
```

Everything else behaves as before. `ResetFrameBuffers()` returns the
pooled snapshots each frame. Groups persist between frames, so only
`ResizeArray` growth allocates. One instanced draw is emitted per key.
A wall column with `Height = 3` is one instance scaled three cells
tall. The instance count follows footprint complexity, not volume.

### Pathfinding and spatial queries

`Grid3DSpatial.findPath` walked voxels. Walk the footprint instead. A
step is legal when the destination column is walkable (floor, or a
ramp whose height difference you accept):

```fsharp
// before
let path = Grid3DSpatial.findPath x1 y1 z1 x2 y2 z2 isWalkable grid

// after: paths over the footprint, heights in the predicate
let canEnter (x: int) (y: int) : bool =
  CellGrid2D.get x y grid
  |> ValueOption.map(fun c -> c.Kind = Floor)
  |> ValueOption.defaultValue false

// costFn: (fromX, fromY, toX, toY) -> step cost; 1f for uniform terrain
let path = Grid2DSpatial.findPath x1 y1 x2 y2 canEnter (fun _ _ _ _ -> 1f) grid
```

Ramps and stairs become footprint tiles with intermediate `Height`
values. A ramp of `rise 5` is a run of columns with heights `0..5`.
Your predicate accepts a step when the height difference is 1. The
retired [Terrain](level-design/3d/terrain.html) and
[Interior](level-design/3d/interior.html) stamp pages remain as
references for those shapes.

## 4. Replace layered grids with your own dictionary

`LayeredGrid2D` / `LayeredHexGrid` (and the 3D siblings) managed a
dictionary of grids keyed by layer index. A dictionary of grids is
something game code can own directly:

```fsharp
// before
let level =
  LayeredGrid2D.create 50 30 cellSize origin
  |> LayeredLayout.layer 0 (fun s -> s |> Layout.fill 0 0 50 30 Terrain)
  |> LayeredLayout.layer 1 (fun s -> s |> Layout.fill 2 2 10 8 Structures)

// after: your model owns the dictionary
let layers = Dictionary<int, CellGrid2D<Tile>>()

let layer index paint =
  let grid =
    match Dictionary.tryGetValue index layers with
    | ValueSome grid -> grid
    | ValueNone ->
        let grid = CellGrid2D.create 50 30 cellSize origin
        layers.[index] <- grid
        grid

  Layout.run paint grid

layer 0 (fun s -> s |> Layout.fill 0 0 50 30 Terrain) |> ignore
layer 1 (fun s -> s |> Layout.fill 2 2 10 8 Structures) |> ignore
```

For independent per-cell attributes (terrain under items), prefer one
grid whose tile is a record:
`Tile = { Ground: GroundKind; Prop: PropKind voption }`. One array
walk beats N parallel grids.

## 5. Replace the stamp libraries with Flow styles

Flow styles replace the stamp libraries' vocabulary. The retired pages
stay as pattern references.

| Retired | Replacement |
|---|---|
| `Platformer.box` / `platform` | `Stamp.box` + `Flow.fill` |
| `Platformer.stairs` / `slope` | A row of columns with stepped `Height` (`Flow.texture` picks the height) |
| `Platformer.pit` | `Flow.fill` with a low `Height` tile |
| `TopDown.room` | `Stamp.box` + `Flow.border` (see the worked level above) |
| `TopDown.corridor` / `wallSegment` | `Flow.strip` or a bordered `Stamp.box` between areas |
| `TopDown.doorway` | `Flow.cell` with a `Door` tile on the border |
| `Terrain.ground` / `plateau` / `pit` | `Flow.fill` / `Flow.noise` with height-carrying tiles |
| `Terrain.ramp` / `path` | Stepped-height `Flow.texture` / `Flow.line` |
| `Interior.room` / `corridor` | The worked level above |
| `Interior.stairs` / `shaft` / `pillar` / `window` | Height-carrying tiles. A window is a border cell with a short `Height`. |

A platformer level, before and after:

```fsharp
// before: Platformer stamps
level
|> Layout.run (fun s ->
    s
    |> Platformer.platform 4 6 8 2 Brick
    |> Platformer.stairs 10 0 4 5 Stone
    |> Platformer.box 20 0 6 4 Crate)

// after: Flow styles over height-carrying tiles
let level =
  Flow.row FlowOpts.Default [
    Stamp.box 8 2 [ Flow.fill { Kind = Brick; Height = 1 } ]
    Stamp.box 4 5 [ Flow.texture(fun x _ -> { Kind = Step; Height = x + 1 }) ]
    Stamp.box 6 4 [ Flow.fill { Kind = Crate; Height = 1 } ]
  ]
```

## 6. Migration checklist

1. Swap `HexGrid.create` for `CellGrid2D.createHex` with a `HexSpec`.
   `set`/`get`/`clear`/`iter`/`getWorldPos` calls rename to
   `CellGrid2D.*` with the same arguments.
2. Replace `HexLayout` pipelines with `Layout` over hex storage, or move
   the level into a Flow document.
3. Give 3D content a `Column`-style tile (`Kind` plus `Height`). Rebuild
   the level as a Flow document over `CellGrid2D`.
4. Move `getWorldPos` consumers to the footprint-plus-height formula.
5. Replace renderer calls with your own `iter` + `drawInstanced` loop
   (scaled unit blocks per column). For the volume-culled instanced
   renderers, follow [volume-culled instanced
   rendering](#volume-culled-instanced-rendering).
6. Replace 3D pathfinding with footprint pathfinding over
   `Grid2DSpatial` / `Hex2DSpatial`.
7. Replace layered grids with a dictionary you own, or fold the layers
   into the tile as fields.
8. Read [Flow - Level Authoring](level-design/2d/flow.html). Hex and
   3D-as-heightmap levels now author with the same DSL.
