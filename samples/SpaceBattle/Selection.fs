namespace SpaceBattle

open Mibo.Layout
open SpaceBattle.Types
open SpaceBattle.Units

[<Struct>]
type SelectionState =
  | NoSelection
  | Selected of selected: struct (int * int)

module Selection =

  let private isTerrainPassable
    (units: Map<struct (int * int), SBUnit>)
    (currentPlayerIndex: int)
    (grid: HexGrid<Tile>)
    (c: int)
    (r: int)
    : bool =
    match units |> Map.tryFind struct (c, r) with
    | Some u when u.PlayerIndex <> currentPlayerIndex -> false
    | _ ->
      match HexGrid.get c r grid with
      | ValueSome Station
      | ValueSome Asteroid1
      | ValueSome Asteroid2 -> false
      | ValueSome _ -> true
      | ValueNone -> false

  let computeMoveRange
    (col: int)
    (row: int)
    (moveRange: int)
    (grid: HexGrid<Tile>)
    (units: Map<struct (int * int), SBUnit>)
    (currentPlayerIndex: int)
    : Set<struct (int * int)> =
    let friendlyOccupied =
      units
      |> Map.toSeq
      |> Seq.choose(fun (cell, u) ->
        if u.PlayerIndex = currentPlayerIndex then
          Some cell
        else
          None)
      |> Set.ofSeq

    let inline isPassable struct (c, r) =
      (c = col && r = row)
      || isTerrainPassable units currentPlayerIndex grid c r

    Hex2DSpatial.inRange col row moveRange grid
    |> Array.filter(fun cell ->
      isPassable cell && not(friendlyOccupied.Contains cell))
    |> Set.ofArray

  let computeAttackRange
    (col: int)
    (row: int)
    (attackRange: int)
    (grid: HexGrid<Tile>)
    : Set<struct (int * int)> =
    Hex2DSpatial.inRange col row attackRange grid
    |> Array.filter(fun struct (c, r) ->
      match HexGrid.get c r grid with
      | ValueSome _ -> true
      | ValueNone -> false)
    |> Set.ofArray

  let computePath
    (from: struct (int * int))
    (dest: struct (int * int))
    (grid: HexGrid<Tile>)
    (units: Map<struct (int * int), SBUnit>)
    (currentPlayerIndex: int)
    : struct (int * int)[] =
    let struct (fc, fr) = from
    let struct (dc, dr) = dest

    let inline isPassable c r =
      (c = fc && r = fr)
      || (c = dc && r = dr)
      || isTerrainPassable units currentPlayerIndex grid c r

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

      let struct (c0, r0) = path[0]

      let struct (pq0, pr0, ps0) =
        Hex2DSpatial.offsetToCube c0 r0 grid.Orientation

      let struct (c1, r1) = path[1]

      let struct (pq1, pr1, ps1) =
        Hex2DSpatial.offsetToCube c1 r1 grid.Orientation

      let mutable prevDq = pq1 - pq0
      let mutable prevDr = pr1 - pr0
      let mutable prevDs = ps1 - ps0

      for i in 2 .. path.Length - 1 do
        let struct (ci, ri) = path[i]

        let struct (cq, cr, cs) =
          Hex2DSpatial.offsetToCube ci ri grid.Orientation

        let struct (pi, riPrev) = path[i - 1]

        let struct (pq, pr, ps) =
          Hex2DSpatial.offsetToCube pi riPrev grid.Orientation

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
    (currentPlayerIndex: int)
    (units: Map<struct (int * int), SBUnit>)
    (state: SelectionState)
    : SelectionState =
    match state with
    | NoSelection ->
      match units |> Map.tryFind cell with
      | Some unit when unit.PlayerIndex = currentPlayerIndex -> Selected cell
      | Some _
      | None -> state
    | Selected _ -> state
