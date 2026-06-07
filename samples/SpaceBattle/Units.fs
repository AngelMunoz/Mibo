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

  type UnitsMsg =
    | MoveUnit of src: struct (int * int) * dest: struct (int * int)
    | UpdateDirection of cell: struct (int * int) * direction: Direction
    | ApplyDamage of target: struct (int * int) * damage: int
    | AttackUnit of attacker: struct (int * int) * target: struct (int * int)
    | RemoveUnit of cell: struct (int * int)

  let directionFromDelta (dc: int) (dr: int) (srcCol: int) : Direction option =
    if srcCol % 2 = 0 then
      match dc, dr with
      | -1, -1 -> Some NW
      | 0, -1 -> Some N
      | 1, -1 -> Some NE
      | 1, 0 -> Some SE
      | 0, 1 -> Some S
      | -1, 0 -> Some SW
      | _ -> None
    else
      match dc, dr with
      | -1, 0 -> Some NW
      | 0, -1 -> Some N
      | 1, 0 -> Some NE
      | 1, 1 -> Some SE
      | 0, 1 -> Some S
      | -1, 1 -> Some SW
      | _ -> None

  let directionFromCells
    (src: struct (int * int))
    (dst: struct (int * int))
    : Direction option =
    let struct (sc, sr) = src
    let struct (dc, dr) = dst
    let dCol = dc - sc
    let dRow = dr - sr

    if dCol = 0 && dRow = 0 then
      None
    elif abs dCol <= 1 && abs dRow <= 1 then
      directionFromDelta dCol dRow sc
    else
      let struct (sq, sr2, ss) =
        Mibo.Layout.Hex2DSpatial.offsetToCube sc sr Mibo.Layout.FlatTop

      let struct (dq, dr2, ds) =
        Mibo.Layout.Hex2DSpatial.offsetToCube dc dr Mibo.Layout.FlatTop

      let aq = dq - sq
      let ar = dr2 - sr2
      let ads = ds - ss

      let absAq = abs aq
      let absAr = abs ar
      let absAds = abs ads

      if absAq >= absAr && absAq >= absAds then
        if aq > 0 then Some SE else Some NW
      elif absAr >= absAq && absAr >= absAds then
        if ar > 0 then Some S else Some N
      else if ads > 0 then
        Some SW
      else
        Some NE

  let private baseDamage = function
    | Fighter -> 30
    | Cruiser -> 20
    | Battleship -> 15

  let update
    (msg: UnitsMsg)
    (units: Map<struct (int * int), SBUnit>)
    : Map<struct (int * int), SBUnit> =
    match msg with
    | MoveUnit(src, dest) ->
      match units |> Map.tryFind src with
      | Some unit -> units |> Map.remove src |> Map.add dest unit
      | None -> units
    | UpdateDirection(cell, dir) ->
      match units |> Map.tryFind cell with
      | Some unit -> units |> Map.add cell { unit with Direction = dir }
      | None -> units
    | ApplyDamage(target, damage) ->
      match units |> Map.tryFind target with
      | Some unit ->
        let hp = unit.HP - damage

        if hp <= 0 then
          units |> Map.remove target
        else
          units |> Map.add target { unit with HP = hp }
      | None -> units
    | AttackUnit(attacker, target) ->
      match units |> Map.tryFind attacker, units |> Map.tryFind target with
      | Some atk, Some tgt ->
        let damage = max 1 (baseDamage atk.Class - tgt.Defense)
        let hp = tgt.HP - damage

        if hp <= 0 then
          units |> Map.remove target
        else
          units |> Map.add target { tgt with HP = hp }
      | _ -> units
    | RemoveUnit cell -> units |> Map.remove cell

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
    (movingUnit: struct (int * int * Vector2) voption)
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
          let worldPos =
            match movingUnit with
            | ValueSome struct (mc, mr, pos) when mc = col && mr = row -> pos
            | _ -> map |> HexGrid.getWorldPos col row

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
