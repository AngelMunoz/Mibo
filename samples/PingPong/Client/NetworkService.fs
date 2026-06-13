module PingPong.Client.NetworkService

open System
open System.Net.WebSockets
open System.Threading
open PingPong.Shared.Types
open FSharp.UMX

// ── WebSocket Client Implementation ────────────────────────────────────────

type WebSocketClient() as this =
    let mutable ws: WebSocket option = None
    let mutable currentState: ConnectionState = Disconnected
    let cts = new CancellationTokenSource()

    let stateChanged = Event<ConnectionState>()
    let messageReceived = Event<int<peerId> * byte[]>()

    let mutable assignedPeerId: int<peerId> = 0<peerId>

    let receiveLoop (ws: WebSocket) = async {
        let buffer = Array.zeroCreate<byte> 4096
        try
            while ws.State = WebSocketState.Open && not cts.IsCancellationRequested do
                let! result = ws.ReceiveAsync(ArraySegment(buffer), cts.Token) |> Async.AwaitTask
                if result.MessageType = WebSocketMessageType.Binary then
                    let data = buffer.[0..result.Count - 1]
                    messageReceived.Trigger(assignedPeerId, data)
        with _ ->
            currentState <- Disconnected
            stateChanged.Trigger(currentState)
    }

    member _.Start(address: string) =
        async {
            let client = new ClientWebSocket()
            do! client.ConnectAsync(Uri(address), cts.Token) |> Async.AwaitTask
            ws <- Some client
            // First message from server is our assigned peer ID (4 bytes)
            let idBuffer = Array.zeroCreate<byte> 4
            let! result = client.ReceiveAsync(System.ArraySegment(idBuffer), cts.Token) |> Async.AwaitTask
            let peerId = System.BitConverter.ToInt32(idBuffer, 0) |> UMX.tag<peerId>
            assignedPeerId <- peerId
            currentState <- Connected peerId
            stateChanged.Trigger(currentState)
            Async.Start(receiveLoop client, cts.Token)
        } |> Async.Start

    member _.Stop() =
        cts.Cancel()
        match ws with
        | Some s when s.State = WebSocketState.Open ->
            try
                s.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                |> fun t -> t.Wait()
                s.Dispose()
            with _ -> ()
        | _ -> ()
        ws <- None
        currentState <- Disconnected
        stateChanged.Trigger(currentState)

    interface INetworkService with
        member _.State = currentState
        member _.StateChanged = stateChanged.Publish :> IObservable<_>
        member _.MessageReceived = messageReceived.Publish :> IObservable<_>

        member _.Send(_, data) =
            match ws with
            | Some s when s.State = WebSocketState.Open ->
                try
                    s.SendAsync(
                        ArraySegment(data),
                        WebSocketMessageType.Binary,
                        true,
                        cts.Token
                    ) |> fun t -> t.Wait()
                with _ -> ()
            | _ -> ()

        member _.Broadcast(data) = (this :> INetworkService).Send(1<peerId>, data)
        member _.Connect(address) = this.Start(address)
        member _.Disconnect() = this.Stop()
