module MiboMono2D.DesktopGL.Program

open Mibo.Elmish
open MiboMono2D

[<EntryPoint>]
let main _ =
  let mgProgram = MiboMono2D.create() |> MonoGameProgram.ofProgram

  use game = new MiboGame<Model, Msg>(mgProgram)
  game.Run()
  0
