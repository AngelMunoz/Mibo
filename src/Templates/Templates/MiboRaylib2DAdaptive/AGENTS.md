# AGENTS.md: Mibo Raylib 2D Adaptive Game

This is a **Mibo** game project built on the **adaptive runtime**. Mibo is an
F# game framework that ships **composable building blocks** for games: grids
and level layout, input mapping, lighting, particles, incremental derived
state, a deferred command-buffer renderer, and more. **Before creating a new
sub-module, check the docs**; the building block you are about to write
likely already exists. Compose existing pieces; do not reinvent them.

This template targets the **raylib-cs** backend (`Mibo.Raylib.Adaptive`, host
`AdaptiveRaylibGame`).

> **Default renderer.** The template wires a `Renderer2D` over a **deferred
> command buffer**: the view reads the packed `Frame` and fills a
> `RenderBuffer2D` with `Command2D` values (`Draw.fillRect`/`sprite`/`text`/…),
> and the renderer sorts them by layer, applies camera transforms, and
> auto-batches the GPU draws. Layer ordering, per-section shaders,
> post-processing, and 2D lighting (`LightContext2D`) are already there; reach
> for `Draw.*` commands rather than immediate-mode calls. See
> [2D Rendering](https://angelmunoz.github.io/Mibo/graphics2d/overview.html),
> [2D Buffer & Commands](https://angelmunoz.github.io/Mibo/graphics2d/buffer-and-commands.html),
> and [2D Lighting](https://angelmunoz.github.io/Mibo/graphics2d/lighting.html).

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
- When the world is bigger than the screen (scroll, zoom, screen↔world, picking) → [Camera](https://angelmunoz.github.io/Mibo/camera.html)
- Where the upgrade ladder is and which rung to pick → [Scaling Adaptive](https://angelmunoz.github.io/Mibo/adaptive/scaling.html)
- Architecture: features, events, translator → [Systems](https://angelmunoz.github.io/Mibo/adaptive/systems.html)

**2D rendering (this is a 2D project)**
- The `Draw.*` DSL the view already uses (`fillRect`/`sprite`/`text`/`line`/`circle`) → [2D Buffer & Commands](https://angelmunoz.github.io/Mibo/graphics2d/buffer-and-commands.html)
- Layers, cameras, the deferred `RenderBuffer2D` overview → [2D Rendering](https://angelmunoz.github.io/Mibo/graphics2d/overview.html)
- Sprite-sheet animation for a real character → [Animation](https://angelmunoz.github.io/Mibo/animation.html)
- Torch/shadow/normal-map lighting (`LightContext2D`) → [2D Lighting](https://angelmunoz.github.io/Mibo/graphics2d/lighting.html)
- Effects without GC (`Particle2D`, `fadeAndCompact`) → [2D Particles](https://angelmunoz.github.io/Mibo/graphics2d/particles.html) + [Pooled Particles](https://angelmunoz.github.io/Mibo/patterns/pooled-particles.html)
- HUD/minimap over the game world (multi-renderer, the `noClear` rule) → [Layered Rendering](https://angelmunoz.github.io/Mibo/patterns/layered-rendering.html)
- Tile levels (`CellGrid2D`, stamps): start at the [Level Design overview](https://angelmunoz.github.io/Mibo/level-design/overview.html), then the [2D Layout Engine](https://angelmunoz.github.io/Mibo/level-design/2d/core.html); genre stamps: [Platformer](https://angelmunoz.github.io/Mibo/level-design/2d/platformer.html), [Top-Down](https://angelmunoz.github.io/Mibo/level-design/2d/topdown.html), [Hex](https://angelmunoz.github.io/Mibo/level-design/2d/hex.html)
- Raw rlgl escape hatch → [Custom Commands](https://angelmunoz.github.io/Mibo/graphics2d/custom-commands.html)

**Performance**
- The 9 GC/throughput rules for 2D → [2D Rendering Performance](https://angelmunoz.github.io/Mibo/graphics2d/performance.html)
- General F# perf ladder (structs → struct tuples → mutable → ArrayPool → Span) → [F# For Perf](https://angelmunoz.github.io/Mibo/performance.html)
- Incremental-computation costs (combined collections, big worlds) → [Mibo.Adaptive Performance](https://angelmunoz.github.io/Mibo/mibo-adaptive/performance.html)

**Tests / servers**
- Run the adaptive loop in virtual time, headless, for unit tests → [Headless Mode](https://angelmunoz.github.io/Mibo/adaptive/headless.html)
- Heavy computation off the game thread → [Background Work](https://angelmunoz.github.io/Mibo/adaptive/background-work.html)

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
