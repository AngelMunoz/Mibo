---
title: Input
category: Amenities
categoryindex: 12
index: 1
---

# Input (raw + mapped)

Mibo supports input via **semantic input mapping** (hardware → actions) using `InputMap` + `InputMapper.subscribe`. The input contracts (`IInput`, `IInputMapper`, `InputMap`, `ActionState`, the key/mouse/gamepad/gesture *codes*) live in `Mibo.Core`, so input handling is portable across backends.

Subscription-based input is available for keyboard, mouse, touch, gamepad, and gestures.

## Semantic input mapping (actions)

Gameplay reads better when it talks about **actions** (Jump, Fire, Interact) instead of **keys**. Mibo provides `InputMap` and `ActionState` for this purpose.

### Define your action type

```fsharp
type Action =
    | MoveLeft
    | MoveRight
    | Jump
    | Fire
```

### Build an `InputMap`

Use the backend-neutral **code DUs** (`KeyCode`, `MouseButtonCode`, `GamepadButtonCode`, `GestureKind`) from `Mibo.Input` — no need to reference `Raylib_cs`:

```fsharp
open Mibo.Input

let map =
    InputMap.empty
    |> InputMap.key MoveLeft KeyCode.A
    |> InputMap.key MoveLeft KeyCode.Left
    |> InputMap.key Jump KeyCode.Space
```

> _**NOTE**_: These code DUs are `[<RequireQualifiedAccess>]` — always write `KeyCode.W`, not
> bare `W`. raylib users migrating from the old API: `KeyboardKey.X` → `KeyCode.X` (number keys
> renamed: `KeyboardKey.Zero/One/…` → `KeyCode.D0/D1/…`).

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

For a zero-subscription alternative, register an `IInputMapper<'Action>` service via the per-backend builder (`Program.withInputMapper` is backend-specific because it instantiates the backend's mapper):

```fsharp
// raylib backend:
program |> RaylibProgram.withInputMapper map

// MonoGame backend:
program |> MonoGameProgram.withInputMapper map
```

This registers `IInput` automatically; you can then query the `IInputMapper<'Action>` service inline. (The `InputMapper.subscribe` subscription path above is backend-neutral and works on either backend without a per-backend builder.)

`ActionState` gives you three sets each frame:

| Field     | Description                           |
|-----------|---------------------------------------|
| `Held`    | Actions whose keys are currently down |
| `Started` | Actions pressed this frame            |
| `Released`| Actions released this frame           |

## See Also

- [Subscriptions](mvu/subscriptions.html) - Continuous input handling
- [Scaling](mvu/scaling.html) - Input handling patterns
