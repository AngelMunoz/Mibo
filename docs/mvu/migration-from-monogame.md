---
title: Migrating from Mibo (MonoGame)
category: MVU
categoryindex: 2
index: 8
---

# Migrating from Mibo (MonoGame) to Mibo.MonoGame

> **Who this guide is for:** Users of the original `Mibo` package (the monolithic
> MonoGame library at `github.com/AngelMunoz/Mibo`) who want to migrate to
> **Mibo v2**'s split architecture (`Mibo.Core` + `Mibo.MonoGame`).
>
> This is a severely breaking migration. The original Mibo was a single assembly
> with the Elmish engine, input, assets, 2D/3D rendering, animation, camera helpers,
> and MonoGame-specific types woven through the public API. The new architecture
> separates backend-agnostic contracts (in `Mibo.Core`) from the MonoGame-specific
> implementation (in `Mibo.MonoGame`), renames/restructures the rendering and
> animation stacks, and changes several core signatures.
>
> The good news: the rendering, animation, camera, lighting, shadow, and
> post-processing features you used in the old package **still exist** — they were
> re-implemented under the new architecture, often with cleaner names. This guide
> maps every old API to its new equivalent.

## What changed architecturally

The original Mibo was a single package:

```
Mibo               ← everything: Elmish engine, input, assets, 2D/3D rendering,
                     animation, camera, layout, spatial, MonoGame host
```

The new architecture splits into:

```
Mibo.Core          ← backend-agnostic: Cmd, Sub, GameTime, System pipeline,
                     Program, ElmishLoop, HeadlessRunner, IInput/IInputMapper
                     contracts, IAssetCache, Layout, Layout3D, InputMapper types
Mibo.MonoGame      ← MonoGame host (MiboGame, MonoGameProgram, MonoGameGameContext),
                     input polling + translation, IAssets, AND the full 2D/3D
                     rendering stacks (Renderer2D, Renderer3D, ForwardPipeline,
                     Command2D/Command3D, Draw/Draw3D DSLs, lighting, shadows,
                     post-processing), 2D + 3D animation, Camera2D/Camera3D
```

**Key principle:** if it's an interface or contract that portable code needs, it
lives in `Mibo.Core`. If it touches MonoGame types, it lives in `Mibo.MonoGame`.
The simulation half of your game (model, update, layout, spatial queries) can
reference only `Mibo.Core` and be shared across the MonoGame and Raylib backends.

## Package and namespace changes

| Old                     | New             | Namespace(s)                                                                                                                                                                              |
| ----------------------- | --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Mibo` (single package) | `Mibo.Core`     | `Mibo.Elmish`, `Mibo.Input`, `Mibo.Layout`, `Mibo.Layout3D`                                                                                                                               |
| `Mibo` (single package) | `Mibo.MonoGame` | `Mibo.Elmish`, `Mibo.Input`, `Mibo.Animation`, `Mibo.Elmish.Graphics2D`, `Mibo.Elmish.Graphics2D.Lighting`, `Mibo.Elmish.Graphics3D`, `Mibo.Elmish.Graphics3D.Pipelines`, `Mibo.Layout3D` |

Most `open` declarations stay the same — the `Mibo.Elmish`, `Mibo.Input`, and
`Mibo.Animation` namespaces are preserved. What changed:

- **2D rendering** moved from `Mibo.Elmish.Graphics2D` (old) to
  `Mibo.Elmish.Graphics2D` + `Mibo.Elmish.Graphics2D.Lighting` (new). The module
  names changed (see §8).
- **3D rendering** moved from `Mibo.Rendering.Graphics3D` (old modern pipeline)
  to `Mibo.Elmish.Graphics3D` + `Mibo.Elmish.Graphics3D.Pipelines` (new).
- The old `Mibo.Elmish.Graphics3D` (legacy `Batch3DRenderer`/`Draw3D`, already
  marked `[<Obsolete>]` in the old package) is gone — its successor is the new
  `Mibo.Elmish.Graphics3D.Draw3D`.

## Migration checklist

| Area                           | Breaking? | Effort                                                                                                                 |
| ------------------------------ | --------- | ---------------------------------------------------------------------------------------------------------------------- |
| Package references             | Yes       | Low — replace `Mibo` with `Mibo.Core` + `Mibo.MonoGame`                                                                |
| Program setup                  | Yes       | Medium — `withConfig` split into `GameConfig` + `MonoGameProgram` wrapper; `withRenderer` signature changed            |
| GameContext access             | Yes       | Medium — direct fields → service registry                                                                              |
| Input types                    | Yes       | Medium — MonoGame enums → backend-neutral codes                                                                        |
| InputMapper setup              | Yes       | Low — `Program.withInputMapper` → `MonoGameProgram.withInputMapper` (on the `MonoGameProgram` wrapper)                 |
| Assets                         | Yes       | Low — `Assets.texture path ctx` → `assets.Texture path`; `IAssets` now extends `IAssetCache`                           |
| Cmd / Sub                      | Yes       | Low — new `Msg` and `Quit` cases in DU                                                                                 |
| Content pipeline & asset paths | Maybe     | Low–Medium — only if you relied on XNB-baked animation data                                                            |
| 2D rendering                   | Yes       | Medium — renderer/command/DSL module names changed (`Batch2DRenderer`→`Renderer2D`, etc.)                              |
| 3D rendering                   | Yes       | Medium — `withPipeline` removed; use `Renderer3D.create (ForwardPipeline(...)) view`                                   |
| 2D animation                   | No        | None — `SpriteSheet`/`AnimatedSprite` API unchanged                                                                    |
| 3D animation                   | N/A (new) | Low — the old package had no 3D animation; new backend ships `AnimatedModel`                                           |
| Camera                         | Yes       | Low — `Camera2D`/`Camera3D` modules exist; `Camera3D.create` takes just position/target/FOV with defaulted up/near/far |
| Culling                        | Yes       | Low — `isGenericVisible` renamed `isVisibleBox` (box-vs-frustum test)                                                  |
| Layout / Spatial               | No        | None — moved to Core, same API                                                                                         |
| System pipeline                | No        | None — moved to Core, same API                                                                                         |

---

## 1. Package references

Replace the single `Mibo` package with two packages:

```xml
<!-- Before -->
<PackageReference Include="Mibo" Version="1.*" />

<!-- After -->
<ProjectReference Include="path/to/Mibo.Core.fsproj" />
<ProjectReference Include="path/to/Mibo.MonoGame.fsproj" />
```

Or if consuming as NuGet packages:

```xml
<PackageReference Include="Mibo.Core" Version="2.*" />
<PackageReference Include="Mibo.MonoGame" Version="2.*" />
```

---

## 2. Program setup

The `Program` builder changed in two significant ways:

### `withConfig` split into two layers

The old API gave you direct access to MonoGame's `Game` and
`GraphicsDeviceManager` in a single callback. The new API splits this into a
backend-neutral `GameConfig` transform (window size/title/FPS) and a
MonoGame-specific device-level callback.

```fsharp
// Before
Program.mkProgram init update
|> Program.withConfig (fun (game, gdm) ->
  game.Content.RootDirectory <- "Content"
  game.Window.Title <- "My Game"
  gdm.PreferredBackBufferWidth <- 1280
  gdm.PreferredBackBufferHeight <- 720
  gdm.SynchronizeWithVerticalRetrace <- true)

// After — window-level config via Core GameConfig
Program.mkProgram init update
|> Program.withConfig (fun cfg ->
  GameConfig.defaultConfig
  |> GameConfig.withWidth 1280
  |> GameConfig.withHeight 720
  |> GameConfig.withTitle "My Game title"
  |> GameConfig.withTargetFPS 60
)
// ... then wrap with MonoGameProgram and add device-level config:
|> MonoGameProgram.ofProgram
|> MonoGameProgram.withConfig (fun (game, gdm) ->
  game.Content.RootDirectory <- "Content"
  gdm.SynchronizeWithVerticalRetrace <- true)
```

`GameConfig` is a struct record (in `Mibo.Core`, namespace `Mibo.Elmish`):

```fsharp
[<Struct>]
type GameConfig = {
  Width: int          // default: 800
  Height: int         // default: 600
  Title: string       // default: varies by backend
  TargetFPS: int voption  // default: ValueNone (backend default); ValueSome n to cap
  MinWidth: int voption
  MinHeight: int voption
}
```

Helper functions are available: `GameConfig.withWidth`, `withHeight`,
`withTitle`, `withTargetFPS`, `withMinWidth`, `withMinHeight`.

`MonoGameProgram.withConfig` receives the `Game` and `GraphicsDeviceManager`
and runs in the `MiboGame` constructor, **before** `Initialize` /
`GraphicsDevice` creation — so `GraphicsProfile`, vsync
(`SynchronizeWithVerticalRetrace`), `IsFullScreen`, `HardwareModeSwitch`,
`Window.AllowUserResizing`, and `Content.RootDirectory` all take effect.

**If you need direct access to `Game` or `GraphicsDeviceManager`** (e.g. for
platform-specific configuration not covered by `GameConfig`), use
`Program.withServiceRegistration` to run code after the host initializes:

```fsharp
|> Program.withServiceRegistration (fun ctx ->
  let game = MonoGameGameContext.getGame ctx
  // access game.Window, game.GraphicsDeviceManager, etc.
)
```

### `withRenderer` signature changed

The old API passed `Game` to the renderer factory. The new API takes `unit` —
renderers receive `GameContext` at draw time.

```fsharp
// Before
|> Program.withRenderer (fun game ->
  Batch2DRenderer.createWithConfig game cfg view)

// After
|> Program.withRenderer (fun () ->
  Renderer2D.createWith cfg view)
```

You can register multiple renderers (they draw in the order you add them). The
new `MonoThreeD` sample registers a 3D renderer and a 2D overlay renderer:

```fsharp
|> Program.withRenderer (fun () ->
  Renderer3D.create (ForwardPipeline()) view)
|> Program.withRenderer (fun () ->
  Renderer2D.createWith Renderer2DConfig.noClear overlayView)
```

### Removed builders

| Old builder                | Replacement                                                                          |
| -------------------------- | ------------------------------------------------------------------------------------ |
| `Program.withComponent`    | Use `Program.withServiceRegistration`                                                |
| `Program.withComponentRef` | Use `Program.withServiceRegistration` + `GameContext.getService`                     |
| `Program.withPipeline`     | Use `Program.withRenderer (fun () -> Renderer3D.create (ForwardPipeline(...)) view)` |

### Game host

```fsharp
// Before
let game = ElmishGame(program)
game.Run()

// After
let mgProgram =
  program
  |> MonoGameProgram.ofProgram
  // optional device-level config (GraphicsProfile, vsync, Content.RootDirectory, etc.)
  |> MonoGameProgram.withConfig (fun (game, gdm) ->
    game.Content.RootDirectory <- "Content")

let game = MiboGame(mgProgram)
game.Run()
```

`MiboGame` inherits from `Microsoft.Xna.Framework.Game` just like `ElmishGame`
did, and `.Run()` is the same. The constructor now takes a `MonoGameProgram`
(the Core `Program` wrapped via `MonoGameProgram.ofProgram`). Device-level
settings go through `MonoGameProgram.withConfig` so they apply before device
creation.

---

## 3. GameContext access

The old `GameContext` exposed MonoGame types as **direct fields** and only had
three members:

```fsharp
// Before — the old GameContext was a 3-field record
let gd = ctx.GraphicsDevice
let content = ctx.Content
let game = ctx.Game
// viewport size came from the graphics device, not the context:
let w = ctx.GraphicsDevice.Viewport.Width
let h = ctx.GraphicsDevice.Viewport.Height
```

The new `GameContext` is a backend-neutral service registry. MonoGame types are
registered as services:

```fsharp
// After
let gd = MonoGameGameContext.getGraphicsDevice ctx
let content = MonoGameGameContext.getContentManager ctx
let game = MonoGameGameContext.getGame ctx
let w = ctx.WindowWidth
let h = ctx.WindowHeight
```

Or use the generic service API:

```fsharp
let gd = GameContext.getService<GraphicsDevice> ctx
```

`WindowWidth` and `WindowHeight` are now **direct members** on `GameContext`
(they were not on the old context — you had to read `GraphicsDevice.Viewport`).
They update automatically on window resize.

---

## 4. Input types

The old API used MonoGame's native enum types directly. The new API uses
backend-neutral struct DUs from `Mibo.Core` (namespace `Mibo.Input`), so your
input bindings are portable across the MonoGame and Raylib backends.

### Keyboard

```fsharp
// Before
open Microsoft.Xna.Framework.Input

InputMap.empty
|> InputMap.key MoveLeft Keys.A
|> InputMap.key Jump Keys.Space

Keyboard.onPressed (fun (key: Keys) -> ...) ctx

// After
open Mibo.Input

InputMap.empty
|> InputMap.key MoveLeft KeyCode.A
|> InputMap.key Jump KeyCode.Space

Keyboard.onPressed (fun (key: KeyCode) -> ...) ctx
```

### Mouse

```fsharp
// Before — mouse button was an int (0 = left, 1 = right, 2 = middle)
|> InputMap.mouse Shoot 0

Mouse.onButton (fun (btn: MouseButtons) -> ...) ctx

// After
|> InputMap.mouse Shoot MouseButtonCode.Left

// onButton now also yields the position alongside the button:
Mouse.onButton (fun (btn: MouseButtonCode, pos: Vector2) -> ...) ctx
```

### Gamepad

```fsharp
// Before
|> InputMap.gamepadButton Jump PlayerIndex.One Buttons.A

// After — player index is a plain int, button is a backend-neutral code
|> InputMap.gamepadButton Jump 0 GamepadButtonCode.FaceDown

Gamepad.listenPlayer 0 (fun delta -> ...) ctx
```

### Translation modules

If you need to call MonoGame APIs that take native types, use the translation
modules in `Mibo.Input` (in the MonoGame backend):

```fsharp
let mgKey = KeyCode.toMonoGameKey keyCode
let mgBtn = GamepadButtonCode.toMonoGameButton gamepadBtn
// and the inverses:
let code = KeyCode.ofMonoGameKey mgKey
```

### New: Key combos

The new `Trigger.KeyCombo` case lets you bind multi-key combinations:

```fsharp
|> InputMap.keyCombo Save (Set [KeyCode.LeftControl; KeyCode.S])
```

### Gesture support

The `IInput` interface exposes `GestureDelta`, but **MonoGame's gesture
recognition is not mapped** — the `GestureDelta` stream is empty on the MonoGame
backend. Touch input is available via `Touch.listen`.

---

## 5. InputMapper setup

```fsharp
// Before
Program.mkProgram init update
|> Program.withInputMapper inputMap

// After
Program.mkProgram init update
|> MonoGameProgram.ofProgram
|> MonoGameProgram.withInputMapper inputMap
```

`MonoGameProgram.withInputMapper` lives on the MonoGame-specific
`MonoGameProgram` module (in `Mibo.MonoGame`, namespace `Mibo.Elmish`) and
operates on a `MonoGameProgram` (wrapping the Core `Program` via
`ofProgram`). It also calls `Program.withInput` automatically.

The subscription-based path (`InputMapper.subscribe` / `subscribeStatic`) works
the same and lives in the `Mibo.Input` namespace (in the MonoGame backend):

```fsharp
// Both old and new — unchanged if you use subscriptions
|> Program.withSubscription (InputMapper.subscribeStatic inputMap MapAction)
```

---

## 6. Assets

### Access style changed

The old `Assets` module took the context piped last. The new style is to resolve
the `IAssets` service once, then call its typed-loader methods.

```fsharp
// Before — module piped against the context
let tex = Assets.texture "player" ctx
let font = Assets.font "ui" ctx
let model = Assets.model "Models/player" ctx
let sfx = Assets.sound "jump" ctx
let effect = Assets.effect "Shaders/lighting" ctx

// After — resolve the service, then call methods
let assets = GameContext.getService<IAssets> ctx
let tex = assets.Texture "player"
let font = assets.Font "ui"
let model = assets.Model "Models/player"
let sfx = assets.Sound "jump"
let effect = assets.Effect "Shaders/lighting"
```

### `IAssets` now extends `IAssetCache`

```fsharp
// Mibo.Core — backend-neutral cache (namespace Mibo.Elmish)
type IAssetCache =
  abstract Get<'T> : key: string -> 'T voption
  abstract Create<'T> : key: string * factory: (unit -> 'T) -> 'T
  abstract GetOrCreate<'T> : key: string * factory: (unit -> 'T) -> 'T
  abstract Clear: unit -> unit
  abstract Dispose: unit -> unit

// Mibo.MonoGame — typed loaders (namespace Mibo.Elmish)
type IAssets =
  inherit IAssetCache
  abstract Texture: path: string -> Texture2D
  abstract Font: path: string -> SpriteFont
  abstract Sound: path: string -> SoundEffect
  abstract Model: path: string -> Model
  abstract Effect: path: string -> Effect
  // NEW — 3D skeletal animation (the old package had none of this)
  abstract ModelAnimations: path: string -> Animation3DClips
  abstract AnimatedMesh: path: string -> AnimatedMesh voption
```

The typed loaders (`Texture`, `Font`, `Sound`, `Model`, `Effect`) load via
`ContentManager` and cache automatically, exactly as before. The generic cache
methods (`Get`, `Create`, `GetOrCreate`) are now on `IAssetCache` and work
identically.

> **Note on the old generic cache:** the old `IAssets.Create`/`GetOrCreate` took
> a `GraphicsDevice -> 'T` factory. The new `IAssetCache.Create`/`GetOrCreate`
> take a `unit -> 'T` factory (resolve the device yourself via
> `MonoGameGameContext.getGraphicsDevice` if you need it).

### Portable code

If you write code that should work on any backend (not just MonoGame), depend on
`IAssetCache` instead of `IAssets`:

```fsharp
let cache = GameContext.getService<IAssetCache> ctx
let config = cache.GetOrCreate("config", fun () -> loadConfig())
```

See §11 for the new `ModelAnimations` / `AnimatedMesh` loaders and the content
pipeline caveats around animation data.

---

## 7. Cmd and Sub

### New `Msg` case

`Cmd<'Msg>` has a new `Msg of 'Msg` case. This is a zero-allocation alternative
to `Single(Effect(...))` for `Cmd.ofMsg`:

```fsharp
// Cmd.ofMsg now returns Msg directly — no delegate allocation
let cmd = Cmd.ofMsg MyMessage  // produces Msg MyMessage

// Cmd.map on Msg stays allocation-free
let mapped = Cmd.map transform cmd  // produces Msg(transformed)
```

If you pattern-match on `Cmd<'Msg>`, add the new case:

```fsharp
match cmd with
| Empty -> ...
| Msg msg -> ...          // NEW
| Single eff -> ...
| Batch effs -> ...
| DeferNextFrame effs -> ...
| NowAndDeferNextFrame(now, next) -> ...
| Quit -> ...
```

### New `Quit` case

`Cmd.signalExit` returns `Quit`, which signals the runtime to exit after the
current frame:

```fsharp
let update msg model =
  match msg with
  | ExitGame -> struct (model, Cmd.signalExit)
  | _ -> ...
```

---

## 8. Rendering

> The old package shipped **two** 2D/3D stacks and **two** 3D stacks (the legacy
> `Batch3DRenderer`/`Draw3D`, already `[<Obsolete>]`, and the modern
> `PipelineRenderer`/`Mibo.Rendering.Graphics3D`). The new `Mibo.MonoGame`
> consolidates these into one 2D stack and one 3D stack. **All the features
> (layer sorting, lighting, shadows, post-processing, PBR) are still there** —
> the module/type names changed.

### 8.1 2D rendering

#### Old → new module mapping

| Old (`Mibo.Elmish.Graphics2D`)                                                                              | New (`Mibo.Elmish.Graphics2D` / `.Lighting`)                                                                   |
| ----------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `Batch2DRenderer.create game view`                                                                          | `Renderer2D.create view`                                                                                       |
| `Batch2DRenderer.createWithConfig game cfg view`                                                            | `Renderer2D.createWith cfg view`                                                                               |
| `Batch2DConfig` (+ `withClearColor`/`withLighting`/`withPostProcess`/`withLitSprite`/`withShadowCaster`)    | `Renderer2DConfig` (+ `Renderer2DConfig.defaults`/`noClear`)                                                   |
| `RenderBuffer<RenderCmd2D>` (= `RenderBuffer<int<RenderLayer>, RenderCmd2D>`)                               | `RenderBuffer2D` (= `RenderBuffer<int<RenderLayer>, Command2D>`)                                               |
| `RenderCmd2D` DU (`DrawSprite`, `DrawText`, `DrawLine2D`, …)                                                | `Command2D` DU (same cases, renamed)                                                                           |
| `RenderLayer` measure                                                                                       | `RenderLayer` measure (unchanged)                                                                              |
| `sprite { }` / `text { }` CEs + `Buffer2D` extensions (`buffer.Sprite(...)`)                                | `SpriteState` / `TextState` records + the fluent draw DSL (`buffer.sprite state`, `buffer.text state`) |
| `Draw2D` fluent module                                                                                      | The fluent draw DSL on the buffer (sprites, text, shapes, lines, triangles, polys, cameras, shaders, targets, particles) |
| `Lighting2DConfig`, `PointLight2D`, `DirectionalLight2D`, `AmbientLight2D`, `Occluder2D`, `Shadows2DConfig` | `LightContext2D` + the same light/occluder records under `Mibo.Elmish.Graphics2D.Lighting`                     |
| 2D post-process (`VignetteConfig`, `BloomConfig2D`, `ColorGradeConfig`, `PostProcess2DConfig`)              | `PostProcess2D` module + `PostProcessPass`                                                                     |

#### Renderer creation

```fsharp
// Before
|> Program.withRenderer (fun game ->
  Batch2DConfig.defaults
  |> Batch2DConfig.withClearColor(ValueSome Color.Black)
  |> Batch2DConfig.withLighting lightingConfig
  |> Batch2DConfig.withLitSprite(game.Content.Load "Shaders/lighting")
  |> fun cfg -> Batch2DRenderer.createWithConfig game cfg view)

// After — the lit-sprite and shadow shaders are now bundled in the assembly;
// you no longer load them from content. Lighting is configured on the renderer.
|> Program.withRenderer (fun () ->
  Renderer2D.createWith Renderer2DConfig.defaults view)
```

#### View function and drawing

```fsharp
// Before — CE + fluent buffer extensions
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  buffer.Sprite(
    sprite {
      texture tex
      sourceRect rect
      at pos.X pos.Y
      size Constants.tileSize Constants.tileSize
      layer 0<RenderLayer>
    }
  ) |> ignore

// After — record builders + the fluent Draw DSL
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let dest = Rectangle(int model.Position.X, int model.Position.Y, 32, 32)
  buffer
    .sprite(
      SpriteState.create(tex, dest, model.SourceRect)
      |> SpriteState.withLayer 0<RenderLayer>
    )
    .drop()
```

The fluent DSL chains members on the buffer: `.sprite(...)`, `.text(...)`,
`.fillRect(...)`, `.lineThick(...)`, `.fillCircle(...)`, `.beginCamera(...)`,
`.beginShader(...)`, `.particles(...)`, etc. Each returns the buffer for
chaining; end the chain with `.drop()`. See
[Draw DSL](../draw-dsl.html).

#### 2D lighting & shadows

Lights and occluders are now submitted through the `LightContext2D` (under
`Mibo.Elmish.Graphics2D.Lighting`). The record shapes (`PointLight2D`,
`DirectionalLight2D`, `AmbientLight2D`, `Occluder2D`) are preserved. Soft
shadows, normal maps, and per-instance lit-sprite quads are all supported.

### 8.2 3D rendering

#### Old → new module mapping

| Old                                                                                                                  | New (`Mibo.Elmish.Graphics3D` / `.Pipelines`)                                                                                                     |
| -------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Program.withPipeline cfg view` (`Mibo.Rendering.Graphics3D`)                                                        | `Program.withRenderer (fun () -> Renderer3D.create (ForwardPipeline(...)) view)`                                                                  |
| `PipelineRenderer` / `RenderPipeline` / `IRenderPipeline`                                                            | `Renderer3D` + `IRenderPipeline3D` + `ForwardPipeline`/`ForwardPipelineBase`                                                                      |
| `ForwardPbrPipeline` (old class name)                                                                                | `ForwardPipeline` (the PBR Cook-Torrance pipeline)                                                                                                |
| `PipelineConfig` (+ `withShadows`/`withPostProcess`/`withDefaultLighting`/`withShader`)                              | `ForwardPipeline(?postProcess, ?shadowAtlas, ?shadowBias)` constructor + `Renderer3DConfig`                                                       |
| `ShadowConfig`                                                                                                       | `ShadowAtlasConfig` + `ShadowBiasConfig` (each with `.defaults`)                                                                                  |
| `PostProcessConfig` (+ `withBloom`/`withToneMapping`)                                                                | `PostProcessConfig3D` + `PostProcessPass3D`                                                                                                       |
| `PipelineBuffer<RenderCommand>`                                                                                      | `RenderBuffer3D` (= `RenderBuffer<unit, Command3D>`)                                                                                              |
| `RenderCommand` DU (`SetCamera`, `AddLight`, `Draw`, `DrawSpriteBillboard`, …)                                       | `Command3D` DU (`BeginCamera`, `AddPointLight`, `DrawModel`, `DrawBillboard`, …)                                                                  |
| `draw { }` / `quad { }` / `billboard { }` CEs + `PipelineBuffer` extensions (`.Camera(...).Draw(...).AddLight(...)`) | the fluent Draw DSL (`buffer.model(...)`, `buffer.addPointLight(...)`, `buffer.beginCamera(...)` — see [Draw DSL](../draw-dsl.html))                                           |
| `Light` DU (`Directional`/`Point`/`Spot`), `DirectionalLight`, `PointLight`, `SpotLight`, `ShadowSettings`           | `AmbientLight3D` + `DirectionalLight3D` + `PointLight3D` + `SpotLight3D` records (ambient is now its own record, not a field of a lighting state) |
| `Material` / `PBRMaterial` / `MaterialFlags`                                                                         | `Material3D` record (+ `Material3D.defaults`, `Material3D.fromModelMeshPart`)                                                                     |
| `Mesh` + `Mesh.fromModel`                                                                                            | `PrimitiveMesh` / `Primitive3D` (unit cube/sphere/cylinder/plane/torus/cone)                                                                      |
| Cascaded shadow maps                                                                                                 | Shadow atlas (directional/point/spot, R32F, 3×3 PCF)                                                                                              |
| 3D post-processing (bloom, SSAO, tone mapping: Reinhard/ACES/Filmic/AgX)                                             | `PostProcess3D` (bloom, tone mapping)                                                                                                             |

#### Renderer creation

```fsharp
// Before — withPipeline wired the modern 3D pipeline
|> Program.withPipeline
  (PipelineConfig.defaults
   |> PipelineConfig.withShadows(shadowCfg)
   |> PipelineConfig.withPostProcess(postCfg)
   |> PipelineConfig.withDefaultLighting(defaultLights)
   |> PipelineConfig.withShader ShaderBase.PBRForward "Effects/PBR")
  view

// After — a renderer wraps a pipeline + your view function
|> Program.withRenderer (fun () ->
  let pipeline =
    ForwardPipeline(
      shadowBias = ShadowBiasConfig.defaults,
      shadowAtlas = { ShadowAtlasConfig.defaults with Resolution = 4096 }
    )
  Renderer3D.create pipeline view)
```

`ForwardPipeline` takes optional `?postProcess`, `?shadowAtlas`, `?shadowBias`.
For a non-PBR shading strategy, subclass `ForwardPipelineBase` and override
`Shade`. There is also `NoopPipeline` if you want to do all drawing yourself via
`buffer.drawImmediate(...)`.

#### View function and the fluent Draw DSL

```fsharp
// After — fluent command recording
let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer3D) =
  buffer
    .beginCameraWith(Camera3D.render camera |> Camera3D.withClear skyColor)
    .setAmbientLight { Color = ambient; Intensity = 0.5f }
    .addDirectionalLight { Direction = sunDir; Color = sunColor
                           Intensity = 1.0f; CastsShadows = true }
    .model(model.PlayerModel, playerTransform)
    .billboard(tex, pos, size, color)
    .addPointLight(light)
    .endCamera()
    .drop()
```

The fluent 3D surface: `model`, `animatedModel`, `mesh`, `instanced`,
`billboard`, `billboardBatch`, `line3D`,
`beginCamera`/`beginCameraWith`/`endCamera`, `setAmbientLight`,
`addDirectionalLight`/`addPointLight`/`addSpotLight`, `setShadowOrigin`,
`enableShadows`/`disableShadows`, `beginEffect`/`endEffect` (per-group custom
shading), `drawImmediate` (raw `GraphicsDevice` access with a gathered
`SceneContext`), `drop`. See [Draw DSL](../draw-dsl.html) for the full surface.

#### What you can still use from Core

The generic `RenderBuffer<'Key, 'Cmd>` (in `Mibo.Core`, namespace `Mibo.Elmish`)
is still available as a sorted-command-buffer if you implement your own renderer
or command types.

---

## 9. Animation

### 9.1 2D sprite animation — unchanged

The `Mibo.Animation` module (`SpriteSheet`, `AnimatedSprite`, `Animation`) is
**present and unchanged** in `Mibo.MonoGame`. The old API ports directly:

```fsharp
// Works the same before and after
open Mibo.Animation

let sheet =
  SpriteSheet.fromGrid texture frameW frameH frameCount
    [| "idle", { Frames = [| |]; FrameDuration = 0.1f; Loop = true } |]

let mutable sprite = AnimatedSprite.create sheet "idle"
sprite <- AnimatedSprite.update dt sprite
let source = AnimatedSprite.currentSource sprite
```

The only change is **how you submit a draw** — go through the new 2D renderer
(see §8.1) instead of the old `RenderCmd2D.DrawSprite`.

### 9.2 3D skeletal animation — new (the old package had none)

The old package had **no 3D animation** — only bone-matrix pass-through via
`DrawSkinned`/the `withBones` CE op, where you supplied the `Matrix[]` yourself.
The new `Mibo.MonoGame` ships a full 3D skeletal-animation stack:

```fsharp
open Mibo.Animation   // AnimatedModel, Animation3DState, AnimatedMesh, Animation3DClips

// Load: a Model for the mesh/textures, plus the skeleton + clips from a raw file
// (see §11 for why the raw file is needed on MonoGame)
let model = assets.Model "Models/character"
let mesh = assets.AnimatedMesh rawPath          // AnimatedMesh voption
let clips = assets.ModelAnimations rawPath      // Animation3DClips

// AnimatedModel bundles Model + Mesh + State
let mutable anim = AnimatedModel.create model mesh clips "idle" 60.0f
anim <- anim |> AnimatedModel.blendTo "walk" 0.15f |> AnimatedModel.update dt

// Draw — the bone palette is computed for you
buffer.animatedModel(anim, transform).drop()
```

There is also a lower-level `Animation3DState` (carries the model on the state)
if you prefer to call `Animation3DState.applyToModel` + a plain
`buffer.model(...)` draw yourself.

---

## 10. Camera

The `Camera2D`/`Camera3D` helper modules **exist** in `Mibo.MonoGame`
(namespace `Mibo.Elmish`). The `Camera3D` is a struct record:

```fsharp
[<Struct>]
type Camera3D = {
  Position: Vector3
  Target: Vector3
  Up: Vector3
  FovY: float32          // radians (perspective) or world-units height (orthographic)
  NearPlane: float32
  FarPlane: float32
  Projection: CameraProjection  // Perspective | Orthographic
}
```

### Simplified construction

`Camera3D.create` takes just position, target, and FOV — sensible defaults
handle the rest (up = `Vector3.Up`, near = `0.1f`, far = `1000f`). Chain
`withUp` / `withNearFar` / `asOrthographic` to override:

```fsharp
// Before (old Mibo — 7 params, returned a Camera struct)
let camera = Camera3D.lookAt cameraPos target Vector3.Up
               (MathHelper.ToRadians 45.0f) aspect 0.1f 1000.0f

// After
let camera = Camera3D.create cameraPos target (MathHelper.ToRadians 55.0f)
// or with overrides:
let camera =
    Camera3D.create cameraPos target fov
    |> Camera3D.withUp customUp
    |> Camera3D.withNearFar 0.01f 5000.0f

// hand it to the 3D renderer via the fluent Draw DSL:
buffer
  .beginCameraWith(Camera3D.render camera |> Camera3D.withClear skyColor)
  // ...
  .drop()
```

The `Camera3D` module provides `create`, `orbit`, `screenPointToRay`, and
the `withUp` / `withNearFar` / `asOrthographic` modifiers — all returning
`Camera3D`. `Camera2D` provides the full 2D surface — `create`, `toMatrix`,
`viewportBounds`, `screenToWorld`/`worldToScreen`, and `smoothFollow`/
`clampTarget` (which return a new camera, since the camera's fields are
immutable). The rendering config builders (`render`, `withViewport`,
`withClear`, `splitScreen*`) live in the `Camera2D` and `Camera3D` modules
themselves — there is no separate config module to open.

---

## 11. Content pipeline & assets

This is the one area where MonoGame itself (not Mibo) forces backend-specific
behavior. Mibo.MonoGame uses MonoGame's `ContentManager`, so your existing
`.mgcb` / XNB pipeline keeps working for textures, fonts, sounds, models, and
effects.

### Asset path conventions

Content-pipeline assets are referenced by name **without extension or directory
prefix** (relative to `Content.RootDirectory`):

```fsharp
game.Content.RootDirectory <- "Content"
let assets = GameContext.getService<IAssets> ctx
let tex   = assets.Texture "player"                       // Content/player.xnb
let font  = assets.Font "diagnostics"                     // Content/diagnostics.xnb
let sfx   = assets.Sound "sfx_jump"                       // Content/sfx_jump.xnb
let model = assets.Model "kenney_platformer-kit/Models/block-grass"
let effect = assets.Effect "Shaders/lighting"
```

> If you are migrating code that ran on the Raylib backend, note that Raylib
> uses **raw files** by full path with extension
> (`"assets/.../block-grass.glb"`). The path strings are not portable across
> backends.

### Animation data and the double-load

MonoGame's content pipeline **discards animation data** when baking a `.glb` to
`.xnb`. To play 3D skeletal animations you load the model twice:

```fsharp
// Mesh + textures from the content pipeline (XNB)
let playerModel = assets.Model "Models/character"

// Skeleton + clips from the RAW .glb via Assimp (copy the raw file to your
// output directory; do NOT run it through MGCB)
let rawPath = System.IO.Path.Combine(AppContext.BaseDirectory, "animations", "character.glb")
let mesh  = assets.AnimatedMesh rawPath       // AnimatedMesh voption
let clips = assets.ModelAnimations rawPath    // Animation3DClips

model.PlayerAnim <- AnimatedModel.create playerModel mesh clips "idle" 60.0f
```

To ship the raw `.glb` without MGCB compiling it, use a `<Content Include>` with
a `<Link>` and `<CopyToOutputDirectory>`:

```xml
<None Include="animations\character.glb">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

This adds the `AssimpNetter` dependency to your project (the new backend uses it
to parse skeleton/clips at runtime).

### Shaders / effects

Custom HLSL effects (`.fx` compiled by the MGCB content pipeline via
`EffectImporter`/`EffectProcessor` to `.xnb`) load via
`assets.Effect path` as before. Note that the 2D lit-sprite and 3D PBR/shadow
shaders are now **bundled inside the `Mibo.MonoGame` assembly** — you no longer
need to author/ship `Shaders/lighting`, `Shaders/shadowcaster`, `Effects/PBR`,
etc. yourself. Drop those `Batch2DConfig.withLitSprite`/`withShader` lines.

---

## 12. What stayed the same

These modules moved to `Mibo.Core` with **identical APIs**:

| Module                                                                 | Namespace       | Notes                                                                            |
| ---------------------------------------------------------------------- | --------------- | -------------------------------------------------------------------------------- |
| `System` pipeline                                                      | `Mibo.Elmish`   | `start`, `pipeMutable`, `snapshot`, `pipe`, `dispatch`, `dispatchWith`, `finish` |
| `Cmd` / `Sub`                                                          | `Mibo.Elmish`   | Same + new `Msg` and `Quit` cases                                                |
| `GameTime`, `DispatchMode`, `FixedStepConfig`                          | `Mibo.Elmish`   | Unchanged                                                                        |
| `GameConfig`                                                           | `Mibo.Elmish`   | Unchanged                                                                        |
| `HeadlessProgram` / `HeadlessRunner`                                   | `Mibo.Elmish`   | Unchanged                                                                        |
| `CellGrid2D`, `HexGrid`, `Layout`, `HexLayout`                         | `Mibo.Layout`   | Unchanged                                                                        |
| `CellGrid3D`, `HexGrid3D`, `Layout3D`, `HexLayout3D`                   | `Mibo.Layout3D` | Unchanged                                                                        |
| `Grid2DSpatial`, `Hex2DSpatial`                                        | `Mibo.Layout`   | Unchanged                                                                        |
| `Grid3DSpatial`, `Hex3DSpatial`                                        | `Mibo.Layout3D` | Unchanged                                                                        |
| `LayeredGrid2D`, `LayeredHexGrid`, `LayeredLayout`, `LayeredHexLayout` | `Mibo.Layout`   | Unchanged                                                                        |
| `LayeredHexGrid3D`, `LayeredHexLayout3D`                               | `Mibo.Layout3D` | Unchanged                                                                        |
| `Platformer`, `TopDown` stamps                                         | `Mibo.Layout`   | Unchanged                                                                        |
| `Interior`, `Terrain` stamps                                           | `Mibo.Layout3D` | Unchanged                                                                        |

---

## 13. Headless testing (new)

The new architecture adds headless simulation for unit testing (in `Mibo.Core`,
namespace `Mibo.Elmish`). This did not exist in the original Mibo:

```fsharp
open Mibo.Elmish

let program =
  HeadlessProgram.mkHeadless init update
  |> HeadlessProgram.withTick Tick

let runner = HeadlessRunner(program)

// Advance one frame
runner.Step(TimeSpan.FromMilliseconds(16))

// Advance N frames
runner.StepN(100, TimeSpan.FromMilliseconds(16))

// Run until condition
runner.StepUntil(fun m -> m.Health <= 0, TimeSpan.FromMilliseconds(16))

// Enumerate frames
for gameTime, model in runner.Run(TimeSpan.FromMilliseconds(16)) do
  printfn "%A" model
```

Because headless programs live in `Mibo.Core`, you can test your simulation
(model + update + layout) with no graphics dependency.

---

## Full before/after example

A minimal 2D platformer-style game. This isolates the migration surface without
the 3D pipeline noise.

### Before (original Mibo)

```fsharp
open Mibo.Elmish
open Mibo.Input
open Mibo.Animation
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input

type Msg = Tick of GameTime | Action of ActionState<Action>
and Action = MoveLeft | MoveRight | Jump
and Model = { Position: Vector2; Sprite: AnimatedSprite }

let init ctx =
  let tex = Assets.texture "player" ctx
  let sheet = SpriteSheet.fromGrid tex 32 32 4 [|
    "idle", { Frames = [| Rectangle(0,0,32,32) |]; FrameDuration = 0.1f; Loop = true } |]
  { Position = Vector2.Zero; Sprite = AnimatedSprite.create sheet "idle" }, Cmd.none

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft Keys.A
  |> InputMap.key MoveRight Keys.D
  |> InputMap.key Jump Keys.Space

let update msg model =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    { model with Sprite = AnimatedSprite.update dt model.Sprite }, Cmd.none
  | Action state ->
    let dx = if Set.contains MoveLeft state.Held then -1f elif Set.contains MoveRight state.Held then 1f else 0f
    { model with Position = model.Position + Vector2(dx * 200f, 0f) * 0.016f }, Cmd.none

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  let source = AnimatedSprite.currentSource model.Sprite
  buffer.Sprite(
    sprite {
      texture model.Sprite.Sheet.Texture
      sourceRect source
      at model.Position.X model.Position.Y
      size 32f 32f
      layer 0<RenderLayer>
    }
  ) |> ignore

let program =
  Program.mkProgram init update
  |> Program.withConfig (fun (game, gdm) ->
    game.Content.RootDirectory <- "Content"
    game.Window.Title <- "Platformer"
    gdm.PreferredBackBufferWidth <- 1280
    gdm.PreferredBackBufferHeight <- 720)
  |> Program.withRenderer (fun game -> Batch2DRenderer.create game view)
  |> Program.withInput
  |> Program.withInputMapper inputMap
  |> Program.withAssets
  |> Program.withSubscription (InputMapper.subscribeStatic inputMap Action)
  |> Program.withTick Tick

[<EntryPoint>]
let main _ =
  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
```

### After (Mibo.Core + Mibo.MonoGame)

```fsharp
open Mibo.Elmish
open Mibo.Input
open Mibo.Animation
open Mibo.Elmish.Graphics2D
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

type Msg = Tick of GameTime | Action of ActionState<Action>
and Action = MoveLeft | MoveRight | Jump
and Model = { Position: Vector2; Sprite: AnimatedSprite; Texture: Texture2D }

let init ctx =
  let assets = GameContext.getService<IAssets> ctx
  let tex = assets.Texture "player"
  let sheet = SpriteSheet.fromGrid tex 32 32 4 [|
    "idle", { Frames = [| Rectangle(0,0,32,32) |]; FrameDuration = 0.1f; Loop = true } |]
  struct ({ Position = Vector2.Zero; Sprite = AnimatedSprite.create sheet "idle"; Texture = tex },
          Cmd.none)

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft KeyCode.A
  |> InputMap.key MoveRight KeyCode.D
  |> InputMap.key Jump KeyCode.Space

let update msg model =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    struct ({ model with Sprite = AnimatedSprite.update dt model.Sprite }, Cmd.none)
  | Action state ->
    let dx = if Set.contains MoveLeft state.Held then -1f elif Set.contains MoveRight state.Held then 1f else 0f
    struct ({ model with Position = model.Position + Vector2(dx * 200f, 0f) * 0.016f }, Cmd.none)

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  let source = AnimatedSprite.currentSource model.Sprite
  let dest = Rectangle(int model.Position.X, int model.Position.Y, 32, 32)
  buffer
    .sprite(
      SpriteState.create(model.Texture, dest, source)
      |> SpriteState.withLayer 0<RenderLayer>
    )
    .drop()

let program =
  Program.mkProgram init update
  |> Program.withConfig (fun cfg ->
    { cfg with Title = "Platformer"; Width = 1280; Height = 720 })
  |> Program.withRenderer (fun () -> Renderer2D.create view)
  |> Program.withInput
  |> Program.withAssets
  |> Program.withSubscription (InputMapper.subscribeStatic inputMap Action)
  |> Program.withTick Tick

let mgProgram =
  program
  |> MonoGameProgram.ofProgram
  |> MonoGameProgram.withInputMapper inputMap
  |> MonoGameProgram.withConfig (fun (game, _gdm) ->
    game.Content.RootDirectory <- "Content")

[<EntryPoint>]
let main _ =
  let game = new MiboGame<Model, Msg>(mgProgram)
  game.Run()
  0
```

### Key differences highlighted

1. `ElmishGame(program)` → `MiboGame(mgProgram)` (Core `Program` wrapped via `MonoGameProgram.ofProgram`; device-level config via `MonoGameProgram.withConfig`)
2. `Program.withConfig (fun (game, gdm) -> ...)` → `Program.withConfig (fun cfg -> { cfg with ... })` for window-level; `MonoGameProgram.withConfig (fun (game, gdm) -> ...)` for device-level (GraphicsProfile, vsync, `Content.RootDirectory`)
3. `Batch2DRenderer.create game view` → `Renderer2D.create view` (factory takes `unit`)
4. `Program.withInputMapper` → `MonoGameProgram.withInputMapper` (on the `MonoGameProgram` wrapper)
5. `Assets.texture "player" ctx` → `GameContext.getService<IAssets> ctx` + `assets.Texture "player"`
6. `Keys.A` → `KeyCode.A` (backend-neutral input codes)
7. `sprite { }` CE / `buffer.Sprite(...)` → `SpriteState.create` + `buffer.sprite(...)`
8. `RenderBuffer<RenderCmd2D>` → `RenderBuffer2D`
9. `init`/`update` now return `struct (model, cmd)` tuples

---

## Appendix: If you later target the Raylib backend

Because `Mibo.Core` is shared, your simulation code (model, update, layout,
spatial, input bindings) is portable. The backend-specific surface is not.
If you aim to share a game core between `Mibo.MonoGame` and `Mibo.Raylib`, these
are the divergences to plan for (surfaced by comparing the `MonoThreeD` and
`ThreeDSample` samples):

| Concern                 | Mibo.MonoGame                                                                    | Mibo.Raylib                                                                         |
| ----------------------- | -------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| Host                    | `MiboGame(mgProgram)` + `MonoGameProgram.withConfig` for `Content.RootDirectory` | `RaylibGame(program)` + `Program.withAssetsBasePath AppContext.BaseDirectory`       |
| Input mapper            | `MonoGameProgram.withInputMapper`                                                | `RaylibProgram.withInputMapper`                                                     |
| 3D pipeline             | `ForwardPipeline(shadowBias=, shadowAtlas=)`                                     | `ForwardPbrPipeline(shadowBiasConfig=, shadowAtlasConfig=)` (different field names) |
| Shadow config           | `ShadowBiasConfig.defaults`, `ShadowAtlasConfig { Resolution; GridSnapSize }`    | explicit per-light biases, `shadowAtlasConfig { Resolution; DirectionalLightSize }` |
| `Camera3D`              | struct record, **radians** FOV, defaulted near/far                               | the raylib `Camera3D` struct, **degrees** FOV, no explicit near/far                 |
| Vector / Color / Matrix | `Microsoft.Xna.Framework.*` (`Color(int)`, `Matrix.Create*`)                     | `System.Numerics` + `Raylib_cs` (`Color(byte)`, `Raymath.Matrix*`)                  |
| 3D animated model       | `AnimatedModel` + `buffer.animatedModel(...)` (bundles model+mesh+state)         | `Animation3DState` + `Animation3DState.applyToModel` + `buffer.model(...)`           |
| Animated mesh loader    | `assets.AnimatedMesh rawPath` (Assimp — XNB drops anim data)                     | not needed — raylib loads `.glb` once with animations                               |
| Assets                  | XNB content pipeline (names without extension)                                   | raw files (paths with extension)                                                    |
| Procedural 1×1 texture  | `new Texture2D(gd,1,1) + SetData`                                                | `GenImageColor + LoadTextureFromImage`                                              |
| Default font            | none — load `assets.Font "diagnostics"`                                          | `Raylib.GetFontDefault()`                                                           |
| Material factory        | `Material3D.fromModelMeshPart`                                                   | `Material3D.fromRaylibMaterial`                                                     |

**Portability tip:** pin your model's math types to `System.Numerics` (not
`Microsoft.Xna.Framework`) even on MonoGame. `Mibo.Core`'s layout/spatial modules
already use `System.Numerics.Vector3`, so this avoids conversion boilerplate at
the Core boundary. Convert to the backend's vector/matrix/color types only at the
view/draw edge.

---

## FAQ

### Can I still use the content pipeline?

Yes. `IAssets.Texture`, `Font`, `Sound`, `Model`, and `Effect` all load via
MonoGame's `ContentManager`, which uses the content pipeline. Your `.mgcb` files
and content builds work as before. The only exception is **3D animation data**,
which the pipeline discards — see §11.

### Do I need to rewrite my rendering from scratch?

No. The 2D and 3D rendering stacks still ship in `Mibo.MonoGame` — the renderer,
command, and DSL module names changed (see §8 for the old→new mapping). The
built-in PBR/shadow/lit-sprite shaders are now bundled in the assembly, so you
can delete your hand-maintained `Shaders/lighting`, `Effects/PBR`, etc.

### What about the 3D pipeline?

`ForwardPbrPipeline` / `PipelineRenderer` / `Program.withPipeline` are replaced
by `ForwardPipeline` + `Renderer3D.create`. Cook-Torrance PBR, shadows
(directional/point/spot), skeletal animation, hardware instancing, billboards,
lines, and post-processing are all present. For a non-PBR shading strategy,
subclass `ForwardPipelineBase` and override `Shade`.

### Can I use both Mibo.Raylib and Mibo.MonoGame in the same solution?

Yes, but not in the same project. Each backend is a separate assembly. Your game
core (model, update, layout) can reference `Mibo.Core` only and be shared between
backend-specific executables. See the appendix for the divergences to plan for.

### Do I still need to write `GameConfig` records by hand?

Only if you construct them literally. Use `GameConfig.defaultConfig` and the
`with*` helpers (or the `Program.withConfig (fun cfg -> { cfg with ... })` shape)
and the new fields (`MinWidth`/`MinHeight`) won't affect you.
