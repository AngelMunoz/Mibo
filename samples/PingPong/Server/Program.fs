module PingPong.Server.Program

open System
open Mibo.Elmish
open PingPong.Shared.Types
open PingPong.Shared.Serialization
open PingPong.Server.NetworkService
open PingPong.Server.GameLogic

// ── Server Main Loop ───────────────────────────────────────────────────────

[<EntryPoint>]
let main _args =
    let port = 5000

    let program =
        HeadlessProgram.mkHeadless init update
        |> HeadlessProgram.withFixedStep {
            StepSeconds = 1f / 60f
            MaxStepsPerFrame = 4
            MaxFrameSeconds = ValueSome 0.25f
            Map = fun _ -> GameTick
        }

    use runner = new HeadlessRunner<_,_>(program)
    let server = new WebSocketServer(port)
    server.Start()
    let net = server :> INetworkService

    // Subscribe to incoming messages
    use _sub = net.MessageReceived.Subscribe(fun (peerId, bytes) ->
        let msg = deserializeClientMsg bytes
        runner.Dispatch(FromClient(peerId, msg))
    )

    printfn "Server listening on port %d" port
    printfn "Press Ctrl+C to stop"

    // Main loop — pace to real time
    let frameMs = 16.0
    let sw = System.Diagnostics.Stopwatch.StartNew()
    let mutable nextTick = 0.0

    while not runner.ShouldQuit do
        let elapsed = sw.Elapsed.TotalMilliseconds

        if elapsed >= nextTick then
          runner.Step(TimeSpan.FromMilliseconds(frameMs))
          nextTick <- nextTick + frameMs

          let bytes = serializeGameState runner.Model
          net.Broadcast(bytes)
        else
          System.Threading.Thread.Sleep(1)

    net.Disconnect()
    0
