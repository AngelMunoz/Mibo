namespace SpaceBattle

open System.Numerics
open SpaceBattle.Units
open SpaceBattle.AnimState

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
    | CellClicked of cell: struct (int * int)

  [<Struct>]
  type Intent =
    | SwitchSelection of cell: struct (int * int)
    | PerformMove of from: struct (int * int) * dest: struct (int * int)
    | PerformAttack of attacker: struct (int * int) * target: struct (int * int)
    | ClearSelection
    | NoIntent

  [<Struct>]
  type PhaseResult = {
    Selection: SelectionState
    Units: Map<struct (int * int), SBUnit>
    MapModel: MapModel
    Intent: Intent
    Turn: Turn
    TurnOrder: TurnOrder
    Anim: AnimationState
  }

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

    open SpaceBattle.Types
    open Mibo.Layout

    let determineIntent
      (cell: struct (int * int))
      (selection: SelectionState)
      (units: Map<struct (int * int), SBUnit>)
      (reachable: Set<struct (int * int)>)
      (currentFaction: Faction)
      (turn: Turn)
      : Intent =
      if turn.Phase <> Active then
        NoIntent
      else
        match selection with
        | Selected src ->
          let hasUnitAt cell = units |> Map.tryFind cell

          let isFriendly =
            function
            | Some(u: SBUnit) -> u.Faction = currentFaction
            | None -> false

          let isReachable = reachable.Contains cell

          let canMoveHere =
            Option.isNone(hasUnitAt cell) && isReachable && canMove src turn

          match
            hasUnitAt cell, isFriendly(hasUnitAt cell), isReachable, canMoveHere
          with
          | Some _, true, _, _ ->
            if cell = src then ClearSelection else SwitchSelection cell
          | Some _, false, true, _ -> PerformAttack(src, cell)
          | Some _, false, false, _ -> ClearSelection
          | None, _, _, true -> PerformMove(src, cell)
          | None, _, _, false -> ClearSelection
        | NoSelection ->
          match units |> Map.tryFind cell with
          | Some u when u.Faction = currentFaction -> SwitchSelection cell
          | _ -> NoIntent

    let update msg turn turnOrder : struct (Turn * TurnOrder) =
      match msg with
      | PerformAction(Move, cell) ->
        {
          markMoved cell turn with
              Phase = Resolving
        },
        turnOrder
      | PerformAction(_, cell) ->
        {
          markActed cell turn with
              Phase = Resolving
        },
        turnOrder
      | Resolution -> { turn with Phase = Active }, turnOrder
      | EndTurn -> advanceTurn turn turnOrder
      | CellClicked _cell -> turn, turnOrder

    let apply
      (phaseMsg: PhaseMsg)
      (selection: SelectionState)
      (units: Map<struct (int * int), SBUnit>)
      (mapModel: MapModel)
      (hovered: struct (int * int) voption)
      (turn: Turn)
      (turnOrder: TurnOrder)
      (anim: AnimationState)
      : PhaseResult =

      let intent =
        match phaseMsg with
        | CellClicked cell ->
          determineIntent
            cell
            selection
            units
            mapModel.Reachable
            turn.CurrentFaction
            turn
        | _ -> NoIntent

      let struct (turn, turnOrder) =
        match intent with
        | PerformMove(src, _) ->
          update (PerformAction(Move, src)) turn turnOrder
        | PerformAttack(src, _) ->
          update (PerformAction(Attack, src)) turn turnOrder
        | _ -> update phaseMsg turn turnOrder

      let selection, units, mapModel =
        match intent with
        | SwitchSelection cell ->
          let sel = Selected cell

          sel,
          units,
          Map.update
            RecalculateRange
            mapModel
            sel
            hovered
            units
            turn.CurrentFaction
        | PerformMove(src, dest) ->
          let units =
            match units |> Map.tryFind src with
            | Some unit -> units |> Map.remove src |> Map.add dest unit
            | None -> units

          NoSelection,
          units,
          Map.update
            RecalculateRange
            mapModel
            NoSelection
            ValueNone
            units
            turn.CurrentFaction
        | PerformAttack(_src, _target) ->
          // TODO: apply damage
          NoSelection,
          units,
          Map.update
            RecalculateRange
            mapModel
            NoSelection
            ValueNone
            units
            turn.CurrentFaction
        | ClearSelection ->
          NoSelection,
          units,
          Map.update
            RecalculateRange
            mapModel
            NoSelection
            ValueNone
            units
            turn.CurrentFaction
        | NoIntent -> selection, units, mapModel

      let anim =
        match turn.Phase with
        | Resolving ->
          match intent with
          | PerformMove(src, dest) ->
            let struct (sc, sr) = src
            let struct (dc, dr) = dest
            let fromPos = mapModel.Grid |> HexGrid.getWorldPos sc sr
            let toPos = mapModel.Grid |> HexGrid.getWorldPos dc dr
            AnimState.startMove src dest fromPos toPos anim
          | SwitchSelection _
          | PerformAttack _
          | ClearSelection
          | NoIntent -> anim

        | Active -> anim

      {
        Selection = selection
        Units = units
        MapModel = mapModel
        Intent = intent
        Turn = turn
        TurnOrder = turnOrder
        Anim = anim
      }
