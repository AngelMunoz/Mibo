namespace Mibo.Elmish.Graphics2D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

/// <summary>Unit of measure for 2D render layer ordering.</summary>
[<Measure>]
type RenderLayer

/// <summary>State required to render a 2D sprite via SpriteBatch.Draw.</summary>
[<Struct>]
type SpriteState = {
  /// <summary>The texture to draw.</summary>
  Texture: Texture2D

  /// <summary>Destination rectangle on screen (in pixels).</summary>
  Dest: Rectangle

  /// <summary>Source rectangle within the texture (in texels).</summary>
  Source: Rectangle

  /// <summary>Origin point for rotation and positioning (relative to Dest).</summary>
  Origin: Vector2

  /// <summary>Rotation in radians around Origin.</summary>
  Rotation: float32

  /// <summary>Tint color (multiplied with texture).</summary>
  Color: Color

  /// <summary>Render layer for ordering.</summary>
  Layer: int<RenderLayer>
}

/// <summary>State required to render 2D text via SpriteBatch.DrawString.</summary>
[<Struct>]
type TextState = {
  /// <summary>The sprite font to use.</summary>
  Font: SpriteFont

  /// <summary>The text string to draw.</summary>
  Text: string

  /// <summary>Top-left position on screen (in pixels).</summary>
  Position: Vector2

  /// <summary>Uniform scale factor applied to the font (1.0 = default size).</summary>
  Scale: float32

  /// <summary>Tint color.</summary>
  Color: Color

  /// <summary>Render layer for ordering.</summary>
  Layer: int<RenderLayer>
}

/// <summary>
/// Closed set of 2D render commands. Stored in <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>
/// and dispatched via pattern matching — no interface boxing.
/// </summary>
/// <remarks>
/// Each case carries a <c>layer: int&lt;RenderLayer&gt;</c> for stable layer sorting.
/// This is the MonoGame backend's equivalent of the raylib <c>Command2D</c> DU,
/// using MonoGame types (<c>Texture2D</c>, <c>SpriteFont</c>, <c>Rectangle</c>, etc.).
/// </remarks>
[<RequireQualifiedAccess; Struct>]
type Command2D =
  // Sprite & Text
  | Sprite of
    spriteTexture: Texture2D *
    spriteDest: Rectangle *
    spriteSource: Rectangle *
    spriteOrigin: Vector2 *
    spriteRotation: float32 *
    spriteColor: Color *
    layer: int<RenderLayer>
  | Text of
    textFont: SpriteFont *
    textValue: string *
    textPosition: Vector2 *
    textScale: float32 *
    textColor: Color *
    layer: int<RenderLayer>
  // Rectangles
  | FillRect of fillRect: Rectangle * fillColor: Color * layer: int<RenderLayer>
  // Circles
  | FillCircle of
    circleCenter: Vector2 *
    circleRadius: float32 *
    circleColor: Color *
    layer: int<RenderLayer>
  // Camera
  | BeginCamera of beginCameraCam: Camera2D * layer: int<RenderLayer>
  | EndCamera of layer: int<RenderLayer>
  // Escape Hatches
  | DrawImmediate of action: (unit -> unit) * layer: int<RenderLayer>
  | Clear of clearColor: Color * layer: int<RenderLayer>

/// <summary>
/// Factory functions that create <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/> values.
/// </summary>
/// <remarks>
/// Each function is <c>inline</c> and curried for partial application of styling
/// parameters (layer, color) before the geometry argument.
/// </remarks>
module Command2D =

  // Sprite & Text

  /// <summary>Creates a sprite command from a pre-configured SpriteState.</summary>
  let inline sprite(state: SpriteState) =
    Command2D.Sprite(
      state.Texture,
      state.Dest,
      state.Source,
      state.Origin,
      state.Rotation,
      state.Color,
      state.Layer
    )

  /// <summary>Creates a text command from a pre-configured TextState.</summary>
  let inline text(state: TextState) =
    Command2D.Text(
      state.Font,
      state.Text,
      state.Position,
      state.Scale,
      state.Color,
      state.Layer
    )

  // Rectangles

  /// <summary>Filled rectangle. (layer, color) can be partially applied.</summary>
  let inline fillRect
    (layer: int<RenderLayer>, color: Color)
    (rect: Rectangle)
    =
    Command2D.FillRect(rect, color, layer)

  // Circles

  /// <summary>Filled circle. (layer, color) can be partially applied.</summary>
  let inline fillCircle
    (layer: int<RenderLayer>, color: Color)
    (center: Vector2, radius: float32)
    =
    Command2D.FillCircle(center, radius, color, layer)

  // Camera

  /// <summary>Begins a 2D camera transform. (layer) can be partially applied.</summary>
  let inline beginCamera (layer: int<RenderLayer>) (camera: Camera2D) =
    Command2D.BeginCamera(camera, layer)

  /// <summary>Ends the current 2D camera transform.</summary>
  let inline endCamera(layer: int<RenderLayer>) = Command2D.EndCamera(layer)

  // Escape Hatches

  /// <summary>
  /// Flushes the SpriteBatch, exits camera, runs the action, then restores state.
  /// (layer) can be partially applied.
  /// </summary>
  let inline drawImmediate (layer: int<RenderLayer>) (action: unit -> unit) =
    Command2D.DrawImmediate(action, layer)

  /// <summary>Clears the current framebuffer to the given color.</summary>
  let inline clear (layer: int<RenderLayer>) (color: Color) =
    Command2D.Clear(color, layer)

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.SpriteState"/>.</summary>
module SpriteState =

  /// <summary>
  /// Creates a sprite state with required fields.
  /// Defaults: Origin=Zero, Rotation=0, Color=White, Layer=0.
  /// </summary>
  let create
    (texture: Texture2D, dest: Rectangle, source: Rectangle)
    : SpriteState =
    {
      Texture = texture
      Dest = dest
      Source = source
      Origin = Vector2.Zero
      Rotation = 0.0f
      Color = Color.White
      Layer = 0<RenderLayer>
    }

  /// <summary>Sets the origin point for rotation/positioning.</summary>
  let inline withOrigin (v: Vector2) (s: SpriteState) = { s with Origin = v }

  /// <summary>Sets the rotation in radians.</summary>
  let inline withRotation (v: float32) (s: SpriteState) = {
    s with
        Rotation = v
  }

  /// <summary>Sets the tint color.</summary>
  let inline withColor (v: Color) (s: SpriteState) = { s with Color = v }

  /// <summary>Sets the render layer.</summary>
  let inline withLayer (v: int<RenderLayer>) (s: SpriteState) = {
    s with
        Layer = v
  }

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.TextState"/>.</summary>
module TextState =

  /// <summary>
  /// Creates a text state with required fields.
  /// Defaults: Scale=1.0, Color=White, Layer=0.
  /// </summary>
  let create(font: SpriteFont, text: string, position: Vector2) : TextState = {
    Font = font
    Text = text
    Position = position
    Scale = 1.0f
    Color = Color.White
    Layer = 0<RenderLayer>
  }

  /// <summary>Sets the uniform scale factor (1.0 = default font size).</summary>
  let inline withScale (v: float32) (s: TextState) = { s with Scale = v }

  /// <summary>Sets the tint color.</summary>
  let inline withColor (v: Color) (s: TextState) = { s with Color = v }

  /// <summary>Sets the render layer.</summary>
  let inline withLayer (v: int<RenderLayer>) (s: TextState) = {
    s with
        Layer = v
  }
