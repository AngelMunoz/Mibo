---
title: Programs & Composition
category: Architecture
index: 2
---

# Programs & Composition

A `Program<'Model,'Msg>` is a **declarative configuration pipeline** for your Mibo game. It defines how the runtime should orchestrate your state, services, and rendering loop.

Instead of heavy inheritance or global state, you build your program by starting with a core and layering capabilities using high-level combinators.

## Core Definition

Every program starts with `Program.mkProgram init update`.

- **`init`**: Receives a `GameContext` and returns your starting state. This is where you load initial assets and trigger startup commands.
- **`update`**: The heart of your game logic. Receives a message and the current model, returning the next state.

## Typical Composition

Most Mibo games follow this "standard" setup in `Program.fs`:

```fsharp
let program =
  Program.mkProgram init update
  // 1. Configure MonoGame boilerplate
  |> Program.withConfig (fun (game, gfx) ->
      game.Content.RootDirectory <- "Content"
      game.IsMouseVisible <- true
      gfx.PreferredBackBufferWidth <- 1280
      gfx.PreferredBackBufferHeight <- 720)
  // 2. Add Mibo services
  |> Program.withAssets  // Enables Assets.texture, Assets.model, etc.
  |> Program.withInput   // Enables Keyboard/Mouse/Gamepad polling
  |> Program.withTick Tick // Enqueue a message every frame
  // 3. Define the view
  |> Program.withRenderer (Batch3DRenderer.create view3d)
  |> Program.withRenderer (Batch2DRenderer.create viewUi)

// Run the game
use game = new ElmishGame<Model, Msg>(program)
game.Run()
```

---

## Amenities & Services

### `withAssets`
Enables the `IAssets` service. This provides caching for every core MonoGame type and includes many "quality of life" features like JSON deserialization and custom object caching. Assets are automatically disposed when the game exits.

### `withInput`
Installs the hardware polling service. This is required if you want to use the high-level `Keyboard`, `Mouse`, `Gamepad`, or `Touch` subscription modules.

### `withSubscription`
Connects your Elmish subscriptions to the runtime. The subscription function is re-evaluated every time your model changes, allowing you to dynamically start/stop listeners (like "only listen for Gamepad if in Gameplay state").

---

## Runtime & Performance Knobs

Mibo gives you fine-grained control over how the game loop behaves.

### `withTick`
Standard per-frame update. Pass a constructor (e.g., `Tick`) and the runtime will dispatch it every frame with the current `GameTime`. Use this for UI animations, camera smoothing, or simple timers.

### `withFixedStep`
Ideal for physics or simulation stability. Unlike `withTick`, which runs exactly once per frame, `withFixedStep` might run zero, one, or many times per frame to maintain a precise simulation frequency.

```fsharp
|> Program.withFixedStep {
    StepSeconds = 1f / 60f
    MaxStepsPerFrame = 5
    Map = PhysicsTick
}
```

### `withDispatchMode`
Controls when messages are processed.
- `Immediate` (Default): Messages dispatched during `update` are processed immediately.
- `FrameBounded`: Deferred to the next frame. Use this if you want to strictly prevent "re-entrant" updates within a single MonoGame call.

---

## MonoGame Integration

### `withRenderer`
Adds an `IRenderer` to the stack. Renderers run in the **order they are added**. It is common to add a 3D renderer first, followed by a 2D UI renderer.

### `withComponent`
The "escape hatch" for classic MonoGame. If you have an existing `IGameComponent` (like a networking library or a diagnostic overlay), you can plug it into the program lifecycle here.

### `withComponentRef`
Provides type-safe access to MonoGame components without globals. You create a `ComponentRef<'T>` and pass it to `withComponentRef`. You can then `TryGet()` that component inside your `update` or `subscribe` functions.

```fsharp
let audioRef = ComponentRef<MyAudioComponent>()

// Composition
|> Program.withComponentRef audioRef (fun game -> new MyAudioComponent(game))

// Usage in update
match audioRef.TryGet() with
| ValueSome audio -> audio.Play("explosion")
| ValueNone -> ()
```

---

## Advanced Configuration

### `withConfig`
Gives you direct access to the `Game` object and the `GraphicsDeviceManager` before the game even initializes.

> [!TIP]
> **Cumulative Pipeline**: You can call `withConfig` multiple times; each callback is executed in the order it was added, allowing you to layer configuration (e.g., base settings in your library, platform overrides in your executable).

> [!IMPORTANT]
> **Platform Specifics**: This is where you should put logic that varies by platform. For example, your Desktop project might set a fixed window size, while your Mobile project might handle screen orientation or full-screen modes.

Use this to set the window title, resolution, multi-sampling, or `IsFixedTimeStep`.
