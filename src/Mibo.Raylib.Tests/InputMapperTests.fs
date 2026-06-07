module Mibo.Raylib.Tests.InputMapper

open Expecto
open Raylib_cs
open Mibo.Input

type Action =
  | MoveUp
  | MoveDown
  | Jump
  | Save
  | DebugToggle

[<Tests>]
let tests =
  testList "InputMapper" [
    let emptyMap = InputMap.empty

    let map =
      emptyMap
      |> InputMap.key MoveUp KeyboardKey.W
      |> InputMap.key MoveDown KeyboardKey.S
      |> InputMap.key Jump KeyboardKey.Space

    testCase "ActionState.update starts an action"
    <| fun _ ->
      let state = ActionState.empty
      let newState = ActionState.update map true (Key KeyboardKey.W) state

      Expect.contains newState.Started MoveUp "MoveUp should have started"
      Expect.contains newState.Held MoveUp "MoveUp should be held"

      Expect.equal
        (Map.find MoveUp newState.Values)
        1.0f
        "MoveUp value should be 1.0"

    testCase "ActionState.update releases an action"
    <| fun _ ->
      let state = {
        ActionState.empty with
            Held = Set.singleton MoveUp
            HeldTriggers = Set.singleton(Key KeyboardKey.W)
      }

      let newState = ActionState.update map false (Key KeyboardKey.W) state

      Expect.isFalse (newState.Held.Contains MoveUp) "MoveUp should not be held"

      Expect.contains
        newState.Released
        MoveUp
        "MoveUp should be in released set"

      Expect.isFalse
        (newState.Values.ContainsKey MoveUp)
        "MoveUp value should be removed"

    testCase "ActionState.update handles multiple triggers for same action"
    <| fun _ ->
      let map =
        emptyMap |> InputMap.key Jump KeyboardKey.Space |> InputMap.mouse Jump 0

      let state = ActionState.empty
      // Press Space
      let state2 = ActionState.update map true (Key KeyboardKey.Space) state
      Expect.contains state2.Held Jump "Jump should be held by Space"

      // Left mouse click
      let state3 = ActionState.update map true (MouseBut 0) state2

      Expect.contains state3.Held Jump "Jump should still be held"

      // Release Space (Jump still held by Mouse)
      let state4 = ActionState.update map false (Key KeyboardKey.Space) state3
      Expect.contains state4.Held Jump "Jump should still be held by Mouse"

      Expect.isFalse
        (state4.Released.Contains Jump)
        "Jump should NOT be released yet"

      // Release Mouse
      let state5 = ActionState.update map false (MouseBut 0) state4

      Expect.isFalse
        (state5.Held.Contains Jump)
        "Jump should finally be released"

      Expect.contains state5.Released Jump "Jump should be in released set"

    testCase "ActionState.nextFrame clears one-shots"
    <| fun _ ->
      let state = {
        ActionState.empty with
            Started = Set.singleton Jump
            Released = Set.singleton MoveUp
      }

      let next = ActionState.nextFrame state

      Expect.isEmpty next.Started "Started should be cleared"
      Expect.isEmpty next.Released "Released should be cleared"

    testCase "KeyCombo starts when all keys are held"
    <| fun _ ->
      let combo = Set [ KeyboardKey.LeftControl; KeyboardKey.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let state = ActionState.empty
      // Combo is down (all keys held)
      let newState = ActionState.update map true (KeyCombo combo) state

      Expect.contains newState.Started Save "Save should have started"
      Expect.contains newState.Held Save "Save should be held"

      Expect.equal
        (Map.find Save newState.Values)
        1.0f
        "Save value should be 1.0"

    testCase "KeyCombo releases when any key is released"
    <| fun _ ->
      let combo = Set [ KeyboardKey.LeftControl; KeyboardKey.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let state = {
        ActionState.empty with
            Held = Set.singleton Save
            HeldTriggers = Set.singleton(KeyCombo combo)
      }

      // Combo is no longer down (some key released)
      let newState = ActionState.update map false (KeyCombo combo) state

      Expect.isFalse (newState.Held.Contains Save) "Save should not be held"

      Expect.contains newState.Released Save "Save should be in released set"

    testCase "KeyCombo does not start when only some keys are held"
    <| fun _ ->
      let combo = Set [ KeyboardKey.LeftControl; KeyboardKey.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let state = ActionState.empty
      // Only Ctrl is down, not the full combo
      let state2 =
        ActionState.update map true (Key KeyboardKey.LeftControl) state

      Expect.isFalse
        (state2.Held.Contains Save)
        "Save should not be held with only Ctrl"

      // Now the full combo is down
      let state3 = ActionState.update map true (KeyCombo combo) state2

      Expect.contains state3.Held Save "Save should be held when combo complete"

    testCase "KeyCombo works with multiple combos for same action"
    <| fun _ ->
      let combo1 = Set [ KeyboardKey.LeftControl; KeyboardKey.G ]
      let combo2 = Set [ KeyboardKey.LeftControl; KeyboardKey.D ]

      let map =
        emptyMap
        |> InputMap.keyCombo DebugToggle combo1
        |> InputMap.keyCombo DebugToggle combo2

      let state = ActionState.empty
      // First combo down
      let state2 = ActionState.update map true (KeyCombo combo1) state

      Expect.contains
        state2.Held
        DebugToggle
        "DebugToggle should be held by combo1"

      // Second combo also down
      let state3 = ActionState.update map true (KeyCombo combo2) state2

      Expect.contains state3.Held DebugToggle "DebugToggle should still be held"

      // First combo released
      let state4 = ActionState.update map false (KeyCombo combo1) state3

      Expect.contains
        state4.Held
        DebugToggle
        "DebugToggle should still be held by combo2"

      Expect.isFalse
        (state4.Released.Contains DebugToggle)
        "DebugToggle should NOT be released yet"

      // Second combo released
      let state5 = ActionState.update map false (KeyCombo combo2) state4

      Expect.isFalse
        (state5.Held.Contains DebugToggle)
        "DebugToggle should finally be released"

      Expect.contains
        state5.Released
        DebugToggle
        "DebugToggle should be in released set"

    // ─────────────────────────────────────────────────────────────────────
    // Integration tests for buildActions (subscription flow)
    // ─────────────────────────────────────────────────────────────────────

    testCase "buildActions: KeyCombo starts when all keys pressed individually"
    <| fun _ ->
      let combo = Set [ KeyboardKey.LeftControl; KeyboardKey.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let mutable heldKeys = Set.empty

      let isKeyDown k = heldKeys |> Set.contains k
      let isMouseButtonDown _ = false
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Press Ctrl
      heldKeys <- heldKeys |> Set.add KeyboardKey.LeftControl

      let state1, comboStates1 =
        InputMapper.buildActions
          getMap
          Map.empty
          [| Key KeyboardKey.LeftControl |]
          [||]
          (Set.singleton KeyboardKey.LeftControl)
          Set.empty
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.isFalse
        (state1.Started.Contains Save)
        "Save should NOT start with only Ctrl"

      Expect.isFalse
        (state1.Held.Contains Save)
        "Save should NOT be held with only Ctrl"

      // Press S (now both keys held)
      heldKeys <- heldKeys |> Set.add KeyboardKey.S

      let state2, _comboStates2 =
        InputMapper.buildActions
          getMap
          comboStates1
          [| Key KeyboardKey.S |]
          [||]
          (Set.singleton KeyboardKey.S)
          Set.empty
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains state2.Started Save "Save should start when S pressed"

      Expect.contains
        state2.Held
        Save
        "Save should be held when both keys pressed"

    testCase "buildActions: KeyCombo releases when any key released"
    <| fun _ ->
      let combo = Set [ KeyboardKey.LeftControl; KeyboardKey.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let mutable heldKeys = Set [ KeyboardKey.LeftControl; KeyboardKey.S ]

      let isKeyDown k = heldKeys |> Set.contains k
      let isMouseButtonDown _ = false
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Initial state: both keys held, combo active
      let initialComboStates = Map.empty |> Map.add combo true

      // Release S
      heldKeys <- heldKeys |> Set.remove KeyboardKey.S

      let state1, _comboStates1 =
        InputMapper.buildActions
          getMap
          initialComboStates
          [||]
          [| Key KeyboardKey.S |]
          Set.empty
          (Set.singleton KeyboardKey.S)
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.isFalse
        (state1.Held.Contains Save)
        "Save should not be held after S released"

      Expect.contains state1.Released Save "Save should be in released set"

    testCase "buildActions: KeyCombo does not trigger from mouse events"
    <| fun _ ->
      let combo = Set [ KeyboardKey.LeftControl; KeyboardKey.S ]

      let map =
        emptyMap |> InputMap.keyCombo Save combo |> InputMap.mouse Jump 0

      let isKeyDown _ = false
      let mutable mouseDown = false
      let isMouseButtonDown _ = mouseDown
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Mouse click
      mouseDown <- true

      let state1, _comboStates1 =
        InputMapper.buildActions
          getMap
          Map.empty
          [| MouseBut 0 |]
          [||]
          Set.empty
          Set.empty
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains state1.Started Jump "Jump should start from mouse"

      Expect.isFalse
        (state1.Started.Contains Save)
        "Save should NOT start from mouse"

    testCase "buildActions: multiple combos with shared keys"
    <| fun _ ->
      let combo1 = Set [ KeyboardKey.LeftControl; KeyboardKey.G ]
      let combo2 = Set [ KeyboardKey.LeftControl; KeyboardKey.D ]

      let map =
        emptyMap
        |> InputMap.keyCombo DebugToggle combo1
        |> InputMap.keyCombo DebugToggle combo2

      let mutable heldKeys = Set.empty

      let isKeyDown k = heldKeys |> Set.contains k
      let isMouseButtonDown _ = false
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Press Ctrl
      heldKeys <- heldKeys |> Set.add KeyboardKey.LeftControl

      let state1, cs1 =
        InputMapper.buildActions
          getMap
          Map.empty
          [| Key KeyboardKey.LeftControl |]
          [||]
          (Set.singleton KeyboardKey.LeftControl)
          Set.empty
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.isFalse
        (state1.Held.Contains DebugToggle)
        "DebugToggle should not be held with only Ctrl"

      // Press G (combo1 complete)
      heldKeys <- heldKeys |> Set.add KeyboardKey.G

      let state2, cs2 =
        InputMapper.buildActions
          getMap
          cs1
          [| Key KeyboardKey.G |]
          [||]
          (Set.singleton KeyboardKey.G)
          Set.empty
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains
        state2.Started
        DebugToggle
        "DebugToggle should start from combo1"

      // Press D (combo2 also complete)
      heldKeys <- heldKeys |> Set.add KeyboardKey.D

      let state3, cs3 =
        InputMapper.buildActions
          getMap
          cs2
          [| Key KeyboardKey.D |]
          [||]
          (Set.singleton KeyboardKey.D)
          Set.empty
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains state3.Held DebugToggle "DebugToggle should still be held"

      // Release G (combo1 broken, but combo2 still holds Ctrl+D)
      heldKeys <- heldKeys |> Set.remove KeyboardKey.G

      let state4, cs4 =
        InputMapper.buildActions
          getMap
          cs3
          [||]
          [| Key KeyboardKey.G |]
          Set.empty
          (Set.singleton KeyboardKey.G)
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains
        state4.Held
        DebugToggle
        "DebugToggle should still be held by combo2"

      Expect.isFalse
        (state4.Released.Contains DebugToggle)
        "DebugToggle should NOT be released yet"

      // Release D (combo2 also broken)
      heldKeys <- heldKeys |> Set.remove KeyboardKey.D

      let state5, _cs5 =
        InputMapper.buildActions
          getMap
          cs4
          [||]
          [| Key KeyboardKey.D |]
          Set.empty
          (Set.singleton KeyboardKey.D)
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.isFalse
        (state5.Held.Contains DebugToggle)
        "DebugToggle should finally be released"

      Expect.contains
        state5.Released
        DebugToggle
        "DebugToggle should be in released set"
  ]
