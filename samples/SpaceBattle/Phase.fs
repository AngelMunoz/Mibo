namespace SpaceBattle

open SpaceBattle.Units

module Phase =

  type TurnPhase =
    | Active
    | Resolving

  type TurnOrder = { Factions: Faction[]; Index: int }

  type Action =
    | Move
    | Attack
    | Rest
    | Capture

  type Turn = {
    Phase: TurnPhase
    CurrentFaction: Faction
    TurnNumber: int
    Moved: Set<struct (int * int)>
    Acted: Set<struct (int * int)>
  }

  type PhaseMsg =
    | EndTurn
    | PerformAction of action: Action * cell: struct (int * int)
    | Resolution

  let inline createTurnOrder(factions: Faction[]) : TurnOrder = {
    Factions = factions
    Index = 0
  }

  let inline newTurn(order: TurnOrder) : Turn = {
    Phase = Active
    CurrentFaction = order.Factions[order.Index]
    TurnNumber = 0
    Moved = Set.empty
    Acted = Set.empty
  }

  let inline markMoved cell (turn: Turn) = {
    turn with
        Moved = turn.Moved |> Set.add cell
  }

  let inline markActed cell (turn: Turn) = {
    turn with
        Acted = turn.Acted |> Set.add cell
  }

  let inline hasMoved cell (turn: Turn) = turn.Moved |> Set.contains cell

  let inline hasActed cell (turn: Turn) = turn.Acted |> Set.contains cell

  let inline canMove cell (turn: Turn) =
    turn.Phase = Active && not(turn.Moved |> Set.contains cell)

  let inline canPerformAction cell (turn: Turn) =
    turn.Phase = Active && not(turn.Acted |> Set.contains cell)

  let inline canAct cell (turn: Turn) =
    turn.Phase = Active
    && (not(turn.Moved |> Set.contains cell)
        || not(turn.Acted |> Set.contains cell))

  let advanceTurn (turn: Turn) (order: TurnOrder) : struct (Turn * TurnOrder) =
    let newIndex = (order.Index + 1) % order.Factions.Length

    {
      turn with
          Phase = Active
          CurrentFaction = order.Factions[newIndex]
          TurnNumber = turn.TurnNumber + 1
          Moved = Set.empty
          Acted = Set.empty
    },
    {
      order with
          Index = (order.Index + 1) % order.Factions.Length
    }


  module System =
    let update msg turn turnOrder : struct (Turn * TurnOrder) =
      match msg with
      | PerformAction(Move, cell) ->
        {
          markMoved cell turn with
              Phase = Resolving
        },
        turnOrder
      | PerformAction(kind, cell) ->
        {
          markActed cell turn with
              Phase = Resolving
        },
        turnOrder
      | Resolution -> { turn with Phase = Active }, turnOrder
      | EndTurn -> advanceTurn turn turnOrder
