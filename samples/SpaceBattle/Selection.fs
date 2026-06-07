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
           | ValueSome Station
           | ValueSome Asteroid1
           | ValueSome Asteroid2 -> false
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
           | ValueSome Station
           | ValueSome Asteroid1
           | ValueSome Asteroid2 -> false
           | ValueSome _ -> true
           | ValueNone -> false

    Hex2DSpatial.findPath fc fr dc dr isPassable (fun _ _ _ _ -> 1f) grid
    |> ValueOption.defaultValue [||]

  let simplifyPath
    (path: struct (int * int)[])
    (grid: HexGrid<'T>)
    : struct (int * int)[] =
    if path.Length <= 2 then
      path
    else
      let result = ResizeArray<struct (int * int)>()
      result.Add(path[0])

      let struct (pq0, pr0, ps0) =
        Hex2DSpatial.offsetToCube
          (let struct (c, _) = path[0] in c)
          (let struct (_, r) = path[0] in r)
          grid.Orientation

      let struct (pq1, pr1, ps1) =
        Hex2DSpatial.offsetToCube
          (let struct (c, _) = path[1] in c)
          (let struct (_, r) = path[1] in r)
          grid.Orientation

      let mutable prevDq = pq1 - pq0
      let mutable prevDr = pr1 - pr0
      let mutable prevDs = ps1 - ps0

      for i in 2 .. path.Length - 1 do
        let struct (cq, cr, cs) =
          Hex2DSpatial.offsetToCube
            (let struct (c, _) = path[i] in c)
            (let struct (_, r) = path[i] in r)
            grid.Orientation

        let struct (pq, pr, ps) =
          Hex2DSpatial.offsetToCube
            (let struct (c, _) = path[i - 1] in c)
            (let struct (_, r) = path[i - 1] in r)
            grid.Orientation

        let dq = cq - pq
        let dr = cr - pr
        let ds = cs - ps

        if dq <> prevDq || dr <> prevDr || ds <> prevDs then
          result.Add(path[i - 1])
          prevDq <- dq
          prevDr <- dr
          prevDs <- ds

      result.Add(path[path.Length - 1])
      result.ToArray()

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
