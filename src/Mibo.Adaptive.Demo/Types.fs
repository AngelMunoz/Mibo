module Mibo.Adaptive.Demo.Types

open System.Numerics

// ── Domain types ─────────────────────────────────────────────────────────────

[<Struct>]
type PaddleSide =
  | Left
  | Right

[<Struct>]
type Ball = { Position: Vector2; Velocity: Vector2 }

[<Struct>]
type Scores = { Left: int; Right: int }

/// Signed per-frame paddle movement, -1..1. Written by the host (keyboard or AI).
[<Struct>]
type InputState = {
  LeftMove: float32
  RightMove: float32
}

[<Struct>]
type Rect = {
  X: float32
  Y: float32
  Width: float32
  Height: float32
}

// ── Court constants ──────────────────────────────────────────────────────────

let courtWidth = 800f
let courtHeight = 800f
let paddleWidth = 10f
let paddleHeight = 80f
let ballRadius = 8f
let paddleSpeed = 420f
