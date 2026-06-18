namespace Mibo.Elmish.Graphics2D.Lighting

open System.Numerics
open Microsoft.Xna.Framework

/// <summary>A single 2D particle rendered as a textured quad with optional sprite-sheet source rect.</summary>
/// <remarks>
/// This is a render snapshot. Simulation state (velocity, lifetime, spin, color rules) lives in the
/// user's model and is written into this struct at the start of the view function.
/// </remarks>
[<Struct>]
type Particle2D = {
  /// <summary>Center position in world/screen space.</summary>
  Position: Vector2

  /// <summary>Width and height of the quad.</summary>
  Size: Vector2

  /// <summary>Rotation in degrees around the center.</summary>
  Rotation: float32

  /// <summary>Source rectangle within the texture in pixels. Use (0, 0, tw, th) for the full texture.</summary>
  SourceRect: Rectangle

  /// <summary>Tint color. Alpha controls transparency.</summary>
  Color: Color
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.Lighting.Particle2D"/>.</summary>
module Particle2D =

  /// <summary>Creates a particle with required fields. Defaults: Rotation=0, SourceRect=empty, Color=White.</summary>
  let create(position: Vector2, size: Vector2) : Particle2D = {
    Position = position
    Size = size
    Rotation = 0.0f
    SourceRect = Rectangle(0, 0, 0, 0)
    Color = Color.White
  }

  let inline withRotation (v: float32) (p: Particle2D) = { p with Rotation = v }

  let inline withSourceRect (v: Rectangle) (p: Particle2D) = {
    p with
        SourceRect = v
  }

  let inline withColor (v: Color) (p: Particle2D) = { p with Color = v }
