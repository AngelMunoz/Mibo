---
title: System Pipeline
category: MVU
categoryindex: 2
index: 5
---

# System Pipeline

The `Mibo.Elmish.System` module provides a generic pipeline pattern for composing frame updates with type-enforced snapshot boundaries.

When `update` grows, the hardest part is maintaining a clear mental model of:

- which subsystems are allowed to **mutate** the world
- which subsystems are **readonly/query**
- and where you want explicit "barriers" between them

`Mibo.Elmish.System` is a small pipeline helper that gives you:

- a natural _phase_ style
- a **type-enforced snapshot boundary**
- a single accumulated `Cmd<'Msg>` (no lists, no reversing)
- **encapsulated side-effects** via `dispatch` and `dispatchWith`

## The idea

You run mutation-heavy phases first, then take a snapshot (often a smaller readonly view), then run readonly phases.

```fsharp
| Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    System.start model
    |> System.pipeMutable (Physics.update dt)
    |> System.pipeMutable (Particles.update dt)
    |> System.snapshot Model.toSnapshot
    |> System.pipe (Ai.decide dt)
    |> System.finish Model.fromSnapshot
```

### What a "system" looks like

A system is a function that returns an updated state and a `Cmd`:

```fsharp
let physics (m: Model) : struct (Model * Cmd<Msg>) =
  // mutate-ish logic (still functional at the boundary)
  struct ({ m with ... }, Cmd.none)
```

## Emitting commands

Sometimes a system doesn't need to change state at all; it needs to trigger a sound, log an event, or dispatch a message. The `dispatch` variants let you run logic that only returns `Cmd<'Msg>`.

Because they don't return a new state, the pipeline passes the snapshot through as-is, which fits "fire-and-forget" side-effects and autonomous subsystems.

### Simple dispatch

Use `dispatch` for quick checks against the snapshot that only produce messages.

```fsharp
let checkPlayerHealth (snap: Snapshot) =
    if snap.Health <= 0f then Cmd.ofMsg PlayerDied else Cmd.none

// in the pipeline:
|> System.snapshot Model.toSnapshot
|> System.dispatch checkPlayerHealth
```

### Selective dispatch

Use `dispatchWith` for autonomous subsystems that track their own internal state (a mutable counter here, or an external service), fed through a selector that extracts what they need from the snapshot:

```fsharp
// Autonomous subsystem with its own state
let healthTracker (input: float32 voption) (snap: Snapshot) =
    let mutable hp = 100f
    let applyDamage (amt: float32) = hp <- hp - amt
    input |> ValueOption.iter applyDamage
    if hp <= 0f then Cmd.ofMsg PlayerDied else Cmd.none

// The selector bridges the parent snapshot to the subsystem's input,
// keeping the internal logic decoupled from your main model structure
let hitAmount (snap: Snapshot) =
    if snap.PlayerWasHit then ValueSome 10f else ValueNone

// in the pipeline:
|> System.dispatchWith hitAmount healthTracker
```

## Why the snapshot boundary matters

The key is the type change:

- before snapshot: `'Model`
- after snapshot: `'Snapshot`

That means you can't accidentally call a "mutable phase" after you've committed to readonly.

## When to use this (and when not)

Use it when:

- you have many continuous subsystems (physics, movement, particles, animation)
- you want predictable per-tick ordering
- you're heading toward <abbr title="action role-playing game">ARPG</abbr>/<abbr title="real-time strategy">RTS</abbr>-scale complexity

Skip it when:

- your game is small and `update` is still easy to read
- you're mostly event-driven (menus, turn-based)

See also: [Scaling Mibo (Simple → Complex)](scaling.html) (how this fits into the ladder).
