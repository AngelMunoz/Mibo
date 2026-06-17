namespace Mibo.Elmish.Next.Animation

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Elmish.Next.Graphics2D
open Mibo.Elmish.Next.Graphics2D.Base

/// <summary>A 2D point with integer coordinates.</summary>
[<Struct>]
type Point = {
  X: int
  Y: int
} with

  static member Zero = { X = 0; Y = 0 }

/// <summary>
/// A single animation with pre-computed frame rectangles.
/// </summary>
/// <remarks>
/// This struct is designed for cache-friendly access during the hot path.
/// Users can construct these from any source format.
/// </remarks>
[<Struct>]
type Animation = {
  Frames: Rect[]
  FrameDuration: float32
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
  Name: string
  Row: int
  StartCol: int
  FrameCount: int
  Fps: float32
  Loop: bool
}

/// <summary>
/// A loaded sprite sheet with opaque texture handle and named animations.
/// </summary>
/// <remarks>
/// Uses Dictionary for O(1) runtime lookup of animations by name.
/// The AnimationsByIndex array enables index-based access for zero-allocation updates.
/// </remarks>
type SpriteSheet = {
  Texture: int<Texture>
  NormalMap: int<Texture> voption
  Animations: IReadOnlyDictionary<string, Animation>
  AnimationsByIndex: Animation[]
  AnimationIndices: IReadOnlyDictionary<string, int>
  Origin: Vector2
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
  Sheet: SpriteSheet
  AnimationIndex: int
  CurrentFrame: int
  TimeInFrame: float32
  Finished: bool
  FlipX: bool
  FlipY: bool
  Color: Color
  Scale: float32
  Rotation: float32
}

module Animation =
  /// <summary>Get the total duration of an animation in seconds.</summary>
  let inline duration(anim: Animation) =
    float32 anim.Frames.Length * anim.FrameDuration

/// <summary>
/// Functions for creating sprite sheets from various sources.
/// </summary>
/// <remarks>These are factory functions intended for use at initialization time.</remarks>
module SpriteSheet =
  /// <summary>Create a sprite sheet from explicit frame rectangles.</summary>
  let fromFrames
    (texture: int<Texture>)
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

          {
            Point.X = int f.Width
            Y = int f.Height
          }
        else
          { Point.X = 0; Y = 0 }
      else
        { Point.X = 0; Y = 0 }

    {
      Texture = texture
      NormalMap = ValueNone
      Origin = origin
      Animations = dict
      AnimationsByIndex = arr
      AnimationIndices = indices
      FrameSize = frameSize
    }

  /// <summary>Add a normal map to an existing sprite sheet.</summary>
  let withNormalMap (nm: int<Texture>) (sheet: SpriteSheet) = {
    sheet with
        NormalMap = ValueSome nm
  }

  /// <summary>Create a sprite sheet from a uniform grid layout.</summary>
  /// <exception cref="T:System.ArgumentException">Thrown when columns, frameWidth, or frameHeight are not positive, or when any animation has a non-positive FPS.</exception>
  let fromGrid
    (texture: int<Texture>)
    (frameWidth: int)
    (frameHeight: int)
    (columns: int)
    (animations: GridAnimationDef[])
    : SpriteSheet =
    if columns <= 0 then
      raise(ArgumentException("columns must be positive", nameof columns))

    if frameWidth <= 0 then
      raise(ArgumentException("frameWidth must be positive", nameof frameWidth))

    if frameHeight <= 0 then
      raise(
        ArgumentException("frameHeight must be positive", nameof frameHeight)
      )

    for i = 0 to animations.Length - 1 do
      let def = animations[i]

      if def.FrameCount < 0 then
        raise(
          ArgumentException(
            $"Animation '{def.Name}' has negative FrameCount",
            nameof animations
          )
        )

      if def.Fps <= 0.0f then
        raise(
          ArgumentException(
            $"Animation '{def.Name}' must have positive Fps",
            nameof animations
          )
        )

    let origin = Vector2(float32 frameWidth / 2.0f, float32 frameHeight / 2.0f)

    let dict = Dictionary<string, Animation>(animations.Length)
    let indices = Dictionary<string, int>(animations.Length)
    let arr = Array.zeroCreate<Animation> animations.Length

    for i = 0 to animations.Length - 1 do
      let def = animations[i]
      let frames = Array.zeroCreate<Rect> def.FrameCount

      for j = 0 to def.FrameCount - 1 do
        let col = (def.StartCol + j) % columns
        let actualRow = def.Row + (def.StartCol + j) / columns

        frames[j] <-
          Rect.create(
            float32(col * frameWidth),
            float32(actualRow * frameHeight),
            float32 frameWidth,
            float32 frameHeight
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
      NormalMap = ValueNone
      Origin = origin
      Animations = dict
      AnimationsByIndex = arr
      AnimationIndices = indices
      FrameSize = { X = frameWidth; Y = frameHeight }
    }

  /// <summary>Create a single-animation sprite sheet.</summary>
  let single
    (texture: int<Texture>)
    (frames: Rect[])
    (fps: float32)
    (loop: bool)
    : SpriteSheet =
    let origin =
      if frames.Length > 0 then
        Vector2(frames.[0].Width / 2.0f, frames.[0].Height / 2.0f)
      else
        Vector2.Zero

    let frameSize =
      if frames.Length > 0 then
        {
          X = int frames.[0].Width
          Y = int frames.[0].Height
        }
      else
        { X = 0; Y = 0 }

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
      NormalMap = ValueNone
      Origin = origin
      Animations = dict
      AnimationsByIndex = [| anim |]
      AnimationIndices = indices
      FrameSize = frameSize
    }

  let static' (texture: int<Texture>) (sourceRect: Rect) : SpriteSheet =
    single texture [| sourceRect |] 1.0f false

  /// <summary>
  /// Try to get the index for an animation name.
  /// </summary>
  /// <remarks>Use at load time to resolve animation names to indices for zero-allocation playback.</remarks>
  let inline tryGetAnimationIndex
    (name: string)
    (sheet: SpriteSheet)
    : int voption =
    match sheet.AnimationIndices.TryGetValue(name) with
    | true, idx -> ValueSome idx
    | false, _ -> ValueNone

  /// <summary>Get the list of animation names in this sheet.</summary>
  let animationNames(sheet: SpriteSheet) : string seq =
    sheet.AnimationIndices.Keys

/// <summary>
/// Functions for creating, updating, and querying animated sprites.
/// </summary>
/// <remarks>Update functions are designed for zero allocations during the game loop.</remarks>
module AnimatedSprite =

  /// <summary>Create a new animated sprite starting on the specified animation.</summary>
  let create (sheet: SpriteSheet) (animationName: string) : AnimatedSprite =
    let idx =
      match sheet.AnimationIndices.TryGetValue(animationName) with
      | true, i -> i
      | false, _ -> 0

    {
      Sheet = sheet
      AnimationIndex = idx
      CurrentFrame = 0
      TimeInFrame = 0.0f
      Finished = false
      FlipX = false
      FlipY = false
      Color = {
        R = 255uy
        G = 255uy
        B = 255uy
        A = 255uy
      }
      Scale = 1.0f
      Rotation = 0.0f
    }

  /// <summary>Create with initial visual properties.</summary>
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

  /// <summary>Play an animation by name. Does string lookup only when actually changing.</summary>
  let play (animationName: string) (sprite: AnimatedSprite) : AnimatedSprite =
    match sprite.Sheet.AnimationIndices.TryGetValue(animationName) with
    | false, _ -> sprite
    | true, idx when idx = sprite.AnimationIndex && not sprite.Finished ->
      sprite
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
  /// <remarks>For maximum performance, resolve animation names to indices once at load time.</remarks>
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

  /// <summary>Play animation only if not already playing it.</summary>
  let playIfNot
    (animationName: string)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    match sprite.Sheet.AnimationIndices.TryGetValue(animationName) with
    | true, idx when idx = sprite.AnimationIndex -> sprite
    | true, _ -> play animationName sprite
    | false, _ -> sprite

  /// <summary>Force restart the current animation from the beginning.</summary>
  let restart(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        CurrentFrame = 0
        TimeInFrame = 0.0f
        Finished = false
  }

  /// <summary>
  /// Advance the animation by delta time.
  /// </summary>
  /// <remarks>Call from your Elmish update function each frame.</remarks>
  let update (deltaSeconds: float32) (sprite: AnimatedSprite) : AnimatedSprite =
    if sprite.Finished then
      sprite
    else
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

  /// <summary>Get the current source rectangle for rendering.</summary>
  let inline currentSource(sprite: AnimatedSprite) : Rect =
    let anim = sprite.Sheet.AnimationsByIndex.[sprite.AnimationIndex]

    if anim.Frames.Length = 0 then
      Rect.Zero
    else
      anim.Frames.[min sprite.CurrentFrame (anim.Frames.Length - 1)]

  /// <summary>Is the current animation finished? (always false for looping animations).</summary>
  let inline isFinished(sprite: AnimatedSprite) = sprite.Finished

  /// <summary>Is currently playing the specified animation?</summary>
  let isPlaying (animName: string) (sprite: AnimatedSprite) =
    match sprite.Sheet.AnimationIndices.TryGetValue(animName) with
    | true, idx -> idx = sprite.AnimationIndex && not sprite.Finished
    | false, _ -> false

  /// <summary>Get the total duration of the current animation.</summary>
  let inline duration(sprite: AnimatedSprite) =
    Animation.duration sprite.Sheet.AnimationsByIndex.[sprite.AnimationIndex]

  let inline withColor
    (color: Color)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    { sprite with Color = color }

  let inline withScale
    (scale: float32)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    { sprite with Scale = scale }

  let inline withRotation
    (rotation: float32)
    (sprite: AnimatedSprite)
    : AnimatedSprite =
    { sprite with Rotation = rotation }

  let inline flipX(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipX = not sprite.FlipX
  }

  let inline flipY(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipY = not sprite.FlipY
  }

  let inline facingLeft(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipX = true
  }

  let inline facingRight(sprite: AnimatedSprite) : AnimatedSprite = {
    sprite with
        FlipX = false
  }
