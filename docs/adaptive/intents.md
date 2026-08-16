---
title: Intents (deferred work)
category: Adaptive
categoryindex: 3
index: 3
---

# Deferring Work With Intents

Sometimes your update function needs to say "do this — but not right now". Not because the work is slow, but because *now* is the wrong moment: you're in the middle of looping over a collection, or the work belongs to the next frame, or it belongs on another thread entirely.

That is what the intent queue is for. During update you queue work; the framework runs it at the moment the method name says.

```fsharp
let update (world: World) (ctx: AdaptiveContext) (gameTime: GameTime) =
    // ...loop over gems, decide which are collected...

    // Not while I'm iterating — run this after update finishes
    ctx.Intents.post(fun () ->
        world.Score.UpdateTo((world.Score |> AVal.getValue) + 1) |> ignore)
```

## The four moments

| Call | Runs |
|---|---|
| `ctx.Intents.post` | right after this frame's update, before the frame is packed |
| `ctx.Intents.postNextFrame` | at the top of the next frame, before update |
| `ctx.Intents.postTask` | on the thread pool; its completion runs like a `post` |
| `ctx.Intents.postAsync` | same, for `Async<'T>` work |

Queued work runs **in the order you posted it**, and all of it finishes before the frame is packed — so the renderer sees the fully-reacted world, never a half of one.

## When to use which

**`post`** is the workhorse. Two situations cover most uses:

* *Don't mutate while iterating.* You're looping over a collection's snapshot and something in that loop wants to remove from it. Do the reads and the writes-from-reads in the loop; collect the removals; post them.
* *React to one feature with another.* Your update scheduled bees, and the bees reported leavings. The "score one honey per leaver" handler is posted work — [Systems](systems.html) shows the full shape.

**`postNextFrame`** for spacing things out — a spawn that should land after this frame's render, a cooldown that starts counting from the next update.

**`postTask` / `postAsync`** for anything slow: file IO, network calls, expensive computations that don't touch game state. The frame doesn't wait for them:

```fsharp
ctx.Intents.postTask(
    (fun () -> env.Leaderboard.Fetch()),
    (fun scores -> world.Scores.Set scores),   // runs on the game thread
    (fun ex -> eprintfn $"leaderboard fetch failed: {ex.Message}"))
```

The rule that keeps this safe: work posted with `post`/`postNextFrame`, and the `ofSuccess`/`ofError` callbacks, always run on the game thread — the only thread allowed to touch your `cval`/`cmap` containers. The task body runs on the thread pool: it reads the plain values you captured before starting it, and hands its result back through `ofSuccess`.

## One caution

Everything queued with plain `post` runs **before the frame is packed**. A heavy handler delays this frame's render. Keep posted handlers small; the slow parts belong in `postTask`.
