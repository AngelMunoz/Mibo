namespace Mibo.Input

open System
open Microsoft.Xna.Framework.Input
open Mibo.Elmish

// Trigger, InputMap<'Action>, ActionState<'Action>, IInputMapper<'Action>, and
// the InputMapper service accessors (getService/tryGetService) all live in Core.
// This module holds the MonoGame-specific factory + polling logic that
// evaluates whether a Core Trigger is currently held, by translating to the
// native MonoGame key/button and reading Keyboard/Mouse/GamePad state.
//
// It is runtime-neutral (no MVU or adaptive types), so both the MVU host
// (Mibo.MonoGame.Mvu) and the adaptive host (Mibo.MonoGame.Adaptive) attach it
// to their input pipelines; the module is internal and exposed to those two
// host packages via InternalsVisibleTo.

[<AutoOpen>]
module internal InputMapperDeltas =

  let internal buildActions
    (getMap: unit -> InputMap<'Action>)
    (prevComboStates: Map<Set<KeyCode>, bool>)
    (prevHeldActions: Set<'Action>)
    (pressed: Trigger[])
    (released: Trigger[])
    (isKeyDown: KeyCode -> bool)
    (isMouseButtonDown: MouseButtonCode -> bool)
    (isGamepadButtonDown: int -> GamepadButtonCode -> bool)
    : ActionState<'Action> * Map<Set<KeyCode>, bool> =
    let map = getMap()
    let mutable started = Set.empty
    let mutable releasedSet = Set.empty
    let mutable held = Set.empty
    let mutable heldTriggers = Set.empty
    let mutable values = Map.empty
    let mutable comboStates = prevComboStates

    // Started is a TRANSITION, matching core ActionState.update: it
    // fires only when the action was NOT already held. Pressing a
    // synonym binding (Left while A already holds the action) must not
    // re-fire Started — Released only fires when the action FULLY
    // releases, so a per-trigger Started unbalances edge-arithmetic
    // consumers (add on Started, subtract on Released): one add per
    // binding press against a single subtract at full release leaves
    // them stuck.
    let startIfNew(a: 'Action) =
      if not(prevHeldActions.Contains a) then
        started <- started |> Set.add a

    for kv in map.TriggerToActions do
      let isDown =
        match kv.Key with
        | Key k -> isKeyDown k
        | KeyCombo keys -> keys |> Set.forall isKeyDown
        | MouseButton b -> isMouseButtonDown b
        | GamepadButton(p, b) -> isGamepadButtonDown p b

      if isDown then
        heldTriggers <- heldTriggers |> Set.add kv.Key

        for a in kv.Value do
          held <- held |> Set.add a
          values <- values |> Map.add a 1.0f

      match kv.Key with
      | KeyCombo keys ->
        let wasHeld =
          comboStates |> Map.tryFind keys |> Option.defaultValue false

        comboStates <- comboStates |> Map.add keys isDown

        if isDown && not wasHeld then
          for a in kv.Value do
            startIfNew a
        elif not isDown && wasHeld then
          for a in kv.Value do
            releasedSet <- releasedSet |> Set.add a
      | _ -> ()

    for t in pressed do
      map.TriggerToActions
      |> Map.tryFind t
      |> Option.iter(fun actions ->
        for a in actions do
          startIfNew a)

    for t in released do
      map.TriggerToActions
      |> Map.tryFind t
      |> Option.iter(fun actions ->
        for a in actions do
          releasedSet <- releasedSet |> Set.add a)

    releasedSet <- releasedSet |> Set.filter(fun a -> not(held.Contains a))

    ({
      Held = held
      Started = started
      Released = releasedSet
      Values = values
      HeldTriggers = heldTriggers
     },
     comboStates)

  // MonoGame whole-state pollers for the "is this held right now?" queries.
  // MonoGame gives whole-state snapshots, so these read Keyboard/Mouse/GamePad
  // state on demand. Used by both attachDeltas (per-event) and the poll-driven
  // createService (per-Update).
  let internal isKeyDownFor (kb: KeyboardState) (k: KeyCode) : bool =
    kb.IsKeyDown(KeyCode.toMonoGameKey k)

  let internal isMouseButtonDownFor
    (ms: MouseState)
    (b: MouseButtonCode)
    : bool =
    match b with
    | MouseButtonCode.Left -> ms.LeftButton = ButtonState.Pressed
    | MouseButtonCode.Right -> ms.RightButton = ButtonState.Pressed
    | MouseButtonCode.Middle -> ms.MiddleButton = ButtonState.Pressed
    | MouseButtonCode.Extra1 -> ms.XButton1 = ButtonState.Pressed
    | MouseButtonCode.Extra2 -> ms.XButton2 = ButtonState.Pressed
    | _ -> false

  let internal isGamepadButtonDownFor
    (gp: GamePadState)
    (playerIndex: int)
    (b: GamepadButtonCode)
    : bool =
    // playerIndex is carried for API parity with the Trigger DU (which is
    // player-tagged); the caller has already routed to this player's state,
    // so no cross-check is needed (and GamePadState has no PlayerIndex field).
    if not gp.IsConnected then
      false
    else
      let btn = GamepadButtonCode.toMonoGameButton b

      match btn with
      | Buttons.DPadUp -> gp.DPad.Up = ButtonState.Pressed
      | Buttons.DPadRight -> gp.DPad.Right = ButtonState.Pressed
      | Buttons.DPadDown -> gp.DPad.Down = ButtonState.Pressed
      | Buttons.DPadLeft -> gp.DPad.Left = ButtonState.Pressed
      | Buttons.LeftTrigger -> gp.Triggers.Left >= 0.5f
      | Buttons.RightTrigger -> gp.Triggers.Right >= 0.5f
      | _ -> gp.IsButtonDown(btn)

  /// Attaches the three delta subscriptions; every keyboard/mouse/gamepad
  /// delta builds a fresh <see cref="T:Mibo.Input.ActionState`1"/> (event time,
  /// owner thread — during the host's input poll) and hands it to
  /// <paramref name="emit"/>. Shared by <see cref="M:Mibo.Input.InputMapper.subscribe"/>
  /// (emits a message) and <see cref="M:Mibo.Input.InputMapper.subscribeAdaptive"/>
  /// (emits a deferred root write) — the emit step is the only difference.
  let internal attachDeltas
    (getMap: unit -> InputMap<'Action>)
    (ctx: GameContext)
    (emit: ActionState<'Action> -> unit)
    : IDisposable =
    let input = Input.getService ctx
    let mutable prevComboStates = Map.empty<Set<KeyCode>, bool>
    let mutable prevHeldActions = Set.empty<'Action>

    let doBuild (pressed: Trigger[]) (released: Trigger[]) =
      // Map-aware snapshot: only fetch the devices the map's triggers
      // actually reference. A moving mouse fires a delta every frame,
      // so a keyboard-only map must not pay mouse + four gamepad polls
      // per event (the pollers are consulted only for mapped triggers,
      // so skipping the fetch is behavior-identical). Unfetched
      // devices leave default states whose pollers answer false —
      // and are never asked anyway.
      let map = getMap()

      let mutable useKeyboard = false
      let mutable useMouse = false
      let mutable useGamepad = false

      for KeyValue(trigger, _) in map.TriggerToActions do
        match trigger with
        | Key _
        | KeyCombo _ -> useKeyboard <- true
        | MouseButton _ -> useMouse <- true
        | GamepadButton _ -> useGamepad <- true

      let kb =
        if useKeyboard then
          Keyboard.GetState()
        else
          Unchecked.defaultof<KeyboardState>

      let ms =
        if useMouse then
          Mouse.GetState()
        else
          Unchecked.defaultof<MouseState>

      let g0 =
        if useGamepad then
          GamePad.GetState(0)
        else
          Unchecked.defaultof<GamePadState>

      let g1 =
        if useGamepad then
          GamePad.GetState(1)
        else
          Unchecked.defaultof<GamePadState>

      let g2 =
        if useGamepad then
          GamePad.GetState(2)
        else
          Unchecked.defaultof<GamePadState>

      let g3 =
        if useGamepad then
          GamePad.GetState(3)
        else
          Unchecked.defaultof<GamePadState>

      let isGpDown (p: int) (b: GamepadButtonCode) =
        match p with
        | 0 -> isGamepadButtonDownFor g0 0 b
        | 1 -> isGamepadButtonDownFor g1 1 b
        | 2 -> isGamepadButtonDownFor g2 2 b
        | _ -> isGamepadButtonDownFor g3 3 b

      let state, newComboStates =
        buildActions
          (fun () -> map)
          prevComboStates
          prevHeldActions
          pressed
          released
          (isKeyDownFor kb)
          (isMouseButtonDownFor ms)
          isGpDown

      prevComboStates <- newComboStates
      prevHeldActions <- state.Held
      state

    let subKey: IDisposable =
      input.KeyboardDelta.Subscribe(fun (d: KeyboardDelta) ->
        let pressed = d.Pressed |> Array.map Key
        let released = d.Released |> Array.map Key
        emit(doBuild pressed released))

    let subMouse: IDisposable =
      input.MouseDelta.Subscribe(fun (d: MouseDelta) ->
        let pressed = d.Buttons.Pressed |> Array.map Trigger.MouseButton
        let released = d.Buttons.Released |> Array.map Trigger.MouseButton
        emit(doBuild pressed released))

    let subGamepad: IDisposable =
      input.GamepadDelta.Subscribe(fun (d: GamepadDelta) ->
        let pressed =
          d.Buttons.Pressed
          |> Array.map(fun b -> Trigger.GamepadButton(d.PlayerIndex, b))

        let released =
          d.Buttons.Released
          |> Array.map(fun b -> Trigger.GamepadButton(d.PlayerIndex, b))

        emit(doBuild pressed released))

    { new IDisposable with
        member _.Dispose() =
          subKey.Dispose()
          subMouse.Dispose()
          subGamepad.Dispose()
    }
