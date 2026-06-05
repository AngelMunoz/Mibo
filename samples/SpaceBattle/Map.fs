namespace SpaceBattle

open System
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Raylib_cs
open SpaceBattle.Types

module Map =
  open System.Numerics

  let createMap origin width height : HexGrid<Tile> =
    HexGrid.create width height Constants.CellSize origin FlatTop

  let asteroidSection (rng: Random) col row =
    HexLayout.section col row (fun section ->

      section
      |> HexLayout.scatterBorder
        0
        0
        section.Width
        section.Height
        5
        (rng.Next())
        Crate1
      |> HexLayout.scatter (rng.Next(10)) (rng.Next()) Asteroid1
      |> HexLayout.scatter (rng.Next(5)) (rng.Next()) Asteroid2)

  let fillMap (rng: Random) (map: HexGrid<Tile>) : HexGrid<Tile> =
    let asteroids = asteroidSection rng

    let filledMap =
      HexLayout.fill 0 0 map.Width map.Height DeepSpace
      >> HexLayout.center map.Width map.Height (asteroids 0 0)

    map |> HexLayout.run filledMap

  let view
    (ctx: GameContext)
    (sprites: Map<struct (int * int), AnimatedSprite>)
    (camera: Camera2D)
    (model: HexGrid<Tile>)
    (hoveredOver: struct (int * int) voption)
    buffer
    =
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(
        Vector2(Constants.VPWidth, Constants.VPHeight),
        camera
      )

    model
    |> HexGrid.iterVisible
      topLeft.X
      topLeft.Y
      bottomRight.X
      bottomRight.Y
      (fun col row tile ->
        let worldPos = model |> HexGrid.getWorldPos col row
        let hexW = Constants.CellSize * 2.0f
        let hexH = Constants.CellSize * sqrt 3.0f

        let targetRect =
          Rectangle(worldPos.X - hexW / 2f, worldPos.Y - hexH / 2f, hexW, hexH)

        let color =
          match tile with
          | Asteroid1 -> Color.Red
          | Asteroid2 -> Color.Violet
          | Crate1 -> Color.Blue
          | Crate2 -> Color.DarkBlue
          | Station -> Color.Green
          | DeepSpace -> Color.DarkGray

        match sprites |> Map.tryFind struct (col, row) with
        | Some animated ->
          let source = AnimatedSprite.currentSource animated
          let texture = animated.Sheet.Texture

          buffer
          |> Draw.sprite(SpriteState.create(texture, targetRect, source))
          |> Draw.drop
        | None ->
          buffer
          |> Draw.polyOutline
            (0<RenderLayer>, color, 1f)
            (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop

        match hoveredOver with
        | ValueSome struct (col, row) ->
          let worldPos = model |> HexGrid.getWorldPos col row

          buffer
          |> Draw.polyOutline
            (0<RenderLayer>, Color.Yellow, 2.5f)
            (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop
        | ValueNone -> ())

    buffer
