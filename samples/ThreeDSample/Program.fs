module ThreeDSample.Program

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Animation
open Mibo.Input
open ThreeDSample.Constants
open ThreeDSample.Types
open ThreeDSample.WorldGen
open ThreeDSample.Physics

let loadInitialChunks(model: GameModel) =
  let spawnPos = spawnPosition
  let pcx = int(Math.Floor(float spawnPos.X / float chunkWorldWidth))
  let pcz = int(Math.Floor(float spawnPos.Z / float chunkWorldDepth))

  for x in pcx - chunkLoadRadius .. pcx + chunkLoadRadius do
    for z in pcz - chunkLoadRadius .. pcz + chunkLoadRadius do
      let key = struct (x, z)

      if not(model.Chunks.ContainsKey key) then
        model.Chunks[key] <- generateChunk x z model.Seed

let init(ctx: GameContext) =
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key GameAction.MoveLeft KeyboardKey.A
    |> InputMap.key GameAction.MoveLeft KeyboardKey.Left
    |> InputMap.key GameAction.MoveRight KeyboardKey.D
    |> InputMap.key GameAction.MoveRight KeyboardKey.Right
    |> InputMap.key GameAction.MoveForward KeyboardKey.W
    |> InputMap.key GameAction.MoveForward KeyboardKey.Up
    |> InputMap.key GameAction.MoveBackward KeyboardKey.S
    |> InputMap.key GameAction.MoveBackward KeyboardKey.Down
    |> InputMap.key GameAction.Jump KeyboardKey.Space
    |> InputMap.key GameAction.Respawn KeyboardKey.R
    |> InputMap.key GameAction.RotateCameraLeft KeyboardKey.Q
    |> InputMap.key GameAction.RotateCameraRight KeyboardKey.E
    |> InputMap.key GameAction.RotateCameraUp KeyboardKey.PageUp
    |> InputMap.key GameAction.RotateCameraDown KeyboardKey.PageDown

  let model = GameModel()
  model.InputMap <- inputMap
  model.Seed <- Random.Shared.Next()
  loadInitialChunks model

  let particleImg =
    Raylib.GenImageColor(1, 1, Color(255uy, 255uy, 255uy, 255uy))

  model.Particles.Texture <- Raylib.LoadTextureFromImage(particleImg)
  Raylib.UnloadImage(particleImg)

  let assets = GameContext.getService<IAssets> ctx
  model.JumpSound <- assets.Sound("assets/sfx_jump.ogg")

  let playerModel = assets.Model(KenneyModels.characterOobi)
  model.PlayerModel <- playerModel

  let animClips = assets.ModelAnimations(KenneyModels.characterOobi)
  let clips = Animation3DClips.fromModelAnimations animClips
  model.PlayerAnimClips <- clips
  model.PlayerAnim <- Animation3DState.create playerModel clips "idle" 60.0f

  let target = spawnPosition + Vector3(0.0f, playerHeight * 0.5f, 0.0f)
  model.CameraTarget <- target

  model.CameraPosition <-
    computeCameraPosition target model.CameraYaw model.CameraPitch

  struct (model, Cmd.none)

let subscribe (ctx: GameContext) (model: GameModel) =
  InputMapper.subscribeStatic model.InputMap InputMapped ctx

let overlayView (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  Minimap.view ctx model buffer
  Diagnostics.view ctx model buffer

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init Systems.update
    |> Program.withAssetsBasePath AppContext.BaseDirectory
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo 3D Platformer"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      Renderer2D.createWith Renderer2DConfig.noClear overlayView)
    |> Program.withRenderer(fun () ->
      let pipeline =
        ForwardPbrPipelineV2(
          shadowBiasConfig = {
            DirectionalBias = 0.002f
            PointBias = 0.01f
            SpotBias = 0.001f
            SlopeScaleBias = 0.001f
          },
          shadowAtlasConfig = {
            ShadowAtlasConfig.defaults with
                Resolution = 4096
                DirectionalLightSize = ValueSome 30.f
          }

        )

      Renderer3D.create pipeline View.view)

  let game = new RaylibGame<GameModel, Msg>(program)
  game.Run()
  0
