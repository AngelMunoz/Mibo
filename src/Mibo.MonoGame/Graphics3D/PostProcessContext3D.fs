namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

/// <summary>
/// Context handed to a <see cref="T:Mibo.Elmish.Graphics3D.Command3D.PostProcess"/> action each frame.
/// The pipeline has already rendered the scene to <c>Source</c> and set the destination render
/// target (a pooled ping-pong RT, or the back-buffer for the last pass). The action binds its own
/// effect, sets model-derived parameters, binds <c>Source</c> to the effect's sampler, and calls
/// <c>Quad.Draw(effect)</c>.
/// </summary>
[<Struct>]
type PostProcessContext3D = {

  /// <summary>Current ping-pong source (the scene RT on the first pass).</summary>
  Source: RenderTarget2D

  /// <summary>
  /// Camera-POV linear depth (R32F). <c>ValueNone</c> unless depth was opted in at pipeline
  /// construction. Sample it for distance effects (fog, SSAO).
  /// </summary>
  Depth: RenderTarget2D voption

  Width: int
  Height: int

  /// <summary>Accumulated frame time in seconds, for animated effects.</summary>
  Time: float32

  /// <summary>The graphics device — needed to apply an <c>Effect</c> and bind textures.</summary>
  Device: GraphicsDevice

  /// <summary>Fullscreen quad primitive; call <c>Draw(effect)</c> once the effect is applied.</summary>
  Quad: FullScreenQuad

  /// <summary>
  /// Game services — e.g. <c>tryGetService&lt;IAssets&gt;</c> to resolve a compiled effect lazily
  /// (defers resolution to first frame, where the device/assets exist).
  /// </summary>
  Context: GameContext
}
