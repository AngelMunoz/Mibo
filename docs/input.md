---
title: Input
category: Amenities
index: 20
---

# Input (raw + mapped)

Mibo supports two complementary input styles:

1. **Raw input deltas** (keyboard/mouse/touch/gamepad) via `IInput` + subscriptions.
2. **Semantic input mapping** (hardware → actions) via `InputMap` + `InputMapper`.

You can use either approach, or combine them (raw for debug/UI, mapped for gameplay).

## Raw input (hardware deltas)

### Enabling input polling

Register the input component in your program:

```fsharp
Program.mkProgram init update
|> Program.withInput
```

This registers an `IInput` service on `Game.Services` and polls devices each MonoGame frame.

### Subscriptions (recommended)

The `Mibo.Input` namespace provides small subscription helpers that turn input deltas into Elmish messages.

```fsharp
open Microsoft.Xna.Framework.Input
open Mibo.Input

type Msg =
    | KeyDown of Keys
    | KeyUp of Keys
    | MouseMoved of Point
    | Tick of GameTime

let subscribe (ctx: GameContext) _model =
    Sub.batch [
        Keyboard.onPressed KeyDown ctx
        Keyboard.onReleased KeyUp ctx
        Mouse.onMove MouseMoved ctx
    ]
```

This style keeps device polling out of your `update` function.

### Direct service access (advanced)

If you need custom behavior, you can access `IInput` directly:

```fsharp
let input = Input.getService ctx
use sub = input.KeyboardDelta.Subscribe(fun delta -> ...)
```

## Semantic input mapping (actions)

Raw deltas are great for tools and UI, but gameplay often reads better when it talks about **actions** (Jump, Fire, Interact) instead of **keys**.

### Define your action type

```fsharp
type Action =
    | MoveLeft
    | MoveRight
    | Jump
    | Fire
```

### Build an `InputMap`

```fsharp
open Microsoft.Xna.Framework.Input
open Mibo.Input

let map =
    InputMap.empty
    |> InputMap.key MoveLeft Keys.A
    |> InputMap.key MoveLeft Keys.Left
    |> InputMap.key Jump Keys.Space
    |> InputMap.gamepadButton Jump PlayerIndex.One Buttons.A
```

### Subscribe to mapped action state

`InputMapper.subscribe` listens to raw input deltas and emits an `ActionState<'Action>`.

```fsharp
type Msg =
    | InputMapped of ActionState<Action>
    | Tick of GameTime

let subscribe (ctx: GameContext) _model =
    Sub.batch [
        InputMapper.subscribeStatic map InputMapped ctx
    ]
```

Then in `update` you can either:

- store the latest `ActionState` in your model and consume it on `Tick`, or
- treat `Started` / `Released` as event-like fields and handle them immediately.

```fsharp
match msg with
| InputMapped actions ->
        struct ({ model with Actions = actions }, Cmd.none)

| Tick gt ->
        if model.Actions.Started.Contains Jump then
            // do jump
            ()
        struct (model, Cmd.none)
```

### Dynamic remapping

If you want runtime rebinding, use the overload that takes `getMap : unit -> InputMap<_>` (often backed by a `ref`).

```fsharp
let mapRef = ref map
InputMapper.subscribe (fun () -> mapRef.Value) InputMapped ctx
```

### Alternative: pull-based mapping (service style)

If you prefer "update once per frame, then read current state" (instead of subscription-push), you can register an `IInputMapper<'Action>` service via `Program.withInputMapper`.

This style is useful when your simulation is already `Tick`-driven and you want input mapping to behave like a per-frame snapshot.

#### Register the mapper

```fsharp
Program.mkProgram init update
|> Program.withInputMapper map
```

`withInputMapper`:

- registers `IInput` automatically (equivalent to `Program.withInput`)
- registers an `IInputMapper<'Action>` service on `Game.Services`
- ticks the mapper each MonoGame frame (via a `GameComponent`)

#### Read state in `update`

Your `update` function does **not** receive `GameContext`, so the usual pattern is:

1. access the service in `init` (which _does_ receive `GameContext`), then
2. store it somewhere your `update` can read (often your model).

```fsharp
open Mibo.Input

type Model = {
    Mapper: IInputMapper<Action>
    Actions: ActionState<Action>
}

let init (ctx: GameContext) =
    let mapper = InputMapper.getService<Action> ctx
    struct ({ Mapper = mapper; Actions = ActionState.empty }, Cmd.none)

let update msg model =
    match msg with
    | Tick _gt ->
        let actions = model.Mapper.CurrentState
        if actions.Started.Contains Jump then
            // do jump
            ()
        struct ({ model with Actions = actions }, Cmd.none)
    | _ ->
        struct (model, Cmd.none)
```

If you would rather keep service references out of your model, you can also stash the mapper in a `ref` during `init` and read it from `update` via closure state.
