---
title: Subscriptions (external events)
category: Adaptive
categoryindex: 3
index: 4
---

# Subscriptions (external events)

Your update function runs once per frame, but games also react to things that arrive on their own schedule: the mouse moves, a key goes down, a network message lands, a timer fires. Subscriptions are how those events get into the loop.

## What a subscription is

A subscription is two things: a stable id, and a function that attaches to an event source and returns a detacher. The attach function receives `post`, which schedules work to run inside the game loop — it does not run anything itself.

```fsharp
type AdaptiveSub = {
    Id: SubId
    Attach: ((unit -> unit) -> unit) -> IDisposable
}
```

Here's one for the mouse, using the framework's input service:

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

            // Click: this changes game state, so run it in the loop
            if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Left then
                pickCell delta.Position
                |> ValueOption.iter(fun cell ->
                    post(fun () -> collectAt cell)))
    }
```

The split in that example is the guideline for input specifically:

* **Where things are** (cursor position, hover) — write a `cval` directly. It's cheap and derived values follow automatically.
* **What the player did** (clicks, key presses) — `post` it, so it runs after your update, in order, on the game thread.

## Why attach only gets `post`

Event sources don't run on your thread. A network callback arrives on a socket thread; a task completes on the thread pool. Your state containers belong to the game thread — touching them from anywhere else is not allowed.

The shape of `AdaptiveSub` makes the safe thing the only thing: the callback receives `post` and nothing else that runs game code. Whatever thread the event fires on, the reaction runs on the game thread at a framework-chosen moment.

## Registering subscriptions

`AdaptiveInit.withSubscriptions` takes a function that returns the subscription set as an `amap<SubId, AdaptiveSub>` — a map, keyed by the subscription ids. Build a named function and register it in `init`:

```fsharp
let subscriptions (ctx: AdaptiveFrameContext) : amap<SubId, AdaptiveSub> =
    [ SubId.ofString "mouse", mouseSub ctx
      SubId.ofString "keys", keyboardSub ctx ]
    |> AMap.ofList

let init (world: World) (ctx: AdaptiveFrameContext) : AdaptiveInit<Frame> =
    AdaptiveInit.ofFrameBuilder (frame world)
    |> AdaptiveInit.withSubscriptions subscriptions
```

The map has to be a **stable adaptive map** — the runner calls your function every step but only re-reads the map when its version moved, and it identifies subscriptions by key: a key that survives a change keeps its attachment, a key that vanishes gets detached (at the next step's boundary — a detachment lags one frame behind the change).

Two consequences:

* With `AMap.ofList` you get a fixed set — build it once and return it every step; clean steps do no diffing at all. Don't rebuild a fresh `ofList` per call and expect changes to be seen — return the same map.
* When the set should follow game state (menus vs. gameplay, connected players), derive it: project your state into the entries and lift with `AMap.ofAVal`.

## Dynamic subscription sets

Deriving the map from a `cval` makes the runner follow the state. When `mode` changes, the map's version moves, the diff runs, and the right subscriptions attach or detach:

```fsharp
let subscriptions (world: World) : amap<SubId, AdaptiveSub> =
    world.Mode                                  // cval<GameMode>
    |> AVal.map(function
        | Menu ->
            [ SubId.ofString "menu-keys", menuKeySub ] |> Map.ofList |> Map.toSeq
        | Playing ->
            [ SubId.ofString "mouse", mouseSub
              SubId.ofString "keys", keyboardSub ]
            |> Map.ofList |> Map.toSeq)
    |> AMap.ofAVal
```

For full control there is `AMap.custom`: its compute function receives the current entries and appends the operations describing what changed — useful when your subscription source is an event queue rather than state. The map contract is the same either way: keys are identity, version gates the diff.

```fsharp
let subMap : amap<SubId, AdaptiveSub> =
    AMap.custom(fun current delta ->
        // consume your own event queue, appending adds/removes
        ())
```

## Beyond input

The same shape covers any event source. A timer that spawns a wave every thirty seconds:

```fsharp
let waveTimer (interval: float32) : AdaptiveSub = {
    Id = SubId.ofString "wave-timer"
    Attach =
        fun post ->
            let timer = new System.Timers.Timer(float interval * 1000f)
            timer.Elapsed.Add(fun _ -> post spawnWave)
            timer.Start()
            { new IDisposable with member _.Dispose() = timer.Stop() }
}
```

A network client pushing opponent moves:

```fsharp
let opponentMoves (client: IGameClient) : AdaptiveSub = {
    Id = SubId.ofString "opponent"
    Attach =
        fun post -> client.OnMove.Subscribe(fun move -> post(fun () -> applyMove move))
}
```

One practical note on the mouse wheel: raylib reports ±1 per notch and MonoGame reports ±120. If zoom matters to your game, fold a scale factor in where you handle the wheel, so the feel matches on both backends.

For gameplay keys, the shared [input guide](../input.html) (`InputMap`, `ActionState`) applies here too — write the current action state into a `cval` from a subscription and read it once at the top of your update.
