module MyGame.WindowsDX.Program

open System
open MyGame.Core.Game
open Mibo.Elmish

[<EntryPoint>]
[<STAThread>]
let main _ =
  let program =
    program
    |> Program.withConfig(fun (game, graphics) ->
      game.IsMouseVisible <- true
      game.Window.Title <- "MyGame Windows"
      graphics.PreferredBackBufferWidth <- 800
      graphics.PreferredBackBufferHeight <- 600)

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
