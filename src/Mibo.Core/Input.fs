namespace Mibo.Input

open System
open System.Numerics

// ─────────────────────────────────────────────────────────────────────────────
// Backend-neutral logical input codes.
//
// These are struct DUs (not enums) on purpose:
//  - exhaustive pattern matching in user code (the compiler proves completeness)
//  - no invalid values at runtime (unlike enums, which can hold any int)
//  - closed type: a third backend (SDL, Silk, ...) can be added without changing
//    these types — it just translates its native codes to/from these cases.
//
// Each DU has an `Unknown` case: a backend maps any native input it does not
// recognize to `Unknown` rather than fabricating a value. Note that the default
// raylib polling implementation filters out `Unknown` values at the polling
// boundary to avoid flooding the input queue with indistinguishable events.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Backend-neutral keyboard key. Covers the standard US-layout key set that any
/// game backend (raylib, MonoGame, SDL, …) can produce.
/// </summary>
/// <remarks>
/// Backends translate their native key types to this DU at the input boundary.
/// Any native key with no logical equivalent maps to <see cref="F:Mibo.Input.KeyCode.Unknown"/>.
/// Cases are <c>RequireQualifiedAccess</c> because names like <c>Up</c>/<c>Down</c>/<c>Left</c>/<c>Right</c>
/// would otherwise collide with user DU cases when <c>Mibo.Input</c> is opened.
/// Always write <c>KeyCode.W</c>, <c>KeyCode.Space</c>, etc.
/// </remarks>
[<Struct>]
[<RequireQualifiedAccess>]
type KeyCode =
  // Letters
  | A
  | B
  | C
  | D
  | E
  | F
  | G
  | H
  | I
  | J
  | K
  | L
  | M
  | N
  | O
  | P
  | Q
  | R
  | S
  | T
  | U
  | V
  | W
  | X
  | Y
  | Z
  // Digits (top row)
  | D0
  | D1
  | D2
  | D3
  | D4
  | D5
  | D6
  | D7
  | D8
  | D9
  // Function keys
  | F1
  | F2
  | F3
  | F4
  | F5
  | F6
  | F7
  | F8
  | F9
  | F10
  | F11
  | F12
  // Arrow / navigation cluster
  | Up
  | Down
  | Left
  | Right
  | PageUp
  | PageDown
  | Home
  | End
  | Insert
  | Delete
  // Editing / control
  | Space
  | Enter
  | Escape
  | Tab
  | Backspace
  | CapsLock
  // Modifiers
  | LeftShift
  | RightShift
  | LeftControl
  | RightControl
  | LeftAlt
  | RightAlt
  | LeftSuper
  | RightSuper
  // Punctuation / symbol keys (US layout)
  | Grave
  | Minus
  | Equal
  | LeftBracket
  | RightBracket
  | Backslash
  | Semicolon
  | Apostrophe
  | Comma
  | Period
  | Slash
  // Keypad
  | Kp0
  | Kp1
  | Kp2
  | Kp3
  | Kp4
  | Kp5
  | Kp6
  | Kp7
  | Kp8
  | Kp9
  | KpDecimal
  | KpDivide
  | KpMultiply
  | KpSubtract
  | KpAdd
  | KpEnter
  | KpEqual
  // Media / system
  | PrintScreen
  | ScrollLock
  | Pause
  | Menu
  // Locks / indicators
  | NumLock
  // Fallback
  | Unknown

/// <summary>
/// Backend-neutral mouse button.
/// </summary>
[<Struct>]
[<RequireQualifiedAccess>]
type MouseButtonCode =
  | Left
  | Right
  | Middle
  | Extra1
  | Extra2
  | Extra3
  | Extra4
  | Unknown

/// <summary>
/// Backend-neutral gamepad button, following the standard gamepad layout
/// (face buttons, D-pad, shoulder/bumper, stick clicks). Backends map their
/// native ordering (which differs between raylib and MonoGame) onto this DU.
/// </summary>
[<Struct>]
[<RequireQualifiedAccess>]
type GamepadButtonCode =
  // Face buttons (backend maps to its own up/right/down/left convention)
  | FaceUp
  | FaceRight
  | FaceDown
  | FaceLeft
  // Shoulder / bumper
  | LeftShoulder
  | RightShoulder
  // Triggers are exposed as analog values (see GamepadAnalog); some backends
  // also report a digital trigger press, so we include them here.
  | LeftTrigger
  | RightTrigger
  // Stick clicks
  | LeftStick
  | RightStick
  // Select / Start / Home
  | Select
  | Start
  | Home
  // D-pad
  | DPadUp
  | DPadRight
  | DPadDown
  | DPadLeft
  | Unknown

/// <summary>
/// Backend-neutral touch gesture kind.
/// </summary>
/// <remarks>
/// Note: there is deliberately no <c>None</c> case — "no gesture detected" is
/// expressed with <c>voption</c> (e.g. <c>GestureDelta voption</c>) to avoid
/// colliding with F#'s <c>Option.None</c> when <c>Mibo.Input</c> is opened.
/// </remarks>
[<Struct>]
[<RequireQualifiedAccess>]
type GestureKind =
  | Tap
  | DoubleTap
  | Hold
  | Drag
  | SwipeRight
  | SwipeLeft
  | SwipeUp
  | SwipeDown
  | Pinch
  | Unknown

// ─────────────────────────────────────────────────────────────────────────────
// Delta types (backend-neutral: use the codes above + System.Numerics vectors).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Keyboard state delta containing keys that changed this frame.</summary>
[<Struct>]
type KeyboardDelta = {
  Pressed: KeyCode[]
  Released: KeyCode[]
}

/// <summary>Mouse button state changes for a single frame.</summary>
[<Struct>]
type MouseButtons = {
  Pressed: MouseButtonCode[]
  Released: MouseButtonCode[]
}

/// <summary>Mouse state delta containing position and button changes.</summary>
[<Struct>]
type MouseDelta = {
  Position: Vector2
  PositionDelta: Vector2
  Buttons: MouseButtons
  ScrollDelta: float32
  ScrollDeltaV: Vector2
}

/// <summary>Touch point state for tracking individual touch lifecycle.</summary>
[<Struct>]
[<RequireQualifiedAccess>]
type TouchState =
  | Pressed
  | Moved
  | Released

/// <summary>A single touch point for touch input.</summary>
[<Struct>]
type TouchPoint = {
  Id: int
  Position: Vector2
  State: TouchState
}

/// <summary>Touch input state containing all active touch points.</summary>
[<Struct>]
type TouchDelta = { Touches: TouchPoint[] }

/// <summary>Gamepad button state changes for a single frame.</summary>
[<Struct>]
type GamepadButtons = {
  Pressed: GamepadButtonCode[]
  Released: GamepadButtonCode[]
}

/// <summary>Gamepad analog input values (thumbsticks and triggers).</summary>
/// <remarks>Thumbsticks range from -1 to 1, triggers range from 0 to 1.</remarks>
[<Struct>]
type GamepadAnalog = {
  LeftThumbstick: Vector2
  RightThumbstick: Vector2
  LeftTrigger: float32
  RightTrigger: float32
}

/// <summary>Per-player gamepad delta containing button and analog changes.</summary>
[<Struct>]
type GamepadDelta = {
  PlayerIndex: int
  Buttons: GamepadButtons
  Analog: GamepadAnalog
}

/// <summary>Gamepad connection state change event.</summary>
[<Struct>]
type GamepadConnection = { PlayerIndex: int; IsConnected: bool }

/// <summary>Gesture detection events for touch-capable devices.</summary>
[<Struct>]
type GestureDelta = {
  Gesture: GestureKind
  HoldDuration: float32
  DragVector: Vector2
  DragAngle: float32
  PinchVector: Vector2
  PinchAngle: float32
}

// ─────────────────────────────────────────────────────────────────────────────
// IInput contract (lives in Core; backend supplies the implementation).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Mouse capture mode for first-person / pointer-lock style input.
/// </summary>
[<RequireQualifiedAccess>]
type MouseCapture =
  /// <summary>Normal cursor behavior — the OS cursor moves freely within the window.</summary>
  | Free
  /// <summary>
  /// The cursor is hidden and confined; mouse movement produces deltas that
  /// allow unlimited rotation (e.g. first-person camera look). The backend
  /// implements this via its native mechanism (raylib's DisableCursor,
  /// MonoGame's re-center-after-poll).
  /// </summary>
  | Captured

/// <summary>
/// Per-game input service providing reactive observables for hardware input.
/// </summary>
/// <remarks>
/// The contract is defined in <c>Mibo.Core</c>; each backend
/// (<c>Mibo.Raylib</c>, <c>Mibo.MonoGame</c>) supplies a concrete implementation
/// that polls its native API and emits backend-neutral delta values.
/// Implementations are registered into <see cref="T:Mibo.Elmish.GameContext"/>
/// by the runtime host and accessed via <see cref="M:Mibo.Input.Input.getService"/>.
/// </remarks>
type IInput =
  abstract Poll: unit -> unit
  abstract SetMouseCapture: MouseCapture -> unit
  abstract KeyboardDelta: IObservable<KeyboardDelta>
  abstract MouseDelta: IObservable<MouseDelta>
  abstract TouchDelta: IObservable<TouchDelta>
  abstract GamepadDelta: IObservable<GamepadDelta>
  abstract GamepadConnection: IObservable<GamepadConnection>
  abstract GestureDelta: IObservable<GestureDelta>
