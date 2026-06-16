module PlatformerSample.Minimap

#nowarn "9"

open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Mibo.Elmish
open Mibo.Layout
open PlatformerSample.Constants
open PlatformerSample.DayNight
open Mibo.Elmish.Next.Graphics2D
open Raylib_cs
open PlatformerSample.Types

// ── Constants ──

[<Literal>]
let minimapSize = 200.0f

[<Literal>]
let minimapMargin = 10.0f

[<Literal>]
let minimapWorldRadius = 400.0f

[<Literal>]
let updateInterval = 4

[<Literal>]
let private texSize = 200

// ── Helpers ──

let private tileColor
  (skyColor: Color)
  (biome: Biome)
  (tile: TileType)
  : Color =
  match tile with
  | Ground ->
    match biome with
    | Grass -> Color(76uy, 153uy, 0uy)
    | Stone -> Color(128uy, 128uy, 128uy)
    | Snow -> Color(230uy, 230uy, 230uy)
    | Sand -> Color(210uy, 180uy, 140uy)
  | Platform -> Color(139uy, 90uy, 43uy)
  | Spikes -> Color(192uy, 192uy, 192uy)
  | Coin -> Color(255uy, 215uy, 0uy)
  | Flag -> Color(255uy, 0uy, 0uy)
  | Empty -> skyColor

// ── System ──

let generateMinimapImage
  (chunks: ConcurrentDictionary<struct (int * int), Chunk>)
  (timeOfDay: float32)
  (playerPos: Vector2)
  : Image =
  let scale = minimapSize / (minimapWorldRadius * 2.0f)

  let blocks =
    Dictionary<struct (int * int), struct (float32 * TileType * Biome)>()

  let halfWorld = minimapWorldRadius

  for KeyValue(struct (_cx, _cy), chunk) in chunks do
    if
      chunk.Bounds.X + chunk.Bounds.Width >= playerPos.X - halfWorld
      && chunk.Bounds.X <= playerPos.X + halfWorld
      && chunk.Bounds.Y + chunk.Bounds.Height >= playerPos.Y - halfWorld
      && chunk.Bounds.Y <= playerPos.Y + halfWorld
    then
      let cellW = chunk.Grid.CellSize.X
      let cellH = chunk.Grid.CellSize.Y
      let chunkBiome = chunk.Biome

      for y in 0 .. chunk.Grid.Height - 1 do
        for x in 0 .. chunk.Grid.Width - 1 do
          match CellGrid2D.get x y chunk.Grid with
          | ValueSome tile when tile <> Empty ->
            let wx = chunk.Grid.Origin.X + float32 x * cellW
            let wy = chunk.Grid.Origin.Y + float32 y * cellH
            let qx = int wx
            let qz = int wy
            let key = struct (qx, qz)

            if not(blocks.ContainsKey key) then
              blocks[key] <- struct (wy, tile, chunkBiome)
          | _ -> ()

  let skyTop, _skyBot = DayNight.getSkyColors timeOfDay
  let halfMinimap = minimapSize * 0.5f
  let pixelSize = tileSize * scale + 1.0f
  let pixelSizeI = max 1 (int pixelSize)

  let mutable img = Raylib.GenImageColor(texSize, texSize, skyTop)
  use imgPin = fixed &img

  for KeyValue(struct (wx, wz), struct (_, tile, biome)) in blocks do
    let relX = (float32 wx - playerPos.X) * scale
    let relZ = (float32 wz - playerPos.Y) * scale
    let pixelX = int(halfMinimap + relX)
    let pixelZ = int(halfMinimap + relZ)
    let color = tileColor skyTop biome tile

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

// ── View ──

let private viewInner
  (ctx: GameContext)
  (minimap: MinimapModel)
  (playerPos: Vector2)
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
      |> SpriteState.withLayer 1010<RenderLayer>
    )
    |> Draw.drop

  let centerX = minimapX + halfMinimap
  let centerY = minimapY + halfMinimap

  buffer
  |> Draw.fillCircle
    (1012<RenderLayer>, Color.Yellow)
    (Vector2(centerX, centerY), 3.0f)
  |> Draw.lineThick
    (1012<RenderLayer>, Color.Yellow, 2.0f)
    (Vector2(centerX, centerY), Vector2(centerX + playerFacing * 10.0f, centerY))
  |> Draw.rectOutline
    (1013<RenderLayer>, Color.White, 2.0f)
    (Rectangle(minimapX, minimapY, minimapSize, minimapSize))
  |> Draw.drop

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  viewInner ctx model.Minimap model.PlayerPosition model.PlayerFacing buffer
  buffer
