---
title: Flow - Level Authoring
category: Level Design
categoryindex: 8
index: 2
---

# Flow: Authoring Levels with Grids

Flow is Mibo's authoring DSL for grid-backed maps. If you have styled
an HTML page, you already know the model:

- Elements (`Stamp`) declare their footprint in cells. Containers place
  children for you, so you never hand-count offsets.
- Containers distribute space. `Flow.row`/`Flow.column` are flexbox,
  `Flow.grid` is CSS grid with template areas, `Flow.dock` is absolute
  anchoring.
- Styles paint content. `Flow.fill`, `Flow.border`, `Flow.noise` are
  your background, border, and scatter brushes.
- Landmarks are the semantic layer. `Stamp.named` and `Stamp.tagged`
  mark regions, and the build reports their rectangles.

One document paints the tiles and describes what the level means.
Gameplay code reads the description, not the paint.

For member-by-member signatures, see the API reference. This page
teaches the patterns.

## The contract: build once, query per frame

Everything in Flow runs when the level builds (`Flow.run`), never per
frame. Build at startup, keep the landmarks, and answer per-frame
questions with cell queries:

```fsharp
open Mibo.Layout

let struct (grid, marks) = gridValue |> Flow.run myDocument

// per frame, no allocation
if Flow.isTag "no-build" { X = x; Y = y } marks then ...
```

In tight loops, hoist the lookup with `Flow.tryTagGrid` and read the bit
grid directly.

## Pattern: zones from a template

You know your map's shape before you know its contents. Draw it as a
grid template, one name per zone, like `grid-template-areas`:

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
  |> Flow.run (Flow.overlay [ harbour ])
```

What you get:

- Resize the map and the zones stretch. `Weight` tracks share the
  slack. `Fixed` tracks keep their size. No coordinate to update.
- Each zone paints itself. `Flow.canvas` fills whatever area its
  container assigns, so a zone grows or shrinks without touching its
  styles.
- The `shore` zone is **named**. `Flow.tryPosition "shore" marks` gives
  you its rectangle for spawn math.

### Track sizes and explicit slots

Beyond `Fixed`, tracks size three ways. `Auto` sizes a track to the largest
footprint of its span-1 places at construction (`Weight` shares the assigned
length after `Fixed`/`Auto`/`Percent`, and `Percent` takes a fraction of
it). A place reports no footprint on a zero axis — `Flow.canvas`, or any
element sized `0` on one side — so it contributes nothing to an `Auto`
track on that axis and the track collapses. A place that spans several
tracks shares its footprint over the `Auto` tracks it covers, so a span
that no single-track place can size still paints its content.

Places can also skip the template entirely: `Slot (col, row, colspan,
rowspan)` mounts an element at explicit track indices. Slots may overlap;
paint order is the `Places` order, later places on top:

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

A slot with negative indices, a span below one, or a span past the last
track throws at construction — the same fail-loud rule as every other
placement mistake.

## Pattern: rooms, corridors, doors

Dungeons, bases, and towns need the same three pieces: a room, a
connection, and a door. Compose them with `Flow.row` and sized boxes:

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

A door is a border cell with a `Door` tile. A secret door is the same
cell with your `SecretDoor` tile. The structure does not change; the
tile carries the meaning.

For a band pinned to a container edge (a wall, a shoreline, a cliff),
layer a strip over the document. A strip occupies no flow space, so it
goes inside `overlay`, not into a row or column:

```fsharp
// a one-cell wall across the whole bottom edge
let walled level =
  Flow.overlay [ level; Flow.strip Dock.Bottom 1 [ Flow.fill Wall ] ]
```

## Pattern: landmarks drive the gameplay

Painting tiles is half the document. The other half tells your systems
where things are.

**Spawn entities from named rectangles.** The rect is the answer; you
do not compute it:

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

**Enforce rules with tags.** Use one tag per rule, checked wherever the
rule applies:

```fsharp
// building placement: is this cell legal?
if Flow.isTag "no-build" { X = px; Y = py } marks then reject ()

// AI: treat every "danger" rect as an alarm zone
for rect in Flow.taggedRects "danger" marks do
    if contains rect px py then soundAlarm ()
```

**Mark extents without painting.** Use it for trigger volumes, music
regions, and camera zones:

```fsharp
Flow.region [ "boss-arena" ] 6 6   // lays out like a box, paints nothing
```

**Derive tags from tiles** when the region is irregular (a noise-carved
wood, scattered debris). `Landmarks.scanTiles` marks every cell your
extractor reports:

```fsharp
let tileTags _ _ (tile: Tile) =
    if tile.Kind = Tree then seq { "flammable" } else Seq.empty

let struct (grid, marks) = gridValue |> Flow.build tileTags document

// "flammable" now answers cell queries like any tagged zone
Flow.isTag "flammable" { X = fx; Y = fy } marks
```

Rectangles record the painted intersection with their container, so
spawn math never reads outside the level.

## Pattern: organic detail from a seed

A plain fill looks artificial. Break it with the scatter styles. Every
scatter style takes a `Seed`. The same document and seed build the same
level on every run:

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

`Count` caps how many cells the style paints. `Seed` picks them. Use a
fixed seed for a curated level. Derive the seed from the save file for
per-run variety.

`Flow.scatter` brings the same determinism to whole elements: sized
children place at seeded, non-overlapping origins over the assigned area,
first-fit in child order. Same seed and same container, same level:

```fsharp
// a rock ring around the spawn cave — boulders never overlap
let rocks = Flow.group 5 5 [ Flow.scatter 13 [ boulder; boulder; boulder ] ]
```

The footprint of a scatter is the largest child's, so mount it inside a
sized context (`group`, a grid area, a docked stretch) to choose the
region. Name or tag the children as usual — their scattered rectangles
report through the landmarks.

## Pattern: build your own vocabulary

Stamps are values. Parameterize them and give them names from your
game. The level document then names concepts, not coordinates:

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

Name the pieces after what they mean in your game (`tavern`,
`bossLair`, `ambushAlcove`).

## Pattern: a semantic document for your game

HTML gives every page the same structure words: `header`, `main`,
`footer`, `aside`. Flow lets your game define the same kind of words
for its levels. Write one module of constructors that mean game
concepts, and levels read like documents.

This replaces the retired `Platformer` and `TopDown` stamp libraries:
the same idea, written with Flow and owned by your game (see
[Migrating to Mibo v6](../../migration-to-v6.html)).

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

A level then reads like a document. Nobody counts cells:

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

The landmarks are the queryable structure of that document, like the
DOM of a page:

```fsharp
Flow.tryPosition "riverside" marks        // the district's rectangle
Flow.taggedRects "shop" marks             // every shop, newest first
Flow.isTag "home" { X = x; Y = y } marks  // per-cell queries
```

Rules that keep the vocabulary useful:

- Constructors take meaning, not coordinates: `shop "bakery"`, not
  `box 6 4 at 12 3`.
- Tags are the query API. Tag by gameplay role (`"shop"`, `"defended"`,
  `"no-build"`), and systems query the roles.
- Grow the module per game or per biome. When a constructor needs
  variants, give it an argument instead of a new name.

## Pattern: bands, bars, and frames

Use the flexbox tools for HUD-shaped layouts and level bands:

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

A zero footprint dimension always means "stretch this axis", with or
without the `Stretch` flags. Only `row` and `column` honor `expand`.
Every other container rejects it at build time instead of ignoring it.

## Which style for which job

Pick by the job. Full signatures live in the API reference:

| Job                    | Styles                                                            |
| ---------------------- | ---------------------------------------------------------------- |
| Cover an area          | `fill`, `fillRect rect` (a local sub-rectangle), `texture` (per-cell generator), `checker` |
| Edges                  | `border`, `rect b f`, `corners`, `checkerBorder`, `scatterBorder` |
| Sparse props           | `noise`, `noiseBy`, `clumps` (paints small stamps)                |
| Lines and shapes       | `line`, `circle`, `polygon`, `scatterLine`                        |
| Rewrite existing paint | `replace`, `weather`, `map`, `setIfEmpty`, `clear`                |

Styles that take several settings receive a named spec record
(`ScatterSpec`, `CircleSpec`, `ScatterLineSpec`, `WeatherSpec`,
`InsetSpec`) or a `CellPoint`. Every value is labeled at the call
site, so coordinates cannot be transposed.

## Hex grids author identically

Hexagons are a storage configuration, not a separate API. Build the
grid with hex geometry and run the same document. Authoring is
cell-space, so zones, docks, and landmarks all work unchanged:

```fsharp
let struct (grid, marks) =
  CellGrid2D.createHex {
  Orientation = HexOrientation.PointyTop
  Width = 24
  Height = 14
  Radius = 32f
  Origin = Vector2.Zero
}
  |> Flow.run (Flow.overlay [ harbour ])
```

Geometry affects two things only: world positions
(`CellGrid2D.getWorldPos` staggers rows or columns) and spatial queries
(`Hex2DSpatial` replaces `Grid2DSpatial`). Reported rectangles are
offset-space bounding boxes.

## 3D levels author as 2D plus column height

Author the footprint as a Flow document and give each tile a height:
the heightmap approach. Walls become tall columns, ramps become stepped
heights, and the vertical axis stays out of the authoring model. The 3D
grid modules (`CellGrid3D`, `Layout3D`, and friends) are obsolete. See
[Migrating to Mibo v6](../../migration-to-v6.html).

## When you need pixel-perfect control

Flow hides coordinates on purpose. Two primitives give them back exactly:

**`Flow.at x y stamp`** places an element at an exact cell offset of its
container. It occupies no flow space, so it mounts inside `overlay` and grid
areas like `docked` does. A zero dimension of the wrapped stamp stretches
from that origin to the container's far edge:

```fsharp
// a 2x1 prop at exactly (3, 5), over a base layer
let prop = Flow.at 3 5 (Stamp.tagged [ "loot" ] (Stamp.box 2 1 [ Flow.fill Chest ]))

// a road from (2, 1) to the right edge, two cells tall
let road = Flow.at 2 1 (Stamp.box 0 2 [ Flow.fill Road ])
```

**Per-side dock insets.** `DockSpec.Inset` is an `InsetSpec` (`Left`, `Top`,
`Right`, `Bottom`), so a docked element can sit at different distances from
each edge. Edge anchors honor their own side; stretching docks span the
container minus the two insets of their axis:

```fsharp
// a HUD band: 2 off the left edge, 1 off the right, 1 above the bottom
let hud =
  Flow.docked {
    Anchor = Dock.StretchX ||| Dock.Bottom
    Inset = { Left = 2; Top = 0; Right = 1; Bottom = 1 }
    Stamp = Stamp.box 0 2 [ Flow.rect Steel Slate ]
  }
```

**`Flow.fillRect rect content`** fills a sub-rectangle of a box in local
coordinates — the local-area counterpart of `fill`, for rooms, roads, and
platforms inside a bigger element:

```fsharp
let road = Stamp.box 40 22 [ Flow.fill Grass; Flow.fillRect { X = 3; Y = 3; W = 1; H = 7 } Path ]
```

When you want exact index math or manual section surgery beyond these, the
`Layout` module remains open and fully supported. Every Flow style is a
`Layout` pipeline underneath.

An old `Layout` stamp is a section pipeline. `Stamp.sized` wraps it as
a Flow element, and it joins any container:

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

The wrapped stamp paints with local coordinates: the `Layout` calls
address the element's own box, not the level. `Stamp.create` does the
same for paints that return `unit` instead of a section.

For the full section-pipeline vocabulary (`section`, `center`,
`repeatX`, the scatter ops), see the [2D Layout
Engine](core.html). Use raw `Layout` if you really need "pixel perfect" control of the backing grid. For the majority of use cases Flow should be enough.
