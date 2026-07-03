namespace Mibo.Elmish.Graphics2D

open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

/// <summary>
/// Context handed to a <see cref="T:Mibo.Elmish.Graphics2D.Command2D.PostProcess"/> action each frame.
/// The renderer has already rendered the scene to <c>Source</c> and set the destination render
/// target (a pooled ping-pong RT, or the back-buffer for the last pass). The action binds its own
/// effect, sets model-derived parameters, binds <c>Source</c> to the effect's sampler, and calls
/// <c>Quad.Draw(effect)</c>.
/// </summary>
[<Struct>]
type PostProcessContext2D = {

  /// <summary>Current ping-pong source (the scene RT on the first pass).</summary>
  Source: RenderTarget2D

  Width: int
  Height: int

  /// <summary>Accumulated frame time in seconds, for animated effects.</summary>
  Time: float32

  /// <summary>The graphics device — needed to apply an <c>Effect</c> and bind textures.</summary>
  Device: GraphicsDevice

  /// <summary>Fullscreen quad primitive; call <c>Draw(effect)</c> once the effect is applied.</summary>
  Quad: Mibo.Elmish.Graphics3D.FullScreenQuad

  /// <summary>
  /// Game services — e.g. <c>tryGetService&lt;IAssets&gt;</c> to resolve a compiled effect lazily
  /// (defers resolution to first frame, where the device/assets exist).
  /// </summary>
  Context: GameContext
}
