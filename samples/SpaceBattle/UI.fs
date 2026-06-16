namespace SpaceBattle

open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Next.Graphics2D
open Mibo.Layout
open Raylib_cs
open SpaceBattle.Types
open SpaceBattle.Units
open SpaceBattle.Phase

module UI =

  [<Literal>]
  let private HpBarHeight = 6.0f

  [<Literal>]
  let private HpBarOffset = 8.0f

  [<Literal>]
  let private DotSize = 4.0f

  [<Literal>]
  let private DotSpacing = 6.0f

  let private factionColor =
    function
    | Federation -> Color(80uy, 140uy, 255uy, 255uy)
    | Empire -> Color(255uy, 80uy, 80uy, 255uy)
    | Pirates -> Color(255uy, 60uy, 60uy, 255uy)

  let private movedColor = Color(100uy, 180uy, 255uy, 255uy)
  let private actedColor = Color(255uy, 100uy, 100uy, 255uy)

  let drawHpBars
    (vpWidth: float32)
    (vpHeight: float32)
    (units: Map<struct (int * int), SBUnit>)
    (grid: HexGrid<Tile>)
    (visible: Set<struct (int * int)>)
    (movingUnit: struct (int * int * Vector2) voption)
    (turn: Turn)
    (camera: Camera2D)
    (buffer: RenderBuffer2D)
    =
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(Vector2(vpWidth, vpHeight), camera)

    let hexH = Constants.CellSize * sqrt 3.0f

    grid
    |> HexGrid.iterVisible
      topLeft.X
      topLeft.Y
      bottomRight.X
      bottomRight.Y
      (fun col row _tile ->
        if not(visible.Contains(struct (col, row))) then
          ()
        else
          match units |> Map.tryFind struct (col, row) with
          | Some unit ->
            let worldPos =
              match movingUnit with
              | ValueSome struct (mc, mr, pos) when mc = col && mr = row -> pos
              | _ -> grid |> HexGrid.getWorldPos col row

            let hexW = Constants.CellSize * 2.0f
            let barWidth = hexW * 0.8f
            let barX = worldPos.X - barWidth / 2.0f
            let barY = worldPos.Y + hexH / 2.0f + HpBarOffset

            // HP bar background (dark gray)
            buffer
            |> Draw.fillRect
              (0<RenderLayer>, Color(40uy, 40uy, 40uy, 200uy))
              (Rectangle(barX, barY, barWidth, HpBarHeight))
            |> Draw.drop

            // HP bar foreground (faction color)
            let hpRatio = float32 unit.HP / float32 unit.MaxHP
            let fillWidth = barWidth * hpRatio

            if fillWidth > 0.0f then
              buffer
              |> Draw.fillRect
                (0<RenderLayer>, factionColor unit.Faction)
                (Rectangle(barX, barY, fillWidth, HpBarHeight))
              |> Draw.drop

            // Action indicators below HP bar
            let dotY = barY + HpBarHeight + 2.0f

            if hasMoved unit.id turn then
              buffer
              |> Draw.fillRect
                (0<RenderLayer>, movedColor)
                (Rectangle(
                  worldPos.X - DotSpacing - DotSize,
                  dotY,
                  DotSize,
                  DotSize
                ))
              |> Draw.drop

            if hasActed unit.id turn then
              buffer
              |> Draw.fillRect
                (0<RenderLayer>, actedColor)
                (Rectangle(worldPos.X + DotSpacing, dotY, DotSize, DotSize))
              |> Draw.drop
          | None -> ())

    buffer

  let drawInfoOverlays
    (vpWidth: float32)
    (vpHeight: float32)
    (units: Map<struct (int * int), SBUnit>)
    (grid: HexGrid<Tile>)
    (visible: Set<struct (int * int)>)
    (hoveredOver: struct (int * int) voption)
    (camera: Camera2D)
    (buffer: RenderBuffer2D)
    =
    match hoveredOver with
    | ValueSome struct (hCol, hRow) when visible.Contains(struct (hCol, hRow)) ->
      match units |> Map.tryFind struct (hCol, hRow) with
      | Some hoveredUnit ->
        let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

        let bottomRight =
          Raylib.GetScreenToWorld2D(Vector2(vpWidth, vpHeight), camera)

        // Move range (blue filled)
        let moveRange =
          Selection.computeMoveRange
            hCol
            hRow
            hoveredUnit.MoveRange
            grid
            units
            hoveredUnit.PlayerIndex

        // Attack ring (red border)
        let attackRing =
          Hex2DSpatial.ring hCol hRow hoveredUnit.AttackRange grid

        // Visibility ring (green border)
        let visRing = Hex2DSpatial.ring hCol hRow hoveredUnit.VisualRange grid

        grid
        |> HexGrid.iterVisible
          topLeft.X
          topLeft.Y
          bottomRight.X
          bottomRight.Y
          (fun col row _tile ->
            let worldPos = grid |> HexGrid.getWorldPos col row

            // Move range (blue filled)
            if moveRange.Contains(struct (col, row)) then
              buffer
              |> Draw.fillPoly
                (0<RenderLayer>, Color(100uy, 180uy, 255uy, 100uy))
                (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
              |> Draw.drop

            // Attack ring (red border, brighter if enemy)
            if attackRing |> Array.contains(struct (col, row)) then
              let hasEnemy =
                match units |> Map.tryFind struct (col, row) with
                | Some u -> u.Faction <> hoveredUnit.Faction
                | None -> false

              let alpha = if hasEnemy then 255uy else 180uy

              buffer
              |> Draw.polyOutline
                (0<RenderLayer>, Color(255uy, 80uy, 80uy, alpha), 2.5f)
                (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
              |> Draw.drop

            // Visibility ring (green border)
            if visRing |> Array.contains(struct (col, row)) then
              buffer
              |> Draw.polyOutline
                (0<RenderLayer>, Color(80uy, 255uy, 120uy, 150uy), 2.0f)
                (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
              |> Draw.drop)
      | None -> ()
    | ValueSome _
    | ValueNone -> ()

    buffer

  let drawTurnIndicator
    (turn: Turn)
    (turnOrder: TurnOrder)
    (font: Font)
    (vpWidth: float32)
    (buffer: RenderBuffer2D)
    =
    let text = $"{turn.CurrentFaction} — Turn {turn.TurnNumber + 1}"
    let fontSize = 24.0f
    let textWidth = Raylib.MeasureTextEx(font, text, fontSize, 1.0f).X
    let x = (vpWidth - textWidth) / 2.0f
    let y = 16.0f

    let color = factionColor turn.CurrentFaction

    buffer
    |> Draw.text(
      TextState.create(font, text, Vector2(x, y))
      |> TextState.withFontSize fontSize
      |> TextState.withSpacing 1.0f
      |> TextState.withColor color
    )
