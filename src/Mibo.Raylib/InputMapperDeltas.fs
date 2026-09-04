namespace Mibo.Input

open System
open Raylib_cs
open Mibo.Elmish

// Trigger, InputMap<'Action>, ActionState<'Action>, IInputMapper<'Action>, and
// the InputMapper service accessors (getService/tryGetService) all live in Core
// now. This module holds the raylib-specific factory + polling logic that
// evaluates whether a Core Trigger is currently held, by translating to the
// native raylib key/button and calling Raylib.IsKeyDown etc.
//
// It is runtime-neutral (no MVU or adaptive types), so both the MVU host
// (Mibo.Raylib.Mvu) and the adaptive host (Mibo.Raylib.Adaptive) attach it to
// their input pipelines; the module is internal and exposed to those two host
// packages via InternalsVisibleTo.

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
      let state, newComboStates =
        buildActions
          getMap
          prevComboStates
          prevHeldActions
          pressed
          released
          (fun k -> Raylib.IsKeyDown(KeyCode.toRaylibKey k).AsBool())
          (fun b ->
            Raylib.IsMouseButtonDown(MouseButtonCode.toRaylibButton b).AsBool())
          (fun p b ->
            Raylib
              .IsGamepadButtonDown(p, GamepadButtonCode.toRaylibButton b)
              .AsBool())

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
