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
  /// Camera-POV depth (NDC z in [0,1], written to an R32F target). Populated only when the view
  /// emitted at least one <c>Command3D.EnableDepthPrePass</c> this frame; otherwise
  /// <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueNone"/>. Linearize with the camera's
  /// near/far planes to get view-space distance (fog, depth-of-field, SSAO).
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
