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

  [<Measure>]
  type UnitId

  type SBUnit = {
    id: int<UnitId>
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

  module Debug =

    let inline view
      (font: Raylib_cs.Font)
      (style: DebugUtils.DebugStyle)
      (units: Map<struct (int * int), SBUnit>)
      (x: int)
      (y: int)
      (buffer: RenderBuffer2D)
      : struct (int * RenderBuffer2D) =
      let struct (y, buffer) =
        DebugUtils.section font style x y $"Units ({units.Count})" buffer

      (struct (y, buffer), units)
      ||> Map.fold(fun (struct (y, buffer)) pos unit ->
        let posStr = DebugUtils.formatCell pos

        let msg =
          $"{posStr} #{unit.id} {unit.Faction} {unit.Class} HP:{unit.HP}/{unit.MaxHP}"

        DebugUtils.kv font style x y posStr msg buffer)
