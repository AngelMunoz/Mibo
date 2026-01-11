module MyGame.Desktop.Program

open Mibo.Elmish
open MyGame.Core.Game

[<EntryPoint>]
let main _ =
  // Configure window here if needed, or pass config to program in Core
  let program =
    program
    |> Program.withConfig(fun (game, graphics) ->
      game.IsMouseVisible <- true
      game.Window.Title <- "MyGame Desktop"
      graphics.PreferredBackBufferWidth <- 800
      graphics.PreferredBackBufferHeight <- 600)

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
