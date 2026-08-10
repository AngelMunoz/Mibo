namespace Defli.Raylib

open System.Numerics
open Mibo
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Defli
open Defli.World

// ─────────────────────────────────────────────────────────────
// DiagnosticsView — the F3 overlay lines (Kimo's
// DiagnosticsViewExtensions), restored from the original Defli.
// The FrameDiag object is shell-owned; the WorldDiag rides the frame.
// ─────────────────────────────────────────────────────────────

[<AutoOpen>]
module DiagnosticsViewExtensions =

  type RenderBuffer2D with

    /// Draws the frame-diagnostics line (the caller gates visibility via
    /// FrameDiag.Visible). Anchored at the given position.
    member inline buffer.frameDiagnostics
      (font: Font, diag: FrameDiag, at: Vector2)
      : RenderBuffer2D =
      let yellow = Mibo.Color.rgb 255uy 210uy 0uy

      buffer.text(
        font,
        diag.Display,
        at,
        18f,
        tint = yellow,
        layer = Layers.Hud
      )

    /// Draws the world-diagnostics line (gated like frameDiagnostics).
    member inline buffer.worldDiagnostics
      (font: Font, diag: WorldDiag, at: Vector2)
      : RenderBuffer2D =
      let yellow = Mibo.Color.rgb 255uy 210uy 0uy

      buffer.text(
        font,
        diag.Display,
        at,
        18f,
        tint = yellow,
        layer = Layers.Hud
      )
