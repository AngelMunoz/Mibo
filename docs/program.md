---
title: Programs & Composition
category: Architecture
index: 2
---

# Programs & Composition

A `Program<'Model,'Msg>` defines **how your game runs**:

- how you create the initial model (`init`)
- how you handle messages (`update`)
- which subscriptions run (`subscribe`)
- which renderers draw (`withRenderer`)
- which MonoGame components exist (`withComponent` / `withComponentRef`)
- which optional runtime features you want (tick, fixed-step, dispatch mode)

Most of the time you start with:

- `Program.mkProgram init update`
- then add capabilities with `Program.with*`

## Typical composition root

```fsharp
type Msg =
  | Tick of GameTime

let program =
  Program.mkProgram init update
  |> Program.withConfig (fun (game, gfx) ->
      game.Content.RootDirectory <- "Content"
      game.IsMouseVisible <- true
      gfx.PreferredBackBufferWidth <- 1280
      gfx.PreferredBackBufferHeight <- 720)
  |> Program.withAssets
  |> Program.withInput
  |> Program.withTick Tick
  |> Program.withSubscription subscribe
  |> Program.withRenderer (Batch2DRenderer.create view)

use game = new ElmishGame<Model, Msg>(program)
game.Run()
```

## Multiple renderers

You can add more than one renderer (for example: 3D world + 2D UI). Renderers run in the order they were added.

```fsharp
let program =
  Program.mkProgram init update
  |> Program.withRenderer (Batch2DRenderer.create viewUi)
  |> Program.withRenderer (Batch3DRenderer.create view3d)
```

## Adding MonoGame components (services)

Sometimes you want a classic MonoGame component (audio, networking, diagnostics, etc). Use `Program.withComponent`:

```fsharp
let program =
  Program.mkProgram init update
  |> Program.withComponent (fun game -> new MyAudioComponent(game) :> IGameComponent)
```

### `ComponentRef` (type-safe access without globals)

If you want to _use_ a component inside `update` / `subscribe`, create a `ComponentRef<'T>` and install via `Program.withComponentRef`.

```fsharp
let audioRef = ComponentRef<MyAudioComponent>()

let program =
  Program.mkProgram init update
  |> Program.withComponentRef audioRef (fun game -> new MyAudioComponent(game))

let update msg model =
  match audioRef.TryGet() with
  | ValueSome audio -> audio.Play("explosion")
  | ValueNone -> ()

  struct (model, Cmd.none)
```

## Optional runtime knobs

These are documented in more detail in [The Elmish Architecture](elmish.html):

- `Program.withTick` — enqueue a per-frame message
- `Program.withFixedStep` — framework-managed fixed timestep
- `Program.withDispatchMode` — immediate vs frame-bounded dispatch

See also: [System pipeline (phases + snapshot boundary)](system.html) and [Custom Rendering in Mibo](rendering.html).
