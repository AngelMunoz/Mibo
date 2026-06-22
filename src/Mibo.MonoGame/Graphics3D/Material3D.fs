namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// Standard PBR material definition. Carries visual properties and texture maps,
/// but never an <c>Effect</c> handle. The pipeline binds the appropriate shader.
/// </summary>
/// <remarks>
/// This is a struct designed for zero-allocation use in the render command hot path.
/// Texture maps are optional; when absent, the scalar/color values apply.
/// For the native MonoGame path, the model's own <c>Effect</c> is preferred;
/// this struct is the fallback/upgrade carrier for custom PBR pipelines.
/// </remarks>
[<Struct>]
type Material3D = {
  /// <summary>Base albedo color. Multiplied with albedo map if present.</summary>
  AlbedoColor: Color
  /// <summary>Optional albedo/diffuse texture map.</summary>
  AlbedoMap: Texture2D voption

  /// <summary>Perceptual roughness. 0 = mirror-like, 1 = fully diffuse.</summary>
  Roughness: float32
  /// <summary>Optional roughness texture map (typically stored in green channel).</summary>
  RoughnessMap: Texture2D voption

  /// <summary>Metallic factor. 0 = dielectric, 1 = fully metallic.</summary>
  Metallic: float32
  /// <summary>Optional metallic texture map (typically stored in blue channel).</summary>
  MetallicMap: Texture2D voption

  /// <summary>Optional normal map for surface detail.</summary>
  NormalMap: Texture2D voption

  /// <summary>Emissive color for self-illumination.</summary>
  EmissionColor: Color
  /// <summary>Optional emissive texture map.</summary>
  EmissionMap: Texture2D voption

  /// <summary>Opacity / alpha value. 1 = fully opaque, 0 = fully transparent.</summary>
  Opacity: float32

  /// <summary>UV tiling offset for texture coordinates.</summary>
  Tiling: Vector2
}

/// <summary>Convenience values and functions for <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/>.</summary>
module Material3D =

  /// <summary>
  /// A default opaque white material with no textures and mid-roughness.
  /// Suitable as a fallback when no material is specified.
  /// </summary>
  let defaults: Material3D = {
    AlbedoColor = Color.White
    AlbedoMap = ValueNone
    Roughness = 0.5f
    RoughnessMap = ValueNone
    Metallic = 0.0f
    MetallicMap = ValueNone
    NormalMap = ValueNone
    EmissionColor = Color.Black
    EmissionMap = ValueNone
    Opacity = 1.0f
    Tiling = Vector2.One
  }

  /// <summary>Creates an unlit emissive material with the given color.</summary>
  let unlit(color: Color) : Material3D = {
    defaults with
        AlbedoColor = color
        EmissionColor = color
  }

  /// <summary>Creates a basic opaque material with a single albedo color.</summary>
  let colored(color: Color) : Material3D = { defaults with AlbedoColor = color }

  /// <summary>Creates a material with an albedo texture map.</summary>
  let withAlbedoMap (tex: Texture2D) (mat: Material3D) : Material3D = {
    mat with
        AlbedoMap = ValueSome tex
  }

  /// <summary>Creates a material with a normal map.</summary>
  let withNormalMap (tex: Texture2D) (mat: Material3D) : Material3D = {
    mat with
        NormalMap = ValueSome tex
  }

  /// <summary>Creates a material with a roughness map.</summary>
  let withRoughnessMap (tex: Texture2D) (mat: Material3D) : Material3D = {
    mat with
        RoughnessMap = ValueSome tex
  }

  /// <summary>Creates a material with a metallic map.</summary>
  let withMetallicMap (tex: Texture2D) (mat: Material3D) : Material3D = {
    mat with
        MetallicMap = ValueSome tex
  }
