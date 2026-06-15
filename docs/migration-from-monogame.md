# Migrating from Mibo (MonoGame) to Mibo.MonoGame

> **Who this guide is for:** Users of the original `Mibo` package (the monolithic
> MonoGame library at `github.com/AngelMunoz/Mibo`) who want to migrate to the
> new split architecture (`Mibo.Core` + `Mibo.MonoGame`).
>
> This is the most breaking migration path — the original Mibo was a single
> assembly with built-in 2D/3D renderers, animation, camera helpers, and
> MonoGame-specific types throughout. The new architecture separates
> backend-agnostic contracts from backend-specific implementations.

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
Mibo.MonoGame      ← MonoGame host: MiboGame, input polling, asset loading,
                     MonoGameGameContext, MonoGameProgram.withInputMapper
```

**Key principle:** if it's an interface or contract that portable code needs, it
lives in `Mibo.Core`. If it touches MonoGame types, it lives in `Mibo.MonoGame`.

## Package and namespace changes

| Old | New | Namespace |
|-----|-----|-----------|
| `Mibo` (single package) | `Mibo.Core` | `Mibo.Elmish`, `Mibo.Input`, `Mibo.Layout`, `Mibo.Layout3D` |
| `Mibo` (single package) | `Mibo.MonoGame` | `Mibo.Elmish`, `Mibo.Input` |

Most `open` declarations stay the same — the namespaces are preserved. The
exception is `Mibo.Input` for input code types (see below).

## Migration checklist

| Area | Breaking? | Effort |
|------|-----------|--------|
| Package references | Yes | Low — replace `Mibo` with `Mibo.Core` + `Mibo.MonoGame` |
| Program setup | Yes | Medium — `withConfig` signature changed, `withRenderer` signature changed |
| GameContext access | Yes | Medium — direct fields → service registry |
| Input types | Yes | Medium — MonoGame enums → backend-neutral codes |
| InputMapper setup | Yes | Low — `Program.withInputMapper` → `MonoGameProgram.withInputMapper` |
| Assets | Yes | Low — `IAssets` now extends `IAssetCache`; typed loaders unchanged |
| Cmd / Sub | Yes | Low — new `Msg` and `Quit` cases in DU |
| Rendering | **Yes** | **High** — no built-in renderers; you implement `IRenderer<'Model>` |
| Animation | **Yes** | **High** — `SpriteSheet`/`AnimatedSprite` removed |
| Camera | Yes | Medium — wrapper types removed; use MonoGame matrices directly |
| Layout / Spatial | No | None — moved to Core, same API |
| System pipeline | No | None — moved to Core, same API |

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

### `withConfig` signature changed

The old API gave you direct access to MonoGame's `Game` and
`GraphicsDeviceManager`. The new API takes a `GameConfig` transform function.

```fsharp
// Before
Program.mkProgram init update
|> Program.withConfig (fun (game, gdm) ->
  game.Window.Title <- "My Game"
  gdm.PreferredBackBufferWidth <- 1280
  gdm.PreferredBackBufferHeight <- 720
  gdm.SynchronizeWithVerticalRetrace <- true)

// After
Program.mkProgram init update
|> Program.withConfig (fun cfg ->
  { cfg with
      Title = "My Game"
      Width = 1280
      Height = 720
      TargetFPS = 60 })
```

`GameConfig` is a struct record:

```fsharp
[<Struct>]
type GameConfig = {
  Width: int          // default: 800
  Height: int         // default: 600
  Title: string       // default: varies by backend
  TargetFPS: int      // default: 60; 0 = unlimited
  MinWidth: int voption
  MinHeight: int voption
}
```

Helper functions are available: `GameConfig.withWidth`, `withHeight`,
`withTitle`, `withTargetFPS`, `withMinWidth`, `withMinHeight`.

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
  Batch2DRenderer.create game (fun ctx model buffer ->
    // draw stuff
  ))

// After
|> Program.withRenderer (fun () ->
  // Implement IRenderer<'Model> yourself — see "Rendering" section below
  { new IRenderer<MyModel> with
      member _.Draw(ctx, model, gameTime) =
        let gd = MonoGameGameContext.getGraphicsDevice ctx
        // draw stuff using SpriteBatch, gd, etc.
  })
```

### Removed builders

| Old builder | Replacement |
|-------------|-------------|
| `Program.withComponent` | Use `Program.withServiceRegistration` |
| `Program.withComponentRef` | Use `Program.withServiceRegistration` + `GameContext.getService` |
| `Program.withPipeline` | Use the `System` pipeline module (see below) |

### Game host

```fsharp
// Before
let game = ElmishGame(program)
game.Run()

// After
let game = MiboGame(program)
game.Run()
```

`MiboGame` inherits from `Microsoft.Xna.Framework.Game` just like `ElmishGame`
did. The API is the same.

---

## 3. GameContext access

The old `GameContext` exposed MonoGame types as direct fields:

```fsharp
// Before
let gd = ctx.GraphicsDevice
let content = ctx.Content
let game = ctx.Game
let w = ctx.WindowWidth
let h = ctx.WindowHeight
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

`WindowWidth` and `WindowHeight` are still direct members on `GameContext`.

---

## 4. Input types

The old API used MonoGame's native enum types directly. The new API uses
backend-neutral struct DUs from `Mibo.Core`.

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
// Before
|> InputMap.mouse Shoot 0  // left button as int

Mouse.onButton (fun (btn: MouseButtons) -> ...) ctx

// After
|> InputMap.mouse Shoot MouseButtonCode.Left

Mouse.onButton (fun (btn: MouseButtonCode) -> ...) ctx
```

### Gamepad

```fsharp
// Before
|> InputMap.gamepadButton Jump 0 Buttons.A

Gamepad.listenPlayer 0 (fun delta -> ...) ctx

// After
|> InputMap.gamepadButton Jump 0 GamepadButtonCode.FaceDown

Gamepad.listenPlayer 0 (fun delta -> ...) ctx
```

### Translation modules

If you need to call MonoGame APIs that take native types, use the translation
modules in `Mibo.Input`:

```fsharp
let mgKey = KeyCode.toMonoGameKey keyCode
let mgBtn = GamepadButtonCode.toMonoGameButton gamepadBtn
```

### New: Key combos

The new `Trigger.KeyCombo` case lets you bind multi-key combinations:

```fsharp
|> InputMap.keyCombo Save (Set [KeyCode.LeftControl; KeyCode.S])
```

### New: Gesture support

The `IInput` interface now exposes `GestureDelta`, but **MonoGame's gesture
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
|> MonoGameProgram.withInputMapper inputMap
```

`MonoGameProgram.withInputMapper` also calls `Program.withInput` automatically.

The subscription-based path (`InputMapper.subscribe` / `subscribeStatic`) works
the same but lives in the `Mibo.Input` namespace (in the MonoGame backend):

```fsharp
// Both old and new — no change needed if you use subscriptions
|> Program.withSubscription (InputMapper.subscribeStatic inputMap MapAction)
```

---

## 6. Assets

The `IAssets` interface now extends `IAssetCache`:

```fsharp
// Mibo.Core — backend-neutral cache
type IAssetCache =
  abstract Get<'T> : key: string -> 'T voption
  abstract Create<'T> : key: string * factory: (unit -> 'T) -> 'T
  abstract GetOrCreate<'T> : key: string * factory: (unit -> 'T) -> 'T
  abstract Clear: unit -> unit
  abstract Dispose: unit -> unit

// Mibo.MonoGame — typed loaders
type IAssets =
  inherit IAssetCache
  abstract Texture: path: string -> Texture2D
  abstract Font: path: string -> SpriteFont
  abstract Sound: path: string -> SoundEffect
  abstract Model: path: string -> Model
  abstract Effect: path: string -> Effect
```

The typed loaders (`Texture`, `Font`, `Sound`, `Model`, `Effect`) work exactly
as before — they load via `ContentManager` and cache automatically.

The generic cache methods (`Get`, `Create`, `GetOrCreate`) are now on
`IAssetCache` and work identically.

### Accessing assets

```fsharp
// Before
let assets = ctx.GetService<IAssets>()  // or however you accessed it
let tex = assets.Texture "player"

// After
let assets = GameContext.getService<IAssets> ctx
let tex = assets.Texture "player"
```

### Portable code

If you write code that should work on any backend (not just MonoGame), use
`IAssetCache` instead of `IAssets`:

```fsharp
let cache = GameContext.getService<IAssetCache> ctx
let config = cache.GetOrCreate("config", fun () -> loadConfig())
```

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

## 8. Rendering — the biggest change

> **This is the most significant breaking change.** The original Mibo shipped
> built-in 2D and 3D renderers (`Batch2DRenderer`, `PipelineRenderer` with
> `ForwardPbrPipeline`, lighting, shadows, post-processing). The new
> `Mibo.MonoGame` backend ships **zero renderers**. You implement
> `IRenderer<'Model>` yourself.

### What you need to do

Create your own renderer that implements `IRenderer<'Model>`:

```fsharp
type IRenderer<'Model> =
  abstract Draw: GameContext * 'Model * GameTime -> unit
```

A typical 2D renderer using SpriteBatch:

```fsharp
let createMyRenderer () =
  let mutable spriteBatch: SpriteBatch = null

  { new IRenderer<MyModel> with
      member _.Draw(ctx, model, _gameTime) =
        let gd = MonoGameGameContext.getGraphicsDevice ctx

        if spriteBatch = null then
          spriteBatch <- new SpriteBatch(gd)

        spriteBatch.Begin()

        // draw your game using spriteBatch.Draw, spriteBatch.DrawString, etc.
        spriteBatch.End()

    interface IDisposable with
      member _.Dispose() =
        if spriteBatch <> null then spriteBatch.Dispose() }
```

Register it:

```fsharp
|> Program.withRenderer createMyRenderer
```

### What you lost

The old Mibo's rendering stack included:

| Old feature | Status in new Mibo.MonoGame |
|-------------|---------------------------|
| `Batch2DRenderer` with layer sorting | **Removed** — implement your own SpriteBatch renderer |
| `RenderCmd2D` (DrawSprite, DrawText, etc.) | **Removed** — use MonoGame draw calls directly |
| `RenderBuffer<RenderLayer, RenderCmd2D>` | Still in Core as a generic helper, but no 2D commands to fill it with |
| DSL CEs (`sprite { }`, `text { }`) | **Removed** |
| `Draw2D` fluent module | **Removed** |
| 2D lighting (point, directional, ambient, occluders, soft shadows) | **Removed** |
| 2D post-processing (vignette, bloom, color grading) | **Removed** |
| `PipelineRenderer` + `ForwardPbrPipeline` | **Removed** |
| `Command3D` / `RenderCommand` | **Removed** |
| PBR materials, shadow atlas, cascaded shadow maps | **Removed** |
| 3D post-processing (bloom, SSAO, tone mapping) | **Removed** |
| `BillboardBatch`, `LineBatch3D`, `SpriteQuadBatch` | **Removed** |

### What you can use from Core

`RenderBuffer<'Key, 'Cmd>` is still available in Core as a generic
sorted-command-buffer. You can use it with your own command types:

```fsharp
type MyCommand = DrawSprite of ... | DrawText of ...

let buffer = RenderBuffer<int, MyCommand>()
buffer.Add(0, DrawSprite { ... })
buffer.Add(1, DrawText { ... })
buffer.Sort()
```

### Recommendation

If you were using the old rendering stack heavily, consider:

1. **Keep it simple:** Write a straightforward SpriteBatch renderer. Most
   MonoGame games don't need a deferred command buffer.
2. **Port the commands:** If you relied on `RenderCmd2D`, define your own command
   DU and a small renderer that interprets it.
3. **Use MonoGame's content pipeline:** Shaders, effects, and models are loaded
   via `ContentManager` as before.

---

## 9. Animation

The `Mibo.Animation` module (`SpriteSheet`, `AnimatedSprite`, `Animation`) is
**not in `Mibo.Core` or `Mibo.MonoGame`**. It existed in the original MonoGame
Mibo but was not ported to the new architecture.

### What you need to do

Implement your own sprite animation. The pattern is straightforward:

```fsharp
type SpriteAnimation = {
  Frames: Rectangle[]
  FrameDuration: float32
  Loop: bool
}

type AnimatedSprite = {
  Animation: SpriteAnimation
  CurrentFrame: int
  TimeInFrame: float32
}

let updateAnimatedSprite (dt: float32) (sprite: AnimatedSprite) =
  let newTime = sprite.TimeInFrame + dt

  if newTime >= sprite.Animation.FrameDuration then
    let nextFrame = sprite.CurrentFrame + 1

    if nextFrame >= sprite.Animation.Frames.Length then
      if sprite.Animation.Loop then
        { sprite with CurrentFrame = 0; TimeInFrame = 0f }
      else
        { sprite with CurrentFrame = sprite.Animation.Frames.Length - 1; TimeInFrame = newTime }
    else
      { sprite with CurrentFrame = nextFrame; TimeInFrame = 0f }
  else
    { sprite with TimeInFrame = newTime }
```

---

## 10. Camera

The old `Camera` record (`{ View: Matrix; Projection: Matrix }`) and the
`Camera2D`/`Camera3D` helper modules are **not in `Mibo.Core` or
`Mibo.MonoGame`**.

### What you need to do

Use MonoGame's matrix helpers directly:

```fsharp
// 2D camera
let viewMatrix =
  Matrix.CreateTranslation(-cameraPosition.X, -cameraPosition.Y, 0f)
  * Matrix.CreateScale(zoom)
  * Matrix.CreateTranslation(screenWidth / 2f, screenHeight / 2f, 0f)

// 3D camera
let viewMatrix = Matrix.CreateLookAt(cameraPos, targetPos, Vector3.Up)
let projMatrix = Matrix.CreatePerspectiveFieldOfView(
  MathHelper.ToRadians(45f), aspectRatio, 0.1f, 1000f)
```

For `screenToWorld` / `worldToScreen` conversions, invert the view matrix.

---

## 11. What stayed the same

These modules moved to `Mibo.Core` with **identical APIs**:

| Module | Namespace | Notes |
|--------|-----------|-------|
| `System` pipeline | `Mibo.Elmish` | `start`, `pipeMutable`, `snapshot`, `pipe`, `dispatch`, `dispatchWith`, `finish` |
| `Cmd` / `Sub` | `Mibo.Elmish` | Same + new `Msg` and `Quit` cases |
| `GameTime`, `DispatchMode`, `FixedStepConfig` | `Mibo.Elmish` | Unchanged |
| `GameConfig` | `Mibo.Elmish` | Unchanged |
| `HeadlessProgram` / `HeadlessRunner` | `Mibo.Elmish` | Unchanged |
| `CellGrid2D`, `HexGrid`, `Layout`, `HexLayout` | `Mibo.Layout` | Unchanged |
| `CellGrid3D`, `HexGrid3D`, `Layout3D`, `HexLayout3D` | `Mibo.Layout3D` | Unchanged |
| `Grid2DSpatial`, `Hex2DSpatial` | `Mibo.Layout` | Unchanged |
| `Grid3DSpatial`, `Hex3DSpatial` | `Mibo.Layout3D` | Unchanged |
| `LayeredGrid2D`, `LayeredHexGrid`, `Layered` | `Mibo.Layout` | Unchanged |
| `Platformer`, `TopDown` stamps | `Mibo.Layout` | Unchanged |
| `Interior`, `Terrain` stamps | `Mibo.Layout3D` | Unchanged |

---

## 12. Headless testing (new)

The new architecture adds headless simulation for unit testing:

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

This did not exist in the original Mibo.

---

## Full before/after example

### Before (original Mibo)

```fsharp
open Mibo.Elmish
open Mibo.Input
open Mibo.Animation
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input

type Msg = Tick of GameTime | Action of ActionState<Action>
type Action = MoveLeft | MoveRight | Jump
type Model = { Position: Vector2; Sprite: AnimatedSprite }

let init ctx =
  let assets = ctx.GetService<IAssets>()
  let sheet = SpriteSheet.fromGrid "player" (assets.Texture "player") 32 32
  { Position = Vector2.Zero
    Sprite = AnimatedSprite.create sheet }, Cmd.none

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft Keys.A
  |> InputMap.key MoveRight Keys.D
  |> InputMap.key Jump Keys.Space

let update msg model =
  match msg with
  | Tick gt ->
    { model with Sprite = AnimatedSprite.update gt.ElapsedGameTime model.Sprite }, Cmd.none
  | Action state ->
    let dx = if Set.contains MoveLeft state.Held then -1f elif Set.contains MoveRight state.Held then 1f else 0f
    { model with Position = model.Position + Vector2(dx * 200f, 0f) * 0.016f }, Cmd.none

let view ctx model buffer =
  let source = AnimatedSprite.currentSource model.Sprite
  buffer.Add(0<RenderLayer>, DrawSprite {
    Texture = model.Sprite.Sheet.Texture
    Position = model.Position
    Source = source
    Color = Color.White
    Scale = Vector2.One
    Rotation = 0f
    Origin = Vector2.Zero
  })

let program =
  Program.mkProgram init update
  |> Program.withConfig (fun (game, gdm) ->
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
  let game = ElmishGame(program)
  game.Run()
  0
```

### After (Mibo.Core + Mibo.MonoGame)

```fsharp
open Mibo.Elmish
open Mibo.Input
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

type Msg = Tick of GameTime | Action of ActionState<Action>
type Action = MoveLeft | MoveRight | Jump
type Model = { Position: Vector2; Texture: Texture2D }

let init ctx =
  let assets = GameContext.getService<IAssets> ctx
  struct ({ Position = Vector2.Zero
            Texture = assets.Texture "player" }, Cmd.none)

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft KeyCode.A
  |> InputMap.key MoveRight KeyCode.D
  |> InputMap.key Jump KeyCode.Space

let update msg model =
  match msg with
  | Tick _gt -> struct (model, Cmd.none)
  | Action state ->
    let dx = if Set.contains MoveLeft state.Held then -1f elif Set.contains MoveRight state.Held then 1f else 0f
    struct ({ model with Position = model.Position + Vector2(dx * 200f, 0f) * 0.016f }, Cmd.none)

let createRenderer () =
  let mutable spriteBatch: SpriteBatch = Unchecked.defaultof<_>

  { new IRenderer<Model> with
      member _.Draw(ctx, model, _gameTime) =
        let gd = MonoGameGameContext.getGraphicsDevice ctx

        if spriteBatch = null then
          spriteBatch <- new SpriteBatch(gd)

        spriteBatch.Begin()
        spriteBatch.Draw(model.Texture, model.Position, Color.White)
        spriteBatch.End()

    interface IDisposable with
      member _.Dispose() =
        if spriteBatch <> null then spriteBatch.Dispose() }

let program =
  Program.mkProgram init update
  |> Program.withConfig (fun cfg ->
    { cfg with Title = "Platformer"; Width = 1280; Height = 720 })
  |> Program.withRenderer createRenderer
  |> MonoGameProgram.withInputMapper inputMap
  |> Program.withAssets
  |> Program.withSubscription (InputMapper.subscribeStatic inputMap Action)
  |> Program.withTick Tick

[<EntryPoint>]
let main _ =
  let game = MiboGame(program)
  game.Run()
  0
```

### Key differences highlighted

1. `Keys.A` → `KeyCode.A` (input codes)
2. `Program.withConfig (fun (game, gdm) -> ...)` → `Program.withConfig (fun cfg -> { cfg with ... })`
3. `Batch2DRenderer.create game view` → custom `IRenderer<Model>` implementation
4. `Program.withInputMapper` → `MonoGameProgram.withInputMapper`
5. `ElmishGame(program)` → `MiboGame(program)`
6. `ctx.GetService<IAssets>()` → `GameContext.getService<IAssets> ctx`
7. `SpriteSheet`/`AnimatedSprite` → direct texture drawing (or your own animation)
8. `RenderBuffer` + `DrawSprite` command → direct `SpriteBatch.Draw` call

---

## FAQ

### Can I still use the content pipeline?

Yes. `IAssets.Texture`, `Font`, `Sound`, `Model`, and `Effect` all load via
MonoGame's `ContentManager`, which uses the content pipeline. Your `.mgcb` files
and content builds work as before.

### Can I use both Mibo.Raylib and Mibo.MonoGame in the same solution?

Yes, but not in the same project. Each backend is a separate assembly. Your game
core (model, update, layout) can reference `Mibo.Core` and be shared between
backend-specific executables.

### What about the 3D pipeline?

The old `ForwardPbrPipeline`, `PipelineRenderer`, `Command3D`, `Material3D`,
shadow mapping, and post-processing are **not** in the new MonoGame backend. If
you need 3D rendering, implement it against MonoGame's `BasicEffect` or your own
custom effects.

### What about `GameConfig` field names that don't exist in old code?

If you were constructing `GameConfig` records directly (not using the DSL), you
need to add the new fields. Use `GameConfig.defaultConfig` and the `with*`
helpers to avoid this.

### The `Cmd<'Msg>` DU has new cases — will my pattern matches break?

Yes, if they're exhaustive without a wildcard. Add `| Msg msg -> dispatch msg`
and `| Quit -> ()` (or `| _ -> ()`).
