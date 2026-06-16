module PlatformerSample.View

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Layout
open Mibo.Animation
open PlatformerSample.Constants
open PlatformerSample.DayNight
open PlatformerSample.WorldGen
open PlatformerSample.Minimap
open Mibo.Elmish.Next.Graphics2D
open Raylib_cs
open PlatformerSample.Types

let inline r (x: int) (y: int) (w: int) (h: int) =
  Rectangle(float32 x, float32 y, float32 w, float32 h)

// Pre-allocated buffers to avoid per-frame allocation
let private nearbyOccluders = ResizeArray<Occluder2D>(256)
let private nearbyTorches = ResizeArray<TorchLight>(64)

// -------------------------------------------------------------
// Lighting & Rendering
// -------------------------------------------------------------

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  model.Lighting.Reset()

  let playerCenterX = model.PlayerPosition.X + playerWidth / 2.0f

  let camera = model.Camera

  // Sky background and day/night ambient
  let time = model.DayNightTimeOfDay

  let dayNight = {
    TimeOfDay = model.DayNightTimeOfDay
    DayDuration = model.DayNightDuration
  }

  let skyTop, skyBot = DayNight.getSkyColors time
  let ambient = DayNight.getAmbientColor time
  let sunIntensity = DayNight.getSunIntensity time
  let moonIntensity = DayNight.getMoonIntensity time
  let sunPos, moonPos = DayNight.orbitalPositions playerCenterX dayNight

  let viewBounds =
    Camera2D.viewportBounds
      camera
      (float32 ctx.WindowWidth)
      (float32 ctx.WindowHeight)

  buffer
  |> Draw.rectGradientV
    (-1000<RenderLayer>)
    (0, 0, ctx.WindowWidth, ctx.WindowHeight, skyTop, skyBot)
  |> Draw.beginCamera 0<RenderLayer> camera
  |> LightDraw.setAmbient model.Lighting (5<RenderLayer>, { Color = ambient })
  |> Draw.drop

  // Sun directional light
  if sunIntensity > 0.0f then
    let sunDir =
      Vector2.Normalize(Vector2(playerCenterX, groundLevel - 200.0f) - sunPos)

    buffer
    |> LightDraw.addDirectionalLight model.Lighting 6<RenderLayer> {
      Direction = sunDir
      Color = Color(255uy, 245uy, 220uy)
      Intensity = sunIntensity * 1.5f
      CastsShadows = true
    }
    |> Draw.drop

  // Moon directional light
  if moonIntensity > 0.0f then
    let moonDir =
      Vector2.Normalize(Vector2(playerCenterX, groundLevel - 200.0f) - moonPos)

    buffer
    |> LightDraw.addDirectionalLight model.Lighting 6<RenderLayer> {
      Direction = moonDir
      Color = Color(180uy, 200uy, 255uy)
      Intensity = moonIntensity * 0.8f
      CastsShadows = true
    }
    |> Draw.drop

  // Collect occluders and torches from nearby chunks
  let pcx = int(Math.Floor(float model.PlayerPosition.X / float chunkWorldSize))
  let pcy = int(Math.Floor(float model.PlayerPosition.Y / float chunkWorldSize))

  nearbyOccluders.Clear()
  nearbyTorches.Clear()

  // Max distance for occluders to cast shadows (1.5x viewport diagonal)
  let maxOccluderDistSq =
    let vw = float32 ctx.WindowWidth
    let vh = float32 ctx.WindowHeight
    (vw * 1.5f) * (vw * 1.5f) + (vh * 1.5f) * (vh * 1.5f)

  let playerPos = model.PlayerPosition

  for KeyValue(key, chunk) in model.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      // Filter occluders by distance — only near ones cast shadows
      for o in chunk.Occluders do
        let mx = (o.P1.X + o.P2.X) * 0.5f
        let my = (o.P1.Y + o.P2.Y) * 0.5f
        let dx = mx - playerPos.X
        let dy = my - playerPos.Y

        if dx * dx + dy * dy <= maxOccluderDistSq then
          nearbyOccluders.Add o

      for t in chunk.Torches do
        nearbyTorches.Add t

  // Sort by distance and take nearest N (avoid Seq allocation)
  let ocCount = min nearbyOccluders.Count maxOccluders

  if nearbyOccluders.Count > 1 then
    nearbyOccluders.Sort(fun a b ->
      let ax = (a.P1.X + a.P2.X) * 0.5f - playerPos.X
      let ay = (a.P1.Y + a.P2.Y) * 0.5f - playerPos.Y
      let bx = (b.P1.X + b.P2.X) * 0.5f - playerPos.X
      let by = (b.P1.Y + b.P2.Y) * 0.5f - playerPos.Y
      compare (ax * ax + ay * ay) (bx * bx + by * by))

  let torchCount = min nearbyTorches.Count maxTorchLights

  if nearbyTorches.Count > 1 then
    nearbyTorches.Sort(fun a b ->
      let ax = a.Position.X - playerPos.X
      let ay = a.Position.Y - playerPos.Y
      let bx = b.Position.X - playerPos.X
      let by = b.Position.Y - playerPos.Y
      compare (ax * ax + ay * ay) (bx * bx + by * by))

  // Add torches as point lights and draw sprites
  let torchSrc = AnimatedSprite.currentSource model.TorchSprite

  for i = 0 to torchCount - 1 do
    let torch = nearbyTorches[i]

    buffer
    |> LightDraw.addPointLight model.Lighting 7<RenderLayer> {
      Position = torch.Position
      Color = torch.Color
      Intensity = 1.2f
      Radius = torch.Radius
      Falloff = 1.5f
      CastsShadows = false
    }
    |> Draw.drop

    let torchDest =
      r (int torch.Position.X - 16) (int torch.Position.Y - 32) 32 32

    buffer
    |> LightDraw.litSprite
      model.Lighting
      (SpriteState.create(model.Assets.TorchSheet.Texture, torchDest, torchSrc)
       |> SpriteState.withLayer 7<RenderLayer>)
    |> Draw.drop

  // Add occluders
  for i = 0 to ocCount - 1 do
    buffer
    |> LightDraw.addOccluder model.Lighting 8<RenderLayer> nearbyOccluders[i]
    |> Draw.drop

  // Render visible tiles from nearby chunks only
  let tileSpriteSrc (biome: Biome) (tile: TileType) =
    match tile with
    | Ground ->
      match biome with
      | Grass -> r 260 585 64 64 // terrain_grass_block
      | Stone -> r 520 975 64 64 // terrain_stone_block
      | Snow -> r 1040 845 64 64 // terrain_snow_block
      | Sand -> r 390 780 64 64 // terrain_sand_block
    | Platform ->
      match biome with
      | Grass -> r 520 975 64 64 // terrain_stone_block
      | Stone -> r 780 455 64 64 // terrain_dirt_block
      | Snow -> r 520 975 64 64 // terrain_stone_block
      | Sand -> r 780 455 64 64 // terrain_dirt_block
    | Spikes -> r 715 0 64 64 // block_spikes
    | Coin -> r 0 130 64 64 // coin_gold
    | Flag -> r 780 195 64 64 // flag_red_a
    | Empty -> r 0 0 0 0

  for KeyValue(key, chunk) in model.Chunks do
    let struct (cx, cy) = key

    if abs(cx - pcx) <= chunkLoadRadius && abs(cy - pcy) <= chunkLoadRadius then
      if Culling.isVisible2D viewBounds chunk.Bounds then
        let chunkBiome = chunk.Biome

        CellGrid2D.iterVisible
          (int viewBounds.X)
          (int viewBounds.Y)
          (int(viewBounds.X + viewBounds.Width))
          (int(viewBounds.Y + viewBounds.Height))
          (fun x y tile ->
            if tile <> TileType.Empty then
              let wx = chunk.Grid.Origin.X + float32 x * tileSize
              let wy = chunk.Grid.Origin.Y + float32 y * tileSize
              let dest = Rectangle(wx, wy, tileSize, tileSize)

              let sprite =
                let sprite =
                  SpriteState.create(
                    model.Assets.TileTexture,
                    dest,
                    tileSpriteSrc chunkBiome tile
                  )
                  |> SpriteState.withLayer 10<RenderLayer>

                if tile = TileType.Coin then
                  sprite
                  |> SpriteState.withNormalMap model.Assets.CoinNormalMap
                else
                  sprite

              buffer |> LightDraw.litSprite model.Lighting sprite |> Draw.drop)
          chunk.Grid

  // Lit player sprite (uses litAnimatedSprite for automatic flip/normal map handling)
  let playerDrawY = int(model.PlayerPosition.Y + playerHeight - 64.0f)
  let playerDest = r (int model.PlayerPosition.X) playerDrawY 64 64

  buffer
  |> LightDraw.litAnimatedSprite
    model.Lighting
    20<RenderLayer>
    playerDest
    model.PlayerSprite
  |> Draw.drop

  // Particles
  buffer
  |> ParticleDraw.particles
    model.Assets.ParticleTexture
    model.Particles
    model.ParticleCount
    3<RenderLayer>

  // End lighting
  |> LightDraw.endLighting model.Lighting 999<RenderLayer>
  // End camera
  |> Draw.endCamera 1000<RenderLayer>
  // UI
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"Day/Night Cycle | Time: {model.DayNightTimeOfDay:F1}h | Chunks: {model.Chunks.Count} | Score: {model.Score} | Pos: %.1f{model.PlayerPosition.X},%.1f{model.PlayerPosition.Y} | WASD/Arrows: Move | Space: Jump | R: Respawn",
      Vector2(10.0f, 10.0f)
    )
    |> TextState.withFontSize 20.0f
    |> TextState.withSpacing 1.0f
    |> TextState.withColor Color.White
    |> TextState.withLayer 1001<RenderLayer>
  )
  |> Draw.text(
    TextState.create(
      model.Assets.Font,
      $"FPS: {model.Diagnostics.Fps} | Frame Time: {model.Diagnostics.FrameTime * 1000.0f:F1}ms",
      Vector2(10.0f, 32.0f)
    )
    |> TextState.withFontSize 20.0f
    |> TextState.withSpacing 1.0f
    |> TextState.withColor Color.White
    |> TextState.withLayer 1001<RenderLayer>
  )
  // Minimap
  |> Minimap.view ctx model
  |> Draw.drop
