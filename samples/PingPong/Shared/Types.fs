module PingPong.Shared.Types

open System.Numerics
open System

// ── Peer Identity ──────────────────────────────────────────────────────────

[<Measure>]
type peerId

// ── Game Types ─────────────────────────────────────────────────────────────

[<Struct>]
type PaddleSide =
  | Left
  | Right

// ── Physics Constants ──────────────────────────────────────────────────────

let paddleWidth = 10f
let paddleHeight = 80f
let ballRadius = 8f

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
