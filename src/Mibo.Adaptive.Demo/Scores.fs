module Mibo.Adaptive.Demo.Scores

open AdaptiveSlop.Core
open Types

/// The scoreboard feature: the scores root and the label projection.
type State = {
  /// The score counts. The root.
  Value: cval<Scores>

  /// The score text, derived from the root. Recomputed only when the score
  /// changes.
  Label: aval<string>
}

/// Creates the feature: builds the root and the projection.
let create() : State =
  let value = CVal.create({ Left = 0; Right = 0 })

  let label =
    CVal.value value
    |> AVal.map(fun s ->
      Telemetry.scoreLabel <- Telemetry.scoreLabel + 1
      sprintf "%d   -   %d" s.Left s.Right)

  { Value = value; Label = label }

/// Awards a point to the given side.
let addPoint (scores: State) (side: PaddleSide) : unit =
  let s = AVal.getValue scores.Value

  scores.Value.Set(
    if side = Left then
      { s with Left = s.Left + 1 }
    else
      { s with Right = s.Right + 1 }
  )
