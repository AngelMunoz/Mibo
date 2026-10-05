---
title: Layered Grids (retired)
index: 11
---

> **⚠ Archived V5-and-below docs.** These pages cover the retired APIs and
> the earlier releases. They are frozen. The current docs live at the
> [site root](../../index.html); the V6 map guides start at
> [Level Design Overview](../../level-design/overview.html).

# Layered grids (retired)

`LayeredGrid2D`, `LayeredLayout`, `LayeredHexGrid`, `LayeredHexLayout`, and
their 3D siblings are obsolete. A layered grid is a dictionary of grids, and
game code owns the dictionary. The [v6 migration guide, section 4](../../migration-to-v6.html#4-Replace-layered-grids-with-your-own-dictionary)
walks the change.

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

For independent per-cell attributes (terrain under items), prefer one grid
whose tile is a record: `Tile = { Ground: GroundKind; Prop: PropKind voption }`.
One array walk beats N parallel grids.
