namespace Mibo.Elmish.Next.Graphics2D

open System.Numerics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

[<Struct>]
type SpriteState = {
  Texture: Texture2D
  Dest: Rectangle
  Source: Rectangle
  Origin: Vector2
  Rotation: float32
  Color: Color
  Layer: int<RenderLayer>
  NormalMap: Texture2D voption
}

module SpriteState =

  let create
    (texture: Texture2D, dest: Rectangle, source: Rectangle)
    : SpriteState =
    {
      Texture = texture
      Dest = dest
      Source = source
      Origin = Vector2.Zero
      Rotation = 0.0f
      Color = Microsoft.Xna.Framework.Color.White
      Layer = 0<RenderLayer>
      NormalMap = ValueNone
    }

  let inline withOrigin (v: Vector2) (s: SpriteState) = { s with Origin = v }

  let inline withRotation (v: float32) (s: SpriteState) = {
    s with
        Rotation = v
  }

  let inline withColor (v: Color) (s: SpriteState) = { s with Color = v }

  let inline withLayer (v: int<RenderLayer>) (s: SpriteState) = {
    s with
        Layer = v
  }

  let inline withNormalMap (v: Texture2D) (s: SpriteState) = {
    s with
        NormalMap = ValueSome v
  }

[<Struct>]
type TextState = {
  Font: SpriteFont
  Text: string
  Position: Vector2
  FontSize: float32
  Spacing: float32
  Color: Color
  Layer: int<RenderLayer>
}

module TextState =

  let create(font: SpriteFont, text: string, position: Vector2) : TextState = {
    Font = font
    Text = text
    Position = position
    FontSize = 20.0f
    Spacing = 1.0f
    Color = Microsoft.Xna.Framework.Color.White
    Layer = 0<RenderLayer>
  }

  let inline withFontSize (v: float32) (s: TextState) = { s with FontSize = v }
  let inline withSpacing (v: float32) (s: TextState) = { s with Spacing = v }
  let inline withColor (v: Color) (s: TextState) = { s with Color = v }

  let inline withLayer (v: int<RenderLayer>) (s: TextState) = {
    s with
        Layer = v
  }

[<Struct>]
type Particle2D = {
  Position: Vector2
  Size: Vector2
  Rotation: float32
  SourceRect: Rectangle
  Color: Color
}

[<Struct>]
type AmbientLight2D = { Color: Color }

[<Struct>]
type DirectionalLight2D = {
  Direction: Vector2
  Color: Color
  Intensity: float32
  CastsShadows: bool
}

[<Struct>]
type PointLight2D = {
  Position: Vector2
  Color: Color
  Intensity: float32
  Radius: float32
  Falloff: float32
  CastsShadows: bool
}

[<Struct>]
type Occluder2D = { P1: Vector2; P2: Vector2 }
