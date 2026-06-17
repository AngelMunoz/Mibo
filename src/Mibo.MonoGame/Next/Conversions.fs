namespace Mibo.Elmish.Next

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

[<RequireQualifiedAccess>]
module Convert =

  let inline toColor(c: Color) : Graphics2D.Base.Color = {
    R = c.R
    G = c.G
    B = c.B
    A = c.A
  }

  let inline toMgColor(c: Graphics2D.Base.Color) : Color =
    Color(c.R, c.G, c.B, c.A)

  let inline toRect(r: Rectangle) : Graphics2D.Rect = {
    X = float32 r.X
    Y = float32 r.Y
    Width = float32 r.Width
    Height = float32 r.Height
  }

  let inline toMgRect(r: Graphics2D.Rect) : Rectangle =
    Rectangle(int r.X, int r.Y, int r.Width, int r.Height)

  let private multiplyBlend =
    let bs = new BlendState()
    bs.ColorSourceBlend <- Blend.DestinationColor
    bs.ColorDestinationBlend <- Blend.Zero
    bs

  let toMgBlendState(m: Graphics2D.Base.BlendMode) : BlendState =
    match m with
    | Graphics2D.Base.BlendMode.Alpha -> BlendState.AlphaBlend
    | Graphics2D.Base.BlendMode.Additive -> BlendState.Additive
    | Graphics2D.Base.BlendMode.Multiplied -> multiplyBlend
    | Graphics2D.Base.BlendMode.Opaque -> BlendState.Opaque
    | _ -> BlendState.AlphaBlend

  let inline toBlendMode(m: BlendState) =
    match m.Name with
    | "BlendState.AlphaBlend" -> Graphics2D.Base.BlendMode.Alpha
    | "BlendState.Additive" -> Graphics2D.Base.BlendMode.Additive
    | "BlendState.NonPremultiplied" -> Graphics2D.Base.BlendMode.Multiplied
    | "BlendState.Opaque" -> Graphics2D.Base.BlendMode.Opaque
    | _ -> Graphics2D.Base.BlendMode.Alpha


  let inline toMgVec2(v: System.Numerics.Vector2) : Vector2 =
    Vector2.op_Implicit v

  let inline toSysVec2(v: Vector2) : System.Numerics.Vector2 = v.ToNumerics()

  let inline toMgMatrix(m: System.Numerics.Matrix4x4) : Matrix =
    Matrix.op_Implicit m

  let inline toSysMatrix(m: Matrix) : System.Numerics.Matrix4x4 = m.ToNumerics()

  let cameraTransform(camera: Graphics2D.Camera2DState) : Matrix =
    // MonoGame / XNA 2D camera convention:
    // Translate world by -Target, scale by Zoom, rotate by -Rotation,
    // then translate by Offset (viewport center).
    let t = toMgVec2 camera.Target
    let o = toMgVec2 camera.Offset

    Matrix.CreateTranslation(-t.X, -t.Y, 0.0f)
    * Matrix.CreateRotationZ(float32 -camera.Rotation)
    * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1.0f)
    * Matrix.CreateTranslation(o.X, o.Y, 0.0f)

  let inline mgRect(x, y, w, h) = Rectangle(x, y, w, h)
