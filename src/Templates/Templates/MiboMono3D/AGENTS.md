# AGENTS.md — Mibo MonoGame 3D Game

This is a **Mibo** game project. Mibo is an Elmish-based F# game framework.
This template targets the **MonoGame** backend (`Mibo.MonoGame`, host `MiboGame`)
and ships **two interchangeable thin clients** sharing one library:

- `src/` — shared library (`Library.fs` + `MiboMono3D.fsproj`, net10.0). All
  game logic, the view, and a `create()` composition root that builds the
  `MonoGameProgram` (with the content root already configured). References
  `Mibo.MonoGame` + `MonoGame.Framework.Native` (the compile-time,
  backend-neutral types).
- `Content/` — shared MonoGame content pipeline (`Content.mgcb`). Each thin
  client builds it via `MonoGame.Content.Builder.Task`, so assets land in both
  clients' output.
- `DesktopGL/` — thin client. Adds `MonoGame.Framework.DesktopGL` (**OpenGL**),
  references `../src/MiboMono3D.fsproj` and the shared content, calls
  `MiboMono3D.create()` and runs `MiboGame`.
- `WindowsDX/` — thin client (net10.0-windows). Adds
  `MonoGame.Framework.WindowsDX` (**DirectX**), `[<STAThread>]`,
  `app.manifest`. References `../src/MiboMono3D.fsproj` and the shared content,
  same three lines as DesktopGL.

The starter draws a bouncing lit cube — pure Model-View-Update with a `Tick`-driven
view. Treat it as Level 0 on the
[scaling ladder](https://angelmunoz.github.io/Mibo/scaling.html).

> **Default renderer.** `create()` wires a `Renderer3D` with Mibo's built-in
> **Forward PBR** pipeline and a **shadow atlas** — physically-based shading,
> point/spot/directional lights with shadows, and post-processing are already
> available; don't re-implement a rendering strategy. See
> [3D Rendering](https://angelmunoz.github.io/Mibo/graphics3d/overview.html),
> [3D Lighting](https://angelmunoz.github.io/Mibo/graphics3d/lighting.html),
> and [3D Materials](https://angelmunoz.github.io/Mibo/graphics3d/materials.html).

## Project structure you must keep (cross-backend split)

MonoGame's platform host packages (`...DesktopGL` and `...WindowsDX`) **cannot
coexist in one project**. That is why there are three projects. Keep the split:

- **Game logic, model, messages, `update`, the `view`, and `create()` live in
  the shared library (`src/`) only.** They must stay backend-neutral (compile
  against `MonoGame.Framework.Native`). Never reference `DesktopGL` or
  `WindowsDX` from the shared lib.
- **Thin clients stay thin.** They only call `MiboMono3D.create()` — which
  already returns a `MonoGameProgram` with the content root
  (`Content.RootDirectory`) configured — and `new MiboGame<_,_>().Run()`. They
  no longer call `MonoGameProgram.ofProgram` themselves. Put no game logic in a
  client.
- Both clients build from the same shared source — **do not fork logic per
  backend.** If you need a backend-specific service (audio, animation),
  define an interface in the shared lib (or `Mibo.Core`) and implement it once,
  injecting it through an `Env`/composition-root record like `create()` does.

## Content pipeline

Assets live under `Content/` and are listed in `Content/Content.mgcb`. Each thin
client builds that pipeline via `MonoGame.Content.Builder.Task`, so the assets
land in both clients' output directories and load at runtime through
`GameContext.getService<IAssets> ctx` (asset names carry no file extension —
e.g. load `Content/foo.png` as `"foo"`). To edit the pipeline visually, run
`dotnet tool restore` then `dotnet mgcb-editor`.

## Architecture you must follow: routed sub-systems

As this game grows, do **not** let the `update` function become a dumping ground.
Split the game into independent sub-systems coordinated by a **router**. This is
the [Composable Systems](https://angelmunoz.github.io/Mibo/patterns/composable-systems.html)
pattern and it is mandatory once the game outgrows a single `update`.

- **The root `update` is a router, not game logic.** It routes messages to
  sub-systems and translates their emitted events into `Cmd<Msg>` for consumers.
  It contains no game logic — only dispatch and translation.
- **Each sub-system owns its slice.** A sub-system owns its model, its message
  type, and its update. It mutates/returns **only its own state**. It never
  imports another sub-system's update or reaches into another sub-system's model.
- **Cross-system communication is declarative.** Sub-systems never call each
  other. They return **Events** (what happened) / **Intents** (what should
  happen) — pure data. The router translates each into `Cmd<Msg>` for the
  relevant systems. The emitter does not know (or import) its consumers.
- **Read access goes through a read-only query — mind the hot path.**
  - *Cold path* (event-driven, turn-based): a closure query record the router
    builds per-message (`{ UnitAt: Vector2 -> UnitId voption; ... }`).
  - *Hot path* (per-`Tick`, real-time): pass **direct values**, not closures.
    A closure-bearing query built inside `Tick` allocates every frame and the
    JIT will not inline across it. Pass `playerPos`, `enemies`, etc. directly.
- **`Cmd.map` lifts sub-commands.** Sub-system commands are `Cmd<SubMsg>`; lift
  them into the root `Msg` with `Cmd.map SubMsg`.

When several sub-systems must run every frame in a fixed order, compose them
with the [System pipeline](https://angelmunoz.github.io/Mibo/system.html)
(`System.start` → `pipeMutable` → `snapshot` → `pipe` → `finish`). The snapshot
call is a **compile-enforced** boundary: mutation phases run before it, readonly
query phases after it. See [Scaling Mibo](https://angelmunoz.github.io/Mibo/scaling.html)
for when each rung pays off — you can ship a lot of games at Level 2–3.

## Pointers by topic

**Core loop / the code you're looking at**
- How the MVU loop, `Tick`, and dispatch modes work → [Elmish](https://angelmunoz.github.io/Mibo/elmish.html)
- The `Program` builder pipeline (`mkProgram`/`withConfig`/`withTick`/`withRenderer`/`withSubscription`) → [Programs](https://angelmunoz.github.io/Mibo/program.html)
- Side-effect command API (`Cmd.ofMsg`/`ofAsync`/`batch`/`map`/`deferNextFrame`) → [Commands](https://angelmunoz.github.io/Mibo/commands.html)
- Continuous external event sources (`Sub`, diffing, `Sub.batch`) → [Subscriptions](https://angelmunoz.github.io/Mibo/subscriptions.html)
- Wiring backend services through an `Env` composition root → [Service Composition](https://angelmunoz.github.io/Mibo/services.html)

**Growing the game**
- Turn input into semantic actions (`InputMap`/`ActionState`/`InputMapper.subscribe`) → [Input](https://angelmunoz.github.io/Mibo/input.html)
- Load textures/fonts/sounds/models (`IAssets`, caching) → [Assets](https://angelmunoz.github.io/Mibo/assets.html)
- Camera movement, follow, orbit, screen↔world, mouse picking → [Camera](https://angelmunoz.github.io/Mibo/camera.html)
- Frustum/rectangle visibility culling → [Culling](https://angelmunoz.github.io/Mibo/culling.html)
- Where the upgrade ladder is and which rung to pick → [Scaling Mibo](https://angelmunoz.github.io/Mibo/scaling.html)
- Architecture: composable sub-systems, events/intents, snapshot boundary → [Composable Systems](https://angelmunoz.github.io/Mibo/patterns/composable-systems.html)

**3D rendering (this is a 3D project)**
- The `Draw3D.*` DSL the view already uses (`beginCamera`/`drawPrimitive`/`endCamera`/`drop`) → [3D Buffer & Commands](https://angelmunoz.github.io/Mibo/graphics3d/buffer-and-commands.html)
- The `RenderBuffer3D` + `ForwardPipeline`/shadow atlas overview → [3D Rendering](https://angelmunoz.github.io/Mibo/graphics3d/overview.html)
- Turn the flat cube into a textured/PBR surface (`Material3D`, primitive meshes) → [3D Materials](https://angelmunoz.github.io/Mibo/graphics3d/materials.html)
- Ambient/directional/point/spot lights + shadows → [3D Lighting](https://angelmunoz.github.io/Mibo/graphics3d/lighting.html)
- Skeletal animation once you swap the cube for a character model → [Animation 3D](https://angelmunoz.github.io/Mibo/animation3d.html)
- Many copies of a mesh — voxel worlds, forests (`drawMeshInstanced`, `InstancedRenderContext`) → [GPU Instancing](https://angelmunoz.github.io/Mibo/graphics3d/instancing.html)
- HUD/2D overlay over the 3D scene (multi-renderer, the `noClear` rule) → [Layered Rendering](https://angelmunoz.github.io/Mibo/patterns/layered-rendering.html)
- Voxel/grid 3D levels (`CellGrid3D`, stamps, `CellGridRenderer3D`) — start at the [Level Design overview](https://angelmunoz.github.io/Mibo/level-design/overview.html), then the [3D Layout Engine](https://angelmunoz.github.io/Mibo/level-design/3d/core.html); genre stamps: [Interior](https://angelmunoz.github.io/Mibo/level-design/3d/interior.html), [Terrain](https://angelmunoz.github.io/Mibo/level-design/3d/terrain.html), [Hex](https://angelmunoz.github.io/Mibo/level-design/3d/hex.html)
- Custom HLSL look (toon/cel/post-processing; `.fx`→`.mgfx`) → [Shaders](https://angelmunoz.github.io/Mibo/shaders.html) + [Shader Uniform Reference](https://angelmunoz.github.io/Mibo/shader-uniforms.html)

**Performance**
- General F# perf ladder (structs → struct tuples → mutable → ArrayPool → Span) → [F# For Perf](https://angelmunoz.github.io/Mibo/performance.html)
- Off-main-thread heavy work (world-gen, pathfinding, save) → [Background Work](https://angelmunoz.github.io/Mibo/patterns/background-work.html)

**Tests / servers**
- Run the MVU loop in virtual time, headless, for unit tests → [Headless Mode](https://angelmunoz.github.io/Mibo/headless.html)

## Reference

- Full docs index → https://angelmunoz.github.io/Mibo/
- API reference → https://angelmunoz.github.io/Mibo/reference/index.html
- Samples (real games built on Mibo) → https://github.com/AngelMunoz/Mibo.Samples
