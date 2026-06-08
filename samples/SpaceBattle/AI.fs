namespace SpaceBattle

open Mibo.Layout
open SpaceBattle.Types
open SpaceBattle.Units
open SpaceBattle.Phase

module AI =

  [<Struct>]
  type AIAction =
    | AttackOnly of struct (int * int) * struct (int * int)
    | MoveAndAttack of
      moveFrom: struct (int * int) *
      moveTo: struct (int * int) *
      attackTarget: struct (int * int)
    | MoveOnly of struct (int * int) * struct (int * int)
    | NoAction

  let private hexDist
    (a: struct (int * int))
    (b: struct (int * int))
    (grid: HexGrid<Tile>)
    : float32 =
    let struct (ac, ar) = a
    let struct (bc, br) = b
    float32(Hex2DSpatial.distance ac ar bc br grid)

  let private findCenter(grid: HexGrid<Tile>) : struct (int * int) =
    let mutable sumC = 0
    let mutable sumR = 0
    let mutable count = 0


    for r in 0 .. grid.Height - 1 do
      for c in 0 .. grid.Width - 1 do
        match HexGrid.get c r grid with
        | ValueSome _ ->
          sumC <- sumC + c
          sumR <- sumR + r
          count <- count + 1
        | ValueNone -> ()

    if count > 0 then
      struct (sumC / count, sumR / count)
    else
      struct (grid.Width / 2, grid.Height / 2)

  let private enemiesOf
    (playerIndex: int)
    (units: Map<struct (int * int), SBUnit>)
    : (struct (int * int) * SBUnit)[] =
    units
    |> Map.toArray
    |> Array.filter(fun (_, u) -> u.PlayerIndex <> playerIndex)

  let private alliesOf
    (playerIndex: int)
    (units: Map<struct (int * int), SBUnit>)
    : (struct (int * int) * SBUnit)[] =
    units
    |> Map.toArray
    |> Array.filter(fun (_, u) -> u.PlayerIndex = playerIndex)

  let private visibleEnemies
    (playerIndex: int)
    (units: Map<struct (int * int), SBUnit>)
    (visible: Set<struct (int * int)>)
    : (struct (int * int) * SBUnit)[] =
    enemiesOf playerIndex units
    |> Array.filter(fun (cell, _) -> visible.Contains cell)

  let private enemiesInAttackRange
    (unit: SBUnit)
    (unitCell: struct (int * int))
    (enemies: (struct (int * int) * SBUnit)[])
    (grid: HexGrid<Tile>)
    : (struct (int * int) * SBUnit)[] =
    let attackCells =
      Selection.computeAttackRange
        (let struct (c, r) = unitCell in c)
        (let struct (c, r) = unitCell in r)
        unit.AttackRange
        grid

    enemies |> Array.filter(fun (cell, _) -> attackCells.Contains cell)

  let private closestEnemy
    (unitCell: struct (int * int))
    (enemies: (struct (int * int) * SBUnit)[])
    (grid: HexGrid<Tile>)
    : (struct (int * int) * SBUnit) voption =
    if enemies.Length = 0 then
      ValueNone
    else
      enemies
      |> Array.minBy(fun (cell, _) -> hexDist unitCell cell grid)
      |> ValueSome

  let private chooseAttackTarget
    (enemies: (struct (int * int) * SBUnit)[])
    : struct (int * int) =
    let cell, _ = enemies |> Array.minBy(fun (_, u) -> u.HP)
    cell

  let private bestMoveToward
    (unit: SBUnit)
    (unitCell: struct (int * int))
    (targetCell: struct (int * int))
    (grid: HexGrid<Tile>)
    (units: Map<struct (int * int), SBUnit>)
    (playerIndex: int)
    : struct (int * int) voption =
    let struct (sc, sr) = unitCell
    let struct (tc, tr) = targetCell

    let reachable =
      Selection.computeMoveRange sc sr unit.MoveRange grid units playerIndex

    if reachable.Count = 0 then
      ValueNone
    else
      let struct (bestCell, _) =
        reachable
        |> Seq.map(fun cell -> struct (cell, hexDist cell targetCell grid))
        |> Seq.minBy(fun struct (_, d) -> d)

      ValueSome bestCell

  let private bestMoveAway
    (unit: SBUnit)
    (unitCell: struct (int * int))
    (threatCell: struct (int * int))
    (grid: HexGrid<Tile>)
    (units: Map<struct (int * int), SBUnit>)
    (playerIndex: int)
    (allies: (struct (int * int) * SBUnit)[])
    : struct (int * int) voption =
    let struct (sc, sr) = unitCell

    let reachable =
      Selection.computeMoveRange sc sr unit.MoveRange grid units playerIndex

    if reachable.Count = 0 then
      ValueNone
    else
      let allyCenter =
        if allies.Length > 0 then
          let mutable sumC = 0
          let mutable sumR = 0

          for struct (ac, ar), _ in allies do
            sumC <- sumC + ac
            sumR <- sumR + ar

          struct (sumC / allies.Length, sumR / allies.Length)
        else
          unitCell

      reachable
      |> Seq.map(fun cell ->
        let threatDist = hexDist cell threatCell grid
        let allyDist = hexDist cell allyCenter grid
        struct (cell, threatDist - allyDist * 0.5f))
      |> Seq.maxBy(fun struct (_, score) -> score)
      |> fun struct (cell, _) -> ValueSome cell

  let private patrolTarget
    (unitCell: struct (int * int))
    (grid: HexGrid<Tile>)
    (knownEnemyPositions: Set<struct (int * int)>)
    (playerIndex: int)
    (turnNumber: int)
    : struct (int * int) =
    let center = findCenter grid
    let struct (uc, ur) = unitCell
    let struct (cc, cr) = center
    let distToCenter = float32(Hex2DSpatial.distance uc ur cc cr grid)

    if knownEnemyPositions.Count > 0 then
      knownEnemyPositions |> Seq.minBy(fun cell -> hexDist unitCell cell grid)
    elif distToCenter > 3.0f then
      center
    else
      let corners = [|
        struct (0, 0)
        struct (grid.Width - 1, 0)
        struct (grid.Width - 1, grid.Height - 1)
        struct (0, grid.Height - 1)
      |]

      let idx = abs(turnNumber + playerIndex) % corners.Length
      let target = corners[idx]
      let struct (tc, tr) = target

      match HexGrid.get tc tr grid with
      | ValueSome _ -> target
      | ValueNone -> center

  [<Struct>]
  type ClassWeights = {
    Health: float32
    Threat: float32
    Target: float32
    Support: float32
  }

  let private classWeights =
    function
    | Fighter -> {
        Health = 0.2f
        Threat = 0.6f
        Target = 0.8f
        Support = -0.1f
      }
    | Cruiser -> {
        Health = 0.3f
        Threat = 0.3f
        Target = 0.5f
        Support = 0.3f
      }
    | Battleship ->
        {
          Health = 0.5f
          Threat = -0.4f
          Target = 0.3f
          Support = 0.6f
        }

  let private scoreAction
    (weights: ClassWeights)
    (hp: float32)
    (maxHP: float32)
    (enemyCount: int)
    (allyCount: int)
    (hasTarget: bool)
    : float32 =
    let healthFactor = hp / maxHP
    let threatFactor = float32(min enemyCount 3) / 3.0f
    let targetFactor = if hasTarget then 1.0f else 0.0f
    let supportFactor = float32(min allyCount 3) / 3.0f

    weights.Health * healthFactor
    + weights.Threat * threatFactor
    + weights.Target * targetFactor
    + weights.Support * supportFactor

  let evaluate
    (unit: SBUnit)
    (unitCell: struct (int * int))
    (units: Map<struct (int * int), SBUnit>)
    (visible: Set<struct (int * int)>)
    (grid: HexGrid<Tile>)
    (turn: Turn)
    : AIAction =
    let weights = classWeights unit.Class
    let visEnemies = visibleEnemies unit.PlayerIndex units visible
    let allies = alliesOf unit.PlayerIndex units
    let inRange = enemiesInAttackRange unit unitCell visEnemies grid
    let closest = closestEnemy unitCell visEnemies grid
    let hp = float32 unit.HP
    let maxHP = float32 unit.MaxHP

    let canStillMove = not(hasMoved unit.id turn)
    let canStillAct = not(hasActed unit.id turn)

    let decide() =
      if inRange.Length > 0 && canStillAct then
        let targetCell = chooseAttackTarget inRange

        if canStillMove then
          let score =
            scoreAction weights hp maxHP visEnemies.Length allies.Length true

          if score > 0.3f then
            AttackOnly(unitCell, targetCell)
          else
            match closest with
            | ValueSome(closestCell, _) ->
              match
                bestMoveAway
                  unit
                  unitCell
                  closestCell
                  grid
                  units
                  unit.PlayerIndex
                  allies
              with
              | ValueSome newCell ->
                MoveAndAttack(unitCell, newCell, targetCell)
              | ValueNone -> AttackOnly(unitCell, targetCell)
            | ValueNone -> AttackOnly(unitCell, targetCell)
        else
          AttackOnly(unitCell, targetCell)
      elif visEnemies.Length > 0 && canStillMove then
        match closest with
        | ValueSome(closestCell, _) ->
          let score =
            scoreAction weights hp maxHP visEnemies.Length allies.Length false

          if score > 0.0f then
            match
              bestMoveToward
                unit
                unitCell
                closestCell
                grid
                units
                unit.PlayerIndex
            with
            | ValueSome newCell ->
              let newAttackCells =
                enemiesInAttackRange unit newCell visEnemies grid

              if newAttackCells.Length > 0 && canStillAct then
                let targetCell = chooseAttackTarget newAttackCells
                MoveAndAttack(unitCell, newCell, targetCell)
              else
                MoveOnly(unitCell, newCell)
            | ValueNone -> NoAction
          else
            match
              bestMoveAway
                unit
                unitCell
                closestCell
                grid
                units
                unit.PlayerIndex
                allies
            with
            | ValueSome newCell -> MoveOnly(unitCell, newCell)
            | ValueNone -> NoAction
        | ValueNone -> NoAction
      elif canStillMove then
        let patrol =
          patrolTarget unitCell grid Set.empty unit.PlayerIndex turn.TurnNumber

        match
          bestMoveToward unit unitCell patrol grid units unit.PlayerIndex
        with
        | ValueSome newCell -> MoveOnly(unitCell, newCell)
        | ValueNone -> NoAction
      else
        NoAction

    decide()

  let computeVisible
    (unit: SBUnit)
    (unitCell: struct (int * int))
    (grid: HexGrid<Tile>)
    : Set<struct (int * int)> =
    let struct (c, r) = unitCell

    Hex2DSpatial.inRange c r unit.VisualRange grid
    |> Array.filter(fun cell ->
      match
        HexGrid.get
          (let struct (c, _) = cell in c)
          (let struct (_, r) = cell in r)
          grid
      with
      | ValueSome _ -> true
      | ValueNone -> false)
    |> Set.ofArray

  let evaluateNextAction
    (units: Map<struct (int * int), SBUnit>)
    (turn: Turn)
    (grid: HexGrid<Tile>)
    : struct (int * int) voption * PhaseMsg * PhaseMsg =
    let playerIndex = turn.CurrentPlayerIndex

    let aiUnits =
      units
      |> Map.toArray
      |> Array.filter(fun (_, u: SBUnit) -> u.PlayerIndex = playerIndex)

    let mutable result: (struct (int * int) * PhaseMsg * PhaseMsg) voption =
      ValueNone

    for cell, unit in aiUnits do
      if result.IsNone then
        let visible = computeVisible unit cell grid
        let action = evaluate unit cell units visible grid turn

        match action with
        | AttackOnly(_, target) ->
          result <-
            ValueSome(
              cell,
              PhaseMsg.CellClicked cell,
              PhaseMsg.CellClicked target
            )
        | MoveAndAttack(_, moveTo, _) ->
          result <-
            ValueSome(
              cell,
              PhaseMsg.CellClicked cell,
              PhaseMsg.CellClicked moveTo
            )
        | MoveOnly(_, moveTo) ->
          result <-
            ValueSome(
              cell,
              PhaseMsg.CellClicked cell,
              PhaseMsg.CellClicked moveTo
            )
        | NoAction -> ()

    match result with
    | ValueSome(unitCell, select, action) -> ValueSome unitCell, select, action
    | ValueNone -> ValueNone, PhaseMsg.EndTurn, PhaseMsg.EndTurn
