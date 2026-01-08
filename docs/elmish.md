---
title: The Elmish Architecture
category: Architecture
index: 1
---

# The Elmish Architecture in Games

Mibo uses the Elmish (MVU) pattern to provide a clean, predictable way to manage game state and side effects.

## The Model

The **Model** represents the entire state of your game at a single point in time. It's usually a record containing everything from player positions to scores and active effects.

```fsharp
type Model = {
    PlayerPos: Vector3
    Score: int
}
```

## The Message

A **Message** is a simple type (usually a discriminated union) that describes something that happened in your game.

```fsharp
type Msg =
    | MoveRequested of direction: Vector3
    | CoinCollected of value: int
    | Tick of dt: float32
```

## The Update

The **Update** function is the heart of your game. It takes a message and the current model, and returns a new model and a **Command** (for side effects).

```fsharp
let update msg model =
    match msg with
    | MoveRequested dir ->
        { model with PlayerPos = model.PlayerPos + dir }, Cmd.none
    | Tick dt ->
        // handle logic
        model, Cmd.none
```

## The View

In Mibo, the **View** doesn't return a visual tree like in web apps. Instead, it receives a `RenderBuffer` and submits drawing commands to it.

```fsharp
let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
    Draw2D.sprite texture model.PlayerPos
    |> Draw2D.submit buffer
```

## Why MVU for Games?

1. **Time Travel Debugging**: Since state is centralized, you can record and replay sessions perfectly.
2. **Easy Testing**: Logic is isolated in the pure `update` function, which is trivial to unit test.
3. **Stability**: No more "spooky action at a distance" caused by unexpected mutations.
