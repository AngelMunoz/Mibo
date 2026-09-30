---
title: Flow - Level Authoring
category: Level Design
categoryindex: 8
index: 2
---

# Flow: Authoring Levels with Grids

Flow is Mibo's authoring DSL for grid-backed maps. It borrows from HTML and CSS layout:

- Elements (`Stamp`) declare their footprint in cells — no hand-counted coordinates.
- Containers distribute space: `Flow.row`/`Flow.column` are flexbox, `Flow.grid` is CSS grid with template areas, `Flow.dock` is absolute anchoring.
- Styles paint content: `Flow.fill`, `Flow.border`, `Flow.noise`, and friends are the CSS properties.
- Landmarks are the semantic layer: `Stamp.named` and `Stamp.tagged` mark regions, and the build reports their rectangles — the `<header>`/`<main>`/safe-zone of your map.

One document paints the tiles and describes the level's meaning. Gameplay reads the meaning, not the paint.

## The contract: build once, query per frame

Everything in Flow runs when the level builds (`Flow.run`/`Flow.build`) — never per frame. Build the document at startup, keep the returned landmarks, and answer per-frame questions with cell queries:

```fsharp
open Mibo.Layout

let struct (grid, marks) = gridValue |> Flow.run myDocument

// per frame, no allocation
if Flow.isTag "no-build" x y marks then ...
```

Hoist the dictionary lookup in tight loops with `Flow.tryTagGrid`.

## A first document

Grid template areas read like CSS `grid-template-areas`. Each zone becomes a named landmark:

```fsharp
let harbour =
  Flow.grid {
    Cols = [ Fixed 12; Weight 1f; Fixed 8 ]
    Rows = [ Fixed 6; Weight 1f; Fixed 4 ]
    Gap = 0
    Areas = [
      "shore shore shore"
      "woods plaza rise"
      "shore shore shore"
    ]
    Places = [
      "shore", Stamp.named "shore" (Flow.canvas [ Flow.fill Sand ])
      "woods", Stamp.tagged [ "no-build" ] (Flow.canvas [ Flow.noise 40 7 Tree ])
      "plaza", Flow.canvas [ Flow.fill Path; Flow.border 0 Wall ]
      "rise", Flow.canvas [ Flow.fill Rock; Flow.scatterBorder 6 3 Rock ]
    ]
  }

let struct (grid, marks) =
  CellGrid2D.create 24 14 (Vector2(32f, 32f)) Vector2.Zero
  |> Flow.run (Flow.overlay [ harbour ])

// the shore rect feeds spawn math directly
let shoreRect = Flow.tryPosition "shore" marks
```

## Flexbox: rows, columns, expand

`Flow.row` and `Flow.column` lay children out with gaps, cross-axis alignment, justification, wrapping, and flex-style space sharing:

```fsharp
let statusBar =
  Flow.row FlowOpts.Default [
    Stamp.box 6 1 [ Flow.fill 1 ]                 // fixed child
    Stamp.expand (Flow.canvas [ Flow.fill 2 ])    // flex-grow: shares the leftover
    Stamp.box 4 1 [ Flow.fill 3 ]
  ]
```

Only `row` and `column` honor `expand`; every other container and combinator rejects it at build time instead of ignoring it.

## Docking: anchored elements

`Flow.docked` pins an element to its container's edges — walls, headers, full-bleed strips:

```fsharp
let wall =
  Stamp.box 0 1 [ Flow.fill Wall ]             // zero footprint: stretches over the axis
  |> Flow.docked (Dock.StretchX ||| Dock.Bottom) 0
```

A zero footprint dimension always means "stretch this axis", with or without the `Stretch` flags.

## Styles are the CSS properties

| Style | CSS analogue | Paints |
|---|---|---|
| `Flow.fill c` | `background` | every cell |
| `Flow.border c` / `Flow.rect b f` / `Flow.corners c` | `border` | edges |
| `Flow.checker a b` / `Flow.checkerBorder a b` | alternating background | checker pattern |
| `Flow.noise count seed c` / `Flow.noiseBy count seed gen` | scatter | sparse props |
| `Flow.texture gen` | `background-image` | generated content per cell |
| `Flow.clumps count seed paint` | — | small stamp clusters |
| `Flow.cell x y c` / `Flow.repeatX n c` / `Flow.repeatY n c` | the atom / `repeat-x` / `repeat-y` | runs of cells |
| `Flow.line` / `Flow.circle` / `Flow.polygon` | `clip-path` shapes | geometric paint |
| `Flow.scatterBorder` / `Flow.scatterLine` | dashed border | weathered edges |
| `Flow.replace a b` / `Flow.weather a b p seed` | remap | content rewriting |
| `Flow.setIfEmpty x y c` | `:empty` | conditional paint |
| `Flow.clear()` | erased region | nothing |
| `Flow.map f` | derive pass | rewrite existing cells |

## Landmarks: the semantic layer

- `Stamp.named "spawn"` — one rectangle, unique names, `Flow.tryPosition` reads it.
- `Stamp.tagged [ "no-build" ]` — many elements per tag, `Flow.taggedRects` lists them.
- Every tagged rectangle rasterizes into a per-tag bit grid: `Flow.isTag "no-build" x y marks` answers cell queries with one dictionary lookup and one array read.
- `Flow.region` marks an extent without painting.
- `Landmarks.scanTiles` derives tags from painted tiles for irregular regions (noise-carved woods, scattered props).

Rectangles record the painted intersection with their container, so spawn math never reads outside the level.

## Hex grids author identically

Hexagons are a storage configuration, not a separate API. Build the grid with hex geometry and run the same document — authoring is cell-space, so zones, docks, and landmarks all work unchanged:

```fsharp
let struct (grid, marks) =
  CellGrid2D.createHex CellGeometry.PointyTopHex 32f 24 14 Vector2.Zero
  |> Flow.run (Flow.overlay [ harbour ])
```

Geometry affects two things only: world positions (`CellGrid2D.getWorldPos` staggers rows or columns) and spatial queries (`Hex2DSpatial` replaces `Grid2DSpatial`). Reported rectangles are offset-space bounding boxes.

## 3D levels author as 2D plus column height

Author the footprint as a Flow document and give each tile a height — the industry-standard heightmap approach. Walls become height-2 columns, ramps become stepped heights, and the vertical axis stays out of the authoring model. The 3D grid modules (`CellGrid3D`, `Layout3D`, and friends) are obsolete.

## When you need pixel-perfect control

Flow deliberately hides coordinates. When you want them — exact index math, manual section surgery — the `Layout` module remains open and fully supported; every Flow style is a `Layout` pipeline underneath. Reach for it consciously; the docs favor Flow.
