module Mibo.Tests.InputMapper

open Expecto
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Input

type Action =
  | MoveUp
  | MoveDown
  | Jump

[<Tests>]
let tests =
  testList "InputMapper" [
    let emptyMap = InputMap.empty

    let map =
      emptyMap
      |> InputMap.key MoveUp Keys.W
      |> InputMap.key MoveDown Keys.S
      |> InputMap.key Jump Keys.Space

    testCase "ActionState.update starts an action"
    <| fun _ ->
      let state = ActionState.empty
      let newState = ActionState.update map true (Key Keys.W) state

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
            HeldTriggers = Set.singleton(Key Keys.W)
      }

      let newState = ActionState.update map false (Key Keys.W) state

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
        |> InputMap.key Jump Keys.Space
        |> InputMap.gamepadButton Jump PlayerIndex.One Buttons.A

      let state = ActionState.empty
      // Press Space
      let state2 = ActionState.update map true (Key Keys.Space) state
      Expect.contains state2.Held Jump "Jump should be held by Space"

      // Press Gamepad A
      let state3 =
        ActionState.update
          map
          true
          (GamepadBut(PlayerIndex.One, Buttons.A))
          state2

      Expect.contains state3.Held Jump "Jump should still be held"

      // Release Space (Jump still held by Gamepad A)
      let state4 = ActionState.update map false (Key Keys.Space) state3
      Expect.contains state4.Held Jump "Jump should still be held by Gamepad A"

      Expect.isFalse
        (state4.Released.Contains Jump)
        "Jump should NOT be released yet"

      // Release Gamepad A
      let state5 =
        ActionState.update
          map
          false
          (GamepadBut(PlayerIndex.One, Buttons.A))
          state4

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
  ]
