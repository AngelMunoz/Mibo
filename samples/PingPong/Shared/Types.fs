module PingPong.Shared.Types

open System.Numerics
open System

// ── Peer Identity ──────────────────────────────────────────────────────────

[<Measure>]
type peerId

// ── Connection State ───────────────────────────────────────────────────────

[<Struct>]
type ConnectionState =
  | Disconnected
  | Connecting
  | Connected of int<peerId>
  | Reconnecting

// ── Network Service Interface ──────────────────────────────────────────────

type INetworkService =
  abstract State: ConnectionState
  abstract StateChanged: IObservable<ConnectionState>
  abstract MessageReceived: IObservable<int<peerId> * byte[]>
  abstract Send: peer: int<peerId> * data: byte[] -> unit
  abstract Broadcast: data: byte[] -> unit
  abstract Connect: address: string -> unit
  abstract Disconnect: unit -> unit

// ── Game Types ─────────────────────────────────────────────────────────────

[<Struct>]
type PaddleSide =
  | Left
  | Right

type Ball = { Position: Vector2; Velocity: Vector2 }

type Paddle = { Side: PaddleSide; Y: float32 }

type Scores = { Left: int; Right: int }

type GameState = {
  Ball: Ball
  LeftPaddle: Paddle
  RightPaddle: Paddle
  Scores: Scores
  Width: float32
  Height: float32
}

// ── Messages ───────────────────────────────────────────────────────────────

type ClientMsg = MovePaddle of side: PaddleSide * y: float32

// ── Initial State ──────────────────────────────────────────────────────────

let initGameState (width: float32) (height: float32) = {
  Ball = {
    Position = Vector2(width / 2f, height / 2f)
    Velocity = Vector2(200f, 100f)
  }
  LeftPaddle = { Side = Left; Y = height / 2f }
  RightPaddle = { Side = Right; Y = height / 2f }
  Scores = { Left = 0; Right = 0 }
  Width = width
  Height = height
}
