module PingPong.Client.NetworkService

open System
open System.Net.WebSockets
open System.Threading
open FSharp.Control
open PingPong.Shared.Types
open PingPong.Client.Types

type private ClientCommand =
  | SendData of data: byte[]
  | Shutdown

/// <summary>
/// Creates a WebSocket client hidden behind IClientTransport.
/// Sends are serialized through a MailboxProcessor to avoid blocking
/// the game loop and prevent concurrent SendAsync on the socket.
/// </summary>
/// <param name="onHandshake">
/// Called with the first message received after the WebSocket connects.
/// The return value is stored and accessible via the second tuple element.
/// The transport does not signal <c>Connected</c> until this callback
/// returns, so handshake data is always available before any subscriber
/// observes the connected state.
/// </param>
/// <returns>
/// The transport and a getter for the handshake data.
/// </returns>
let createWebSocketClient
  (onHandshake: byte[] -> 'handshake)
  : IClientTransport * (unit -> 'handshake) =

  let mutable ws: WebSocket option = None
  let mutable currentState: ConnectionState = Disconnected
  let mutable handshakeData: 'handshake = Unchecked.defaultof<'handshake>
  let cts = new CancellationTokenSource()

  let stateChanged = Event<ConnectionState>()
  let messageReceived = Event<byte[]>()

  let sendTo (s: WebSocket) (data: byte[]) = async {
    if s.State = WebSocketState.Open then
      try
        do!
          s.SendAsync(
            ArraySegment data,
            WebSocketMessageType.Binary,
            true,
            cts.Token
          )
          |> Async.AwaitTask
      with
      | :? WebSocketException
      | :? OperationCanceledException
      | :? ObjectDisposedException -> ()
  }

  let agentBody(inbox: MailboxProcessor<ClientCommand>) =
    let rec loop() = async {
      let! cmd = inbox.Receive()

      match cmd with
      | SendData(data) ->
        match ws with
        | Some s -> do! sendTo s data
        | None -> ()

        return! loop()

      | Shutdown -> ()
    }

    loop()

  let agent = MailboxProcessor.Start(agentBody, cancellationToken = cts.Token)

  let receiveLoop(s: WebSocket) = async {
    let buffer = Array.zeroCreate<byte> 4096

    try
      try
        while s.State = WebSocketState.Open && not cts.IsCancellationRequested do
          let! result =
            s.ReceiveAsync(ArraySegment(buffer), cts.Token) |> Async.AwaitTask

          if result.MessageType = WebSocketMessageType.Binary then
            messageReceived.Trigger(buffer.[0 .. result.Count - 1])
      with ex ->
        Console.WriteLine $"receiveLoop error: {ex}"
    finally
      currentState <- Disconnected
      stateChanged.Trigger Disconnected
  }

  let connect(address: string) =
    async {
      currentState <- Connecting
      stateChanged.Trigger Connecting

      let client = new ClientWebSocket()

      try
        do! client.ConnectAsync(Uri(address), cts.Token) |> Async.AwaitTask
        ws <- Some client

        // Consume the handshake (first message) BEFORE signaling Connected.
        // This ensures handshake data is available before any subscriber
        // observes the connected state, eliminating the race between
        // early message delivery and subscription setup.
        let handshakeBuffer = Array.zeroCreate<byte> 4096

        let! result =
          client.ReceiveAsync(ArraySegment(handshakeBuffer), cts.Token)
          |> Async.AwaitTask

        handshakeData <- onHandshake(handshakeBuffer.[0 .. result.Count - 1])

        currentState <- Connected
        stateChanged.Trigger Connected
        Async.Start(receiveLoop client, cts.Token)
      with ex ->
        Console.WriteLine $"connect error: {ex}"
        ws <- None

        try
          client.Dispose()
        with _ ->
          ()

        currentState <- Disconnected
        stateChanged.Trigger Disconnected
    }
    |> Async.Start

  let disconnect() =
    match ws with
    | Some s when s.State = WebSocketState.Open ->
      try
        s.CloseOutputAsync(
          WebSocketCloseStatus.NormalClosure,
          "",
          CancellationToken.None
        )
        |> fun t -> t.Wait()
      with
      | :? WebSocketException
      | :? OperationCanceledException
      | :? ObjectDisposedException -> ()
    | _ -> ()

    cts.Cancel()
    agent.Post Shutdown

    ws
    |> Option.iter(fun s ->
      try
        s.Dispose()
      with
      | :? WebSocketException
      | :? OperationCanceledException
      | :? ObjectDisposedException -> ())

    ws <- None
    currentState <- Disconnected
    stateChanged.Trigger Disconnected

  let transport =
    { new IClientTransport with
        member _.State = currentState
        member _.StateChanged = stateChanged.Publish :> IObservable<_>
        member _.MessageReceived = upcast messageReceived.Publish
        member _.Send(data) = agent.Post(SendData data)
        member _.Connect(address) = connect address
        member _.Disconnect() = disconnect()
    }

  (transport, fun () -> handshakeData)


module Network =
  open Mibo.Elmish
  open PingPong.Shared.Serialization

  let send (transport: IClientTransport) msg =
    Cmd.ofEffect(
      Effect<_>(fun _ ->
        let bytes = serializeClientMsg msg
        transport.Send(bytes))
    )

  let subscribeState (transport: IClientTransport) msg dispatch =
    match transport.State with
    | Connected as state -> dispatch(msg state)
    | _ -> ()

    transport.StateChanged.Subscribe(fun state -> state |> msg |> dispatch)
