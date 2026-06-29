namespace Mibo.Elmish.Graphics3D

open System.Numerics

/// <summary>Internal conversions between System.Numerics and Microsoft.Xna.Framework types.</summary>
/// <remarks>
/// Convert only at the Core↔backend boundary. Keep internal rendering code in XNA types end-to-end.
/// </remarks>
module internal Conversions =
  let inline toNumericsVector2(v: Microsoft.Xna.Framework.Vector2) : Vector2 =
    Vector2(v.X, v.Y)

  let inline fromNumericsVector2(v: Vector2) : Microsoft.Xna.Framework.Vector2 =
    Microsoft.Xna.Framework.Vector2(v.X, v.Y)

  let inline toNumericsVector3(v: Microsoft.Xna.Framework.Vector3) : Vector3 =
    Vector3(v.X, v.Y, v.Z)

  let inline fromNumericsVector3(v: Vector3) : Microsoft.Xna.Framework.Vector3 =
    Microsoft.Xna.Framework.Vector3(v.X, v.Y, v.Z)

  let inline toNumericsVector4(v: Microsoft.Xna.Framework.Vector4) : Vector4 =
    Vector4(v.X, v.Y, v.Z, v.W)

  let inline fromNumericsVector4(v: Vector4) : Microsoft.Xna.Framework.Vector4 =
    Microsoft.Xna.Framework.Vector4(v.X, v.Y, v.Z, v.W)

  let inline toNumericsMatrix(m: Microsoft.Xna.Framework.Matrix) : Matrix4x4 =
    m.ToNumerics()

  let inline fromNumericsMatrix(m: Matrix4x4) : Microsoft.Xna.Framework.Matrix =
    Microsoft.Xna.Framework.Matrix.op_Implicit(m)

  let inline toNumericsQuaternion
    (q: Microsoft.Xna.Framework.Quaternion)
    : Quaternion =
    Quaternion(q.X, q.Y, q.Z, q.W)

  let inline fromNumericsQuaternion
    (q: Quaternion)
    : Microsoft.Xna.Framework.Quaternion =
    Microsoft.Xna.Framework.Quaternion(q.X, q.Y, q.Z, q.W)

  let inline toNumericsColor(c: Microsoft.Xna.Framework.Color) : Vector4 =
    Vector4(
      float32 c.R / 255.0f,
      float32 c.G / 255.0f,
      float32 c.B / 255.0f,
      float32 c.A / 255.0f
    )

  let inline fromNumericsColor(v: Vector4) : Microsoft.Xna.Framework.Color =
    let clamp(x: float32) =
      if x > 1.0f then 1.0f
      elif x < 0.0f then 0.0f
      else x

    Microsoft.Xna.Framework.Color(
      byte(clamp v.X * 255.0f),
      byte(clamp v.Y * 255.0f),
      byte(clamp v.Z * 255.0f),
      byte(clamp v.W * 255.0f)
    )
