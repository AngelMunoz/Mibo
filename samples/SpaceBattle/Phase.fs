namespace SpaceBattle

open System.Numerics
open SpaceBattle.Types
open SpaceBattle.Units

module Phase =

  type TurnPhase =
    | Active
    | Resolving

  type TurnOrder = {
    Factions: Faction[]
    PlayerIndices: int[]
    PlayerControls: Units.UnitControl[]
    Index: int
  }

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
    CurrentPlayerIndex: int
    PlayerControl: Units.UnitControl
    TurnNumber: int
    Moved: ActionEntry list
    Acted: ActionEntry list
    PendingMove: struct (int * int) voption
    PendingAttack: (struct (int * int) * struct (int * int)) voption
  }

  type PhaseMsg =
    | EndTurn
    | Resolution
    | TransitionDone
    | CellClicked of cell: struct (int * int)

  [<Struct>]
  type MoveIntent = {
    UnitId: int<UnitId>
    From: struct (int * int)
    Dest: struct (int * int)
  }

  [<Struct>]
  type AttackIntent = {
    AttackerId: int<UnitId>
    AttackerCell: struct (int * int)
    Target: struct (int * int)
  }

  [<Struct>]
  type MoveResolvedIntent = {
    Source: struct (int * int)
    Dest: struct (int * int)
  }

  [<Struct>]
  type AttackResolvedIntent = {
    Attacker: struct (int * int)
    Target: struct (int * int)
  }

  [<Struct>]
  type Intent =
    | SwitchSelection of cell: struct (int * int)
    | PerformMove of move: MoveIntent
    | PerformAttack of attack: AttackIntent
    | MoveResolved of moveResolved: MoveResolvedIntent
    | AttackResolved of attackResolved: AttackResolvedIntent
    | StartTransition of newFaction: Faction
    | ClearSelection
    | NoIntent

  [<Struct>]
  type PhaseResult = {
    Intent: Intent
    Turn: Turn
    TurnOrder: TurnOrder
  }

  // TODO: Revisit PhaseQuery as an interface once the shape stabilizes
  [<Struct>]
  type PhaseQuery = {
    Selection: SelectionState
    UnitAt: struct (int * int) -> SBUnit voption
    IsReachable: struct (int * int) -> bool
    IsAttackable: struct (int * int) -> bool
    IsVisible: struct (int * int) -> bool
    CurrentFaction: Faction
    CurrentPlayerIndex: int
    PlayerControl: Units.UnitControl
  }

  [<Struct>]
  type PhaseInput = {
    Msg: PhaseMsg
    Query: PhaseQuery
    Turn: Turn
    TurnOrder: TurnOrder
  }

  [<Struct>]
  type IntentInput = {
    Cell: struct (int * int)
    Query: PhaseQuery
    Turn: Turn
  }

  let inline createTurnOrder
    (factions: Faction[])
    (playerIndices: int[])
    (playerControls: Units.UnitControl[])
    : TurnOrder =
    {
      Factions = factions
      PlayerIndices = playerIndices
      PlayerControls = playerControls
      Index = 0
    }

  let inline private resetPending(turn: Turn) : Turn = {
    turn with
        Moved = []
        Acted = []
        PendingMove = ValueNone
        PendingAttack = ValueNone
  }

  let emptyTurn: Turn =
    resetPending {
      Phase = Active
      CurrentFaction = Federation
      CurrentPlayerIndex = 0
      PlayerControl = Units.Human
      TurnNumber = 0
      Moved = []
      Acted = []
      PendingMove = ValueNone
      PendingAttack = ValueNone
    }

  let inline newTurn(order: TurnOrder) : Turn =
    if order.Factions.Length = 0 then
      emptyTurn
    else
      resetPending {
        Phase = Active
        CurrentFaction = order.Factions[order.Index]
        CurrentPlayerIndex = order.PlayerIndices[order.Index]
        PlayerControl = order.PlayerControls[order.Index]
        TurnNumber = 0
        Moved = []
        Acted = []
        PendingMove = ValueNone
        PendingAttack = ValueNone
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

    resetPending {
      turn with
          Phase = Active
          CurrentFaction = order.Factions[newIndex]
          CurrentPlayerIndex = order.PlayerIndices[newIndex]
          PlayerControl = order.PlayerControls[newIndex]
          TurnNumber = turn.TurnNumber + 1
    },
    { order with Index = newIndex }


  module System =

    open SpaceBattle.Types
    open Mibo.Elmish

    let private determineIntent(input: IntentInput) : Intent =
      if input.Turn.Phase <> TurnPhase.Active then
        NoIntent
      else

        match input.Query.Selection with
        | Selected src ->
          let struct (col, row) = src
          let actingUnit = input.Query.UnitAt src
          let targetUnit = input.Query.UnitAt input.Cell

          match actingUnit, targetUnit with
          | ValueSome { id = id; PlayerIndex = playerIdx }, ValueNone ->
            if
              input.Query.CurrentPlayerIndex = playerIdx
              && input.Query.IsReachable input.Cell
              && canMove id input.Turn
            then
              PerformMove {
                UnitId = id
                From = src
                Dest = input.Cell
              }
            else
              ClearSelection
          | ValueSome { id = id; PlayerIndex = playerIdx },
            ValueSome { PlayerIndex = targetPlayerIdx } ->
            if
              input.Query.CurrentPlayerIndex = playerIdx
              && playerIdx <> targetPlayerIdx
              && canPerformAction id input.Turn
              && input.Query.IsAttackable input.Cell
              && (input.Query.PlayerControl = Units.AI
                  || input.Query.IsVisible input.Cell)
            then
              PerformAttack {
                AttackerId = id
                AttackerCell = struct (col, row)
                Target = input.Cell
              }
            elif
              input.Query.CurrentPlayerIndex = playerIdx && input.Cell <> src
            then
              SwitchSelection input.Cell
            else
              ClearSelection
          | ValueNone, _ -> ClearSelection
        | NoSelection ->
          let isSomethingThere = input.Query.UnitAt input.Cell

          match isSomethingThere with
          | ValueSome { PlayerIndex = playerIdx } ->
            if playerIdx = input.Query.CurrentPlayerIndex then
              SwitchSelection input.Cell
            else
              NoIntent
          | ValueNone -> NoIntent

    let update(input: PhaseInput) : struct (PhaseResult * Cmd<PhaseMsg>) =
      match input.Msg with
      | CellClicked cell ->
        let intent =
          determineIntent {
            Cell = cell
            Query = input.Query
            Turn = input.Turn
          }

        match intent with
        | PerformMove move ->
          let entry = {
            UnitId = move.UnitId
            Source = move.From
            Target = move.Dest
          }

          let turn = {
            markMoved entry input.Turn with
                Phase = Resolving
                PendingMove = ValueSome move.Dest
          }

          {
            Intent = PerformMove move
            Turn = turn
            TurnOrder = input.TurnOrder
          },
          Cmd.none

        | PerformAttack attack ->
          let entry = {
            UnitId = attack.AttackerId
            Source = attack.AttackerCell
            Target = attack.Target
          }

          let turn = {
            markActed entry input.Turn with
                Phase = Resolving
                PendingAttack = ValueSome(attack.AttackerCell, attack.Target)
          }

          {
            Intent = PerformAttack attack
            Turn = turn
            TurnOrder = input.TurnOrder
          },
          Cmd.none

        | SwitchSelection _ ->
          {
            Intent = SwitchSelection cell
            Turn = input.Turn
            TurnOrder = input.TurnOrder
          },
          Cmd.none

        | ClearSelection ->
          {
            Intent = ClearSelection
            Turn = input.Turn
            TurnOrder = input.TurnOrder
          },
          Cmd.none

        | NoIntent ->
          {
            Intent = NoIntent
            Turn = input.Turn
            TurnOrder = input.TurnOrder
          },
          Cmd.none

        | _ ->
          {
            Intent = intent
            Turn = input.Turn
            TurnOrder = input.TurnOrder
          },
          Cmd.none

      | Resolution ->
        let intent =
          match input.Turn.PendingMove with
          | ValueSome dest ->
            match input.Turn.Moved with
            | entry :: _ -> MoveResolved { Source = entry.Source; Dest = dest }
            | [] -> NoIntent
          | ValueNone ->
            match input.Turn.PendingAttack with
            | ValueSome(src, target) ->
              AttackResolved { Attacker = src; Target = target }
            | ValueNone -> NoIntent

        let turn = {
          input.Turn with
              Phase = TurnPhase.Active
              PendingMove = ValueNone
              PendingAttack = ValueNone
        }

        {
          Intent = intent
          Turn = turn
          TurnOrder = input.TurnOrder
        },
        Cmd.none

      | EndTurn ->
        let nextIndex =
          (input.TurnOrder.Index + 1) % input.TurnOrder.Factions.Length

        let nextFaction = input.TurnOrder.Factions[nextIndex]

        {
          Intent = StartTransition nextFaction
          Turn = input.Turn
          TurnOrder = input.TurnOrder
        },
        Cmd.none

      | TransitionDone ->
        let struct (turn, order) = advanceTurn input.Turn input.TurnOrder

        {
          Intent = NoIntent
          Turn = turn
          TurnOrder = order
        },
        Cmd.none

  module Debug =

    open Mibo.Elmish.Next.Graphics2D
    open Raylib_cs

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
