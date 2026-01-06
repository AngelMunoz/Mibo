namespace Mibo.Input

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch
open Mibo.Elmish

[<Struct>]
type KeyboardDelta = { Pressed: Keys[]; Released: Keys[] }

[<Struct>]
type MouseButtons = {
  LeftPressed: bool
  LeftReleased: bool
  RightPressed: bool
  RightReleased: bool
  MiddlePressed: bool
  MiddleReleased: bool
}

[<Struct>]
type MouseDelta = {
  Position: Point
  PositionDelta: Point
  Buttons: MouseButtons
  ScrollDelta: int
}

[<Struct>]
type TouchPoint = {
  Id: int
  Position: Vector2
  State: TouchLocationState
}

[<Struct>]
type TouchDelta = { Touches: TouchPoint[] }

/// Per-game input service.
///
/// This service is intended to be registered into `Game.Services` by `Program.withInput`.
type IInput =
  abstract KeyboardDelta: IObservable<KeyboardDelta>
  abstract MouseDelta: IObservable<MouseDelta>
  abstract TouchDelta: IObservable<TouchDelta>

type InputService() =
  let keyboardDelta = Event<KeyboardDelta>()
  let mouseDelta = Event<MouseDelta>()
  let touchDelta = Event<TouchDelta>()

  let mutable prevKeyboard = KeyboardState()
  let mutable prevMouse = MouseState()

  let updateKeyboard() =
    let curr = Keyboard.GetState()
    let pKeys = prevKeyboard.GetPressedKeys()
    let cKeys = curr.GetPressedKeys()

    let pressed = ResizeArray<Keys>()
    let released = ResizeArray<Keys>()

    for k in pKeys do
      if not(curr.IsKeyDown(k)) then
        released.Add(k)

    for k in cKeys do
      if not(prevKeyboard.IsKeyDown(k)) then
        pressed.Add(k)

    if pressed.Count > 0 || released.Count > 0 then
      keyboardDelta.Trigger(
        {
          Pressed = pressed.ToArray()
          Released = released.ToArray()
        }
      )

    prevKeyboard <- curr

  let updateMouse() =
    let curr = Mouse.GetState()

    let posChanged = curr.X <> prevMouse.X || curr.Y <> prevMouse.Y
    let scrollChanged = curr.ScrollWheelValue <> prevMouse.ScrollWheelValue

    let leftPressed =
      curr.LeftButton = ButtonState.Pressed
      && prevMouse.LeftButton = ButtonState.Released

    let leftReleased =
      curr.LeftButton = ButtonState.Released
      && prevMouse.LeftButton = ButtonState.Pressed

    let rightPressed =
      curr.RightButton = ButtonState.Pressed
      && prevMouse.RightButton = ButtonState.Released

    let rightReleased =
      curr.RightButton = ButtonState.Released
      && prevMouse.RightButton = ButtonState.Pressed

    let middlePressed =
      curr.MiddleButton = ButtonState.Pressed
      && prevMouse.MiddleButton = ButtonState.Released

    let middleReleased =
      curr.MiddleButton = ButtonState.Released
      && prevMouse.MiddleButton = ButtonState.Pressed

    let hasButtonChange =
      leftPressed
      || leftReleased
      || rightPressed
      || rightReleased
      || middlePressed
      || middleReleased

    if posChanged || scrollChanged || hasButtonChange then
      mouseDelta.Trigger {
        Position = Point(curr.X, curr.Y)
        PositionDelta = Point(curr.X - prevMouse.X, curr.Y - prevMouse.Y)
        Buttons = {
          LeftPressed = leftPressed
          LeftReleased = leftReleased
          RightPressed = rightPressed
          RightReleased = rightReleased
          MiddlePressed = middlePressed
          MiddleReleased = middleReleased
        }
        ScrollDelta = curr.ScrollWheelValue - prevMouse.ScrollWheelValue
      }

    prevMouse <- curr

  let updateTouch() =
    let touches = TouchPanel.GetState()

    if touches.Count > 0 then
      let points = Array.zeroCreate<TouchPoint> touches.Count

      for i = 0 to touches.Count - 1 do
        let t = touches.[i]

        points.[i] <- {
          Id = t.Id
          Position = t.Position
          State = t.State
        }

      touchDelta.Trigger({ Touches = points })

  interface IInput with
    member _.KeyboardDelta = keyboardDelta.Publish
    member _.MouseDelta = mouseDelta.Publish
    member _.TouchDelta = touchDelta.Publish

  interface IEngineService with
    member _.Update(_ctx, _gameTime) =
      updateKeyboard()
      updateMouse()
      updateTouch()

module Input =

  let tryGetService(ctx: GameContext) : IInput voption =
    let svc = ctx.Game.Services.GetService(typeof<IInput>)

    match svc with
    | null -> ValueNone
    | :? IInput as i -> ValueSome i
    | _ -> ValueNone

  let getService(ctx: GameContext) : IInput =
    match tryGetService ctx with
    | ValueSome i -> i
    | ValueNone ->
      failwith
        "IInput service not registered. Add Program.withInput to your program."

module Keyboard =
  let listen
    (onPressed: Keys -> 'Msg)
    (onReleased: Keys -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Keyboard"; "listen" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Pressed do
            dispatch(onPressed k)

          for k in delta.Released do
            dispatch(onReleased k))

    Sub.Active(subId, subscribe)

  let onPressed (handler: Keys -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Keyboard"; "onPressed" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Pressed do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

  let onReleased (handler: Keys -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Keyboard"; "onReleased" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Released do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

module Mouse =

  let listen (handler: MouseDelta -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Mouse"; "listen" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta -> dispatch(handler delta))

    Sub.Active(subId, subscribe)

  let onMove (handler: Point -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Mouse"; "onMove" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.PositionDelta.X <> 0 || delta.PositionDelta.Y <> 0 then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onLeftClick (handler: Point -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Mouse"; "onLeftClick" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.Buttons.LeftPressed then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onRightClick (handler: Point -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Mouse"; "onRightClick" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.Buttons.RightPressed then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onScroll (handler: int -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Mouse"; "onScroll" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.ScrollDelta <> 0 then
            dispatch(handler delta.ScrollDelta))

    Sub.Active(subId, subscribe)

module Touch =
  let listen (handler: TouchPoint[] -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = [ "Mibo"; "Input"; "Touch"; "listen" ]

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .TouchDelta.Subscribe(fun delta -> dispatch(handler delta.Touches))

    Sub.Active(subId, subscribe)
