---
title: Input
category: v1
categoryindex: 200
index: 5
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# Input (raw + mapped)

Mibo.Raylib supports input via **semantic input mapping** (hardware → actions) using `InputMap` + `InputMapper.subscribe`.

Subscription-based input is available for keyboard, mouse, touch, gamepad, and gestures. Direct polling is also available via `InputPolling`.

## Semantic input mapping (actions)

Gameplay reads better when it talks about **actions** (Jump, Fire, Interact) instead of **keys**. Mibo.Raylib provides `InputMap` and `ActionState` for this purpose.

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
open Raylib_cs
open Mibo.Input

let map =
    InputMap.empty
    |> InputMap.key MoveLeft KeyboardKey.A
    |> InputMap.key MoveLeft KeyboardKey.Left
    |> InputMap.key Jump KeyboardKey.Space
```

### Subscribe with `InputMapper.subscribe`

The recommended approach uses `InputMapper.subscribe` to wire your `InputMap` into an Elmish subscription:

```fsharp
open Mibo.Input

type Msg =
    | InputMapped of ActionState<Action>

let subscribe (ctx: GameContext) (model: Model) : Sub<Msg> =
    InputMapper.subscribeStatic map InputMapped ctx
```

Then in your program:

```fsharp
open Mibo.Elmish

Program.mkProgram init update
|> Program.withInput
|> Program.withSubscription subscribe
```

Each frame, the mapper dispatches `InputMapped` with the current action state:

```fsharp
let update msg model =
    match msg with
    | InputMapped actions ->
        if actions.Started.Contains Jump then
            // do jump
            ()
        struct ({ model with Actions = actions }, Cmd.none)
```

For a zero-subscription alternative, use `Program.withInputMapper` which registers an `IInputMapper<'Action>` service you can query inline.

`ActionState` gives you three sets each frame:

| Field     | Description                           |
|-----------|---------------------------------------|
| `Held`    | Actions whose keys are currently down |
| `Started` | Actions pressed this frame            |
| `Released`| Actions released this frame           |

## See Also

- [Subscriptions](subscriptions.html) - Continuous input handling
- [Scaling](../scaling.html) - Input handling patterns
