namespace SpaceBattle

open System.Numerics
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Raylib_cs
open SpaceBattle.Types

module Units =

  type Faction =
    | Federation // Colony
    | Empire // Terrok
    | Pirates // Kelvor

  type UnitClass =
    | Fighter // Fast, Hits Hard, low defense
    | Cruiser // mid speed, Hits low, mid defense
    | Battleship // Slow, Hits mid, good defense

  type Direction =
    | N
    | NE
    | SE
    | S
    | SW
    | NW


  type SBUnit = {
    Faction: Faction
    Class: UnitClass
    Direction: Direction
    HP: int
    MaxHP: int
    Defense: int
    MoveRange: int
    AttackRange: int
  }

  let private directionFrame =
    function
    | N -> 0
    | NE -> 1
    | SE -> 2
    | S -> 3
    | SW -> 4
    | NW -> 5

  let view
    (ctx: GameContext)
    (units: Map<struct (int * int), SBUnit>)
    (unitSprites: Map<struct (Faction * UnitClass), SpriteSheet>)
    (map: HexGrid<Tile>)
    camera
    buffer
    =
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(
        Vector2(Constants.VPWidth, Constants.VPHeight),
        camera
      )

    map
    |> HexGrid.iterVisible
      topLeft.X
      topLeft.Y
      bottomRight.X
      bottomRight.Y
      (fun col row tile ->
        match units |> Map.tryFind struct (col, row) with
        | Some unit ->
          let worldPos = map |> HexGrid.getWorldPos col row
          let hexW = Constants.CellSize * 2.0f
          let hexH = Constants.CellSize * sqrt 3.0f

          let targetRect =
            Rectangle(
              worldPos.X - hexW / 2.0f,
              worldPos.Y - hexH / 2.0f,
              hexW,
              hexH
            )

          match
            unitSprites |> Map.tryFind struct (unit.Faction, unit.Class)
          with
          | Some sheet ->
            let frameIdx = directionFrame unit.Direction
            let cols = sheet.Texture.Width / sheet.FrameSize.X
            let srcCol = frameIdx % cols
            let srcRow = frameIdx / cols
            let fw = sheet.FrameSize.X
            let fh = sheet.FrameSize.Y

            let source =
              Rectangle(
                float32(srcCol * fw),
                float32(srcRow * fh),
                float32 fw,
                float32 fh
              )

            buffer
            |> Draw.sprite(
              SpriteState.create(sheet.Texture, targetRect, source)
            )
            |> Draw.drop
          | None -> ()
        | None -> ())

    buffer
