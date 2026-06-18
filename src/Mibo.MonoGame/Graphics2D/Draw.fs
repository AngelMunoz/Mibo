namespace Mibo.Elmish.Graphics2D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

/// <summary>
/// Pipe-friendly drawing DSL. Each function takes a <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>
/// as its last argument, adds the corresponding command, and returns the buffer for chaining.
/// </summary>
/// <remarks>
/// <para>
/// Commands are built via <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/> and added to the buffer.
/// Partial application of styling parameters (layer, color) is supported — bind
/// them once and reuse across multiple draw calls.
/// </para>
/// <para>
/// Usage:
/// <code lang="fsharp">
/// buffer
/// |> Draw.beginCamera 0&lt;RenderLayer&gt; worldCamera
/// |> Draw.fillRect (10&lt;RenderLayer&gt;, Color.Red) groundRect
/// |> Draw.fillCircle (10&lt;RenderLayer&gt;, Color.Blue) (center, radius)
/// |> Draw.endCamera 1000&lt;RenderLayer&gt;
/// |> Draw.drop
/// </code>
/// </para>
/// </remarks>
module Draw =

  // ──────────────────────────────────────────────
  // Sprite & Text
  // ──────────────────────────────────────────────

  /// <summary>Draws a sprite from a pre-configured SpriteState.</summary>
  let inline sprite (state: SpriteState) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.sprite state)
    buffer

  /// <summary>Draws text from a pre-configured TextState.</summary>
  let inline text (state: TextState) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.text state)
    buffer

  // ──────────────────────────────────────────────
  // Rectangles
  // ──────────────────────────────────────────────

  /// <summary>Filled rectangle. (layer, color) can be partially applied.</summary>
  let inline fillRect
    (layer: int<RenderLayer>, color: Color)
    (rect: Rectangle)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.fillRect (layer, color) rect)
    buffer

  // ──────────────────────────────────────────────
  // Circles
  // ──────────────────────────────────────────────

  /// <summary>Filled circle. (layer, color) can be partially applied.</summary>
  let inline fillCircle
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.fillCircle (layer, color) (center, radius))
    buffer

  // ──────────────────────────────────────────────
  // Camera
  // ──────────────────────────────────────────────

  /// <summary>Begins a 2D camera transform. (layer) can be partially applied.</summary>
  let inline beginCamera
    (layer: int<RenderLayer>)
    (camera: Camera2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.beginCamera layer camera)
    buffer

  /// <summary>Ends the current 2D camera transform.</summary>
  let inline endCamera (layer: int<RenderLayer>) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.endCamera layer)
    buffer

  // ──────────────────────────────────────────────
  // Escape Hatches
  // ──────────────────────────────────────────────

  /// <summary>
  /// Flushes the SpriteBatch, exits camera, runs the action, then restores state.
  /// (layer) can be partially applied.
  /// </summary>
  let inline drawImmediate
    (layer: int<RenderLayer>)
    (action: unit -> unit)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.drawImmediate layer action)
    buffer

  /// <summary>Clears the current framebuffer to the given color.</summary>
  let inline clear
    (layer: int<RenderLayer>)
    (color: Color)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(Command2D.clear layer color)
    buffer

  /// <summary>
  /// Terminal function that discards the buffer, silencing the unused-value warning.
  /// Does nothing.
  /// </summary>
  let inline drop(_buffer: RenderBuffer2D) = ()
