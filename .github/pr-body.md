## Summary

Two changes in this PR:

### 1. `Cmd.ofMsg` Optimization — Zero-Alloc Message Dispatch

Added a `Msg of 'Msg` case to the `Cmd<'Msg>` struct DU, eliminating the delegate allocation that `Cmd.ofMsg` previously incurred.

**Before:**
```fsharp
let inline ofMsg(msg: 'Msg) : Cmd<'Msg> =
  Single(Effect<'Msg>(fun dispatch -> dispatch msg))  // allocates
```

**After:**
```fsharp
let inline ofMsg(msg: 'Msg) : Cmd<'Msg> = Msg msg  // zero-alloc
```

Key wins:
- `Cmd.ofMsg` is now allocation-free
- `Cmd.map` on a `Msg` stays allocation-free (`Msg msg -> Msg(f msg)`)
- `execCmd` dispatches `Msg` directly without invoking a delegate
- `batch` and `batch2` preserve the `Msg` case in their fast paths
- `deferNextFrame` and `split` convert `Msg` to an effect when needed

### 2. Test Split — `Mibo.Core.Tests` Project

Extracted 11 backend-agnostic test files into a new `Mibo.Core.Tests` project that references only `Mibo.Core` (no Raylib dependency).

**Mibo.Core.Tests (503 tests):**
- ElmishTests, HeadlessTests
- LayoutTests, HexGridTests, HexLayoutTests, LayeredHexTests
- HexGrid3DTests, HexLayout3DTests, LayeredHex3DTests
- Spatial2DTests, Spatial3DTests

**Mibo.Raylib.Tests (390 tests):**
- Graphics2DTests, Graphics3DTests
- AnimationTests, Animation3DTests
- CameraTests, RenderBufferTests, MathTests
- InputMapperTests (uses internal `buildActions` from Raylib)
- Layout3DTests (uses `CellGridRenderer3D` from Raylib)

## Test Results

- **Mibo.Core.Tests:** 503 passed, 0 failed
- **Mibo.Raylib.Tests:** 390 passed, 0 failed
- **Total:** 893 tests passing
