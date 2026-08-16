module Mibo.Raylib.Tests.InputMapper

open Expecto
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
      |> InputMap.key MoveUp KeyCode.W
      |> InputMap.key MoveDown KeyCode.S
      |> InputMap.key Jump KeyCode.Space

    testCase "ActionState.update starts an action"
    <| fun _ ->
      let state = ActionState.empty
      let newState = ActionState.update map true (Key KeyCode.W) state

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
            HeldTriggers = Set.singleton(Key KeyCode.W)
      }

      let newState = ActionState.update map false (Key KeyCode.W) state

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
        emptyMap
        |> InputMap.key Jump KeyCode.Space
        |> InputMap.mouse Jump MouseButtonCode.Left

      let state = ActionState.empty
      // Press Space
      let state2 = ActionState.update map true (Key KeyCode.Space) state
      Expect.contains state2.Held Jump "Jump should be held by Space"

      // Left mouse click
      let state3 =
        ActionState.update map true (MouseButton MouseButtonCode.Left) state2

      Expect.contains state3.Held Jump "Jump should still be held"

      // Release Space (Jump still held by Mouse)
      let state4 = ActionState.update map false (Key KeyCode.Space) state3
      Expect.contains state4.Held Jump "Jump should still be held by Mouse"

      Expect.isFalse
        (state4.Released.Contains Jump)
        "Jump should NOT be released yet"

      // Release Mouse
      let state5 =
        ActionState.update map false (MouseButton MouseButtonCode.Left) state4

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
      let combo = Set [ KeyCode.LeftControl; KeyCode.S ]

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
      let combo = Set [ KeyCode.LeftControl; KeyCode.S ]

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
      let combo = Set [ KeyCode.LeftControl; KeyCode.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let state = ActionState.empty
      // Only Ctrl is down, not the full combo
      let state2 = ActionState.update map true (Key KeyCode.LeftControl) state

      Expect.isFalse
        (state2.Held.Contains Save)
        "Save should not be held with only Ctrl"

      // Now the full combo is down
      let state3 = ActionState.update map true (KeyCombo combo) state2

      Expect.contains state3.Held Save "Save should be held when combo complete"

    testCase "KeyCombo works with multiple combos for same action"
    <| fun _ ->
      let combo1 = Set [ KeyCode.LeftControl; KeyCode.G ]
      let combo2 = Set [ KeyCode.LeftControl; KeyCode.D ]

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
      let combo = Set [ KeyCode.LeftControl; KeyCode.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let mutable heldKeys = Set.empty

      let isKeyDown k = heldKeys |> Set.contains k
      let isMouseButtonDown(_b: MouseButtonCode) = false
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Press Ctrl
      heldKeys <- heldKeys |> Set.add KeyCode.LeftControl

      let state1, comboStates1 =
        InputMapper.buildActions
          getMap
          Map.empty
          Set.empty
          [| Key KeyCode.LeftControl |]
          [||]
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
      heldKeys <- heldKeys |> Set.add KeyCode.S

      let state2, _comboStates2 =
        InputMapper.buildActions
          getMap
          comboStates1
          state1.Held
          [| Key KeyCode.S |]
          [||]
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
      let combo = Set [ KeyCode.LeftControl; KeyCode.S ]

      let map = emptyMap |> InputMap.keyCombo Save combo

      let mutable heldKeys = Set [ KeyCode.LeftControl; KeyCode.S ]

      let isKeyDown k = heldKeys |> Set.contains k
      let isMouseButtonDown(_b: MouseButtonCode) = false
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Initial state: both keys held, combo active
      let initialComboStates = Map.empty |> Map.add combo true

      // Release S
      heldKeys <- heldKeys |> Set.remove KeyCode.S

      let state1, _comboStates1 =
        InputMapper.buildActions
          getMap
          initialComboStates
          (Set.ofList [ Save ])
          [||]
          [| Key KeyCode.S |]
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.isFalse
        (state1.Held.Contains Save)
        "Save should not be held after S released"

      Expect.contains state1.Released Save "Save should be in released set"

    testCase
      "buildActions: synonym bindings — Started is a transition, Released only at full release"
    <| fun _ ->
      // The regression that locked Defli3D's pan: A and Left both map
      // MoveUp. Pressing Left while A already holds the action must
      // NOT re-fire Started (edge arithmetic — add on Started,
      // subtract on Released — would go +2/−1 and stick), and
      // releasing A while Left still holds must NOT fire Released.
      let map =
        emptyMap
        |> InputMap.key MoveUp KeyCode.A
        |> InputMap.key MoveUp KeyCode.Left

      let mutable heldKeys = Set.empty

      let isKeyDown k = heldKeys |> Set.contains k
      let isMouseButtonDown(_b: MouseButtonCode) = false
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      let build prevState pressed released =
        InputMapper.buildActions
          getMap
          Map.empty
          prevState
          pressed
          released
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      // Press A → Started once.
      heldKeys <- heldKeys |> Set.add KeyCode.A

      let s1, _ = build Set.empty [| Key KeyCode.A |] [||]

      Expect.contains s1.Started MoveUp "starts from the first binding"

      // Press Left (synonym) while A holds → Held stays, NO new Started.
      heldKeys <- heldKeys |> Set.add KeyCode.Left

      let s2, _ = build s1.Held [| Key KeyCode.Left |] [||]

      Expect.contains s2.Held MoveUp "still held"

      Expect.isFalse
        (s2.Started.Contains MoveUp)
        "synonym press is not a new start"

      // Release A while Left holds → NO Released.
      heldKeys <- heldKeys |> Set.remove KeyCode.A

      let s3, _ = build s2.Held [||] [| Key KeyCode.A |]

      Expect.contains s3.Held MoveUp "held by the remaining binding"

      Expect.isFalse
        (s3.Released.Contains MoveUp)
        "partial release is not a release"

      // Release Left → the one Released, edges balanced (+1/−1).
      heldKeys <- heldKeys |> Set.remove KeyCode.Left

      let s4, _ = build s3.Held [||] [| Key KeyCode.Left |]

      Expect.isFalse (s4.Held.Contains MoveUp) "fully released"
      Expect.contains s4.Released MoveUp "full release fires exactly once"

    testCase "buildActions: KeyCombo does not trigger from mouse events"
    <| fun _ ->
      let combo = Set [ KeyCode.LeftControl; KeyCode.S ]

      let map =
        emptyMap
        |> InputMap.keyCombo Save combo
        |> InputMap.mouse Jump MouseButtonCode.Left

      let isKeyDown _ = false
      let mutable mouseDown = false
      let isMouseButtonDown(_b: MouseButtonCode) = mouseDown
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Mouse click
      mouseDown <- true

      let state1, _comboStates1 =
        InputMapper.buildActions
          getMap
          Map.empty
          Set.empty
          [| MouseButton MouseButtonCode.Left |]
          [||]
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains state1.Started Jump "Jump should start from mouse"

      Expect.isFalse
        (state1.Started.Contains Save)
        "Save should NOT start from mouse"

    testCase "buildActions: multiple combos with shared keys"
    <| fun _ ->
      let combo1 = Set [ KeyCode.LeftControl; KeyCode.G ]
      let combo2 = Set [ KeyCode.LeftControl; KeyCode.D ]

      let map =
        emptyMap
        |> InputMap.keyCombo DebugToggle combo1
        |> InputMap.keyCombo DebugToggle combo2

      let mutable heldKeys = Set.empty

      let isKeyDown k = heldKeys |> Set.contains k
      let isMouseButtonDown(_b: MouseButtonCode) = false
      let isGamepadButtonDown _ _ = false

      let getMap() = map

      // Press Ctrl
      heldKeys <- heldKeys |> Set.add KeyCode.LeftControl

      let state1, cs1 =
        InputMapper.buildActions
          getMap
          Map.empty
          Set.empty
          [| Key KeyCode.LeftControl |]
          [||]
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.isFalse
        (state1.Held.Contains DebugToggle)
        "DebugToggle should not be held with only Ctrl"

      // Press G (combo1 complete)
      heldKeys <- heldKeys |> Set.add KeyCode.G

      let state2, cs2 =
        InputMapper.buildActions
          getMap
          cs1
          state1.Held
          [| Key KeyCode.G |]
          [||]
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains
        state2.Started
        DebugToggle
        "DebugToggle should start from combo1"

      // Press D (combo2 also complete)
      heldKeys <- heldKeys |> Set.add KeyCode.D

      let state3, cs3 =
        InputMapper.buildActions
          getMap
          cs2
          state2.Held
          [| Key KeyCode.D |]
          [||]
          isKeyDown
          isMouseButtonDown
          isGamepadButtonDown

      Expect.contains state3.Held DebugToggle "DebugToggle should still be held"

      // Release G (combo1 broken, but combo2 still holds Ctrl+D)
      heldKeys <- heldKeys |> Set.remove KeyCode.G

      let state4, cs4 =
        InputMapper.buildActions
          getMap
          cs3
          state3.Held
          [||]
          [| Key KeyCode.G |]
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
      heldKeys <- heldKeys |> Set.remove KeyCode.D

      let state5, _cs5 =
        InputMapper.buildActions
          getMap
          cs4
          state4.Held
          [||]
          [| Key KeyCode.D |]
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
