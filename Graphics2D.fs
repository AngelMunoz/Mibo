namespace Gamino.Elmish

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Gamino.Elmish

// --- Render Optimization (Standard 2D Implementation) ---

/// Defines the drawing order. Lower numbers draw first.
type RenderLayer =
    | Background = 0
    | World = 1
    | Particles = 2
    | UI = 3

/// A data-only command for rendering.
[<Struct>]
type RenderCmd =
    | DrawTexture of
        texture: Texture2D *
        dest: Rectangle *
        source: Nullable<Rectangle> *
        color: Color *
        rotation: float32 *
        origin: Vector2 *
        effects: SpriteEffects *
        depth: float32

/// A flat command buffer.
type RenderBuffer() =
    static let LayerCount = 4
    let buckets = Array.init LayerCount (fun _ -> ResizeArray<RenderCmd>(1024))

    member _.Clear() =
        for b in buckets do
            b.Clear()

    member _.Add(layer: RenderLayer, cmd: RenderCmd) = buckets[int layer].Add(cmd)

    member _.Execute(sb: SpriteBatch) =
        for b in buckets do
            let count = b.Count

            for i = 0 to count - 1 do
                let cmd = b[i]

                match cmd with
                | DrawTexture(tex, dest, src, color, rot, origin, fx, depth) ->
                    if src.HasValue then
                        sb.Draw(tex, dest, src.Value, color, rot, origin, fx, depth)
                    else
                        sb.Draw(tex, dest, color)

/// Builder API for Ergonomics
module Render =
    let draw (texture: Texture2D) (dest: Rectangle) (color: Color) (layer: RenderLayer) (buffer: RenderBuffer) =
        buffer.Add(layer, DrawTexture(texture, dest, Nullable(), color, 0.0f, Vector2.Zero, SpriteEffects.None, 0.0f))
        buffer

    let drawEx
        (texture: Texture2D)
        (dest: Rectangle)
        (src: Nullable<Rectangle>)
        (color: Color)
        (rot: float32)
        (origin: Vector2)
        (layer: RenderLayer)
        (buffer: RenderBuffer)
        =
        buffer.Add(layer, DrawTexture(texture, dest, src, color, rot, origin, SpriteEffects.None, 0.0f))
        buffer

/// The standard 2D Renderer implementation
type Batch2DRenderer<'Model>(game: Game, [<InlineIfLambda>] view: 'Model -> RenderBuffer -> unit) =
    let mutable spriteBatch: SpriteBatch = null
    let renderBuffer = RenderBuffer()

    interface IRenderer<'Model> with
        member _.Draw (model: 'Model) (gameTime: GameTime) =
            game.GraphicsDevice.Clear Color.CornflowerBlue

            if isNull spriteBatch then
                spriteBatch <- new SpriteBatch(game.GraphicsDevice)

            renderBuffer.Clear()
            view model renderBuffer

            spriteBatch.Begin()
            renderBuffer.Execute spriteBatch
            spriteBatch.End()

module Batch2DRenderer =
    let inline create<'Model> ([<InlineIfLambda>] view: 'Model -> RenderBuffer -> unit) (game: Game) =
        Batch2DRenderer<'Model>(game, view) :> IRenderer<'Model>
