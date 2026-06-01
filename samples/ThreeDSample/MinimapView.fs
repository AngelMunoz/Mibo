module ThreeDSample.Minimap

#nowarn "9"

open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Layout3D
open ThreeDSample.Constants
open ThreeDSample.Types

// ── Constants ──

[<Literal>]
let minimapSize = 200.0f

[<Literal>]
let minimapMargin = 10.0f

[<Literal>]
let minimapWorldRadius = 40.0f

[<Literal>]
let sampleStep = 2

[<Literal>]
let updateInterval = 4

[<Literal>]
let private texSize = 200

// ── Helpers ──

let private blockColor (fallbackColor: Color) (blockType: BlockType) : Color =
  match blockType with
  | Ground
  | GroundSlopeXPos
  | GroundSlopeXNeg
  | GroundSlopeZPos
  | GroundSlopeZNeg -> Color(76uy, 153uy, 0uy)
  | Platform
  | PlatformRamp -> Color(100uy, 100uy, 100uy)
  | SnowGround
  | SnowSlopeXPos
  | SnowSlopeXNeg
  | SnowSlopeZPos
  | SnowSlopeZNeg -> Color(230uy, 230uy, 230uy)
  | TreePine
  | TreeSnow -> Color(0uy, 100uy, 0uy)
  | Rock -> Color(128uy, 128uy, 128uy)
  | GrassTuft -> Color(50uy, 120uy, 50uy)
  | Coin -> Color(255uy, 215uy, 0uy)
  | Jewel -> Color(0uy, 191uy, 255uy)
  | Heart -> Color(255uy, 0uy, 0uy)
  | Star -> Color(255uy, 255uy, 0uy)
  | Mushrooms
  | MushroomLight -> Color(139uy, 69uy, 19uy)
  | Crate -> Color(160uy, 82uy, 45uy)
  | Barrel -> Color(139uy, 90uy, 43uy)
  | Flag -> Color(255uy, 0uy, 0uy)
  | Spikes -> Color(192uy, 192uy, 192uy)
  | Empty -> fallbackColor

// ── System ──

let private collectBlocks
  (playerPos: Vector3)
  (bounds: BoundingBox)
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (blocks: Dictionary<struct (int * int), struct (float32 * BlockType)>)
  : unit =
  blocks.Clear()

  for KeyValue(struct (_cx, _cz), chunk) in chunks do
    if
      chunk.Bounds.Max.X >= bounds.Min.X
      && chunk.Bounds.Min.X <= bounds.Max.X
      && chunk.Bounds.Max.Z >= bounds.Min.Z
      && chunk.Bounds.Min.Z <= bounds.Max.Z
    then
      CellGrid3D.iterVolume
        bounds
        (fun x y z blockType ->
          if blockType <> Empty then
            let worldX =
              chunk.Grid.Origin.X + float32 x * chunk.Grid.CellSize.X

            let worldZ =
              chunk.Grid.Origin.Z + float32 z * chunk.Grid.CellSize.Z

            let worldY =
              chunk.Grid.Origin.Y + float32 y * chunk.Grid.CellSize.Y

            let qx = int(worldX) / sampleStep * sampleStep
            let qz = int(worldZ) / sampleStep * sampleStep
            let key = struct (qx, qz)

            match blocks.TryGetValue key with
            | true, struct (existingY, _) when existingY >= worldY -> ()
            | _ -> blocks[key] <- struct (worldY, blockType))
        chunk.Grid

let private generateImage
  (playerPos: Vector3)
  (timeOfDay: float32)
  (scale: float32)
  (blocks: Dictionary<struct (int * int), struct (float32 * BlockType)>)
  : Image =
  let halfMinimap = minimapSize * 0.5f
  let bgColor = Color(20uy, 20uy, 40uy, 200uy)
  let skyColor = DayNight.getSkyColor timeOfDay
  let pixelSize = float32 sampleStep * scale + 1.0f
  let pixelSizeI = max 1 (int pixelSize)

  let mutable img = Raylib.GenImageColor(texSize, texSize, bgColor)
  use imgPin = fixed &img

  for KeyValue(struct (wx, wz), struct (_, blockType)) in blocks do
    let relX = (float32 wx - playerPos.X) * scale
    let relZ = (float32 wz - playerPos.Z) * scale
    let pixelX = int(halfMinimap + relX)
    let pixelZ = int(halfMinimap + relZ)
    let color = blockColor skyColor blockType

    if color.A > 0uy then
      Raylib.ImageDrawRectangle(
        imgPin,
        pixelX,
        pixelZ,
        pixelSizeI,
        pixelSizeI,
        color
      )

  img

let uploadTexture (image: Image) (model: inref<MinimapModel>) : unit =
  if model.TexReady then
    Raylib.UpdateTexture(
      model.Texture,
      NativePtr.toVoidPtr(NativePtr.ofVoidPtr<byte> image.Data)
    )
  else
    model.Texture <- Raylib.LoadTextureFromImage(image)
    model.TexReady <- true

  Raylib.UnloadImage(image)

let generateMinimapImage
  (playerPos: Vector3)
  (timeOfDay: float32)
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  : Image =
  let scale = minimapSize / (minimapWorldRadius * 2.0f)

  let bounds = {
    Min =
      Vector3(
        playerPos.X - minimapWorldRadius,
        -100.0f,
        playerPos.Z - minimapWorldRadius
      )
    Max =
      Vector3(
        playerPos.X + minimapWorldRadius,
        100.0f,
        playerPos.Z + minimapWorldRadius
      )
  }

  let blocks = Dictionary<struct (int * int), struct (float32 * BlockType)>()
  collectBlocks playerPos bounds chunks blocks
  generateImage playerPos timeOfDay scale blocks


// ── View ──

let private viewInner
  (ctx: GameContext)
  (minimap: MinimapModel)
  (playerPos: Vector3)
  (playerFacing: float32)
  (buffer: RenderBuffer2D)
  =
  let screenWidth = float32 ctx.WindowWidth
  let screenHeight = float32 ctx.WindowHeight

  let minimapX = screenWidth - minimapSize - minimapMargin
  let minimapY = screenHeight - minimapSize - minimapMargin
  let halfMinimap = minimapSize * 0.5f

  if minimap.TexReady then
    buffer
    |> Draw.sprite(
      SpriteState.create(
        minimap.Texture,
        Rectangle(minimapX, minimapY, minimapSize, minimapSize),
        Rectangle(0.0f, 0.0f, float32 texSize, float32 texSize)
      )
      |> SpriteState.withLayer 100<RenderLayer>
    )
    |> Draw.drop

  let centerX = minimapX + halfMinimap
  let centerY = minimapY + halfMinimap
  let facingX = sin playerFacing
  let facingZ = cos playerFacing

  buffer
  |> Draw.fillCircle
    (102<RenderLayer>, Color.Yellow)
    (Vector2(centerX, centerY), 3.0f)
  |> Draw.lineThick
    (102<RenderLayer>, Color.Yellow, 2.0f)
    (Vector2(centerX, centerY),
     Vector2(centerX + facingX * 10.0f, centerY + facingZ * 10.0f))
  |> Draw.rectOutline
    (103<RenderLayer>, Color.White, 2.0f)
    (Rectangle(minimapX, minimapY, minimapSize, minimapSize))
  |> Draw.drop

let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  viewInner ctx model.Minimap model.PlayerPosition model.PlayerFacing buffer
