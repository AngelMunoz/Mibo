namespace Mibo.Elmish

open Raylib_cs

/// <summary>
/// Pipe-friendly helpers for configuring a loaded <c>Texture2D</c>.
/// </summary>
/// <remarks>
/// raylib's texture filter is a property of the texture itself, set with
/// <c>Raylib.SetTextureFilter</c>. The Mibo loader (<c>IAssets.Texture</c>)
/// generates mipmaps and forces <c>Trilinear</c> filtering on every texture at
/// load time — good for 3D/PBR surfaces, but it makes tiles sampled from a
/// gutterless spritesheet bleed at the edges. Use these helpers to override
/// that per texture:
/// <code>
/// let assets = GameContext.getService&lt;IAssets&gt; ctx
/// let atlas = assets.Texture "tiles.png" |> Texture.filter TextureFilter.Point
/// </code>
/// Apply once (e.g. in <c>init</c>) — not every frame — since it mutates the
/// cached texture's sampler.
/// </remarks>
module Texture =

  /// <summary>
  /// Sets the texture's filtering mode, overriding the load-time default
  /// (mipmaps + <c>Trilinear</c>).
  /// </summary>
  /// <param name="filterMode">The raylib <c>TextureFilter</c> to apply.</param>
  /// <param name="tex">The texture to configure (returned for piping).</param>
  /// <remarks>
  /// <c>Point</c> (nearest) filtering reads exact texels — use it on a tile
  /// atlas to stop adjacent tiles from bleeding into each other. The texture
  /// already has mipmaps generated on load; <c>Point</c>/<c>Bilinear</c> simply
  /// ignore them.
  /// </remarks>
  let inline filter (filterMode: TextureFilter) (tex: Texture2D) =
    Raylib.SetTextureFilter(tex, filterMode)
    tex

  /// <summary>
  /// Sets the texture's wrap (addressing) mode — how sampling handles texture
  /// coordinates outside the <c>[0, 1]</c> range.
  /// </summary>
  /// <param name="wrapMode">The raylib <c>TextureWrap</c> to apply
  /// (<c>Clamp</c>, <c>Repeat</c>, <c>MirrorClamp</c>, <c>MirrorRepeat</c>).</param>
  /// <param name="tex">The texture to configure (returned for piping).</param>
  /// <remarks>
  /// Use <c>Repeat</c>/<c>MirrorRepeat</c> for a tiling background; <c>Clamp</c>
  /// stops edge texels from wrapping. Like <c>filter</c>, this is a per-texture
  /// sampler property.
  /// </remarks>
  let inline wrap (wrapMode: TextureWrap) (tex: Texture2D) =
    Raylib.SetTextureWrap(tex, wrapMode)
    tex

  /// <summary>
  /// Generates mipmaps for the texture (the Mibo loader already does this on
  /// load, so this is mainly useful for textures loaded outside the loader).
  /// </summary>
  /// <param name="tex">The texture; the updated struct (new mipmap count) is
  /// returned for piping.</param>
  /// <remarks>
  /// raylib-cs mutates the texture byref and writes the mipmap count back into
  /// the same struct, so the returned texture must be used.
  /// </remarks>
  let inline mipmaps(tex: Texture2D) =
    let mutable t = tex
    Raylib.GenTextureMipmaps(&t) |> ignore
    t
