module MiboMono3DAdaptive.DesktopVK.Program

open Mibo.Adaptive
open MiboMono3DAdaptive

[<EntryPoint>]
let main _ =
  let mgProgram = MiboMono3DAdaptive.create()

  use game = new AdaptiveMonoGameGame<Frame>(mgProgram)
  game.Run()
  0
