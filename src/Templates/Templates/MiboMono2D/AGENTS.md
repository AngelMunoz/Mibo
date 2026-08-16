# AGENTS.md: Mibo MonoGame 2D Game

This is a **Mibo** game project. Mibo is an Elmish-based F# game framework.
This template targets the **MonoGame** backend (`Mibo.MonoGame`, host `MiboGame`)
and ships **three interchangeable thin clients** sharing one library:

- `src/`: shared library (net10.0): `Library.fs` (module `MiboMono2D`, all game
  logic, the view, and a `create()` composition root that builds the
  `MonoGameProgram`) + `MiboMono2D.fsproj`. References `Mibo.MonoGame` +
  `MonoGame.Framework.Native` (the compile-time, backend-neutral types) and the
  shared content pipeline.
- `Content/`: shared MonoGame content pipeline (`Content.mgcb`). Each thin
  client builds it via `MonoGame.Content.Builder.Task`, so the same assets are
  available in all backends.
- `DesktopGL/`: thin client. Adds `MonoGame.Framework.DesktopGL` (**OpenGL**),
  references `../src/MiboMono2D.fsproj` and the shared content, calls
  `MiboMono2D.create()` and runs `MiboGame`.
- `DesktopVK/`: thin client (**Vulkan**, cross-platform). Adds
  `MonoGame.Framework.Native` + the `MonoGame.Runtime.*.Vulkan` runtime packages
  and sets `<MonoGamePlatform>DesktopVK</MonoGamePlatform>`. Same wiring as
  DesktopGL.
- `WindowsDX12/`: thin client (net10.0-windows, **DirectX 12**). Adds
  `MonoGame.Framework.Native` + `MonoGame.Runtime.Windows.DX12`, sets
  `<MonoGamePlatform>WindowsDX12</MonoGamePlatform>`, `[<STAThread>]`,
  `app.manifest`. Same wiring as DesktopGL.

Mibo is an Elmish-based F# game framework that ships **composable building blocks**
for games: grids and level layout, input mapping, lighting, particles, the
`System` pipeline, a deferred command-buffer renderer, and more. **Before creating
a new sub-module, check the docs**; the building block you are about to write
likely already exists. Compose existing pieces; do not reinvent them.

> **Default renderer.** `create()` wires a `Renderer2D` over a **deferred
> command buffer**: the view fills a `RenderBuffer2D` with `Command2D` values
> (`Draw.fillRect`/`sprite`/`text`/…), and the renderer sorts them by layer,
> applies camera transforms, and auto-batches the GPU draws. Layer ordering,
> per-section shaders, post-processing, and 2D lighting (`LightContext2D`) are
> already there; reach for `Draw.*` commands rather than immediate-mode calls.
> See [2D Rendering](https://angelmunoz.github.io/Mibo/graphics2d/overview.html),
> [2D Buffer & Commands](https://angelmunoz.github.io/Mibo/graphics2d/buffer-and-commands.html),
> and [2D Lighting](https://angelmunoz.github.io/Mibo/graphics2d/lighting.html).

## MonoGame type quirks: read this first

MonoGame type quirks. You **MUST** read the
[MonoGame type quirks](https://angelmunoz.github.io/Mibo/monogame-types.html)
document before writing or extending any code in this project. MonoGame's native
types (`Microsoft.Xna.Framework.Vector2`/`Matrix`/`Color`/`Rectangle`) differ
from the raylib types most snippets use, and `Mibo.Core` layout/spatial/light
APIs take `System.Numerics` on **both** backends, so a bare `Vector2(...)`
resolves to the wrong type and fails to compile. Read that doc, then qualify your
Core-facing vectors explicitly and use MonoGame's native types at the draw edge.

## Project structure you must keep (cross-backend split)

MonoGame's platform host packages (`...DesktopGL`, the `MonoGame.Runtime.*`
packages) **cannot coexist in one project**. That is why there are four
projects. Keep the split:

- **Game logic, model, messages, `update`, the `view`, and `create()` live in
  `src/` only.** They must stay backend-neutral (compile against
  `MonoGame.Framework.Native`). Never reference `DesktopGL`, `DesktopVK`, or
  `WindowsDX12` from the shared lib.
- **Thin clients stay thin.** They just call `MiboMono2D.create()` (which already
  returns a `MonoGameProgram` with the content root configured) and
  `new MiboGame<_,_>().Run()`; they no longer call `MonoGameProgram.ofProgram`
  themselves. Asset / `Content.RootDirectory` configuration is centralized in
  `create()`. Put no game logic in a client.
- All clients build from the same shared source. **Do not fork logic per
  backend.** If you need a backend-specific service (audio, animation),
  define an interface in the shared lib (or `Mibo.Core`) and implement it once,
  injecting it through an `Env`/composition-root record like `create()` does.

## Content pipeline

Add assets under `Content/` and reference them in `Content/Content.mgcb` (asset
names have no extension). They build into each client's output directory and
load at runtime via `GameContext.getService<IAssets> ctx`. You can edit the
pipeline with `dotnet mgcb-editor` (run `dotnet tool restore` first to install
it from `dotnet-tools.json`).

## Architecture you must follow: routed sub-systems

As this game grows, do **not** let the `update` function become a dumping ground.
Split the game into independent sub-systems coordinated by a **router**. This is
the [Composable Systems](https://angelmunoz.github.io/Mibo/mvu/composable-systems.html)
pattern and it is mandatory once the game outgrows a single `update`.

- **The root `update` is a router, not game logic.** It routes messages to
  sub-systems and translates their emitted events into `Cmd<Msg>` for consumers.
  It contains no game logic, only dispatch and translation.
- **Each sub-system owns its slice.** A sub-system owns its model, its message
  type, and its update. It mutates/returns **only its own state**. It never
  imports another sub-system's update or reaches into another sub-system's model.
- **Cross-system communication is declarative.** Sub-systems never call each
  other. They return **Events** (what happened) / **Intents** (what should
  happen): pure data. The router translates each into `Cmd<Msg>` for the
  relevant systems. The emitter does not know (or import) its consumers.
- **Read access goes through a read-only query; mind the hot path.**
  - *Cold path* (event-driven, turn-based): a closure query record the router
    builds per-message (`{ UnitAt: Vector2 -> UnitId voption; ... }`).
  - *Hot path* (per-`Tick`, real-time): pass **direct values**, not closures.
    A closure-bearing query built inside `Tick` allocates every frame and the
    JIT will not inline across it. Pass `playerPos`, `enemies`, etc. directly.
- **`Cmd.map` lifts sub-commands.** Sub-system commands are `Cmd<SubMsg>`; lift
  them into the root `Msg` with `Cmd.map SubMsg`.

When several sub-systems must run every frame in a fixed order, compose them
with the [System pipeline](https://angelmunoz.github.io/Mibo/mvu/system.html)
(`System.start` → `pipeMutable` → `snapshot` → `pipe` → `finish`). The snapshot
call is a **compile-enforced** boundary: mutation phases run before it, readonly
query phases after it. See [Scaling Mibo](https://angelmunoz.github.io/Mibo/mvu/scaling.html)
for when each rung pays off. You can ship a lot of games at Level 2 or 3.

## Pointers by topic

> **Read before you build.** These links are not suggestions. Before writing or
> extending code in any area below, open the linked doc(s) for that area and
> verify whether Mibo already ships a building block for what you need. Do not
> re-implement existing functionality; compose what is already there.

**Core loop / the code you're looking at**
- How the MVU loop, `Tick`, and dispatch modes work → [Elmish](https://angelmunoz.github.io/Mibo/mvu/elmish.html)
- The `Program` builder pipeline (`mkProgram`/`withConfig`/`withTick`/`withRenderer`/`withSubscription`) → [Programs](https://angelmunoz.github.io/Mibo/mvu/program.html)
- Side-effect command API (`Cmd.ofMsg`/`ofAsync`/`batch`/`map`/`deferNextFrame`) → [Commands](https://angelmunoz.github.io/Mibo/mvu/commands.html)
- Continuous external event sources (`Sub`, diffing, `Sub.batch`) → [Subscriptions](https://angelmunoz.github.io/Mibo/mvu/subscriptions.html)
- Wiring backend services through an `Env` composition root → [Service Composition](https://angelmunoz.github.io/Mibo/mvu/services.html)

**Growing the game**
- Turn input into semantic actions (`InputMap`/`ActionState`/`InputMapper.subscribe`) → [Input](https://angelmunoz.github.io/Mibo/input.html)
- Load textures/fonts/sounds/models (`IAssets`, caching) → [Assets](https://angelmunoz.github.io/Mibo/assets.html)
- When the world is bigger than the screen (scroll, zoom, screen↔world, picking) → [Camera](https://angelmunoz.github.io/Mibo/camera.html)
- Where the upgrade ladder is and which rung to pick → [Scaling Mibo](https://angelmunoz.github.io/Mibo/mvu/scaling.html)
- Architecture: composable sub-systems, events/intents, snapshot boundary → [Composable Systems](https://angelmunoz.github.io/Mibo/mvu/composable-systems.html)

**2D rendering (this is a 2D project)**
- The `Draw.*` DSL the view already uses (`fillRect`/`sprite`/`text`/`line`/`circle`) → [2D Buffer & Commands](https://angelmunoz.github.io/Mibo/graphics2d/buffer-and-commands.html)
- Layers, cameras, the deferred `RenderBuffer2D` overview → [2D Rendering](https://angelmunoz.github.io/Mibo/graphics2d/overview.html)
- Sprite-sheet animation for a real character → [Animation](https://angelmunoz.github.io/Mibo/animation.html)
- Torch/shadow/normal-map lighting (`LightContext2D`) → [2D Lighting](https://angelmunoz.github.io/Mibo/graphics2d/lighting.html)
- Effects without GC (`Particle2D`, `fadeAndCompact`) → [2D Particles](https://angelmunoz.github.io/Mibo/graphics2d/particles.html) + [Pooled Particles](https://angelmunoz.github.io/Mibo/patterns/pooled-particles.html)
- HUD/minimap over the game world (multi-renderer, the `noClear` rule) → [Layered Rendering](https://angelmunoz.github.io/Mibo/patterns/layered-rendering.html)
- Tile levels (`CellGrid2D`, stamps): start at the [Level Design overview](https://angelmunoz.github.io/Mibo/level-design/overview.html), then the [2D Layout Engine](https://angelmunoz.github.io/Mibo/level-design/2d/core.html); genre stamps: [Platformer](https://angelmunoz.github.io/Mibo/level-design/2d/platformer.html), [Top-Down](https://angelmunoz.github.io/Mibo/level-design/2d/topdown.html), [Hex](https://angelmunoz.github.io/Mibo/level-design/2d/hex.html)
- Raw device escape hatch → [Custom Commands](https://angelmunoz.github.io/Mibo/graphics2d/custom-commands.html)

**Performance**
- The 9 GC/throughput rules for 2D → [2D Rendering Performance](https://angelmunoz.github.io/Mibo/graphics2d/performance.html)
- General F# perf ladder (structs → struct tuples → mutable → ArrayPool → Span) → [F# For Perf](https://angelmunoz.github.io/Mibo/performance.html)

**Tests / servers**
- Run the MVU loop in virtual time, headless, for unit tests → [Headless Mode](https://angelmunoz.github.io/Mibo/mvu/headless.html)

## Reference

> **API shape vs usage.** If you need an exact signature, parameter list, return
> type, or member set of a type/function, you **MUST** consult the
> [API reference](https://angelmunoz.github.io/Mibo/reference/index.html), not
> the guides. The guides show patterns and general usage; they are not a complete
> signature listing and you must not guess API shapes from prose. If you want
> general usage or examples of a feature/module, use the documentation sections
> linked above.

- Full docs index → https://angelmunoz.github.io/Mibo/
- API reference → https://angelmunoz.github.io/Mibo/reference/index.html
- Samples (real games built on Mibo) → https://github.com/AngelMunoz/Mibo.Samples
