module PingPong.Client.Program

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open PingPong.Shared.Types
open PingPong.Shared.Serialization
open PingPong.Client.NetworkService
open PingPong.Client.View

// ── Model ──────────────────────────────────────────────────────────────────

type Model = {
  GameState: GameState
  Connected: bool
  PeerId: int<peerId>
}

// ── Messages ───────────────────────────────────────────────────────────────

type Msg =
  | ServerState of byte[]
  | ConnectionChanged of ConnectionState
  | LocalInput of float32
  | Tick of GameTime

// ── Env ────────────────────────────────────────────────────────────────────

type Env = { Network: INetworkService }

// ── Elmish Logic ───────────────────────────────────────────────────────────

let init ctx =
  struct ({
            GameState = initGameState 800f 800f
            Connected = false
            PeerId = 0<peerId>
          },
          Cmd.none)

let update env msg model =
  match msg with
  | ConnectionChanged state ->
    match state with
    | Connected peerId ->
      struct ({
                model with
                    Connected = true
                    PeerId = peerId
              },
              Cmd.none)
    | _ -> struct ({ model with Connected = false }, Cmd.none)

  | ServerState bytes ->
    let serverState = deserializeGameState bytes
    struct ({ model with GameState = serverState }, Cmd.none)

  | LocalInput mouseY ->
    if model.Connected then
      let side = if model.PeerId = 1<peerId> then Left else Right
      let bytes = serializeClientMsg(MovePaddle(side, mouseY))

      struct (model,
              Cmd.ofEffect(
                Effect<Msg>(fun _ ->
                  env.Network.Send(model.PeerId, bytes))
              ))
    else
      struct (model, Cmd.none)

  | Tick _ -> struct (model, Cmd.none)

// ── Subscriptions ──────────────────────────────────────────────────────────

let subscribe (net: INetworkService) ctx model =
  let networkSub =
    Sub.Active(
      SubId.ofString "network",
      fun dispatch ->
        net.MessageReceived.Subscribe(fun (_, bytes) ->
          dispatch(ServerState bytes))
    )

  let stateSub =
    Sub.Active(
      SubId.ofString "network/state",
      fun dispatch ->
        // If already connected by the time subscription starts, dispatch now
        match net.State with
        | Connected _ as state -> dispatch(ConnectionChanged state)
        | _ -> ()
        net.StateChanged.Subscribe(fun state ->
          dispatch(ConnectionChanged state))
    )

  let inputSub =
    Mibo.Input.Mouse.onMove (fun pos -> LocalInput pos.Y) ctx

  Sub.batch [ networkSub; stateSub; inputSub ]

// ── Program ────────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _args =
  let net = new WebSocketClient() :> INetworkService

  let env = { Network = net }

  let program =
    Program.mkProgram init (update env)
    |> Program.withSubscription(subscribe net)
    |> Program.withTick Tick
    |> Program.withInput
    |> Program.withConfig(fun cfg -> {
      cfg with
          Title = "Ping Pong - Client"
          Width = 800
          Height = 800
          TargetFPS = 60
    })
    |> Program.withRenderer(fun () ->
      Renderer2D.create(fun c m b -> view c m.GameState b))

  // Connect to server
  net.Connect("ws://localhost:5000")

  // Run game
  let game = new RaylibGame<Model, Msg>(program)
  game.Run()

  net.Disconnect()
  0
