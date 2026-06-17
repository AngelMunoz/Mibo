namespace Mibo.Elmish.Next.Animation

open System.Numerics
open Raylib_cs
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D

/// <summary>
/// Raylib-specific sprite-sheet shims that keep user asset loading unchanged.
/// </summary>
/// <remarks>
/// The Core.Next <see cref="T:Mibo.Elmish.Next.Animation.SpriteSheet"/> stores
/// opaque <c>int&lt;Texture&gt;</c> handles. These helpers register the native
/// <c>Texture2D</c> with the current render buffer and return a ready-to-use
/// neutral sheet, so sample/game code can keep calling <c>assets.Texture</c>
/// and pass the resulting <c>Texture2D</c> through.
/// </remarks>
[<RequireQualifiedAccess>]
module SpriteSheet =

  /// <summary>
  /// Create a sprite sheet from explicit frame rectangles, registering the
  /// native texture with the render buffer.
  /// </summary>
  let fromTexture2D
    (buffer: RenderBuffer2D)
    (texture: Texture2D)
    (origin: Vector2)
    (animations: struct (string * Animation)[])
    : SpriteSheet =
    let h = buffer.Textures.Register texture
    SpriteSheet.fromFrames h origin animations

  /// <summary>
  /// Create a sprite sheet from a uniform grid layout, registering the
  /// native texture with the render buffer.
  /// </summary>
  let fromGridTexture2D
    (buffer: RenderBuffer2D)
    (texture: Texture2D)
    (frameWidth: int)
    (frameHeight: int)
    (columns: int)
    (animations: GridAnimationDef[])
    : SpriteSheet =
    let h = buffer.Textures.Register texture
    SpriteSheet.fromGrid h frameWidth frameHeight columns animations

  /// <summary>
  /// Create a single-animation sprite sheet, registering the native texture.
  /// </summary>
  let singleTexture2D
    (buffer: RenderBuffer2D)
    (texture: Texture2D)
    (frames: Rect[])
    (fps: float32)
    (loop: bool)
    : SpriteSheet =
    let h = buffer.Textures.Register texture
    SpriteSheet.single h frames fps loop

  /// <summary>
  /// Create a static single-frame sprite sheet, registering the native texture.
  /// </summary>
  let staticTexture2D
    (buffer: RenderBuffer2D)
    (texture: Texture2D)
    (sourceRect: Rect)
    : SpriteSheet =
    let h = buffer.Textures.Register texture
    SpriteSheet.static' h sourceRect

  /// <summary>
  /// Add a normal map to a sprite sheet, registering the native texture.
  /// </summary>
  let withTexture2DNormalMap
    (buffer: RenderBuffer2D)
    (normalMap: Texture2D)
    (sheet: SpriteSheet)
    : SpriteSheet =
    let h = buffer.Textures.Register normalMap
    SpriteSheet.withNormalMap h sheet
