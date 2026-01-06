namespace Gamino.Elmish

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX

// --- 2D Rendering Implementation ---

[<Struct>]
type RenderCmd2D =
    | DrawTexture of
        texture: Texture2D *
        dest: Rectangle *
        source: Nullable<Rectangle> *
        color: Color *
        rotation: float32 *
        origin: Vector2 *
        effects: SpriteEffects *
        depth: float32

/// Standard 2D Renderer using SpriteBatch
type Batch2DRenderer<'Model>(game: Game, [<InlineIfLambda>] view: 'Model -> RenderBuffer<RenderCmd2D> -> unit) =
    let mutable spriteBatch: SpriteBatch = null
    let buffer = RenderBuffer<RenderCmd2D>()

    interface IRenderer<'Model> with
        member _.Draw (model: 'Model) (gameTime: GameTime) =
            if isNull spriteBatch then
                spriteBatch <- new SpriteBatch(game.GraphicsDevice)

            game.GraphicsDevice.Clear Color.CornflowerBlue
            buffer.Clear()
            view model buffer
            buffer.Sort()
            spriteBatch.Begin()

            for i = 0 to buffer.Count - 1 do
                let struct (_, cmd) = buffer.Item(i)

                match cmd with
                | DrawTexture(tex, dest, src, color, rot, origin, fx, depth) ->
                    if src.HasValue then
                        spriteBatch.Draw(tex, dest, src.Value, color, rot, origin, fx, depth)
                    else
                        spriteBatch.Draw(tex, dest, color)

            spriteBatch.End()

module Batch2DRenderer =

    let inline create<'Model> ([<InlineIfLambda>] view: 'Model -> RenderBuffer<RenderCmd2D> -> unit) (game: Game) =
        Batch2DRenderer<'Model>(game, view) :> IRenderer<'Model>

// --- Fluent Builder API ---

[<Struct>]
type Draw2DBuilder =
    { Texture: Texture2D
      Dest: Rectangle
      Source: Nullable<Rectangle>
      Color: Color
      Rotation: float32
      Origin: Vector2
      Effects: SpriteEffects
      Depth: float32
      Layer: int<RenderLayer> }

module Draw2D =
    let sprite tex dest =
        { Texture = tex
          Dest = dest
          Source = Nullable()
          Color = Color.White
          Rotation = 0.0f
          Origin = Vector2.Zero
          Effects = SpriteEffects.None
          Depth = 0.0f
          Layer = 0<RenderLayer> }

    let withSource src (b: Draw2DBuilder) = { b with Source = Nullable src }
    let withColor col (b: Draw2DBuilder) = { b with Color = col }
    let atLayer layer (b: Draw2DBuilder) = { b with Layer = layer }

    let submit (buffer: RenderBuffer<RenderCmd2D>) (b: Draw2DBuilder) =
        buffer.Add(b.Layer, DrawTexture(b.Texture, b.Dest, b.Source, b.Color, b.Rotation, b.Origin, b.Effects, b.Depth))
