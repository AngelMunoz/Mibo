---
title: Composable Systems
category: Patterns
categoryindex: 6
index: 62
---

# Composable Systems

## What and Why

As your game grows, the `update` function becomes a dumping ground — input handling, physics, AI, particles, UI state, all tangled together. Changing one thing breaks another. Testing is impossible because everything depends on everything else.

The pattern: break your update into small, independent functions (systems) that run in a fixed order. Each system owns one concern. They compose with a pipeline that makes ordering explicit.

## Use Cases

### Any action game
Input → Physics → Particles → Rendering. Four systems, each one testable in isolation.

### Strategy game
Input → Economy → AI → Combat → UI. Economy runs before AI so AI sees updated resource counts. Combat runs before UI so health bars reflect the latest damage.

### Multiplayer game
Input → Simulation → Network Sync → Prediction. The network system reads the simulation result and sends it. The prediction system runs after receiving remote state.

### Platformer
Input → Movement → Collision → Camera → Particles. Camera runs after collision so it reads the resolved position, not the pre-collision one.

## The Technique

Each system is a function with the same signature:

```fsharp
let mySystem (dt: float32) (model: Model) : struct (Model * Cmd<Msg>) =
  // do work, mutate model, return
  struct (model, Cmd.none)
```

Compose them with a pipeline:

```fsharp
System.start model
|> System.pipeMutable (inputSystem dt)
|> System.pipeMutable (physicsSystem dt)
|> System.pipeMutable (aiSystem dt)
|> System.pipeMutable (particleSystem dt)
|> System.finish id
```

`pipeMutable` passes the model by reference — no allocation per system call. Use `pipe` for immutable updates if you prefer functional style.

### Snapshot boundaries

For larger games, add a type-enforced boundary between mutable and readonly phases:

```fsharp
System.start model
|> System.pipeMutable (inputSystem dt)
|> System.pipeMutable (physicsSystem dt)
|> System.snapshot Model.toReadonly
|> System.pipe (aiSystem dt)
|> System.finish Model.fromReadonly
```

After the snapshot, the compiler prevents accidental mutation in downstream systems.

## Key Insight

Ordering is the whole point. Input must run before physics. Physics must run before rendering. The pipeline makes this ordering visible and enforced — not buried in a 200-line `update` function where a misplaced line breaks everything.

## When to use

- Your update function has grown past ~50 lines.
- You have multiple phases that need predictable ordering.
- You want to test phases independently.
- You need to swap phases (e.g., replay mode skips physics).
- You're adding features and afraid to touch the update function.

## See also

- [ThreeDSample/Systems.fs](https://github.com/...) — nine systems composed in a real game.
- [System Pipeline](../system.html) — the `System.start`, `pipeMutable`, `snapshot` pipeline.
