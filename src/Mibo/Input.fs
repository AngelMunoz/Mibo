namespace Mibo.Input

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch
open Mibo.Elmish

/// <summary>
/// Keyboard state delta containing keys that changed this frame.
/// </summary>
/// <remarks>
/// This struct is emitted when keyboard state changes. Both arrays may be
/// empty if only modifier state changed without key presses.
/// </remarks>
[<Struct>]
type KeyboardDelta = {
  /// Keys that were pressed this frame (were up, now down).
  Pressed: Keys[]
  /// Keys that were released this frame (were down, now up).
  Released: Keys[]
}

/// <summary>Mouse button state changes for a single frame.</summary>
[<Struct>]
type MouseButtons = {
  LeftPressed: bool
  LeftReleased: bool
  RightPressed: bool
  RightReleased: bool
  MiddlePressed: bool
  MiddleReleased: bool
}

/// <summary>
/// Mouse state delta containing position and button changes.
/// </summary>
/// <remarks>
/// This struct is emitted when mouse state changes (movement, button press,
/// or scroll wheel).
/// </remarks>
[<Struct>]
type MouseDelta = {
  /// Current mouse position in screen coordinates.
  Position: Point
  /// Change in position since last frame.
  PositionDelta: Point
  /// Button state changes.
  Buttons: MouseButtons
  /// Scroll wheel delta (positive = up, negative = down).
  ScrollDelta: int
}

/// <summary>A single touch point for touch input.</summary>
[<Struct>]
type TouchPoint = {
  /// Unique identifier for tracking this touch across frames.
  Id: int
  /// Touch position in screen coordinates.
  Position: Vector2
  /// Current state of the touch (Pressed, Moved, Released).
  State: TouchLocationState
}

/// <summary>Touch input state containing all active touch points.</summary>
[<Struct>]
type TouchDelta = { Touches: TouchPoint[] }

/// <summary>Gamepad button state changes for a single frame.</summary>
[<Struct>]
type GamepadButtons = {
  /// Buttons that were pressed this frame.
  Pressed: Buttons[]
  /// Buttons that were released this frame.
  Released: Buttons[]
}

/// <summary>
/// Gamepad analog input values (thumbsticks and triggers).
/// </summary>
/// <remarks>
/// All values are normalized: thumbsticks range from -1 to 1,
/// triggers range from 0 to 1.
/// </remarks>
[<Struct>]
type GamepadAnalog = {
  LeftThumbstick: Vector2
  RightThumbstick: Vector2
  LeftTrigger: float32
  RightTrigger: float32
}

/// <summary>
/// Per-player gamepad delta containing button and analog changes.
/// </summary>
/// <remarks>
/// Only emitted for connected controllers when input changes.
/// </remarks>
[<Struct>]
type GamepadDelta = {
  /// Which player this input is from (One through Four).
  PlayerIndex: PlayerIndex
  /// Button state changes.
  Buttons: GamepadButtons
  /// Current analog input values.
  Analog: GamepadAnalog
}

/// <summary>Gamepad connection state change event.</summary>
[<Struct>]
type GamepadConnection = {
  PlayerIndex: PlayerIndex
  IsConnected: bool
}

/// <summary>
/// Per-game input service providing reactive observables for hardware input.
/// </summary>
/// <remarks>
/// Subscribe to these observables to receive input deltas. The service is
/// typically registered by <see cref="M:Mibo.Elmish.Program.withInput"/> and accessed via the
/// Keyboard, Mouse, Touch, and Gamepad subscription modules.
/// </remarks>
type IInput =
  /// Emits when keyboard state changes.
  abstract KeyboardDelta: IObservable<KeyboardDelta>
  /// Emits when mouse state changes (position, buttons, or scroll).
  abstract MouseDelta: IObservable<MouseDelta>
  /// Emits when touch input is detected.
  abstract TouchDelta: IObservable<TouchDelta>
  /// Emits when gamepad input changes (buttons or analog).
  abstract GamepadDelta: IObservable<GamepadDelta>
  /// Emits when a gamepad connects or disconnects.
  abstract GamepadConnection: IObservable<GamepadConnection>

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

  // Cached list of all Buttons to check
  let private allButtons: Buttons[] =
    Enum.GetValues(typeof<Buttons>) :?> Buttons[]

  let pollGamepad
    (prevStates: GamePadState[])
    (pressedBuf: ResizeArray<Buttons>)
    (releasedBuf: ResizeArray<Buttons>)
    (triggerDelta: GamepadDelta -> unit)
    (triggerConnection: GamepadConnection -> unit)
    =
    for i = 0 to 3 do
      let playerIndex = enum<PlayerIndex>(i)
      let curr = GamePad.GetState(playerIndex)
      let prev = prevStates.[i]

      // Connection state change detection
      if prev.IsConnected <> curr.IsConnected then
        triggerConnection {
          PlayerIndex = playerIndex
          IsConnected = curr.IsConnected
        }

      // Only poll input if connected
      if curr.IsConnected then
        pressedBuf.Clear()
        releasedBuf.Clear()

        for btn in allButtons do
          let wasDown = prev.IsButtonDown(btn)
          let isDown = curr.IsButtonDown(btn)

          if isDown && not wasDown then
            pressedBuf.Add(btn)
          elif wasDown && not isDown then
            releasedBuf.Add(btn)

        // Emit delta if there's any change
        let hasButtonChange = pressedBuf.Count > 0 || releasedBuf.Count > 0

        let hasAnalogChange =
          curr.ThumbSticks.Left <> prev.ThumbSticks.Left
          || curr.ThumbSticks.Right <> prev.ThumbSticks.Right
          || curr.Triggers.Left <> prev.Triggers.Left
          || curr.Triggers.Right <> prev.Triggers.Right

        if hasButtonChange || hasAnalogChange then
          triggerDelta {
            PlayerIndex = playerIndex
            Buttons = {
              Pressed = pressedBuf.ToArray()
              Released = releasedBuf.ToArray()
            }
            Analog = {
              LeftThumbstick = curr.ThumbSticks.Left
              RightThumbstick = curr.ThumbSticks.Right
              LeftTrigger = curr.Triggers.Left
              RightTrigger = curr.Triggers.Right
            }
          }

      prevStates.[i] <- curr

// ─────────────────────────────────────────────────────────────────────────────
// Input Component Factory
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Factory and service access for the core input polling component.
/// </summary>
module Input =

  /// <summary>
  /// Creates an input component that polls hardware each frame.
  /// </summary>
  /// <remarks>
  /// The component implements both <see cref="T:Microsoft.Xna.Framework.IGameComponent"/> (for the MonoGame lifecycle)
  /// and <see cref="T:Mibo.Input.IInput"/> (for reactive input access). Normally called internally by
  /// <see cref="M:Mibo.Elmish.Program.withInput"/>.
  /// </remarks>
  let create(game: Game) =
    // Mutable state captured in closure
    let keyboardDelta = Event<KeyboardDelta>()
    let mouseDelta = Event<MouseDelta>()
    let touchDelta = Event<TouchDelta>()
    let gamepadDelta = Event<GamepadDelta>()
    let gamepadConnection = Event<GamepadConnection>()

    let pressedKeysBuf = ResizeArray<Keys>(8)
    let releasedKeysBuf = ResizeArray<Keys>(8)
    let pressedButtonsBuf = ResizeArray<Buttons>(8)
    let releasedButtonsBuf = ResizeArray<Buttons>(8)

    let mutable prevKeyboard = KeyboardState()
    let mutable prevMouse = MouseState()
    let prevGamepads = Array.zeroCreate<GamePadState>(4)

    let input =
      { new GameComponent(game) with
          override _.Update(gameTime) =
            InputPolling.pollKeyboard
              &prevKeyboard
              pressedKeysBuf
              releasedKeysBuf
              keyboardDelta.Trigger

            InputPolling.pollMouse &prevMouse mouseDelta.Trigger
            InputPolling.pollTouch touchDelta.Trigger

            InputPolling.pollGamepad
              prevGamepads
              pressedButtonsBuf
              releasedButtonsBuf
              gamepadDelta.Trigger
              gamepadConnection.Trigger

            base.Update(gameTime)
        interface IInput with
          member _.KeyboardDelta = keyboardDelta.Publish
          member _.MouseDelta = mouseDelta.Publish
          member _.TouchDelta = touchDelta.Publish
          member _.GamepadDelta = gamepadDelta.Publish
          member _.GamepadConnection = gamepadConnection.Publish
      }

    input

  /// Attempts to get the IInput service from the game context.
  ///
  /// Returns ValueNone if the service is not registered (i.e., Program.withInput wasn't used).
  let tryGetService(ctx: GameContext) : IInput voption =
    let svc = ctx.Game.Services.GetService(typeof<IInput>)

    match svc with
    | null -> ValueNone
    | :? IInput as i -> ValueSome i
    | _ -> ValueNone

  /// Gets the IInput service from the game context.
  ///
  /// Throws if the service is not registered. Use Program.withInput to ensure registration.
  let getService(ctx: GameContext) : IInput =
    match tryGetService ctx with
    | ValueSome i -> i
    | ValueNone ->
      failwith
        "IInput service not registered. Add Program.withInput to your program."

/// <summary>
/// Keyboard input subscriptions for Elmish.
/// </summary>
/// <remarks>
/// These functions create subscriptions that dispatch messages when keyboard
/// state changes. Requires <see cref="M:Mibo.Elmish.Program.withInput"/> to be applied.
/// </remarks>
/// <example>
/// <code>
/// let subscribe ctx model =
///     Sub.batch [
///         Keyboard.onPressed KeyPressed ctx
///         Keyboard.onReleased KeyReleased ctx
///     ]
/// </code>
/// </example>
module Keyboard =

  /// <summary>
  /// Subscribes to both key press and release events.
  /// </summary>
  /// <remarks>
  /// <para>The subscription ID is shared, so only one <c>listen</c> subscription
  /// can be active at a time per Elmish program.</para>
  /// </remarks>
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

  /// <summary>
  /// Subscribes to key press events only.
  /// </summary>
  /// <remarks>
  /// Dispatches the handler for each key that was pressed this frame.
  /// </remarks>
  let onPressed (handler: Keys -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Keyboard/onPressed"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Pressed do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

  /// <summary>Subscribes to key release events only.</summary>
  let onReleased (handler: Keys -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Keyboard/onReleased"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .KeyboardDelta.Subscribe(fun delta ->
          for k in delta.Released do
            dispatch(handler k))

    Sub.Active(subId, subscribe)

/// <summary>Mouse input subscriptions for Elmish.</summary>
module Mouse =

  /// <summary>Subscribes to all mouse events (position, buttons, and scroll).</summary>
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

  /// <summary>Subscribes to left-click events at a specific position.</summary>
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

/// <summary>Touch input subscriptions for Elmish.</summary>
module Touch =
  /// <summary>Subscribes to all touch events.</summary>
  let listen (handler: TouchPoint[] -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Touch/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .TouchDelta.Subscribe(fun delta -> dispatch(handler delta.Touches))

    Sub.Active(subId, subscribe)

/// <summary>Gamepad input subscriptions for Elmish.</summary>
module Gamepad =
  /// <summary>Listen to all gamepad input changes (buttons and analog).</summary>
  let listen (handler: GamepadDelta -> 'Msg) (ctx: GameContext) : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/listen"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadDelta.Subscribe(fun delta -> dispatch(handler delta))

    Sub.Active(subId, subscribe)

  /// <summary>Listen to gamepad input for a specific player.</summary>
  let listenPlayer
    (player: PlayerIndex)
    (handler: GamepadDelta -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString $"Mibo/Input/Gamepad/listenPlayer/{int player}"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadDelta.Subscribe(fun delta ->
          if delta.PlayerIndex = player then
            dispatch(handler delta))

    Sub.Active(subId, subscribe)

  /// <summary>Called when a gamepad connects.</summary>
  let onConnected
    (handler: PlayerIndex -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/onConnected"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadConnection.Subscribe(fun conn ->
          if conn.IsConnected then
            dispatch(handler conn.PlayerIndex))

    Sub.Active(subId, subscribe)

  /// <summary>Called when a gamepad disconnects.</summary>
  let onDisconnected
    (handler: PlayerIndex -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/onDisconnected"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadConnection.Subscribe(fun conn ->
          if not conn.IsConnected then
            dispatch(handler conn.PlayerIndex))

    Sub.Active(subId, subscribe)

  /// <summary>Listen to all connection/disconnection events.</summary>
  let onConnectionChange
    (handler: GamepadConnection -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/Gamepad/onConnectionChange"

    let subscribe(dispatch: Dispatch<'Msg>) =
      (Input.getService ctx)
        .GamepadConnection.Subscribe(fun conn -> dispatch(handler conn))

    Sub.Active(subId, subscribe)
