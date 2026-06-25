namespace Mibo.Elmish.Graphics3D

open Mibo.Elmish

/// <summary>
/// Interface for a pluggable 3D rendering pipeline.
/// Owns how to turn a sorted buffer into pixels: pass order, shadow maps, lighting math, post-process.
/// </summary>
/// <remarks>
/// The pipeline is the consumer of geometry, not the definer. It receives a buffer of
/// <see cref="T:Mibo.Elmish.Graphics3D.Command3D"/> and interprets them.
///
/// The built-in forward pipeline (B5–B10) is the reference implementation; users may swap
/// it for a deferred, SDF, or visibility-buffer pipeline without changing their view functions.
/// </remarks>
type IRenderPipeline3D =

  /// <summary>
  /// Executes all commands in the buffer, turning them into pixels.
  /// The pipeline dispatches each <see cref="T:Mibo.Elmish.Graphics3D.Command3D"/> directly,
  /// handling pass order, shader binding, and render target management.
  /// </summary>
  /// <param name="gameCtx">The current game context (window dimensions, services).</param>
  /// <param name="gameTime">The frame's time (total + elapsed) — surfaced to shaders as the <c>time</c> uniform for animation.</param>
  /// <param name="buffer">The accumulated render commands for this frame.</param>
  /// <param name="rtPool">Pooled render textures for intermediate targets (shadow maps, post-process ping-pong).</param>
  abstract Execute:
    gameCtx: GameContext *
    gameTime: GameTime *
    buffer: RenderBuffer3D *
    rtPool: IRenderTargetPool3D ->
      unit

  /// <summary>
  /// Called once when the renderer is created. Use for shader loading,
  /// mesh generation, and other one-time initialization.
  /// </summary>
  abstract Initialize: unit -> unit

  /// <summary>
  /// Called once when the renderer is disposed. Use for shader unloading
  /// and resource cleanup.
  /// </summary>
  abstract Shutdown: unit -> unit
