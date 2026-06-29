namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework

/// <summary>Ambient light configuration for 3D scenes.</summary>
[<Struct>]
type AmbientLight3D = {
  /// <summary>Base color of the ambient light.</summary>
  Color: Color
  /// <summary>Intensity multiplier for the ambient light.</summary>
  Intensity: float32
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics3D.AmbientLight3D"/>.</summary>
module AmbientLight3D =

  /// <summary>Creates an ambient light. Defaults: Intensity=1.</summary>
  let create(color: Color) : AmbientLight3D = {
    Color = color
    Intensity = 1.0f
  }

  let inline withIntensity (v: float32) (l: AmbientLight3D) = {
    l with
        Intensity = v
  }

/// <summary>Directional light configuration for 3D scenes.</summary>
[<Struct>]
type DirectionalLight3D = {
  /// <summary>Direction the light shines (should be normalized).</summary>
  Direction: Vector3
  /// <summary>Color of the directional light.</summary>
  Color: Color
  /// <summary>Intensity multiplier.</summary>
  Intensity: float32
  /// <summary>
  /// Whether this directional light casts shadows.
  /// </summary>
  /// <remarks>
  /// <b>Pipeline-dependent:</b> Not all rendering pipelines support shadow casting.
  /// Pipelines that don't support shadows will ignore this field.
  /// Check your pipeline's documentation for shadow support details.
  /// </remarks>
  CastsShadows: bool
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics3D.DirectionalLight3D"/>.</summary>
module DirectionalLight3D =

  /// <summary>Creates a directional light. Defaults: Color=White, Intensity=1, CastsShadows=true.</summary>
  let create(direction: Vector3) : DirectionalLight3D = {
    Direction = direction
    Color = Color.White
    Intensity = 1.0f
    CastsShadows = true
  }

  let inline withColor (v: Color) (l: DirectionalLight3D) = { l with Color = v }

  let inline withIntensity (v: float32) (l: DirectionalLight3D) = {
    l with
        Intensity = v
  }

  let inline withCastsShadows (v: bool) (l: DirectionalLight3D) = {
    l with
        CastsShadows = v
  }

/// <summary>Point light configuration for 3D scenes.</summary>
[<Struct>]
type PointLight3D = {
  /// <summary>World-space position of the light.</summary>
  Position: Vector3
  /// <summary>Color of the point light.</summary>
  Color: Color
  /// <summary>Brightness multiplier. 1.0 = full, 0.0 = off.</summary>
  Intensity: float32
  /// <summary>Maximum radius of influence.</summary>
  Radius: float32
  /// <summary>Falloff curve exponent. 1.0 = linear, 2.0 = quadratic.</summary>
  Falloff: float32
  /// <summary>
  /// Whether this point light casts shadows.
  /// </summary>
  /// <remarks>
  /// <b>Pipeline-dependent:</b> Not all rendering pipelines support shadow casting.
  /// Pipelines that don't support shadows will ignore this field.
  /// Check your pipeline's documentation for shadow support details.
  /// </remarks>
  CastsShadows: bool
  /// <summary>
  /// Per-light shadow bias override. When None, uses the pipeline's global bias setting.
  /// </summary>
  /// <remarks>
  /// <b>Pipeline-dependent:</b> Only used by pipelines that support shadow casting.
  /// Adjust this value to fix shadow acne (too low) or peter-panning (too high).
  /// Typical range: 0.001 to 0.01.
  /// </remarks>
  ShadowBias: float32 voption
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics3D.PointLight3D"/>.</summary>
module PointLight3D =

  /// <summary>Creates a point light. Defaults: Color=White, Intensity=1, Falloff=2, CastsShadows=false, ShadowBias=None.</summary>
  let create(position: Vector3, radius: float32) : PointLight3D = {
    Position = position
    Color = Color.White
    Intensity = 1.0f
    Radius = radius
    Falloff = 2.0f
    CastsShadows = false
    ShadowBias = ValueNone
  }

  let inline withColor (v: Color) (l: PointLight3D) = { l with Color = v }

  let inline withIntensity (v: float32) (l: PointLight3D) = {
    l with
        Intensity = v
  }

  let inline withFalloff (v: float32) (l: PointLight3D) = { l with Falloff = v }

  let inline withCastsShadows (v: bool) (l: PointLight3D) = {
    l with
        CastsShadows = v
  }

  let inline withShadowBias (v: float32) (l: PointLight3D) = {
    l with
        ShadowBias = ValueSome v
  }

/// <summary>Spot light configuration for cone-shaped lights with distance attenuation.</summary>
[<Struct>]
type SpotLight3D = {
  /// <summary>World-space position of the light.</summary>
  Position: Vector3
  /// <summary>Direction the spot light points (should be normalized).</summary>
  Direction: Vector3
  /// <summary>Color of the spot light.</summary>
  Color: Color
  /// <summary>Intensity multiplier.</summary>
  Intensity: float32
  /// <summary>Maximum distance the light reaches.</summary>
  Radius: float32
  /// <summary>Cosine of the inner cone half-angle (full brightness).</summary>
  InnerCutoff: float32
  /// <summary>Cosine of the outer cone half-angle (fade to zero).</summary>
  OuterCutoff: float32
  /// <summary>
  /// Whether this spot light casts shadows.
  /// </summary>
  /// <remarks>
  /// <b>Pipeline-dependent:</b> Not all rendering pipelines support shadow casting.
  /// Pipelines that don't support shadows will ignore this field.
  /// Check your pipeline's documentation for shadow support details.
  /// </remarks>
  CastsShadows: bool
  /// <summary>
  /// Per-light shadow bias override. When None, uses the pipeline's global bias setting.
  /// </summary>
  /// <remarks>
  /// <b>Pipeline-dependent:</b> Only used by pipelines that support shadow casting.
  /// Adjust this value to fix shadow acne (too low) or peter-panning (too high).
  /// Typical range: 0.001 to 0.01.
  /// </remarks>
  ShadowBias: float32 voption
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics3D.SpotLight3D"/>.</summary>
module SpotLight3D =

  /// <summary>Creates a spot light. Defaults: Color=White, Intensity=1, InnerCutoff=0.5, OuterCutoff=0.7, CastsShadows=false, ShadowBias=None.</summary>
  let create
    (position: Vector3, direction: Vector3, radius: float32)
    : SpotLight3D =
    {
      Position = position
      Direction = direction
      Color = Color.White
      Intensity = 1.0f
      Radius = radius
      InnerCutoff = 0.5f
      OuterCutoff = 0.7f
      CastsShadows = false
      ShadowBias = ValueNone
    }

  let inline withColor (v: Color) (l: SpotLight3D) = { l with Color = v }

  let inline withIntensity (v: float32) (l: SpotLight3D) = {
    l with
        Intensity = v
  }

  let inline withCutoff (inner: float32) (outer: float32) (l: SpotLight3D) = {
    l with
        InnerCutoff = inner
        OuterCutoff = outer
  }

  let inline withCastsShadows (v: bool) (l: SpotLight3D) = {
    l with
        CastsShadows = v
  }

  let inline withShadowBias (v: float32) (l: SpotLight3D) = {
    l with
        ShadowBias = ValueSome v
  }
