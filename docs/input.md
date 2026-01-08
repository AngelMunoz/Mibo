---
title: Input Mapping
category: Architecture
index: 2
---

# Semantic Input Mapping

Mibo features a powerful semantic input mapping system that decouples physical hardware inputs (keyboard, mouse, gamepad) from your game logic.

## The Problem

In many game frameworks, player logic is littered with hardware-specific checks:

```fsharp
if keyboardState.IsKeyDown(Keys.Space) then
    jump()
```

This makes it difficult to support gamepads, rebinding, or multiple platforms.

## The Mibo Solution: InputMapper

With Mibo, you define your game actions as a Discriminated Union:

```fsharp
type Action =
    | MoveLeft
    | MoveRight
    | Jump
    | Fire
```

Then you create an `InputMap` to bind keys to these actions:

```fsharp
let inputMap =
    InputMap.empty
    |> InputMap.key MoveLeft Keys.A
    |> InputMap.key MoveLeft Keys.Left
    |> InputMap.key Jump Keys.Space
    |> InputMap.gamepadButton Jump PlayerIndex.One Buttons.A
```

## Consuming Input in Update

The `InputMapper` subscription automatically emits messages to update your model with an `ActionState`. In your update function, you check for the state of these actions:

```fsharp
match msg with
| InputChanged actionState ->
    if actionState.Started.Contains Jump then
        // Logic for jumping

    if actionState.Held.Contains MoveLeft then
        // Logic for moving left
```

## Benefits

1. **Multi-Input support**: Bind as many keys/buttons to a single action as you like.
2. **Analog values**: Supports analog input from gamepad triggers and thumbsticks via `actionState.Values`.
3. **Action-based logic**: Your game logic becomes easier to read and test.
4. **Rebinding**: Since the `InputMap` is just data, you can easily save/load it to allow players to rebind their controls.
