namespace Mibo.Input

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// Raylib ↔ Core input translation.
//
// This is the ONLY place in the raylib backend where Raylib_cs.KeyboardKey /
// MouseButton / GamepadButton / Gesture touch the Core-neutral code types.
// Everything else in the backend works in Core codes.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Functions for translating between raylib's <c>KeyboardKey</c> and Mibo's backend-neutral <c>KeyCode</c>.</summary>
module KeyCode =
  /// <summary>Maps a raylib <c>KeyboardKey</c> to the backend-neutral <see cref="T:Mibo.Input.KeyCode"/>.
  /// Returns <see cref="F:Mibo.Input.KeyCode.Unknown"/> for any raylib key with no logical equivalent.</summary>
  let ofRaylibKey(k: KeyboardKey) : KeyCode =
    match k with
    // Letters
    | KeyboardKey.A -> KeyCode.A
    | KeyboardKey.B -> KeyCode.B
    | KeyboardKey.C -> KeyCode.C
    | KeyboardKey.D -> KeyCode.D
    | KeyboardKey.E -> KeyCode.E
    | KeyboardKey.F -> KeyCode.F
    | KeyboardKey.G -> KeyCode.G
    | KeyboardKey.H -> KeyCode.H
    | KeyboardKey.I -> KeyCode.I
    | KeyboardKey.J -> KeyCode.J
    | KeyboardKey.K -> KeyCode.K
    | KeyboardKey.L -> KeyCode.L
    | KeyboardKey.M -> KeyCode.M
    | KeyboardKey.N -> KeyCode.N
    | KeyboardKey.O -> KeyCode.O
    | KeyboardKey.P -> KeyCode.P
    | KeyboardKey.Q -> KeyCode.Q
    | KeyboardKey.R -> KeyCode.R
    | KeyboardKey.S -> KeyCode.S
    | KeyboardKey.T -> KeyCode.T
    | KeyboardKey.U -> KeyCode.U
    | KeyboardKey.V -> KeyCode.V
    | KeyboardKey.W -> KeyCode.W
    | KeyboardKey.X -> KeyCode.X
    | KeyboardKey.Y -> KeyCode.Y
    | KeyboardKey.Z -> KeyCode.Z
    // Digits
    | KeyboardKey.Zero -> KeyCode.D0
    | KeyboardKey.One -> KeyCode.D1
    | KeyboardKey.Two -> KeyCode.D2
    | KeyboardKey.Three -> KeyCode.D3
    | KeyboardKey.Four -> KeyCode.D4
    | KeyboardKey.Five -> KeyCode.D5
    | KeyboardKey.Six -> KeyCode.D6
    | KeyboardKey.Seven -> KeyCode.D7
    | KeyboardKey.Eight -> KeyCode.D8
    | KeyboardKey.Nine -> KeyCode.D9
    // Function keys
    | KeyboardKey.F1 -> KeyCode.F1
    | KeyboardKey.F2 -> KeyCode.F2
    | KeyboardKey.F3 -> KeyCode.F3
    | KeyboardKey.F4 -> KeyCode.F4
    | KeyboardKey.F5 -> KeyCode.F5
    | KeyboardKey.F6 -> KeyCode.F6
    | KeyboardKey.F7 -> KeyCode.F7
    | KeyboardKey.F8 -> KeyCode.F8
    | KeyboardKey.F9 -> KeyCode.F9
    | KeyboardKey.F10 -> KeyCode.F10
    | KeyboardKey.F11 -> KeyCode.F11
    | KeyboardKey.F12 -> KeyCode.F12
    // Arrows / navigation
    | KeyboardKey.Right -> KeyCode.Right
    | KeyboardKey.Left -> KeyCode.Left
    | KeyboardKey.Down -> KeyCode.Down
    | KeyboardKey.Up -> KeyCode.Up
    | KeyboardKey.PageUp -> KeyCode.PageUp
    | KeyboardKey.PageDown -> KeyCode.PageDown
    | KeyboardKey.Home -> KeyCode.Home
    | KeyboardKey.End -> KeyCode.End
    | KeyboardKey.Insert -> KeyCode.Insert
    | KeyboardKey.Delete -> KeyCode.Delete
    // Editing / control
    | KeyboardKey.Space -> KeyCode.Space
    | KeyboardKey.Enter -> KeyCode.Enter
    | KeyboardKey.Escape -> KeyCode.Escape
    | KeyboardKey.Tab -> KeyCode.Tab
    | KeyboardKey.Backspace -> KeyCode.Backspace
    | KeyboardKey.CapsLock -> KeyCode.CapsLock
    // Modifiers
    | KeyboardKey.LeftShift -> KeyCode.LeftShift
    | KeyboardKey.RightShift -> KeyCode.RightShift
    | KeyboardKey.LeftControl -> KeyCode.LeftControl
    | KeyboardKey.RightControl -> KeyCode.RightControl
    | KeyboardKey.LeftAlt -> KeyCode.LeftAlt
    | KeyboardKey.RightAlt -> KeyCode.RightAlt
    | KeyboardKey.LeftSuper -> KeyCode.LeftSuper
    | KeyboardKey.RightSuper -> KeyCode.RightSuper
    // Punctuation / symbols (US layout)
    | KeyboardKey.Grave -> KeyCode.Grave
    | KeyboardKey.Minus -> KeyCode.Minus
    | KeyboardKey.Equal -> KeyCode.Equal
    | KeyboardKey.LeftBracket -> KeyCode.LeftBracket
    | KeyboardKey.RightBracket -> KeyCode.RightBracket
    | KeyboardKey.Backslash -> KeyCode.Backslash
    | KeyboardKey.Semicolon -> KeyCode.Semicolon
    | KeyboardKey.Apostrophe -> KeyCode.Apostrophe
    | KeyboardKey.Comma -> KeyCode.Comma
    | KeyboardKey.Period -> KeyCode.Period
    | KeyboardKey.Slash -> KeyCode.Slash
    // Keypad
    | KeyboardKey.Kp0 -> KeyCode.Kp0
    | KeyboardKey.Kp1 -> KeyCode.Kp1
    | KeyboardKey.Kp2 -> KeyCode.Kp2
    | KeyboardKey.Kp3 -> KeyCode.Kp3
    | KeyboardKey.Kp4 -> KeyCode.Kp4
    | KeyboardKey.Kp5 -> KeyCode.Kp5
    | KeyboardKey.Kp6 -> KeyCode.Kp6
    | KeyboardKey.Kp7 -> KeyCode.Kp7
    | KeyboardKey.Kp8 -> KeyCode.Kp8
    | KeyboardKey.Kp9 -> KeyCode.Kp9
    | KeyboardKey.KpDecimal -> KeyCode.KpDecimal
    | KeyboardKey.KpDivide -> KeyCode.KpDivide
    | KeyboardKey.KpMultiply -> KeyCode.KpMultiply
    | KeyboardKey.KpSubtract -> KeyCode.KpSubtract
    | KeyboardKey.KpAdd -> KeyCode.KpAdd
    | KeyboardKey.KpEnter -> KeyCode.KpEnter
    | KeyboardKey.KpEqual -> KeyCode.KpEqual
    // Media / system
    | KeyboardKey.PrintScreen -> KeyCode.PrintScreen
    | KeyboardKey.ScrollLock -> KeyCode.ScrollLock
    | KeyboardKey.Pause -> KeyCode.Pause
    | KeyboardKey.Menu -> KeyCode.Menu
    // Locks
    | KeyboardKey.NumLock -> KeyCode.NumLock
    | _ -> KeyCode.Unknown

  /// <summary>Maps a backend-neutral <see cref="T:Mibo.Input.KeyCode"/> back to raylib's <c>KeyboardKey</c>.
  /// Returns <c>KeyboardKey.Null</c> for <see cref="F:Mibo.Input.KeyCode.Unknown"/>.</summary>
  let toRaylibKey(k: KeyCode) : KeyboardKey =
    match k with
    | KeyCode.A -> KeyboardKey.A
    | KeyCode.B -> KeyboardKey.B
    | KeyCode.C -> KeyboardKey.C
    | KeyCode.D -> KeyboardKey.D
    | KeyCode.E -> KeyboardKey.E
    | KeyCode.F -> KeyboardKey.F
    | KeyCode.G -> KeyboardKey.G
    | KeyCode.H -> KeyboardKey.H
    | KeyCode.I -> KeyboardKey.I
    | KeyCode.J -> KeyboardKey.J
    | KeyCode.K -> KeyboardKey.K
    | KeyCode.L -> KeyboardKey.L
    | KeyCode.M -> KeyboardKey.M
    | KeyCode.N -> KeyboardKey.N
    | KeyCode.O -> KeyboardKey.O
    | KeyCode.P -> KeyboardKey.P
    | KeyCode.Q -> KeyboardKey.Q
    | KeyCode.R -> KeyboardKey.R
    | KeyCode.S -> KeyboardKey.S
    | KeyCode.T -> KeyboardKey.T
    | KeyCode.U -> KeyboardKey.U
    | KeyCode.V -> KeyboardKey.V
    | KeyCode.W -> KeyboardKey.W
    | KeyCode.X -> KeyboardKey.X
    | KeyCode.Y -> KeyboardKey.Y
    | KeyCode.Z -> KeyboardKey.Z
    | KeyCode.D0 -> KeyboardKey.Zero
    | KeyCode.D1 -> KeyboardKey.One
    | KeyCode.D2 -> KeyboardKey.Two
    | KeyCode.D3 -> KeyboardKey.Three
    | KeyCode.D4 -> KeyboardKey.Four
    | KeyCode.D5 -> KeyboardKey.Five
    | KeyCode.D6 -> KeyboardKey.Six
    | KeyCode.D7 -> KeyboardKey.Seven
    | KeyCode.D8 -> KeyboardKey.Eight
    | KeyCode.D9 -> KeyboardKey.Nine
    | KeyCode.F1 -> KeyboardKey.F1
    | KeyCode.F2 -> KeyboardKey.F2
    | KeyCode.F3 -> KeyboardKey.F3
    | KeyCode.F4 -> KeyboardKey.F4
    | KeyCode.F5 -> KeyboardKey.F5
    | KeyCode.F6 -> KeyboardKey.F6
    | KeyCode.F7 -> KeyboardKey.F7
    | KeyCode.F8 -> KeyboardKey.F8
    | KeyCode.F9 -> KeyboardKey.F9
    | KeyCode.F10 -> KeyboardKey.F10
    | KeyCode.F11 -> KeyboardKey.F11
    | KeyCode.F12 -> KeyboardKey.F12
    | KeyCode.Right -> KeyboardKey.Right
    | KeyCode.Left -> KeyboardKey.Left
    | KeyCode.Down -> KeyboardKey.Down
    | KeyCode.Up -> KeyboardKey.Up
    | KeyCode.PageUp -> KeyboardKey.PageUp
    | KeyCode.PageDown -> KeyboardKey.PageDown
    | KeyCode.Home -> KeyboardKey.Home
    | KeyCode.End -> KeyboardKey.End
    | KeyCode.Insert -> KeyboardKey.Insert
    | KeyCode.Delete -> KeyboardKey.Delete
    | KeyCode.Space -> KeyboardKey.Space
    | KeyCode.Enter -> KeyboardKey.Enter
    | KeyCode.Escape -> KeyboardKey.Escape
    | KeyCode.Tab -> KeyboardKey.Tab
    | KeyCode.Backspace -> KeyboardKey.Backspace
    | KeyCode.CapsLock -> KeyboardKey.CapsLock
    | KeyCode.LeftShift -> KeyboardKey.LeftShift
    | KeyCode.RightShift -> KeyboardKey.RightShift
    | KeyCode.LeftControl -> KeyboardKey.LeftControl
    | KeyCode.RightControl -> KeyboardKey.RightControl
    | KeyCode.LeftAlt -> KeyboardKey.LeftAlt
    | KeyCode.RightAlt -> KeyboardKey.RightAlt
    | KeyCode.LeftSuper -> KeyboardKey.LeftSuper
    | KeyCode.RightSuper -> KeyboardKey.RightSuper
    | KeyCode.Grave -> KeyboardKey.Grave
    | KeyCode.Minus -> KeyboardKey.Minus
    | KeyCode.Equal -> KeyboardKey.Equal
    | KeyCode.LeftBracket -> KeyboardKey.LeftBracket
    | KeyCode.RightBracket -> KeyboardKey.RightBracket
    | KeyCode.Backslash -> KeyboardKey.Backslash
    | KeyCode.Semicolon -> KeyboardKey.Semicolon
    | KeyCode.Apostrophe -> KeyboardKey.Apostrophe
    | KeyCode.Comma -> KeyboardKey.Comma
    | KeyCode.Period -> KeyboardKey.Period
    | KeyCode.Slash -> KeyboardKey.Slash
    | KeyCode.Kp0 -> KeyboardKey.Kp0
    | KeyCode.Kp1 -> KeyboardKey.Kp1
    | KeyCode.Kp2 -> KeyboardKey.Kp2
    | KeyCode.Kp3 -> KeyboardKey.Kp3
    | KeyCode.Kp4 -> KeyboardKey.Kp4
    | KeyCode.Kp5 -> KeyboardKey.Kp5
    | KeyCode.Kp6 -> KeyboardKey.Kp6
    | KeyCode.Kp7 -> KeyboardKey.Kp7
    | KeyCode.Kp8 -> KeyboardKey.Kp8
    | KeyCode.Kp9 -> KeyboardKey.Kp9
    | KeyCode.KpDecimal -> KeyboardKey.KpDecimal
    | KeyCode.KpDivide -> KeyboardKey.KpDivide
    | KeyCode.KpMultiply -> KeyboardKey.KpMultiply
    | KeyCode.KpSubtract -> KeyboardKey.KpSubtract
    | KeyCode.KpAdd -> KeyboardKey.KpAdd
    | KeyCode.KpEnter -> KeyboardKey.KpEnter
    | KeyCode.KpEqual -> KeyboardKey.KpEqual
    | KeyCode.PrintScreen -> KeyboardKey.PrintScreen
    | KeyCode.ScrollLock -> KeyboardKey.ScrollLock
    | KeyCode.Pause -> KeyboardKey.Pause
    | KeyCode.Menu -> KeyboardKey.Menu
    | KeyCode.NumLock -> KeyboardKey.NumLock
    | KeyCode.Unknown -> KeyboardKey.Null

/// <summary>Functions for translating between raylib's <c>MouseButton</c> and Mibo's backend-neutral <c>MouseButtonCode</c>.</summary>
module MouseButtonCode =
  let ofRaylibButton(b: MouseButton) : MouseButtonCode =
    match b with
    | MouseButton.Left -> MouseButtonCode.Left
    | MouseButton.Right -> MouseButtonCode.Right
    | MouseButton.Middle -> MouseButtonCode.Middle
    | MouseButton.Side -> MouseButtonCode.Extra1
    | MouseButton.Extra -> MouseButtonCode.Extra2
    | MouseButton.Forward -> MouseButtonCode.Extra3
    | MouseButton.Back -> MouseButtonCode.Extra4
    | _ -> MouseButtonCode.Unknown

  let toRaylibButton(b: MouseButtonCode) : MouseButton =
    match b with
    | MouseButtonCode.Left -> MouseButton.Left
    | MouseButtonCode.Right -> MouseButton.Right
    | MouseButtonCode.Middle -> MouseButton.Middle
    | MouseButtonCode.Extra1 -> MouseButton.Side
    | MouseButtonCode.Extra2 -> MouseButton.Extra
    | MouseButtonCode.Extra3 -> MouseButton.Forward
    | MouseButtonCode.Extra4 -> MouseButton.Back
    | MouseButtonCode.Unknown -> MouseButton.Left

/// <summary>Functions for translating between raylib's <c>GamepadButton</c> and Mibo's backend-neutral <c>GamepadButtonCode</c>.</summary>
/// <remarks>
/// raylib's <c>GamepadButton</c> enum uses "left face" for the D-pad cluster and
/// "right face" for the action-button cluster (Xbox: Y/B/A/X). The Core codes
/// abstract that naming so backends with different conventions (e.g. MonoGame /
/// XNA) can map onto the same logical buttons.
/// </remarks>
module GamepadButtonCode =
  let ofRaylibButton(b: GamepadButton) : GamepadButtonCode =
    match b with
    // raylib's "left face" cluster IS the D-pad
    | GamepadButton.LeftFaceUp -> GamepadButtonCode.DPadUp
    | GamepadButton.LeftFaceRight -> GamepadButtonCode.DPadRight
    | GamepadButton.LeftFaceDown -> GamepadButtonCode.DPadDown
    | GamepadButton.LeftFaceLeft -> GamepadButtonCode.DPadLeft
    // raylib's "right face" cluster is the action buttons (Y/B/A/X on Xbox)
    | GamepadButton.RightFaceUp -> GamepadButtonCode.FaceUp
    | GamepadButton.RightFaceRight -> GamepadButtonCode.FaceRight
    | GamepadButton.RightFaceDown -> GamepadButtonCode.FaceDown
    | GamepadButton.RightFaceLeft -> GamepadButtonCode.FaceLeft
    | GamepadButton.LeftTrigger1 -> GamepadButtonCode.LeftShoulder
    | GamepadButton.LeftTrigger2 -> GamepadButtonCode.LeftTrigger
    | GamepadButton.RightTrigger1 -> GamepadButtonCode.RightShoulder
    | GamepadButton.RightTrigger2 -> GamepadButtonCode.RightTrigger
    | GamepadButton.LeftThumb -> GamepadButtonCode.LeftStick
    | GamepadButton.RightThumb -> GamepadButtonCode.RightStick
    | GamepadButton.MiddleLeft -> GamepadButtonCode.Select
    | GamepadButton.Middle -> GamepadButtonCode.Home
    | GamepadButton.MiddleRight -> GamepadButtonCode.Start
    | _ -> GamepadButtonCode.Unknown

  let toRaylibButton(b: GamepadButtonCode) : GamepadButton =
    match b with
    | GamepadButtonCode.DPadUp -> GamepadButton.LeftFaceUp
    | GamepadButtonCode.DPadRight -> GamepadButton.LeftFaceRight
    | GamepadButtonCode.DPadDown -> GamepadButton.LeftFaceDown
    | GamepadButtonCode.DPadLeft -> GamepadButton.LeftFaceLeft
    | GamepadButtonCode.FaceUp -> GamepadButton.RightFaceUp
    | GamepadButtonCode.FaceRight -> GamepadButton.RightFaceRight
    | GamepadButtonCode.FaceDown -> GamepadButton.RightFaceDown
    | GamepadButtonCode.FaceLeft -> GamepadButton.RightFaceLeft
    | GamepadButtonCode.LeftShoulder -> GamepadButton.LeftTrigger1
    | GamepadButtonCode.RightShoulder -> GamepadButton.RightTrigger1
    | GamepadButtonCode.LeftTrigger -> GamepadButton.LeftTrigger2
    | GamepadButtonCode.RightTrigger -> GamepadButton.RightTrigger2
    | GamepadButtonCode.LeftStick -> GamepadButton.LeftThumb
    | GamepadButtonCode.RightStick -> GamepadButton.RightThumb
    | GamepadButtonCode.Select -> GamepadButton.MiddleLeft
    | GamepadButtonCode.Home -> GamepadButton.Middle
    | GamepadButtonCode.Start -> GamepadButton.MiddleRight
    | GamepadButtonCode.Unknown -> GamepadButton.Unknown

/// <summary>Functions for translating between raylib's <c>Gesture</c> and Mibo's backend-neutral <c>GestureKind</c>.</summary>
module GestureKind =
  let ofRaylibGesture(g: Gesture) : GestureKind =
    match g with
    | Gesture.Tap -> GestureKind.Tap
    | Gesture.DoubleTap -> GestureKind.DoubleTap
    | Gesture.Hold -> GestureKind.Hold
    | Gesture.Drag -> GestureKind.Drag
    | Gesture.SwipeRight -> GestureKind.SwipeRight
    | Gesture.SwipeLeft -> GestureKind.SwipeLeft
    | Gesture.SwipeUp -> GestureKind.SwipeUp
    | Gesture.SwipeDown -> GestureKind.SwipeDown
    | Gesture.PinchIn
    | Gesture.PinchOut -> GestureKind.Pinch
    | _ -> GestureKind.Unknown

  let toRaylibGesture(g: GestureKind) : Gesture =
    match g with
    | GestureKind.Tap -> Gesture.Tap
    | GestureKind.DoubleTap -> Gesture.DoubleTap
    | GestureKind.Hold -> Gesture.Hold
    | GestureKind.Drag -> Gesture.Drag
    | GestureKind.SwipeRight -> Gesture.SwipeRight
    | GestureKind.SwipeLeft -> Gesture.SwipeLeft
    | GestureKind.SwipeUp -> Gesture.SwipeUp
    | GestureKind.SwipeDown -> Gesture.SwipeDown
    | GestureKind.Pinch -> Gesture.PinchOut
    | GestureKind.Unknown -> Gesture.None

// ─────────────────────────────────────────────────────────────────────────────
// Input polling (raylib). Emits Core-neutral delta values.
// ─────────────────────────────────────────────────────────────────────────────

module InputPolling =
  // Only iterate over keys that are likely to produce a meaningful KeyCode —
  // the translation function returns Unknown for the rest, which we filter out.
  let private allKeyboardKeys =
    Enum.GetValues(typeof<KeyboardKey>) :?> KeyboardKey[]
    |> Array.filter(fun k ->
      k <> KeyboardKey.Null && int k >= 32 && int k <= 348)

  let private allMouseButtons =
    Enum.GetValues(typeof<MouseButton>) :?> MouseButton[]

  let private allGamepadButtons =
    Enum.GetValues(typeof<GamepadButton>) :?> GamepadButton[]
    |> Array.filter(fun b -> b <> GamepadButton.Unknown)

  let pollKeyboard
    (pressedBuf: ResizeArray<KeyCode>)
    (releasedBuf: ResizeArray<KeyCode>)
    (trigger: KeyboardDelta -> unit)
    =
    pressedBuf.Clear()
    releasedBuf.Clear()

    for k in allKeyboardKeys do
      if Raylib.IsKeyPressed(k).AsBool() then
        let code = KeyCode.ofRaylibKey k

        if code <> KeyCode.Unknown then
          pressedBuf.Add(code)
      elif Raylib.IsKeyReleased(k).AsBool() then
        let code = KeyCode.ofRaylibKey k

        if code <> KeyCode.Unknown then
          releasedBuf.Add(code)

    if pressedBuf.Count > 0 || releasedBuf.Count > 0 then
      trigger {
        Pressed = pressedBuf.ToArray()
        Released = releasedBuf.ToArray()
      }

  let pollMouse
    (pressedBuf: ResizeArray<MouseButtonCode>)
    (releasedBuf: ResizeArray<MouseButtonCode>)
    (trigger: MouseDelta -> unit)
    =
    pressedBuf.Clear()
    releasedBuf.Clear()

    for btn in allMouseButtons do
      if Raylib.IsMouseButtonPressed(btn).AsBool() then
        let code = MouseButtonCode.ofRaylibButton btn

        if code <> MouseButtonCode.Unknown then
          pressedBuf.Add code
      elif Raylib.IsMouseButtonReleased(btn).AsBool() then
        let code = MouseButtonCode.ofRaylibButton btn

        if code <> MouseButtonCode.Unknown then
          releasedBuf.Add code

    let pos = Raylib.GetMousePosition()
    let delta = Raylib.GetMouseDelta()
    let scroll = Raylib.GetMouseWheelMove()
    let scrollV = Raylib.GetMouseWheelMoveV()

    let hasButtonChange = pressedBuf.Count > 0 || releasedBuf.Count > 0
    let hasMove = delta.X <> 0.0f || delta.Y <> 0.0f
    let hasScroll = scroll <> 0.0f || scrollV.X <> 0.0f || scrollV.Y <> 0.0f

    if hasButtonChange || hasMove || hasScroll then
      trigger {
        Position = pos
        PositionDelta = delta
        Buttons = {
          Pressed = pressedBuf.ToArray()
          Released = releasedBuf.ToArray()
        }
        ScrollDelta = scroll
        ScrollDeltaV = scrollV
      }

  let pollTouch (prevTouchIds: ResizeArray<int>) (trigger: TouchDelta -> unit) =
    let count = Raylib.GetTouchPointCount()

    if count > 0 then
      let currentIds = ResizeArray<int>(count)
      let points = Array.zeroCreate<TouchPoint> count

      for i = 0 to count - 1 do
        let id = Raylib.GetTouchPointId(i)
        let pos = Raylib.GetTouchPosition(i)
        currentIds.Add(id)

        let state =
          if prevTouchIds.Contains(id) then
            TouchState.Moved
          else
            TouchState.Pressed

        points[i] <- {
          Id = id
          Position = pos
          State = state
        }

      let releasedIds = prevTouchIds |> Seq.filter(not << currentIds.Contains)

      let releasedPoints =
        releasedIds
        |> Seq.map(fun id -> {
          Id = id
          Position = Vector2.Zero
          State = TouchState.Released
        })
        |> Seq.toArray

      trigger {
        Touches = Array.append points releasedPoints
      }

      prevTouchIds.Clear()
      prevTouchIds.AddRange(currentIds)
    else if prevTouchIds.Count > 0 then
      let releasedPoints =
        prevTouchIds
        |> Seq.map(fun id -> {
          Id = id
          Position = Vector2.Zero
          State = TouchState.Released
        })
        |> Seq.toArray

      trigger { Touches = releasedPoints }
      prevTouchIds.Clear()

  let pollGamepad
    (prevConnected: bool[])
    (pressedBuf: ResizeArray<GamepadButtonCode>)
    (releasedBuf: ResizeArray<GamepadButtonCode>)
    (triggerDelta: GamepadDelta -> unit)
    (triggerConnection: GamepadConnection -> unit)
    =
    for i = 0 to 3 do
      let isConnected = Raylib.IsGamepadAvailable(i).AsBool()

      if prevConnected[i] <> isConnected then
        triggerConnection {
          PlayerIndex = i
          IsConnected = isConnected
        }

      prevConnected[i] <- isConnected

      if isConnected then
        pressedBuf.Clear()
        releasedBuf.Clear()

        for btn in allGamepadButtons do
          if Raylib.IsGamepadButtonPressed(i, btn).AsBool() then
            let code = GamepadButtonCode.ofRaylibButton btn

            if code <> GamepadButtonCode.Unknown then
              pressedBuf.Add(code)
          elif Raylib.IsGamepadButtonReleased(i, btn).AsBool() then
            let code = GamepadButtonCode.ofRaylibButton btn

            if code <> GamepadButtonCode.Unknown then
              releasedBuf.Add(code)

        let hasButtonChange = pressedBuf.Count > 0 || releasedBuf.Count > 0

        let leftStick =
          Vector2(
            Raylib.GetGamepadAxisMovement(i, GamepadAxis.LeftX),
            Raylib.GetGamepadAxisMovement(i, GamepadAxis.LeftY)
          )

        let rightStick =
          Vector2(
            Raylib.GetGamepadAxisMovement(i, GamepadAxis.RightX),
            Raylib.GetGamepadAxisMovement(i, GamepadAxis.RightY)
          )

        let leftTrigger =
          Raylib.GetGamepadAxisMovement(i, GamepadAxis.LeftTrigger)

        let rightTrigger =
          Raylib.GetGamepadAxisMovement(i, GamepadAxis.RightTrigger)

        let hasAnalogChange =
          leftStick.X <> 0.0f
          || leftStick.Y <> 0.0f
          || rightStick.X <> 0.0f
          || rightStick.Y <> 0.0f
          || leftTrigger <> 0.0f
          || rightTrigger <> 0.0f

        if hasButtonChange || hasAnalogChange then
          triggerDelta {
            PlayerIndex = i
            Buttons = {
              Pressed = pressedBuf.ToArray()
              Released = releasedBuf.ToArray()
            }
            Analog = {
              LeftThumbstick = leftStick
              RightThumbstick = rightStick
              LeftTrigger = leftTrigger
              RightTrigger = rightTrigger
            }
          }

  let pollGestures(trigger: GestureDelta -> unit) =
    let detected = Raylib.GetGestureDetected()

    if detected <> Gesture.None then
      let kind = GestureKind.ofRaylibGesture detected

      if kind <> GestureKind.Unknown then
        trigger {
          Gesture = kind
          HoldDuration = Raylib.GetGestureHoldDuration()
          DragVector = Raylib.GetGestureDragVector()
          DragAngle = Raylib.GetGestureDragAngle()
          PinchVector = Raylib.GetGesturePinchVector()
          PinchAngle = Raylib.GetGesturePinchAngle()
        }

// ─────────────────────────────────────────────────────────────────────────────
// IInput factory (raylib implementation).
//
// Input.getService / tryGetService and the Keyboard/Mouse/Touch/Gamepad/Gesture
// subscription modules all live in Core now; only the polling + factory stays here.
// ─────────────────────────────────────────────────────────────────────────────

module Input =

  let internal create(gestures: Gesture list) : IInput =
    let keyboardDelta = Event<KeyboardDelta>()
    let mouseDelta = Event<MouseDelta>()
    let touchDelta = Event<TouchDelta>()
    let gamepadDelta = Event<GamepadDelta>()
    let gamepadConnection = Event<GamepadConnection>()
    let gestureDelta = Event<GestureDelta>()

    let pressedKeysBuf = ResizeArray<KeyCode>(8)
    let releasedKeysBuf = ResizeArray<KeyCode>(8)
    let pressedMouseBuf = ResizeArray<MouseButtonCode>(4)
    let releasedMouseBuf = ResizeArray<MouseButtonCode>(4)
    let pressedGpBuf = ResizeArray<GamepadButtonCode>(8)
    let releasedGpBuf = ResizeArray<GamepadButtonCode>(8)
    let prevTouchIds = ResizeArray<int>(8)
    let prevConnected = Array.create 4 false

    // Enable requested gestures (bitwise OR of flags)
    let gestureFlags =
      match gestures with
      | [] -> Gesture.None
      | _ -> gestures |> List.reduce(fun acc g -> acc ||| g)

    if gestures.Length > 0 then
      Raylib.SetGesturesEnabled(gestureFlags)

    { new IInput with
        member _.Poll() =
          InputPolling.pollKeyboard
            pressedKeysBuf
            releasedKeysBuf
            keyboardDelta.Trigger

          InputPolling.pollMouse
            pressedMouseBuf
            releasedMouseBuf
            mouseDelta.Trigger

          InputPolling.pollTouch prevTouchIds touchDelta.Trigger

          InputPolling.pollGamepad
            prevConnected
            pressedGpBuf
            releasedGpBuf
            gamepadDelta.Trigger
            gamepadConnection.Trigger

          InputPolling.pollGestures gestureDelta.Trigger

        member _.KeyboardDelta = keyboardDelta.Publish
        member _.MouseDelta = mouseDelta.Publish
        member _.TouchDelta = touchDelta.Publish
        member _.GamepadDelta = gamepadDelta.Publish
        member _.GamepadConnection = gamepadConnection.Publish
        member _.GestureDelta = gestureDelta.Publish
    }
