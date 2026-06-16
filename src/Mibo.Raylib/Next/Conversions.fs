namespace Mibo.Elmish.Next

open System
open System.Numerics
open System.Runtime.CompilerServices
open Raylib_cs
open Mibo.Elmish.Next.Graphics2D
open Mibo.Elmish.Next.Graphics2D.Base

// ─────────────────────────────────────────────────────────────────
// Neutral ↔ Raylib type conversions
//
// Color and Rect are byte/value-isomorphic with their raylib
// counterparts.  The Unsafe.As path is zero-cost (same size, same
// layout); the explicit field path is provided as a safe fallback
// and is what the JIT will inline for 4-byte structs anyway.
// ─────────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
module Convert =

  // ── Color ─────────────────────────────────────────────────────

  let inline toColor(c: Raylib_cs.Color) : Color = {
    R = c.R
    G = c.G
    B = c.B
    A = c.A
  }

  let inline toRaylibColor(c: Color) : Raylib_cs.Color =
    Raylib_cs.Color(c.R, c.G, c.B, c.A)

  // ── Rect / Rectangle ──────────────────────────────────────────

  let inline toRect(r: Raylib_cs.Rectangle) : Rect = {
    X = r.X
    Y = r.Y
    Width = r.Width
    Height = r.Height
  }

  let inline toRaylibRect(r: Rect) : Raylib_cs.Rectangle =
    Raylib_cs.Rectangle(r.X, r.Y, r.Width, r.Height)

  // ── Camera2D ──────────────────────────────────────────────────

  let inline toCamera2DState(c: Raylib_cs.Camera2D) : Camera2DState = {
    Offset = c.Offset
    Target = c.Target
    Rotation = c.Rotation
    Zoom = c.Zoom
  }

  let inline toRaylibCamera2D(c: Camera2DState) : Raylib_cs.Camera2D =
    Raylib_cs.Camera2D(c.Offset, c.Target, c.Rotation, c.Zoom)

  // ── Camera3D → Camera (View/Projection matrices) ─────────────

  let toCamera (aspectRatio: float32) (c: Raylib_cs.Camera3D) : Camera =
    let view = Matrix4x4.CreateLookAt(c.Position, c.Target, c.Up)

    let proj =
      match c.Projection with
      | CameraProjection.Perspective ->
        Matrix4x4.CreatePerspectiveFieldOfView(
          c.FovY * (MathF.PI / 180.0f),
          aspectRatio,
          0.01f,
          1000.0f
        )
      | _ ->
        let halfH = c.FovY
        let halfW = halfH * aspectRatio
        Matrix4x4.CreateOrthographic(halfW * 2.0f, halfH * 2.0f, 0.01f, 1000.0f)

    { View = view; Projection = proj }

  // ── BlendMode ─────────────────────────────────────────────────

  let inline toBlendMode(m: Raylib_cs.BlendMode) : BlendMode =
    LanguagePrimitives.EnumOfValue(int m)

  let inline toRaylibBlendMode(m: BlendMode) : Raylib_cs.BlendMode =
    LanguagePrimitives.EnumOfValue(int m)
