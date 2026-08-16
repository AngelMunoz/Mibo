module MiboMono3DAdaptive.WindowsDX12.Program

open System
open Mibo.Adaptive
open MiboMono3DAdaptive

[<EntryPoint; STAThread>]
let main _ =
  let mgProgram = MiboMono3DAdaptive.create()

  use game = new AdaptiveMonoGameGame<Frame>(mgProgram)
  game.Run()
  0
