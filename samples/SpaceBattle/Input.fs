namespace SpaceBattle

open System.Numerics
open Mibo.Elmish
open Mibo.Input
open Mibo.Layout
open Raylib_cs
open SpaceBattle.Types

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown
  | Deselect

[<Struct>]
type MouseAction =
  | Zoom of zoom: float32
  | Select of Vector2
  | GetInfo of Vector2
  | MovedTo of Vector2

[<Struct>]
type InputMsg =
  | InputChanged of inputs: ActionState<GameAction>
  | MouseAction of mouse: MouseAction
  | CalculateRange

[<Struct>]
type SelectionAction =
  | SelectUnit of cell: struct (int * int)
  | MoveTo of cell: struct (int * int)
  | CancelSelection
  | NoAction

type InputModel = {
  State: ActionState<GameAction>
  HoveredOver: struct (int * int) voption
  Selection: SelectionState
}

module Input =

  let inputMap =
    InputMap.empty
    |> InputMap.key MoveLeft KeyboardKey.Left
    |> InputMap.key MoveLeft KeyboardKey.A
    |> InputMap.key MoveRight KeyboardKey.Right
    |> InputMap.key MoveRight KeyboardKey.D
    |> InputMap.key MoveUp KeyboardKey.Up
    |> InputMap.key MoveUp KeyboardKey.W
    |> InputMap.key MoveDown KeyboardKey.Down
    |> InputMap.key MoveDown KeyboardKey.S
    |> InputMap.key Deselect KeyboardKey.Escape

  let init = {
    State = ActionState.empty
    HoveredOver = ValueNone
    Selection = NoSelection
  }

  let inline clearSelection(model: InputModel) = {
    model with
        Selection = NoSelection
  }

  let handleCellClick
    (newlySelected: struct (int * int) voption)
    (model: InputModel)
    (units: Map<struct (int * int), Units.SBUnit>)
    (currentFaction: Units.Faction)
    : struct (InputModel * Cmd<InputMsg>) =
    match model.Selection with
    | Selected cell ->
      newlySelected
      |> ValueOption.map(fun newlySelected ->
        if newlySelected = cell then
          struct (model, Cmd.none)
        else
          clearSelection model, Cmd.ofMsg CalculateRange)
      |> ValueOption.defaultValue(clearSelection model, Cmd.none)
    | NoSelection ->
      match newlySelected with
      | ValueSome cell ->
        let selection =
          Selection.trySelect cell currentFaction units model.Selection

        { model with Selection = selection }, Cmd.ofMsg CalculateRange
      | ValueNone -> model, Cmd.none

  let cellFromMouse (pos: Vector2) (camera: Camera2D) (grid: HexGrid<Tile>) =
    let worldPos = Raylib.GetScreenToWorld2D(pos, camera)
    grid |> Hex2DSpatial.worldToCell worldPos


  let update
    msg
    (model: InputModel)
    (camera: Camera2D)
    (grid: HexGrid<Tile>)
    (units: Map<struct (int * int), Units.SBUnit>)
    (currentFaction: Units.Faction)
    : struct (InputModel * Cmd<InputMsg>) =
    match msg with
    | CalculateRange -> model, Cmd.none
    | InputChanged input ->
      let model = { model with State = input }

      if input.Started.Contains Deselect then
        { model with Selection = NoSelection }, Cmd.none
      else
        model, Cmd.none

    | MouseAction action ->
      match action with
      | MouseAction.Zoom _ -> model, Cmd.none
      | MouseAction.Select pos ->
        let newlySelected = cellFromMouse pos camera grid
        handleCellClick newlySelected model units currentFaction

      | MouseAction.GetInfo pos ->
        let newlySelected = cellFromMouse pos camera grid
        handleCellClick newlySelected model units currentFaction
      | MouseAction.MovedTo pos ->
        let worldPos = Raylib.GetScreenToWorld2D(pos, camera)
        let cell = grid |> Hex2DSpatial.worldToCell worldPos

        let cell, cmd =
          match model.HoveredOver with
          | ValueNone -> cell, Cmd.ofMsg CalculateRange
          | ValueSome existing ->
            match cell with
            | ValueSome cell ->
              if cell = existing then
                ValueSome existing, Cmd.none
              else
                ValueSome cell, Cmd.ofMsg CalculateRange
            | ValueNone -> ValueNone, Cmd.none

        { model with HoveredOver = cell }, cmd
