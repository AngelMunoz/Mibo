module PingPong.Server.GameLogic

open Mibo.Elmish
open PingPong.Shared.Types
open PingPong.Shared.Physics

// ── Server Messages ────────────────────────────────────────────────────────

type ServerMsg =
  | FromClient of int<peerId> * ClientMsg
  | GameTick

// ── Elmish Update ──────────────────────────────────────────────────────────

let private rng = System.Random()

let init ctx : struct (_ * Cmd<_>) =
  struct (initGameState 800f 800f, Cmd.none)

let update msg model : struct (_ * Cmd<_>) =
  match msg with
  | GameTick -> step rng model (1f / 60f), Cmd.none

  | FromClient(_, clientMsg) ->
    match clientMsg with
    | MovePaddle(side, y) ->
      let clampedY = clampPaddle model.Height y

      match side with
      | Left ->
        {
          model with
              LeftPaddle = { model.LeftPaddle with Y = clampedY }
        },
        Cmd.none
      | Right ->
        {
          model with
              RightPaddle = { model.RightPaddle with Y = clampedY }
        },
        Cmd.none
