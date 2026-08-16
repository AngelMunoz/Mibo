---
title: Adaptive Input
category: Adaptive
categoryindex: 3
index: 5
---

# Input in Adaptive Programs

The host polls keyboard and mouse once per frame, before your update runs. You receive the results through subscriptions: small hooks you register at startup that get called with whatever changed.

## A subscription

A subscription is a stable id plus a function that attaches to an event source and returns a detacher. The attach function receives `post`, which schedules work to run inside the game loop:

```fsharp
let mouseSub (ctx: AdaptiveFrameContext) : AdaptiveSub =
    let input = ctx.Context |> GameContext.getService<IInput>

    {
      Id = SubId.ofString "mouse"
      Attach =
        fun post -> input.MouseDelta.Subscribe(fun delta ->
            // Hover: just remember where the cursor is. A cval write is
            // enough — anything derived from it updates automatically.
            hoverCell |> CVal.set(pickCell delta.Position)

            // Click: this changes game state, so run it in the loop.
            if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Left then
                pickCell delta.Position
                |> ValueOption.iter(fun cell ->
                    post(fun () -> collectAt cell)))
    }
```

The split is the guideline:

* **Where things are** (cursor position, hover) — write a `cval` directly. It's cheap and derived values follow automatically.
* **What the player did** (clicks, key presses) — `post` it, so it runs after your update, in order, on the game thread.

That second rule matters for callbacks that don't come from input at all — network messages, task completions. They arrive on other threads; `post` is the only safe way in. The shape of `AdaptiveSub` makes it so: attach gets `post` and nothing else that runs game code.

## Registering subscriptions

Collect your subscriptions into a map and hand it to the program. The runner attaches them at startup, detaches on shutdown, and re-checks the map every frame — return a different set and the changes apply automatically:

```fsharp
AdaptiveInit.ofFrameBuilder frameBuilder
|> AdaptiveInit.withSubscriptions(fun ctx ->
    [ SubId.ofString "mouse", mouseSub ctx
      SubId.ofString "keys", keyboardSub ctx ]
    |> AMap.ofList)
```

## Keyboard and semantic actions

For gameplay keys you rarely want raw keycodes scattered through your code. Define an action type and a key map, exactly like the [input guide](../input.html) shows — the `InputMap` and `ActionState` types are shared. The adaptive difference is only delivery: write the current action state into a `cval` from a subscription, and read it once at the top of your update.

One practical note: the mouse wheel reports ±1 per notch on raylib and ±120 on MonoGame. If zoom matters to your game, fold a scale factor in where you handle the wheel, so the feel matches on both backends.
