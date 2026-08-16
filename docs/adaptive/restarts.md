---
title: Restarts & State
category: Adaptive
categoryindex: 3
index: 11
---

# Restarts & State

Most games need a "restart" — back to level one, fresh world, same window. In an adaptive game the runner, the window, and the renderers are all built around a state graph, so you can't just throw the state away. The pattern that makes restart cheap is: **wrap the state in a mutable holder, and restart by swapping the holder's value.**

## The state cell

```fsharp
/// The composition root and the frame force read the CURRENT state
/// through this holder. A restart swaps the value in place; the next
/// frame re-binds to the fresh state, so the runner, the window, and
/// the subscriptions all survive the swap.
[<Sealed>]
type StateCell(value: State) =
    member val Value = value with get, set
```

Nothing in the game reads `state` directly — update, the frame builder, and every subscription go through `cell.Value`. That one indirection is the whole trick.

```fsharp
let init (cell: StateCell) (ctx: AdaptiveFrameContext) : AdaptiveInit<Frame> =
    AdaptiveInit.ofFrameBuilder(fun () -> frame cell.Value)

let update (cell: StateCell) (ctx: AdaptiveContext) (gameTime: GameTime) =
    let state = cell.Value
    // ... tick the sim on state ...
```

## Restarting

Build a fresh state and put it in the cell. Because the frame builder reads `cell.Value` *at pack time*, the very next frame packs the fresh world — no window re-create, no runner rebuild:

```fsharp
let restart (cell: StateCell) (config: WorldConfig) =
    cell.Value <- State.init config
```

Call it from wherever a restart is triggered — a button, a key, a game-over screen. Do it inside the update (or a posted intent), so the swap never happens mid-frame while another part of the loop is reading the old state.

## Why subscriptions keep working

Subscriptions read `cell.Value` too. A restart swaps the state's identity, and the next step's subscription diff sees the fresh state — the runner re-attaches what changed, keeps what didn't, all keyed by id. Your mouse and keyboard hooks survive a restart without you re-registering anything:

```fsharp
let subscriptions (cell: StateCell) (ctx: AdaptiveFrameContext) : amap<SubId, AdaptiveSub> =
    // reads cell.Value; re-runs when the state's identity changes
    ...
```

See [Subscriptions](subscriptions.html) for the map contract the diffing relies on.

## When to use this

Use a state cell whenever the state can be replaced at runtime — restarts, loading a save, switching levels. For a game whose state is built once and never swapped, closing over the state directly (as the [Programs](program.html) example does) is fine and slightly simpler. The cell is the tool for "the world resets while the app keeps running".
