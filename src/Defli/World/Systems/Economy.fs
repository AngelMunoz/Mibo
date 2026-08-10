module Defli.World.Systems.Economy

open AdaptiveSlop.Core
open Defli
open Defli.World

// ─────────────────────────────────────────────────────────────
// Economy sub-system — two singletons, one system. No events out
// (nothing consumes economy output except the view/router); kills
// and arrivals reach it via router-translated Cmd.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type EconomyMsg =
  | SpendGold of amount: int
  | EarnGold of amount: int
  | LoseLife

type EconomyModel() =
  member val Gold = CVal.create 0 with get, set
  member val Lives = CVal.create 0 with get, set
  // Own projection (showcase #4): game over.
  member val GameOver: aval<bool> = Unchecked.defaultof<_> with get, set

module Economy =

  let init(cfg: WorldConfig) : EconomyModel =
    let m = EconomyModel()
    m.Gold.Set cfg.StartingGold
    m.Lives.Set cfg.StartingLives

    m.GameOver <-
      m.Lives
      |> AVal.map(fun lives ->
        Telemetry.gameOver <- Telemetry.gameOver + 1
        lives <= 0)

    m

  let update (msg: EconomyMsg) (model: EconomyModel) : unit =
    match msg with
    | SpendGold amount ->
      let gold = model.Gold |> AVal.getValue
      model.Gold.UpdateTo(max 0 (gold - amount)) |> ignore
    | EarnGold amount ->
      let gold = model.Gold |> AVal.getValue
      model.Gold.UpdateTo(gold + amount) |> ignore
    | LoseLife ->
      let lives = model.Lives |> AVal.getValue
      model.Lives.UpdateTo(max 0 (lives - 1)) |> ignore
