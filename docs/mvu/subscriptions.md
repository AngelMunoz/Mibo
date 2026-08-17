---
title: Subscriptions (external events)
category: MVU
categoryindex: 2
index: 4
---

# Subscriptions

Subscriptions connect external event sources to your Elmish update loop. Unlike commands (one-time effects), subscriptions run continuously, dispatching messages whenever events occur.

## Quick Start

The built-in input modules cover the common case. Your `subscribe` function receives the game context and the current model, and returns the subscriptions that should be running right now:

```fsharp
open Mibo.Elmish
open Mibo.Input

let keyPressed (k: KeyCode) = KeyPressed k
let mouseClicked (pos: Vector2) = MouseClicked pos

let subscribe (ctx: GameContext) (model: Model) : Sub<Msg> =
    Sub.batch [
        Keyboard.onPressed keyPressed ctx
        Mouse.onLeftClick mouseClicked ctx
    ]

let program =
    Program.mkProgram init update
    |> Program.withSubscription subscribe
```

See [Input](../input.html) for `InputMapper.subscribe` and semantic action mapping.

## How Subscriptions Work

The Elmish runtime diffs subscriptions by `SubId` each frame:

- **New ID?** Start the subscription
- **Same ID?** Keep it running
- **ID gone?** Dispose and stop

This gives you precise control over subscription lifetimes based on your model state.

## Creating Subscriptions

When no built-in module fits, a subscription is an id plus a function that starts listening and returns a stop handle (an `IDisposable`). That pair is the `Active` case of the `Sub<'Msg>` struct union:

```fsharp
// in Mibo.Elmish
type Subscribe<'Msg> = Dispatch<'Msg> -> IDisposable  // Dispatch<'Msg> = 'Msg -> unit

type Sub<'Msg> =
    | NoSub
    | Active of SubId * Subscribe<'Msg>
    | BatchSub of Sub<'Msg>[]
```

Wrap any event source you own: the start function hooks dispatch up to the source, and the disposable unhooks it. This example re-implements what `Keyboard.onPressed` does for you, reading the raw keyboard delta observable off the input service:

```fsharp
let onDelta (dispatch: Msg -> unit) (delta: KeyboardDelta) =
    for k in delta.Pressed do
        dispatch (KeyPressed k)

let startKeyboard (ctx: GameContext) (dispatch: Msg -> unit) : IDisposable =
    (Input.getService ctx).KeyboardDelta.Subscribe(onDelta dispatch)

let keyboardSub (ctx: GameContext) : Sub<Msg> =
    Sub.Active(SubId.ofString "keyboard", startKeyboard ctx)
```

### Timer Subscription

```fsharp
let onElapsed (dispatch: Msg -> unit) _ = dispatch Tick

let startTimer (interval: TimeSpan) (dispatch: Msg -> unit) : IDisposable =
    let timer = new Timer(interval)
    timer.Elapsed.Add(onElapsed dispatch)
    timer.Start()
    timer   // Timer is IDisposable: it is its own stop handle

let timerSub (interval: TimeSpan) : Sub<Msg> =
    Sub.Active(SubId.ofString "timer", startTimer interval)
```

### Conditional Subscriptions

Start/stop based on model state:

```fsharp
let subscribe ctx model =
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
        Sub.Active(SubId.ofString "input", startInput)

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

    let onChatMessage (dispatch: Chat.Msg -> unit) (e: MessageEvent) =
        dispatch (NewMessage e.Data)

    let openChatSocket (dispatch: Chat.Msg -> unit) : IDisposable =
        let ws = new WebSocket("ws://server/chat")
        ws.OnMessage.Add(onChatMessage dispatch)
        ws   // the socket is the stop handle: disposing closes it

    let subscribe (model: Chat.Model) : Sub<Chat.Msg> =
        if model.IsOpen then
            Sub.Active(SubId.ofString "chat/socket", openChatSocket)
        else
            Sub.none

// Parent wires it up:
type Parent.Msg = ChatMsg of Chat.Msg

let subscribe ctx model =
    model.Chat
    |> Chat.subscribe
    |> Sub.map "chat" ChatMsg  // Prefix: "chat/chat/socket"
```

## Common Patterns

### Network Events

```fsharp
let onPacket (dispatch: Msg -> unit) (packet: Packet) =
    dispatch (PacketReceived packet)

let startNetwork (client: NetworkClient) (dispatch: Msg -> unit) : IDisposable =
    client.OnPacket.Subscribe(onPacket dispatch)

let networkSub (client: NetworkClient) : Sub<Msg> =
    Sub.Active(SubId.ofString "network", startNetwork client)
```

### Time-based

`async { ... }` is F#'s syntax for asynchronous work: `do!` waits without blocking a thread, and `return!` loops by starting the work again.

```fsharp
let startFpsPolling (dispatch: Msg -> unit) : IDisposable =
    let rec poll () =
        async {
            do! Async.Sleep 1000
            dispatch CalculateFps
            return! poll ()
        }

    let cts = new CancellationTokenSource()
    Async.Start(poll (), cts.Token)

    { new IDisposable with
        member _.Dispose() = cts.Cancel() }

// Every second, dispatch a tick
let fpsSub : Sub<Msg> = Sub.Active(SubId.ofString "fps", startFpsPolling)
```

## Lifecycle Management

The runtime automatically manages subscription lifecycles:

```fsharp
// Frame 1: Model says we need network
let subscribe ctx model =
    if model.Online then networkSub client else Sub.none
// Runtime: Starts networkSub

// Frame 2: Model goes offline
// Runtime: Disposes networkSub (ID disappeared)

// Frame 3: Model back online
// Runtime: Starts fresh networkSub
```

Clean up resources in your disposable:

```fsharp
let startResource (dispatch: Msg -> unit) : IDisposable =
    let resource = acquireResource()

    { new IDisposable with
        member _.Dispose() =
            resource.Close()
            resource.Dispose() }
```

## Performance Notes

- SubIds are strings: keep them stable (don't generate random IDs)
- The diff is O(N) on subscription count: don't create hundreds
- Disposables should be lightweight: move heavy cleanup to commands

## See Also

- [Input](../input.html): Input handling
- [Commands](commands.html): One-time side effects
- [Elmish runtime](elmish.html): How the loop works
