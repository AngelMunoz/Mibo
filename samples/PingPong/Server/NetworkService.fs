module PingPong.Server.NetworkService

open System
open System.Net
open System.Net.WebSockets
open System.Collections.Generic
open System.Threading
open PingPong.Shared.Types
open FSharp.UMX

// ── WebSocket Server Implementation ────────────────────────────────────────

type WebSocketServer(port: int) as this =
  let connections = Dictionary<int<peerId>, WebSocket>()
  let mutable nextPeerId = 1<peerId>
  let cts = new CancellationTokenSource()

  let stateChanged = Event<ConnectionState>()
  let messageReceived = Event<int<peerId> * byte[]>()

  let receiveLoop (peer: int<peerId>) (ws: WebSocket) = async {
    let buffer = Array.zeroCreate<byte> 4096

    try
      while ws.State = WebSocketState.Open && not cts.IsCancellationRequested do
        let! result =
          ws.ReceiveAsync(ArraySegment(buffer), cts.Token) |> Async.AwaitTask

        if result.MessageType = WebSocketMessageType.Binary then
          let data = buffer.[0 .. result.Count - 1]
          messageReceived.Trigger(peer, data)
    with _ ->
      connections.Remove(peer) |> ignore
      stateChanged.Trigger Disconnected
  }

  let acceptWebSocket(ctx: HttpListenerContext) = async {
    let! socket = ctx.AcceptWebSocketAsync(null) |> Async.AwaitTask
    let ws = socket.WebSocket
    let peer = nextPeerId
    nextPeerId <- nextPeerId + UMX.tag 1
    connections.Add(peer, ws)
    // Send the assigned peer ID back to the client as a single int
    let peerBytes = System.BitConverter.GetBytes(int peer)
    do! ws.SendAsync(
          System.ArraySegment(peerBytes),
          WebSocketMessageType.Binary,
          true,
          cts.Token
        ) |> Async.AwaitTask |> Async.Ignore
    stateChanged.Trigger(Connected peer)
    Async.Start(receiveLoop peer ws, cts.Token)
  }

  let acceptConnections(listener: HttpListener) = async {
    while not cts.IsCancellationRequested do
      let! ctx = listener.GetContextAsync() |> Async.AwaitTask

      if ctx.Request.IsWebSocketRequest then
        do! acceptWebSocket ctx
  }

  let listener = new HttpListener()

  member _.Start() =
    listener.Prefixes.Add(sprintf "http://localhost:%d/" port)
    listener.Start()
    Async.Start(acceptConnections listener, cts.Token)

  member _.Stop() =
    cts.Cancel()
    listener.Stop()

    for KeyValue(_, ws) in connections do
      try
        ws.CloseAsync(
          WebSocketCloseStatus.NormalClosure,
          "",
          CancellationToken.None
        )
        |> fun t -> t.Wait()

        ws.Dispose()
      with _ ->
        ()

  interface INetworkService with
    member _.State = Connected 1<peerId>
    member _.StateChanged = stateChanged.Publish :> IObservable<_>
    member _.MessageReceived = messageReceived.Publish :> IObservable<_>

    member _.Send(peer, data) =
      match connections.TryGetValue(peer) with
      | true, ws when ws.State = WebSocketState.Open ->
        try
          ws.SendAsync(
            ArraySegment(data),
            WebSocketMessageType.Binary,
            true,
            cts.Token
          )
          |> fun t -> t.Wait()
        with _ ->
          ()
      | _ -> ()

    member _.Broadcast(data) =
      for KeyValue(_, ws) in connections do
        if ws.State = WebSocketState.Open then
          try
            ws.SendAsync(
              ArraySegment(data),
              WebSocketMessageType.Binary,
              true,
              cts.Token
            )
            |> fun t -> t.Wait()
          with _ ->
            ()

    member _.Connect(_) = ()
    member _.Disconnect() = this.Stop()
