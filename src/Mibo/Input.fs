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
/// This service is intended to be registered into `Game.Services` by `Program.withInput`.
type IInput =
  abstract KeyboardDelta: IObservable<KeyboardDelta>
  abstract MouseDelta: IObservable<MouseDelta>
  abstract TouchDelta: IObservable<TouchDelta>

// ─────────────────────────────────────────────────────────────────────────────
// Input Polling Functions (module-level, composable)
// ─────────────────────────────────────────────────────────────────────────────

module InputPolling =

  // Cached list of all Keys to avoid per-frame allocations from GetPressedKeys()
  let private allKeys: Keys[] = Enum.GetValues(typeof<Keys>) :?> Keys[]

  let pollKeyboard
    (prevKeyboard: byref<KeyboardState>)
    (pressedBuf: ResizeArray<Keys>)
    (releasedBuf: ResizeArray<Keys>)
    (trigger: KeyboardDelta -> unit)
    =
    let curr = Keyboard.GetState()

    pressedBuf.Clear()
    releasedBuf.Clear()

    for i = 0 to allKeys.Length - 1 do
      let k = allKeys[i]

      if k <> Keys.None then
        let wasDown = prevKeyboard.IsKeyDown(k)
        let isDown = curr.IsKeyDown(k)

        if wasDown && not isDown then
          releasedBuf.Add(k)
        elif isDown && not wasDown then
          pressedBuf.Add(k)

    if pressedBuf.Count > 0 || releasedBuf.Count > 0 then
      trigger {
        Pressed = pressedBuf.ToArray()
        Released = releasedBuf.ToArray()
      }

    prevKeyboard <- curr

  let pollMouse (prevMouse: byref<MouseState>) (trigger: MouseDelta -> unit) =
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
      trigger {
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

  let pollTouch(trigger: TouchDelta -> unit) =
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

      trigger { Touches = points }

// ─────────────────────────────────────────────────────────────────────────────
// Input Component Factory
// ─────────────────────────────────────────────────────────────────────────────

module Input =

  /// Create an input component that polls hardware each frame and publishes deltas via IInput.
  let create(game: Game) =
    // Mutable state captured in closure
    let keyboardDelta = Event<KeyboardDelta>()
    let mouseDelta = Event<MouseDelta>()
    let touchDelta = Event<TouchDelta>()

    let pressedBuf = ResizeArray<Keys>(8)
    let releasedBuf = ResizeArray<Keys>(8)

    let mutable prevKeyboard = KeyboardState()
    let mutable prevMouse = MouseState()

    let input =
      { new GameComponent(game) with
          override _.Update(gameTime) =
            InputPolling.pollKeyboard
              &prevKeyboard
              pressedBuf
              releasedBuf
              keyboardDelta.Trigger

            InputPolling.pollMouse &prevMouse mouseDelta.Trigger
            InputPolling.pollTouch touchDelta.Trigger
            base.Update(gameTime)
        interface IInput with
          member _.KeyboardDelta = keyboardDelta.Publish
          member _.MouseDelta = mouseDelta.Publish
          member _.TouchDelta = touchDelta.Publish
      }

    input

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
    let subId = SubId.ofString "Mibo/Input/Keyboard/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Pressed do
            dispatch(onPressed k)

          for k in delta.Released do
            dispatch(onReleased k))

    Sub.Active(subId, subscribe)

  let onPressed (handler: Keys -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Keyboard/onPressed"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Pressed do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

  let onReleased (handler: Keys -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Keyboard/onReleased"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Released do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

module Mouse =

  let listen (handler: MouseDelta -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta -> dispatch(handler delta))

    Sub.Active(subId, subscribe)

  let onMove (handler: Point -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onMove"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.PositionDelta.X <> 0 || delta.PositionDelta.Y <> 0 then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onLeftClick (handler: Point -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onLeftClick"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.Buttons.LeftPressed then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onRightClick (handler: Point -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onRightClick"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.Buttons.RightPressed then
            dispatch(handler delta.Position))

    Sub.Active(subId, subscribe)

  let onScroll (handler: int -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Mouse/onScroll"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .MouseDelta.Subscribe(fun delta ->
          if delta.ScrollDelta <> 0 then
            dispatch(handler delta.ScrollDelta))

    Sub.Active(subId, subscribe)

module Touch =
  let listen (handler: TouchPoint[] -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Touch/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .TouchDelta.Subscribe(fun delta -> dispatch(handler delta.Touches))

    Sub.Active(subId, subscribe)
