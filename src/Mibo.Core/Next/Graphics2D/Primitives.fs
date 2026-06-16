namespace Mibo.Elmish.Next.Graphics2D.Base

open System.Numerics

// ─────────────────────────────────────────────────────────────────
// Neutral Color (sub-namespace to avoid shadowing Raylib_cs.Color)
// ─────────────────────────────────────────────────────────────────

[<Struct>]
type Color = { R: byte; G: byte; B: byte; A: byte }

// ─────────────────────────────────────────────────────────────────
// Neutral BlendMode (sub-namespace to avoid shadowing Raylib_cs.BlendMode)
// ─────────────────────────────────────────────────────────────────

type BlendMode =
  | Alpha = 0
  | Additive = 1
  | Multiplied = 2
  | AddColors = 3
  | SubtractColors = 4
  | AlphaPremultiply = 5
  | Custom = 6
  | CustomSeparate = 7

// ─────────────────────────────────────────────────────────────────
// Everything else stays in Mibo.Elmish.Next.Graphics2D
// ─────────────────────────────────────────────────────────────────

namespace Mibo.Elmish.Next.Graphics2D

open System.Numerics
open Mibo.Elmish.Next.Graphics2D.Base

[<Measure>]
type Texture

[<Measure>]
type Font

[<Measure>]
type Shader

[<Measure>]
type RenderTarget

[<Measure>]
type Mesh

[<Measure>]
type ModelAsset

[<Measure>]
type LightContext

[<Struct>]
type Rect = {
  X: float32
  Y: float32
  Width: float32
  Height: float32
}

module Rect =
  let inline create(x: float32, y: float32, w: float32, h: float32) : Rect = {
    X = x
    Y = y
    Width = w
    Height = h
  }

  let Zero: Rect = {
    X = 0.0f
    Y = 0.0f
    Width = 0.0f
    Height = 0.0f
  }

[<Struct>]
type Camera2DState = {
  Offset: Vector2
  Target: Vector2
  Rotation: float32
  Zoom: float32
}

[<Struct>]
type Camera2DConfig = {
  Camera: Camera2DState
  Viewport: Rect voption
  ClearColor: Color voption
}

[<Struct>]
type Camera = {
  View: Matrix4x4
  Projection: Matrix4x4
}

[<Struct>]
type Camera3DConfig = {
  Camera: Camera
  Viewport: Rect voption
  ClearColor: Color voption
  PostProcessPasses: int[] voption
}

[<Measure>]
type RenderLayer
