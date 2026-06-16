module PingPong.Client.Program

open System
open Mibo.Elmish
open Mibo.Elmish.Next.Graphics2D
open PingPong.Shared.Types
open PingPong.Shared.Physics
open PingPong.Shared.Serialization
open PingPong.Client.Types
open PingPong.Client.NetworkService
open PingPong.Client.View
open FSharp.UMX

// ── Env ────────────────────────────────────────────────────────────────────

type Env = { Network: IClientTransport }

// ── Elmish Logic ───────────────────────────────────────────────────────────

let init ctx : struct (Model * Cmd<_>) =
  {
    LocalState = initGameState 800f 800f
    ServerState = ValueNone
    AssignedPaddle = ValueNone
    Connected = false
    PeerId = 0<peerId>
  },
  Cmd.none

let update env msg model : struct (Model * Cmd<_>) =
  match msg with
  | ConnectionChanged(state, peerId, side) ->
    match state with
    | Connected ->
      {
        model with
            Connected = true
            PeerId = peerId
            AssignedPaddle = side
      },
      Cmd.none
    | _ -> { model with Connected = false }, Cmd.none

  | ServerState serverState ->
    let local = model.LocalState

    let syncedBall = serverState.Ball

    let leftPaddle =
      match model.AssignedPaddle with
      | ValueSome Left -> local.LeftPaddle
      | _ ->
          {
            local.LeftPaddle with
                Y = lerp local.LeftPaddle.Y serverState.LeftPaddle.Y 0.3f
          }

    let rightPaddle =
      match model.AssignedPaddle with
      | ValueSome Right -> local.RightPaddle
      | _ ->
          {
            local.RightPaddle with
                Y = lerp local.RightPaddle.Y serverState.RightPaddle.Y 0.3f
          }

    let synced = {
      local with
          Ball = syncedBall
          LeftPaddle = leftPaddle
          RightPaddle = rightPaddle
          Scores = serverState.Scores
    }

    {
      model with
          LocalState = synced
          ServerState = ValueSome serverState
    },
    Cmd.none

  | LocalInput mouseY ->
    match model.AssignedPaddle with
    | ValueSome side ->
      let clampedY = clampPaddle model.LocalState.Height mouseY

      let localState =
        match side with
        | Left -> {
            model.LocalState with
                LeftPaddle = {
                  model.LocalState.LeftPaddle with
                      Y = clampedY
                }
          }
        | Right ->
            {
              model.LocalState with
                  RightPaddle = {
                    model.LocalState.RightPaddle with
                        Y = clampedY
                  }
            }

      { model with LocalState = localState },
      Network.send env.Network (MovePaddle(side, mouseY))
    | ValueNone -> model, Cmd.none

// ── Subscriptions ──────────────────────────────────────────────────────────

let subscribe
  (transport: IClientTransport)
  (getHandshake: unit -> struct (int<peerId> * PaddleSide voption))
  ctx
  model
  =
  let stateSub =
    Sub.Active(
      SubId.ofString "network/state",
      Network.subscribeState transport (fun state ->
        match state with
        | Connected ->
          let struct (peerId, side) = getHandshake()
          ConnectionChanged(Connected, peerId, side)
        | other -> ConnectionChanged(other, 0<peerId>, ValueNone))
    )

  let msgSub =
    Sub.Active(
      SubId.ofString "network/messages",
      fun dispatch ->
        transport.MessageReceived.Subscribe(fun bytes ->
          deserializeGameState bytes |> ServerState |> dispatch)
    )

  let inputSub = Mibo.Input.Mouse.onMove (fun pos -> LocalInput pos.Y) ctx

  Sub.batch [ stateSub; msgSub; inputSub ]

// ── Program ────────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _args =
  let transport, getHandshake =
    createWebSocketClient(fun bytes ->
      let peerId = BitConverter.ToInt32(bytes, 0) |> UMX.tag<peerId>

      let side =
        match bytes[4] with
        | 0uy -> ValueSome Left
        | 1uy -> ValueSome Right
        | _ -> ValueNone

      struct (peerId, side))

  let env = { Network = transport }

  let program =
    Program.mkProgram init (update env)
    |> Program.withSubscription(subscribe transport getHandshake)
    |> Program.withInput
    |> Program.withConfig(fun cfg -> {
      cfg with
          Title = "Ping Pong - Client"
          Width = 800
          Height = 800
          TargetFPS = 60
    })
    |> Program.withRenderer(fun () ->
      Renderer2D.create(fun c m b -> view c m.LocalState b))

  transport.Connect("ws://localhost:5000")

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()

  transport.Disconnect()
  0
