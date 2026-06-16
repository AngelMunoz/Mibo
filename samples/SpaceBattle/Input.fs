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
    |> InputMap.key MoveLeft KeyCode.Left
    |> InputMap.key MoveLeft KeyCode.A
    |> InputMap.key MoveRight KeyCode.Right
    |> InputMap.key MoveRight KeyCode.D
    |> InputMap.key MoveUp KeyCode.Up
    |> InputMap.key MoveUp KeyCode.W
    |> InputMap.key MoveDown KeyCode.Down
    |> InputMap.key MoveDown KeyCode.S
    |> InputMap.key Deselect KeyCode.Escape
    |> InputMap.key EndTurn KeyCode.Enter
    |> InputMap.key Restart KeyCode.R
    |> InputMap.key InfoMode KeyCode.LeftShift
    |> InputMap.key InfoMode KeyCode.RightShift
    |> InputMap.key ToggleFullScreen KeyCode.F11

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
        if cell = model.HoveredOver then
          model, Cmd.none
        else
          { model with HoveredOver = cell }, Cmd.ofMsg CalculateRange

  module Debug =

    open Mibo.Elmish.Next.Graphics2D
    open Raylib_cs

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
