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

  // ───────────────────────────────────────────────────────────────────
  // fromModelMeshPart / fromEffect — read a part's baked native effect
  // into a Material3D. This is the bridge that lets the PBR pipeline
  // preserve a model's authored look when it swaps out the native effect.
  // Mirrors the canonical Mibo.Raylib Material3D.fromRaylibMaterial shape,
  // reduced to what MonoGame's stock BasicEffect/SkinnedEffect expose:
  // DiffuseColor, Texture, Alpha. Map extraction (normal/roughness/metallic)
  // is deferred until the content-pipeline rework (spec §10).
  // ───────────────────────────────────────────────────────────────────

  let private vec3ToColor(v: Vector3) : Color =
    // DiffuseColor is in [0,1] float32; Color has float32 * float32 * float32 overloads.
    Color(
      min 1.0f (max 0.0f v.X),
      min 1.0f (max 0.0f v.Y),
      min 1.0f (max 0.0f v.Z)
    )

  /// <summary>
  /// Reads material params from a native <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>
  /// that exposes diffuse color/texture/alpha (<c>BasicEffect</c>/<c>SkinnedEffect</c>).
  /// Returns <c>defaults</c> (opaque white, mid-roughness, non-metal) when the effect exposes
  /// no recognizable material fields. Per §10: albedo color + albedo map + opacity only.
  /// </summary>
  let fromEffect(effect: Effect) : Material3D =
    match box effect with
    | :? BasicEffect as be ->
      let albedoMap =
        if be.TextureEnabled && not(isNull be.Texture) then
          ValueSome be.Texture
        else
          ValueNone

      {
        defaults with
            AlbedoColor = vec3ToColor be.DiffuseColor
            AlbedoMap = albedoMap
            Opacity = be.Alpha
      }
    | :? SkinnedEffect as se ->
      let albedoMap =
        if not(isNull se.Texture) then
          ValueSome se.Texture
        else
          ValueNone

      {
        defaults with
            AlbedoColor = vec3ToColor se.DiffuseColor
            AlbedoMap = albedoMap
            Opacity = se.Alpha
      }
    | _ -> defaults

  /// <summary>
  /// Reads material params from a <see cref="T:Microsoft.Xna.Framework.Graphics.ModelMeshPart"/>'s
  /// baked native effect (the content-pipeline material). Convenience over <c>fromEffect</c>.
  /// </summary>
  let fromModelMeshPart(part: ModelMeshPart) : Material3D =
    fromEffect part.Effect
