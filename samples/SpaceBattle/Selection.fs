namespace SpaceBattle

open Mibo.Layout
open SpaceBattle.Types
open SpaceBattle.Units

[<Struct>]
type SelectionState =
  | NoSelection
  | Selected of selected: struct (int * int)

module Selection =

  let computeMoveRange
    (col: int)
    (row: int)
    (moveRange: int)
    (grid: HexGrid<Tile>)
    (units: Map<struct (int * int), SBUnit>)
    (currentFaction: Faction)
    : Set<struct (int * int)> =
    let friendlyOccupied =
      units
      |> Map.toSeq
      |> Seq.choose(fun (cell, u) ->
        if u.Faction = currentFaction then Some cell else None)
      |> Set.ofSeq

    let inline isPassable struct (c, r) =
      c = col && r = row
      || match units |> Map.tryFind struct (c, r) with
         | Some u when u.Faction <> currentFaction -> false
         | _ ->
           match HexGrid.get c r grid with
           | ValueSome Station -> false
           | ValueSome _ -> true
           | ValueNone -> false

    Hex2DSpatial.inRange col row moveRange grid
    |> Array.filter(fun cell ->
      isPassable cell && not(friendlyOccupied.Contains cell))
    |> Set.ofArray

  let computePath
    (from: struct (int * int))
    (dest: struct (int * int))
    (grid: HexGrid<Tile>)
    (units: Map<struct (int * int), SBUnit>)
    (currentFaction: Faction)
    : struct (int * int)[] =
    let struct (fc, fr) = from
    let struct (dc, dr) = dest

    let inline isPassable c r =
      c = fc && r = fr
      || c = dc && r = dr
      || match units |> Map.tryFind struct (c, r) with
         | Some u when u.Faction <> currentFaction -> false
         | _ ->
           match HexGrid.get c r grid with
           | ValueSome Station -> false
           | ValueSome _ -> true
           | ValueNone -> false

    Hex2DSpatial.findPath fc fr dc dr isPassable (fun _ _ _ _ -> 1f) grid
    |> ValueOption.defaultValue [||]

  let trySelect
    (cell: struct (int * int))
    (currentFaction: Faction)
    (units: Map<struct (int * int), SBUnit>)
    (state: SelectionState)
    : SelectionState =
    match state with
    | NoSelection ->
      match units |> Map.tryFind cell with
      | Some unit when unit.Faction = currentFaction -> Selected cell
      | Some _
      | None -> state
    | Selected _ -> state
