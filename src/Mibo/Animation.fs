namespace Mibo.Animation

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

// ─────────────────────────────────────────────────────────────────────────────
// Core Types (Format-Agnostic, Performance-Optimized)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single animation with pre-computed frame rectangles.
/// </summary>
/// <remarks>
/// This struct is designed for cache-friendly access during the hot path.
/// Users can construct these from any source format.
/// </remarks>
[<Struct>]
type Animation = {
  /// Pre-computed source rectangles for each frame.
  Frames: Rectangle[]
  /// Duration of each frame in seconds (1/fps).
  FrameDuration: float32
  /// Does this animation loop?
  Loop: bool
}

/// <summary>
/// Definition for an animation in a grid-based sprite sheet.
/// </summary>
/// <remarks>
/// Use this with SpriteSheet.fromGrid for self-documenting animation definitions.
/// </remarks>
[<Struct>]
type GridAnimationDef = {
  /// Name of the animation (e.g., "idle", "walk").
  Name: string
  /// Row in the sprite sheet (0-indexed).
  Row: int
  /// Starting column in the row (0-indexed).
  StartCol: int
  /// Number of frames in this animation.
  FrameCount: int
  /// Frames per second.
  Fps: float32
  /// Does this animation loop?
  Loop: bool
}

/// <summary>
/// A loaded sprite sheet with texture and named animations.
/// </summary>
/// <remarks>
/// Uses Dictionary for O(1) runtime lookup of animations by name.
/// The AnimationsByIndex array enables index-based access for zero-string-allocation
/// updates during the hot path.
/// </remarks>
type SpriteSheet = {
  /// The texture atlas.
  Texture: Texture2D
  /// Named animations - Dictionary for O(1) runtime lookup.
  Animations: IReadOnlyDictionary<string, Animation>
  /// Pre-resolved animation array for index-based access.
  AnimationsByIndex: Animation[]
  /// Name to index mapping for resolving animation names once.
  AnimationIndices: IReadOnlyDictionary<string, int>
  /// Origin point for drawing (typically center of frame).
  Origin: Vector2
  /// Frame dimensions (width, height).
  FrameSize: Point
}

/// <summary>
/// Runtime state for a playing animation.
/// </summary>
/// <remarks>
/// This is a small struct designed for zero-allocation updates.
/// Store this in your Elmish model for each animated entity.
/// </remarks>
[<Struct>]
type AnimatedSprite = {
  /// Reference to the loaded sprite sheet.
  Sheet: SpriteSheet
  /// Index into AnimationsByIndex (faster than string lookup every frame).
  AnimationIndex: int
  /// Current frame index within the animation.
  CurrentFrame: int
  /// Time accumulated in the current frame (seconds).
  TimeInFrame: float32
  /// Has animation finished? (non-looping only)
  Finished: bool
  /// Flip horizontally?
  FlipX: bool
  /// Flip vertically?
  FlipY: bool
  /// Tint color.
  Color: Color
  /// Scale factor.
  Scale: float32
  /// Rotation in radians.
  Rotation: float32
}

// ─────────────────────────────────────────────────────────────────────────────
// Animation Module: Data Queries
// ─────────────────────────────────────────────────────────────────────────────

module Animation =
  /// <summary>
  /// Get the total duration of an animation in seconds.
  /// </summary>
  let inline duration(anim: Animation) =
    float32 anim.Frames.Length * anim.FrameDuration

// ─────────────────────────────────────────────────────────────────────────────
// SpriteSheet Module: Factory Functions (Cold Path)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Functions for creating sprite sheets from various sources.
/// </summary>
/// <remarks>
/// These are factory functions intended for use at initialization time.
/// Allocations are acceptable here as sheets are typically created once.
/// </remarks>
module SpriteSheet =

  /// <summary>
  /// Create a sprite sheet from explicit frame rectangles.
  /// </summary>
  /// <remarks>
  /// This is the most flexible constructor - use this when you've already
  /// parsed animation data from any format (TexturePacker, Aseprite, custom).
  /// </remarks>
  /// <param name="texture">The texture atlas.</param>
  /// <param name="origin">Origin point for drawing (typically center of frame).</param>
  /// <param name="animations">Array of (name, animation) struct tuples.</param>
  let fromFrames
    (texture: Texture2D)
    (origin: Vector2)
    (animations: struct (string * Animation)[])
    : SpriteSheet =
    let dict = Dictionary<string, Animation>(animations.Length)
    let indices = Dictionary<string, int>(animations.Length)
    let arr = Array.zeroCreate<Animation> animations.Length

    for i = 0 to animations.Length - 1 do
      let struct (name, anim) = animations.[i]
      dict.[name] <- anim
      indices.[name] <- i
      arr.[i] <- anim

    let frameSize =
      if animations.Length > 0 then
        let struct (_, firstAnim) = animations.[0]

        if firstAnim.Frames.Length > 0 then
          let f = firstAnim.Frames.[0]
          Point(f.Width, f.Height)
        else
          Point.Zero
      else
        Point.Zero

    {
      Texture = texture
      Origin = origin
      Animations = dict
      AnimationsByIndex = arr
      AnimationIndices = indices
      FrameSize = frameSize
    }

  /// <summary>
  /// Create a sprite sheet from a uniform grid layout.
  /// </summary>
  /// <remarks>
  /// Use this for sprite sheets where all frames are the same size
  /// arranged in a grid (common for retro-style games).
  /// </remarks>
  /// <param name="texture">The texture atlas.</param>
  /// <param name="frameWidth">Width of each frame in pixels.</param>
  /// <param name="frameHeight">Height of each frame in pixels.</param>
  /// <param name="columns">Number of columns in the sprite sheet.</param>
  /// <param name="animations">Array of GridAnimationDef records.</param>
  let fromGrid
    (texture: Texture2D)
    (frameWidth: int)
    (frameHeight: int)
    (columns: int)
    (animations: GridAnimationDef[])
    : SpriteSheet =
    let origin = Vector2(float32 frameWidth / 2.0f, float32 frameHeight / 2.0f)

    let dict = Dictionary<string, Animation>(animations.Length)
    let indices = Dictionary<string, int>(animations.Length)
    let arr = Array.zeroCreate<Animation> animations.Length

    for i = 0 to animations.Length - 1 do
      let def = animations[i]
      let frames = Array.zeroCreate<Rectangle> def.FrameCount

      for j = 0 to def.FrameCount - 1 do
        let col = (def.StartCol + j) % columns
        let actualRow = def.Row + (def.StartCol + j) / columns

        frames[j] <-
          Rectangle(
            col * frameWidth,
            actualRow * frameHeight,
            frameWidth,
            frameHeight
          )

      let anim = {
        Frames = frames
        FrameDuration = 1.0f / def.Fps
        Loop = def.Loop
      }

      dict[def.Name] <- anim
      indices[def.Name] <- i
      arr[i] <- anim

    {
      Texture = texture
      Origin = origin
      Animations = dict
      AnimationsByIndex = arr
      AnimationIndices = indices
      FrameSize = Point(frameWidth, frameHeight)
    }

  /// <summary>
  /// Create a single-animation sprite sheet.
  /// </summary>
  /// <remarks>
  /// Convenience function for simple sprites with just one animation
  /// (e.g., a spinning coin, flickering torch).
  /// </remarks>
  let single
    (texture: Texture2D)
    (frames: Rectangle[])
    (fps: float32)
    (loop: bool)
    : SpriteSheet =
    let origin =
      if frames.Length > 0 then
        Vector2(
          float32 frames.[0].Width / 2.0f,
          float32 frames.[0].Height / 2.0f
        )
      else
        Vector2.Zero

    let frameSize =
      if frames.Length > 0 then
        Point(frames.[0].Width, frames.[0].Height)
      else
        Point.Zero

    let anim = {
      Frames = frames
      FrameDuration = 1.0f / fps
      Loop = loop
    }

    let dict = Dictionary<string, Animation>(1)
    dict.["default"] <- anim
    let indices = Dictionary<string, int>(1)
    indices.["default"] <- 0

    {
      Texture = texture
      Origin = origin
      Animations = dict
      AnimationsByIndex = [| anim |]
      AnimationIndices = indices
      FrameSize = frameSize
    }

  /// <summary>
  /// Create a sprite sheet for a single static frame (no animation).
  /// </summary>
  let static' (texture: Texture2D) (sourceRect: Rectangle) : SpriteSheet =
    single texture [| sourceRect |] 1.0f false

  /// <summary>
  /// Try to get the index for an animation name.
  /// </summary>
  /// <remarks>
  /// Use this at load time to resolve animation names to indices,
  /// then use index-based playback during gameplay for zero allocations.
  /// </remarks>
  let inline tryGetAnimationIndex
    (name: string)
    (sheet: SpriteSheet)
    : int voption =
    match sheet.AnimationIndices.TryGetValue(name) with
    | true, idx -> ValueSome idx
    | false, _ -> ValueNone

  /// <summary>
  /// Get the list of animation names in this sheet.
  /// </summary>
  let animationNames(sheet: SpriteSheet) : string seq =
    sheet.AnimationIndices.Keys

// ─────────────────────────────────────────────────────────────────────────────
// AnimatedSprite Module: Runtime Operations (Hot Path Optimized)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Functions for creating, updating, and drawing animated sprites.
/// </summary>
/// <remarks>
/// The update and draw functions are aggressively inlined and designed
/// for zero allocations during the game loop.
/// </remarks>
module AnimatedSprite =

  // ─────────────────────────────────────────────────────────────────────────
  // Creation (Cold Path)
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Create a new animated sprite starting on the specified animation.
  /// </summary>
  /// <param name="sheet">The sprite sheet containing animations.</param>
  /// <param name="animationName">Name of the animation to play initially.</param>
  let create (sheet: SpriteSheet) (animationName: string) : AnimatedSprite =
    let idx =
      match sheet.AnimationIndices.TryGetValue(animationName) with
      | true, i -> i
      | false, _ -> 0 // Fallback to first animation

    {
      Sheet = sheet
      AnimationIndex = idx
      CurrentFrame = 0
      TimeInFrame = 0.0f
      Finished = false
      FlipX = false
      FlipY = false
      Color = Color.White
      Scale = 1.0f
      Rotation = 0.0f
    }

  /// <summary>
  /// Create with initial visual properties.
  /// </summary>
  let createWith
    (sheet: SpriteSheet)
    (animationName: string)
    (color: Color)
    (scale: float32)
    : AnimatedSprite =
    {
      create sheet animationName with
          Color = color
          Scale = scale
    }

  // ─────────────────────────────────────────────────────────────────────────
  // Animation Control (Warm - one dictionary lookup when changing)
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Play an animation by name. Does string lookup only when actually changing.
  /// </summary>
  let play (animationName: string) (sprite: AnimatedSprite) : AnimatedSprite =
    match sprite.Sheet.AnimationIndices.TryGetValue(animationName) with
    | false, _ -> sprite // Animation not found, no-op
    | true, idx when idx = sprite.AnimationIndex && not sprite.Finished ->
      sprite // Already playing
    | true, idx ->
        {
          sprite with
              AnimationIndex = idx
              CurrentFrame = 0
              TimeInFrame = 0.0f
              Finished = false
        }

  /// <summary>
  /// Play by animation index (zero string allocation).
  /// </summary>
  /// <remarks>
  /// For maximum performance, resolve animation names to indices once at load time,
  /// then use this function during gameplay.
  /// </remarks>
  let playByIndex (animIndex: int) (sprite: AnimatedSprite) : AnimatedSprite =
    if animIndex = sprite.AnimationIndex && not sprite.Finished then
      sprite
    elif
      animIndex < 0 || animIndex >= sprite.Sheet.AnimationsByIndex.Length
    then
      sprite
    else
      {
        sprite with
            AnimationIndex = animIndex
            CurrentFrame = 0
            TimeInFrame = 0.0f
            Finished = false
      }

  /// <summary>
  /// Play animation only if not already playing it.
  /// </summary>
  let playIfNot
    (animationName: string)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    match sprite.Sheet.AnimationIndices.TryGetValue(animationName) with
    | true, idx when idx = sprite.AnimationIndex -> sprite
    | true, _ -> play animationName sprite
    | false, _ -> sprite

  /// <summary>
  /// Force restart the current animation from the beginning.
  /// </summary>
  let restart(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        CurrentFrame = 0
        TimeInFrame = 0.0f
        Finished = false
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Update (HOT PATH - Zero allocations)
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Advance the animation by delta time.
  /// </summary>
  /// <remarks>
  /// This function is designed for zero allocations. Call it from your
  /// Elmish update function each frame.
  /// </remarks>
  let update (deltaSeconds: float32) (sprite: AnimatedSprite) : AnimatedSprite =
    if sprite.Finished then
      sprite
    else
      // Direct array access - O(1), no dictionary lookup
      let anim = sprite.Sheet.AnimationsByIndex.[sprite.AnimationIndex]

      if anim.Frames.Length = 0 || anim.FrameDuration <= 0.0f then
        sprite
      else
        let totalTime = sprite.TimeInFrame + deltaSeconds
        let framesToSkip = int(totalTime / anim.FrameDuration)

        if framesToSkip = 0 then
          { sprite with TimeInFrame = totalTime }
        else
          let remainingTime = totalTime % anim.FrameDuration
          let nextFrame = sprite.CurrentFrame + framesToSkip

          if nextFrame < anim.Frames.Length then
            {
              sprite with
                  CurrentFrame = nextFrame
                  TimeInFrame = remainingTime
            }
          elif anim.Loop then
            {
              sprite with
                  CurrentFrame = nextFrame % anim.Frames.Length
                  TimeInFrame = remainingTime
            }
          else
            {
              sprite with
                  Finished = true
                  CurrentFrame = anim.Frames.Length - 1
                  TimeInFrame = 0.0f
            }

  // ─────────────────────────────────────────────────────────────────────────
  // Queries (HOT PATH - Zero allocations)
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Get the current source rectangle for rendering.
  /// </summary>
  let inline currentSource(sprite: AnimatedSprite) : Rectangle =
    let anim = sprite.Sheet.AnimationsByIndex.[sprite.AnimationIndex]

    if anim.Frames.Length = 0 then
      Rectangle.Empty
    else
      anim.Frames.[min sprite.CurrentFrame (anim.Frames.Length - 1)]

  /// <summary>
  /// Is the current animation finished? (always false for looping animations)
  /// </summary>
  let inline isFinished(sprite: AnimatedSprite) = sprite.Finished

  /// <summary>
  /// Is currently playing the specified animation?
  /// </summary>
  let isPlaying (animName: string) (sprite: AnimatedSprite) =
    match sprite.Sheet.AnimationIndices.TryGetValue(animName) with
    | true, idx -> idx = sprite.AnimationIndex && not sprite.Finished
    | false, _ -> false

  /// <summary>
  /// Get the total duration of the current animation.
  /// </summary>
  let inline duration(sprite: AnimatedSprite) =
    Animation.duration sprite.Sheet.AnimationsByIndex.[sprite.AnimationIndex]

  // ─────────────────────────────────────────────────────────────────────────
  // Visual Properties (Cold/Warm - struct copy)
  // ─────────────────────────────────────────────────────────────────────────

  /// Set the tint color.
  let inline withColor
    (color: Color)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    { sprite with Color = color }

  /// Set the scale factor.
  let inline withScale
    (scale: float32)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    { sprite with Scale = scale }

  /// Set the rotation in radians.
  let inline withRotation
    (rotation: float32)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    { sprite with Rotation = rotation }

  /// Toggle horizontal flip.
  let inline flipX(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipX = not sprite.FlipX
  }

  /// Toggle vertical flip.
  let inline flipY(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipY = not sprite.FlipY
  }

  /// Set facing left (flip X on).
  let inline facingLeft(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipX = true
  }

  /// Set facing right (flip X off).
  let inline facingRight(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipX = false
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Drawing (HOT PATH - Zero allocations)
  // ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Get the MonoGame SpriteEffects for the sprite's current flip state.
  /// </summary>
  let inline toSpriteEffects(sprite: AnimatedSprite) =
    match sprite.FlipX, sprite.FlipY with
    | false, false -> SpriteEffects.None
    | true, false -> SpriteEffects.FlipHorizontally
    | false, true -> SpriteEffects.FlipVertically
    | true, true ->
      SpriteEffects.FlipHorizontally ||| SpriteEffects.FlipVertically

  /// <summary>
  /// Draw the sprite at a position using the 2D render buffer.
  /// </summary>
  /// <remarks>
  /// The sprite is drawn centered on the position with the sheet's origin.
  /// </remarks>
  let draw
    (position: Vector2)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer<RenderCmd2D>)
    (sprite: AnimatedSprite)
    =
    let anim = sprite.Sheet.AnimationsByIndex[sprite.AnimationIndex]

    if anim.Frames.Length > 0 then
      let src = anim.Frames[min sprite.CurrentFrame (anim.Frames.Length - 1)]
      let scaledW = int(float32 src.Width * sprite.Scale)
      let scaledH = int(float32 src.Height * sprite.Scale)

      buffer.Add(
        layer,
        DrawTexture(
          sprite.Sheet.Texture,
          Rectangle(int position.X, int position.Y, scaledW, scaledH),
          Nullable src,
          sprite.Color,
          sprite.Rotation,
          sprite.Sheet.Origin,
          toSpriteEffects sprite,
          0.0f
        )
      )

  /// <summary>
  /// Draw the sprite with a custom depth value.
  /// </summary>
  let drawWithDepth
    (position: Vector2)
    (depth: float32)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer<RenderCmd2D>)
    (sprite: AnimatedSprite)
    =
    let anim = sprite.Sheet.AnimationsByIndex.[sprite.AnimationIndex]

    if anim.Frames.Length > 0 then
      let src = anim.Frames.[min sprite.CurrentFrame (anim.Frames.Length - 1)]
      let scaledW = int(float32 src.Width * sprite.Scale)
      let scaledH = int(float32 src.Height * sprite.Scale)

      buffer.Add(
        layer,
        DrawTexture(
          sprite.Sheet.Texture,
          Rectangle(int position.X, int position.Y, scaledW, scaledH),
          Nullable src,
          sprite.Color,
          sprite.Rotation,
          sprite.Sheet.Origin,
          toSpriteEffects sprite,
          depth
        )
      )

  /// <summary>
  /// Draw the sprite at a specific destination rectangle (ignores scale/origin).
  /// </summary>
  /// <remarks>
  /// Note: Rotation will occur around the top-left corner (Vector2.Zero)
  /// of the destination rectangle.
  /// </remarks>
  let drawRect
    (destRect: Rectangle)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer<RenderCmd2D>)
    (sprite: AnimatedSprite)
    =
    let anim = sprite.Sheet.AnimationsByIndex.[sprite.AnimationIndex]

    if anim.Frames.Length > 0 then
      let src = anim.Frames.[min sprite.CurrentFrame (anim.Frames.Length - 1)]

      buffer.Add(
        layer,
        DrawTexture(
          sprite.Sheet.Texture,
          destRect,
          Nullable src,
          sprite.Color,
          sprite.Rotation,
          Vector2.Zero,
          toSpriteEffects sprite,
          0.0f
        )
      )
