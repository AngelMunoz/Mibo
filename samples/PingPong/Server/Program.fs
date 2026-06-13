module PingPong.Server.Program

open System
open System.Collections.Concurrent
open System.Threading
open IcedTasks
open FSharp.Control
open Mibo.Elmish
open PingPong.Shared.Types
open PingPong.Shared.Serialization
open PingPong.Server.NetworkService
open PingPong.Server.GameLogic

// ── Server Main Loop ───────────────────────────────────────────────────────

[<EntryPoint>]
let main _args =
  let port = 5000

  use runner =
    new HeadlessRunner<_, _>(
      HeadlessProgram.mkHeadless init update
      |> HeadlessProgram.withFixedStep {
        StepSeconds = 1f / 60f
        MaxStepsPerFrame = 4
        MaxFrameSeconds = ValueSome 0.25f
        Map = fun _ -> GameTick
      }
    )

  let paddles = ConcurrentDictionary<int<peerId>, PaddleSide voption>()
  let paddleLock = obj()

  let assignPaddle() =
    let hasLeft = paddles.Values |> Seq.exists((=)(ValueSome Left))
    let hasRight = paddles.Values |> Seq.exists((=)(ValueSome Right))

    if not hasLeft then ValueSome Left
    elif not hasRight then ValueSome Right
    else ValueNone

  let server: IServerTransport =
    createWebSocketServer
      port
      (fun peerId ->
        let side =
          lock paddleLock (fun () ->
            let s = assignPaddle()
            paddles[peerId] <- s
            s)

        let sideByte =
          match side with
          | ValueSome Left -> 0uy
          | ValueSome Right -> 1uy
          | ValueNone -> 2uy

        let peerBytes = BitConverter.GetBytes(int peerId)
        let data = Array.zeroCreate<byte> 5
        Array.Copy(peerBytes, data, 4)
        data[4] <- sideByte
        printfn "Peer %d connected, assigned %A" (int peerId) side
        ValueSome data)
      (fun peerId ->
        lock paddleLock (fun () ->
          let mutable _side = ValueNone
          paddles.TryRemove(peerId, &_side) |> ignore)

        printfn "Peer %d disconnected" (int peerId))

  server.MessageReceived.Add(fun (peerId, bytes) ->
    let msg = deserializeClientMsg bytes
    runner.Dispatch(FromClient(peerId, msg)))

  printfn "Server listening on port %d" port
  printfn "Press Ctrl+C to stop"

  use cts = new CancellationTokenSource()

  Console.CancelKeyPress.Add(fun args ->
    args.Cancel <- true
    cts.Cancel())

  try
    asyncEx {
      for _, gameState in
        runner.RunAsync(TimeSpan.FromMilliseconds 16., cts.Token) do
        serializeGameState gameState |> server.Broadcast
    }
    |> Async.RunSynchronously
  with :? OperationCanceledException ->
    ()

  server.Disconnect()
  0
