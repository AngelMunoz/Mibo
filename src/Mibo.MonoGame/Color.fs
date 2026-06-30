namespace Mibo

// ─────────────────────────────────────────────────────────────────────────────
// Mibo.Color ↔ Microsoft.Xna.Framework.Color conversions.
//
// Inlineable — the JIT reduces these to direct field loads. Used at the
// Core↔backend boundary when uploading light/camera colors to shaders.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Conversions between the backend-neutral <see cref="T:Mibo.Color"/> and MonoGame <c>Color</c>.</summary>
module MonoGameColor =

  /// <summary>Convert a <see cref="T:Mibo.Color"/> to a MonoGame <c>Color</c>.</summary>
  let inline toMonoGameColor(c: Color) : Microsoft.Xna.Framework.Color =
    Microsoft.Xna.Framework.Color(c.R, c.G, c.B, c.A)

  /// <summary>Convert a MonoGame <c>Color</c> to a <see cref="T:Mibo.Color"/>.</summary>
  let inline fromMonoGameColor(c: Microsoft.Xna.Framework.Color) : Color = {
    R = c.R
    G = c.G
    B = c.B
    A = c.A
  }

[<AutoOpen>]
module MonoGameColorExtensions =

  type Color with

    /// <summary>Implicit conversion to MonoGame <c>Color</c>.</summary>
    static member op_Implicit(c: Color) : Microsoft.Xna.Framework.Color =
      MonoGameColor.toMonoGameColor c
