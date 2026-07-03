module MiboMono3D.WindowsDX.Program

open System
open Mibo.Elmish
open MiboMono3D

[<EntryPoint; STAThread>]
let main _ =
  let mgProgram = MiboMono3D.create()

  use game = new MiboGame<Model, Msg>(mgProgram)
  game.Run()
  0
