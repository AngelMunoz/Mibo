module PingPong.Server.NetworkService

open System
open System.Net
open System.Net.WebSockets
open System.Collections.Concurrent
open System.Threading
open FSharp.Control
open PingPong.Shared.Types
open FSharp.UMX

// ── Server Transport Interface (server-only) ───────────────────────────────
// Multiplexer for many connected peers. Peer lifecycle is handled via
// factory callbacks, not the interface.

type IServerTransport =
  abstract MessageReceived: IObservable<int<peerId> * byte[]>
  abstract Send: peer: int<peerId> * data: byte[] -> unit
  abstract Broadcast: data: byte[] -> unit
  abstract Disconnect: unit -> unit

type private Command =
  | SendOne of peer: int<peerId> * data: byte[]
  | Broadcast of data: byte[]
  | Shutdown

/// <summary>
/// Creates a WebSocket server hidden behind IServerTransport.
/// All sends are serialized through a MailboxProcessor — WebSocket permits
/// at most one outstanding SendAsync per instance.
/// </summary>
/// <param name="port">TCP port to listen on.</param>
/// <param name="onPeerConnected">
/// Called when a peer connects. Return optional handshake bytes to send back.
/// </param>
/// <param name="onPeerDisconnected">
/// Called on both clean and abnormal disconnect. The peer has already been
/// removed from the connection table when this fires.
/// </param>
let createWebSocketServer
  (port: int)
  (onPeerConnected: int<peerId> -> byte[] voption)
  (onPeerDisconnected: int<peerId> -> unit)
  : IServerTransport =

  let connections = ConcurrentDictionary<int<peerId>, WebSocket>()
  let mutable nextPeerId = 0
  let cts = new CancellationTokenSource()

  let messageReceived = Event<int<peerId> * byte[]>()

  let sendTo (ws: WebSocket) (data: byte[]) = async {
    if ws.State = WebSocketState.Open then
      try
        do!
          ws.SendAsync(
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

  let agentBody(inbox: MailboxProcessor<Command>) =
    let rec loop() = async {
      let! cmd = inbox.Receive()

      match cmd with
      | SendOne(peer, data) ->
        match connections.TryGetValue peer with
        | true, ws -> do! sendTo ws data
        | false, _ -> ()

        return! loop()

      | Broadcast data ->
        for KeyValue(_, ws) in connections do
          do! sendTo ws data

        return! loop()

      | Shutdown -> ()
    }

    loop()

  let agent = MailboxProcessor.Start(agentBody, cancellationToken = cts.Token)

  let receiveLoop (peer: int<peerId>) (ws: WebSocket) = async {
    let buffer = Array.zeroCreate<byte> 4096

    try
      try
        while ws.State = WebSocketState.Open && not cts.IsCancellationRequested do
          let! result =
            ws.ReceiveAsync(ArraySegment(buffer), cts.Token) |> Async.AwaitTask

          if result.MessageType = WebSocketMessageType.Binary then
            messageReceived.Trigger(peer, buffer.[0 .. result.Count - 1])
      with ex ->
        Console.WriteLine $"receiveLoop error for peer {peer}: {ex}"
    finally
      let mutable _ws = Unchecked.defaultof<WebSocket>
      connections.TryRemove(peer, &_ws) |> ignore
      onPeerDisconnected peer
  }

  let acceptWebSocket(ctx: HttpListenerContext) = async {
    try
      let! socket = ctx.AcceptWebSocketAsync(null) |> Async.AwaitTask
      let ws = socket.WebSocket
      let peer = Interlocked.Increment(&nextPeerId) |> UMX.tag<peerId>

      // Send handshake BEFORE adding to connections so the Broadcast loop
      // can't race ahead of the handshake with a GameState JSON blob.
      match onPeerConnected peer with
      | ValueSome handshake -> do! sendTo ws handshake
      | ValueNone -> ()

      connections[peer] <- ws
      Async.Start(receiveLoop peer ws, cts.Token)
    with ex ->
      Console.WriteLine $"acceptWebSocket error: {ex}"
  }

  let acceptConnections(listener: HttpListener) = async {
    while not cts.IsCancellationRequested do
      let! ctx = listener.GetContextAsync() |> Async.AwaitTask

      if ctx.Request.IsWebSocketRequest then
        Async.Start(acceptWebSocket ctx, cts.Token)
  }

  let listener = new HttpListener()
  listener.Prefixes.Add(sprintf "http://localhost:%d/" port)
  listener.Start()
  Async.Start(acceptConnections listener, cts.Token)

  let stop() =
    cts.Cancel()
    agent.Post Shutdown
    listener.Stop()

    for KeyValue(_, ws) in connections do
      try
        if ws.State = WebSocketState.Open then
          ws.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "",
            CancellationToken.None
          )
          |> fun t -> t.Wait()

        ws.Dispose()
      with
      | :? WebSocketException
      | :? OperationCanceledException
      | :? ObjectDisposedException -> ()

  { new IServerTransport with
      member _.MessageReceived = upcast messageReceived.Publish
      member _.Send(peer, data) = agent.Post(SendOne(peer, data))
      member _.Broadcast(data) = agent.Post(Broadcast data)
      member _.Disconnect() = stop()
  }
