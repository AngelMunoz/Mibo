namespace SpaceBattle

open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Raylib_cs
open SpaceBattle.Types
open SpaceBattle.Units
open SpaceBattle.Phase
open SpaceBattle.Input

[<Struct>]
type PlayerSlot = {
  Faction: Faction
  Control: UnitControl
  Enabled: bool
}

[<Struct>]
type PreStartState = {
  Slots: PlayerSlot[]
  SelectedSlot: int
  SelectedField: int
}

[<Struct>]
type PreStartMsg =
  | ToggleSlot of slot: int
  | CycleFaction of slot: int
  | CycleControl of slot: int
  | SelectSlot of slot: int
  | SelectField of field: int
  | NavigateSlot of delta: int
  | NavigateField of delta: int
  | StartGame

module PreStart =

  let private allFactions = [| Federation; Empire; Pirates |]

  let private factionName =
    function
    | Federation -> "Federation"
    | Empire -> "Empire"
    | Pirates -> "Pirates"

  let private factionColor =
    function
    | Federation -> Color(80uy, 140uy, 255uy, 255uy)
    | Empire -> Color(255uy, 80uy, 80uy, 255uy)
    | Pirates -> Color(255uy, 180uy, 60uy, 255uy)

  let private controlName =
    function
    | Units.Human -> "HUMAN"
    | Units.AI -> "AI"

  let private controlColor =
    function
    | Units.Human -> Color(100uy, 255uy, 100uy, 255uy)
    | Units.AI -> Color(255uy, 200uy, 100uy, 255uy)

  let init() : PreStartState = {
    Slots = [|
      {
        Faction = Pirates
        Control = Units.Human
        Enabled = true
      }
      {
        Faction = Federation
        Control = Units.Human
        Enabled = true
      }
      {
        Faction = Empire
        Control = Units.Human
        Enabled = false
      }
      {
        Faction = Pirates
        Control = Units.Human
        Enabled = false
      }
    |]
    SelectedSlot = 0
    SelectedField = 0
  }

  let getMapSize(enabledCount: int) : struct (int * int) =
    match enabledCount with
    | 1 -> struct (10, 10)
    | 2 -> struct (12, 12)
    | 3 -> struct (14, 14)
    | _ -> struct (16, 16)

  let private nextFaction(current: Faction) : Faction =
    let idx = allFactions |> Array.findIndex(fun f -> f = current)

    allFactions[(idx + 1) % allFactions.Length]

  let private nextControl(current: UnitControl) : UnitControl =
    match current with
    | Human -> AI
    | AI -> Human

  let private updateSlot
    (slots: PlayerSlot[])
    (index: int)
    (f: PlayerSlot -> PlayerSlot)
    : PlayerSlot[] =
    let newSlots = Array.copy slots
    newSlots[index] <- f newSlots[index]
    newSlots

  let update (msg: PreStartMsg) (state: PreStartState) : PreStartState =
    match msg with
    | ToggleSlot slot ->
      let newSlots =
        updateSlot state.Slots slot (fun s -> {
          s with
              Enabled = not s.Enabled
        })

      { state with Slots = newSlots }
    | CycleFaction slot ->
      let newSlots =
        updateSlot state.Slots slot (fun s -> {
          s with
              Faction = nextFaction s.Faction
        })

      { state with Slots = newSlots }
    | CycleControl slot ->
      let newSlots =
        updateSlot state.Slots slot (fun s -> {
          s with
              Control = nextControl s.Control
        })

      { state with Slots = newSlots }
    | SelectSlot slot -> { state with SelectedSlot = slot }
    | SelectField field -> { state with SelectedField = field }
    | NavigateSlot delta ->
      let count = state.Slots.Length

      {
        state with
            SelectedSlot = (state.SelectedSlot + delta + count) % count
      }
    | NavigateField delta -> {
        state with
            SelectedField = (state.SelectedField + delta + 3) % 3
      }
    | StartGame -> state

  let private cornerPositions
    (w: int)
    (h: int)
    (index: int)
    : struct (int * int)[] =
    match index with
    | 0 -> [| struct (0, 0); struct (1, 0); struct (0, 1) |]
    | 1 -> [|
        struct (w - 1, h - 1)
        struct (w - 1, h - 2)
        struct (w - 2, h - 1)
      |]
    | 2 -> [| struct (w - 1, 0); struct (w - 2, 0); struct (w - 1, 1) |]
    | _ -> [| struct (0, h - 1); struct (1, h - 1); struct (0, h - 2) |]

  let private directionForCorner(index: int) : Direction =
    match index with
    | 0 -> SE
    | 1 -> NW
    | 2 -> SW
    | _ -> NE

  let private unitClasses = [| Fighter; Cruiser; Battleship |]

  let createUnits
    (state: PreStartState)
    (w: int)
    (h: int)
    : Map<struct (int * int), SBUnit> =
    let mutable units = Map.empty
    let mutable id = 1

    let mutable enabledIndex = 0

    for i in 0 .. state.Slots.Length - 1 do
      let slot = state.Slots[i]

      if slot.Enabled then
        let positions = cornerPositions w h i
        let dir = directionForCorner i

        let unitControl = slot.Control

        for j in 0..2 do
          let struct (col, row) = positions[j]

          let unitClass = unitClasses[j]

          let hp, defense, moveRange, attackRange, visualRange =
            match unitClass with
            | Fighter -> 20, 15, 8, 2, 2
            | Cruiser -> 20, 20, 5, 3, 3
            | Battleship -> 35, 30, 3, 4, 7

          let unit = {
            id = id * 1<UnitId>
            PlayerIndex = i
            Control = unitControl
            Faction = slot.Faction
            Class = unitClass
            Direction = dir
            HP = hp
            MaxHP = hp
            Defense = defense
            MoveRange = moveRange
            AttackRange = attackRange
            VisualRange = visualRange
          }

          units <- units |> Map.add struct (col, row) unit
          id <- id + 1

        enabledIndex <- enabledIndex + 1

    units

  let createTurnOrder(state: PreStartState) : TurnOrder =
    let enabledSlots =
      state.Slots
      |> Array.mapi(fun i s -> (i, s))
      |> Array.filter(fun (_, s) -> s.Enabled)

    let factions = enabledSlots |> Array.map(fun (_, s) -> s.Faction)

    let playerIndices = enabledSlots |> Array.map(fun (i, _) -> i)

    let playerControls = enabledSlots |> Array.map(fun (_, s) -> s.Control)

    Phase.createTurnOrder factions playerIndices playerControls

  let handleInput
    (inputMsg: InputMsg)
    (state: PreStartState)
    (vpWidth: float32)
    : PreStartMsg voption =
    match inputMsg with
    | InputChanged inputs ->
      let mutable result = ValueNone

      if inputs.Started.Contains MoveUp then
        result <- ValueSome(NavigateSlot -1)
      elif inputs.Started.Contains MoveDown then
        result <- ValueSome(NavigateSlot 1)
      elif inputs.Started.Contains MoveLeft then
        result <- ValueSome(NavigateField -1)
      elif inputs.Started.Contains MoveRight then
        result <- ValueSome(NavigateField 1)
      elif inputs.Started.Contains GameAction.EndTurn then
        let slot = state.Slots[state.SelectedSlot]

        match state.SelectedField with
        | 0 -> result <- ValueSome(ToggleSlot state.SelectedSlot)
        | 1 when slot.Enabled ->
          result <- ValueSome(CycleFaction state.SelectedSlot)
        | 2 when slot.Enabled ->
          result <- ValueSome(CycleControl state.SelectedSlot)
        | _ -> ()

      result
    | MouseAction(Select _) ->
      let pos = Raylib.GetMousePosition()
      let cx = vpWidth / 2.0f
      let slotsStartY = 160.0f
      let buttonY = 480.0f
      let slotHeight = 60.0f

      let mutable result = ValueNone

      for i in 0 .. state.Slots.Length - 1 do
        let y = slotsStartY + float32 i * slotHeight

        if
          pos.Y >= y
          && pos.Y <= y + slotHeight - 8.0f
          && pos.X >= cx - 250.0f
          && pos.X <= cx + 250.0f
        then
          result <- ValueSome(SelectSlot i)

          if pos.X >= cx - 180.0f && pos.X <= cx - 120.0f then
            result <- ValueSome(ToggleSlot i)
          elif pos.X >= cx - 80.0f && pos.X <= cx + 80.0f then
            let slot = state.Slots[i]

            if slot.Enabled then
              result <- ValueSome(CycleFaction i)
          elif pos.X >= cx + 100.0f && pos.X <= cx + 180.0f then
            let slot = state.Slots[i]

            if slot.Enabled then
              result <- ValueSome(CycleControl i)

      let enabledCount =
        state.Slots |> Array.filter(fun s -> s.Enabled) |> Array.length

      if enabledCount >= 2 && pos.Y >= buttonY && pos.Y <= buttonY + 40.0f then
        result <- ValueSome StartGame

      result
    | _ -> ValueNone

  let private slotHeight = 60.0f
  let private fieldWidth = 180.0f
  let private titleY = 80.0f
  let private slotsStartY = 160.0f
  let private buttonY = 480.0f

  let view
    (state: PreStartState)
    (font: Font)
    (vpWidth: float32)
    (vpHeight: float32)
    (buffer: RenderBuffer2D)
    =
    let cx = vpWidth / 2.0f

    buffer
    |> Draw.text(
      TextState.create(font, "Space Battle", Vector2(cx - 120.0f, titleY))
      |> TextState.withFontSize 48.0f
      |> TextState.withSpacing 2.0f
      |> TextState.withColor Color.White
    )
    |> Draw.drop

    buffer
    |> Draw.text(
      TextState.create(
        font,
        "Configure Players",
        Vector2(cx - 100.0f, titleY + 50.0f)
      )
      |> TextState.withFontSize 24.0f
      |> TextState.withSpacing 1.0f
      |> TextState.withColor Color.Gray
    )
    |> Draw.drop

    for i in 0 .. state.Slots.Length - 1 do
      let slot = state.Slots[i]
      let y = slotsStartY + float32 i * slotHeight
      let isSelected = i = state.SelectedSlot

      let bgColor =
        if isSelected then
          Color(50uy, 50uy, 70uy, 200uy)
        else
          Color(30uy, 30uy, 40uy, 150uy)

      buffer
      |> Draw.fillRect
        (0<RenderLayer>, bgColor)
        (Rectangle(cx - 250.0f, y, 500.0f, slotHeight - 8.0f))
      |> Draw.drop

      buffer
      |> Draw.text(
        TextState.create(font, $"P{i + 1}", Vector2(cx - 240.0f, y + 16.0f))
        |> TextState.withFontSize 20.0f
        |> TextState.withSpacing 1.0f
        |> TextState.withColor Color.White
      )
      |> Draw.drop

      let enabledText = if slot.Enabled then "ON" else "OFF"

      let enabledColor =
        if slot.Enabled then
          Color(100uy, 255uy, 100uy, 255uy)
        else
          Color(150uy, 150uy, 150uy, 255uy)

      let enabledX = cx - 180.0f
      let isField0Selected = isSelected && state.SelectedField = 0

      if isField0Selected then
        buffer
        |> Draw.fillRect
          (0<RenderLayer>, Color(80uy, 80uy, 120uy, 255uy))
          (Rectangle(enabledX - 4.0f, y + 10.0f, 60.0f, 30.0f))
        |> Draw.drop

      buffer
      |> Draw.text(
        TextState.create(font, enabledText, Vector2(enabledX, y + 16.0f))
        |> TextState.withFontSize 20.0f
        |> TextState.withSpacing 1.0f
        |> TextState.withColor enabledColor
      )
      |> Draw.drop

      let factionX = cx - 80.0f
      let isField1Selected = isSelected && state.SelectedField = 1

      if isField1Selected then
        buffer
        |> Draw.fillRect
          (0<RenderLayer>, Color(80uy, 80uy, 120uy, 255uy))
          (Rectangle(factionX - 4.0f, y + 10.0f, 160.0f, 30.0f))
        |> Draw.drop

      if slot.Enabled then
        buffer
        |> Draw.text(
          TextState.create(
            font,
            factionName slot.Faction,
            Vector2(factionX, y + 16.0f)
          )
          |> TextState.withFontSize 20.0f
          |> TextState.withSpacing 1.0f
          |> TextState.withColor(factionColor slot.Faction)
        )
        |> Draw.drop

      let controlX = cx + 100.0f
      let isField2Selected = isSelected && state.SelectedField = 2

      if isField2Selected then
        buffer
        |> Draw.fillRect
          (0<RenderLayer>, Color(80uy, 80uy, 120uy, 255uy))
          (Rectangle(controlX - 4.0f, y + 10.0f, 80.0f, 30.0f))
        |> Draw.drop

      if slot.Enabled then
        buffer
        |> Draw.text(
          TextState.create(
            font,
            controlName slot.Control,
            Vector2(controlX, y + 16.0f)
          )
          |> TextState.withFontSize 20.0f
          |> TextState.withSpacing 1.0f
          |> TextState.withColor(controlColor slot.Control)
        )
        |> Draw.drop

    let enabledCount =
      state.Slots |> Array.filter(fun s -> s.Enabled) |> Array.length

    let canStart = enabledCount >= 2
    let startColor = if canStart then Color.White else Color.Gray

    let startText =
      if canStart then
        $"Start Game ({enabledCount} Players)"
      else
        "Need at least 2 players"

    let textWidth = Raylib.MeasureTextEx(font, startText, 28.0f, 1.0f).X

    buffer
    |> Draw.text(
      TextState.create(font, startText, Vector2(cx - textWidth / 2.0f, buttonY))
      |> TextState.withFontSize 28.0f
      |> TextState.withSpacing 1.0f
      |> TextState.withColor startColor
    )
    |> Draw.drop

    buffer
    |> Draw.text(
      TextState.create(
        font,
        "Arrows: Navigate  |  Enter: Select/Toggle  |  Tab: Start",
        Vector2(cx - 200.0f, vpHeight - 40.0f)
      )
      |> TextState.withFontSize 16.0f
      |> TextState.withSpacing 1.0f
      |> TextState.withColor Color.DarkGray
    )
    |> Draw.drop

    buffer
