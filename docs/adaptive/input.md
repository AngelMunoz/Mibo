---
title: Adaptive Input
category: Adaptive
categoryindex: 3
index: 4
---

# Input in Adaptive Programs

Adaptive programs do not dispatch input as messages. The host polls the input service once per frame **before** the step runs, and your subscriptions see the deltas. The discipline has two tracks:

* **Continuous state** (where the cursor is, what is hovered) — write a `cval` root directly.
* **Discrete actions** (a click, a key press, a restart) — post an intent.

```fsharp
// A subscription: a stable id + an attach function that receives `post`
// and returns the detaching disposable.
let mouseSub (ctx: AdaptiveFrameContext) (cell: StateCell) : AdaptiveSub =
    let input = ctx.Context |> GameContext.getService<IInput>

    {
      Id = SubId.ofString "mouse"
      Attach =
        fun post -> input.MouseDelta.Subscribe(fun delta ->
            let state = cell.Value

            // Continuous: write the root — the hover projections
            // re-derive on the next force. The poll runs on the game
            // thread before Step, so the write is legal.
            state.HoverCell
            |> CVal.set(pickCell state ctx delta.Position)

            // Discrete: post the intent — it drains after Update,
            // before the frame is forced.
            if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Left then
                pickCell state ctx delta.Position
                |> ValueOption.iter(fun c ->
                    post(fun () -> Application.placeTower cell.Value c |> ignore)))
    }
```

## Why the split matters

Hover state changes many times per second and only feeds projections — a root write is the cheapest possible path, and the graph re-derives exactly the dependent nodes on the next force. Actions cause work (gold spends, rows appear) — they must run at a framework-owned moment, so they post.

The subscription set itself is an `amap<SubId, AdaptiveSub>` returned from `AdaptiveInit.withSubscriptions`: the runner diffs it every step, attaching new subscriptions and detaching removed ones. Because the map is derived from the current state, a restart that swaps the state can swap the subscription set too — cache the built map on the state's identity and the runner's version check makes clean steps diff-free.

## Callbacks only get `post`

The attach function receives a `post` function and nothing else that runs game code. A foreign-thread callback (a network thread, a task completion) can therefore never run game logic directly — it can only enqueue work, handled on the owner thread at the next step's boundary. This is enforced by the shape of `AdaptiveSub`, not by convention.

## Semantic input

For keyboard maps and held-key queries, the [input](../input.html) contracts apply unchanged — the input service and the `InputMap`/`ActionState` machinery are shared between runtimes. The semantic mapper produces an `AdaptiveSub` that writes an action-state root; the sim's `update` reads that root once per step. The difference from MVU is delivery only: MVU dispatches an `InputMapped` message, adaptive programs read the root inside `update`.

## Backend seams

Keep them out of the sim. The one that matters here is the mouse wheel: raylib reports ±1 per notch while XNA reports ±120. Pass a `wheelScale` parameter from the client (raylib: `1.1`; MonoGame: `1.1 ** (1.0 / 120.0)`) and fold it into the zoom factor — no branching in shared code.
