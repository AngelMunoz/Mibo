---
title: Commands (async + effects)
index: 8
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# Commands

Commands handle side effects in Mibo's Elmish architecture. While `update` should be pure, commands let you execute impure work (I/O, timers, random) and dispatch messages back into the loop.

## Quick Start

```fsharp
open Mibo.Elmish

type Msg =
  | SaveGame
  | SaveComplete
  | SaveFailed of exn

let update msg model =
  match msg with
  | SaveGame ->
    // Return unchanged model + command to save
    model, Cmd.ofAsync (saveToDisk model) SaveGameComplete SaveFailed

  | SaveComplete ->
    printfn "Game saved!"
    model, Cmd.none
```

## Command Basics

Commands are values returned from `init` and `update` alongside the new model:

```fsharp
let update msg model : struct(Model * Cmd<Msg>) =
  match msg with
  | Tick ->
    { model with Time = model.Time + 1 }, Cmd.none
  | Fire ->
    model, Cmd.ofMsg SpawnProjectile
```

`Cmd.none` means "no side effects this frame."

## Creating Commands

### Immediate Messages

Dispatch another message right away:

```fsharp
Cmd.ofMsg SpawnProjectile
```

### Async Workflows

Run F# async and map results:

```fsharp
let loadData url = async {
  use client = new HttpClient()
  let! json = client.GetStringAsync(url) |> Async.AwaitTask
  return parseJson json
}

// In update:
model, Cmd.ofAsync (loadData "api/data") DataLoaded LoadFailed
```

The async runs on a background thread. When it completes, the result message is dispatched back into the game loop.

### .NET Tasks

For existing Task-based APIs:

```fsharp
let task = File.ReadAllTextAsync("save.json")
model, Cmd.ofTask task LoadComplete LoadFailed
```

### Custom Effects

For full control over dispatch:

```fsharp
let delayedMsg (ms: int) (msg: Msg) : Cmd<Msg> =
  Cmd.ofEffect (Effect<Msg>(fun dispatch ->
    async {
      do! Async.Sleep ms
      dispatch msg
    } |> Async.StartImmediate
  ))

// Usage
model, delayedMsg 1000 (DelayedAction "1 second passed")
```

## Combining Commands

Return multiple commands from one update:

```fsharp
let update msg model =
  match msg with
  | StartLevel level ->
    let newModel = { model with Level = level }
    let cmd = Cmd.batch [
      Cmd.ofMsg (PlayMusic level.Music)
      Cmd.ofAsync loadLevelData level.id LevelDataLoaded LoadFailed
      Cmd.ofMsg SpawnPlayer
    ]
    newModel, cmd
```

| Function | Use Case |
|----------|----------|
| `Cmd.batch [cmd1; cmd2; ...]` | Variable list of commands |
| `Cmd.batch2 (a, b)` | Exactly 2 commands (optimized) |
| `Cmd.batch3 (a, b, c)` | Exactly 3 commands (optimized) |
| `Cmd.batch4 (a, b, c, d)` | Exactly 4 commands (optimized) |

## Deferred Commands

Sometimes you need to break infinite loops or schedule work for the next frame:

```fsharp
let update msg model =
  match msg with
  | CheckCondition ->
    if stillNeedToCheck then
      // Check again next frame, not immediately
      model, Cmd.deferNextFrame (Cmd.ofMsg CheckCondition)
    else
      model, Cmd.none
```

`deferNextFrame` prevents stack overflow when messages trigger themselves.

## Parent-Child Composition

Child components often have their own message types. Map them to parent messages:

```fsharp
module Child =
  type Msg = Jump | Move
  let update msg model = ...

// Parent update:
let update msg model =
  match msg with
  | ChildMsg childMsg ->
    let (childModel, childCmd) = Child.update childMsg model.Child
    let parentModel = { model with Child = childModel }
    // Map child's Cmd<Child.Msg> to Cmd<Parent.Msg>
    parentModel, Cmd.map ChildMsg childCmd
```

## Common Patterns

### Fire-and-Forget

For effects where you don't care about the result:

```fsharp
let log msg =
  Cmd.ofEffect (Effect<_>(fun _ ->
    printfn "[LOG] %s" msg
  ))

// Usage
model, log "Player jumped"
```

### Sequential Commands

Chain dependent operations:

```fsharp
let saveThenLoad path =
  Cmd.batch2 (
    Cmd.ofAsync saveData path (fun _ -> DataSaved) SaveFailed,
    Cmd.ofMsg LoadNextLevel  // Runs immediately, not waiting for save
  )
```

For true sequencing (B runs after A completes), use async:

```fsharp
let sequential = Cmd.ofAsync (async {
  do! saveDataAsync()
  let! result = loadDataAsync()
  return result
}) Loaded Failed
```

### Conditional Commands

```fsharp
let maybeSave model =
  if model.Dirty then
    Cmd.ofAsync autoSave model SaveComplete SaveFailed
  else
    Cmd.none

// In update:
model, maybeSave model
```

## Performance Notes

- Commands are structs - minimal allocation
- `Cmd.none` is a singleton - zero allocation
- `batch2`/`batch3`/`batch4` avoid array allocations
- Async commands don't block the game loop

## See Also

- [Elmish runtime](../mvu/elmish.html) - The update loop
- [Subscriptions](subscriptions.html) - External event sources
- [Service composition](../mvu/services.html) - Dependency injection patterns
