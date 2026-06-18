namespace Mibo.Elmish.Graphics2D.Lighting

open Microsoft.Xna.Framework

/// <summary>Helpers for converting colors to shader-friendly vectors.</summary>
module internal ColorHelpers =
  /// <summary>Converts a <see cref="Color"/> to a normalized <see cref="Vector3"/> (0-1 range).</summary>
  let colorToVec3(c: Color) : Vector3 =
    Vector3(float32 c.R / 255.0f, float32 c.G / 255.0f, float32 c.B / 255.0f)

open ColorHelpers

/// <summary>Ambient light for 2D scenes.</summary>
[<Struct>]
type AmbientLight2D = {
  /// <summary>Ambient color (alpha ignored).</summary>
  Color: Color
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.Lighting.AmbientLight2D"/>.</summary>
module AmbientLight2D =
  /// <summary>Creates an ambient light with the given color.</summary>
  let create(color: Color) : AmbientLight2D = { Color = color }

/// <summary>A directional light for 2D scenes (e.g. sunlight).</summary>
[<Struct>]
type DirectionalLight2D = {
  /// <summary>Direction the light travels (normalized at upload time).</summary>
  Direction: Vector2
  /// <summary>Light color.</summary>
  Color: Color
  /// <summary>Intensity multiplier.</summary>
  Intensity: float32
  /// <summary>Whether this light casts shadows.</summary>
  CastsShadows: bool
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.Lighting.DirectionalLight2D"/>.</summary>
module DirectionalLight2D =
  /// <summary>Creates a directional light. Defaults: White, intensity 1, casts shadows.</summary>
  let create(direction: Vector2) : DirectionalLight2D = {
    Direction = direction
    Color = Color.White
    Intensity = 1.0f
    CastsShadows = true
  }

  /// <summary>Sets the color.</summary>
  let inline withColor (color: Color) (l: DirectionalLight2D) = {
    l with
        Color = color
  }

  /// <summary>Sets the intensity.</summary>
  let inline withIntensity (intensity: float32) (l: DirectionalLight2D) = {
    l with
        Intensity = intensity
  }

  /// <summary>Sets whether this light casts shadows.</summary>
  let inline withCastsShadows (casts: bool) (l: DirectionalLight2D) = {
    l with
        CastsShadows = casts
  }

/// <summary>A point light for 2D scenes with radius-based falloff.</summary>
[<Struct>]
type PointLight2D = {
  /// <summary>World position of the light.</summary>
  Position: Vector2
  /// <summary>Light color.</summary>
  Color: Color
  /// <summary>Intensity multiplier.</summary>
  Intensity: float32
  /// <summary>Radius beyond which the light contributes nothing.</summary>
  Radius: float32
  /// <summary>Falloff exponent (higher = sharper edge).</summary>
  Falloff: float32
  /// <summary>Whether this light casts shadows.</summary>
  CastsShadows: bool
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.Lighting.PointLight2D"/>.</summary>
module PointLight2D =
  /// <summary>Creates a point light. Defaults: White, intensity 1, falloff 2, no shadows.</summary>
  let create(position: Vector2, radius: float32) : PointLight2D = {
    Position = position
    Color = Color.White
    Intensity = 1.0f
    Radius = radius
    Falloff = 2.0f
    CastsShadows = false
  }

  /// <summary>Sets the color.</summary>
  let inline withColor (color: Color) (l: PointLight2D) = {
    l with
        Color = color
  }

  /// <summary>Sets the intensity.</summary>
  let inline withIntensity (intensity: float32) (l: PointLight2D) = {
    l with
        Intensity = intensity
  }

  /// <summary>Sets the falloff.</summary>
  let inline withFalloff (falloff: float32) (l: PointLight2D) = {
    l with
        Falloff = falloff
  }

  /// <summary>Sets whether this light casts shadows.</summary>
  let inline withCastsShadows (casts: bool) (l: PointLight2D) = {
    l with
        CastsShadows = casts
  }

/// <summary>A line segment that occludes light (for 2D SDF soft shadows).</summary>
[<Struct>]
type Occluder2D = {
  /// <summary>First endpoint.</summary>
  P1: Vector2
  /// <summary>Second endpoint.</summary>
  P2: Vector2
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics2D.Lighting.Occluder2D"/>.</summary>
module Occluder2D =
  /// <summary>Creates an occluder segment from two points.</summary>
  let create(p1: Vector2, p2: Vector2) : Occluder2D = { P1 = p1; P2 = p2 }
