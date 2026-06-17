namespace Mibo.Elmish.Next.Graphics2D.Lighting

open System.Numerics
open Mibo.Elmish.Next.Graphics2D.Base

/// <summary>Global ambient light applied to all lit sprites.</summary>
[<Struct>]
type AmbientLight2D = {
  /// <summary>Base illumination color. Lower values produce darker scenes.</summary>
  Color: Color
}

/// <summary>A radial point light (torch, lamp, explosion).</summary>
[<Struct>]
type PointLight2D = {
  /// <summary>World-space position of the light source.</summary>
  Position: Vector2

  /// <summary>Color of the light.</summary>
  Color: Color

  /// <summary>Brightness multiplier. 1.0 = full, 0.0 = off.</summary>
  Intensity: float32

  /// <summary>Maximum distance the light reaches in world units.</summary>
  Radius: float32

  /// <summary>Falloff curve exponent. 1.0 = linear, 2.0 = quadratic.</summary>
  Falloff: float32

  /// <summary>Whether this light should cast shadows.</summary>
  CastsShadows: bool
}

/// <summary>A directional light (sun, moon) with parallel rays.</summary>
[<Struct>]
type DirectionalLight2D = {
  /// <summary>Normalized direction the light travels IN (not toward). E.g. (0.3, -0.7) for sun shining down-right.</summary>
  Direction: Vector2

  /// <summary>Color of the light.</summary>
  Color: Color

  /// <summary>Brightness multiplier. 1.0 = full, 0.0 = off.</summary>
  Intensity: float32

  /// <summary>Whether this light should cast shadows.</summary>
  CastsShadows: bool
}

/// <summary>A shadow-casting occluder defined as a line segment.</summary>
[<Struct>]
type Occluder2D = {
  /// <summary>Start point of the occluder segment in world space.</summary>
  P1: Vector2

  /// <summary>End point of the occluder segment in world space.</summary>
  P2: Vector2
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Next.Graphics2D.Lighting.AmbientLight2D"/>.</summary>
module AmbientLight2D =

  /// <summary>Creates an ambient light with the given color.</summary>
  let create(color: Color) : AmbientLight2D = { Color = color }

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Next.Graphics2D.Lighting.DirectionalLight2D"/>.</summary>
module DirectionalLight2D =

  /// <summary>Creates a directional light. Defaults: Color=White, Intensity=1, CastsShadows=true.</summary>
  let create(direction: Vector2) : DirectionalLight2D = {
    Direction = direction
    Color = {
      R = 255uy
      G = 255uy
      B = 255uy
      A = 255uy
    }
    Intensity = 1.0f
    CastsShadows = true
  }

  let inline withColor (v: Color) (l: DirectionalLight2D) = { l with Color = v }

  let inline withIntensity (v: float32) (l: DirectionalLight2D) = {
    l with
        Intensity = v
  }

  let inline withCastsShadows (v: bool) (l: DirectionalLight2D) = {
    l with
        CastsShadows = v
  }

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Next.Graphics2D.Lighting.PointLight2D"/>.</summary>
module PointLight2D =

  /// <summary>Creates a point light. Defaults: Color=White, Intensity=1, Falloff=2, CastsShadows=false.</summary>
  let create(position: Vector2, radius: float32) : PointLight2D = {
    Position = position
    Color = {
      R = 255uy
      G = 255uy
      B = 255uy
      A = 255uy
    }
    Intensity = 1.0f
    Radius = radius
    Falloff = 2.0f
    CastsShadows = false
  }

  let inline withColor (v: Color) (l: PointLight2D) = { l with Color = v }

  let inline withIntensity (v: float32) (l: PointLight2D) = {
    l with
        Intensity = v
  }

  let inline withFalloff (v: float32) (l: PointLight2D) = { l with Falloff = v }

  let inline withCastsShadows (v: bool) (l: PointLight2D) = {
    l with
        CastsShadows = v
  }

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Next.Graphics2D.Lighting.Occluder2D"/>.</summary>
module Occluder2D =

  /// <summary>Creates an occluder from two endpoints.</summary>
  let create(p1: Vector2, p2: Vector2) : Occluder2D = { P1 = p1; P2 = p2 }
