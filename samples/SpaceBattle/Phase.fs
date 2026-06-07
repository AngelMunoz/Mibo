namespace SpaceBattle

open System.Numerics
open SpaceBattle.Types
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

  [<Struct>]
  type ActionEntry = {
    UnitId: int<UnitId>
    Source: struct (int * int)
    Target: struct (int * int)
  }

  type Turn = {
    Phase: TurnPhase
    CurrentFaction: Faction
    TurnNumber: int
    Moved: ActionEntry list
    Acted: ActionEntry list
  }

  type PhaseMsg =
    | EndTurn
    | Resolution
    | CellClicked of cell: struct (int * int)

  [<Struct>]
  type Intent =
    | SwitchSelection of cell: struct (int * int)
    | PerformMove of
      unitId: int<UnitId> *
      from: struct (int * int) *
      dest: struct (int * int)
    | PerformAttack of
      attacker: struct (int<UnitId> * int * int) *
      target: struct (int * int)
    | ClearSelection
    | NoIntent

  [<Struct>]
  type PhaseResult = {
    Selection: SelectionState
    Units: Map<struct (int * int), SBUnit>
    Intent: Intent
    Turn: Turn
    TurnOrder: TurnOrder
    Anim: AnimationState
  }

  [<Struct>]
  type PhaseInput = {
    Msg: PhaseMsg
    Selection: SelectionState
    Units: Map<struct (int * int), SBUnit>
    Grid: Mibo.Layout.HexGrid<Tile>
    Reachable: Set<struct (int * int)>
    Turn: Turn
    TurnOrder: TurnOrder
    Anim: AnimationState
  }

  [<Struct>]
  type IntentInput = {
    Cell: struct (int * int)
    Selection: SelectionState
    Units: Map<struct (int * int), SBUnit>
    Reachable: Set<struct (int * int)>
    CurrentFaction: Faction
    Turn: Turn
  }

  let inline createTurnOrder(factions: Faction[]) : TurnOrder = {
    Factions = factions
    Index = 0
  }

  let inline newTurn(order: TurnOrder) : Turn = {
    Phase = Active
    CurrentFaction = order.Factions[order.Index]
    TurnNumber = 0
    Moved = []
    Acted = []
  }

  let inline private hasEntry id (lst: ActionEntry list) =
    lst |> List.exists(fun e -> e.UnitId = id)

  let inline markMoved (entry: ActionEntry) (turn: Turn) = {
    turn with
        Moved = entry :: turn.Moved
  }

  let inline markActed (entry: ActionEntry) (turn: Turn) = {
    turn with
        Acted = entry :: turn.Acted
  }

  let inline hasMoved id (turn: Turn) = hasEntry id turn.Moved

  let inline hasActed id (turn: Turn) = hasEntry id turn.Acted

  let inline canMove id (turn: Turn) =
    turn.Phase = Active && not(hasMoved id turn)

  let inline canPerformAction id (turn: Turn) =
    turn.Phase = Active && not(hasActed id turn)

  let inline canAct id (turn: Turn) =
    turn.Phase = Active && (not(hasMoved id turn) || not(hasActed id turn))

  let advanceTurn (turn: Turn) (order: TurnOrder) : struct (Turn * TurnOrder) =
    let newIndex = (order.Index + 1) % order.Factions.Length

    {
      turn with
          Phase = Active
          CurrentFaction = order.Factions[newIndex]
          TurnNumber = turn.TurnNumber + 1
          Moved = []
          Acted = []
    },
    {
      order with
          Index = (order.Index + 1) % order.Factions.Length
    }


  module System =

    open SpaceBattle.Types
    open Mibo.Elmish
    open Mibo.Layout

    let private determineIntent(input: IntentInput) : Intent =
      if input.Turn.Phase <> TurnPhase.Active then
        NoIntent
      else

        match input.Selection with
        | Selected src ->
          let struct (col, row) = src
          let actingUnit = input.Units |> Map.tryFind src
          let targetUnit = input.Units |> Map.tryFind input.Cell

          match actingUnit, targetUnit with
          | Some { id = id; Faction = actingFaction }, None ->
            if
              input.CurrentFaction = actingFaction
              && input.Reachable.Contains input.Cell
              && canMove id input.Turn
            then
              PerformMove(id, src, input.Cell)
            else
              ClearSelection
          | Some { id = id; Faction = actingFaction },
            Some { Faction = targetFaction } ->
            if
              input.CurrentFaction = actingFaction
              && actingFaction <> targetFaction
              && canPerformAction id input.Turn
              && input.Reachable.Contains input.Cell
            then
              PerformAttack((id, col, row), input.Cell)
            elif input.CurrentFaction = actingFaction && input.Cell <> src then
              SwitchSelection input.Cell
            else
              ClearSelection
          | None, _ -> ClearSelection
        | NoSelection ->
          let isSomethingThere = input.Units |> Map.tryFind input.Cell

          match isSomethingThere with
          | Some { Faction = faction } ->
            if faction = input.CurrentFaction then
              SwitchSelection input.Cell
            else
              NoIntent
          | None -> NoIntent

    let update(input: PhaseInput) : struct (PhaseResult * Cmd<PhaseMsg>) =
      match input.Msg with
      | CellClicked cell ->
        let intent =
          determineIntent {
            Cell = cell
            Selection = input.Selection
            Units = input.Units
            Reachable = input.Reachable
            CurrentFaction = input.Turn.CurrentFaction
            Turn = input.Turn
          }

        match intent with
        | PerformMove(id, src, dest) ->
          let entry = {
            UnitId = id
            Source = src
            Target = dest
          }

          let turn = {
            markMoved entry input.Turn with
                Phase = Resolving
          }

          let units =
            match input.Units |> Map.tryFind src with
            | Some unit -> input.Units |> Map.remove src |> Map.add dest unit
            | None -> input.Units

          let struct (sc, sr) = src
          let struct (dc, dr) = dest

          let fromPos = input.Grid |> HexGrid.getWorldPos sc sr

          let toPos = input.Grid |> HexGrid.getWorldPos dc dr

          let anim = AnimState.startMove src dest fromPos toPos input.Anim

          {
            Selection = NoSelection
            Units = units
            Intent = intent
            Turn = turn
            TurnOrder = input.TurnOrder
            Anim = anim
          },
          Cmd.none

        | PerformAttack(src, target) ->
          let struct (id, col, row) = src

          let entry = {
            UnitId = id
            Source = struct (col, row)
            Target = target
          }

          let turn = {
            markActed entry input.Turn with
                Phase = Resolving
          }

          {
            Selection = NoSelection
            Units = input.Units
            Intent = intent
            Turn = turn
            TurnOrder = input.TurnOrder
            Anim = input.Anim
          },
          Cmd.none

        | SwitchSelection _ ->
          {
            Selection = Selected cell
            Units = input.Units
            Intent = intent
            Turn = input.Turn
            TurnOrder = input.TurnOrder
            Anim = input.Anim
          },
          Cmd.none

        | ClearSelection ->
          {
            Selection = NoSelection
            Units = input.Units
            Intent = intent
            Turn = input.Turn
            TurnOrder = input.TurnOrder
            Anim = input.Anim
          },
          Cmd.none

        | NoIntent ->
          {
            Selection = input.Selection
            Units = input.Units
            Intent = intent
            Turn = input.Turn
            TurnOrder = input.TurnOrder
            Anim = input.Anim
          },
          Cmd.none

      | Resolution ->
        {
          Selection = input.Selection
          Units = input.Units
          Intent = NoIntent
          Turn = {
            input.Turn with
                Phase = TurnPhase.Active
          }
          TurnOrder = input.TurnOrder
          Anim = input.Anim
        },
        Cmd.none

      | EndTurn ->
        let struct (turn, order) = advanceTurn input.Turn input.TurnOrder

        {
          Selection = input.Selection
          Units = input.Units
          Intent = NoIntent
          Turn = turn
          TurnOrder = order
          Anim = input.Anim
        },
        Cmd.none

  module Debug =

    open Raylib_cs
    open Mibo.Elmish.Graphics2D

    let inline view
      (font: Font)
      (style: DebugUtils.DebugStyle)
      (turn: Turn)
      (turnOrder: TurnOrder)
      (x: int)
      (y: int)
      (buffer: RenderBuffer2D)
      : struct (int * RenderBuffer2D) =
      let struct (y, buffer) = DebugUtils.section font style x y "Turn" buffer

      let struct (y, buffer) =
        DebugUtils.kv font style x y "Phase" (string turn.Phase) buffer

      let struct (y, buffer) =
        DebugUtils.kv
          font
          style
          x
          y
          "Faction"
          (string turn.CurrentFaction)
          buffer

      let struct (y, buffer) =
        DebugUtils.kv font style x y "Turn#" (string turn.TurnNumber) buffer

      let moved =
        turn.Moved |> List.map(fun e -> string e.UnitId) |> String.concat ", "

      let struct (y, buffer) =
        DebugUtils.kv
          font
          style
          x
          y
          "Moved"
          (if moved = "" then "—" else moved)
          buffer

      let acted =
        turn.Acted |> List.map(fun e -> string e.UnitId) |> String.concat ", "

      let struct (y, buffer) =
        DebugUtils.kv
          font
          style
          x
          y
          "Acted"
          (if acted = "" then "—" else acted)
          buffer

      let struct (y, buffer) =
        DebugUtils.section font style x y "TurnOrder" buffer

      let factions =
        turnOrder.Factions |> Array.map string |> String.concat ", "

      let struct (y, buffer) =
        DebugUtils.kv font style x y "Factions" factions buffer

      let struct (y, buffer) =
        DebugUtils.kv font style x y "Index" (string turnOrder.Index) buffer

      struct (y, buffer)
