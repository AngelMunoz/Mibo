namespace Defli.Raylib

open System.Numerics
open Defli
open Defli.World

// ─────────────────────────────────────────────────────────────
// Shell — the windowed frontend's per-frame state: input intents
// accumulated between the host's input poll and the sim's Update
// phase, plus the frame-diagnostics object (shell-owned; the
// WorldDiag lives on the world). Holds no gameplay state.
// ─────────────────────────────────────────────────────────────

[<Sealed>]
type Shell() =
  /// Accumulated keyboard pan direction (pressed adds, released
  /// subtracts — the ActionState.Held tracking of the original shell).
  member val PanDir = Vector2.Zero with get, set
  /// Middle button held → drag pans the camera.
  member val MiddleDown = false with get, set
  /// Last mouse position in screen pixels.
  member val MousePos = Vector2.Zero with get, set
  /// Main-loop frame diagnostics (F3 overlay).
  member val Diag = FrameDiag() with get, set

/// The world cell — the composition root reads the CURRENT world
/// through this holder, so a restart can swap it and the runner's
/// re-run Init picks up the fresh world (fresh graph, fresh
/// subscriptions).
[<Sealed>]
type WorldCell(value: World) =
  member val Value = value with get, set
