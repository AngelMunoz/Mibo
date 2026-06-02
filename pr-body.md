# Add 3D Hex Grid with Layout DSL and Layered Grid

## Summary

Adds `HexGrid3D<'T>` — a 3D hex grid with hexagonal positioning in the XZ plane and linear layer height on the Y axis. Supports both **PointyTop** and **FlatTop** orientations. Provides feature parity with the existing `CellGrid3D` and `Layout3D` modules.

## What's New

### HexGrid3D (`Mibo.Layout3D.HexGrid3D`)

- `HexGrid3D<'T>` struct with flat `'T voption[]` storage
- Separate `HexSize` (hex radius in XZ) and `LayerHeight` (Y spacing) parameters
- Module functions: `create`, `set`, `get`, `clear`, `getWorldPos`, `iter`, `iterVolume`
- Both `PointyTop` and `FlatTop` orientations supported

### HexLayout3D DSL (`Mibo.Layout3D.HexLayout3D`)

- `HexGrid3DSection<'T>` struct mirroring `GridSection3D<'T>`
- Full DSL matching `Layout3D` module: `run`, `section`, `padding`, `paddingEx`, `center`, `flowX`, `flowY`, `flowZ`, `set`, `setIfEmpty`, `repeatX`, `repeatY`, `repeatZ`, `column`, `fill`, `clear`, `floorHex`, `wallXY`, `wallYZ`, `shell`, `edges`, `line`, `sphere`, `cylinder`, `generate`, `generateHexLayer`, `generateXY`, `generateYZ`, `iter`, `map`, `replace`, `replaceScatter`, `scatter3D`, `scatterHexLayer`, `scatterXY`, `scatterYZ`, `scatterShell`, `scatterEdges`, `scatterStamp`, `checker3D`, `checkerHexLayer`, `checkerXY`, `checkerYZ`, `checkerShell`
- All layout functions operate in logical space; spatial info is only used by coordinate conversion functions

### LayeredHexGrid3D (`Mibo.Layout3D.LayeredHexGrid3D`)

- `LayeredHexGrid3D<'T>` with `Dictionary<int, HexGrid3D<'T>>` layers
- `LayeredHexLayout3D.layer` for composable per-layer DSL

### HexGrid3DRenderer (`Mibo.Layout3D.HexGrid3DRenderer`)

- `render`, `renderVolume`, `renderWithIndices` — basic rendering
- `renderInstanced`, `renderVolumeInstanced` — GPU instanced rendering using existing `InstancedRenderContext`

## Design Decisions

- **Spatial info on grid**: Follows existing Mibo pattern (`CellGrid3D` has `CellSize`). Grid includes `HexSize` and `LayerHeight` for simplicity.
- **Hex positioning in XZ plane**: Hexagons lay flat in XZ plane, Y axis is linear layer stacking.
- **Separate HexSize/LayerHeight**: Hex radius and vertical spacing are independent parameters.
- **Logical space layout**: All DSL functions operate on logical coordinates (col, row, layer). Spatial info is only used by `getWorldPos`, `iterVolume`, and renderer functions.
- **Hex adjacency**: Checker patterns follow hex grid adjacency, not simple `(col + row) % 2`.

## Tests

New tests covering both orientations:

- **HexGrid3DTests**: create, set/get, clear, getWorldPos (pointy-top & flat-top), iter
- **HexLayout3DTests**: DSL composition, geometry (fill, clear, floorHex, line), procedural (generate, iter), transformation (replace, map), flow
- **LayeredHex3DTests**: layered access (pointy-top & flat-top)

Full suite: 530 tests passing.

## Files Changed

| File | Change |
|---|---|
| `src/Mibo.Raylib/Layout3D/HexGrid3D.fs` | New — core hex grid 3D type |
| `src/Mibo.Raylib/Layout3D/HexLayout3D.fs` | New — layout DSL |
| `src/Mibo.Raylib/Layout3D/LayeredHex3D.fs` | New — layered hex grid 3D |
| `src/Mibo.Raylib/Layout3D/Renderer3D.fs` | Added `HexGrid3DRenderer` module |
| `src/Mibo.Raylib/Mibo.Raylib.fsproj` | Added 3 new files |
| `src/Mibo.Raylib.Tests/HexGrid3DTests.fs` | New — core grid tests |
| `src/Mibo.Raylib.Tests/HexLayout3DTests.fs` | New — DSL tests |
| `src/Mibo.Raylib.Tests/LayeredHex3DTests.fs` | New — layered tests |
| `src/Mibo.Raylib.Tests/Mibo.Raylib.Tests.fsproj` | Added 3 new test files |
