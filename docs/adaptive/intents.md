---
title: Intents (deferred work)
category: Adaptive
categoryindex: 3
index: 3
---

# Deferring Work With Intents

Sometimes your update function needs to say "do this, but not right now". Not because the work is slow, but because *now* is the wrong moment: you're in the middle of looping over a collection, or the work belongs to the next frame, or it belongs on another thread entirely.

That is what the intent queue is for. During update you queue work; the framework runs it at the moment the method name says. `init` receives the same queue through its context, so startup setup can defer work too.

```fsharp
let update (world: World) (ctx: AdaptiveContext) (gameTime: GameTime) =
    // ...loop over gems, decide which are collected...

    let awardPoint () =
        world.Score.UpdateTo((world.Score |> AVal.getValue) + 1) |> ignore

    // Not while I'm iterating: run this after update finishes
    ctx.Intents.post awardPoint
```

## The four moments

| Call | Runs |
|---|---|
| `ctx.Intents.post` | right after this step's update, before the frame is forced |
| `ctx.Intents.postNextFrame` | at the top of the next step, before update |
| `ctx.Intents.postTask` | on the thread pool; its completion runs like a `post` |
| `ctx.Intents.postAsync` | same, for `Async<'T>` work |

Work queued with `post` runs **in the order you posted it** and finishes before the frame is forced, so the renderer sees the fully-reacted world, never half of one. `postNextFrame` work lands on the next step; `postTask`/`postAsync` completions land like a `post` once the work is done.

From `init`, the same four calls work, and their moments line up with the `Cmd` the MVU `init` returns. `post` runs at the startup drain, right after `init` returns and before the first frame is forced, so the first frame already includes its effects. `postNextFrame` runs at the first step's boundary, before the first update. `postTask`/`postAsync` start their work at the startup drain and run the completion at a later post drain. The one rule: `init`'s context is also the subscription projection's context, and the projection must not post — it runs once per step, and its work would land a step late.

## When to use which

**`post`** is the workhorse. Two situations cover most uses:

* *Don't mutate while iterating.* You're looping over a collection's snapshot and something in that loop wants to remove from it. Do the reads and the writes-from-reads in the loop; collect the removals; post them.
* *React to one feature with another.* Your update scheduled bees, and the bees reported leavings. The "score one honey per leaver" handler is posted work; [Systems](systems.html) shows the full shape.

**`postNextFrame`** for spacing things out: a spawn that should land after this frame's render, a cooldown that starts counting from the next update.

**`postTask` / `postAsync`** for anything slow: file IO, network calls, expensive computations that don't touch game state. The frame doesn't wait for them:

```fsharp
let fetchLeaderboard () = env.Leaderboard.Fetch()
let scoresFetched (scores: Score list) = world.Scores.Set scores
let fetchFailed (ex: exn) = eprintfn $"leaderboard fetch failed: {ex.Message}"

ctx.Intents.postTask(
    fetchLeaderboard,
    ofSuccess = scoresFetched,   // runs on the game thread
    ofError = fetchFailed)
```

The rule that keeps this safe: work posted with `post`/`postNextFrame`, and the `ofSuccess`/`ofError` callbacks, always run on the game thread, the only thread allowed to touch your `cval`/`cmap` containers. The task body runs on the thread pool: it reads the plain values you captured before starting it, and hands its result back through `ofSuccess`.

## One caution

Everything queued with plain `post` runs **before the frame is forced**. A heavy handler delays this frame's render. Keep posted handlers small; the slow parts belong in `postTask`.

## See also

- [Background Work](background-work.html): when to move work off the thread entirely, and when to slice it across frames instead.
- [Systems](systems.html): features reporting events as data, with the update as the translator that posts the reactions.
