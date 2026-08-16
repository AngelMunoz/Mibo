module Mibo.Core.Tests.InputMapper

open Expecto
open Mibo.Input

type Action =
  | MoveUp
  | MoveDown
  | Jump

/// A minimal state builder for the merge tests — the merge is pure, so
/// the tests build states directly.
let private state
  (started: Action list)
  (released: Action list)
  (held: Action list)
  : ActionState<Action> =
  {
    Started = Set.ofList started
    Released = Set.ofList released
    Held = Set.ofList held
    Values = held |> List.map(fun a -> a, 1.0f) |> Map.ofList
    HeldTriggers = Set.empty
  }

[<Tests>]
let tests =
  testList "InputMapper.mergeEdges" [
    testCase "edges accumulate across merges"
    <| fun _ ->
      // A key press, then a mouse-move build (empty edges) between two
      // consumptions: the press's Started must survive the merge.
      let afterPress =
        ActionState.mergeEdges ActionState.empty (state [ Jump ] [] [ Jump ])

      let afterMouseMove =
        ActionState.mergeEdges afterPress (state [] [] [ Jump ])

      Expect.contains
        afterMouseMove.Started
        Jump
        "the press edge survives the empty build"

      Expect.contains afterMouseMove.Held Jump "held stays"

    testCase "Held/Values stay last-wins"
    <| fun _ ->
      let current = state [] [] [ MoveUp ]

      let incoming = state [] [] [ MoveDown ]

      let merged = ActionState.mergeEdges current incoming

      Expect.isFalse
        (merged.Held.Contains MoveUp)
        "held comes from the incoming state"

      Expect.contains merged.Held MoveDown "held is the current truth"

      Expect.isFalse
        (merged.Values.ContainsKey MoveUp)
        "values come from the incoming state"

    testCase "clearing with nextFrame empties the edges for the next merge"
    <| fun _ ->
      // The consumption cycle: merge → read → clear → the next merge
      // starts from an empty edge set.
      let consumed =
        ActionState.mergeEdges ActionState.empty (state [ Jump ] [ MoveUp ] [])
        |> ActionState.nextFrame

      let merged = ActionState.mergeEdges consumed (state [] [] [])

      Expect.isEmpty merged.Started "cleared edges do not re-appear"
      Expect.isEmpty merged.Released "cleared edges do not re-appear"

    testCase "a fast tap keeps both edges of the same action"
    <| fun _ ->
      // Press and release between two consumptions: both edges must be
      // observable so an add/subtract consumer nets zero.
      let merged =
        ActionState.mergeEdges
          (state [ Jump ] [] [ Jump ])
          (state [] [ Jump ] [])

      Expect.contains merged.Started Jump "the press edge is kept"

      Expect.contains merged.Released Jump "the release edge is kept"

    testCase "merging into an empty state is the incoming state's edges"
    <| fun _ ->
      let merged =
        ActionState.mergeEdges
          ActionState.empty
          (state [ MoveUp ] [ MoveDown ] [])

      Expect.equal
        merged.Started
        (Set.ofList [ MoveUp ])
        "started is exactly the incoming edge"

      Expect.equal
        merged.Released
        (Set.ofList [ MoveDown ])
        "released is exactly the incoming edge"
  ]
