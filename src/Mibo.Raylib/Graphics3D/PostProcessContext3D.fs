namespace Mibo.Elmish.Graphics3D

open Raylib_cs
open Mibo.Elmish

/// <summary>
/// Context handed to a <c>Command3D.PostProcess</c> action each frame. The pipeline
/// has already rendered the scene to <c>Source</c> and entered the destination render
/// target (a pooled ping-pong RT, or the back-buffer for the last pass). The action
/// resolves its own shader, sets model-derived params, and draws a fullscreen quad of
/// <c>Source</c>.
/// </summary>
[<Struct>]
type PostProcessContext3D = {

  /// <summary>Current ping-pong source (the scene RT on the first pass).</summary>
  Source: RenderTexture2D

  /// <summary>
  /// Camera-POV linear depth (R32F). <c>ValueNone</c> unless depth was opted in at
  /// pipeline construction. Sample it for distance effects (fog, SSAO).
  /// </summary>
  Depth: Texture2D voption

  Width: int
  Height: int

  /// <summary>Accumulated frame time in seconds, for animated effects.</summary>
  Time: float32

  /// <summary>
  /// Game services — e.g. <c>tryGetService&lt;IAssets&gt;</c> to resolve a shader lazily
  /// (defers MonoGame-style resolution to first frame, where the device/assets exist).
  /// </summary>
  Context: GameContext
}
