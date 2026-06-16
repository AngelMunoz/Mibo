namespace SpaceBattle

open System
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Layout
open Mibo.Elmish.Next.Graphics2D
open Raylib_cs
open SpaceBattle.Types
open SpaceBattle.Units

type MapModel = {
  Grid: HexGrid<Tile>
  Seed: int
  Reachable: Set<struct (int * int)>
  Visible: Set<struct (int * int)>
  AttackTargets: Set<struct (int * int)>
  Path: struct (int * int)[]
}

[<Struct>]
type RangeQuery = {
  Selection: SelectionState
  Hovered: struct (int * int) voption
  Units: Map<struct (int * int), SBUnit>
  CurrentPlayerIndex: int
  CanMove: bool
  CanAct: bool
}

[<Struct>]
type MapMsg =
  | RecalculateRange of query: RangeQuery
  | RefreshVisibility of
    units: Map<struct (int * int), SBUnit> *
    playerIndex: int

module Map =
  open System.Numerics

  let inline createMap origin width height : HexGrid<Tile> =
    HexGrid.create width height Constants.CellSize origin FlatTop

  let inline asteroidSection (rng: Random) col row =
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

  let init(seed: int, width: int, height: int) : MapModel =
    let grid = createMap Vector2.Zero width height |> fillMap(Random seed)
    let w, h = grid.Width, grid.Height

    let corners = [|
      struct (0, 0)
      struct (1, 0)
      struct (0, 1)
      struct (w - 1, h - 1)
      struct (w - 1, h - 2)
      struct (w - 2, h - 1)
      struct (w - 1, 0)
      struct (w - 2, 0)
      struct (w - 1, 1)
      struct (0, h - 1)
      struct (1, h - 1)
      struct (0, h - 2)
    |]

    for struct (c, r) in corners do
      HexGrid.set c r DeepSpace grid

    {
      Grid = grid
      Seed = seed
      Reachable = Set.empty
      AttackTargets = Set.empty
      Visible = Set.empty
      Path = [||]
    }

  let private pathIndexMap
    (path: struct (int * int)[])
    : Map<struct (int * int), int> =
    path |> Array.mapi(fun i cell -> cell, i) |> Map.ofArray

  let private pathGradientColor (pathLen: int) (idx: int) =
    let t = float32 idx / float32(pathLen - 1)
    let alpha = 80uy + byte(t * 160f)
    Color(100uy, 200uy, 255uy, alpha)

  let computeVisibleUnits
    (units: Map<struct (int * int), SBUnit>)
    (playerIndex: int)
    (grid: HexGrid<Tile>)
    : Set<struct (int * int)> =
    let mutable visible = Set.empty

    for KeyValue(struct (col, row), unit) in units do
      if unit.PlayerIndex = playerIndex then
        let cells = Hex2DSpatial.inRange col row unit.VisualRange grid

        for cell in cells do
          visible <- visible |> Set.add cell

    visible

  let update (msg: MapMsg) (model: MapModel) : MapModel =
    match msg with
    | RecalculateRange query ->
      match query.Selection with
      | NoSelection -> {
          model with
              Reachable = Set.empty
              AttackTargets = Set.empty
              Path = [||]
        }
      | Selected cell ->
        let struct (col, row) = cell

        match query.Units |> Map.tryFind cell with
        | None -> {
            model with
                Reachable = Set.empty
                AttackTargets = Set.empty
                Path = [||]
          }
        | Some unit ->
          if query.CanMove && query.CanAct then
            let reachable =
              Selection.computeMoveRange
                col
                row
                unit.MoveRange
                model.Grid
                query.Units
                query.CurrentPlayerIndex

            let attackTargets =
              Selection.computeAttackRange col row unit.AttackRange model.Grid

            let path =
              match query.Hovered with
              | ValueSome dest when reachable.Contains dest ->
                Selection.computePath
                  cell
                  dest
                  model.Grid
                  query.Units
                  query.CurrentPlayerIndex
              | _ -> [||]

            {
              model with
                  Reachable = reachable
                  AttackTargets = attackTargets
                  Path = path
            }
          elif query.CanMove then
            let reachable =
              Selection.computeMoveRange
                col
                row
                unit.MoveRange
                model.Grid
                query.Units
                query.CurrentPlayerIndex

            let path =
              match query.Hovered with
              | ValueSome dest when reachable.Contains dest ->
                Selection.computePath
                  cell
                  dest
                  model.Grid
                  query.Units
                  query.CurrentPlayerIndex
              | _ -> [||]

            {
              model with
                  Reachable = reachable
                  AttackTargets = Set.empty
                  Path = path
            }
          elif query.CanAct then
            let attackTargets =
              Selection.computeAttackRange col row unit.AttackRange model.Grid

            {
              model with
                  Reachable = Set.empty
                  AttackTargets = attackTargets
                  Path = [||]
            }
          else
            {
              model with
                  Reachable = Set.empty
                  AttackTargets = Set.empty
                  Path = [||]
            }

    | RefreshVisibility(units, playerIndex) ->
        {
          model with
              Visible = computeVisibleUnits units playerIndex model.Grid
        }

  let viewTiles
    (vpWidth: float32)
    (vpHeight: float32)
    (sprites: Map<struct (int * int), AnimatedSprite>)
    (camera: Camera2D)
    (mapModel: MapModel)
    (lightCtx: LightContext2D)
    buffer
    =
    let model = mapModel.Grid
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(Vector2(vpWidth, vpHeight), camera)

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
          |> LightDraw.litSprite
            lightCtx
            (SpriteState.create(texture, targetRect, source))
          |> Draw.drop
        | None ->
          buffer
          |> Draw.polyOutline
            (0<RenderLayer>, color, 1f)
            (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop)

    buffer

  let viewOverlays
    (vpWidth: float32)
    (vpHeight: float32)
    (camera: Camera2D)
    (mapModel: MapModel)
    (hoveredOver: struct (int * int) voption)
    buffer
    =
    let model = mapModel.Grid
    let reachable = mapModel.Reachable
    let attackTargets = mapModel.AttackTargets
    let path = mapModel.Path
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(Vector2(vpWidth, vpHeight), camera)

    let pathIdx = pathIndexMap path

    model
    |> HexGrid.iterVisible
      topLeft.X
      topLeft.Y
      bottomRight.X
      bottomRight.Y
      (fun col row tile ->
        let worldPos = model |> HexGrid.getWorldPos col row

        if attackTargets.Contains(struct (col, row)) then
          buffer
          |> Draw.fillPoly
            (0<RenderLayer>, Color(255uy, 80uy, 80uy, 120uy))
            (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop

        if reachable.Contains(struct (col, row)) then
          buffer
          |> Draw.fillPoly
            (0<RenderLayer>, Color(100uy, 180uy, 255uy, 100uy))
            (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop

        match pathIdx |> Map.tryFind struct (col, row) with
        | Some idx when path.Length > 1 ->
          buffer
          |> Draw.fillPoly
            (0<RenderLayer>, pathGradientColor path.Length idx)
            (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop
        | Some _
        | None -> ()

        match hoveredOver with
        | ValueSome struct (hCol, hRow) ->
          let hWorldPos = model |> HexGrid.getWorldPos hCol hRow

          buffer
          |> Draw.polyOutline
            (0<RenderLayer>, Color.Yellow, 2.5f)
            (Vector2(hWorldPos.X, hWorldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop
        | ValueNone -> ())

    buffer
