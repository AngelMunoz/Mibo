namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>Coarse rendering pass selection for 3D.</summary>
/// <remarks>
/// <para><see cref="F:Mibo.Elmish.Graphics3D.RenderPass.Opaque"/>: depth testing + opaque blending. Can be depth-sorted for performance.</para>
/// <para><see cref="F:Mibo.Elmish.Graphics3D.RenderPass.Transparent"/>: typically depth read + alpha blending. Must be sorted back-to-front.</para>
/// </remarks>
[<Struct>]
type RenderPass =
  | Opaque
  | Transparent

/// <summary>Standard transformation matrices used during effect setup.</summary>
[<Struct>]
type EffectContext = {
  World: Matrix
  View: Matrix
  Projection: Matrix
}

/// <summary>Callback for configuring an effect before a draw operation.</summary>
type EffectSetup = Effect -> EffectContext -> unit

/// <summary>UV rectangle in normalized texture coordinates.</summary>
[<Struct>]
type UvRect = {
  U0: float32
  V0: float32
  U1: float32
  V1: float32
}

module UvRect =
  let full: UvRect = {
    U0 = 0.0f
    V0 = 0.0f
    U1 = 1.0f
    V1 = 1.0f
  }

/// <summary>Billboard facing mode.</summary>
[<Struct>]
type BillboardMode =
  /// Faces the camera fully.
  | Spherical
  /// Rotates around a fixed up axis (good for trees).
  | Cylindrical of upAxis: Vector3

/// <summary>A textured quad in 3D space, represented as center + basis half-extents.</summary>
[<Struct>]
type Quad3D = {
  Center: Vector3
  Right: Vector3
  Up: Vector3
  Color: Color
  Uv: UvRect
}

/// <summary>A billboard (camera-facing quad) in 3D space.</summary>
[<Struct>]
type Billboard3D = {
  Position: Vector3
  Size: Vector2
  Rotation: float32
  Color: Color
  Uv: UvRect
  Mode: BillboardMode
}

/// <summary>Sprite-style quad draw (90% case): unlit textured quad using built-in sprite effect.</summary>
[<Struct>]
type SpriteQuadCmd = {
  Pass: RenderPass
  Texture: Texture2D
  Quad: Quad3D
}

/// <summary>Sprite-style billboard draw (90% case): unlit textured billboard using built-in sprite effect.</summary>
[<Struct>]
type SpriteBillboardCmd = {
  Pass: RenderPass
  Texture: Texture2D
  Billboard: Billboard3D
}

/// <summary>Effect-driven quad draw: user provides an effect and optional setup callback.</summary>
[<Struct>]
type EffectQuadCmd = {
  Pass: RenderPass
  Effect: Effect
  Setup: EffectSetup voption
  Quad: Quad3D
}

/// <summary>Effect-driven billboard draw: user provides an effect and optional setup callback.</summary>
[<Struct>]
type EffectBillboardCmd = {
  Pass: RenderPass
  Effect: Effect
  Setup: EffectSetup voption
  Billboard: Billboard3D
}
