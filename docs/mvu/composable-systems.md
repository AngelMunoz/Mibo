---
title: Composable Systems
category: MVU
categoryindex: 2
index: 9
---

# Composable Systems

> **NOTE:** these rules apply to adaptive programs too: features own their data, report what happened as data, and one place reacts. See [Adaptive Systems](../adaptive/systems.html).

## The problem

As a game grows, the `update` function becomes a dumping ground: input, physics, AI, particles, audio, UI state, all tangled together. Changing one thing breaks another. Testing is impossible because everything depends on everything else. The function grows to hundreds of lines and nobody wants to touch it.

The solution is to split the game into **independent sub-systems** that each own one concern, and coordinate them through a **router**, not a god-function that reaches into every piece of state.

## The routed sub-system architecture

The unit of decomposition is a **sub-system**: an independent module that owns its **model**, its **message type**, and its **update function**. Sub-systems never call or import each other. The root `update` is a **router** that dispatches messages to sub-systems and translates the declarative values they emit into commands for the consumers that care.

```
  Msg ──┬──▶ Input.update      ──▶ model.Input
        ├──▶ Physics.update    ──▶ model.Player
        ├──▶ Weapon.update     ──▶ model.Weapon + WeaponEvent
        └──▶ EnemyAi.update    ──▶ model.Enemy + EnemyEvent

  WeaponEvent  ──▶ router ──▶ Cmd<Msg>   (audio + effects)
  EnemyEvent   ──▶ router ──▶ Cmd<Msg>   (player + audio)
```

The router is the **only** place that knows which systems consume which events. Each sub-system stays independently testable because it has no dependencies on its siblings.

### 1. The root `update` is a router, not game logic

The root `update` function (wherever you wire up `Program.mkProgram`) routes messages to the matching sub-system and translates emitted events into `Cmd<Msg>` for other systems. It contains **no game logic**: only dispatch and translation.

```fsharp
let update msg model =
    match msg with
    | WeaponMsg wmsg ->
        let weapon, events = Weapon.update wmsg model.Weapon
        // router: translate the sub-system's events into commands for consumers
        let cmd = events |> Seq.collect translateWeaponEvent |> Cmd.batch
        { model with Weapon = weapon }, cmd
    | Tick gt -> runTickPipeline gt model
    // ...
```

### 2. Each sub-system owns its slice

A sub-system owns its model, its message type, and its update. It mutates/returns **only** its own state. It never imports another sub-system's update or reaches into another sub-system's model.

```fsharp
module Weapon =
    type Model = { Ammo: int; Cooldown: float32; ... }
    type Msg = | Fire | Reload | RefillAmmo

    let update (msg: Msg) (model: Model) : Model * WeaponEvent seq =
        // touches only model.Ammo / model.Cooldown: nothing else
        ...
```

### 3. Cross-system communication is declarative

When a sub-system needs to affect another, it returns **declarative values**: Events (what happened) or Intents (what should happen). These are pure data. The router translates each into `Cmd<Msg>` for the relevant systems. The emitting system does not know (or import) its consumers.

```fsharp
type WeaponEvent =
    | Fired of pos: Vector3 * dir: Vector3
    | EnemyKilled of pos: Vector3

// router-side translation:
let translateWeaponEvent (event: WeaponEvent) =
    match event with
    | WeaponEvent.Fired(pos, dir) ->
        [| AudioMsg.OneShot(fire, pos); EffectMsg.SpawnSmoke(pos, dir) |]
    | WeaponEvent.EnemyKilled(pos) ->
        [| AudioMsg.OneShot(injured, pos); PlayerMsg.AddScore 100 |]
```

The weapon system never imports audio or effects. It emits `Fired` and moves on. Add a new consumer (a screen-shake system, an achievement tracker) by adding a translation in the router; the weapon system is untouched.

### 4. Read access goes through a read-only query, but mind the hot path

When a sub-system needs to **read** another's state, the router passes it read-only access, never a direct mutable reference to another sub-system's model. There are two forms, and the choice depends on call frequency.

**Cold path (event-driven, turn-based): a closure query record.** The query hides the source model behind function fields. Building it per-message is acceptable. Each field is a small function carrying the root model with it (a closure):

```fsharp
[<Struct>]
type TargetingQuery = {
    UnitAt: Vector2 -> UnitId voption
    IsReachable: Vector2 -> bool
    CurrentFaction: Faction
}

let unitAt (cell: Vector2) = model.Units |> Map.tryFind cell
let isReachable (cell: Vector2) = model.Map.Reachable.Contains cell

let query = {
    UnitAt = unitAt
    IsReachable = isReachable
    CurrentFaction = model.Turn.CurrentFaction
}
```

**Hot path (per-tick, real-time): direct values.** Function-typed record fields are heap values: each one you build allocates, and the runtime cannot optimize calls through them as well as direct calls. Building such a query inside `Tick` allocates every frame. For real-time AI, pass the needed values directly instead:

```fsharp
// signature: direct values, no closures, no query record
let update (dt: float32) (playerPos: Vector3) (enemies: Enemy[]) (colliders: BoundingBox[]) : EnemyEvent seq
```

No closures: `playerPos` is a struct value, `enemies`/`colliders` are direct array references. Every read inside is a direct field access. The caller extracts values once:

```fsharp
let playerPos = model.Player.Position                       // one struct copy
let events = EnemyAi.update dt playerPos model.Enemy.Items model.Colliders
```

The read-only contract still holds: the AI receives `playerPos` (a value, it cannot mutate the player) and mutates only its own `enemies`. Decoupling is achieved by *passing values*, not by wrapping reads in closures.

> **Rule:** closure query = cold path only (event-driven, turn-based). Per-tick reads = direct values (real-time). Never construct a closure-bearing query inside `Tick`; it allocates per frame and the runtime cannot inline the calls.

### 5. `Cmd.map` lifts sub-commands

Sub-system commands are `Cmd<SubMsg>`. When they don't need cross-system translation, lift them directly into the root `Msg` via `Cmd.map`:

```fsharp
let childCmd = Child.update cmsg model.Child |> snd
model, Cmd.map ChildMsg childCmd
```

When the sub-system's events need translation into other systems' messages, the router expands them into root commands instead:

```fsharp
let cmd = Weapon.update wmsg model.Weapon |> snd
          |> Seq.collect translateWeaponEvent |> Cmd.batch
```

## The Tick pipeline: composing sub-systems per frame

Real-time games run many sub-systems every tick in a fixed order (physics before AI, AI before effects). Mibo's `System` pipeline makes that ordering explicit and enforces a **snapshot boundary** between mutation and query phases. Each phase calls a sub-system that owns its slice; the pipeline is the composition mechanism, not a replacement for the architecture.

```fsharp
let updateAudio (dt: float32) (snap: Snapshot) =
    audio.Update(dt, snap)
    snap, Cmd.none

let finishWithModel _ = model

let runTickPipeline (gt: GameTime) (model: Model) =
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    System.start model
    // mutation phases: each sub-system mutates only its own slice
    |> System.pipeMutable (Physics.update dt)     // model.Player
    |> System.pipeMutable (weaponSystem dt)       // model.Weapon → WeaponEvent → Cmd
    |> System.pipeMutable (enemySystem dt)        // model.Enemy  → EnemyEvent  → Cmd
    |> Model.toSnapshot                           // readonly boundary
    // readonly phases: backend services read a consistent this-frame state
    |> System.pipe (updateAudio dt)
    |> System.finish finishWithModel
```

The pipeline and the routed-sub-system architecture compose. A sub-system in the pipeline still owns its slice and emits events; the router still translates them. The pipeline makes per-tick ordering and the mutable/readonly boundary explicit.

### Snapshot boundary

The `snapshot` call changes the pipeline's type from the mutable `Model` to a readonly `Snapshot`, a struct record sharing sub-model references (zero allocation). After it, only `System.pipe` (readonly) phases are allowed. The compiler prevents a query phase from accidentally mutating state that a later mutation phase expects untouched.

```fsharp
System.start model
|> System.pipeMutable (Physics.update dt)
|> System.pipeMutable (Particles.update dt)
|> System.snapshot Model.toSnapshot
|> System.pipe (Ai.decide dt)        // readonly: reads the snapshot
|> System.finish Model.fromSnapshot
```

## When to use

- Your `update` has grown past ~50 lines, or a single concern (physics, AI, combat) is hard to change in isolation.
- You have cross-cutting interactions ("enemy died" should trigger a sound, a score bump, and a particle burst) and they're currently implemented by one system reaching into several models.
- You want sub-systems to be unit-testable without standing up the whole game.

You don't need it for a small game where a single `update` with pattern matching is still easy to read; see [Scaling Mibo](scaling.html) for when each rung pays off.

## See also

- [System Pipeline](system.html): the `System.start`, `pipeMutable`, `snapshot` API.
- [Commands](commands.html): `Cmd.map` and `Cmd.batch` for lifting/combining sub-system commands.
- [Scaling Mibo](scaling.html): where this pattern sits on the complexity ladder.
