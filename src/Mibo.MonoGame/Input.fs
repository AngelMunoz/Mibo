namespace Mibo.Input

open System
open System.Numerics
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch
open Mibo.Elmish

// NOTE: we deliberately do NOT `open Microsoft.Xna.Framework` (the root
// namespace). It defines Microsoft.Xna.Framework.Vector2, which would shadow
// System.Numerics.Vector2 used by the Core delta types. Only the lone
// `Microsoft.Xna.Framework.Game` parameter type (in Input.create) is
// fully-qualified below. The MG input types we need all live in the .Input /
// .Input.Touch sub-namespaces opened above.

// ─────────────────────────────────────────────────────────────────────────────
// MonoGame ↔ Core input translation.
//
// This is the ONLY place in the MonoGame backend where Microsoft.Xna.Framework
// .Keys / Buttons / MouseState / GamePadState touch the Core-neutral code types.
// Everything else in the backend works in Core codes.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Functions for translating between MonoGame's <c>Keys</c> and Mibo's backend-neutral <see cref="T:Mibo.Input.KeyCode"/>.</summary>
module KeyCode =

  /// <summary>Maps a MonoGame <c>Keys</c> to the backend-neutral <see cref="T:Mibo.Input.KeyCode"/>.
  /// Returns <see cref="F:Mibo.Input.KeyCode.Unknown"/> for any key with no logical equivalent.</summary>
  let ofMonoGameKey(k: Keys) : KeyCode =
    match k with
    // Letters
    | Keys.A -> KeyCode.A
    | Keys.B -> KeyCode.B
    | Keys.C -> KeyCode.C
    | Keys.D -> KeyCode.D
    | Keys.E -> KeyCode.E
    | Keys.F -> KeyCode.F
    | Keys.G -> KeyCode.G
    | Keys.H -> KeyCode.H
    | Keys.I -> KeyCode.I
    | Keys.J -> KeyCode.J
    | Keys.K -> KeyCode.K
    | Keys.L -> KeyCode.L
    | Keys.M -> KeyCode.M
    | Keys.N -> KeyCode.N
    | Keys.O -> KeyCode.O
    | Keys.P -> KeyCode.P
    | Keys.Q -> KeyCode.Q
    | Keys.R -> KeyCode.R
    | Keys.S -> KeyCode.S
    | Keys.T -> KeyCode.T
    | Keys.U -> KeyCode.U
    | Keys.V -> KeyCode.V
    | Keys.W -> KeyCode.W
    | Keys.X -> KeyCode.X
    | Keys.Y -> KeyCode.Y
    | Keys.Z -> KeyCode.Z
    // Digits (top row)
    | Keys.D0 -> KeyCode.D0
    | Keys.D1 -> KeyCode.D1
    | Keys.D2 -> KeyCode.D2
    | Keys.D3 -> KeyCode.D3
    | Keys.D4 -> KeyCode.D4
    | Keys.D5 -> KeyCode.D5
    | Keys.D6 -> KeyCode.D6
    | Keys.D7 -> KeyCode.D7
    | Keys.D8 -> KeyCode.D8
    | Keys.D9 -> KeyCode.D9
    // Function keys
    | Keys.F1 -> KeyCode.F1
    | Keys.F2 -> KeyCode.F2
    | Keys.F3 -> KeyCode.F3
    | Keys.F4 -> KeyCode.F4
    | Keys.F5 -> KeyCode.F5
    | Keys.F6 -> KeyCode.F6
    | Keys.F7 -> KeyCode.F7
    | Keys.F8 -> KeyCode.F8
    | Keys.F9 -> KeyCode.F9
    | Keys.F10 -> KeyCode.F10
    | Keys.F11 -> KeyCode.F11
    | Keys.F12 -> KeyCode.F12
    // Arrow / navigation cluster
    | Keys.Up -> KeyCode.Up
    | Keys.Down -> KeyCode.Down
    | Keys.Left -> KeyCode.Left
    | Keys.Right -> KeyCode.Right
    | Keys.PageUp -> KeyCode.PageUp
    | Keys.PageDown -> KeyCode.PageDown
    | Keys.Home -> KeyCode.Home
    | Keys.End -> KeyCode.End
    | Keys.Insert -> KeyCode.Insert
    | Keys.Delete -> KeyCode.Delete
    // Editing / control
    | Keys.Space -> KeyCode.Space
    | Keys.Enter -> KeyCode.Enter
    | Keys.Escape -> KeyCode.Escape
    | Keys.Tab -> KeyCode.Tab
    | Keys.Back -> KeyCode.Backspace
    | Keys.CapsLock -> KeyCode.CapsLock
    // Modifiers
    | Keys.LeftShift -> KeyCode.LeftShift
    | Keys.RightShift -> KeyCode.RightShift
    | Keys.LeftControl -> KeyCode.LeftControl
    | Keys.RightControl -> KeyCode.RightControl
    | Keys.LeftAlt -> KeyCode.LeftAlt
    | Keys.RightAlt -> KeyCode.RightAlt
    // MonoGame surfaces a single platform "Windows key" — map to LeftSuper.
    // RightSuper has no MG equivalent on most platforms → falls to Unknown below.
    | Keys.LeftWindows -> KeyCode.LeftSuper
    // Punctuation / symbol keys (US layout)
    | Keys.OemTilde -> KeyCode.Grave
    | Keys.OemMinus -> KeyCode.Minus
    | Keys.OemPlus -> KeyCode.Equal
    | Keys.OemOpenBrackets -> KeyCode.LeftBracket
    | Keys.OemCloseBrackets -> KeyCode.RightBracket
    | Keys.OemBackslash -> KeyCode.Backslash
    | Keys.OemSemicolon -> KeyCode.Semicolon
    | Keys.OemQuotes -> KeyCode.Apostrophe
    | Keys.OemComma -> KeyCode.Comma
    | Keys.OemPeriod -> KeyCode.Period
    | Keys.OemQuestion -> KeyCode.Slash
    // Keypad
    | Keys.NumPad0 -> KeyCode.Kp0
    | Keys.NumPad1 -> KeyCode.Kp1
    | Keys.NumPad2 -> KeyCode.Kp2
    | Keys.NumPad3 -> KeyCode.Kp3
    | Keys.NumPad4 -> KeyCode.Kp4
    | Keys.NumPad5 -> KeyCode.Kp5
    | Keys.NumPad6 -> KeyCode.Kp6
    | Keys.NumPad7 -> KeyCode.Kp7
    | Keys.NumPad8 -> KeyCode.Kp8
    | Keys.NumPad9 -> KeyCode.Kp9
    | Keys.Decimal -> KeyCode.KpDecimal
    | Keys.Divide -> KeyCode.KpDivide
    | Keys.Multiply -> KeyCode.KpMultiply
    | Keys.Subtract -> KeyCode.KpSubtract
    | Keys.Add -> KeyCode.KpAdd
    // NOTE: MonoGame has no separate numpad Enter (it shares Keys.Enter with the
    // main Enter key), so KeyCode.KpEnter is never produced by this backend.
    // Media / system
    | Keys.PrintScreen -> KeyCode.PrintScreen
    | Keys.Scroll -> KeyCode.ScrollLock
    | Keys.Pause -> KeyCode.Pause
    // Locks / indicators
    | Keys.NumLock -> KeyCode.NumLock
    | _ -> KeyCode.Unknown

  /// <summary>Maps a backend-neutral <see cref="T:Mibo.Input.KeyCode"/> back to a MonoGame <c>Keys</c>.
  /// Returns <c>Keys.None</c> for <see cref="F:Mibo.Input.KeyCode.Unknown"/> and for codes with no MG equivalent.</summary>
  let toMonoGameKey(k: KeyCode) : Keys =
    match k with
    // Letters
    | KeyCode.A -> Keys.A
    | KeyCode.B -> Keys.B
    | KeyCode.C -> Keys.C
    | KeyCode.D -> Keys.D
    | KeyCode.E -> Keys.E
    | KeyCode.F -> Keys.F
    | KeyCode.G -> Keys.G
    | KeyCode.H -> Keys.H
    | KeyCode.I -> Keys.I
    | KeyCode.J -> Keys.J
    | KeyCode.K -> Keys.K
    | KeyCode.L -> Keys.L
    | KeyCode.M -> Keys.M
    | KeyCode.N -> Keys.N
    | KeyCode.O -> Keys.O
    | KeyCode.P -> Keys.P
    | KeyCode.Q -> Keys.Q
    | KeyCode.R -> Keys.R
    | KeyCode.S -> Keys.S
    | KeyCode.T -> Keys.T
    | KeyCode.U -> Keys.U
    | KeyCode.V -> Keys.V
    | KeyCode.W -> Keys.W
    | KeyCode.X -> Keys.X
    | KeyCode.Y -> Keys.Y
    | KeyCode.Z -> Keys.Z
    // Digits (top row)
    | KeyCode.D0 -> Keys.D0
    | KeyCode.D1 -> Keys.D1
    | KeyCode.D2 -> Keys.D2
    | KeyCode.D3 -> Keys.D3
    | KeyCode.D4 -> Keys.D4
    | KeyCode.D5 -> Keys.D5
    | KeyCode.D6 -> Keys.D6
    | KeyCode.D7 -> Keys.D7
    | KeyCode.D8 -> Keys.D8
    | KeyCode.D9 -> Keys.D9
    // Function keys
    | KeyCode.F1 -> Keys.F1
    | KeyCode.F2 -> Keys.F2
    | KeyCode.F3 -> Keys.F3
    | KeyCode.F4 -> Keys.F4
    | KeyCode.F5 -> Keys.F5
    | KeyCode.F6 -> Keys.F6
    | KeyCode.F7 -> Keys.F7
    | KeyCode.F8 -> Keys.F8
    | KeyCode.F9 -> Keys.F9
    | KeyCode.F10 -> Keys.F10
    | KeyCode.F11 -> Keys.F11
    | KeyCode.F12 -> Keys.F12
    // Arrow / navigation cluster
    | KeyCode.Up -> Keys.Up
    | KeyCode.Down -> Keys.Down
    | KeyCode.Left -> Keys.Left
    | KeyCode.Right -> Keys.Right
    | KeyCode.PageUp -> Keys.PageUp
    | KeyCode.PageDown -> Keys.PageDown
    | KeyCode.Home -> Keys.Home
    | KeyCode.End -> Keys.End
    | KeyCode.Insert -> Keys.Insert
    | KeyCode.Delete -> Keys.Delete
    // Editing / control
    | KeyCode.Space -> Keys.Space
    | KeyCode.Enter -> Keys.Enter
    | KeyCode.Escape -> Keys.Escape
    | KeyCode.Tab -> Keys.Tab
    | KeyCode.Backspace -> Keys.Back
    | KeyCode.CapsLock -> Keys.CapsLock
    // Modifiers
    | KeyCode.LeftShift -> Keys.LeftShift
    | KeyCode.RightShift -> Keys.RightShift
    | KeyCode.LeftControl -> Keys.LeftControl
    | KeyCode.RightControl -> Keys.RightControl
    | KeyCode.LeftAlt -> Keys.LeftAlt
    | KeyCode.RightAlt -> Keys.RightAlt
    | KeyCode.LeftSuper -> Keys.LeftWindows
    | KeyCode.RightSuper -> Keys.None
    // Punctuation / symbol keys (US layout)
    | KeyCode.Grave -> Keys.OemTilde
    | KeyCode.Minus -> Keys.OemMinus
    | KeyCode.Equal -> Keys.OemPlus
    | KeyCode.LeftBracket -> Keys.OemOpenBrackets
    | KeyCode.RightBracket -> Keys.OemCloseBrackets
    | KeyCode.Backslash -> Keys.OemBackslash
    | KeyCode.Semicolon -> Keys.OemSemicolon
    | KeyCode.Apostrophe -> Keys.OemQuotes
    | KeyCode.Comma -> Keys.OemComma
    | KeyCode.Period -> Keys.OemPeriod
    | KeyCode.Slash -> Keys.OemQuestion
    // Keypad
    | KeyCode.Kp0 -> Keys.NumPad0
    | KeyCode.Kp1 -> Keys.NumPad1
    | KeyCode.Kp2 -> Keys.NumPad2
    | KeyCode.Kp3 -> Keys.NumPad3
    | KeyCode.Kp4 -> Keys.NumPad4
    | KeyCode.Kp5 -> Keys.NumPad5
    | KeyCode.Kp6 -> Keys.NumPad6
    | KeyCode.Kp7 -> Keys.NumPad7
    | KeyCode.Kp8 -> Keys.NumPad8
    | KeyCode.Kp9 -> Keys.NumPad9
    | KeyCode.KpDecimal -> Keys.Decimal
    | KeyCode.KpDivide -> Keys.Divide
    | KeyCode.KpMultiply -> Keys.Multiply
    | KeyCode.KpSubtract -> Keys.Subtract
    | KeyCode.KpAdd -> Keys.Add
    | KeyCode.KpEnter -> Keys.Enter
    | KeyCode.KpEqual -> Keys.None
    // Media / system
    | KeyCode.PrintScreen -> Keys.PrintScreen
    | KeyCode.ScrollLock -> Keys.Scroll
    | KeyCode.Pause -> Keys.Pause
    | KeyCode.Menu -> Keys.None
    // Locks / indicators
    | KeyCode.NumLock -> Keys.NumLock
    | KeyCode.Unknown -> Keys.None

/// <summary>Functions for translating between MonoGame's <c>Buttons</c> and Mibo's backend-neutral <see cref="T:Mibo.Input.GamepadButtonCode"/>.</summary>
module GamepadButtonCode =

  /// <summary>Maps a MonoGame <c>Buttons</c> to the backend-neutral <see cref="T:Mibo.Input.GamepadButtonCode"/>.
  /// Returns <see cref="F:Mibo.Input.GamepadButtonCode.Unknown"/> for any button with no logical equivalent.</summary>
  /// <remarks>
  /// MonoGame's <c>Buttons</c> uses an Xbox layout: <c>A</c>/<c>B</c>/<c>X</c>/<c>Y</c> for face buttons,
  /// <c>LeftShoulder</c>/<c>RightShoulder</c> for bumpers, <c>LeftTrigger</c>/<c>RightTrigger</c> for triggers.
  /// We map to the abstract <c>FaceUp</c>/<c>FaceRight</c>/<c>FaceDown</c>/<c>FaceLeft</c> (Xbox: Y/B/A/X).
  /// </remarks>
  let ofMonoGameButton(b: Buttons) : GamepadButtonCode =
    match b with
    // Face buttons (Xbox layout → Face{Up,Right,Down,Left} = Y,B,A,X)
    | Buttons.Y -> GamepadButtonCode.FaceUp
    | Buttons.B -> GamepadButtonCode.FaceRight
    | Buttons.A -> GamepadButtonCode.FaceDown
    | Buttons.X -> GamepadButtonCode.FaceLeft
    // Shoulder / bumper
    | Buttons.LeftShoulder -> GamepadButtonCode.LeftShoulder
    | Buttons.RightShoulder -> GamepadButtonCode.RightShoulder
    // Triggers (digital press; analog values come through GamepadAnalog)
    | Buttons.LeftTrigger -> GamepadButtonCode.LeftTrigger
    | Buttons.RightTrigger -> GamepadButtonCode.RightTrigger
    // Stick clicks
    | Buttons.LeftStick -> GamepadButtonCode.LeftStick
    | Buttons.RightStick -> GamepadButtonCode.RightStick
    // Select / Start / Home
    | Buttons.Back -> GamepadButtonCode.Select
    | Buttons.Start -> GamepadButtonCode.Start
    | Buttons.BigButton -> GamepadButtonCode.Home
    // D-pad (the Buttons enum carries these, but the GamePadButtons snapshot
    // does NOT — D-pad deltas come from GamePadState.DPad in the poller below).
    | Buttons.DPadUp -> GamepadButtonCode.DPadUp
    | Buttons.DPadRight -> GamepadButtonCode.DPadRight
    | Buttons.DPadDown -> GamepadButtonCode.DPadDown
    | Buttons.DPadLeft -> GamepadButtonCode.DPadLeft
    | _ -> GamepadButtonCode.Unknown

  /// <summary>Maps a backend-neutral <see cref="T:Mibo.Input.GamepadButtonCode"/> back to a MonoGame <c>Buttons</c>.
  /// Returns <c>Buttons.None</c> for <see cref="F:Mibo.Input.GamepadButtonCode.Unknown"/>.</summary>
  let toMonoGameButton(b: GamepadButtonCode) : Buttons =
    match b with
    | GamepadButtonCode.FaceUp -> Buttons.Y
    | GamepadButtonCode.FaceRight -> Buttons.B
    | GamepadButtonCode.FaceDown -> Buttons.A
    | GamepadButtonCode.FaceLeft -> Buttons.X
    | GamepadButtonCode.LeftShoulder -> Buttons.LeftShoulder
    | GamepadButtonCode.RightShoulder -> Buttons.RightShoulder
    | GamepadButtonCode.LeftTrigger -> Buttons.LeftTrigger
    | GamepadButtonCode.RightTrigger -> Buttons.RightTrigger
    | GamepadButtonCode.LeftStick -> Buttons.LeftStick
    | GamepadButtonCode.RightStick -> Buttons.RightStick
    | GamepadButtonCode.Select -> Buttons.Back
    | GamepadButtonCode.Start -> Buttons.Start
    | GamepadButtonCode.Home -> Buttons.BigButton
    | GamepadButtonCode.DPadUp -> Buttons.DPadUp
    | GamepadButtonCode.DPadRight -> Buttons.DPadRight
    | GamepadButtonCode.DPadDown -> Buttons.DPadDown
    | GamepadButtonCode.DPadLeft -> Buttons.DPadLeft
    | GamepadButtonCode.Unknown -> Buttons.None

// ─────────────────────────────────────────────────────────────────────────────
// Input polling (MonoGame). Emits Core-neutral delta values.
//
// MonoGame gives whole-state snapshots (KeyboardState/MouseState/GamePadState),
// so each poller keeps the previous frame's state and diffs.
// Mirrors the shape of the raylib InputPolling module.
// ─────────────────────────────────────────────────────────────────────────────

module private InputPolling =

  // Only iterate over keys that are likely to produce a meaningful KeyCode.
  let allKeys = Enum.GetValues(typeof<Keys>) :?> Keys[]

  let pollKeyboard
    (prevKeyboard: byref<KeyboardState>)
    (pressedBuf: ResizeArray<KeyCode>)
    (releasedBuf: ResizeArray<KeyCode>)
    (trigger: KeyboardDelta -> unit)
    =
    let curr = Keyboard.GetState()
    pressedBuf.Clear()
    releasedBuf.Clear()

    for k in allKeys do
      if k <> Keys.None then
        let wasDown = prevKeyboard.IsKeyDown(k)
        let isDown = curr.IsKeyDown(k)

        if isDown && not wasDown then
          let code = KeyCode.ofMonoGameKey k

          if code <> KeyCode.Unknown then
            pressedBuf.Add(code)
        elif wasDown && not isDown then
          let code = KeyCode.ofMonoGameKey k

          if code <> KeyCode.Unknown then
            releasedBuf.Add(code)

    if pressedBuf.Count > 0 || releasedBuf.Count > 0 then
      trigger {
        Pressed = pressedBuf.ToArray()
        Released = releasedBuf.ToArray()
      }

    prevKeyboard <- curr

  let pollMouseCapturing
    (prevMouse: byref<MouseState>)
    (captureMode: MouseCapture)
    (game: Microsoft.Xna.Framework.Game)
    (trigger: MouseDelta -> unit)
    =
    let curr = Mouse.GetState()

    // When captured, compute delta from the last known position and re-center
    // only when the mouse approaches the window edge. This gives unlimited
    // mouse movement for FPS-style look without the cursor reaching the edge,
    // while keeping deltas smooth (re-centering every frame can cause jitter
    // on WinForms where Mouse.SetPosition is asynchronous via the message pump).
    let struct (deltaX, deltaY, reportedX, reportedY, newPrevX, newPrevY) =
      match captureMode with
      | MouseCapture.Captured when game.IsActive ->
        let w = game.Window.ClientBounds.Width
        let h = game.Window.ClientBounds.Height
        let struct (cx, cy) = w / 2, h / 2
        let margin = 50 // pixels from edge before re-centering

        let struct (dx, dy) = curr.X - prevMouse.X, curr.Y - prevMouse.Y

        // Re-center only when near the edge to avoid jitter from async SetPosition
        let nearEdge =
          curr.X < margin
          || curr.X > w - margin
          || curr.Y < margin
          || curr.Y > h - margin

        if nearEdge then
          Mouse.SetPosition(cx, cy)
          dx, dy, cx, cy, cx, cy
        else
          dx, dy, curr.X, curr.Y, curr.X, curr.Y
      | _ ->
        let struct (dx, dy) = curr.X - prevMouse.X, curr.Y - prevMouse.Y
        dx, dy, curr.X, curr.Y, curr.X, curr.Y

    let posChanged = deltaX <> 0 || deltaY <> 0
    let scrollDelta = curr.ScrollWheelValue - prevMouse.ScrollWheelValue

    let scrollDeltaH =
      curr.HorizontalScrollWheelValue - prevMouse.HorizontalScrollWheelValue

    let pressed = ResizeArray<MouseButtonCode>(2)
    let released = ResizeArray<MouseButtonCode>(2)

    let inline deltaBtn
      (currBtn: ButtonState)
      (prevBtn: ButtonState)
      (code: MouseButtonCode)
      =
      if currBtn = ButtonState.Pressed && prevBtn = ButtonState.Released then
        pressed.Add code
      elif currBtn = ButtonState.Released && prevBtn = ButtonState.Pressed then
        released.Add code

    deltaBtn curr.LeftButton prevMouse.LeftButton MouseButtonCode.Left
    deltaBtn curr.RightButton prevMouse.RightButton MouseButtonCode.Right
    deltaBtn curr.MiddleButton prevMouse.MiddleButton MouseButtonCode.Middle
    deltaBtn curr.XButton1 prevMouse.XButton1 MouseButtonCode.Extra1
    deltaBtn curr.XButton2 prevMouse.XButton2 MouseButtonCode.Extra2

    let hasButtonChange = pressed.Count > 0 || released.Count > 0

    if
      posChanged || scrollDelta <> 0 || scrollDeltaH <> 0 || hasButtonChange
    then
      trigger {
        Position = Vector2(float32 reportedX, float32 reportedY)
        PositionDelta = Vector2(float32 deltaX, float32 deltaY)
        Buttons = {
          Pressed = pressed.ToArray()
          Released = released.ToArray()
        }
        ScrollDelta = float32 scrollDelta
        ScrollDeltaV = Vector2(float32 scrollDeltaH, float32 scrollDelta)
      }

    prevMouse <-
      Microsoft.Xna.Framework.Input.MouseState(
        newPrevX,
        newPrevY,
        curr.ScrollWheelValue,
        curr.LeftButton,
        curr.MiddleButton,
        curr.RightButton,
        curr.XButton1,
        curr.XButton2,
        curr.HorizontalScrollWheelValue
      )

  let pollTouch(trigger: TouchDelta -> unit) =
    // MonoGame's TouchPanel exposes raw touch points. High-level gesture
    // detection (Tap/Hold/Swipe/Pinch) is not built in the way raylib provides;
    // the GestureKind stream stays empty (see Input.create). Raw touch points
    // are surfaced here when present.
    let touches = TouchPanel.GetState()

    if touches.Count > 0 then
      let points = Array.zeroCreate<TouchPoint> touches.Count

      for i = 0 to touches.Count - 1 do
        let t = touches[i]

        let state =
          match t.State with
          | TouchLocationState.Pressed -> TouchState.Pressed
          | TouchLocationState.Moved -> TouchState.Moved
          | TouchLocationState.Released
          | TouchLocationState.Invalid
          | _ -> TouchState.Released

        points[i] <- {
          Id = t.Id
          Position = Vector2(t.Position.X, t.Position.Y)
          State = state
        }

      trigger { Touches = points }

  let pollGamepad
    (prevGamepad: GamePadState[])
    (prevConnected: bool[])
    (triggerDelta: GamepadDelta -> unit)
    (triggerConnection: GamepadConnection -> unit)
    =
    for i = 0 to 3 do
      let state = GamePad.GetState(i)
      let isConnected = state.IsConnected

      if prevConnected[i] <> isConnected then
        triggerConnection {
          PlayerIndex = i
          IsConnected = isConnected
        }

      prevConnected[i] <- isConnected

      // On the first connected frame (or after a reconnect), prev is
      // default(GamePadState) which reads as all-released, so a frame of
      // currently-held buttons emits spurious "pressed" events — acceptable
      // and matches the raylib backend's first-frame behavior.
      if isConnected then
        let prev = prevGamepad[i]
        let pressed = ResizeArray<GamepadButtonCode>(8)
        let released = ResizeArray<GamepadButtonCode>(8)

        let inline delta
          (currBtn: ButtonState)
          (prevBtn: ButtonState)
          (code: GamepadButtonCode)
          =
          if
            currBtn = ButtonState.Pressed && prevBtn = ButtonState.Released
          then
            pressed.Add code
          elif
            currBtn = ButtonState.Released && prevBtn = ButtonState.Pressed
          then
            released.Add code

        // Face / shoulder / stick / start-select-home live on GamePadButtons.
        delta state.Buttons.A prev.Buttons.A GamepadButtonCode.FaceDown
        delta state.Buttons.B prev.Buttons.B GamepadButtonCode.FaceRight
        delta state.Buttons.X prev.Buttons.X GamepadButtonCode.FaceLeft
        delta state.Buttons.Y prev.Buttons.Y GamepadButtonCode.FaceUp

        delta
          state.Buttons.LeftShoulder
          prev.Buttons.LeftShoulder
          GamepadButtonCode.LeftShoulder

        delta
          state.Buttons.RightShoulder
          prev.Buttons.RightShoulder
          GamepadButtonCode.RightShoulder

        delta
          state.Buttons.LeftStick
          prev.Buttons.LeftStick
          GamepadButtonCode.LeftStick

        delta
          state.Buttons.RightStick
          prev.Buttons.RightStick
          GamepadButtonCode.RightStick

        delta state.Buttons.Back prev.Buttons.Back GamepadButtonCode.Select
        delta state.Buttons.Start prev.Buttons.Start GamepadButtonCode.Start

        delta
          state.Buttons.BigButton
          prev.Buttons.BigButton
          GamepadButtonCode.Home

        // D-pad lives on a separate GamePadDPad struct, not on GamePadButtons.
        delta state.DPad.Up prev.DPad.Up GamepadButtonCode.DPadUp
        delta state.DPad.Right prev.DPad.Right GamepadButtonCode.DPadRight
        delta state.DPad.Down prev.DPad.Down GamepadButtonCode.DPadDown
        delta state.DPad.Left prev.DPad.Left GamepadButtonCode.DPadLeft

        // Triggers: expose as digital presses at a threshold (analog values
        // flow through the GamepadAnalog below). 0.5 matches the original Mibo.
        let ltDigital = state.Triggers.Left >= 0.5f
        let ltPrev = prev.Triggers.Left >= 0.5f
        let rtDigital = state.Triggers.Right >= 0.5f
        let rtPrev = prev.Triggers.Right >= 0.5f

        if ltDigital && not ltPrev then
          pressed.Add GamepadButtonCode.LeftTrigger
        elif not ltDigital && ltPrev then
          released.Add GamepadButtonCode.LeftTrigger

        if rtDigital && not rtPrev then
          pressed.Add GamepadButtonCode.RightTrigger
        elif not rtDigital && rtPrev then
          released.Add GamepadButtonCode.RightTrigger

        let analog = {
          LeftThumbstick =
            Vector2(state.ThumbSticks.Left.X, -state.ThumbSticks.Left.Y)
          RightThumbstick =
            Vector2(state.ThumbSticks.Right.X, -state.ThumbSticks.Right.Y)
          LeftTrigger = state.Triggers.Left
          RightTrigger = state.Triggers.Right
        }

        let hasChange =
          pressed.Count > 0
          || released.Count > 0
          || analog.LeftThumbstick <> Vector2.Zero
          || analog.RightThumbstick <> Vector2.Zero
          || analog.LeftTrigger <> 0.0f
          || analog.RightTrigger <> 0.0f

        if hasChange then
          triggerDelta {
            PlayerIndex = i
            Buttons = {
              Pressed = pressed.ToArray()
              Released = released.ToArray()
            }
            Analog = analog
          }

      prevGamepad[i] <- state

// ─────────────────────────────────────────────────────────────────────────────
// IInput factory (MonoGame implementation).
// ─────────────────────────────────────────────────────────────────────────────

module Input =

  let internal create(game: Microsoft.Xna.Framework.Game) : IInput =
    let keyboardDelta = Event<KeyboardDelta>()
    let mouseDelta = Event<MouseDelta>()
    let touchDelta = Event<TouchDelta>()
    let gamepadDelta = Event<GamepadDelta>()
    let gamepadConnection = Event<GamepadConnection>()

    let pressedKeysBuf = ResizeArray<KeyCode>(8)
    let releasedKeysBuf = ResizeArray<KeyCode>(8)
    let mutable prevKeyboard = Keyboard.GetState()
    let mutable prevMouse = Mouse.GetState()
    let prevConnected = Array.create 4 false
    let prevGamepad = Array.init 4 (fun i -> GamePad.GetState(i))
    let mutable mouseCapture = MouseCapture.Free

    { new IInput with
        member _.Poll() =
          InputPolling.pollKeyboard
            &prevKeyboard
            pressedKeysBuf
            releasedKeysBuf
            keyboardDelta.Trigger

          InputPolling.pollMouseCapturing
            &prevMouse
            mouseCapture
            game
            mouseDelta.Trigger

          InputPolling.pollTouch touchDelta.Trigger

          InputPolling.pollGamepad
            prevGamepad
            prevConnected
            gamepadDelta.Trigger
            gamepadConnection.Trigger

        member _.KeyboardDelta = keyboardDelta.Publish
        member _.MouseDelta = mouseDelta.Publish
        member _.TouchDelta = touchDelta.Publish
        member _.GamepadDelta = gamepadDelta.Publish
        member _.GamepadConnection = gamepadConnection.Publish

        member _.SetMouseCapture(mode) = mouseCapture <- mode

        // MonoGame has no built-in high-level gesture detection matching the
        // Core GestureKind set. Gestures are a known 80/20 gap — this stream
        // stays empty until a gesture recognizer is layered on.
        member _.GestureDelta =
          { new IObservable<GestureDelta> with
              member _.Subscribe(_observer) =
                { new IDisposable with
                    member _.Dispose() = ()
                }
          }
    }
