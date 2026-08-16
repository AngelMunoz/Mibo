---
title: Background Work
category: Adaptive
categoryindex: 3
index: 9
---

# Background Work

Some work is too heavy for `update`: pathfinding across a big map, generating a chunk of terrain, parsing a level file. Run it inside the frame and you drop frames. The adaptive toolkit has two answers: move it off the game thread, or slice it across frames — and a rule for choosing.

## Moving work off the game thread

`ctx.Intents.postTask` runs your work on the thread pool; when it completes, your `ofSuccess` callback runs on the game thread where state writes are legal:

```fsharp
let requestPath (world: World) (ctx: AdaptiveContext) (from: Vector2) (target: Vector2) =
    // Capture plain values — the task must not touch cval/cmap
    let obstacles = world.Bees |> AMap.force

    ctx.Intents.postTask(
        (fun () -> pathfinder.FindAsync(obstacles, from, target)),
        (fun path -> world.Path.Set path),   // back on the game thread
        (fun ex -> eprintfn $"pathfinding failed: {ex.Message}"))
```

The contract that makes this safe:

* **In**: plain values. Capture what the task needs before starting — forced copies (`force`) when the data would otherwise be borrowed.
* **Out**: plain values. The task returns its result; it never writes a container directly. The write happens in `ofSuccess`.
* **Progress**: posts to a `cval`, same as the result:

```fsharp
do ctx.Intents.post(fun () -> world.GenerationProgress.Set 0.5f)
```

While the task runs, the game keeps updating and drawing — the loop doesn't wait.

## Cancellation

A long task should take a `CancellationToken` and check it in its loop; the code that *requested* the work owns triggering cancellation (a new request supersedes the old one, the player left the screen, the game is exiting):

```fsharp
type World = {
    ...
    PathRequest: cval<PathRequest voption>   // carries the token source
}
```

Keep it cooperative and simple: check the token between chunks, abandon cleanly, and ignore stale results on completion (compare a request id before applying).

## Slicing work across frames

Going off-thread is not always worth it. If the work is a few milliseconds and would spend more time marshaling data than computing, slice it instead: do a fixed budget per frame and keep the rest in a queue:

```fsharp
let update (world: World) (ctx: AdaptiveContext) (gameTime: GameTime) =
    // Millisecond budget for this frame's slice
    let budget = TimeSpan.FromMilliseconds 2.0

    let sw = System.Diagnostics.Stopwatch.StartNew()

    // world.ParseQueue is a plain ResizeArray — tick work, not graph work
    while world.ParseQueue.Count > 0 && sw.Elapsed < budget do
        parseOne world.ParseQueue

    // Anything left? The next update picks the queue up — no wiring needed,
    // the loop just runs again next frame.
```

The queue lives as plain mutable state on your world — this is tick work, not graph work. A progress `cval` beside it drives a loading bar the same way as the threaded version.

## Which one, when

* **Off-thread** when the work is genuinely slow (tens of ms or more) or blocks on IO — files, network, heavy computation.
* **Sliced** when the work is a few ms, allocation-shy, and easy to checkpoint.
* **Neither** when you haven't measured. Most "heavy" loops are fine in `update` — profile before adding machinery.

For the queue calls themselves (`post`, `postNextFrame`, `postTask`, `postAsync`) and their timing guarantees, see [Intents](intents.html).
