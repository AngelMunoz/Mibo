---
title: Mibo v5 and Below Archive
index: 0
---

# Mibo v5 and below docs — archived

These pages cover the retired APIs and the releases before Mibo v6. They are
kept as a frozen snapshot, so users on older releases can still find matching
docs. Nothing here is in the sidebar.

**Looking for current docs?** Start at the [site root](../index.html), or go
straight to [Level Design Overview](../level-design/overview.html) for maps.
The current authoring model is [Code-First Maps](../level-design/code-first.html)
plus [Authored Maps](../level-design/authored.html); the older grid families
(`CellGrid3D`, `Layout3D`, the layered grids, the stamp libraries, `HexGrid`,
and `HexLayout`) are retired.

## Old to new

| Archived page | Current replacement |
|---|---|
| [3D Layout Engine](legacy-grids/3d-core.html) | [3D from 2D](../level-design/three-d.html) |
| [Building Outdoor Terrain](legacy-grids/terrain.html) | [Code-First Maps](../level-design/code-first.html) + `Height` per column |
| [Building Interior Spaces](legacy-grids/interior.html) | [Code-First Maps](../level-design/code-first.html) + `Height` per column |
| [Hex Grid Layout (3D)](legacy-grids/3d-hex.html) | [Hex Grids](../level-design/hex.html) + `Height` per cell |
| [Layered Grids](legacy-grids/layered-2d.html) | A dictionary of grids you own; see [Layers](../level-design/layers.html) |
| [Building Platformer Levels](legacy-stamps/platformer.html) | Flow styles; see [Code-First Maps](../level-design/code-first.html) |
| [Building Top-Down Levels](legacy-stamps/topdown.html) | Flow styles; see [Code-First Maps](../level-design/code-first.html) |
| [HexLayout (2D)](legacy-stamps/hex-layout-2d.html) | [Hex Grids](../level-design/hex.html) |
| [Migrating to v2](migrate/migration-to-v2.html) | [Migrating to v6](../migration-to-v6.html) |
| [Migrating to v4](migrate/migration-to-v4.html) | [Migrating to v6](../migration-to-v6.html) |
| [Migrating to v5](migrate/migration-to-v5.html) | [Migrating to v6](../migration-to-v6.html) |

The [v1 (raylib-only) archive](../v1/index.html) holds the original release
docs.
