---
title: Code-First Maps
category: Level Design
categoryindex: 8
index: 2
---

# Code-First Maps

Build a 2D map in F# with the `Flow` DSL. A `Stamp` is a block of cells with a size and a paint. Containers place stamps. Styles paint cells. Landmarks report the rectangles of named stamps.

[Authored Maps](authored.html) build the same levels from KDL or XML. [3D from 2D](three-d.html) adds per-cell height and spans.

## Build once, query per frame

`Flow.run` builds the level and returns the grid with the landmarks. Run it once at startup.

```fsharp
open Mibo.Layout

let struct (grid, marks) = gridValue |> Flow.run myDocument

// per frame, no allocation
if Flow.isTag "no-build" { X = x; Y = y } marks then ...
```

In tight loops, use `Flow.tryTagGrid` and read the bit grid directly.

## Grid template areas

`Flow.grid` takes `Cols`, `Rows`, and `Areas`. Each area name is a zone.

```fsharp
let harbour =
  Flow.grid {
    Cols = [| Fixed 12; Weight 1f; Fixed 8 |]
    Rows = [| Fixed 6; Weight 1f; Fixed 4 |]
    Gap = 0
    Areas = [|
      "shore shore shore"
      "woods plaza rise"
      "shore shore shore"
    |]
    Places = [|
      struct (Area "shore", Stamp.named "shore" (Flow.canvas [ Flow.fill Sand ]))
      struct (Area "woods", Stamp.tagged [ "no-build" ] (Flow.canvas [ Flow.noise { Count = 40; Seed = 7 } Tree ]))
      struct (Area "plaza", Flow.canvas [ Flow.fill Path; Flow.border Wall ])
      struct (Area "rise", Flow.canvas [ Flow.fill Rock; Flow.scatterBorder { Count = 6; Seed = 3 } Rock ])
    |]
  }

let struct (grid, marks) =
  CellGrid2D.create 24 14 (Vector2(32f, 32f)) Vector2.Zero
  |> Flow.run harbour
```

`Fixed` tracks keep their size. `Weight` tracks share the slack. `Flow.canvas` fills the area its container assigns, so a zone grows or shrinks with the map. Read the `shore` rectangle with `Flow.tryPosition "shore" marks`.

### Track sizes and slots

`Auto` sizes a track to the largest footprint of its span-1 places. `Weight` shares the length left after `Fixed`, `Auto`, and `Percent`. `Percent` takes a fraction of the assigned length. A place with a zero axis contributes nothing to an `Auto` track on that axis, so the track collapses. A place that spans several tracks shares its footprint over the `Auto` tracks it covers. The shared size does not subtract fixed tracks in the same span: a 6-wide place over `Fixed 10` and one `Auto` sizes the auto track to 6, so the span covers 16 cells of track space.

`Slot (col, row, colspan, rowspan)` places an element at explicit track indices. Slots may overlap. Later places paint on top.

```fsharp
Flow.grid {
  Cols = [| Fixed 12; Auto |]
  Rows = [| Fixed 6 |]
  Gap = 1
  Areas = [||]
  Places = [|
    struct (Slot (0, 0, 1, 1), Flow.canvas [ Flow.fill Path ])   // fixed column
    struct (Slot (1, 0, 1, 1), sidePanel)   // Auto track sizes to the panel
  |]
}
```

A negative index, a span below one, or a span past the last track throws at construction.

## Rooms, corridors, and doors

Compose a dungeon from sized boxes and `Flow.row`:

```fsharp
// one room: floor with a wall ring
let room = Stamp.box 5 5 [ Flow.fill Floor; Flow.border Wall ]

// a corridor: floor strip, wall ring, both ends open at the middle cell
let corridor =
  Stamp.box 10 3 [
    Flow.fill Floor
    Flow.border Wall
    Flow.cell { X = 0; Y = 1 } Floor
    Flow.cell { X = 9; Y = 1 } Floor
  ]

// a four-way junction: a room with the border opened on all sides
let junction =
  Stamp.box 5 5 [
    Flow.fill Floor
    Flow.border Wall
    Flow.cell { X = 2; Y = 0 } Door
    Flow.cell { X = 2; Y = 4 } Door
    Flow.cell { X = 0; Y = 2 } Door
    Flow.cell { X = 4; Y = 2 } Door
  ]

let dungeon = Flow.row FlowOpts.Default [ room; corridor; junction; corridor; room ]
```

A door is a border cell with a `Door` tile. A secret door uses a different tile. The structure does not change.

Pin a band to a container edge with `Flow.strip` inside `overlay`:

```fsharp
// a one-cell wall across the whole bottom edge
let walled level =
  Flow.overlay [ Flow.stretch level; Flow.strip Dock.Bottom 1 [ Flow.fill Wall ] ]
```

`overlay` and `group` children are full-bleed layers: `canvas`, `docked`, `at`, `strip`, and `stretch`. A sized child throws at construction. Mount a sized layout with `Flow.stretch`. Place an exact rectangle with `Flow.at` or `docked`.

## Landmarks

Spawn entities from named rectangles. The rectangle is the answer; do not compute it.

```fsharp
let struct (grid, marks) = gridValue |> Flow.run harbour

match Flow.tryPosition "shore" marks with
| ValueSome rect ->
  // spawn one crab per shore cell
  for y = rect.Y to rect.Y + rect.H - 1 do
    for x = rect.X to rect.X + rect.W - 1 do
      spawnCrab x y
| ValueNone -> failwith "the template promised a shore zone"
```

Enforce rules with tags. Use one tag per rule.

```fsharp
// building placement: is this cell legal?
if Flow.isTag "no-build" { X = px; Y = py } marks then reject ()

// AI: treat every "danger" rect as an alarm zone
for rect in Flow.taggedRects "danger" marks do
  if contains rect px py then soundAlarm ()
```

Mark extents without painting them. Use this for trigger volumes, music regions, and camera zones.

```fsharp
Flow.region [ "boss-arena" ] 6 6   // lays out like a box, paints nothing
```

Derive tags from tiles when the region is irregular. `Landmarks.scanTiles` marks every cell the extractor reports.

```fsharp
let tileTags _ _ (tile: Tile) =
  if tile.Kind = Tree then seq { "flammable" } else Seq.empty

let struct (grid, marks) = gridValue |> Flow.build tileTags document

// "flammable" now answers cell queries like any tagged zone
Flow.isTag "flammable" { X = fx; Y = fy } marks
```

Rectangles record the painted intersection with their container, so spawn math never reads outside the level.

## Layers

A level can use more than one grid: ground under decor, traffic over terrain, a trigger volume over both. `Flow.runLayers` is the plural of `Flow.run`. You create the grids. A layer is an array position; stamp i paints grid i, bottom first.

```fsharp
open Mibo.Layout

let grids =
  [|
    CellGrid2D.create 40 24 cellSize Vector2.Zero
    CellGrid2D.create 40 24 cellSize Vector2.Zero
  |]

let layers = grids |> Flow.runLayers [| ground; decor |]

let struct (groundGrid, groundMarks) = layers[0]
let struct (decorGrid, decorMarks) = layers[1]
```

Each layer gets its own landmarks registry, so an element name is unique per layer and may repeat across layers. `Flow.buildLayers` adds `Landmarks.scanTiles` per layer.

`runLayers` throws before it paints when the arrays differ in length, when the grids differ in width or height, or when a stamp asks for `Expand`. Hex grids run unchanged. The upper grid keeps its unpainted cells empty, so it draws transparent over the lower one.

Authoring a stack from a text document is [Layers](layers.html).

## Scatter

Scatter styles break up a plain fill. Every style takes a `Seed`. The same document and seed build the same level.

```fsharp
// sparse props over an area
Flow.noise { Count = 40; Seed = 7 } Tree

// scattered props where the tile depends on position
Flow.noiseBy { Count = 12; Seed = 9 } (fun x y -> pickRock x y)

// weathered edges: a dashed border
Flow.scatterBorder { Count = 6; Seed = 3 } Vine

// a worn path: a dashed line between two points
Flow.scatterLine { From = { X = 0; Y = 0 }; To = { X = 9; Y = 4 }; Count = 5; Seed = 2 } Gravel

// content that varies per cell (background-image)
Flow.texture (fun x y -> heightMap.[x, y])
```

`Count` caps the painted cells. `Seed` picks them. Use a fixed seed for a curated level. Derive the seed from the save file for per-run variety.

`Flow.scatter` places whole elements at seeded, non-overlapping origins, first fit in child order.

```fsharp
// a rock ring around the spawn cave
let rocks =
  Flow.group 5 5 [ Flow.stretch (Flow.scatter 13 [ boulder; boulder; boulder ]) ]
```

The footprint is the largest child's, so give the scatter a sized region: a grid area, `Flow.stretch` over a `group`, or a stretched dock. Name or tag the children to report their scattered rectangles. A child that fits nowhere fails the build and names the child.

`Flow.scatter` uses a fixed xorshift shuffle, so a seeded scatter builds the same level on every .NET version. The paint styles above use the BCL random generator, whose sequence can change between .NET versions. Use `Flow.scatter` when a level must reproduce byte for byte.

## Reusable vocabulary

Stamps are values. Parameterize them and name them after game concepts.

```fsharp
// sized, parameterized pieces
let guardPost (w: int) =
  Stamp.box w 3 [
    Flow.fill Floor
    Flow.border Wall
    Flow.cell { X = w / 2; Y = 0 } Door
  ]

let courtyard seed =
  Flow.canvas [
    Flow.fill Path
    Flow.noise { Count = 10; Seed = seed } Bush
  ]

// compose pieces into bigger pieces
let outpost = Flow.row FlowOpts.Default [ guardPost 4; courtyard 7; guardPost 4 ]

// ...and mark the composite like any element
let spawn = Stamp.tagged [ "spawn"; "defended" ] outpost
```

Name the pieces after what they mean in the game (`tavern`, `bossLair`, `ambushAlcove`).

## A level vocabulary module

Write one module of constructors that mean game concepts. Levels then read as calls, not coordinates.

```fsharp
/// the game's level vocabulary
module Town =
  // content pieces, like <p> and <figure>
  let room w h = Stamp.box w h [ Flow.fill Floor; Flow.border Wall ]
  let house = Stamp.tagged [ "home" ] (room 5 4)
  let shop kind = Stamp.tagged [ "shop"; kind ] (room 6 4)
  let park = Flow.canvas [ Flow.fill Grass; Flow.noise { Count = 6; Seed = 5 } Tree ]

  // structural bands, like <header> and <footer>.
  // zero width in a column stretches to full width
  let road = Stamp.box 0 2 [ Flow.fill Road ]
  let river = Stamp.box 0 3 [ Flow.fill Water ]

  // containers with meaning, like <section> and <article>
  let district name children =
    Flow.column { FlowOpts.Default with Gap = 1 } children
    |> Stamp.named name
```

```fsharp
let millbrook =
  Town.district "riverside" [
    Town.road
    Flow.row { FlowOpts.Default with Gap = 1 } [
      Stamp.expand Town.park
      Town.house
      Town.shop "bakery"
    ]
    Town.river
  ]

let struct (grid, marks) =
  CellGrid2D.create 30 16 (Vector2(32f, 32f)) Vector2.Zero
  |> Flow.run millbrook
```

Query the landmarks:

```fsharp
Flow.tryPosition "riverside" marks        // the district's rectangle
Flow.taggedRects "shop" marks             // every shop, newest first
Flow.isTag "home" { X = x; Y = y } marks  // per-cell queries
```

Rules:

- Constructors take meaning, not coordinates: `shop "bakery"`, not `box 6 4 at 12 3`.
- Tag by gameplay role (`"shop"`, `"defended"`, `"no-build"`) and query the roles.
- Grow the module per game or per biome. When a constructor needs variants, give it an argument.

## Bands, bars, and frames

Use `Flow.row` for HUD-shaped layouts and level bands.

```fsharp
// a minimap strip: fixed ends, flexible middle
let statusBar =
  Flow.row FlowOpts.Default [
    Stamp.box 6 1 [ Flow.fill 1 ]                 // fixed child
    Stamp.expand (Flow.canvas [ Flow.fill 2 ])    // takes the leftover space
    Stamp.box 4 1 [ Flow.fill 3 ]
  ]

// a wall pinned to the container's bottom edge
let wall =
  Flow.docked {
    Anchor = Dock.StretchX ||| Dock.Bottom
    Inset = InsetSpec.Zero
    Stamp = Stamp.box 0 1 [ Flow.fill Wall ]   // zero width: stretch the axis
  }
```

A zero footprint dimension means "stretch this axis", with or without the `Stretch` flags. Only `row` and `column` honor `expand`. Every other container rejects it at build time.

## Styles at a glance

| Job | Styles |
| --- | --- |
| Cover an area | `fill`, `fillRect rect` (a local sub-rectangle), `texture` (per-cell generator), `checker` |
| Edges | `border`, `rect b f`, `corners`, `checkerBorder`, `scatterBorder` |
| Sparse props | `noise`, `noiseBy`, `clumps` (paints small stamps) |
| Lines and shapes | `line`, `circle`, `polygon`, `scatterLine` |
| Rewrite existing paint | `replace`, `weather`, `map`, `setIfEmpty`, `clear` |

Styles with several settings take a named spec record (`ScatterSpec`, `CircleSpec`, `ScatterLineSpec`, `WeatherSpec`, `InsetSpec`) or a `CellPoint`. Every value is labeled at the call site.

## Hex grids

Hexagons are a storage configuration. Build the grid with hex geometry and run the same document. Authoring is cell-space, so zones, docks, and landmarks work unchanged.

```fsharp
let struct (grid, marks) =
  CellGrid2D.createHex {
  Orientation = HexOrientation.PointyTop
  Width = 24
  Height = 14
  Radius = 32f
  Origin = Vector2.Zero
}
  |> Flow.run harbour
```

Geometry affects world positions (`CellGrid2D.getWorldPos` staggers rows or columns) and spatial queries (`Hex2DSpatial` replaces `Grid2DSpatial`). Reported rectangles are offset-space bounding boxes. See [Hex Grids](hex.html).

## Draw the grid

Walk the visible cells and submit one sprite per tile. Tag each sprite with its `RenderLayer`.

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  buffer.beginCamera(model.Camera).drop()

  model.Grid
  |> CellGrid2D.iterVisible
    viewLeft
    viewTop
    viewRight
    viewBottom
    (fun x y tile ->
      let pos = CellGrid2D.getWorldPos x y model.Grid

      buffer
        .sprite(
          SpriteState.create(textureFor tile, destOf pos, sourceOf tile),
          layer = tileLayer tile
        )
        .drop())

  buffer.endCamera().drop()
```

`viewLeft`/`viewTop`/`viewRight`/`viewBottom` are the camera's world bounds as `int`s. `textureFor`, `destOf`, `sourceOf`, and `tileLayer` are game functions. The buffer sorts by layer, so one pass per layer draws back to front. See [Buffer & Commands](../graphics2d/buffer-and-commands.html) and the [Draw DSL](../draw-dsl.html).

For 3D maps, draw through `InstancedRenderContext`. See [3D from 2D](three-d.html).

## Exact placement

Flow hides coordinates. Three primitives give them back.

`Flow.at x y stamp` places an element at an exact cell offset. It occupies no flow space, so it mounts inside `overlay` and grid areas like `docked`. A zero dimension of the wrapped stamp stretches from that origin to the far edge.

```fsharp
// a 2x1 prop at exactly (3, 5), over a base layer
let prop = Flow.at 3 5 (Stamp.tagged [ "loot" ] (Stamp.box 2 1 [ Flow.fill Chest ]))

// a road from (2, 1) to the right edge, two cells tall
let road = Flow.at 2 1 (Stamp.box 0 2 [ Flow.fill Road ])
```

`DockSpec.Inset` is an `InsetSpec` (`Left`, `Top`, `Right`, `Bottom`). A docked element can sit at different distances from each edge. Stretching docks span the container minus the two insets of their axis.

```fsharp
// a HUD band: 2 off the left edge, 1 off the right, 1 above the bottom
let hud =
  Flow.docked {
    Anchor = Dock.StretchX ||| Dock.Bottom
    Inset = { Left = 2; Top = 0; Right = 1; Bottom = 1 }
    Stamp = Stamp.box 0 2 [ Flow.rect Steel Slate ]
  }
```

`Flow.fillRect rect content` fills a sub-rectangle of a box in local coordinates.

```fsharp
let road = Stamp.box 40 22 [ Flow.fill Grass; Flow.fillRect { X = 3; Y = 3; W = 1; H = 7 } Path ]
```

When exact index math or manual section surgery is needed, the `Layout` module stays available. Every Flow style is a `Layout` pipeline underneath. `Stamp.sized` wraps a section pipeline as a Flow element.

```fsharp
// a hand-written stamp: exact cell math
let campfire (s: GridSection2D<Tile>) =
    s
    |> Layout.fill 0 0 3 3 Grass
    |> Layout.set 1 1 Campfire
    |> Layout.set 0 1 Log
    |> Layout.set 2 1 Log

// the same stamp as a Flow element: 3x3 footprint
let camp = Stamp.sized 3 3 campfire

let restStop =
  Flow.row { FlowOpts.Default with Gap = 1 } [
    Stamp.tagged [ "camp" ] camp
    Stamp.box 4 3 [ Flow.fill Grass ]
  ]
```

The wrapped stamp paints with local coordinates: the `Layout` calls address the element's own box, not the level. `Stamp.create` does the same for paints that return `unit`.

For the full section-pipeline vocabulary, see [The Layout Escape Hatch](layout.html).

## Next

- [Authored Maps](authored.html) write the same levels in KDL or XML.
- [3D from 2D](three-d.html) adds `Height` per cell and `Span` per instance.
