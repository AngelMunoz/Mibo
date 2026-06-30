namespace Mibo

open System

// ─────────────────────────────────────────────────────────────────────────────
// Backend-neutral Color type.
//
// A simple byte RGBA color struct shared across all backends. Both raylib and
// MonoGame define their own Color structs with the same R/G/B/A byte layout —
// this type lets shared code (light definitions, camera configs, etc.) express
// colors without referencing either backend.
//
// Each backend provides inlineable conversion helpers (op_Implicit / explicit)
// to/from its native Color type at the Core↔backend boundary.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A 32-bit RGBA color (8 bits per channel). Backend-neutral — converts to
/// raylib <c>Color</c> or MonoGame <c>Color</c> at the boundary.
/// </summary>
[<Struct; CustomEquality; CustomComparison>]
type Color = {
  /// <summary>Red channel (0-255).</summary>
  R: byte
  /// <summary>Green channel (0-255).</summary>
  G: byte
  /// <summary>Blue channel (0-255).</summary>
  B: byte
  /// <summary>Alpha channel (0-255).</summary>
  A: byte
} with

  override this.GetHashCode() =
    HashCode.Combine(this.R, this.G, this.B, this.A)

  override this.Equals(other: obj) =
    match other with
    | :? Color as c ->
      this.R = c.R && this.G = c.G && this.B = c.B && this.A = c.A
    | _ -> false

  interface IComparable with
    member this.CompareTo(other: obj) =
      match other with
      | :? Color as c ->
        if this.R <> c.R then compare this.R c.R
        elif this.G <> c.G then compare this.G c.G
        elif this.B <> c.B then compare this.B c.B
        else compare this.A c.A
      | _ -> -1

/// <summary>Convenience constructors and conversions for <see cref="T:Mibo.Color"/>.</summary>
module Color =

  /// <summary>Create a color from RGBA byte values.</summary>
  let inline create (r: byte) (g: byte) (b: byte) (a: byte) : Color = {
    R = r
    G = g
    B = b
    A = a
  }

  /// <summary>Create an opaque color from RGB byte values (alpha = 255).</summary>
  let inline rgb (r: byte) (g: byte) (b: byte) : Color = create r g b 255uy

  /// <summary>White (255, 255, 255, 255).</summary>
  let White: Color = rgb 255uy 255uy 255uy

  /// <summary>Black (0, 0, 0, 255).</summary>
  let Black: Color = rgb 0uy 0uy 0uy

  /// <summary>Red (255, 0, 0, 255).</summary>
  let Red: Color = rgb 255uy 0uy 0uy

  /// <summary>Green (0, 255, 0, 255).</summary>
  let Green: Color = rgb 0uy 255uy 0uy

  /// <summary>Blue (0, 0, 255, 255).</summary>
  let Blue: Color = rgb 0uy 0uy 255uy

  /// <summary>Transparent (0, 0, 0, 0).</summary>
  let Transparent: Color = create 0uy 0uy 0uy 0uy

  /// <summary>Convert a color to a normalized Vector3 (RGB in [0,1], alpha dropped).</summary>
  let inline toVector3(c: Color) : System.Numerics.Vector3 =
    System.Numerics.Vector3(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f
    )

  /// <summary>Convert a color to a normalized Vector4 (RGBA in [0,1]).</summary>
  let inline toVector4(c: Color) : System.Numerics.Vector4 =
    System.Numerics.Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )
