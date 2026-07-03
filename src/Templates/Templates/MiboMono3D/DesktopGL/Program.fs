module MiboMono3D.DesktopGL.Program

open Mibo.Elmish
open MiboMono3D

[<EntryPoint>]
let main _ =
  let mgProgram = MiboMono3D.create()

  use game = new MiboGame<Model, Msg>(mgProgram)
  game.Run()
  0
