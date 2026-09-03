---
title: Programs & Composition
category: MVU
categoryindex: 2
index: 2
---

# Programs & Composition

A `Program<'Model,'Msg>` is a **declarative configuration pipeline** for your Mibo game. It defines how the runtime should orchestrate your state, services, and rendering loop.

The `Program` builder lives in `Mibo.Mvu`, so the same combinators work on every backend. Only the host type and a couple of backend-specific extensions differ (see [Backend wiring](#Backend-wiring) below).

Instead of heavy inheritance or global state, you build your program by starting with a core and adding features one builder at a time.

## Core Definition

Every program starts with `Program.mkProgram init update`.

- **`init`**: Receives a `GameContext` and returns your starting state. This is where you load initial assets and trigger startup commands.
- **`update`**: The heart of your game logic. Receives a message and the current model, returning the next state.

## Typical Composition

Most Mibo games follow this "standard" setup in `Program.fs`:

```fsharp
let configureWindow (cfg: GameConfig) =
    { cfg with Width = 1280; Height = 720; Title = "My Game" }

let create3DRenderer () =
    let pipeline = ForwardPbrPipeline(...)   // raylib: ForwardPbrPipeline
    Renderer3D.create pipeline View.view     // MonoGame: ForwardPipeline

let createUiRenderer () = Renderer2D.create viewUi

let program =
  Program.mkProgram init update
  // 1. Configure window settings via GameConfig
  |> Program.withConfig configureWindow
  // 2. Add services (Core builder; asset caching is automatic via IAssets/IAssetCache)
  |> Program.withAssets
  |> Program.withTick Tick // Enqueue a message every frame
  // 3. Define the view
  |> Program.withRenderer create3DRenderer
  |> Program.withRenderer createUiRenderer

// Run the game with your backend's host:
//   raylib:   new RaylibGame<Model, Msg>(program)
//   MonoGame: new MiboGame<Model, Msg>(program)
let game = new RaylibGame<Model, Msg>(program)
game.Run()
```

---

## Amenities & Services

### `withAssets`
A placeholder for API consistency. Asset loading and caching are handled through the backend's `IAssets` (which extends the Core `IAssetCache`), so assets are obtained from the service registry via `GameContext.getService<IAssets> ctx`. No explicit opt-in is needed:

```fsharp
let assets = GameContext.getService<IAssets> ctx
let tex = assets.Texture("sprites/player")
```

Use `withAssetsBasePath` to configure a root path. The concrete asset *types* differ per backend (raylib vs XNA), but the `Get`/`GetOrCreate`/`Create` caching surface is backend-neutral through `IAssetCache`.

### `withInput`
Registers the `IInput` service, enabling Keyboard, Mouse, Touch, Gamepad, and Gesture subscriptions.

### `withSubscription`
Connects your Elmish subscriptions to the runtime. The subscription function is re-evaluated every time your model changes, allowing you to dynamically start/stop listeners.

```fsharp
let subscribe (ctx: GameContext) (model: Model) =
    Sub.batch [ ... ]

Program.mkProgram init update
|> Program.withSubscription subscribe
```

See [The Subscription](elmish.html#The-Subscription) in the Elmish guide for a detailed breakdown.

---

## Runtime & Performance Knobs

Mibo gives you control over how the game loop behaves.

### `withTick`
Standard per-frame update. Pass a constructor (e.g., `Tick`) and the runtime will dispatch it every frame with the current `GameTime`. Use this for UI animations, camera smoothing, or simple timers.

### `withFixedStep`
Ideal for physics or simulation stability. Unlike `withTick`, which runs exactly once per frame, `withFixedStep` might run zero, one, or many times per frame to maintain a precise simulation frequency.

```fsharp
|> Program.withFixedStep {
    StepSeconds = 1f / 60f
    MaxStepsPerFrame = 5
    MaxFrameSeconds = ValueSome 0.25f
    Map = PhysicsTick
}
```

### `withDispatchMode`
Controls when messages are processed.
- `DispatchMode.Immediate` (Default): Messages dispatched during `update` are processed immediately.
- `DispatchMode.FrameBounded`: Deferred to the next frame. Use this if you want to strictly prevent updates triggered from inside another update within a single frame.

---

## Renderers & Backend wiring

### `withRenderer`
Adds an `IRenderer` to the stack. Renderers run in the **order they are added**. It is common to add a 3D renderer first, followed by a 2D UI renderer.

```fsharp
let createRenderer () = Renderer2D.create view

|> Program.withRenderer createRenderer
```

### Backend wiring

The `Program` builder is in `Mibo.Mvu`, but a few pieces are backend-specific:

| Concern | raylib backend | MonoGame backend |
|---------|----------------|------------------|
| Host type | `RaylibGame<'Model,'Msg>` | `MiboGame<'Model,'Msg>` |
| Input mapper builder | `RaylibProgram.withInputMapper` | `MonoGameProgram.withInputMapper` |
| 3D pipeline | `ForwardPbrPipeline` | `ForwardPipeline` |
| Shader language | GLSL | HLSL (`.fx` → `.mgfx`) |

`withInputMapper` is the only builder that cannot live in Core, because it instantiates the backend's `IInputMapper` implementation. If you prefer to avoid the input-mapper service entirely, use the backend-neutral `InputMapper.subscribe` instead and handle a single message.

> _**TIP**_: The `.drawImmediate(...)` escape hatch and custom render commands are the raw backend integration points when you need GPU work outside the deferred command buffer.

---

## Advanced Configuration

### `withConfig`
Gives you direct access to the `GameConfig` record before the game initializes (the same `withConfig` used in the composition example above).

> _**NOTE**_: `Width`/`Height` here are **config-time** values. For the **live, resizable** window size at runtime (in `init`/`update`/`view`), read `ctx.WindowWidth`/`ctx.WindowHeight`; these update on resize. See the "Window size" section of [MonoGame type quirks](../monogame-types.html) for the full note.

> _**TIP**_: **Cumulative Pipeline**: You can call `withConfig` multiple times; each callback is executed in the order it was added, allowing you to layer configuration.

> _**IMPORTANT**_: **Platform Specifics**: This is where you should put logic that varies by platform. For example, your Desktop project might set a fixed window size, while your Mobile project might handle screen orientation or full-screen modes.
