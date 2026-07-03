namespace Mibo.Elmish.Graphics2D

open Raylib_cs
open Mibo.Elmish

/// <summary>
/// Context handed to a <c>Command2D.PostProcess</c> action each frame. The renderer
/// has already rendered the scene to <c>Source</c> and entered the destination render
/// target (a pooled ping-pong RT, or the back-buffer for the last pass). The action
/// resolves its own shader, sets model-derived params, and draws a fullscreen quad of
/// <c>Source</c>.
/// </summary>
[<Struct>]
type PostProcessContext2D = {

  /// <summary>Current ping-pong source (the scene RT on the first pass).</summary>
  Source: RenderTexture2D

  Width: int
  Height: int

  /// <summary>Accumulated frame time in seconds, for animated effects.</summary>
  Time: float32

  /// <summary>
  /// Game services — e.g. <c>tryGetService&lt;IAssets&gt;</c> to resolve a shader lazily
  /// (defers resolution to first frame, where the device/assets exist).
  /// </summary>
  Context: GameContext
}
