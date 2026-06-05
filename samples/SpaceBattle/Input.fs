namespace SpaceBattle

open System.Numerics
open Mibo.Input
open Raylib_cs

open SpaceBattle.Types

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown
  | GetInfo
  | Select

[<Struct>]
type MouseAction =
  | Zoom of zoom: float32
  | Select of Vector2
  | GetInfo of Vector2
  | MovedTo of Vector2

type InputModel = {
  State: ActionState<GameAction>
  HoveredOver: struct (int * int) voption
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

  let init = {
    State = ActionState.empty
    HoveredOver = ValueNone
  }

  let updateHover
    (pos: Vector2)
    (camera: Camera2D)
    (map: Mibo.Layout.HexGrid<Tile>)
    (model: InputModel)
    : InputModel =
    let worldPos = Raylib.GetScreenToWorld2D(pos, camera)
    let cell = map |> Mibo.Layout.Hex2DSpatial.worldToCell worldPos
    { model with HoveredOver = cell }
