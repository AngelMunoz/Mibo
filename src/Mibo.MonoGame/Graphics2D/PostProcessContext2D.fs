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
  /// The active 2D light context (<c>ValueNone</c> when no lit sprites were drawn this frame).
  /// Read <c>Lights.Value.PointLights</c>, <c>Lights.Value.DirLights</c>, <c>Lights.Value.Ambient</c>
  /// to drive light-aware effects (bloom on lit areas, light-tinted color grading).
  /// </summary>
  Lights: Lighting.LightContext2D voption

  /// <summary>
  /// The last active camera during the scene render (<c>ValueNone</c> when no
  /// <c>BeginCamera</c> block was used). Useful for anchoring post-process effects in world space
  /// (world-aligned fog, distortion). When multiple camera blocks exist in the same frame this is
  /// the <b>last</b> one — the scene RT contains all of them composited, so a single camera
  /// reference can't reconstruct per-camera regions. Commands within a layer are executed in
  /// insertion order (deterministic stable sort), so "last" is well-defined.
  /// </summary>
  Camera: Camera2D voption

  /// <summary>
  /// Game services — e.g. <c>tryGetService&lt;IAssets&gt;</c> to resolve a compiled effect lazily
  /// (defers resolution to first frame, where the device/assets exist).
  /// </summary>
  Context: GameContext
}
