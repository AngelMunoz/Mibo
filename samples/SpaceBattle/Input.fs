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
  | EndTurn
  | Restart
  | InfoMode
  | ToggleFullScreen

[<Struct>]
type MouseAction =
  | Zoom of zoom: float32
  | Select of cell: struct (int * int) voption
  | GetInfo of cell: struct (int * int) voption
  | Hover of cell: struct (int * int) voption

[<Struct>]
type InputMsg =
  | InputChanged of inputs: ActionState<GameAction>
  | MouseAction of mouse: MouseAction
  | CalculateRange
  | CellClicked of cell: struct (int * int)
  | ClearSelection
  | SelectCell of cell: struct (int * int)

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
    |> InputMap.key EndTurn KeyboardKey.Enter
    |> InputMap.key Restart KeyboardKey.R
    |> InputMap.key InfoMode KeyboardKey.LeftShift
    |> InputMap.key InfoMode KeyboardKey.RightShift
    |> InputMap.key ToggleFullScreen KeyboardKey.F11

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
    (currentPlayerIndex: int)
    : struct (InputModel * Cmd<InputMsg>) =
    match model.Selection, newlySelected with
    | Selected _src, ValueSome clicked ->
      // Has selection — do NOT touch selection, let Phase decide intent
      model, Cmd.ofMsg(CellClicked clicked)
    | Selected cell, ValueNone ->
      // Clicked empty space — notify Phase with original selection cell
      model, Cmd.ofMsg(CellClicked cell)
    | NoSelection, ValueSome cell ->
      let selection =
        Selection.trySelect cell currentPlayerIndex units model.Selection

      match selection with
      | Selected _ ->
        { model with Selection = selection }, Cmd.ofMsg CalculateRange
      | NoSelection -> model, Cmd.none
    | NoSelection, ValueNone -> model, Cmd.none

  let inline cellFromMouse
    (pos: Vector2)
    (camera: Camera2D)
    (grid: HexGrid<Tile>)
    =
    let worldPos = Raylib.GetScreenToWorld2D(pos, camera)
    grid |> Hex2DSpatial.worldToCell worldPos


  let update
    msg
    (model: InputModel)
    (units: Map<struct (int * int), Units.SBUnit>)
    (currentPlayerIndex: int)
    : struct (InputModel * Cmd<InputMsg>) =
    match msg with
    | CalculateRange -> model, Cmd.none
    | CellClicked _ -> model, Cmd.none
    | ClearSelection ->
      { model with Selection = NoSelection }, Cmd.ofMsg CalculateRange
    | SelectCell cell ->
      { model with Selection = Selected cell }, Cmd.ofMsg CalculateRange
    | InputChanged input ->
      let model = { model with State = input }

      if input.Started.Contains ToggleFullScreen then
        Raylib.ToggleBorderlessWindowed()

      if input.Started.Contains Deselect then
        { model with Selection = NoSelection }, Cmd.none
      else
        model, Cmd.none

    | MouseAction action ->
      match action with
      | MouseAction.Zoom _ -> model, Cmd.none
      | MouseAction.Select cell ->
        handleCellClick cell model units currentPlayerIndex

      | MouseAction.GetInfo cell ->
        handleCellClick cell model units currentPlayerIndex
      | MouseAction.Hover cell ->
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

  module Debug =

    open Raylib_cs
    open Mibo.Elmish.Graphics2D

    let inline view
      (font: Font)
      (style: DebugUtils.DebugStyle)
      (model: InputModel)
      (x: int)
      (y: int)
      (buffer: RenderBuffer2D)
      : struct (int * RenderBuffer2D) =
      let struct (y, buffer) = DebugUtils.section font style x y "Input" buffer

      let struct (y, buffer) =
        DebugUtils.kv font style x y "Selection" (string model.Selection) buffer

      let hovered =
        DebugUtils.formatVopt DebugUtils.formatCell model.HoveredOver

      let struct (y, buffer) =
        DebugUtils.kv font style x y "Hovered" hovered buffer

      let struct (y, buffer) =
        DebugUtils.kv font style x y "Held" (string model.State.Held) buffer

      let struct (y, buffer) =
        DebugUtils.kv
          font
          style
          x
          y
          "Started"
          (string model.State.Started)
          buffer

      struct (y, buffer)
