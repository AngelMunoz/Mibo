module MyGame.WindowsDX.Program

open System
open MyGame.Core.Game
open Mibo.Elmish

[<EntryPoint>]
[<STAThread>]
let main _ =
    let program = 
        program 
        |> Program.withConfig (fun (game, graphics) ->
            game.Window.Title <- "Mibo WindowsDX Game"
            graphics.PreferredBackBufferWidth <- 800
            graphics.PreferredBackBufferHeight <- 600
            game.IsMouseVisible <- true
        )

    use game = new ElmishGame<Model, Msg>(program)
    game.Run()
    0
