module PlatformerSample.Program

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Input
open Mibo.Animation
open Mibo.Layout
open PlatformerSample.Constants
open PlatformerSample.Types
open PlatformerSample.WorldGen

// -------------------------------------------------------------
// Asset Loading
// -------------------------------------------------------------

let inline r (x: int) (y: int) (w: int) (h: int) =
  Rectangle(float32 x, float32 y, float32 w, float32 h)

let loadAssets(ctx: GameContext) : SpriteAssets =
  let assets = GameContext.getService<IAssets> ctx

  let playerTex =
    assets.Texture(
      "assets/kenney_platformer/Spritesheets/spritesheet-characters-default.png"
    )

  let tileTex =
    assets.Texture(
      "assets/kenney_platformer/Spritesheets/spritesheet-tiles-default.png"
    )

  let font = assets.Font("assets/Fonts/monogram.ttf")
  let jumpSound = assets.Sound("assets/sfx_jump.ogg")
  let coinNormalMap = assets.Texture("assets/NormalMap.png")

  // White 1x1 texture for particles (black would multiply to zero)
  let particleImg =
    Raylib.GenImageColor(1, 1, Color(255uy, 255uy, 255uy, 255uy))

  let particleTex = Raylib.LoadTextureFromImage(particleImg)
  Raylib.UnloadImage(particleImg)

  let playerSheet =
    SpriteSheet.fromFrames playerTex Vector2.Zero [|
      struct ("idle",
              {
                Frames = [| r 645 0 128 128 |]
                FrameDuration = 1.0f
                Loop = false
              })
      struct ("walk",
              {
                Frames = [| r 0 129 128 128; r 129 129 128 128 |]
                FrameDuration = 0.1f
                Loop = true
              })
      struct ("jump",
              {
                Frames = [| r 774 0 128 128 |]
                FrameDuration = 1.0f
                Loop = false
              })
      struct ("fall",
              {
                Frames = [| r 774 0 128 128 |]
                FrameDuration = 1.0f
                Loop = false
              })
    |]

  // torch_on_a (65,1105) and torch_on_b (130,1105) — 64x64 each
  let torchSheet =
    SpriteSheet.fromFrames tileTex (Vector2(32.0f, 32.0f)) [|
      struct ("lit",
              {
                Frames = [| r 65 1105 64 64; r 130 1105 64 64 |]
                FrameDuration = 0.15f
                Loop = true
              })
    |]

  {
    PlayerSheet = playerSheet
    TileTexture = tileTex
    TorchSheet = torchSheet
    ParticleTexture = particleTex
    CoinNormalMap = coinNormalMap
    Font = font
    JumpSound = jumpSound
  }

// -------------------------------------------------------------
// Init
// -------------------------------------------------------------

let init(ctx: GameContext) =
  let assets = loadAssets ctx

  let inputMap =
    InputMap.empty
    |> InputMap.key GameAction.MoveLeft KeyboardKey.A
    |> InputMap.key GameAction.MoveLeft KeyboardKey.Left
    |> InputMap.key GameAction.MoveRight KeyboardKey.D
    |> InputMap.key GameAction.MoveRight KeyboardKey.Right
    |> InputMap.key GameAction.Jump KeyboardKey.Space
    |> InputMap.key GameAction.Respawn KeyboardKey.R

  let model = new Model()
  model.InputMap <- inputMap
  model.Assets <- assets
  model.PlayerSprite <- AnimatedSprite.create assets.PlayerSheet "idle"
  model.TorchSprite <- AnimatedSprite.create assets.TorchSheet "lit"

  let seed = Random().Next()
  let spawnY = groundSurface - playerHeight

  // Pre-load spawn chunks
  let spawnChunkX = 0
  let spawnChunkY = 0

  for x in spawnChunkX - chunkLoadRadius .. spawnChunkX + chunkLoadRadius do
    for y in spawnChunkY - chunkLoadRadius .. spawnChunkY + chunkLoadRadius do
      if x >= 0 then
        model.Chunks[struct (x, y)] <- generateChunk x y seed

  model.PlayerPosition <- Vector2(spawnX, spawnY)

  model.Camera <-
    Camera2D.create
      (Vector2(spawnX, spawnY))
      1.0f
      (Vector2(viewportWidth, viewportHeight))

  model.Seed <- seed

  model.Lighting <-
    new LightContext2D(softness = 0.05f, maxShadowDistance = 2000.0f)

  struct (model, Cmd.none)

// -------------------------------------------------------------
// Subscription
// -------------------------------------------------------------

let subscribe (ctx: GameContext) (model: Model) =
  Mibo.Input.InputMapper.subscribeStatic model.InputMap InputMapped ctx

// -------------------------------------------------------------
// Entry Point
// -------------------------------------------------------------

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init Systems.update
    |> Program.withAssetsBasePath AppContext.BaseDirectory
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo Raylib Platformer"
          TargetFPS = 120
    })
    |> Program.withInput
    |> Program.withSubscription subscribe
    |> Program.withTick Tick
    |> Program.withRenderer(fun () -> Renderer2D.create View.view)

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()
  0
