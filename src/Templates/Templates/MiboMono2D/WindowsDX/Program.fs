module MiboMono2D.WindowsDX.Program

open System
open Mibo.Elmish
open MiboMono2D

[<EntryPoint; STAThread>]
let main _ =
  let mgProgram = MiboMono2D.create() |> MonoGameProgram.ofProgram

  use game = new MiboGame<Model, Msg>(mgProgram)
  game.Run()
  0
