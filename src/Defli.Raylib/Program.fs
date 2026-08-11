module Defli.Raylib.Program

open System
open System.IO
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Defli.World

// ─────────────────────────────────────────────────────────────
// Defli — the windowed raylib frontend (milestone 2). The sim runs
// on the AdaptiveRaylibGame host: one Step per frame (input poll →
// shell phase → Router.step → force), the renderers draw the forced
// frame. The headless sim stays in src/Defli (`dotnet run --project
// src/Defli -- sim`).
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let cell = WorldCell(World.init WorldConfig.defaults)
  let shell = Shell()

  let config =
    GameConfig.defaultConfig
    |> GameConfig.withWidth 1280
    |> GameConfig.withHeight 800
    |> GameConfig.withTitle "Defli"
    |> GameConfig.withTargetFPS 60

  let vfx = VfxView()

  // The assets are copied to the output directory; resolve them from
  // the exe location so the game runs from any working directory.
  let assetsBasePath = Path.Combine(AppContext.BaseDirectory, "assets")

  let program =
    Application.windowedProgram cell shell
    |> AdaptiveProgram.withConfig(fun _ -> config)
    |> AdaptiveProgram.withAssetsBasePath assetsBasePath
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.create(WorldView.worldView shell vfx))
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear (WorldView.hudView shell))

  let game = new AdaptiveRaylibGame<Frame.RenderFrame>(program)

  game.Run()
  0
