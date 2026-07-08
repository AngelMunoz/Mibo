---
title: MonoGame type quirks
category: Documentation
categoryindex: 0
index: 1
---

# MonoGame type quirks (and how the backends differ)

Most snippets in this documentation use the **raylib** backend's types by default
(`System.Numerics` vectors, `Raylib_cs.Color`/`Rectangle`). On the **MonoGame**
backend the native types are different (`Microsoft.Xna.Framework.*`), and a few of
those differences are silent — code that compiles against one backend will not
compile (or renders wrong) against the other.

This page is the straight-to-the-point reference for those divergences. Read it
once before wiring up a MonoGame project; reach for it whenever a snippet fails
to compile or a value looks off.

## The one rule: math types

The vector/matrix/color/rectangle types are **not** the same type on the two
backends. When a snippet shows a bare `Vector2(...)`/`Color(...)`/`Rectangle(...)`,
the type it resolves to depends on which namespace is open.

| Type | raylib backend | MonoGame backend |
|------|----------------|------------------|
| 2D vector | `System.Numerics.Vector2` | `Microsoft.Xna.Framework.Vector2` |
| 3D vector | `System.Numerics.Vector3` | `Microsoft.Xna.Framework.Vector3` |
| 4D vector / quaternion | `System.Numerics.Vector4` / `Quaternion` | `Microsoft.Xna.Framework.Vector4` / `Quaternion` |
| Matrix | `System.Numerics.Matrix4x4` | `Microsoft.Xna.Framework.Matrix` |
| Color | `Raylib_cs.Color` | `Microsoft.Xna.Framework.Color` |
| Rectangle | `Raylib_cs.Rectangle` (**float**) | `Microsoft.Xna.Framework.Rectangle` (**int**) |

### `Mibo.Core` always speaks `System.Numerics`

`Mibo.Core`'s layout engines (`CellGrid2D`, `LayeredGrid2D`, `CellGrid3D`, hex
variants), spatial indices, input-delta types, and 3D light records
(`AmbientLight3D`/`DirectionalLight3D`/…) take `System.Numerics.Vector2`/
`Vector3` on **both** backends. They are backend-agnostic and reference no
graphics type.

This is the single most common MonoGame stumble. A MonoGame project usually has
`open Microsoft.Xna.Framework`, so a bare `Vector2(...)` resolves to XNA's vector
and the Core call fails (`FS0193` / a type mismatch):

```fsharp
open Microsoft.Xna.Framework

// Compiles on raylib; FAILS on MonoGame — bare Vector2 is XNA's:
let grid = CellGrid2D.create 100 50 (Vector2(32f, 32f)) Vector2.Zero

// Correct on MonoGame — qualify the Core-facing vector explicitly:
let grid =
    CellGrid2D.create 100 50
        (System.Numerics.Vector2(32f, 32f)) System.Numerics.Vector2.Zero
```

**Rule of thumb:** for any `Mibo.Core`/`Mibo.Layout`/`Mibo.Layout3D` call, spell
out `System.Numerics.Vector2`/`Vector3`. Backend-specific APIs (each backend's
`Camera2D.create`/`Camera3D`, `SpriteState`, `TextState`, `Draw.*` geometry) take
that backend's native type, so a bare `Vector2(...)` is correct there as long as
the matching namespace is open.

`Mibo.Core` also ships a backend-neutral `Mibo.Color` (byte RGBA) used by the 3D
light records; convert it to the backend color at the draw edge if you need a
native `Color`.

## `Rectangle` is float on raylib, int on MonoGame

`Raylib_cs.Rectangle` stores four `float32` fields; `Microsoft.Xna.Framework.
Rectangle` stores four `int` fields. The same literal means different things:

```fsharp
// raylib (float Rectangle) — what the 2D docs usually show:
let dest = Rectangle(100f, 100f, 32f, 32f)
SpriteState.create(tex, dest, Rectangle(0f, 0f, 32f, 32f))

// MonoGame (int Rectangle) — pixel coordinates:
let dest = Rectangle(100, 100, 32, 32)
SpriteState.create(tex, dest, Rectangle(0, 0, 32, 32))
```

This affects every `Draw.*` geometry argument and `SpriteState`/`TextState`
destination rectangle on the 2D stack. If a snippet uses `100f`/`32f` literals
inside a `Rectangle(...)`, it is raylib-only — drop the `f` suffixes on MonoGame.

## `Color` literals

Named colors (`Color.Red`, `Color.SkyBlue`, …) work on both backends. Constructing
a color from components differs:

- **raylib** `Raylib_cs.Color` — byte components (`Color(255uy, 0uy, 0uy)`).
- **MonoGame** `Microsoft.Xna.Framework.Color` — int `0–255` (`Color(255, 0, 0)`)
  or float `0.0–1.0` (`Color(1.0f, 0.0f, 0.0f)`).

Prefer named colors when you can; verify the overload you need in the
[API reference](reference/index.html).

## `IAssets`: same namespace, different types and paths

`IAssets` lives in the **`Mibo.Elmish`** namespace on **both** backends (raylib:
`Mibo.Raylib/Assets.fs`; MonoGame: `Mibo.MonoGame/Assets.fs`). You resolve it the
same way:

```fsharp
let assets = GameContext.getService<IAssets> ctx
```

What differs is what the loaders **return** and the **path convention**:

| | raylib | MonoGame |
|---|--------|----------|
| `Texture` | `Raylib_cs.Texture2D` | `Microsoft.Xna.Framework.Graphics.Texture2D` |
| `Font` | `Raylib_cs.Font` (a `.ttf` file) | `SpriteFont` (compiled `.spritefont`) |
| `Sound` | `Raylib_cs.Sound` | `SoundEffect` |
| `Model` | `Raylib_cs.Model` | `Model` |
| `Effect` | — | `Effect` (compiled `.mgfx`) |
| Path | **raw file on disk, with extension** (`"sprites/player.png"`) | **content-pipeline name, no extension** (`"sprites/player"`) |

See [Assets](assets.html) for the full loader table and the animation
double-load caveat (MonoGame needs the raw `.glb` via Assimp because the content
pipeline drops animation data).

## Window size: `ctx.WindowWidth` / `ctx.WindowHeight`

The current drawable window size is on `GameContext`:

```fsharp
let w = ctx.WindowWidth
let h = ctx.WindowHeight
```

These update on resize. Do **not** reach for `ctx.GameConfig.Width/Height` — that
is the config-time struct record passed to `Program.withConfig`, not a live value,
and `GameContext` does not expose it at runtime. (The old pre-v2 MonoGame path
read `ctx.GraphicsDevice.Viewport`; that is no longer how you get the size.)

## MonoGame-only divergences to plan for

| Concern | raylib | MonoGame |
|---------|--------|----------|
| Host | `RaylibGame<_,_>` | `MiboGame<_,_>` (with a `MonoGameProgram` wrapper) |
| Input mapper | `RaylibProgram.withInputMapper` | `MonoGameProgram.withInputMapper` |
| Content pipeline | none — load raw files | `.mgcb` → `.xnb` content pipeline |
| Custom shaders | GLSL | HLSL (`.fx` compiled to `.mgfx`) |
| Texture filtering | property of the texture: `Texture.filter TextureFilter.Point` | property of the draw: `Draw.setSamplerState layer SamplerState.PointClamp` |
| Default font | `Raylib.GetFontDefault()` | none — load `assets.Font "..."` |
| `Camera3D` FOV | **degrees**, no explicit near/far | **radians**, explicit near/far planes |
| 3D animated model | load `.glb` once (animations included) | double-load: `assets.Model` (XNB mesh) + `assets.AnimatedMesh`/`ModelAnimations` (raw `.glb` via Assimp) |
| Pipeline class | `ForwardPbrPipeline(shadowBiasConfig=, shadowAtlasConfig=)` | `ForwardPipeline(shadowBias=, shadowAtlas=)` (different field names) |

## Portability tip

If you want game logic that survives a backend switch, pin the **Core-facing**
math (anything that touches `Mibo.Core`/layout/spatial/lights) to
`System.Numerics` even on MonoGame. Convert to the backend's vector/matrix/color
types only at the view/draw edge. This keeps your model, update, and level-design
code backend-neutral and avoids conversion boilerplate at the Core boundary.
