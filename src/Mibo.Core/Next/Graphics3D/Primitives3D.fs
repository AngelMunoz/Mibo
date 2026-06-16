namespace Mibo.Elmish.Next.Graphics3D

open Mibo.Elmish.Next.Graphics2D.Base

open System.Numerics
open Mibo.Elmish.Next.Graphics2D

// ─────────────────────────────────────────────────────────────────
// Neutral Material (no resource handles except texture maps)
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Standard PBR material definition. Texture maps are opaque
/// <c>int&lt;Texture&gt;</c> handles. Scalars/colors are backend-neutral.
/// </summary>
[<Struct>]
type MaterialData = {
  AlbedoColor: Color
  AlbedoMap: int<Texture> voption
  Roughness: float32
  RoughnessMap: int<Texture> voption
  Metallic: float32
  MetallicMap: int<Texture> voption
  NormalMap: int<Texture> voption
  EmissionColor: Color
  EmissionMap: int<Texture> voption
  Opacity: float32
  Tiling: Vector2
}

module MaterialData =

  let Default: MaterialData = {
    AlbedoColor = {
      R = 255uy
      G = 255uy
      B = 255uy
      A = 255uy
    }
    AlbedoMap = ValueNone
    Roughness = 0.5f
    RoughnessMap = ValueNone
    Metallic = 0.0f
    MetallicMap = ValueNone
    NormalMap = ValueNone
    EmissionColor = { R = 0uy; G = 0uy; B = 0uy; A = 255uy }
    EmissionMap = ValueNone
    Opacity = 1.0f
    Tiling = Vector2.One
  }

  let inline colored(c: Color) : MaterialData = { Default with AlbedoColor = c }

  let inline unlit(c: Color) : MaterialData = {
    Default with
        AlbedoColor = c
        EmissionColor = c
  }

// ─────────────────────────────────────────────────────────────────
// Neutral 3D light types
// ─────────────────────────────────────────────────────────────────

[<Struct>]
type AmbientLight3DData = { Color: Color; Intensity: float32 }

[<Struct>]
type DirectionalLight3DData = {
  Direction: Vector3
  Color: Color
  Intensity: float32
  CastsShadows: bool
}

[<Struct>]
type PointLight3DData = {
  Position: Vector3
  Color: Color
  Intensity: float32
  Radius: float32
  Falloff: float32
  CastsShadows: bool
  ShadowBias: float32 voption
}

[<Struct>]
type SpotLight3DData = {
  Position: Vector3
  Direction: Vector3
  Color: Color
  Intensity: float32
  Radius: float32
  InnerCutoff: float32
  OuterCutoff: float32
  CastsShadows: bool
  ShadowBias: float32 voption
}
