---
title: Adaptive Services
category: Adaptive
categoryindex: 3
index: 6
---

# Services in Adaptive Programs

Games need things that aren't game state: audio, networking, save files. The [service composition guide](../services.html) covers the pattern in depth — build your services first, keep them in a record, pass them where needed. Everything there applies here too. This page is about the parts that work differently.

## Setup has a natural home

Your program gets a `boot` function that runs once, before the first frame, and receives the game context. That's the place for anything a service needs before it can work — connecting, loading, subscribing:

```fsharp
let boot (ctx: AdaptiveFrameContext) =
    env.Network.Connect()
```

On the Elmish side you sometimes need a service that can't be built until the game context exists, which forces awkward workarounds. Here there's no such corner: build what you can before the program, do the rest in `boot`.

## Framework services

Some services are already there — the asset cache, the input service. Pull them from the context when you need them instead of building your own:

```fsharp
let assets = ctx.Context |> GameContext.getService<IAssets>
```

## Background work comes back through post

Never touch game state from a background thread — the containers are single-threaded by design. Use `ctx.Intents.postTask` (or `postAsync`), and its completion runs on the game thread where writes are allowed:

```fsharp
ctx.Intents.postTask(fun () ->
    env.SaveData.SaveAsync(snapshot))
```

A task that needs the result back in the game posts it:

```fsharp
ctx.Intents.postTask(fun () ->
    task {
        let! scores = env.Leaderboard.Fetch()
        do ctx.Intents.post(fun () -> scoresCVal.Set scores)
    })
```

## Counters and diagnostics

Frame counters, cost timers, "how many entities" stats: keep them as plain mutable fields on your world, and write to them from `update` (or the frame function). Resist the urge to increment a global from inside a derived value's computation — derived values are supposed to be pure, and a hidden write in one is invisible to everything else.
