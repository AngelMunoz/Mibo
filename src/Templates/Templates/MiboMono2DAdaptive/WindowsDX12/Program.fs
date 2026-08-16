module MiboMono2DAdaptive.WindowsDX12.Program

open System
open Mibo.Adaptive
open MiboMono2DAdaptive

[<EntryPoint; STAThread>]
let main _ =
  let mgProgram = MiboMono2DAdaptive.create()

  use game = new AdaptiveMonoGameGame<Frame>(mgProgram)
  game.Run()
  0
