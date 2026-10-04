---
title: Level Design Overview
category: Level Design
categoryindex: 8
index: 1
---

# Level Design

Mibo builds maps from a `CellGrid2D<'T>`. Two channels author the same grid: F# values ([Code-First Maps](code-first.html)) and KDL/XML documents ([Authored Maps](authored.html)). Both stop at cells; the game draws them.

## Map terms

| Term | Meaning |
|---|---|
| **Cell** | One grid position. The game defines the cell type `'T`. |
| **Grid** | `CellGrid2D<'T>`: the cell array, square or hex. |
| **Stamp** | A reusable F# value with a footprint and a paint. |
| **Word** | A name for a cell value, used by documents: `fill grass`. |
| **Kernel** | A named per-cell rule, used by documents through `generate`. |
| **Element** | A named body of statements with an optional size. |
| **Surface** | The game's frozen tables of words, kernels, and elements. |
| **Layer** | One grid in a stack, painted bottom first. |
| **Span** | How many cells one drawn instance covers: `Span(4, 2)`, `Radius 2`. |
| **Occupancy** | The build's answer to "which instance owns this cell". |
| **Landmarks** | The rectangles element names resolve to, plus tag and cell queries. |

The **domain** is the map data: words, kernels, elements, layers, spans. The **infrastructure** is the game side: the cell type, the surface, the build call, the queries, the render context, the stack. [3D from 2D](three-d.html) walks the infrastructure.

## Pick a path

| Goal | Page |
|---|---|
| Author in F# | [Code-First Maps](code-first.html) |
| Author in KDL or XML | [Authored Maps](authored.html) |
| Use raw grid operations | [The Layout Escape Hatch](layout.html) |
| Stack layers | [Layers](layers.html) |
| Cover many cells with one instance | [Instances and Occupancy](instances.html) |
| Build a 3D map | [3D from 2D](three-d.html) |
| Work with hexes | [Hex Grids](hex.html) |

## Grid storage

Square and hex maps share one storage type. Geometry is a configuration.

| | Square | Hex |
|---|---|---|
| **Storage** | `CellGrid2D<'T>` | `CellGrid2D<'T>` (hex geometry) |
| **Build** | `CellGrid2D.create` | `CellGrid2D.createHex` |
| **Cursor** | `GridSection2D<'T>` | `GridSection2D<'T>` |
| **World space** | `Vector2` | `Vector2` (staggered rows or columns) |
| **Spatial queries** | `Grid2DSpatial` | `Hex2DSpatial` |

Cells hold `'T voption`: `ValueSome content` or `ValueNone`. `CellGrid2D.getWorldPos` converts a cell to world space.

## Build and draw order

1. Create the grid with `CellGrid2D.create` or `CellGrid2D.createHex`.
2. Paint it with `Flow.run`, or build a document with `DocFlow.buildLayers`.
3. Query it: `Landmarks` answers names, tags, and cells; `Occupancy` answers the instance that owns a cell.
4. Draw it: a 2D sprite loop ([Buffer & Commands](../graphics2d/buffer-and-commands.html)), or `InstancedRenderContext` for a footprint grid ([GPU Instancing](../graphics3d/instancing.html)).

Run steps 1 to 3 at load. Step 4 runs per frame.

## 2D and 3D

2D layout fits side-scrolling platformers, top-down RPGs and roguelikes, isometric games, and tile-based puzzles.

A 3D map is a 2D footprint plus a `Height` per cell: walls are tall columns, ramps are stepped heights. A span scales one instance over several cells, so one model covers a 2×2 deck or a 16×6 plaza. See [3D from 2D](three-d.html).

Hex geometry fits strategy and tactics games, wargames, and games with six-directional adjacency.

## Guides

- [Code-First Maps](code-first.html): the Flow DSL.
- [Authored Maps](authored.html): KDL and XML documents.
- [3D from 2D](three-d.html): height, spans, the stack, the instance draw.
- [The Layout Escape Hatch](layout.html): raw grid and section operations.
- [Migrating to Mibo v6](../migration-to-v6.html): the old-to-new mapping.
- [V5-and-below archive](../v5/index.html): frozen docs for the retired APIs.
