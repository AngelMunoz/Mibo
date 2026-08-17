---
title: Commands (async + effects)
category: MVU
categoryindex: 2
index: 3
---

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
    model, Cmd.ofAsync (saveToDisk model) SaveComplete SaveFailed

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

`async { ... }` is F#'s syntax for asynchronous work: `let!` awaits a result without blocking a thread. Run F# async and map results:

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

`Effect<'Msg>` wraps a function that receives `dispatch`; use it for full control:

```fsharp
let runDelayed (ms: int) (msg: Msg) (dispatch: Msg -> unit) =
    async {
        do! Async.Sleep ms
        dispatch msg
    }
    |> Async.StartImmediate

let delayedMsg (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofEffect (Effect<Msg>(runDelayed ms msg))

// Usage
model, delayedMsg 1000 (DelayedAction "1 second passed")
```

## Combining Commands

Return multiple commands from one update:

```fsharp
let startLevelCmd (level: Level) =
    Cmd.batch [
        Cmd.ofMsg (PlayMusic level.Music)
        Cmd.ofAsync loadLevelData level.id LevelDataLoaded LoadFailed
        Cmd.ofMsg SpawnPlayer
    ]

let update msg model =
  match msg with
  | StartLevel level ->
    { model with Level = level }, startLevelCmd level
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
let logEffect (msg: string) (_dispatch: Msg -> unit) =
    printfn "[LOG] %s" msg

let logMessage (msg: string) =
    Cmd.ofEffect (Effect<Msg>(logEffect msg))

// Usage
model, logMessage "Player jumped"
```

### Sequential Commands

The batch functions run their commands in the same frame; the order between them is not a wait. For true sequencing (B runs after A completes), use async:

```fsharp
let saveThenLoad path =
  Cmd.ofAsync (async {
    do! saveDataAsync()
    let! result = loadDataAsync()
    return result
  }) Loaded Failed
```

To run two independent effects together in one frame, batch them:

```fsharp
let saveAndStartNext (path: string) =
  let saved _ = DataSaved
  Cmd.batch2 (
    Cmd.ofAsync saveData path saved SaveFailed,
    Cmd.ofMsg LoadNextLevel  // starts immediately, not after the save
  )
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

- Commands are structs: minimal allocation
- `Cmd.none` is a singleton: zero allocation
- `batch2`/`batch3`/`batch4` avoid array allocations
- Async commands don't block the game loop

## See Also

- [Elmish runtime](elmish.html): The update loop
- [Subscriptions](subscriptions.html): External event sources
- [Service composition](services.html): Dependency injection patterns
