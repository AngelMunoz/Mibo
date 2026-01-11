module MyGame.iOS.Program

open System
open Foundation
open UIKit
open MyGame.Core.Game
open Mibo.Elmish

[<Register("AppDelegate")>]
type AppDelegate() =
  inherit UIApplicationDelegate()

  override this.FinishedLaunching(app, options) =
    let program =
      program
      |> Program.withConfig(fun (game, graphics) ->
        graphics.SupportedOrientations <-
          DisplayOrientation.LandscapeLeft
          ||| DisplayOrientation.LandscapeRight
          ||| DisplayOrientation.Portrait)

    let game = new ElmishGame<Model, Msg>(program)
    game.Run()
    true

[<EntryPoint>]
let main args =
  UIApplication.Main(args, null, typeof<AppDelegate>)
  0
