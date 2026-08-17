---
title: Background Work
category: MVU
categoryindex: 2
index: 10
---

# Background Work

## What and Why

Games need to do heavy work — generate worlds, pathfind for dozens of enemies, load assets, save game state. Doing this on the main thread drops frames. Doing it naively on background threads creates race conditions, duplicate requests, and crash-prone synchronization.

The pattern: use Elmish commands to run async work on the thread pool, return results as messages, and process them on the main thread. The framework handles the threading — you write the async work, the framework delivers the result.

## Use Cases

### World generation
Procedural terrain, chunk loading, dungeon generation. Generate chunks around the player as they move, evict distant ones to reclaim memory.

### Pathfinding
A* or flow-field calculations for dozens of AI units. Each unit's path computes on a background thread, result arrives as a message, movement system reads it next frame.

### Asset loading
Load textures, models, and sounds at runtime without freezing. A loading screen shows progress while assets stream in from disk.

### Save/load
Serialize large game states — hundreds of entities, world data, inventory. Write to disk on a background thread, show a "Saving..." indicator.

### Procedural content
Loot table rolls, enemy wave composition, event triggers. Compute results off the main thread, apply them when ready.

## The Technique

Any `Async<'T>` can run as a command:

```fsharp
let heavyWork (input: 'Input) : Cmd<Msg> =
  Cmd.ofAsync
    (async { return computeOnBackgroundThread input })
    (fun result -> WorkComplete result)
    (fun _ex -> WorkFailed input)
```

Track pending work to prevent duplicates:

```fsharp
type Model = {
  PendingWork: HashSet<string>
  Results: ConcurrentDictionary<string, Result>
}
```

Check the tracking set before dispatching:

```fsharp
if not(model.PendingWork.Contains(key)) then
  model.PendingWork.Add(key) |> ignore
  heavyWork input
```

Process the result on the main thread:

```fsharp
| WorkComplete result ->
  model.PendingWork.Remove(result.Key) |> ignore
  model.Results[result.Key] <- result
  struct (model, Cmd.none)
```

### Error handling

The error callback provides a fallback — retry synchronously, return a default, or skip:

```fsharp
Cmd.ofAsync
  (async { return compute input })
  (fun result -> WorkDone result)
  (fun _ex -> WorkDone (fallback input))
```

## Key Insight

The Elmish `Cmd` system is the threading mechanism. You don't create threads, manage locks, or poll for completion. You return a `Cmd`, and the framework runs it on the thread pool, then delivers the result as a message on the main thread. The tracking set prevents duplicate requests — not thread safety, but request deduplication.

## When to use

- Any computation that takes >16ms (will drop a frame).
- I/O operations — file, network, database.
- Anything that doesn't need the GPU.
- You want the Elmish message pattern for async results.

## See also

- [Platformer3D world generation](https://github.com/AngelMunoz/Mibo.Samples/blob/master/Platformer3D/Shared/WorldGen.fs) — background chunk generation in a real game.
- [Composable Systems](composable-systems.html) — how background work fits into the system pipeline.
- [Intents](../adaptive/intents.html) — the adaptive runtime's version of this pattern (`postTask`/`postAsync`).
