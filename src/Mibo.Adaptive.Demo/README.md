# Mibo.Adaptive.Demo — AdaptivePong

A ping-pong game written on **`AdaptiveHeadless`** (`Mibo.Adaptive`): state as
adaptive roots, derived state as projections, and the frame forced once per
`Step` so drawing is plain struct reads. The simulation logic is the
PingPong sample's (ball, paddles, bounce, scores) — only the state layer
changed, from Elmish MVU to adaptive data.

The point of this demo is the **shape** — how a game is written this way —
not the game itself.

```bash
dotnet run --project src/Mibo.Adaptive.Demo -- sim      # headless: AI vs AI + telemetry
dotnet run --project src/Mibo.Adaptive.Demo -- raylib   # window: W/S move the left paddle
```

## The module map

| Module | What it is | Adaptive concepts |
|---|---|---|
| `Types` | Domain types and court constants | — |
| `Physics` | Pure simulation functions (bounce, clamp) | — |
| `Telemetry` | Demo instrumentation: recompute counters | — |
| `Paddle` | The paddle feature | root `Y`, projection `Rect`, `move` |
| `Ball` | The ball feature | root `Value`, projections `Rect` + `Threat`, `step` |
| `Scores` | The scoreboard feature | root `Value`, projection `Label`, `addPoint` |
| `World` | The composition root | `World` record, router `step`, frame builder |
| `Program` | Two frontends (sim, raylib) for the same world | writes the `Input` root, draws the frame |

## Where the projections are stored

A projection is an object: a cached value, the versions of its dependencies,
and a compute function. It recomputes only when it is **forced** (`getValue`)
and dirty. The runner never sees projections — it only calls the frame
builder the world provides. So something must retain them. In this game,
**the `World` record is that something**:

```
World (World.fs)
├── Input: cval<InputState>          root, written by the frontend
├── Paused: cval<bool>               root, written by the frontend
├── LeftPaddle: Paddle.State         feature record
│   ├── Y: cval<float32>             root
│   └── Rect: aval<Rect>             projection — retained here
├── RightPaddle: Paddle.State        same shape
├── Ball: Ball.State                 feature record
│   ├── Value: cval<Ball>            root
│   ├── Rect: aval<Rect>             projection — retained here
│   └── Threat: aval<bool>           projection, composed from ball + left paddle
└── Scores: Scores.State             feature record
    ├── Value: cval<Scores>          root
    └── Label: aval<string>          projection — retained here
```

Nothing lives at module scope. A feature is a bundle: its roots, its
projections, and its logic (`Paddle.move`, `Ball.step`, `Scores.addPoint`).
The `World` record is the store that owns the long-lived graph.

## How it composes

1. **Features take other features' roots as inputs.** `Ball.create` receives
   the left paddle's root, so `Threat` is one `AVal.map2` over two features'
   roots. The graph is wired at `World.create`.

2. **The router wires events.** `World.step` moves both paddles, feeds the
   ball physics the values it just wrote, and matches the ball's goal event:

   ```fsharp
   match Ball.step world.Ball dt leftY rightY with
   | ValueSome side ->
       Scores.addPoint world.Scores side
       Ball.reset world.Ball side
   | ValueNone -> ()
   ```

   Features never reach into each other's records; the world routes.

3. **The frame builder forces through the world.** `World.buildFrame` reads
   `world.Ball.Rect`, `world.Scores.Label`, … once per `Step` and packs the
   struct. The one exception: the HUD clock is a time-dependent projection,
   created in `Init` because it depends on the runner-owned time root.

A bigger game adds features to the world and routes more events; the shape
does not change.

## What the sim output shows

Run `-- sim` and read the telemetry:

```
ballRect          recomputed 361x  — the ball moved every live frame
leftPaddleRect    recomputed 168x  — only while the paddle moved
scoreLabel        recomputed   2x  — only when the score changed
clockLabel        recomputed 421x  — depends on the time root: every frame
```

That is the whole story in numbers: what changed recomputes, what did not
does not. During the paused phase the sim's projections recompute 0x and
allocate 0 bytes; the one thing that still runs is the clock label, because
it depends on the time root, which the runner advances every frame.

## What this is not

- Not an ECS — nothing iterates entities; the dirty thing pulls its own answer.
- Not MVU — there is no `'Msg`, no `Cmd`, no `Sub`, no model rebuild.
- Rendering is deliberately naive (ASCII / flat rects) — the point is that
  it is a function of the resolved frame, nothing more.
