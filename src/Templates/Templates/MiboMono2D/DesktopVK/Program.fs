module MiboMono2D.DesktopVK.Program

open Mibo.Elmish
open MiboMono2D

[<EntryPoint>]
let main _ =
  let mgProgram = MiboMono2D.create()

  use game = new MiboGame<Model, Msg>(mgProgram)
  game.Run()
  0
