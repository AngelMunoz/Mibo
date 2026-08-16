module MiboMono2DAdaptive.DesktopVK.Program

open Mibo.Adaptive
open MiboMono2DAdaptive

[<EntryPoint>]
let main _ =
  let mgProgram = MiboMono2DAdaptive.create()

  use game = new AdaptiveMonoGameGame<Frame>(mgProgram)
  game.Run()
  0
