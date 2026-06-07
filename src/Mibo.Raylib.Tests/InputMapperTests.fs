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
  ]
