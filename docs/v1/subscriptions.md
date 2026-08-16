---
title: Subscriptions (external events)
index: 9
---

> **⚠ Archived v1 docs (raylib-only).** These are the original docs for the raylib-only release. The current multi-backend docs (Mibo.Core + Mibo.Raylib + Mibo.MonoGame) live at the [site root](../index.html).


# Subscriptions

Subscriptions connect external event sources to your Elmish update loop. Unlike commands (one-time effects), subscriptions run continuously, dispatching messages whenever events occur.

## Quick Start

```fsharp
open Mibo.Elmish

// Define subscription IDs
type SubId = 
  static member Keyboard = SubId.ofString "keyboard"
  static member Network = SubId.ofString "network"

// Create a subscription
let keyboardSub : Sub<Msg> =
  SubId.Keyboard,
  fun dispatch ->
    // Listen to events, dispatch messages
    let handler = EventHandler<KeyboardEvent>(fun _ e ->
      dispatch (KeyPressed e.Key)
    )
    Keyboard.addListener handler
    
    // Return disposable to clean up
    { new IDisposable with
      member _.Dispose() = Keyboard.removeListener handler
    }

// In your program
Program.mkProgram init update
|> Program.withSubscription (fun _model -> keyboardSub)
```

## How Subscriptions Work

The Elmish runtime diffs subscriptions by `SubId` each frame:

- **New ID?** Start the subscription
- **Same ID?** Keep it running
- **ID gone?** Dispose and stop

This gives you precise control over subscription lifetimes based on your model state.

## Creating Subscriptions

### Basic Subscription

```fsharp
let timerSub (interval: TimeSpan) : Sub<Msg> =
  let id = SubId.ofString "timer"
  
  id,
  fun dispatch ->
    let timer = new Timer(interval)
    timer.Elapsed.Add(fun _ -> dispatch Tick)
    timer.Start()
    
    { new IDisposable with
      member _.Dispose() = timer.Dispose()
    }
```

### Conditional Subscriptions

Start/stop based on model state:

```fsharp
let subscribe model =
  if model.IsConnected then
    Sub.batch2 (
      heartbeatSub,
      messageListenerSub
    )
  else
    Sub.none
```

### Multiple Subscriptions

| Function | Use Case |
|----------|----------|
| `Sub.batch [sub1; sub2]` | Variable list |
| `Sub.batch2 (a, b)` | Exactly 2 (optimized) |
| `Sub.batch3 (a, b, c)` | Exactly 3 (optimized) |
| `Sub.batch4 (a, b, c, d)` | Exactly 4 (optimized) |

## Subscription IDs

IDs must be unique per subscription. Use namespacing for parent-child composition:

```fsharp
module Player =
  let inputSub : Sub<Player.Msg> =
    SubId.ofString "input",
    fun dispatch -> ...

// Parent prefixes child IDs:
let parentSub =
  Player.inputSub |> Sub.map "player" PlayerMsg
// Resulting ID: "player/input"
```

## Parent-Child Composition

Child modules often need their own subscriptions:

```fsharp
module Chat =
  type Msg = NewMessage of string | ConnectionLost
  
  let subscribe (model: Chat.Model) : Sub<Chat.Msg> =
    if model.IsOpen then
      SubId.ofString "chat/socket",
      fun dispatch ->
        let ws = new WebSocket("ws://server/chat")
        ws.OnMessage.Add(fun e -> dispatch (NewMessage e.Data))
        ws
    else
      Sub.none

// Parent wires it up:
type Parent.Msg = ChatMsg of Chat.Msg

let subscribe model =
  model.Chat
  |> Chat.subscribe
  |> Sub.map "chat" ChatMsg  // Prefix: "chat/chat/socket"
```

## Common Patterns

### Input Handling

For subscription-based input, use the built-in modules:

```fsharp
open Mibo.Input

let inputSub : Sub<Msg> =
  Sub.batch [
    Keyboard.onPressed (fun k -> KeyPressed k) ctx
    Mouse.onLeftClick (fun pos -> MouseClicked pos) ctx
    Gamepad.listen (fun g -> GamepadInput g) ctx
  ]
```

See [Input](input.html) for `InputMapper.subscribe` for semantic action mapping.

### Network Events

```fsharp
let networkSub (client: NetworkClient) : Sub<Msg> =
  SubId.ofString "network",
  fun dispatch ->
    let handler = client.OnPacket.Subscribe(fun packet ->
      dispatch (PacketReceived packet)
    )
    handler
```

### Time-based

```fsharp
// Every second, dispatch a tick
let fpsSub : Sub<Msg> =
  SubId.ofString "fps",
  fun dispatch ->
    let rec loop () = async {
      do! Async.Sleep 1000
      dispatch CalculateFps
      return! loop()
    }
    let cts = new CancellationTokenSource()
    Async.Start(loop(), cts.Token)
    
    { new IDisposable with
      member _.Dispose() = cts.Cancel()
    }
```

## Lifecycle Management

The runtime automatically manages subscription lifecycles:

```fsharp
// Frame 1: Model says we need network
let subscribe model =
  if model.Online then networkSub else Sub.none
// Runtime: Starts networkSub

// Frame 2: Model goes offline
// Runtime: Disposes networkSub (ID disappeared)

// Frame 3: Model back online
// Runtime: Starts fresh networkSub
```

Clean up resources in your disposable:

```fsharp
fun dispatch ->
  let resource = acquireResource()
  
  { new IDisposable with
    member _.Dispose() =
      resource.Close()
      resource.Dispose()
  }
```

## Performance Notes

- SubIds are strings - keep them stable (don't generate random IDs)
- The diff is O(N) on subscription count - don't create hundreds
- Disposables should be lightweight - move heavy cleanup to commands

## See Also

- [Input](input.html) - Input handling
- [Commands](commands.html) - One-time side effects
- [Elmish runtime](../mvu/elmish.html) - How the loop works
