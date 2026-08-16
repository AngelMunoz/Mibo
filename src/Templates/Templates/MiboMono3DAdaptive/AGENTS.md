# AGENTS.md: Mibo MonoGame 3D Adaptive Game

This is a **Mibo** game project built on the **adaptive runtime**. This
template targets the **MonoGame** backend (`Mibo.MonoGame`, host
`AdaptiveMonoGameGame`) and ships **three interchangeable thin clients**
sharing one library:

- `src/`: shared library (`Library.fs` + `MiboMono3DAdaptive.fsproj`,
  net10.0). All game logic, the view, and a `create()` composition root that
  builds the `AdaptiveMonoGameProgram` (with the content root already
  configured). References `Mibo.MonoGame` + `Mibo.Adaptive` +
  `MonoGame.Framework.Native` (the compile-time, backend-neutral types).
- `Content/`: shared MonoGame content pipeline (`Content.mgcb`). Each thin
  client builds it via `MonoGame.Content.Builder.Task`, so assets land in all
  clients' output.
- `DesktopGL/`: thin client. Adds `MonoGame.Framework.DesktopGL` (**OpenGL**),
  references `../src/MiboMono3DAdaptive.fsproj` and the shared content, calls
  `MiboMono3DAdaptive.create()` and runs `AdaptiveMonoGameGame`.
- `DesktopVK/`: thin client (**Vulkan**, cross-platform). Adds
  `MonoGame.Framework.Native` + the `MonoGame.Runtime.*.Vulkan` runtime packages
  and sets `<MonoGamePlatform>DesktopVK</MonoGamePlatform>`. Same wiring as
  DesktopGL.
- `WindowsDX12/`: thin client (net10.0-windows, **DirectX 12**). Adds
  `MonoGame.Framework.Native` + `MonoGame.Runtime.Windows.DX12`, sets
  `<MonoGamePlatform>WindowsDX12</MonoGamePlatform>`, `[<STAThread>]`,
  `app.manifest`. References `../src/MiboMono3DAdaptive.fsproj` and the shared
  content, same three lines as DesktopGL.

Mibo is an F# game framework that ships **composable building blocks** for
games: grids and level layout, input mapping, lighting and shadows, GPU
instancing, skeletal animation, incremental derived state, a deferred
command-buffer renderer, and more. **Before creating a new sub-module, check
the docs**; the building block you are about to write likely already exists.
Compose existing pieces; do not reinvent them.

> **Default renderer.** `create()` wires a `Renderer3D` with Mibo's built-in
> **Forward PBR** pipeline and a **shadow atlas**: physically-based shading,
> point/spot/directional lights with shadows, and post-processing are already
> available; don't re-implement a rendering strategy. The view reads the
> packed `Frame` and fills a `RenderBuffer3D`. See
> [3D Rendering](https://angelmunoz.github.io/Mibo/graphics3d/overview.html),
> [3D Lighting](https://angelmunoz.github.io/Mibo/graphics3d/lighting.html),
> and [3D Materials](https://angelmunoz.github.io/Mibo/graphics3d/materials.html).

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

- **Game logic, the world (roots), `update`, the frame, the `view`, and
  `create()` live in `src/` only.** They must stay backend-neutral (compile
  against `MonoGame.Framework.Native`). Never reference `DesktopGL`,
  `DesktopVK`, or `WindowsDX12` from the shared lib.
- **Thin clients stay thin.** They just call `MiboMono3DAdaptive.create()`
  (which already returns an `AdaptiveMonoGameProgram` with the content root
  configured) and `new AdaptiveMonoGameGame<_>().Run()`. Asset /
  `Content.RootDirectory` configuration is centralized in `create()`. Put no
  game logic in a client.
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

## Architecture you must follow: state, update, frame

The game state lives in **containers** (`cval`, `cmap`) that know when they
changed. Each frame, `update` writes facts into the roots; the runner then
packs everything the renderer needs into a single **`Frame`** value. The
renderer never looks at the game state; it reads the frame. As the game
grows, keep this shape:

- **`update` advances the game by writing roots.** It reads the action state
  once at the top, ticks the features in order, and writes the results back
  to the containers. It is a schedule plus a translator, not a dumping ground.
- **Split by feature once `update` outgrows the screen.** A feature owns its
  model (its containers) and its `tick`; features never reach into each
  other's containers. A feature returns **Events** (plain data describing
  what happened), and one place (the update) translates them for the features
  that care. This is the
  [Systems](https://angelmunoz.github.io/Mibo/adaptive/systems.html) pattern
  and it is mandatory once the game outgrows a single `update`.
- **Cross-feature reads pass plain values.** Read one feature's containers
  once and pass the `IReadOnlyDictionary`, vectors, ints. Do not pass the containers.
  The receiving feature cannot write them, and the compiler keeps you honest.
- **Derived state lives in the graph, not in update.** Counts, filtered
  lists, score strings: anything that is a function of state you already
  have becomes an `AVal`/`AMap` projection built **once** at startup. You
  never keep derived values up to date; the graph does.
- **The frame packs what the renderer needs.** Read each value once with
  `getValue`. It is free, and safe because nothing writes to the state while the
  frame is built and drawn. Use `force` only for data that outlives the frame:
  sent over the network, written to disk, or handed to another thread.
- **Some work waits its turn.** `ctx.Intents.post` runs right after `update`.
  use it when one feature must react to what another did, or to remove entries
  from a collection you are looping over. `postNextFrame`, `postTask` and
  `postAsync` run at other moments. Never write roots from another thread;
  post the work instead.
- **External events arrive through subscriptions.** A subscription watches an
  event source (keys, a timer, the network) and hands its events to the game
  loop. It never runs game code itself. Build the subscription map once in
  `init`, keyed by id. Input is already wired this way
  (`InputMapper.subscribeStaticAdaptive` writes the `Actions` root). One-shot
  actions read `Started`/`Released`; the subscription clears them after
  `update`, so they are fresh every step and no manual clear is needed.
  Continuous movement reads `Held`.
- **Keep the per-frame work small.** Do not create closures inside `update`,
  and do not recompute each frame what a projection already gives you.

See [Scaling Adaptive](https://angelmunoz.github.io/Mibo/adaptive/scaling.html)
for when each rung pays off. You can ship a lot of games at Level 2 or 3.

## Pointers by topic

> **Read before you build.** These links are not suggestions. Before writing or
> extending code in any area below, open the linked doc(s) for that area and
> verify whether Mibo already ships a building block for what you need. Do not
> re-implement existing functionality; compose what is already there.

**Core loop / the code you're looking at**
- How the adaptive loop, roots, and frames work → [Adaptive Overview](https://angelmunoz.github.io/Mibo/adaptive/overview.html)
- The program pipeline (`AdaptiveProgram.mkProgram`/`withConfig`/`withInput`/`withRenderer`) → [Adaptive Programs](https://angelmunoz.github.io/Mibo/adaptive/program.html)
- Deferred work (`Intents.post`/`postNextFrame`/`postTask`) → [Intents](https://angelmunoz.github.io/Mibo/adaptive/intents.html)
- External events: keys, timers, network (`AdaptiveSub`, keyed `amap`) → [Subscriptions](https://angelmunoz.github.io/Mibo/adaptive/subscriptions.html)
- Values that follow state (`AVal.map`/`AMap.filter`, built once) → [Derived State](https://angelmunoz.github.io/Mibo/adaptive/derived-state.html)
- Wiring backend services through an `Env` composition root → [Services](https://angelmunoz.github.io/Mibo/adaptive/services.html)

**Growing the game**
- Turn input into semantic actions (`InputMap`/`ActionState`/`InputMapper.subscribeStaticAdaptive`) → [Input](https://angelmunoz.github.io/Mibo/input.html)
- Load textures/fonts/sounds/models (`IAssets`, caching) → [Assets](https://angelmunoz.github.io/Mibo/assets.html)
- Camera movement, follow, orbit, screen↔world, mouse picking → [Camera](https://angelmunoz.github.io/Mibo/camera.html)
- Frustum/rectangle visibility culling → [Culling](https://angelmunoz.github.io/Mibo/culling.html)
- Where the upgrade ladder is and which rung to pick → [Scaling Adaptive](https://angelmunoz.github.io/Mibo/adaptive/scaling.html)
- Architecture: features, events, translator → [Systems](https://angelmunoz.github.io/Mibo/adaptive/systems.html)

**3D rendering (this is a 3D project)**
- The `Draw3D.*` DSL the view already uses (`beginCamera`/`drawPrimitive`/`endCamera`/`drop`) → [3D Buffer & Commands](https://angelmunoz.github.io/Mibo/graphics3d/buffer-and-commands.html)
- The `RenderBuffer3D` + `ForwardPipeline`/shadow atlas overview → [3D Rendering](https://angelmunoz.github.io/Mibo/graphics3d/overview.html)
- Turn the flat cube into a textured/PBR surface (`Material3D`, primitive meshes) → [3D Materials](https://angelmunoz.github.io/Mibo/graphics3d/materials.html)
- Ambient/directional/point/spot lights + shadows → [3D Lighting](https://angelmunoz.github.io/Mibo/graphics3d/lighting.html)
- Skeletal animation once you swap the cube for a character model → [Animation 3D](https://angelmunoz.github.io/Mibo/animation3d.html)
- Many copies of a mesh: voxel worlds, forests (`drawMeshInstanced`, `InstancedRenderContext`) → [GPU Instancing](https://angelmunoz.github.io/Mibo/graphics3d/instancing.html)
- HUD/2D overlay over the 3D scene (multi-renderer, the `noClear` rule) → [Layered Rendering](https://angelmunoz.github.io/Mibo/patterns/layered-rendering.html)
- Voxel/grid 3D levels (`CellGrid3D`, stamps, `CellGridRenderer3D`): start at the [Level Design overview](https://angelmunoz.github.io/Mibo/level-design/overview.html), then the [3D Layout Engine](https://angelmunoz.github.io/Mibo/level-design/3d/core.html); genre stamps: [Interior](https://angelmunoz.github.io/Mibo/level-design/3d/interior.html), [Terrain](https://angelmunoz.github.io/Mibo/level-design/3d/terrain.html), [Hex](https://angelmunoz.github.io/Mibo/level-design/3d/hex.html)
- Custom HLSL look (toon/cel/post-processing; `.fx`→`.mgfx`) → [Shaders](https://angelmunoz.github.io/Mibo/shaders.html) + [Shader Uniform Reference](https://angelmunoz.github.io/Mibo/shader-uniforms.html)

**Performance**
- General F# perf ladder (structs → struct tuples → mutable → ArrayPool → Span) → [F# For Perf](https://angelmunoz.github.io/Mibo/performance.html)
- Off-main-thread heavy work (world-gen, pathfinding, save) → [Background Work](https://angelmunoz.github.io/Mibo/adaptive/background-work.html)
- Incremental-computation costs (combined collections, big worlds) → [Mibo.Adaptive Performance](https://angelmunoz.github.io/Mibo/mibo-adaptive/performance.html)

**Tests / servers**
- Run the adaptive loop in virtual time, headless, for unit tests → [Headless Mode](https://angelmunoz.github.io/Mibo/adaptive/headless.html)

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
