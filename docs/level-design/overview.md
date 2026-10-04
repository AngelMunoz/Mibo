---
title: Level Design Overview
category: Level Design
categoryindex: 8
index: 1
---

# Level Design

Mibo ships one grid-backed map model with two authoring channels: F# values
([Code-First Maps](code-first.html)) and KDL/XML documents
([Authored Maps](authored.html)). Both build the same `CellGrid2D<'T>` and
the same landmarks, and both stop at cells: your game draws them.

## The map vocabulary

| Term | Meaning |
|---|---|
| **Cell** | One position on the grid. Your game defines the cell type `'T`. |
| **Grid** | `CellGrid2D<'T>` — the flat array of cells, square or hex. |
| **Stamp** | A reusable F# value with a footprint and a paint. Flow composes stamps. |
| **Word** | A name for a cell value, used by documents: `fill grass`. |
| **Kernel** | A named per-cell rule, used by documents through `generate`. |
| **Element** | A named body of statements with an optional size, used by documents. |
| **Surface** | The game's frozen tables of words, kernels, and elements. |
| **Layer** | One grid in a stack, painted bottom first. |
| **Span** | How many cells one drawn instance covers: `Span(4, 2)`, `Radius 2`. |
| **Occupancy** | The build's answer to "which instance owns this cell". |
| **Landmarks** | The rectangles element names resolve to, plus tag and cell queries. |

The **domain** is what the map is: words, kernels, elements, layers, and
spans. The **infrastructure** is what your game implements to consume a built
map: the cell type, the surface, the build call, the queries, the render
context, and the stack. [3D from 2D](three-d.html) walks the infrastructure
end to end.

## Pick a path

| I want to… | Page |
|---|---|
| Author in F#, end to end | [Code-First Maps](code-first.html) |
| Author in KDL or XML, end to end | [Authored Maps](authored.html) |
| Drop to raw grid operations | [The Layout Escape Hatch](layout.html) |
| Stack layers | [Layers](layers.html) |
| Cover many cells with one instance | [Instances and Occupancy](instances.html) |
| Build a 3D map | [3D from 2D](three-d.html) |
| Work with hexes | [Hex Grids](hex.html) |

## Grid storage

All 2D maps — square or hex — live in one storage type. Geometry is a
configuration, not an API family:

| | Square | Hex |
|---|---|---|
| **Storage** | `CellGrid2D<'T>` | `CellGrid2D<'T>` (hex geometry) |
| **Build** | `CellGrid2D.create` | `CellGrid2D.createHex` |
| **Cursor** | `GridSection2D<'T>` | `GridSection2D<'T>` |
| **World space** | `Vector2` | `Vector2` (staggered rows or columns) |
| **Spatial queries** | `Grid2DSpatial` | `Hex2DSpatial` |

Cells hold `'T voption`: `ValueSome content` or `ValueNone` (empty), a struct
option with no heap allocation. `CellGrid2D.getWorldPos` converts a cell to
world space for rendering.

## How a map is built and drawn

1. Create the grid: `CellGrid2D.create` or `CellGrid2D.createHex`.
2. Paint it: `Flow.run` in F#, or `DocFlow.buildLayers` for a document.
3. Query it: `Landmarks` answers names, tags, and cell queries; `Occupancy`
   answers which instance owns a cell.
4. Draw it: a 2D sprite loop ([Buffer & Commands](../graphics2d/buffer-and-commands.html)),
   or `InstancedRenderContext` for a footprint grid
   ([GPU Instancing](../graphics3d/instancing.html)).

Build at load, query per frame. `Flow.run`, `DocFlow.build*`, and
`Occupancy.scan` never run in the frame loop.

## 2D and 3D

Use 2D layout for side-scrolling platformers, top-down RPGs and roguelikes,
isometric games, and tile-based puzzles.

3D maps author as 2D plus column height: the footprint is a `CellGrid2D`,
each tile carries a `Height`, and walls become tall columns, ramps become
stepped heights. A **span** scales one instance over several cells in the
grid plane, so one model covers a 2×2 deck or a 16×6 plaza. See
[3D from 2D](three-d.html).

Use **hex geometry** for strategy and tactics games, wargames, and any game
where six-directional adjacency matters.

## Getting started

- **[Code-First Maps](code-first.html)** — the Flow authoring DSL; start here.
- **[Authored Maps](authored.html)** — the same levels in KDL or XML.
- **[3D from 2D](three-d.html)** — height, spans, the stack, and the instance draw.
- **[The Layout Escape Hatch](layout.html)** — raw grid and section operations.
- **[Migrating to Mibo v6](../migration-to-v6.html)** — the old-to-new mapping
  for the retired hex, 3D, layered, and stamp APIs.
- **[V5-and-below archive](../v5/index.html)** — frozen docs for the retired APIs.
