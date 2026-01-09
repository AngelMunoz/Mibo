module MyGame.Core.Game

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

type Model = unit
type Msg = Tick of GameTime

let init (ctx: GameContext) = struct ((), Cmd.none)

let update (msg: Msg) (model: Model) = struct (model, Cmd.none)

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
    // Draw something simple
    let pixel = Assets.getOrCreate "pixel" (fun gd ->
        let t = new Texture2D(gd, 1, 1)
        t.SetData([| Color.White |])
        t
    ) ctx

    Draw2D.sprite pixel (Rectangle(100, 100, 50, 50))
    |> Draw2D.withColor Color.Lime
    |> Draw2D.submit buffer

let program =
    Program.mkProgram init update
    |> Program.withAssets
    |> Program.withRenderer (Batch2DRenderer.create view)
    |> Program.withTick Tick
