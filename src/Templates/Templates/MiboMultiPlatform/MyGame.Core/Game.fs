module MyGame.Core.Game

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

type Model = { Position: Vector2; Velocity: Vector2 }

type Msg = Tick of GameTime

let init(ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let model = {
    Position = Vector2(100.f, 100.f)
    Velocity = Vector2(150.f, 100.f)
  }

  model, Cmd.none

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let mutable velocity = model.Velocity
    let mutable position = model.Position + (velocity * dt)

    // Simple bounce (assuming some bounds, though resolution varies)
    if position.X < 0.f || position.X > 750.f then
      velocity.X <- -velocity.X

    if position.Y < 0.f || position.Y > 550.f then
      velocity.Y <- -velocity.Y

    {
      model with
          Position = position
          Velocity = velocity
    },
    Cmd.none

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  // Draw something simple
  let pixel =
    Assets.getOrCreate
      "pixel"
      (fun gd ->
        let t = new Texture2D(gd, 1, 1)
        t.SetData([| Color.White |])
        t)
      ctx

  Draw2D.sprite
    pixel
    (Rectangle(int model.Position.X, int model.Position.Y, 50, 50))
  |> Draw2D.withColor Color.Lime
  |> Draw2D.submit buffer

let program =
  Program.mkProgram init update
  |> Program.withAssets
  |> Program.withRenderer(Batch2DRenderer.create view)
  |> Program.withTick Tick
  |> Program.withConfig(fun (game, graphics) ->
    game.Content.RootDirectory <- "Content")
