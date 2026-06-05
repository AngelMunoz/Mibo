namespace SpaceBattle

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Mibo.Input
open Raylib_cs
open SpaceBattle.Types

type CameraModel = { Camera: Camera2D }

module Camera =

  let init() = {
    Camera =
      Camera2D.create
        Vector2.Zero
        1f
        (Vector2(Constants.VPWidth, Constants.VPHeight))
  }

  let applyZoom (zoom: float32) (model: CameraModel) : CameraModel =
    let mutable camera = model.Camera

    camera.Zoom <-
      camera.Zoom + zoom * 0.1f
      |> max Constants.MinZoom
      |> min Constants.MaxZoom

    { Camera = camera }

  let clampToMapBounds (map: HexGrid<Tile>) (camera: byref<Camera2D>) =
    let hexW = Constants.CellSize * 2.0f
    let hexH = Constants.CellSize * sqrt 3.0f

    let mapLeft = map.Origin.X
    let mapTop = map.Origin.Y
    let mapRight = map.Origin.X + float32(map.Width - 1) * hexW * 0.75f + hexW
    let mapBottom = map.Origin.Y + float32(map.Height - 1) * hexH + hexH * 1.5f

    let vw = Constants.VPWidth / camera.Zoom
    let vh = Constants.VPHeight / camera.Zoom

    let marginX = Constants.BorderMargin * vw
    let marginY = Constants.BorderMargin * vh
    let minX = mapLeft + vw * 0.5f - marginX
    let maxX = mapRight - vw * 0.5f + marginX
    let minY = mapTop + vh * 0.5f - marginY
    let maxY = mapBottom - vh * 0.5f + marginY

    let clampMinX, clampMaxX =
      if minX <= maxX then
        (minX, maxX)
      else
        ((mapLeft + mapRight) * 0.5f, (mapLeft + mapRight) * 0.5f)

    let clampMinY, clampMaxY =
      if minY <= maxY then
        (minY, maxY)
      else
        ((mapTop + mapBottom) * 0.5f, (mapTop + mapBottom) * 0.5f)

    Camera2D.clampTarget &camera clampMinX clampMinY clampMaxX clampMaxY

  let applyMovement
    (input: ActionState<GameAction>)
    (gt: GameTime)
    (model: CameraModel)
    : CameraModel =
    let speed = 300f * float32 gt.ElapsedGameTime.TotalSeconds
    let mutable cam = model.Camera

    if input.Held.Contains MoveLeft then
      cam.Target <- cam.Target + Vector2(-speed, 0f)

    if input.Held.Contains MoveRight then
      cam.Target <- cam.Target + Vector2(speed, 0f)

    if input.Held.Contains MoveUp then
      cam.Target <- cam.Target + Vector2(0f, -speed)

    if input.Held.Contains MoveDown then
      cam.Target <- cam.Target + Vector2(0f, speed)

    { Camera = cam }

  let inline beginView (model: CameraModel) (buffer: RenderBuffer2D) =
    Draw.beginCamera 0<RenderLayer> model.Camera buffer

  let inline endView(buffer: RenderBuffer2D) =
    Draw.endCamera 0<RenderLayer> buffer
