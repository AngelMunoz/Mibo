namespace Mibo.Input

open System
open System.Numerics
open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// Input subscription helpers (MVU). Pure over IInput — backend-neutral.
//
// These depend only on IInput and GameContext (the Core kernel) plus the Sub
// machinery in Mibo.Mvu. They work on every backend once an IInput is
// registered (which the runtime host does when Program.withInput /
// withInputMapper is set). The IInput service accessors themselves live in
// Mibo.Core (module Input).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Keyboard subscription helpers.</summary>
module Keyboard =
  let listen
    (onPressed: KeyCode -> 'Msg)
    (onReleased: KeyCode -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Keyboard/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Pressed do
            dispatch(onPressed k)

          for k in delta.Released do
            dispatch(onReleased k))

    Sub.Active(subId, subscribe)

  let onPressed (handler: KeyCode -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Keyboard/onPressed"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Pressed do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

  let onReleased (handler: KeyCode -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Keyboard/onReleased"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Released do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

/// <summary>Mouse subscription helpers.</summary>
module Mouse =
  let listen (handler: MouseDelta -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta -> dispatch(handler delta))

    Sub.Active(subId, subscribe)

  let onMove (handler: Vector2 -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onMove"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if
            delta.PositionDelta.X <> 0.0f || delta.PositionDelta.Y <> 0.0f
          then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onButton
    (handler: MouseButtonCode * Vector2 -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onButton"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          for btn in delta.Buttons.Pressed do
            dispatch(handler(btn, delta.Position)))

    Sub.Active(subId, subscribe)

  let onLeftClick (handler: Vector2 -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onLeftClick"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Left then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onRightClick (handler: Vector2 -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onRightClick"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Right then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onScroll (handler: float32 -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onScroll"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.ScrollDelta <> 0.0f then
            dispatch(handler delta.ScrollDelta))

    Sub.Active(subId, subscribe)

/// <summary>Touch subscription helpers.</summary>
module Touch =
  let listen (handler: TouchPoint[] -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Touch/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .TouchDelta.Subscribe(fun delta -> dispatch(handler delta.Touches))

    Sub.Active(subId, subscribe)

/// <summary>Gamepad subscription helpers.</summary>
module Gamepad =
  let listen (handler: GamepadDelta -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadDelta.Subscribe(fun delta -> dispatch(handler delta))

    Sub.Active(subId, subscribe)

  let listenPlayer
    (player: int)
    (handler: GamepadDelta -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString $"Mibo/Input/Gamepad/listenPlayer/{player}"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadDelta.Subscribe(fun delta ->
          if delta.PlayerIndex = player then
            dispatch(handler delta))

    Sub.Active(subId, subscribe)

  let onConnected (handler: int -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/onConnected"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadConnection.Subscribe(fun conn ->
          if conn.IsConnected then
            dispatch(handler conn.PlayerIndex))

    Sub.Active(subId, subscribe)

  let onDisconnected (handler: int -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/onDisconnected"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadConnection.Subscribe(fun conn ->
          if not conn.IsConnected then
            dispatch(handler conn.PlayerIndex))

    Sub.Active(subId, subscribe)

  let onConnectionChange
    (handler: GamepadConnection -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/onConnectionChange"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadConnection.Subscribe(fun conn -> dispatch(handler conn))

    Sub.Active(subId, subscribe)

/// <summary>Gesture subscription helpers.</summary>
module Gesture =
  let listen (handler: GestureDelta -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gesture/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GestureDelta.Subscribe(fun delta -> dispatch(handler delta))

    Sub.Active(subId, subscribe)
