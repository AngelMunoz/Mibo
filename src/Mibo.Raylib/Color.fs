namespace Mibo

open System.Runtime.CompilerServices

// ─────────────────────────────────────────────────────────────────────────────
// Mibo.Color ↔ Raylib_cs.Color conversions.
//
// Inlineable — the JIT reduces these to direct field loads. Used at the
// Core↔backend boundary when uploading light/camera colors to shaders.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Conversions between the backend-neutral <see cref="T:Mibo.Color"/> and raylib <c>Color</c>.</summary>
module RaylibColor =

  /// <summary>Convert a <see cref="T:Mibo.Color"/> to a raylib <c>Color</c>.</summary>
  let inline toRaylibColor(c: Color) : Raylib_cs.Color =
    Raylib_cs.Color(c.R, c.G, c.B, c.A)

  /// <summary>Convert a raylib <c>Color</c> to a <see cref="T:Mibo.Color"/>.</summary>
  let inline fromRaylibColor(c: Raylib_cs.Color) : Color = {
    R = c.R
    G = c.G
    B = c.B
    A = c.A
  }

[<AutoOpen>]
module RaylibColorExtensions =

  type Color with

    /// <summary>Implicit conversion to raylib <c>Color</c>.</summary>
    static member op_Implicit(c: Color) : Raylib_cs.Color =
      RaylibColor.toRaylibColor c

  type Raylib_cs.Color with

    /// <summary>Convert this raylib <c>Color</c> to a backend-neutral <see cref="T:Mibo.Color"/>.</summary>
    member c.ToMiboColor() : Color = RaylibColor.fromRaylibColor c
